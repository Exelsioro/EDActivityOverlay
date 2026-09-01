using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace EDActivityOverlay.Services.Hardware;

public enum X52StartupProfileStatus
{
    Ready,
    Active,
    ProfileMissing,
    ControllerMissing,
    ControllerAmbiguous,
    RestoreConflict,
    Error
}

public sealed record X52ProfileOption(string ProfilePath)
{
    public string Label => Path.GetFileName(ProfilePath);
}

public sealed record X52StartupProfileState(
    X52StartupProfileStatus Status,
    string ControllerId,
    string ProfilePath,
    string CurrentStartupPath,
    bool HasBackup,
    string Error)
{
    public static X52StartupProfileState Failure(
        X52StartupProfileStatus status,
        string error = "") =>
        new(status, string.Empty, string.Empty, string.Empty, false, error);
}

internal sealed record X52ControllerRegistryCandidate(
    string Id,
    RegistryView View,
    string Descriptor);

internal sealed record X52StartupProfileBackup(
    string ControllerId,
    string RegistryView,
    bool HadValue,
    string PreviousPath,
    string ConfiguredProfilePath,
    DateTimeOffset CreatedUtc);

/// <summary>
/// Configures the startup .pr0 profile consumed by Logitech's X52 profiler.
/// No Logitech UI automation and no game input are involved.
/// </summary>
public sealed class X52StartupProfileService
{
    private const string ControllersKey =
        @"SOFTWARE\Logitech\Configuration\Controllers";
    private const string StartupProfilesKey =
        @"Software\Logitech\Configuration\StartupProfiles";

    private readonly string backupPath;

    public static X52StartupProfileService Instance { get; } = new();

    private X52StartupProfileService()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EDActivityOverlay");
        Directory.CreateDirectory(appData);
        backupPath = Path.Combine(
            appData,
            "x52-startup-profile-backup.json");
    }

    public IReadOnlyList<X52ProfileOption> GetAvailableProfiles() =>
        FindProfiles()
            .Select(path => new X52ProfileOption(path))
            .ToArray();

    public X52StartupProfileState Inspect(
        string? preferredProfilePath = null)
    {
        try
        {
            IReadOnlyList<string> profiles = FindProfiles();

            IReadOnlyList<X52ControllerRegistryCandidate> candidates =
                FindControllerCandidates();
            X52ControllerRegistryCandidate? controller =
                SelectController(candidates);

            if (controller is null)
            {
                return X52StartupProfileState.Failure(
                    candidates.Count == 0
                        ? X52StartupProfileStatus.ControllerMissing
                        : X52StartupProfileStatus.ControllerAmbiguous);
            }

            string current =
                ReadStartupProfile(controller)
                ?? string.Empty;

            string? profile = ResolveProfile(
                profiles,
                preferredProfilePath,
                current);

            if (string.IsNullOrWhiteSpace(profile))
            {
                return new X52StartupProfileState(
                    X52StartupProfileStatus.ProfileMissing,
                    controller.Id,
                    string.Empty,
                    current,
                    File.Exists(backupPath),
                    string.Empty);
            }

            return new X52StartupProfileState(
                PathsEqual(current, profile)
                    ? X52StartupProfileStatus.Active
                    : X52StartupProfileStatus.Ready,
                controller.Id,
                profile,
                current,
                File.Exists(backupPath),
                string.Empty);
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"X52 startup profile inspection failed: {ex.Message}");
            return X52StartupProfileState.Failure(
                X52StartupProfileStatus.Error,
                ex.Message);
        }
    }

    public X52StartupProfileState Configure(
        string profilePath)
    {
        X52StartupProfileState state = Inspect(profilePath);
        if (state.Status == X52StartupProfileStatus.Active)
        {
            return state;
        }
        if (state.Status != X52StartupProfileStatus.Ready)
        {
            return state;
        }

        try
        {
            X52ControllerRegistryCandidate? controller =
                SelectController(FindControllerCandidates());
            if (controller is null)
            {
                return Inspect();
            }

            string? previous = ReadStartupProfile(controller);

            File.WriteAllText(
                backupPath,
                JsonSerializer.Serialize(
                    new X52StartupProfileBackup(
                        controller.Id,
                        controller.View.ToString(),
                        previous is not null,
                        previous ?? string.Empty,
                        state.ProfilePath,
                        DateTimeOffset.UtcNow),
                    new JsonSerializerOptions { WriteIndented = true }));

            using RegistryKey currentUser =
                RegistryKey.OpenBaseKey(
                    RegistryHive.CurrentUser,
                    controller.View);
            using RegistryKey startup =
                currentUser.CreateSubKey(
                    StartupProfilesKey,
                    writable: true)
                ?? throw new InvalidOperationException(
                    "Could not open Logitech StartupProfiles registry key.");

            startup.SetValue(
                controller.Id,
                state.ProfilePath,
                RegistryValueKind.String);

            Logger.Logger.Info(
                $"X52 startup profile configured: controller={controller.Id}, profile={state.ProfilePath}, view={controller.View}");

            return Inspect();
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"X52 startup profile configuration failed: {ex.Message}");
            return X52StartupProfileState.Failure(
                X52StartupProfileStatus.Error,
                ex.Message);
        }
    }

    public X52StartupProfileState RestorePrevious()
    {
        try
        {
            X52StartupProfileBackup? backup = LoadBackup();
            if (backup is null)
            {
                return Inspect();
            }

            if (!Enum.TryParse(
                    backup.RegistryView,
                    ignoreCase: true,
                    out RegistryView view))
            {
                view = RegistryView.Default;
            }

            var controller = new X52ControllerRegistryCandidate(
                backup.ControllerId,
                view,
                string.Empty);

            string? current = ReadStartupProfile(controller);
            if (!string.IsNullOrWhiteSpace(current)
                && !PathsEqual(current, backup.ConfiguredProfilePath))
            {
                X52StartupProfileState inspected = Inspect();
                return inspected with
                {
                    Status = X52StartupProfileStatus.RestoreConflict,
                    CurrentStartupPath = current,
                    HasBackup = true
                };
            }

            using RegistryKey currentUser =
                RegistryKey.OpenBaseKey(
                    RegistryHive.CurrentUser,
                    view);
            using RegistryKey startup =
                currentUser.CreateSubKey(
                    StartupProfilesKey,
                    writable: true)
                ?? throw new InvalidOperationException(
                    "Could not open Logitech StartupProfiles registry key.");

            if (backup.HadValue)
            {
                startup.SetValue(
                    backup.ControllerId,
                    backup.PreviousPath,
                    RegistryValueKind.String);
            }
            else
            {
                startup.DeleteValue(
                    backup.ControllerId,
                    throwOnMissingValue: false);
            }

            File.Delete(backupPath);

            Logger.Logger.Info(
                $"X52 startup profile restored: controller={backup.ControllerId}, hadPrevious={backup.HadValue}");

            return Inspect();
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"X52 startup profile restore failed: {ex.Message}");
            return X52StartupProfileState.Failure(
                X52StartupProfileStatus.Error,
                ex.Message);
        }
    }

    internal static X52ControllerRegistryCandidate? SelectController(
        IReadOnlyList<X52ControllerRegistryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        X52ControllerRegistryCandidate[] unique =
            candidates
                .GroupBy(
                    item => item.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderBy(item =>
                            item.View == RegistryView.Registry64 ? 0 : 1)
                        .First())
                .ToArray();

        if (unique.Length == 1)
        {
            return unique[0];
        }

        X52ControllerRegistryCandidate[] x52 =
            unique
                .Where(item =>
                    item.Descriptor.Contains(
                        "x52",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return x52.Length == 1 ? x52[0] : null;
    }

    internal static bool PathsEqual(
        string? left,
        string? right)
    {
        if (string.IsNullOrWhiteSpace(left)
            || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(
                left.Trim(),
                right.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private IReadOnlyList<X52ControllerRegistryCandidate>
        FindControllerCandidates()
    {
        var result = new List<X52ControllerRegistryCandidate>();

        foreach (RegistryView view in new[]
                 {
                     RegistryView.Registry64,
                     RegistryView.Registry32
                 })
        {
            try
            {
                using RegistryKey localMachine =
                    RegistryKey.OpenBaseKey(
                        RegistryHive.LocalMachine,
                        view);
                using RegistryKey? controllers =
                    localMachine.OpenSubKey(
                        ControllersKey,
                        writable: false);

                if (controllers is null)
                {
                    continue;
                }

                foreach (string id in controllers.GetSubKeyNames())
                {
                    if (!Guid.TryParse(
                            id.Trim('{', '}'),
                            out _))
                    {
                        continue;
                    }

                    using RegistryKey? controller =
                        controllers.OpenSubKey(
                            id,
                            writable: false);

                    result.Add(
                        new X52ControllerRegistryCandidate(
                            id,
                            view,
                            BuildDescriptor(controller)));
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Debug(
                    $"X52 controller registry read skipped ({view}): {ex.Message}");
            }
        }

        return result;
    }

    private static string BuildDescriptor(RegistryKey? key)
    {
        if (key is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (string name in key.GetValueNames())
        {
            object? value = key.GetValue(name);
            if (value is not null)
            {
                parts.Add(name);
                parts.Add(value.ToString() ?? string.Empty);
            }
        }

        return string.Join(" ", parts);
    }

    private static string? ReadStartupProfile(
        X52ControllerRegistryCandidate controller)
    {
        using RegistryKey currentUser =
            RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser,
                controller.View);
        using RegistryKey? startup =
            currentUser.OpenSubKey(
                StartupProfilesKey,
                writable: false);

        return startup?.GetValue(controller.Id) as string;
    }

    private static IReadOnlyList<string> FindProfiles()
    {
        string commonDocuments =
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonDocuments);

        string[] directories =
        [
            Path.Combine(commonDocuments, "Logitech", "X52 Professional"),
            Path.Combine(commonDocuments, "Logitech", "X52 Pro"),
            Path.Combine(commonDocuments, "Logitech", "X52")
        ];

        return directories
            .Where(Directory.Exists)
            .SelectMany(directory =>
                Directory.EnumerateFiles(
                    directory,
                    "*.pr0",
                    SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                path => Path.GetFileName(path),
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string? ResolveProfile(
        IReadOnlyList<string> discovered,
        string? preferredProfilePath,
        string? currentStartupPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredProfilePath)
            && preferredProfilePath.EndsWith(
                ".pr0",
                StringComparison.OrdinalIgnoreCase)
            && File.Exists(preferredProfilePath))
        {
            return Path.GetFullPath(preferredProfilePath);
        }

        if (!string.IsNullOrWhiteSpace(currentStartupPath)
            && currentStartupPath.EndsWith(
                ".pr0",
                StringComparison.OrdinalIgnoreCase)
            && File.Exists(currentStartupPath))
        {
            return Path.GetFullPath(currentStartupPath);
        }

        return discovered.FirstOrDefault();
    }

    private X52StartupProfileBackup? LoadBackup()
    {
        if (!File.Exists(backupPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<X52StartupProfileBackup>(
            File.ReadAllText(backupPath));
    }
}
