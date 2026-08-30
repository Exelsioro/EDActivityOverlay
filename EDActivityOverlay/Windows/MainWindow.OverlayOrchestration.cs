using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Utils;
using EDActivityOverlay.Windows;
using EDActivityOverlay.Models.Trading;

namespace EDActivityOverlay
{
    public partial class MainWindow
    {
        private bool IsTradeSurfaceVisible() =>
            currentActivity == ActivityType.Trade
            && (activityWorkspaceWindow?.IsVisible == true
                || pinnedRouteOverlay?.IsVisible == true);

        private void SetupGlobalHotkeys()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    bool toggleRegistered = WindowsAPI.RegisterHotKey(helper.Handle, HOTKEY_ID_TOGGLE, toggleHotkeyModifiers, toggleHotkeyVirtualKey);
                    bool interactiveRegistered = WindowsAPI.RegisterHotKey(helper.Handle, HOTKEY_ID_INTERACTIVE, interactiveHotkeyModifiers, interactiveHotkeyVirtualKey);
                    bool unpinRegistered = WindowsAPI.RegisterHotKey(helper.Handle, HOTKEY_ID_UNPIN, unpinHotkeyModifiers, unpinHotkeyVirtualKey);
                    bool tradeRegistered = WindowsAPI.RegisterHotKey(helper.Handle, HOTKEY_ID_TRADE, tradeHotkeyModifiers, tradeHotkeyVirtualKey);
                    bool engineeringRegistered = WindowsAPI.RegisterHotKey(helper.Handle, HOTKEY_ID_ENGINEERING, engineeringHotkeyModifiers, engineeringHotkeyVirtualKey);
                    bool explorationRegistered = WindowsAPI.RegisterHotKey(helper.Handle, HOTKEY_ID_EXPLORATION, explorationHotkeyModifiers, explorationHotkeyVirtualKey);
                    bool miningRegistered = WindowsAPI.RegisterHotKey(helper.Handle, HOTKEY_ID_MINING, miningHotkeyModifiers, miningHotkeyVirtualKey);
                    if (toggleRegistered || interactiveRegistered || unpinRegistered || tradeRegistered
                        || engineeringRegistered || explorationRegistered || miningRegistered)
                    {
                        Logger.Logger.Info($"Global hotkeys registration: toggle={toggleRegistered}, interactive={interactiveRegistered}, unpin={unpinRegistered}, activities={tradeRegistered}/{engineeringRegistered}/{explorationRegistered}/{miningRegistered}");
                        hwndSource = HwndSource.FromHwnd(helper.Handle);
                        hwndSource?.AddHook(HwndHook);
                    }
                    else
                    {
                        Logger.Logger.Warning($"Hotkey registration status: toggle={toggleRegistered}, interactive={interactiveRegistered}, unpin={unpinRegistered}");
                    }
                }
                else
                {
                    Logger.Logger.Warning("Cannot register global hotkeys - window handle not available");
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error setting up global hotkeys: {ex.Message}");
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WindowsAPI.WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                long now = Environment.TickCount64;
                if (hotkeyLastHandledAt.TryGetValue(hotkeyId, out long previous) && now - previous < 350)
                {
                    hotkeyLastHandledAt[hotkeyId] = now;
                    handled = true;
                    return IntPtr.Zero;
                }
                hotkeyLastHandledAt[hotkeyId] = now;
                if (hotkeyId == HOTKEY_ID_TOGGLE)
                {
                    Logger.Logger.Info("Global hotkey detected - triggering toggle action");
                    Dispatcher.BeginInvoke(new Action(PerformToggleAction));
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_INTERACTIVE)
                {
                    Dispatcher.BeginInvoke(new Action(ToggleInteractiveModeFromHotkey));
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_UNPIN)
                {
                    Dispatcher.BeginInvoke(new Action(UnpinRouteOverlay));
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_ENGINEERING)
                {
                    Dispatcher.BeginInvoke(new Action(() => ToggleActivityFromHotkey(ActivityType.Engineering)));
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_TRADE)
                {
                    Dispatcher.BeginInvoke(new Action(() => ToggleActivityFromHotkey(ActivityType.Trade)));
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_EXPLORATION)
                {
                    Dispatcher.BeginInvoke(new Action(() => ToggleActivityFromHotkey(ActivityType.Exploration)));
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_MINING)
                {
                    Dispatcher.BeginInvoke(new Action(() => ToggleActivityFromHotkey(ActivityType.Mining)));
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private void UnregisterGlobalHotkeys()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    WindowsAPI.UnregisterHotKey(helper.Handle, HOTKEY_ID_TOGGLE);
                    WindowsAPI.UnregisterHotKey(helper.Handle, HOTKEY_ID_INTERACTIVE);
                    WindowsAPI.UnregisterHotKey(helper.Handle, HOTKEY_ID_UNPIN);
                    WindowsAPI.UnregisterHotKey(helper.Handle, HOTKEY_ID_TRADE);
                    WindowsAPI.UnregisterHotKey(helper.Handle, HOTKEY_ID_ENGINEERING);
                    WindowsAPI.UnregisterHotKey(helper.Handle, HOTKEY_ID_EXPLORATION);
                    WindowsAPI.UnregisterHotKey(helper.Handle, HOTKEY_ID_MINING);
                    Logger.Logger.Info("Global hotkeys unregistered");
                }

                hwndSource?.RemoveHook(HwndHook);
                hwndSource = null;
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error unregistering global hotkeys: {ex.Message}");
            }
        }

        private void PerformToggleAction()
        {
            if (!overlaysSuppressedByHotkey)
            {
                restoreTradeVisible = false;
                restoreResultsVisible = false;
                restorePinnedVisible = pinnedRouteOverlay?.IsVisible == true;
                restoreEngineeringVisible = engineeringOverlayWindow?.IsVisible == true;
                restoreActivityWorkspaceVisible = activityWorkspaceWindow?.IsVisible == true;

                bool anyOverlayVisible =
                    restorePinnedVisible
                    || restoreEngineeringVisible
                    || restoreActivityWorkspaceVisible;
                if (!anyOverlayVisible)
                {
                    SelectActivity(currentActivity);
                    return;
                }

                overlaysSuppressedByHotkey = true;
                OverlayVisibilityState.SuppressAll = true;
                HideAllOverlaysForHotkey();
                UpdateInteractionStatusUi();
                Logger.Logger.Info("Global Ctrl+5 toggle: all visible overlay windows hidden.");
            }
            else
            {
                overlaysSuppressedByHotkey = false;
                OverlayVisibilityState.SuppressAll = false;
                RestoreOverlaysAfterHotkey();
                UpdateInteractionStatusUi();
                Logger.Logger.Info("Global Ctrl+5 toggle: previously visible overlay windows restored.");
            }
        }

        private void ToggleTradeRouteWindowLegacy() =>
            ToggleActivityFromHotkey(
                ActivityType.Trade);

        private void HideAllOverlaysForHotkey()
        {
            if (tradeRouteWindow != null && tradeRouteWindow.IsVisible)
            {
                tradeRouteWindow.Hide();
            }

            if (resultsOverlayWindow != null && resultsOverlayWindow.IsVisible)
            {
                resultsOverlayWindow.Hide();
            }

            if (pinnedRouteOverlay != null && pinnedRouteOverlay.IsVisible)
            {
                pinnedRouteOverlay.Hide();
            }

            if (engineeringOverlayWindow != null && engineeringOverlayWindow.IsVisible)
            {
                engineeringOverlayWindow.Hide();
            }

            if (activityWorkspaceWindow != null && activityWorkspaceWindow.IsVisible)
            {
                activityWorkspaceWindow.Hide();
            }

            if (shipStatusOverlayWindow != null && shipStatusOverlayWindow.IsVisible)
            {
                shipStatusOverlayWindow.Hide();
            }
        }

        private void RestoreOverlaysAfterHotkey()
        {


            if (restorePinnedVisible && pinnedRouteOverlay != null)
            {
                pinnedRouteOverlay.Show();
            }

            if (restoreEngineeringVisible && engineeringOverlayWindow != null)
            {
                engineeringOverlayWindow.Show();
            }

            if (restoreActivityWorkspaceVisible && activityWorkspaceWindow != null)
            {
                activityWorkspaceWindow.Show();
            }

            if (shipStatusOverlayWindow != null && SettingsService.Instance.Settings.EnableShipStatusWidget)
            {
                shipStatusOverlayWindow.Show();
            }
        }

        public void CloseEngineeringOverlay()
        {
            engineeringOverlayWindow?.Close();
            engineeringOverlayWindow = null;
        }

        public void OnEngineeringOverlayClosed() => engineeringOverlayWindow = null;

        private string GetEngineeringOverlayPlacement() =>
            OverlayLayoutHelper.GetOppositeSidePlacement(SettingsService.Instance.Settings.PinnedRoutePosition);

        private void UpdateToggleButtonState()
        {
            // MainWindow no longer has a Trade Routes button; state kept for overlay lifecycle logic.
        }

        public void OnTradeRouteWindowClosed()
        {
            isToggleActive = false;
            UpdateToggleButtonState();
        }

        public void OnResultsWindowClosed()
        {
            isResultsActive = false;
            Logger.Logger.Info("Results overlay window closed");
        }

        public void OnPinRouteRequested(
            TradeRoute tradeRoute,
            bool keepTradeWorkspace = false)
        {
            Logger.Logger.Info($"Pin route requested from MainWindow: {tradeRoute.CardHeader.FromStation.System} -> {tradeRoute.CardHeader.ToStation.System}");
            if (pinnedRouteOverlay == null || !pinnedRouteOverlay.IsLoaded)
            {
                Logger.Logger.Info("Creating new PinnedRouteOverlay instance");
                pinnedRouteOverlay = new PinnedRouteOverlay(this);
            }

            pinnedRouteOverlay.SetTargetWindow(targetWindow, targetProcessId);
            pinnedRouteOverlay.SetPlacement(SettingsService.Instance.Settings.PinnedRoutePosition);
            pinnedRouteOverlay.ApplyInteractionMode(interactionModeEnabled && interactiveModeActive, showCursorWhenInteractive);
            pinnedRouteOverlay.PinTradeRoute(tradeRoute);
            engineeringOverlayWindow?.SetPlacement(GetEngineeringOverlayPlacement());
            isPinnedRouteActive = true;

            if (currentActivity == ActivityType.Trade
                && !keepTradeWorkspace)
            {
                CloseActivityWorkspace();
            }

            CloseOverlayWindows();
            Logger.Logger.Info("Route pinned successfully, closing other overlays");
        }

        public void UnpinRouteOverlay()
        {
            if (pinnedRouteOverlay != null && isPinnedRouteActive)
            {
                Logger.Logger.Info("Unpinning current pinned route overlay");
                try
                {
                    pinnedRouteOverlay.Close();
                }
                catch (Exception ex)
                {
                    Logger.Logger.Warning($"Error closing pinned route overlay: {ex.Message}");
                }

                pinnedRouteOverlay = null;
                isPinnedRouteActive = false;
                activityWorkspaceWindow?.ClearActiveTradeRouteFromHost();
                Logger.Logger.Info("Pinned route overlay unpinned successfully");
            }
        }

        private void CloseOverlayWindows()
        {
            Logger.Logger.Info("Closing overlay windows for pin route functionality");
            if (tradeRouteWindow != null && tradeRouteWindow.IsVisible)
            {
                Logger.Logger.Info("Closing TradeRouteWindow");
                tradeRouteWindow.Close();
                tradeRouteWindow = null;
                isToggleActive = false;
                UpdateToggleButtonState();
            }

            if (resultsOverlayWindow != null && resultsOverlayWindow.IsVisible)
            {
                Logger.Logger.Info("Closing ResultsOverlayWindow");
                resultsOverlayWindow.Close();
                isResultsActive = false;
                resultsOverlayWindow = null;
                Logger.Logger.Info("ResultsOverlayWindow closed and state reset");
            }

            Logger.Logger.Info("All overlay windows closed");
        }

        private bool IsTargetProcessRunning()
        {
            try
            {
                if (targetProcessId == 0)
                {
                    return false;
                }

                var process = Process.GetProcessById((int)targetProcessId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning($"Error checking target process status: {ex.Message}");
                return false;
            }
        }

        private void ShutdownApplication(string reason)
        {
            Logger.Logger.Info($"Initiating application shutdown: {reason}");
            isToggleActive = false;
            UpdateToggleButtonState();
            CloseAllOverlayWindows();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Logger.Logger.Info($"Application shutdown initiated: {reason}");
                Application.Current.Shutdown();
            }));
        }

        private void CloseAllOverlayWindows()
        {
            Logger.Logger.Info("Closing all overlay windows due to target window closure");
            try
            {
                if (updateTimer != null)
                {
                    updateTimer.Stop();
                    Logger.Logger.Info("Main update timer stopped");
                }

                if (tradeRouteWindow != null)
                {
                    Logger.Logger.Info("Closing TradeRouteWindow");
                    try
                    {
                        tradeRouteWindow.StopUpdateTimer();
                        tradeRouteWindow.Close();
                    }
                    catch (Exception ex)
                    {
                        Logger.Logger.Warning($"Error closing TradeRouteWindow: {ex.Message}");
                    }

                    tradeRouteWindow = null;
                }

                if (resultsOverlayWindow != null)
                {
                    Logger.Logger.Info("Closing ResultsOverlayWindow");
                    try
                    {
                        resultsOverlayWindow.StopUpdateTimer();
                        resultsOverlayWindow.Close();
                        resultsOverlayWindow.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Logger.Warning($"Error closing ResultsOverlayWindow: {ex.Message}");
                    }

                    resultsOverlayWindow = null;
                }

                if (pinnedRouteOverlay != null)
                {
                    Logger.Logger.Info("Closing PinnedRouteOverlay");
                    try
                    {
                        pinnedRouteOverlay.Close();
                    }
                    catch (Exception ex)
                    {
                        Logger.Logger.Warning($"Error closing PinnedRouteOverlay: {ex.Message}");
                    }

                    pinnedRouteOverlay = null;
                }

                if (engineeringOverlayWindow != null)
                {
                    Logger.Logger.Info("Closing Engineering Assistant overlay");
                    engineeringOverlayWindow.Close();
                    engineeringOverlayWindow = null;
                }

                if (activityWorkspaceWindow != null)
                {
                    Logger.Logger.Info("Closing activity workspace overlay");
                    activityWorkspaceWindow.Close();
                    activityWorkspaceWindow = null;
                }

                if (notificationOverlayWindow != null)
                {
                    notificationOverlayWindow.Close();
                    notificationOverlayWindow = null;
                }

                if (shipStatusOverlayWindow != null)
                {
                    shipStatusOverlayWindow.Close();
                    shipStatusOverlayWindow = null;
                }

                isToggleActive = false;
                isResultsActive = false;
                isPinnedRouteActive = false;
                Logger.Logger.Info("All overlay windows closed successfully");
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error during overlay windows cleanup: {ex.Message}");
            }
        }

        public void ShowResultsOverlay(List<TradeRoute> tradeRoutes)
        {
            bool needNewInstance = resultsOverlayWindow == null
                || !resultsOverlayWindow.IsLoaded
                || (resultsOverlayWindow.Tag?.ToString() == "disposed");

            if (needNewInstance)
            {
                Logger.Logger.Info("Creating new ResultsOverlayWindow instance");
                resultsOverlayWindow = new ResultsOverlayWindow(this);
                resultsOverlayWindow.ApplyInteractionMode(interactionModeEnabled && interactiveModeActive, showCursorWhenInteractive);
            }

            if (resultsOverlayWindow != null)
            {
                resultsOverlayWindow.SetTargetWindow(targetWindow, targetProcessId);
                resultsOverlayWindow.ApplyInteractionMode(interactionModeEnabled && interactiveModeActive, showCursorWhenInteractive);
                resultsOverlayWindow.DisplayTradeRoutes(tradeRoutes);
                if (!resultsOverlayWindow.IsVisible)
                {
                    Logger.Logger.Info("Showing ResultsOverlayWindow");
                    resultsOverlayWindow.Show();
                }

                isResultsActive = true;
                Logger.Logger.Info($"Results overlay active: {isResultsActive}");
            }
            else
            {
                Logger.Logger.Error("Failed to create or access ResultsOverlayWindow instance");
            }
        }

        public void EnsureVisibleAfterTargetDetection()
        {
            Logger.Logger.Info(
                "Setting forceVisible flag and transitioning to ForceShow state for target detection");

            forceVisible =
                true;

            if (currentState == OverlayState.Waiting)
            {
                currentState =
                    OverlayState.ForceShow;

                Logger.Logger.Info(
                    "State transition: Waiting -> ForceShow (triggered by target detection)");
            }

            if (targetWindow == IntPtr.Zero)
            {
                return;
            }

            ApplyAdaptiveSizeForTarget();
            PositionMainOverlayInPhysicalCorner();

            if (!IsLoaded)
            {
                return;
            }

            bool targetVisible =
                WindowsAPI.IsWindowVisible(
                    targetWindow);

            bool targetMinimized =
                WindowsAPI.IsIconic(
                    targetWindow);

            if (!targetVisible
                || targetMinimized
                || IsVisible)
            {
                return;
            }

            Logger.Logger.Info(
                "Showing MainWindow immediately after target detection");

            Show();
            ApplyAdaptiveSizeForTarget();
            PositionMainOverlayInPhysicalCorner();

            bool targetFocused =
                WindowsAPI.IsWindowOwnedByProcess(
                    WindowsAPI.GetForegroundWindow(),
                    targetProcessId);

            WindowsAPI.SetTopmost(
                this,
                targetFocused);
        }
        protected override void OnClosed(EventArgs e)
        {
            Logger.Logger.Info("MainWindow is closing - cleaning up resources");
            disposed = true;

            if (updateTimer != null)
            {
                updateTimer.Stop();
                updateTimer = null;
            }

            if (retryTimer != null)
            {
                retryTimer.Stop();
                retryTimer = null;
            }

            UnregisterGlobalHotkeys();
            CloseAllOverlayWindows();
            ThemeManager.Instance.ThemeApplied -= OnThemeApplied;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            JournalMonitorService.Instance.StateChanged -= OnJournalStateChanged;
            Services.Hardware.X52IntegrationService.Instance.ControlRequested -= OnX52ControlRequested;
            x52OverlayPointerController.Dispose();
            Logger.Logger.Info("MainWindow closed and all resources cleaned up");
            base.OnClosed(e);
        }

        private void OnThemeApplied(object? sender, ThemeAppliedEventArgs e)
        {
            Logger.Logger.Info($"Theme changed to: {e.Theme.Name}");
            UpdateToggleButtonState();
            InvalidateVisual();
        }

        private void ToggleInteractiveModeFromHotkey()
        {
            if (!interactionModeEnabled)
            {
                Logger.Logger.Info("Interactive hotkey ignored - interaction mode disabled in settings");
                return;
            }

            SetInteractiveMode(!interactiveModeActive, "hotkey");
        }
    }
}
