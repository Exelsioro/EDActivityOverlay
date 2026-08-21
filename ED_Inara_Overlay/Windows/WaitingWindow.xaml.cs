using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;
using ED_Inara_Overlay.Utils;
using ED_Inara_Overlay.Services;

namespace ED_Inara_Overlay.Windows
{
    /// <summary>
    /// Waiting window that shows while target application is not running
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class WaitingWindow : Window
    {
        private readonly string targetProcessName;
        private DispatcherTimer? checkTimer;
        private bool shouldClose = false;
        private bool targetFound = false; // Track if closure is due to target being found
        private bool targetProcessRunning = false; // Track if target process is currently running
        private bool startRequested = false;
        private bool overlayStartTriggered = false;

        public event EventHandler<string>? TargetProcessFound;

        public WaitingWindow(string processName = "notepad")
        {
            InitializeComponent();
            
            targetProcessName = processName;
            
            Logger.Logger.Info($"WaitingWindow initialized for target process: {processName}");
            
            SetupUI();
            StartMonitoring();
        }

        private void SetupUI()
        {
            // Update UI based on target process

            string displayName = GetDisplayName(targetProcessName);
            //TargetAppName.Text = displayName;
            //TargetAppName.Foreground = new System.Windows.Media.SolidColorBrush(
            //    System.Windows.Media.Color.FromRgb(0, 255, 0)); // Green
            
            Logger.Logger.Info($"WaitingWindow UI configured for: {displayName}");
        }

        private string GetDisplayName(string processName)
        {
            return processName.ToLower() switch
            {
                "notepad" => "Notepad",
                "elitedangerous64" => "Elite Dangerous",
                "elitedangerous32" => "Elite Dangerous",
                "steam" => "Steam",
                _ => processName
            };
        }

        private void StartMonitoring()
        {
            checkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // Check every second
            };
            
            checkTimer.Tick += CheckTimer_Tick;
            checkTimer.Start();
            
            Logger.Logger.Info($"Started monitoring for target process: {targetProcessName}");
        }

        private void CheckTimer_Tick(object? sender, EventArgs e)
        {
            if (shouldClose || checkTimer == null)
                return;

            try
            {
                // Check if target process is running
                var process = WindowsAPI.FindProcessByName(targetProcessName);
                
                if (process != null)
                {
                    if (!targetProcessRunning)
                    {
                        Logger.Logger.Info($"Target process found: {targetProcessName} (PID: {process.Id})");
                        targetProcessRunning = true;
                        
                        // Update UI to show target is available
                        UpdateStatusTargetFound();
                    }

                    if (startRequested && !overlayStartTriggered)
                    {
                        TriggerOverlayStart();
                    }
                }
                else
                {
                    if (targetProcessRunning)
                    {
                        Logger.Logger.Info($"Target process {targetProcessName} is no longer running");
                        targetProcessRunning = false;
                        
                        // Update UI to show we're looking again
                        UpdateStatus();
                    }
                    else
                    {
                        // Update status to show we're still looking
                        UpdateStatus();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error checking for target process: {ex.Message}");
            }
        }

        private void UpdateStatus()
        {
            // Show different status based on target process availability
            if (targetProcessRunning)
            {
                StatusText.Text = Loc.Get("Loc_Elite_Dangerous_detected_The_overlay_is_ready");
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
            }
            else
            {
                // Add some visual feedback that we're actively searching
                var now = DateTime.Now;
                var dots = (now.Second % 4) switch
                {
                    0 => "",
                    1 => ".",
                    2 => "..",
                    _ => "..."
                };
                
                StatusText.Text = Loc.Format("Loc_Welcome_Commander_Format", dots);
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextColorBrush");
            }
        }
        
        private void UpdateStatusTargetFound()
        {
            if (startRequested)
            {
                StatusText.Text = Loc.Get("Loc_Game_found_Starting_overlay");
            }
            else
            {
                StatusText.Text = Loc.Get("Loc_Game_found_Click_Start_to_continue");
            }
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
        }

        public void RefreshLocalization() => UpdateStatus();
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Logger.Info("User clicked Exit Overlay button in WaitingWindow");
            Logger.Logger.LogUserAction("Exit button clicked in waiting window", new { TargetProcess = targetProcessName });
            
            shouldClose = true;
            
            // Stop monitoring
            if (checkTimer != null)
            {
                checkTimer.Stop();
                checkTimer = null;
            }
            
            // Close the application
            Application.Current.Shutdown();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                app.ShowSettingsWindow();
            }
        }

        private void KofiLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Logger.Logger.Info($"User clicked Ko-fi link: {e.Uri}");
                Logger.Logger.LogUserAction("Ko-fi link clicked", new { Uri = e.Uri.ToString() });
                
                // Open the URL in the default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error opening Ko-fi link: {ex.Message}");
            }
        }

        private void StartOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Logger.Info("User clicked Start Overlay button in WaitingWindow");
            Logger.Logger.LogUserAction("Start Overlay button clicked in waiting window", new { TargetProcess = targetProcessName });
            startRequested = true;

            if (targetProcessRunning)
            {
                TriggerOverlayStart();
                return;
            }

            StatusText.Text = Loc.Get("Loc_Waiting_for_the_game_The_overlay_will_start_automatically");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextColorBrush");

            if (Application.Current is App app)
            {
                app.ShowTrayWaitingHint();
            }
            this.Hide();
        }

        private void TriggerOverlayStart()
        {
            if (overlayStartTriggered)
            {
                return;
            }

            overlayStartTriggered = true;
            shouldClose = true;

            if (checkTimer != null)
            {
                checkTimer.Stop();
                checkTimer = null;
            }

            Logger.Logger.Info("Auto-start conditions met. Triggering overlay startup.");
            TargetProcessFound?.Invoke(this, targetProcessName);
            this.Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger.Logger.Info($"WaitingWindow is closing - targetFound: {targetFound}, shouldClose: {shouldClose}");
            
            // Stop monitoring
            if (checkTimer != null)
            {
                checkTimer.Stop();
                checkTimer = null;
            }
            
            // Only shutdown application if window was closed manually (not due to target being found)
            if (!shouldClose || (!targetFound && shouldClose))
            {
                Logger.Logger.Info("WaitingWindow closed by user - shutting down application");
                Application.Current.Shutdown();
            }
            else if (targetFound)
            {
                Logger.Logger.Info("WaitingWindow closed due to target process found - transitioning to main overlay");
                // Don't shutdown - let the app transition to main overlay
            }
            
            base.OnClosed(e);
        }

        /// <summary>
        /// Manually close the waiting window (called when target process is found)
        /// </summary>
        public void CloseWaitingWindow()
        {
            Logger.Logger.Info("CloseWaitingWindow called - target was found");
            targetFound = true;
            shouldClose = true;
            
            if (checkTimer != null)
            {
                checkTimer.Stop();
                checkTimer = null;
            }
            
            this.Close();
        }
    }
}
