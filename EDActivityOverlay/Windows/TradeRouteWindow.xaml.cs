using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Utils;
using System.Windows.Interop;
using System.Windows.Media;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services;

namespace EDActivityOverlay.Windows
{
    /// <summary>
    /// Trade Route Window - allows searching and displaying trade routes
    /// </summary>
    public partial class TradeRouteWindow : Window
    {
        private DispatcherTimer? updateTimer;
        private bool disposed;
        private IntPtr targetWindow;
        private uint targetProcessId;
        private readonly double baseWindowWidth;
        private double lastAppliedScale = 1.0;
        private bool isSearchInProgress;
        private bool interactiveModeEnabled;
        private bool showCursorWhenInteractive;
        private bool applyingJournalValues;
        private bool systemOverridden;
        private bool cargoOverridden;
        private string chromeStyle = OverlayChromeStyles.Compact;

        public TradeRouteWindow(Window owner)
        {
            Owner = owner;
            InitializeComponent();
            baseWindowWidth = Width;
            SetChromeStyle(Services.SettingsService.Instance.Settings.OverlayChromeStyle);

#if DEBUG
            TestDataButton.Visibility = Visibility.Visible;
#else
            Grid.SetColumnSpan(SearchButton, 3);
#endif

            SetupOverlay();
            SetupUpdateTimer();
            NearStarSystemTextBox.TextChanged += (_, _) =>
            {
                if (!applyingJournalValues) systemOverridden = true;
            };
            CargoCapacityTextBox.TextChanged += (_, _) =>
            {
                if (!applyingJournalValues) cargoOverridden = true;
            };
            JournalMonitorService.Instance.StateChanged += OnJournalStateChanged;
            ApplyJournalState(JournalMonitorService.Instance.Current);
            Logger.Logger.Info("TradeRouteWindow constructor completed");
        }

        private void OnJournalStateChanged(object? sender, GameStateChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyJournalState(e.State)));
        }

        private void ApplyJournalState(GameStateSnapshot state, bool force = false)
        {
            if (!state.JournalAvailable)
            {
                JournalSourceText.Text = Loc.Get("Loc_JOURNAL_NOT_FOUND");
                JournalDetailsText.Text = Loc.Get("Loc_Check_the_Journal_directory_in_Settings");
                return;
            }

            JournalSourceText.Text = state.IsLive ? Loc.Get("Loc_JOURNAL_LIVE") : Loc.Get("Loc_JOURNAL_CONNECTED");
            string ship = string.IsNullOrWhiteSpace(state.ShipName) ? state.Ship : state.ShipName;
            string location = string.IsNullOrWhiteSpace(state.StarSystem) ? Loc.Get("Loc_waiting_for_location") : state.StarSystem;
            string cargo = state.CargoCapacity > 0
                ? Loc.Format("Loc_Free_Cargo_Format", state.FreeCargo, state.CargoCapacity)
                : Loc.Get("Loc_cargo_unknown");
            JournalDetailsText.Text = $"{location}  •  {(string.IsNullOrWhiteSpace(ship) ? Loc.Get("Loc_ship_unknown") : ship)}  •  {cargo}";

            applyingJournalValues = true;
            try
            {
                if ((force || !systemOverridden) && !string.IsNullOrWhiteSpace(state.StarSystem))
                {
                    NearStarSystemTextBox.Text = state.StarSystem;
                }
                if ((force || !cargoOverridden) && state.CargoCapacity > 0)
                {
                    CargoCapacityTextBox.Text = state.FreeCargo.ToString();
                }
            }
            finally
            {
                applyingJournalValues = false;
            }
        }

        public void RefreshLocalization() => ApplyJournalState(JournalMonitorService.Instance.Current, force: true);

        private void UseJournalValuesButton_Click(object sender, RoutedEventArgs e)
        {
            systemOverridden = false;
            cargoOverridden = false;
            ApplyJournalState(JournalMonitorService.Instance.Current, force: true);
        }
        
        private void SetupOverlay()
        {
            // Set up overlay behavior when window is loaded
            this.Loaded += (s, e) =>
            {
                WindowsAPI.SetupOverlayWindow(this);
                ApplyInteractionMode(interactiveModeEnabled, showCursorWhenInteractive);
                Logger.Logger.Info("TradeRouteWindow overlay setup completed");
            };
        }

        public void ApplyInteractionMode(bool interactive, bool showCursor)
        {
            interactiveModeEnabled = interactive;
            showCursorWhenInteractive = showCursor;
            WindowsAPI.SetClickThrough(this, !interactiveModeEnabled);

            if (interactiveModeEnabled && showCursorWhenInteractive && IsVisible)
            {
                WindowsAPI.EnsureCursorVisibleOnWindow(this);
            }
        }

        public void SetChromeStyle(string? value)
        {
            chromeStyle = OverlayChromeStyles.Normalize(value);
            OverlayChromeHelper.Apply(OverlayFrame, chromeStyle);
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

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (disposed || updateTimer == null)
                return;

            if (OverlayVisibilityState.SuppressAll || OverlayVisibilityState.SuppressActivity)
            {
                if (this.IsVisible)
                {
                    this.Hide();
                }
                return;
            }

            // Update window position to stay relative to target window
            if (targetWindow != IntPtr.Zero)
            {
                // Check if target window still exists
                if (!WindowsAPI.IsWindow(targetWindow))
                {
                    this.Hide();
                    return;
                }

                // Update position to follow target window at right center
                try
                {
                    if (WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT targetRect))
                    {
                        ApplyAdaptiveSizeForTarget(targetRect);
                        var workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
                        PositionAtTargetRightCenter(targetRect, workArea, allowLeftFallbackWhenOverflow: false, logDetails: false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Logger.Error($"Error updating TradeRouteWindow position: {ex.Message}");
                }
                
                // Check target window focus and visibility state
                IntPtr foregroundWindow = WindowsAPI.GetForegroundWindow();
                bool targetHasFocus = (foregroundWindow == targetWindow);
                bool overlayHasFocus = WindowsAPI.IsOverlayWindow(foregroundWindow);
                bool targetMinimized = WindowsAPI.IsIconic(targetWindow);
                bool targetVisible = WindowsAPI.IsWindowVisible(targetWindow);
                
                // This window should be visible when the target window is focused and visible
                // OR when any overlay window has focus
                bool shouldBeVisible = targetVisible && !targetMinimized && (targetHasFocus || overlayHasFocus);
                
                // Set topmost only when target or overlay has focus
                bool shouldBeTopmost = targetHasFocus || overlayHasFocus;
                
                if (!shouldBeVisible && this.IsVisible)
                {
                    Logger.Logger.Info("TradeRouteWindow hiding - target window lost focus or is not visible");
                    this.Hide();
                }
                else if (shouldBeVisible && !this.IsVisible)
                {
                    Logger.Logger.Info("TradeRouteWindow showing - target window has focus and is visible");
                    this.Show();
                }
                
                // Apply topmost state conditionally using WindowInteropHelper
                if (this.IsVisible && this.IsLoaded)
                {
                    WindowsAPI.SetTopmost(this, shouldBeTopmost);
                }
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // Remote route search is deliberately disabled while the application
            // moves from INARA to a provider-neutral market-data implementation.
            // Keep the trading workspace, Journal integration, test data and
            // pinned-route workflow intact.
            StatusText.Text = "Trade route search is temporarily unavailable while a new market-data provider is being integrated.";
            Logger.Logger.Info("Trade route search requested while remote provider is disabled.");
        }
        
        private void TestDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (isSearchInProgress)
            {
                return;
            }

            StatusText.Text = Loc.Get("Loc_Loading_test_data");
            Logger.Logger.Info("Loading test data for trade routes...");

            // Unpin any existing pinned route overlay before showing new results
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.UnpinRouteOverlay();
                Logger.Logger.Info("Unpinned existing route overlay before loading test data");
            }

            try
            {
                var testRoutes = TestDataGenerator.GenerateTestData();
                StatusText.Text = Loc.Format("Loc_Loaded_Test_Routes_Format", testRoutes.Count);
                Logger.Logger.Info($"Loaded {testRoutes.Count} test trade routes.");
                
                // Show results in overlay window
                ((MainWindow)Owner).ShowResultsOverlay(testRoutes);
            }
            catch (Exception ex)
            {
                StatusText.Text = Loc.Get("Loc_Error_loading_test_data");
                Logger.Logger.Error($"Error loading test data: {ex.Message}");
            }
        }

        private void SetSearchControlsEnabled(bool enabled)
        {
            SearchButton.IsEnabled = enabled;
            TestDataButton.IsEnabled = enabled;
        }

        private void ShowFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle visibility of additional filters
            if (AdditionalFiltersGroupBox.Visibility == Visibility.Collapsed)
            {
                AdditionalFiltersGroupBox.Visibility = Visibility.Visible;
                ShowFiltersButton.Content = Loc.Get("Loc_ADVANCED_FILTERS_OPEN");
                Logger.Logger.Info("Additional filters shown");
            }
            else
            {
                AdditionalFiltersGroupBox.Visibility = Visibility.Collapsed;
                ShowFiltersButton.Content = Loc.Get("Loc_ADVANCED_FILTERS");
                Logger.Logger.Info("Additional filters hidden");
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Notify main window if owner is set
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.OnTradeRouteWindowClosed();
            }
            Close();
        }

        public void SetTargetWindow(IntPtr window, uint pid)
        {
            targetWindow = window;
            targetProcessId = pid;
            
            Logger.Logger.Info($"TradeRouteWindow.SetTargetWindow: window={window}, pid={pid}");
            
            // Position window at a safe visible location
            try
            {
                // Get screen working area to ensure window is visible
                var workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
                
                // Position at right side of screen initially (for testing)
                this.Left = workArea.Right - this.Width - 50; // Right side with margin
                this.Top = Math.Max(workArea.Top + 50, workArea.Top + ((workArea.Height - this.Height) / 2));
                
                Logger.Logger.Info($"TradeRouteWindow positioned at screen right: Left={this.Left}, Top={this.Top}, WorkArea={workArea.Width}x{workArea.Height}");
                
                // If we have a target window, calculate right center position
                if (targetWindow != IntPtr.Zero)
                {
                    Logger.Logger.Info($"Target window found: {targetWindow}, attempting relative positioning");
                }
                else
                {
                    Logger.Logger.Info("No target window set, keeping center position");
                }
                
                if (targetWindow != IntPtr.Zero)
                {
                    try
                    {
                        if (WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT targetRect))
                        {
                            ApplyAdaptiveSizeForTarget(targetRect);
                            PositionAtTargetRightCenter(targetRect, workArea, allowLeftFallbackWhenOverflow: true, logDetails: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Logger.Error($"Error calculating right center position: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error positioning TradeRouteWindow: {ex.Message}");
            }
        }

        public void StopUpdateTimer()
        {
            updateTimer?.Stop();
        }

        private string GetMaxRouteDistanceApiValue()
        {
            int distance = (Math.Clamp(MaxRouteDistanceComboBox.SelectedIndex, 0, 7) + 1) * 10;
            return $"{distance} Ly";
        }

        private string GetMaxPriceAgeApiValue()
        {
            string[] values = ["8 hours", "16 hours", "1 day", "2 days", "3 days"];
            return values[Math.Clamp(MaxPriceAgeComboBox.SelectedIndex, 0, values.Length - 1)];
        }

        private int GetComboBoxIndex(ComboBox comboBox)
        {
            if (comboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                return comboBox.SelectedIndex;
            }
            return 0;
        }

        private void ApplyAdaptiveSizeForTarget(WindowsAPI.RECT targetRect)
        {
            double targetWidth = targetRect.Right - targetRect.Left;
            double targetHeight = targetRect.Bottom - targetRect.Top;

            OverlayLayoutHelper.TryApplyAdaptiveSize(
                this,
                baseWindowWidth,
                null,
                targetWidth,
                targetHeight,
                OverlayLayoutSettings.TradeWindowMinScale,
                OverlayLayoutSettings.TradeWindowMaxScale,
                ref lastAppliedScale);
        }

        private void PositionAtTargetRightCenter(WindowsAPI.RECT targetRect, Rect workArea, bool allowLeftFallbackWhenOverflow, bool logDetails)
        {
            const double gapSize = OverlayLayoutSettings.DefaultGap;
            const double margin = OverlayLayoutSettings.DefaultMargin;

            var (newLeft, newTop) = OverlayLayoutHelper.GetRightCenteredPosition(targetRect, this.Width, this.Height, gapSize);

            if (logDetails)
            {
                double targetWidth = targetRect.Right - targetRect.Left;
                double targetHeight = targetRect.Bottom - targetRect.Top;
                Logger.Logger.Info($"Calculated right center position: Target=({targetRect.Left},{targetRect.Top},{targetRect.Right},{targetRect.Bottom}), TargetSize={targetWidth}x{targetHeight}, Calculated=({newLeft},{newTop}), WindowSize=({this.Width}x{this.Height})");
            }

            if (allowLeftFallbackWhenOverflow)
            {
                bool fitsOnRight = (newLeft + this.Width) <= workArea.Right;
                if (logDetails)
                {
                    Logger.Logger.Info($"Right side fit check: newLeft={newLeft}, windowWidth={this.Width}, total={newLeft + this.Width}, workAreaRight={workArea.Right}, fitsOnRight={fitsOnRight}");
                }

                if (!fitsOnRight)
                {
                    newLeft = targetRect.Left - this.Width - gapSize;
                    if (logDetails)
                    {
                        Logger.Logger.Info($"Right side doesn't fit, trying left side: newLeft={newLeft}");
                    }
                }

                if (newLeft < workArea.Left)
                {
                    newLeft = workArea.Left + margin;
                    if (logDetails)
                    {
                        Logger.Logger.Info($"Left side doesn't fit either, using screen edge: newLeft={newLeft}");
                    }
                }
            }

            OverlayLayoutHelper.ClampPosition(ref newLeft, ref newTop, this.Width, this.Height, workArea, margin, margin);
            this.Left = newLeft;
            this.Top = newTop;

            if (logDetails)
            {
                Logger.Logger.Info($"TradeRouteWindow positioned at right center: Final=({this.Left},{this.Top}), WindowSize=({this.Width},{this.Height}), WorkArea=({workArea.Width},{workArea.Height})");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            disposed = true;
            updateTimer?.Stop();
            JournalMonitorService.Instance.StateChanged -= OnJournalStateChanged;
            base.OnClosed(e);
        }
    }
}



