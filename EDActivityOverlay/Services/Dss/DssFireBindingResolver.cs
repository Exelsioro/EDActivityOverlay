using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using EDActivityOverlay.Services.Navigation;

namespace EDActivityOverlay.Services.Dss;

internal enum DssPhysicalInputKind
{
    Keyboard,
    Mouse,
    Joystick
}

internal sealed record DssPhysicalInputToken(
    DssPhysicalInputKind Kind,
    string Device,
    string Key,
    ushort VirtualKey,
    int JoystickButton);

internal sealed record DssFireInputBinding(
    string Action,
    string Slot,
    DssPhysicalInputToken Input,
    IReadOnlyList<DssPhysicalInputToken> Modifiers)
{
    public string Identity =>
        $"{Action}:{Slot}:{Input.Device}:{Input.Key}";
}

internal sealed record DssFireBindingSet(
    string PresetName,
    string FilePath,
    IReadOnlyList<DssFireInputBinding> Bindings);

/// <summary>
/// Resolves the actual Elite fire inputs from the selected/active .binds file.
///
/// The project already has an Elite .binds parser for route automation. DSS
/// deliberately uses the same Settings file override / preset selection rather
/// than introducing another independent controls setting.
/// </summary>
internal static class DssFireBindingResolver
{
    private static readonly string[] FireActions =
    [
        "PrimaryFire",
        "SecondaryFire"
    ];

    public static DssFireBindingSet Resolve()
    {
        string path =
            ResolveBindingsFile();

        XElement root =
            XDocument.Load(path).Root
            ?? throw new InvalidDataException(
                "Elite bindings XML has no root element.");

        string preset =
            ((string?)root.Attribute("PresetName")
             ?? Path.GetFileNameWithoutExtension(path))
            .Trim();

        return Parse(
            root,
            path,
            preset);
    }

    internal static DssFireBindingSet Parse(
        XElement root,
        string filePath,
        string presetName)
    {
        EliteKeyboardLayout keyboardLayout =
            EliteBindingsService.DetectKeyboardLayout(
                root);

        var bindings =
            new List<DssFireInputBinding>();

        foreach (string action
                 in FireActions)
        {
            XElement? actionNode =
                root.Element(action);

            if (actionNode is null)
            {
                continue;
            }

            foreach (XElement slot
                     in actionNode.Elements())
            {
                string slotName =
                    slot.Name.LocalName;

                if (!slotName.Equals(
                        "Primary",
                        StringComparison.OrdinalIgnoreCase)
                    && !slotName.Equals(
                        "Secondary",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DssPhysicalInputToken? input =
                    ParseToken(
                        slot,
                        keyboardLayout);

                if (input is null)
                {
                    continue;
                }

                DssPhysicalInputToken[] modifiers =
                    slot.Elements("Modifier")
                        .Select(
                            element =>
                                ParseToken(
                                    element,
                                    keyboardLayout))
                        .Where(
                            token =>
                                token is not null)
                        .Cast<DssPhysicalInputToken>()
                        .ToArray();

                bindings.Add(
                    new DssFireInputBinding(
                        action,
                        slotName,
                        input,
                        modifiers));
            }
        }

        return new DssFireBindingSet(
            presetName,
            Path.GetFullPath(filePath),
            bindings);
    }

    private static DssPhysicalInputToken?
        ParseToken(
            XElement element,
            EliteKeyboardLayout keyboardLayout)
    {
        string device =
            ((string?)element.Attribute("Device")
             ?? string.Empty)
            .Trim();

        string key =
            ((string?)element.Attribute("Key")
             ?? string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(device)
            || device.Equals(
                "{NoDevice}",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (device.Equals(
                "Keyboard",
                StringComparison.OrdinalIgnoreCase))
        {
            EliteResolvedKey resolved =
                EliteBindingsService.ResolvePhysicalKey(
                    key,
                    keyboardLayout);

            return new DssPhysicalInputToken(
                DssPhysicalInputKind.Keyboard,
                device,
                key,
                resolved.VirtualKey,
                0);
        }

        if (device.Equals(
                "Mouse",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!TryMapMouseVirtualKey(
                    key,
                    out ushort virtualKey))
            {
                return null;
            }

            return new DssPhysicalInputToken(
                DssPhysicalInputKind.Mouse,
                device,
                key,
                virtualKey,
                0);
        }

        if (TryParseJoystickButton(
                key,
                out int button))
        {
            return new DssPhysicalInputToken(
                DssPhysicalInputKind.Joystick,
                device,
                key,
                0,
                button);
        }

        return null;
    }

    internal static bool TryParseJoystickButton(
        string key,
        out int button)
    {
        button = 0;

        if (!key.StartsWith(
                "Joy_",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
                   key[4..],
                   out button)
               && button is >= 1 and <= 32;
    }

    private static bool TryMapMouseVirtualKey(
        string key,
        out ushort virtualKey)
    {
        virtualKey =
            key.ToLowerInvariant() switch
            {
                "mouse_1" => 0x01,
                "mouse_2" => 0x02,
                "mouse_3" => 0x04,
                "mouse_4" => 0x05,
                "mouse_5" => 0x06,
                _ => 0
            };

        return virtualKey != 0;
    }

    private static string ResolveBindingsFile()
    {
        string explicitPath =
            SettingsService.Instance.Settings
                .EliteBindingsFilePath?
                .Trim()
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(
                explicitPath))
        {
            string fullPath =
                Path.GetFullPath(
                    explicitPath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Selected Elite bindings file was not found.",
                    fullPath);
            }

            return fullPath;
        }

        string directory =
            EliteBindingsService
                .DefaultBindingsDirectory;

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                directory);
        }

        string preset =
            SettingsService.Instance.Settings
                .EliteBindingsPreset?
                .Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(
                preset))
        {
            string startFile =
                Path.Combine(
                    directory,
                    "StartPreset.4.start");

            if (File.Exists(startFile))
            {
                preset =
                    File.ReadLines(startFile)
                        .Select(
                            line =>
                                line.Trim()
                                    .TrimStart('\uFEFF'))
                        .FirstOrDefault(
                            line =>
                                !string.IsNullOrWhiteSpace(
                                    line))
                    ?? string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(
                preset))
        {
            string? matching =
                Directory
                    .EnumerateFiles(
                        directory,
                        "*.binds")
                    .Select(
                        path =>
                            new
                            {
                                Path = path,
                                Root = TryLoad(path)
                            })
                    .Where(
                        item =>
                            item.Root is not null
                            && string.Equals(
                                (string?)item.Root
                                    .Attribute(
                                        "PresetName"),
                                preset,
                                StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(
                        item =>
                            File.GetLastWriteTimeUtc(
                                item.Path))
                    .Select(
                        item => item.Path)
                    .FirstOrDefault();

            if (matching is not null)
            {
                return matching;
            }
        }

        string? newest =
            Directory
                .EnumerateFiles(
                    directory,
                    "*.binds")
                .OrderByDescending(
                    File.GetLastWriteTimeUtc)
                .FirstOrDefault();

        return newest
            ?? throw new FileNotFoundException(
                "No Elite .binds file was found.",
                directory);
    }

    private static XElement? TryLoad(
        string path)
    {
        try
        {
            return XDocument.Load(path).Root;
        }
        catch
        {
            return null;
        }
    }
}
