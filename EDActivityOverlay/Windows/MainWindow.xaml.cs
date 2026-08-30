using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Collections.Generic;
using EDActivityOverlay.Utils;
using EDActivityOverlay.Windows;
using EDActivityOverlay.Services;
using System.Diagnostics;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Notifications;
using EDActivityOverlay.Services.Hardware;
using EDActivityOverlay.Services.Dss;

namespace EDActivityOverlay
{
    /// <summary>
    /// Main overlay window - equivalent to OverlayForm in the Windows Forms version
    /// </summary>
    public partial class MainWindow : Window
    {
        internal IntPtr TargetWindowHandle => targetWindow;

        private enum OverlayState { Waiting, ForceShow, Auto };
        
        private IntPtr targetWindow;
        private uint targetProcessId;
        private DispatcherTimer? updateTimer;
        private bool disposed;
        private TradeRouteWindow? tradeRouteWindow;
        private ResultsOverlayWindow? resultsOverlayWindow;
        private PinnedRouteOverlay? pinnedRouteOverlay;
        private EngineeringWindow? engineeringOverlayWindow;
        private NotificationOverlayWindow? notificationOverlayWindow;
        private ShipStatusOverlayWindow? shipStatusOverlayWindow;
        private DssPrototypeController? dssPrototypeController;
        private readonly X52OverlayPointerController x52OverlayPointerController;
        private bool isToggleActive = false;
        private bool isResultsActive = false;
        private bool isPinnedRouteActive = false;
        private bool overlaysSuppressedByHotkey = false;
        private bool restoreTradeVisible = false;
        private bool restoreResultsVisible = false;
        private bool restorePinnedVisible = false;
        private bool restoreEngineeringVisible = false;
        private bool forceVisible = false; // Flag to ensure visibility after target detection
        private OverlayState currentState = OverlayState.Waiting;
        private const int HOTKEY_ID_TOGGLE = 9001;
        private const int HOTKEY_ID_INTERACTIVE = 9002;
        private const int HOTKEY_ID_UNPIN = 9003;
        private const int HOTKEY_ID_TRADE = 9010;
        private const int HOTKEY_ID_ENGINEERING = 9011;
        private const int HOTKEY_ID_EXPLORATION = 9012;
        private const int HOTKEY_ID_MINING = 9013;
        private HwndSource? hwndSource; // For handling Windows messages
        private readonly Dictionary<int, long> hotkeyLastHandledAt = new();
        private uint toggleHotkeyModifiers = WindowsAPI.MOD_CONTROL;
        private uint toggleHotkeyVirtualKey = WindowsAPI.VK_5;
        private uint interactiveHotkeyModifiers = WindowsAPI.MOD_CONTROL;
        private uint interactiveHotkeyVirtualKey = WindowsAPI.VK_6;
        private uint unpinHotkeyModifiers = WindowsAPI.MOD_CONTROL;
        private uint unpinHotkeyVirtualKey = WindowsAPI.VK_7;
        private uint tradeHotkeyModifiers = WindowsAPI.MOD_CONTROL;
        private uint tradeHotkeyVirtualKey = WindowsAPI.VK_1;
        private uint engineeringHotkeyModifiers = WindowsAPI.MOD_CONTROL;
        private uint engineeringHotkeyVirtualKey = WindowsAPI.VK_2;
        private uint explorationHotkeyModifiers = WindowsAPI.MOD_CONTROL;
        private uint explorationHotkeyVirtualKey = WindowsAPI.VK_3;
        private uint miningHotkeyModifiers = WindowsAPI.MOD_CONTROL;
        private uint miningHotkeyVirtualKey = WindowsAPI.VK_4;
        private bool interactionModeEnabled = true;
        private bool interactiveModeActive;
        private bool returnOnFocusLoss = true;
        private bool showCursorWhenInteractive = true;
        private int autoReturnTimeoutSeconds = 8;
        private bool exclusiveOverlayInteraction;
        private bool interactionStateBeforeExclusiveOverlay;
        private DateTime interactiveModeEnteredAtUtc;
        private DateTime interactiveFocusLossGraceUntilUtc;
        private static readonly TimeSpan InteractiveFocusLossGracePeriod = TimeSpan.FromMilliseconds(1500);
        private readonly double baseWindowWidth;
        private readonly double baseWindowHeight;
        private double lastAppliedScale = 1.0;
        private string chromeStyle = OverlayChromeStyles.Compact;

        public MainWindow(string processName = "notepad", Process? foundProcess = null)
        {
            InitializeComponent();
            x52OverlayPointerController = new X52OverlayPointerController(GetInteractiveInputWindowHandle);
            baseWindowWidth = Width;
            baseWindowHeight = Height;
            
            // Start hidden - only show when target has focus
            this.Visibility = Visibility.Hidden;
            
            Logger.Logger.Info($"Initializing MainWindow for process: {processName}");
            if(foundProcess != null)
            {
                targetProcessId = (uint)foundProcess.Id;
                targetWindow = WindowsAPI.FindWindowByPID(targetProcessId);
                Logger.Logger.Info($"Found target process {processName} with PID {targetProcessId}");
            }
            else
            {
                // Find target process with retry mechanism
                FindTargetProcessWithRetry(processName);
            }
            SetupOverlay();
            notificationOverlayWindow = new NotificationOverlayWindow(targetWindow);
            shipStatusOverlayWindow = new ShipStatusOverlayWindow(targetWindow);
            shipStatusOverlayWindow.SetContextSuppression(null);
            SetupUpdateTimer();
            LoadConfiguredSettings();
            RestoreMainOverlayCollapsedState();
            SetChromeStyle(SettingsService.Instance.Settings.OverlayChromeStyle);
            UpdateOverlayInteractionModes();
            UpdateInteractionStatusUi();
            UpdateActivityNavigationUi();
            
            // Listen for theme changes
            ThemeManager.Instance.ThemeApplied += OnThemeApplied;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            JournalMonitorService.Instance.StateChanged += OnJournalStateChanged;
            X52IntegrationService.Instance.ControlRequested += OnX52ControlRequested;
            X52IntegrationService.Instance.SetActivity(currentActivity);
            UpdateJournalStatusUi(JournalMonitorService.Instance.Current);
            RefreshExperimentalDssLifecycle(
                SettingsService.Instance.Settings.EnableExperimentalDssAssistant);

            Closed += (_, _) =>
            {
                dssPrototypeController?.Dispose();
                dssPrototypeController = null;
            };
            
            Logger.Logger.Info("MainWindow initialization complete - starting hidden");
        }

        private void FindTargetProcessWithRetry(string processName)
        {
            var process = WindowsAPI.FindProcessByName(processName);
            if (process != null)
            {
                targetProcessId = (uint)process.Id;
                targetWindow = WindowsAPI.FindWindowByPID(targetProcessId);
                notificationOverlayWindow?.SetTargetWindow(targetWindow);
                shipStatusOverlayWindow?.SetTargetWindow(targetWindow);
                if (targetWindow != IntPtr.Zero)
                {
                    Logger.Logger.Info($"Found target process {processName} with PID {targetProcessId} and window handle {targetWindow} immediately");
                    return;
                }
                Logger.Logger.Info($"Found target process {processName} with PID {targetProcessId} but no window handle yet");
            }
            else
            {
                Logger.Logger.Info($"Target process {processName} not found initially");
            }
            
            SetupRetryTimer(processName);
        }
        
        private DispatcherTimer? retryTimer;
        private int retryAttempts = 0;
        private string retryProcessName = "";
        private const int maxRetryAttempts = 20; // 10 seconds with 500ms intervals
        
        private void SetupRetryTimer(string processName)
        {
            retryProcessName = processName;
            retryAttempts = 0;
            
            retryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            retryTimer.Tick += RetryTimer_Tick;
            retryTimer.Start();
            
            Logger.Logger.Info($"Started retry timer for target process {processName}");
        }
        
        private void RetryTimer_Tick(object? sender, EventArgs e)
        {
            retryAttempts++;
            
            var process = WindowsAPI.FindProcessByName(retryProcessName);
            if (process != null)
            {
                targetProcessId = (uint)process.Id;
                targetWindow = WindowsAPI.FindWindowByPID(targetProcessId);
                notificationOverlayWindow?.SetTargetWindow(targetWindow);
                shipStatusOverlayWindow?.SetTargetWindow(targetWindow);
                
                if (targetWindow != IntPtr.Zero)
                {
                    Logger.Logger.Info($"Found target process {retryProcessName} with PID {targetProcessId} and window handle {targetWindow} on retry attempt {retryAttempts}");
                    
                    // Stop the retry timer
                    retryTimer?.Stop();
                    retryTimer = null;
                    
                    // Force visibility after successful detection
                    EnsureVisibleAfterTargetDetection();
                    return;
                }
                else
                {
                    Logger.Logger.Info($"Found target process {retryProcessName} with PID {targetProcessId} but no window handle yet on retry attempt {retryAttempts}");
                }
            }
            else
            {
                Logger.Logger.Info($"Target process {retryProcessName} not found on retry attempt {retryAttempts}");
            }
            
            // Stop retrying after max attempts
            if (retryAttempts >= maxRetryAttempts)
            {
                Logger.Logger.Info($"Target process {retryProcessName} not found after {maxRetryAttempts} retry attempts - giving up");
                retryTimer?.Stop();
                retryTimer = null;
            }
        }

        private void SetupOverlay()
        {
            Loaded += (_, _) =>
            {
                try
                {
                    WindowsAPI.SetupOverlayWindow(
                        this);

                    WindowsAPI.SetClickThrough(
                        this,
                        !(interactionModeEnabled
                          && interactiveModeActive));

                    ApplyAdaptiveSizeForTarget();
                    PositionMainOverlayInPhysicalCorner();

                    Logger.Logger.Info(
                        $"MainWindow positioned in physical monitor corner: " +
                        $"Left={Left}, Top={Top}");

                    SetupGlobalHotkeys();
                }
                catch (Exception ex)
                {
                    Logger.Logger.Error(
                        $"Error setting up overlay: {ex.Message}");
                }
            };
        }
        private void SetupUpdateTimer()
        {
            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        private void UpdateTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (disposed
                || updateTimer == null)
            {
                return;
            }

            if (targetWindow == IntPtr.Zero)
            {
                return;
            }

            if (currentState == OverlayState.Waiting)
            {
                currentState =
                    OverlayState.ForceShow;

                Logger.Logger.Info(
                    "State transition: Waiting -> ForceShow");
            }

            if (!WindowsAPI.IsWindow(
                    targetWindow))
            {
                Logger.Logger.Info(
                    "Target window no longer exists - shutting down application");

                ShutdownApplication(
                    "Target window closed");

                return;
            }

            if (!IsTargetProcessRunning())
            {
                Logger.Logger.Info(
                    "Target process is no longer running - shutting down application");

                ShutdownApplication(
                    "Target process terminated");

                return;
            }

            try
            {
                ApplyAdaptiveSizeForTarget();
                PositionMainOverlayInPhysicalCorner();
            }
            catch (Exception ex)
            {
                Logger.Logger.Error(
                    $"Error updating MainWindow position: {ex.Message}");
            }

            IntPtr foregroundWindow =
                WindowsAPI.GetForegroundWindow();

            bool targetHasFocus =
                WindowsAPI.IsWindowOwnedByProcess(
                    foregroundWindow,
                    targetProcessId);

            bool overlayHasFocus =
                WindowsAPI.IsOverlayWindow(
                    foregroundWindow);

            IntPtr visibilityWindow =
                targetHasFocus
                && foregroundWindow != IntPtr.Zero
                    ? foregroundWindow
                    : targetWindow;

            bool targetMinimized =
                WindowsAPI.IsIconic(
                    visibilityWindow);

            bool targetVisible =
                WindowsAPI.IsWindowVisible(
                    visibilityWindow);

            EvaluateInteractiveAutoReturn(
                foregroundWindow);

            bool shouldBeVisible =
                targetVisible
                && !targetMinimized;

            bool shouldBeTopmost =
                false;

            if (currentState == OverlayState.ForceShow)
            {
                shouldBeTopmost =
                    targetHasFocus
                    || overlayHasFocus;
            }
            else if (currentState == OverlayState.Auto)
            {
                shouldBeVisible =
                    shouldBeVisible
                    && (targetHasFocus
                        || overlayHasFocus);

                shouldBeTopmost =
                    targetHasFocus
                    || overlayHasFocus;
            }

            if (IsVisible
                && IsLoaded)
            {
                WindowsAPI.SetTopmost(
                    this,
                    shouldBeTopmost);
            }

            if (forceVisible
                && targetVisible
                && !targetMinimized)
            {
                shouldBeVisible =
                    true;

                Logger.Logger.Info(
                    "Using forceVisible flag for initial display");
            }

            if (shouldBeVisible
                && !IsVisible)
            {
                Logger.Logger.Info(
                    $"MainWindow showing - state: {currentState}, " +
                    $"targetVisible: {targetVisible}, targetFocus: {targetHasFocus}, " +
                    $"overlayFocus: {overlayHasFocus}, forced: {forceVisible}");

                WindowState =
                    WindowState.Normal;

                Show();
                ApplyAdaptiveSizeForTarget();
                PositionMainOverlayInPhysicalCorner();

                WindowsAPI.SetTopmost(
                    this,
                    shouldBeTopmost);

                if (forceVisible)
                {
                    forceVisible =
                        false;

                    Logger.Logger.Info(
                        "Resetting forceVisible flag after successful show");
                }



                if (!overlaysSuppressedByHotkey
                    && !activityHiddenByHotkey
                    && currentActivity == ActivityType.Engineering
                    && engineeringOverlayWindow
                       is
                       {
                           IsLoaded: true,
                           IsVisible: false
                       })
                {
                    engineeringOverlayWindow.Show();
                }
            }
            else if (!shouldBeVisible
                     && IsVisible)
            {
                Logger.Logger.Info(
                    $"MainWindow hiding - state: {currentState}, " +
                    $"targetFocus: {targetHasFocus}, overlayFocus: {overlayHasFocus}");

                Hide();



                if (engineeringOverlayWindow?.IsVisible == true)
                {
                    engineeringOverlayWindow.Hide();
                }
            }

            if (currentState == OverlayState.ForceShow
                && IsVisible
                && (!targetHasFocus
                    || overlayHasFocus))
            {
                currentState =
                    OverlayState.Auto;

                Logger.Logger.Info(
                    "State transition: ForceShow -> Auto (focus change detected)");
            }
        }
        private void ApplyAdaptiveSizeForTarget()
        {
            ApplyMainOverlaySizeForCurrentState();
        }
        private void LoadConfiguredSettings()
        {
            var settings = SettingsService.Instance.Settings;
            interactionModeEnabled = settings.EnableInteractionMode;
            autoReturnTimeoutSeconds = NormalizeAutoReturnTimeout(settings.AutoReturnTimeoutSeconds);
            returnOnFocusLoss = settings.ReturnOnFocusLoss;
            showCursorWhenInteractive = settings.ShowCursorWhenInteractive;

            if (TryResolveHotkey(settings.ToggleHotkeyModifiers, settings.ToggleHotkeyKey, out var resolvedToggleModifiers, out var resolvedToggleKey))
            {
                toggleHotkeyModifiers = resolvedToggleModifiers;
                toggleHotkeyVirtualKey = resolvedToggleKey;
            }
            else
            {
                toggleHotkeyModifiers = WindowsAPI.MOD_CONTROL;
                toggleHotkeyVirtualKey = WindowsAPI.VK_5;
                Logger.Logger.Warning($"Invalid toggle hotkey ({settings.ToggleHotkeyModifiers}+{settings.ToggleHotkeyKey}). Falling back to Ctrl+D5.");
            }

            if (TryResolveHotkey(settings.InteractiveHotkeyModifiers, settings.InteractiveHotkeyKey, out var resolvedInteractiveModifiers, out var resolvedInteractiveKey))
            {
                interactiveHotkeyModifiers = resolvedInteractiveModifiers;
                interactiveHotkeyVirtualKey = resolvedInteractiveKey;
            }
            else
            {
                interactiveHotkeyModifiers = WindowsAPI.MOD_CONTROL;
                interactiveHotkeyVirtualKey = WindowsAPI.VK_6;
                Logger.Logger.Warning($"Invalid interactive hotkey ({settings.InteractiveHotkeyModifiers}+{settings.InteractiveHotkeyKey}). Falling back to Ctrl+D6.");
            }

            ResolveActivityHotkey(settings.TradeHotkeyModifiers, settings.TradeHotkeyKey, WindowsAPI.VK_1,
                out tradeHotkeyModifiers, out tradeHotkeyVirtualKey, "trade");
            ResolveActivityHotkey(settings.EngineeringHotkeyModifiers, settings.EngineeringHotkeyKey, WindowsAPI.VK_2,
                out engineeringHotkeyModifiers, out engineeringHotkeyVirtualKey, "engineering");
            ResolveActivityHotkey(settings.ExplorationHotkeyModifiers, settings.ExplorationHotkeyKey, WindowsAPI.VK_3,
                out explorationHotkeyModifiers, out explorationHotkeyVirtualKey, "exploration");
            ResolveActivityHotkey(settings.MiningHotkeyModifiers, settings.MiningHotkeyKey, WindowsAPI.VK_4,
                out miningHotkeyModifiers, out miningHotkeyVirtualKey, "mining");
        }

        private void ResolveActivityHotkey(string modifiersText, string keyText, uint fallbackKey,
            out uint modifiers, out uint virtualKey, string activity)
        {
            if (TryResolveHotkey(modifiersText, keyText, out modifiers, out virtualKey)) return;
            modifiers = WindowsAPI.MOD_CONTROL;
            virtualKey = fallbackKey;
            Logger.Logger.Warning($"Invalid {activity} hotkey ({modifiersText}+{keyText}); using its default Ctrl+number shortcut.");
        }

        private bool TryResolveHotkey(string modifiersText, string keyText, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;

            modifiers = modifiersText switch
            {
                "Ctrl" => WindowsAPI.MOD_CONTROL,
                "Shift" => WindowsAPI.MOD_SHIFT,
                "Alt" => WindowsAPI.MOD_ALT,
                "Ctrl+Shift" => WindowsAPI.MOD_CONTROL | WindowsAPI.MOD_SHIFT,
                "Ctrl+Alt" => WindowsAPI.MOD_CONTROL | WindowsAPI.MOD_ALT,
                "Alt+Shift" => WindowsAPI.MOD_ALT | WindowsAPI.MOD_SHIFT,
                _ => 0
            };

            if (modifiers == 0)
            {
                return false;
            }

            if (!Enum.TryParse<Key>(keyText, true, out var key))
            {
                return false;
            }

            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk <= 0)
            {
                return false;
            }

            virtualKey = (uint)vk;
            return true;
        }

        private void RefreshExperimentalDssLifecycle(
            bool enabled)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        () =>
                            RefreshExperimentalDssLifecycle(
                                enabled)));

                return;
            }

            if (disposed)
            {
                return;
            }

            if (!enabled)
            {
                if (dssPrototypeController is not null)
                {
                    dssPrototypeController.Dispose();
                    dssPrototypeController = null;

                    Logger.Logger.Info(
                        "Experimental DSS assistant disabled; capture/CV lifecycle stopped.");
                }

                DssAssistantStateService.Instance.Clear();

                return;
            }

            if (dssPrototypeController is null)
            {
                dssPrototypeController =
                    new DssPrototypeController(
                        () => targetWindow,
                        Dispatcher);

                Logger.Logger.Info(
                    "Experimental DSS assistant enabled; lifecycle started.");
            }
        }

        private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
        {
            bool hotkeysChanged = false;

            if (TryResolveHotkey(e.Settings.ToggleHotkeyModifiers, e.Settings.ToggleHotkeyKey, out var newToggleModifiers, out var newToggleVirtualKey)
                && (newToggleModifiers != toggleHotkeyModifiers || newToggleVirtualKey != toggleHotkeyVirtualKey))
            {
                toggleHotkeyModifiers = newToggleModifiers;
                toggleHotkeyVirtualKey = newToggleVirtualKey;
                hotkeysChanged = true;
            }

            if (TryResolveHotkey(e.Settings.InteractiveHotkeyModifiers, e.Settings.InteractiveHotkeyKey, out var newInteractiveModifiers, out var newInteractiveVirtualKey)
                && (newInteractiveModifiers != interactiveHotkeyModifiers || newInteractiveVirtualKey != interactiveHotkeyVirtualKey))
            {
                interactiveHotkeyModifiers = newInteractiveModifiers;
                interactiveHotkeyVirtualKey = newInteractiveVirtualKey;
                hotkeysChanged = true;
            }

            hotkeysChanged |= UpdateResolvedHotkey(e.Settings.TradeHotkeyModifiers, e.Settings.TradeHotkeyKey,
                ref tradeHotkeyModifiers, ref tradeHotkeyVirtualKey);
            hotkeysChanged |= UpdateResolvedHotkey(e.Settings.EngineeringHotkeyModifiers, e.Settings.EngineeringHotkeyKey,
                ref engineeringHotkeyModifiers, ref engineeringHotkeyVirtualKey);
            hotkeysChanged |= UpdateResolvedHotkey(e.Settings.ExplorationHotkeyModifiers, e.Settings.ExplorationHotkeyKey,
                ref explorationHotkeyModifiers, ref explorationHotkeyVirtualKey);
            hotkeysChanged |= UpdateResolvedHotkey(e.Settings.MiningHotkeyModifiers, e.Settings.MiningHotkeyKey,
                ref miningHotkeyModifiers, ref miningHotkeyVirtualKey);

            interactionModeEnabled = e.Settings.EnableInteractionMode;
            autoReturnTimeoutSeconds = NormalizeAutoReturnTimeout(e.Settings.AutoReturnTimeoutSeconds);
            returnOnFocusLoss = e.Settings.ReturnOnFocusLoss;
            showCursorWhenInteractive = e.Settings.ShowCursorWhenInteractive;
            pinnedRouteOverlay?.SetPlacement(e.Settings.PinnedRoutePosition);
            engineeringOverlayWindow?.SetPlacement(GetEngineeringOverlayPlacement());
            activityWorkspaceWindow?.SetPlacement(GetEngineeringOverlayPlacement());
            pinnedRouteOverlay?.SetChromeStyle(e.Settings.OverlayChromeStyle);
            tradeRouteWindow?.SetChromeStyle(e.Settings.OverlayChromeStyle);
            resultsOverlayWindow?.SetChromeStyle(e.Settings.OverlayChromeStyle);
            engineeringOverlayWindow?.SetChromeStyle(e.Settings.OverlayChromeStyle);
            activityWorkspaceWindow?.SetChromeStyle(e.Settings.OverlayChromeStyle);
            SetChromeStyle(e.Settings.OverlayChromeStyle);

            RefreshExperimentalDssLifecycle(
                e.Settings.EnableExperimentalDssAssistant);

            if (!interactionModeEnabled && interactiveModeActive)
            {
                SetInteractiveMode(false, "disabled from settings");
            }

            UpdateOverlayInteractionModes();
            UpdateInteractionStatusUi();

            if (hotkeysChanged)
            {
                UnregisterGlobalHotkeys();
                SetupGlobalHotkeys();
                Logger.Logger.Info("Global hotkeys reconfigured from settings");
            }
        }

        private void OnJournalStateChanged(object? sender, GameStateChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateJournalStatusUi(e.State)));
        }

        private bool UpdateResolvedHotkey(string modifiersText, string keyText, ref uint modifiers, ref uint virtualKey)
        {
            if (!TryResolveHotkey(modifiersText, keyText, out uint resolvedModifiers, out uint resolvedKey)
                || (resolvedModifiers == modifiers && resolvedKey == virtualKey))
            {
                return false;
            }
            modifiers = resolvedModifiers;
            virtualKey = resolvedKey;
            return true;
        }

        private void UpdateJournalStatusUi(
            GameStateSnapshot state)
        {
            // The compact controller no longer duplicates system/location data.
        }
        private int NormalizeAutoReturnTimeout(int value)
        {
            return value switch
            {
                0 or 5 or 8 or 10 or 15 => value,
                _ => 8
            };
        }

        private void SetInteractiveMode(bool isActive, string reason)
        {
            if (exclusiveOverlayInteraction && !isActive)
            {
                return;
            }

            if (interactiveModeActive == isActive)
            {
                return;
            }

            interactiveModeActive = isActive;
            if (interactiveModeActive)
            {
                interactiveModeEnteredAtUtc = DateTime.UtcNow;
                interactiveFocusLossGraceUntilUtc = interactiveModeEnteredAtUtc + InteractiveFocusLossGracePeriod;

                // Foreground-exclusive DirectInput needs one of our overlay
                // windows to own foreground focus before acquisition.
                FocusInteractiveOverlayWindow();
            }

            UpdateOverlayInteractionModes();
            UpdateInteractionStatusUi();
            Logger.Logger.Info($"Interactive mode {(interactiveModeActive ? "ENABLED" : "DISABLED")} ({reason})");
        }

        private void UpdateOverlayInteractionModes()
        {
            bool canInteract = exclusiveOverlayInteraction || (interactionModeEnabled && interactiveModeActive);
            bool showCursor = exclusiveOverlayInteraction || showCursorWhenInteractive;
            AppSettings settings = SettingsService.Instance.Settings;
            x52OverlayPointerController.Enabled = canInteract && settings.EnableX52Support;

            tradeRouteWindow?.ApplyInteractionMode(canInteract, showCursor);
            resultsOverlayWindow?.ApplyInteractionMode(canInteract, showCursor);
            pinnedRouteOverlay?.ApplyInteractionMode(canInteract, showCursor);
            engineeringOverlayWindow?.ApplyInteractionMode(canInteract, showCursor);
            activityWorkspaceWindow?.ApplyInteractionMode(canInteract, showCursor);
            shipStatusOverlayWindow?.ApplyInteractionMode(canInteract, showCursor);
            WindowsAPI.SetClickThrough(this, !canInteract);
            if (!canInteract || !showCursor)
            {
                WindowsAPI.RestoreCursorVisibility();
            }
            bool overlaySettingsVisible = OperatingSystem.IsWindows()
                && Application.Current is App app
                && app.ActiveOverlaySettingsWindow?.IsVisible == true;
            if (canInteract == false && !overlaySettingsVisible)
            {
                WindowsAPI.TryActivateWindow(targetWindow);
            }
        }

        private void UpdateInteractionStatusUi()
        {
            if (InteractionStatusBadge == null
                || CollapsedInteractionStatusBadge == null
                || InteractionHintText == null)
            {
                return;
            }

            bool canInteract =
                exclusiveOverlayInteraction
                || (interactionModeEnabled
                    && interactiveModeActive);

            string stateText =
                canInteract
                    ? Loc.Get(
                        "Loc_INTERACTIVE")
                    : Loc.Get(
                        "Loc_PASSIVE");

            InteractionStatusBadge.Text =
                stateText;

            CollapsedInteractionStatusBadge.Text =
                stateText;

            InteractionStatusBadge.Background =
                chromeStyle == OverlayChromeStyles.Minimal
                    ? Brushes.Transparent
                    : canInteract
                        ? new SolidColorBrush(
                            Color.FromArgb(
                                180,
                                180,
                                95,
                                0))
                        : new SolidColorBrush(
                            Color.FromArgb(
                                120,
                                70,
                                25,
                                0));

            string interactionHotkeyText =
                FormatHotkeyDisplay(
                    SettingsService.Instance.Settings.InteractiveHotkeyModifiers,
                    SettingsService.Instance.Settings.InteractiveHotkeyKey);

            string toggleHotkeyText =
                FormatHotkeyDisplay(
                    SettingsService.Instance.Settings.ToggleHotkeyModifiers,
                    SettingsService.Instance.Settings.ToggleHotkeyKey);

            string overlaysStateText =
                overlaysSuppressedByHotkey
                    ? Loc.Get(
                        "Loc_HIDDEN")
                    : ActivityOptions
                        .First(
                            option =>
                                option.Activity
                                == currentActivity)
                        .Label;

            InteractionHintText.Text =
                Loc.Format(
                    "Loc_Main_Hint_Format",
                    toggleHotkeyText,
                    overlaysStateText,
                    interactionHotkeyText);
        }
        private void SetChromeStyle(string? value)
        {
            chromeStyle = OverlayChromeStyles.Normalize(value);
            OverlayChromeHelper.Apply(OverlayFrame, chromeStyle);
            UpdateInteractionStatusUi();
        }

        private static string FormatHotkeyDisplay(string modifiers, string key)
        {
            if (key.StartsWith("D", StringComparison.OrdinalIgnoreCase) && key.Length == 2 && char.IsDigit(key[1]))
            {
                return $"{modifiers}+{key[1]}";
            }

            return $"{modifiers}+{key}";
        }

        private void EvaluateInteractiveAutoReturn(IntPtr foregroundWindow)
        {
            if (!interactiveModeActive || exclusiveOverlayInteraction)
            {
                return;
            }

            bool focusIsOnInteractiveOverlay = IsWindowFocused(this, foregroundWindow)
                || IsWindowFocused(tradeRouteWindow, foregroundWindow)
                || IsWindowFocused(resultsOverlayWindow, foregroundWindow)
                || IsWindowFocused(pinnedRouteOverlay, foregroundWindow)
                || IsWindowFocused(engineeringOverlayWindow, foregroundWindow)
                || IsWindowFocused(activityWorkspaceWindow, foregroundWindow)
                || (OperatingSystem.IsWindows()
                    && Application.Current is App app
                    && IsWindowFocused(app.ActiveOverlaySettingsWindow, foregroundWindow));

            bool gracePeriodActive = DateTime.UtcNow < interactiveFocusLossGraceUntilUtc;
            if (returnOnFocusLoss && !gracePeriodActive && !focusIsOnInteractiveOverlay)
            {
                SetInteractiveMode(false, "focus loss");
                return;
            }

            if (autoReturnTimeoutSeconds > 0
                && (DateTime.UtcNow - interactiveModeEnteredAtUtc).TotalSeconds >= autoReturnTimeoutSeconds)
            {
                SetInteractiveMode(false, $"timeout {autoReturnTimeoutSeconds}s");
            }
        }

        public void BeginExclusiveOverlayInteraction()
        {
            if (exclusiveOverlayInteraction)
            {
                return;
            }

            interactionStateBeforeExclusiveOverlay = interactiveModeActive;
            exclusiveOverlayInteraction = true;
            interactiveModeActive = true;
            interactiveModeEnteredAtUtc = DateTime.UtcNow;
            interactiveFocusLossGraceUntilUtc = DateTime.MaxValue;
            if (activityWorkspaceWindow?.IsVisible == true)
            {
                activityWorkspaceWindow.Activate();
            }
            else
            {
                engineeringOverlayWindow?.Activate();
            }

            UpdateOverlayInteractionModes();
            UpdateInteractionStatusUi();
            Logger.Logger.Info("Exclusive overlay interaction enabled for a full overlay assistant.");
        }

        public void EndExclusiveOverlayInteraction()
        {
            if (!exclusiveOverlayInteraction)
            {
                return;
            }

            bool restoreInteractive = interactionStateBeforeExclusiveOverlay;
            exclusiveOverlayInteraction = false;
            interactiveModeActive = restoreInteractive;
            interactiveModeEnteredAtUtc = DateTime.UtcNow;
            interactiveFocusLossGraceUntilUtc = interactiveModeEnteredAtUtc + InteractiveFocusLossGracePeriod;
            UpdateOverlayInteractionModes();
            UpdateInteractionStatusUi();
            if (!restoreInteractive)
            {
                WindowsAPI.TryActivateWindow(targetWindow);
            }
            Logger.Logger.Info($"Exclusive overlay interaction ended; restored interactive={restoreInteractive}.");
        }

        public void ReturnControlToGameForNavigation()
        {
            exclusiveOverlayInteraction =
                false;

            interactionStateBeforeExclusiveOverlay =
                false;

            interactiveModeActive =
                false;

            interactiveModeEnteredAtUtc =
                DateTime.UtcNow;

            interactiveFocusLossGraceUntilUtc =
                interactiveModeEnteredAtUtc;

            UpdateOverlayInteractionModes();
            UpdateInteractionStatusUi();

            WindowsAPI.RestoreCursorVisibility();

            bool focused =
                WindowsAPI.TryActivateWindow(
                    targetWindow);

            Logger.Logger.Info(
                $"Navigation handoff returned control to Elite: focused={focused}, target={targetWindow}.");
        }

        private static bool IsWindowFocused(Window? window, IntPtr foregroundWindow)
        {
            if (window == null || !window.IsLoaded)
            {
                return false;
            }

            var handle = new WindowInteropHelper(window).Handle;
            return handle != IntPtr.Zero && handle == foregroundWindow;
        }

        private IntPtr GetInteractiveInputWindowHandle()
        {
            IntPtr foreground = WindowsAPI.GetForegroundWindow();
            return WindowsAPI.IsOverlayWindow(foreground)
                ? foreground
                : IntPtr.Zero;
        }

        private void FocusInteractiveOverlayWindow()
        {
            try
            {
                if (IsVisible)
                {
                    Activate();
                    return;
                }

                if (resultsOverlayWindow?.IsVisible == true)
                {
                    resultsOverlayWindow.Activate();
                    return;
                }

                if (tradeRouteWindow?.IsVisible == true)
                {
                    tradeRouteWindow.Activate();
                    return;
                }

                if (pinnedRouteOverlay?.IsVisible == true)
                {
                    pinnedRouteOverlay.Activate();
                    return;
                }

                if (engineeringOverlayWindow?.IsVisible == true)
                {
                    engineeringOverlayWindow.Activate();
                    return;
                }

                if (activityWorkspaceWindow?.IsVisible == true)
                {
                    activityWorkspaceWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning($"Failed to focus interactive overlay window: {ex.Message}");
            }
        }

    }
}
