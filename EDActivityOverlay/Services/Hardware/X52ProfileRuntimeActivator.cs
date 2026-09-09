using System.Diagnostics;
using System.IO;

namespace EDActivityOverlay.Services.Hardware;

public sealed record X52ApplyNowResult(
    X52StartupProfileState State,
    bool ProfilerRestarted,
    string Error);

public static class X52ProfileRuntimeActivator
{
    private static readonly string[] LoaderProcessNames =
    [
        "X52Pro_Profiler",
        "X52_Profiler",
        "ProfilerU"
    ];

    public static X52ApplyNowResult ApplyNow(string profilePath)
    {
        X52StartupProfileState configured =
            X52StartupProfileService.Instance.Configure(profilePath);

        if (configured.Status != X52StartupProfileStatus.Active)
        {
            return new X52ApplyNowResult(
                configured,
                false,
                configured.Error);
        }

        try
        {
            string? launcher = FindProfilerLauncherPath();
            if (string.IsNullOrWhiteSpace(launcher))
            {
                return new X52ApplyNowResult(
                    configured,
                    false,
                    "Logitech X52 profile loader was not found");
            }

            string processName = Path.GetFileNameWithoutExtension(launcher);
            StopProcesses(processName);

            Process? started = Process.Start(
                new ProcessStartInfo(launcher)
                {
                    UseShellExecute = true,
                    WorkingDirectory =
                        Path.GetDirectoryName(launcher)
                        ?? string.Empty
                });

            started?.Dispose();

            Logger.Logger.Info(
                $"X52 profile apply-now restarted Logitech profile loader: {launcher}");

            return new X52ApplyNowResult(
                X52StartupProfileService.Instance.Inspect(profilePath),
                true,
                string.Empty);
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"X52 profile apply-now failed: {ex.Message}");

            return new X52ApplyNowResult(
                configured,
                false,
                ex.Message);
        }
    }

    internal static string? FindProfilerLauncherPath()
    {
        foreach (string processName in LoaderProcessNames)
        {
            string? running = FindRunningExecutable(processName);
            if (!string.IsNullOrWhiteSpace(running))
            {
                return running;
            }
        }

        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];

        string[] relativePaths =
        [
            // Current Logitech X52/X52 Pro package.
            @"Logitech\X52 Professional\X52Pro_Profiler.exe",
            @"Logitech\X52\X52_Profiler.exe",

            // Older Saitek / Smart Technology packages.
            @"SmartTechnology\Software\ProfilerU.exe",
            @"Saitek\SD6\Software\ProfilerU.exe",
            @"Saitek\Software\ProfilerU.exe"
        ];

        string? known = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .SelectMany(root =>
                relativePaths.Select(relative =>
                    Path.Combine(root, relative)))
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(known))
        {
            return known;
        }

        // Logitech has shipped the loader under more than one directory name.
        // Keep the fallback bounded to Logitech/Saitek folders rather than
        // recursively scanning all of Program Files.
        foreach (string root in roots.Where(root =>
                     !string.IsNullOrWhiteSpace(root)))
        {
            foreach (string vendor in new[] { "Logitech", "Saitek" })
            {
                string vendorRoot = Path.Combine(root, vendor);
                string? discovered = FindLoaderBelow(vendorRoot);
                if (!string.IsNullOrWhiteSpace(discovered))
                {
                    return discovered;
                }
            }
        }

        return null;
    }

    private static string? FindRunningExecutable(string processName)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                string? running = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(running)
                    && File.Exists(running))
                {
                    return running;
                }
            }
            catch
            {
                // Access to MainModule can be denied when the profiler runs elevated.
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static void StopProcesses(string processName)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Logger.Logger.Debug(
                    $"X52 profile-loader stop skipped ({processName}): {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string? FindLoaderBelow(string vendorRoot)
    {
        if (!Directory.Exists(vendorRoot))
        {
            return null;
        }

        foreach (string fileName in new[]
                 {
                     "X52Pro_Profiler.exe",
                     "X52_Profiler.exe",
                     "ProfilerU.exe"
                 })
        {
            try
            {
                string? match = Directory
                    .EnumerateFiles(
                        vendorRoot,
                        fileName,
                        SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Debug(
                    $"X52 profile-loader search skipped ({vendorRoot}): {ex.Message}");
            }
        }

        return null;
    }
}
