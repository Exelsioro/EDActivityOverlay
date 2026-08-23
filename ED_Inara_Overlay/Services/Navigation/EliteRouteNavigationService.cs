using System.IO;
using ED_Inara_Overlay.Services.Journal;
using ED_Inara_Overlay.Utils;

namespace ED_Inara_Overlay.Services.Navigation;

public enum EliteNavigationStatus
{
    Ready,
    WaitingForPlayer,
    Verifying,
    Completed,
    Failed
}

public sealed record EliteNavigationResult(
    EliteNavigationStatus Status,
    string TargetSystem,
    string MessageKey,
    string Detail = "");

public sealed class EliteRouteNavigationService
{
    private const ushort ControlKey = 0x11;
    private const ushort AKey = 0x41;
    private const ushort EnterKey = 0x0D;

    public static EliteRouteNavigationService Instance { get; } = new();

    public EliteNavigationBindings DetectBindings() => EliteBindingsService.Detect(
        presetOverride: SettingsService.Instance.Settings.EliteBindingsPreset,
        fileOverride: SettingsService.Instance.Settings.EliteBindingsFilePath);

    public async Task<EliteNavigationResult> PrepareAsync(
        string targetSystem,
        IntPtr gameWindow,
        bool confirmAutomatically,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetSystem))
            return Failure(targetSystem, "Loc_NAVIGATION_NO_TARGET");
        if (gameWindow == IntPtr.Zero || !WindowsAPI.IsWindow(gameWindow))
            return Failure(targetSystem, "Loc_NAVIGATION_GAME_NOT_FOUND");

        AppSettings settings = SettingsService.Instance.Settings;
        if (confirmAutomatically && !settings.EnableExperimentalRouteAutomation)
            return Failure(targetSystem, "Loc_NAVIGATION_AUTO_DISABLED");

        EliteNavigationBindings bindings;
        try { bindings = DetectBindings(); }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Elite navigation bindings were not detected: {ex.Message}");
            return Failure(targetSystem, "Loc_NAVIGATION_BINDINGS_FAILED", ex.Message);
        }

        try
        {
            Logger.Logger.Info($"Galaxy Map handoff started: target={targetSystem}, automatic={confirmAutomatically}, preset={bindings.PresetName}, file={Path.GetFileName(bindings.FilePath)}, keys={bindings.GalaxyMap.DisplayName}/{bindings.NextPanel.DisplayName}/{bindings.Select.DisplayName}");
            bool activationReported = WindowsAPI.TryActivateWindow(gameWindow);
            Logger.Logger.Info($"Galaxy Map handoff focus request: reported={activationReported}, foreground={WindowsAPI.GetForegroundWindow()}, target={gameWindow}");
            await WaitFocusedAsync(gameWindow, 1500, cancellationToken);
            await DelayAndCheckFocus(gameWindow, 350, cancellationToken);

            if (JournalMonitorService.Instance.Current.GuiFocus != 6)
            {
                Logger.Logger.Info("Galaxy Map handoff: sending map-open key.");
                await EliteInputSender.PressAsync(bindings.GalaxyMap, cancellationToken);
                bool mapOpened = await WaitForGuiFocusAsync(6,
                    Math.Max(5000, settings.RouteAutomationMapDelayMs), gameWindow, cancellationToken);
                if (!mapOpened)
                    return Failure(targetSystem, "Loc_NAVIGATION_MAP_NOT_DETECTED");
            }
            Logger.Logger.Info("Galaxy Map handoff: Galaxy Map detected; clicking navigation search field.");
            await DelayAndCheckFocus(gameWindow, Math.Max(700, settings.RouteAutomationStepDelayMs), cancellationToken);
            if (!WindowsAPI.GetWindowRect(gameWindow, out WindowsAPI.RECT gameRect))
                return Failure(targetSystem, "Loc_NAVIGATION_GAME_NOT_FOUND");
            int searchX = gameRect.Left + (gameRect.Right - gameRect.Left) / 2;
            int searchY = gameRect.Top + (int)Math.Round((gameRect.Bottom - gameRect.Top) * 0.118);
            await EliteInputSender.ClickAsync(searchX, searchY, cancellationToken);
            await DelayAndCheckFocus(gameWindow, settings.RouteAutomationStepDelayMs, cancellationToken);
            await EliteInputSender.PressAsync(AKey, cancellationToken, ControlKey);
            await EliteInputSender.TypeTextAsync(targetSystem, cancellationToken);
            Logger.Logger.Info($"Galaxy Map handoff: search clicked at {searchX},{searchY}; target typed; automatic={confirmAutomatically}.");

            if (!confirmAutomatically)
                return new EliteNavigationResult(EliteNavigationStatus.WaitingForPlayer, targetSystem,
                    "Loc_NAVIGATION_READY_CONFIRM", bindings.PresetName);

            // Search results are populated asynchronously. A coordinate click made before the
            // dropdown appeared could hit the map and leave the previously selected star active.
            // Enter selects the exact first result reliably, but only after the result list has
            // had enough time to appear.
            int searchResultDelayMs = Math.Max(3000, settings.RouteAutomationStepDelayMs * 3);
            await DelayAndCheckFocus(gameWindow, searchResultDelayMs, cancellationToken);
            await EliteInputSender.PressAsync(EnterKey, cancellationToken);
            Logger.Logger.Info($"Galaxy Map automation: search result selected with Enter after {searchResultDelayMs} ms.");

            await DelayAndCheckFocus(gameWindow, Math.Max(1800, settings.RouteAutomationStepDelayMs * 2), cancellationToken);
            await EliteInputSender.HoldAsync(bindings.Select, 1300, cancellationToken);
            Logger.Logger.Info($"Galaxy Map automation: held UI Select ({bindings.Select.DisplayName}) to plot route; waiting for NavRoute.json.");
            bool verified = await WaitForRouteAsync(targetSystem,
                TimeSpan.FromSeconds(settings.RouteAutomationVerificationSeconds), cancellationToken);
            return verified
                ? new EliteNavigationResult(EliteNavigationStatus.Completed, targetSystem,
                    "Loc_NAVIGATION_VERIFIED")
                : Failure(targetSystem, "Loc_NAVIGATION_NOT_VERIFIED");
        }
        catch (OperationCanceledException)
        {
            return Failure(targetSystem, "Loc_NAVIGATION_CANCELLED");
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Elite navigation automation stopped: {ex.Message}");
            return Failure(targetSystem, "Loc_NAVIGATION_FAILED", ex.Message);
        }
    }

    public static bool RouteContainsTarget(string targetSystem) =>
        JournalMonitorService.Instance.Current.NavRoute.Any(star =>
            string.Equals(star.System, targetSystem, StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> WaitForRouteAsync(string targetSystem, TimeSpan timeout, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (RouteContainsTarget(targetSystem)) return true;
            await Task.Delay(250, token);
        }
        return RouteContainsTarget(targetSystem);
    }

    private static async Task WaitFocusedAsync(IntPtr gameWindow, int milliseconds, CancellationToken token)
    {
        int elapsed = 0;
        while (elapsed < milliseconds)
        {
            token.ThrowIfCancellationRequested();
            if (WindowsAPI.GetForegroundWindow() == gameWindow) return;
            await Task.Delay(25, token);
            elapsed += 25;
        }
        if (WindowsAPI.GetForegroundWindow() != gameWindow)
            throw new InvalidOperationException("Elite Dangerous did not receive focus.");
    }

    private static async Task<bool> WaitForGuiFocusAsync(
        int expectedFocus, int timeoutMs, IntPtr gameWindow, CancellationToken token)
    {
        int remaining = Math.Max(0, timeoutMs);
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            if (WindowsAPI.GetForegroundWindow() != gameWindow)
                throw new InvalidOperationException("Elite Dangerous lost focus while opening Galaxy Map.");
            if (JournalMonitorService.Instance.Current.GuiFocus == expectedFocus) return true;
            int delay = Math.Min(remaining, 100);
            await Task.Delay(delay, token);
            remaining -= delay;
        }
        Logger.Logger.Warning($"Galaxy Map handoff: expected GuiFocus={expectedFocus}, actual={JournalMonitorService.Instance.Current.GuiFocus}.");
        return JournalMonitorService.Instance.Current.GuiFocus == expectedFocus;
    }

    private static async Task DelayAndCheckFocus(IntPtr gameWindow, int milliseconds, CancellationToken token)
    {
        int remaining = Math.Max(0, milliseconds);
        while (remaining > 0)
        {
            if (WindowsAPI.GetForegroundWindow() != gameWindow)
                throw new InvalidOperationException("Elite Dangerous lost focus; automation was stopped.");
            int delay = Math.Min(remaining, 100);
            await Task.Delay(delay, token);
            remaining -= delay;
        }
        if (WindowsAPI.GetForegroundWindow() != gameWindow)
            throw new InvalidOperationException("Elite Dangerous lost focus; automation was stopped.");
    }

    private static EliteNavigationResult Failure(string target, string key, string detail = "") =>
        new(EliteNavigationStatus.Failed, target, key, detail);
}
