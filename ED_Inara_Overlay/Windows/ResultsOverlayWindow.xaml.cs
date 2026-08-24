using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using ED_Inara_Overlay.Models.Trading;
using Logger;
using ED_Inara_Overlay.Utils;
using ED_Inara_Overlay.Services;
using ED_Inara_Overlay.UserControls;

namespace ED_Inara_Overlay.Windows
{
    /// <summary>
    /// Results Overlay Window - displays trade route results in an overlay format
    /// Similar to the original ResultsOverlayForm but using WPF
    /// </summary>
    public partial class ResultsOverlayWindow : Window
    {
        private IntPtr targetWindow;
        private uint targetProcessId;
        private DispatcherTimer? updateTimer;
        private bool disposed = false;
        private MainWindow? parentMainWindow;
        private List<UserControl> tradeRouteControls = new List<UserControl>();
        private bool interactiveModeEnabled;
        private bool showCursorWhenInteractive;
        private string chromeStyle = OverlayChromeStyles.Compact;
        private List<TradeRoute> currentTradeRoutes = new();

        public ResultsOverlayWindow(MainWindow? parentWindow = null)
        {
            parentMainWindow = parentWindow;
            Logger.Logger.Info("Initializing ResultsOverlayWindow");
            
            InitializeComponent();
            SetChromeStyle(Services.SettingsService.Instance.Settings.OverlayChromeStyle);
            SetupOverlay();
            SetupUpdateTimer();
            Logger.Logger.Info("ResultsOverlayWindow initialization complete");
        }

        private void SetupOverlay()
        {
            // Set up overlay behavior when window is loaded
            this.Loaded += (s, e) =>
            {
                WindowsAPI.SetupOverlayWindow(this);
                ApplyInteractionMode(interactiveModeEnabled, showCursorWhenInteractive);
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
            bool minimal = chromeStyle == OverlayChromeStyles.Minimal;
            ResultsFrame.Background = minimal
                ? Brushes.Transparent
                : (Brush)FindResource("SecondaryBackgroundColorBrush");
            ResultsFrame.BorderThickness = minimal ? new Thickness(0) : new Thickness(1);
            ResultsScrollViewer.Background = minimal
                ? Brushes.Transparent
                : (Brush)FindResource("SecondaryBackgroundColorBrush");
            foreach (TradeRouteCard card in tradeRouteControls.OfType<TradeRouteCard>())
            {
                card.SetChromeStyle(chromeStyle);
            }
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

            // If form is not visible, we still need to update position for when it becomes visible
            if (targetWindow != IntPtr.Zero)
            {
                // Check if target window still exists
                if (!WindowsAPI.IsWindow(targetWindow))
                {
                    this.Hide();
                    this.Close();
                    return;
                }

                // Always update overlay position to match target window (positioned at top center)
                PositionOverlay();

                // Check if target window has focus
                IntPtr foregroundWindow = WindowsAPI.GetForegroundWindow();
                bool targetHasFocus = (foregroundWindow == targetWindow);
                bool overlayHasFocus = WindowsAPI.IsOverlayWindow(foregroundWindow);

                // Check if target window is minimized or not visible
                bool targetMinimized = WindowsAPI.IsIconic(targetWindow);
                bool targetVisible = WindowsAPI.IsWindowVisible(targetWindow);

                // Determine if overlay should be visible based on target window state and focus
                // Should be visible if target has focus OR any overlay window has focus
                bool shouldBeVisible = targetVisible && !targetMinimized && (targetHasFocus || overlayHasFocus);
                
                // Set topmost only when target or overlay has focus
                bool shouldBeTopmost = targetHasFocus || overlayHasFocus;

                if (shouldBeVisible && !this.IsVisible)
                {
                    Logger.Logger.Info("ResultsOverlayWindow showing - target window has focus and is visible");
                    this.Show();
                }
                else if (!shouldBeVisible && this.IsVisible)
                {
                    Logger.Logger.Info("ResultsOverlayWindow hiding - target window lost focus or is not visible");
                    this.Hide();
                }
                
                // Apply topmost state conditionally using WindowInteropHelper
                if (this.IsVisible && this.IsLoaded)
                {
                    WindowsAPI.SetTopmost(this, shouldBeTopmost);
                }

                if (updateTimer != null)
                {
                    updateTimer.Tag = ((int?)updateTimer.Tag ?? 0) + 1;
                }
            }
        }

        private void PositionOverlay()
        {
            if (!WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect))
                return;

            Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);

            // Position results overlay at middle top of the target window
            int targetHeight = rect.Bottom - rect.Top;
            int targetWidth = rect.Right - rect.Left;
            int overlayWidth = Math.Min(
                (int)(workArea.Width * OverlayLayoutSettings.ResultsWidthByMonitor),
                Math.Min(OverlayLayoutSettings.ResultsMaxWidth, (int)(targetWidth * OverlayLayoutSettings.ResultsWidthByTarget)));
            int overlayHeight = Math.Min(
                (int)(workArea.Height * OverlayLayoutSettings.ResultsHeightByMonitor),
                Math.Min(OverlayLayoutSettings.ResultsMaxHeight, targetHeight / OverlayLayoutSettings.ResultsHeightByTargetDivisor));

            // Center horizontally, position in upper portion of window
            var (centerX, topY) = OverlayLayoutHelper.GetTopCenteredPosition(rect, overlayWidth, OverlayLayoutSettings.ResultsTopOffset);
            OverlayLayoutHelper.ClampPosition(
                ref centerX,
                ref topY,
                overlayWidth,
                overlayHeight,
                workArea,
                OverlayLayoutSettings.DefaultMargin,
                OverlayLayoutSettings.DefaultMargin);
            
            this.Left = centerX;
            this.Top = topY;
            this.Width = overlayWidth;
            this.Height = overlayHeight;
        }

        public void SetTargetWindow(IntPtr windowHandle, uint processId)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ResultsOverlayWindow));
                
            targetWindow = windowHandle;
            targetProcessId = processId;

            if (windowHandle != IntPtr.Zero)
            {
                PositionOverlay();
            }
        }
        
        public void StopUpdateTimer()
        {
            if (updateTimer != null && !disposed)
            {
                updateTimer.Stop();
                Logger.Logger.Info("ResultsOverlayWindow update timer stopped");
            }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Logger.Info("Results overlay close button clicked");
            Logger.Logger.LogUserAction("Results overlay closed by user");
            Close();
        }

        public void DisplayTradeRoutes(List<TradeRoute> tradeRoutes)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ResultsOverlayWindow));
                
            currentTradeRoutes = tradeRoutes.ToList();
            ClearTradeRouteControls();

            int displayCount = Math.Min(6, tradeRoutes.Count);
            TitleLabel.Text = tradeRoutes.Count > displayCount
                ? Loc.Format("Loc_Routes_Limited_Format", displayCount, tradeRoutes.Count)
                : Loc.Format("Loc_Routes_Count_Format", tradeRoutes.Count);

            foreach (var tradeRoute in tradeRoutes.Take(6)) // Limit to 6 routes for performance
            {
                var tradeRouteCard = new TradeRouteCard(tradeRoute);
                tradeRouteCard.SetChromeStyle(chromeStyle);
                
                // Ensure card stretches to fill available width
                tradeRouteCard.HorizontalAlignment = HorizontalAlignment.Stretch;
                
                // Subscribe to pin route event
                tradeRouteCard.PinRouteRequested += OnPinRouteRequested;
                
                ResultsPanel.Children.Add(tradeRouteCard);
                tradeRouteControls.Add(tradeRouteCard);
                
                // Force immediate layout update after adding each card
                tradeRouteCard.UpdateLayout();
            }

            // Force layout update to ensure proper scrolling
            ResultsPanel.UpdateLayout();
            
            Logger.Logger.Info($"Displayed {tradeRouteControls.Count} trade routes in results overlay");
        }

        public void RefreshLocalization()
        {
            if (!disposed)
            {
                DisplayTradeRoutes(currentTradeRoutes);
            }
        }
        
        private void OnPinRouteRequested(object? sender, TradeRoute tradeRoute)
        {
            Logger.Logger.Info($"Pin route requested from results overlay: {tradeRoute.CardHeader.FromStation.System} -> {tradeRoute.CardHeader.ToStation.System}");
            
            // Notify parent window to handle the pin request
            parentMainWindow?.OnPinRouteRequested(tradeRoute);
        }


        private void ClearTradeRouteControls()
        {
            foreach (var control in tradeRouteControls)
            {
                // Unsubscribe from events to prevent memory leaks
                if (control is TradeRouteCard card)
                {
                    card.PinRouteRequested -= OnPinRouteRequested;
                }
                ResultsPanel.Children.Remove(control);
            }
            tradeRouteControls.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Ensure parent window state is consistent when form is closing
            parentMainWindow?.OnResultsWindowClosed();
            
            Dispose();
            base.OnClosed(e);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                
                Logger.Logger.Info("Disposing ResultsOverlayWindow resources");

                // Stop and dispose the update timer
                if (updateTimer != null)
                {
                    updateTimer.Stop();
                    updateTimer.Tick -= UpdateTimer_Tick;
                    updateTimer = null;
                }

                // Clear trade route controls
                ClearTradeRouteControls();

                // Mark as disposed
                this.Tag = "disposed";

                // Clear references
                targetWindow = IntPtr.Zero;
                targetProcessId = 0;
                parentMainWindow = null;

                Logger.Logger.Info("ResultsOverlayWindow resources disposed successfully");
            }
        }

    }
}
