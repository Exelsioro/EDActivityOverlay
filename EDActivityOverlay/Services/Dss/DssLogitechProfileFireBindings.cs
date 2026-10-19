using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Extends Elite's fire-action bindings with physical buttons from the selected
/// Logitech profile. The button is derived from the profile's output, never
/// assumed to be a particular X52 control.
/// </summary>
internal static class DssLogitechProfileFireBindings
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        | RegexOptions.Singleline;
    private const string GuidPattern = @"[0-9a-f-]{36}";

    internal static DssFireBindingSet AddProfileInputs(
        DssFireBindingSet bindings,
        bool enabled,
        string? profilePath)
    {
        if (!enabled || string.IsNullOrWhiteSpace(profilePath)
            || !File.Exists(profilePath))
            return bindings;

        try
        {
            return AddProfileInputs(bindings, File.ReadAllText(profilePath));
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS Logitech profile bindings unavailable: {ex.Message}");
            return bindings;
        }
    }

    internal static DssFireBindingSet AddProfileInputs(
        DssFireBindingSet bindings,
        string profile)
    {
        Match rootShift = Regex.Matches(profile,
                @"\[shift=" + GuidPattern
                + @"(?:\s+fallback=" + GuidPattern + @")?"
                + @"\s+name=(?:'[^']+'|[^\]\s]+)", Options)
            .Cast<Match>()
            .FirstOrDefault(match => !match.Value.Contains(
                "fallback=", StringComparison.OrdinalIgnoreCase))!;
        if (rootShift is null)
            return bindings;

        int nextShift = profile.IndexOf("[shift=",
            rootShift.Index + rootShift.Length,
            StringComparison.OrdinalIgnoreCase);
        string assignments = profile.Substring(rootShift.Index,
            (nextShift < 0 ? profile.Length : nextShift) - rootShift.Index);

        Match deviceName = Regex.Match(profile,
            @"\bshortname=(?:'(?<name>[^']+)'|(?<name>[^\]\s]+))", Options);
        string device = deviceName.Success
            ? deviceName.Groups["name"].Value : "X52Pro";

        var commandOutputs = new Dictionary<string, HashSet<(int Page, int Usage)>>(
            StringComparer.OrdinalIgnoreCase);
        MatchCollection commands = Regex.Matches(profile,
            @"\[actioncommand=(?<id>" + GuidPattern
            + @")(?<body>.*?)(?=\[actioncommand=|$)", Options);
        foreach (Match command in commands)
        {
            var outputs = new HashSet<(int Page, int Usage)>();
            foreach (Match output in Regex.Matches(command.Groups["body"].Value,
                @"\[action\s+device=(?<device>keyboard|mouse)\s+usage=0x(?<usage>[0-9a-f]+)\s+page=0x(?<page>[0-9a-f]+)", Options))
            {
                outputs.Add((
                    Convert.ToInt32(output.Groups["page"].Value, 16),
                    Convert.ToInt32(output.Groups["usage"].Value, 16)));
            }
            commandOutputs[command.Groups["id"].Value] = outputs;
        }

        var derived = new List<DssFireInputBinding>(bindings.Bindings);
        foreach (Match button in Regex.Matches(assignments,
            @"\[button=0x000900(?<button>[0-9a-f]{2})\s+role=bands(?<body>.*?)(?=\[button=|\[hat=|\[axis=|$)", Options))
        {
            int number = Convert.ToInt32(button.Groups["button"].Value, 16);
            if (number is < 1 or > 32)
                continue;

            Match band = Regex.Match(button.Groups["body"].Value,
                @"\[band=1\s+command=(?<id>" + GuidPattern + @")\]", Options);
            if (!band.Success || !commandOutputs.TryGetValue(
                    band.Groups["id"].Value, out var outputs))
                continue;

            foreach (DssFireInputBinding fire in bindings.Bindings)
            {
                (int Page, int Usage)? target = GetHidOutput(fire.Input);
                if (target is null || !outputs.Contains(target.Value))
                    continue;

                if (derived.Any(existing => existing.Action == fire.Action
                    && existing.Input.Kind == DssPhysicalInputKind.Joystick
                    && existing.Input.JoystickButton == number))
                    continue;

                derived.Add(new DssFireInputBinding(
                    fire.Action, $"Profile:{fire.Slot}",
                    new DssPhysicalInputToken(
                        DssPhysicalInputKind.Joystick, device,
                        $"Joy_{number}", 0, number), []));
            }
        }

        return bindings with { Bindings = derived };
    }

    internal static (int Page, int Usage)? GetHidOutput(
        DssPhysicalInputToken input)
    {
        if (input.Kind == DssPhysicalInputKind.Mouse)
            return input.VirtualKey switch
            {
                0x01 => (0x09, 1),
                0x02 => (0x09, 2),
                0x04 => (0x09, 3),
                0x05 => (0x09, 4),
                0x06 => (0x09, 5),
                _ => null
            };

        if (input.Kind != DssPhysicalInputKind.Keyboard)
            return null;

        int key = input.VirtualKey;
        if (key is >= 0x41 and <= 0x5A)
            return (0x07, key - 0x41 + 0x04);
        if (key is >= 0x31 and <= 0x39)
            return (0x07, key - 0x31 + 0x1E);
        if (key is >= 0x70 and <= 0x7B)
            return (0x07, key - 0x70 + 0x3A);

        return key switch
        {
            0x30 => (0x07, 0x27),
            0x0D => (0x07, 0x28),
            0x1B => (0x07, 0x29),
            0x08 => (0x07, 0x2A),
            0x09 => (0x07, 0x2B),
            0x20 => (0x07, 0x2C),
            _ => null
        };
    }
}
