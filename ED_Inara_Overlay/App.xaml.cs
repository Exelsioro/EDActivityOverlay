using System;
using System.Windows;
using ED_Inara_Overlay.Windows;
using ED_Inara_Overlay.Utils;
using ED_Inara_Overlay.Services;
using ED_Inara_Overlay.Services.Engineering;
using ED_Inara_Overlay.Services.Exploration;
using ED_Inara_Overlay.Services.Journal;
using ED_Inara_Overlay.Services.Notifications;
using ED_Inara_Overlay.Services.Hardware;
using System.Runtime.Versioning;
using System.Windows.Threading;

namespace ED_Inara_Overlay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class App : Application
    {
        private string targetProcessName = "EliteDangerous64";
        private WaitingWindow? waitingWindow;
        private MainWindow? mainWindow;
        private TrayIconService? trayIconService;
        private EngineeringWindow? engineeringWindow;
        private SettingsWindow? settingsWindow;

        internal SettingsWindow? ActiveOverlaySettingsWindow =>
            settingsWindow is { IsLoaded: true, IsOverlayMode: true } ? settingsWindow : null;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Get target process from command line args or default to notepad
            if (e.Args.Length > 0)
            {
                targetProcessName = e.Args[0];
            }

            Logger.Logger.Info($"Application starting with target process: {targetProcessName}");
            LocalizationService.Instance.Initialize(SettingsService.Instance.Settings.Language);
            InitializeTrayIcon();

            var settings = SettingsService.Instance.Settings;
            EngineeringService.Instance.Start();
            NotificationCenterService.Instance.Start();
            ExplorationDataService.Instance.Start();
            ExplorationHistoryService.Instance.Start(settings.JournalDirectory);
            ExplorationEarningsService.Instance.Start(settings.JournalDirectory);
            ExplorationLogService.Instance.Start();
            ExplorationRouteService.Instance.Start();
            ExplorationPoiService.Instance.Start();
            X52IntegrationService.Instance.Start();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            if (settings.EnableJournalIntegration)
            {
                JournalMonitorService.Instance.Start(settings.JournalDirectory);
            }

            // Initialize theme system
            try
            {
                ThemeManager.Instance.LoadAvailableThemes();
                if (ThemeManager.Instance.CurrentTheme != null)
                {
                    // Apply the loaded theme (saved or default)
                    ThemeManager.Instance.ApplyTheme(ThemeManager.Instance.CurrentTheme);
                    Logger.Logger.Info("Theme system initialized successfully");
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error initializing theme system: {ex.Message}");
            }

            // Always show waiting window first, regardless of target process status
            // This gives users control over when to start the overlay
            Logger.Logger.Info($"Starting application - showing waiting window for target process: {targetProcessName}");
            ShowWaitingWindow();
        }

        private void ShowWaitingWindow()
        {
            if (waitingWindow != null)
            {
                if (!waitingWindow.IsVisible)
                {
                    waitingWindow.Show();
                }
                waitingWindow.Activate();
                return;
            }

            Logger.Logger.Info("Creating and showing WaitingWindow");
            
            waitingWindow = new WaitingWindow(targetProcessName);
            waitingWindow.TargetProcessFound += OnTargetProcessFound;
            waitingWindow.Show();
            
            Logger.Logger.Info("WaitingWindow displayed");
        }

        private void OnTargetProcessFound(object? sender, string processName)
        {
            Logger.Logger.Info($"Target process found event received: {processName}");
            
            if (waitingWindow != null)
            {
                waitingWindow.Hide();
            }
            
            // Start main overlay
            this.Dispatcher.BeginInvoke(new Action(() => 
            {
                StartMainOverlay();
            }));
        }

        private void StartMainOverlay()
        {
            Logger.Logger.Info($"Starting main overlay for target process: {targetProcessName}");
            
            try
            {
                if (mainWindow != null)
                {
                    Logger.Logger.Info("Main overlay is already running. Activating existing instance.");
                    mainWindow.EnsureVisibleAfterTargetDetection();
                    trayIconService?.ShowWaitingHint();
                    return;
                }

                // Create main window (starts hidden)
                mainWindow = new MainWindow(targetProcessName);
                
                // Set the main window as the shutdown target
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                this.MainWindow = mainWindow;
                
                // Ensure it will be visible after target detection
                mainWindow.EnsureVisibleAfterTargetDetection();
                
                // Note: MainWindow starts hidden and will show when target has focus
                Logger.Logger.Info("Main overlay window created and displayed with forced visibility");
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error starting main overlay: {ex.Message}");
                
                // Show error message and shutdown
                MessageBox.Show(
                    Loc.Format("Loc_Failed_to_start_overlay_0", ex.Message),
                    Loc.Get("Loc_ED_Inara_Overlay_Error"),
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                
                this.Shutdown();
            }
        }


        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Logger.Logger.Info("Application is exiting");
                
                // Clean up waiting window
                if (waitingWindow != null)
                {
                    waitingWindow.TargetProcessFound -= OnTargetProcessFound;
                    waitingWindow.Close();
                    waitingWindow = null;
                }
                
                // Clean up main window
                if (mainWindow != null)
                {
                    mainWindow.Close();
                    mainWindow = null;
                }

                if (engineeringWindow != null)
                {
                    engineeringWindow.Close();
                    engineeringWindow = null;
                }

                if (settingsWindow != null)
                {
                    settingsWindow.Close();
                    settingsWindow = null;
                }

                if (trayIconService != null)
                {
                    trayIconService.OpenRequested -= OnTrayOpenRequested;
                    trayIconService.EngineeringRequested -= OnTrayEngineeringRequested;
                    trayIconService.SettingsRequested -= OnTraySettingsRequested;
                    trayIconService.ExitRequested -= OnTrayExitRequested;
                    trayIconService.Dispose();
                    trayIconService = null;
                }

                EngineeringService.Instance.Dispose();
                NotificationCenterService.Instance.Dispose();
                ExplorationDataService.Instance.Dispose();
                ExplorationHistoryService.Instance.Dispose();
                ExplorationEarningsService.Instance.Dispose();
                ExplorationLogService.Instance.Dispose();
                ExplorationRouteService.Instance.Dispose();
                ExplorationPoiService.Instance.Dispose();
                X52IntegrationService.Instance.Dispose();
                JournalMonitorService.Instance.Dispose();
                LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                
                Logger.Logger.Info("Application exit cleanup completed");
            }
            finally
            {
                Logger.Logger.Close();
                base.OnExit(e);
            }
        }

        private void InitializeTrayIcon()
        {
            trayIconService = new TrayIconService();
            trayIconService.OpenRequested += OnTrayOpenRequested;
            trayIconService.EngineeringRequested += OnTrayEngineeringRequested;
            trayIconService.SettingsRequested += OnTraySettingsRequested;
            trayIconService.ExitRequested += OnTrayExitRequested;
            trayIconService.Initialize();
            Logger.Logger.Info("Tray icon initialized.");
        }

        private void OnTrayOpenRequested(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                ShowWaitingWindow();
            }));
        }

        private void OnTrayExitRequested(object? sender, EventArgs e)
        {
            Logger.Logger.Info("Exit requested from tray menu.");
            Shutdown();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                EngineeringService.Instance.RefreshLocalization();
                trayIconService?.RefreshLocalization();
                waitingWindow?.RefreshLocalization();
                mainWindow?.RefreshLocalization();
                engineeringWindow?.RefreshLocalization();
            });
        }

        private void OnTrayEngineeringRequested(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ShowEngineeringWindow));
        }

        public void ShowEngineeringWindow()
        {
            if (mainWindow is { IsLoaded: true }
                && mainWindow.TargetWindowHandle != IntPtr.Zero
                && WindowsAPI.IsWindow(mainWindow.TargetWindowHandle))
            {
                mainWindow.SelectActivity(Models.ActivityType.Engineering);
                return;
            }

            if (engineeringWindow is { IsLoaded: true })
            {
                if (!engineeringWindow.IsVisible)
                {
                    engineeringWindow.Show();
                }
                if (engineeringWindow.WindowState == WindowState.Minimized)
                {
                    engineeringWindow.WindowState = WindowState.Normal;
                }
                engineeringWindow.Activate();
                return;
            }

            engineeringWindow = new EngineeringWindow();
            engineeringWindow.Closed += (_, _) => engineeringWindow = null;
            engineeringWindow.Show();
            engineeringWindow.Activate();
        }

        private void OnTraySettingsRequested(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ShowSettingsWindow));
        }

        public void ShowSettingsWindow()
        {
            ShowSettingsWindowCore(false, IntPtr.Zero);
        }

        public void ShowOverlaySettingsWindow()
        {
            IntPtr targetWindow = mainWindow?.TargetWindowHandle ?? IntPtr.Zero;
            ShowSettingsWindowCore(targetWindow != IntPtr.Zero, targetWindow);
        }

        internal void CloseOverlaySettingsWindow()
        {
            if (settingsWindow is { IsLoaded: true, IsOverlayMode: true })
            {
                settingsWindow.Close();
                settingsWindow = null;
            }
        }

        private void ShowSettingsWindowCore(bool overlayMode, IntPtr targetWindow)
        {
            if (settingsWindow is { IsLoaded: true })
            {
                if (settingsWindow.IsOverlayMode != overlayMode)
                {
                    settingsWindow.Close();
                    settingsWindow = null;
                }
            }

            if (settingsWindow is { IsLoaded: true })
            {
                if (!settingsWindow.IsVisible) settingsWindow.Show();
                if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new SettingsWindow(overlayMode, targetWindow);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        public void ShowTrayWaitingHint()
        {
            trayIconService?.ShowWaitingHint();
            Logger.Logger.Info("Displayed tray notification about tray mode and tray exit.");
        }
    }
}

