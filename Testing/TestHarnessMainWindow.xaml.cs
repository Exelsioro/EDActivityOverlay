using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Drawing;
using ED_Inara_Overlay;
using ED_Inara_Overlay.Utils;
using Application = System.Windows.Application;
using WinForms = System.Windows.Forms;

namespace OverlayTestHarness
{
    [SupportedOSPlatform("windows6.1")]
    public partial class TestHarnessMainWindow : Window
    {
        private Form? mockTargetWindow;
        private IntPtr mockTargetHandle;
        private uint mockTargetProcessId;
        private ED_Inara_Overlay.MainWindow? overlayWindow;
        private DispatcherTimer? focusAutomationTimer;
        private bool automationRunning = false;
        private bool focusToggleState = false;
        private AutomationElement? targetAutomationElement;
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        public TestHarnessMainWindow()
        {
            InitializeComponent();
            
            IntervalSlider.ValueChanged += IntervalSlider_ValueChanged;
            LogMessage("Test Harness initialized. Ready to create mock target window.");
            LogTextBox.TextChanged += (s, e) => LogScrollViewer.ScrollToBottom();
        }

        private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            IntervalText.Text = $"{(int)e.NewValue}s";
            
            if (focusAutomationTimer != null && automationRunning)
            {
                focusAutomationTimer.Interval = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        private void CreateMockWindowButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create a mock target window using WinForms
                mockTargetWindow = new Form
                {
                    Text = "Mock Elite Dangerous Window",
                    Size = new System.Drawing.Size(800, 600),
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(100, 100),
                    FormBorderStyle = FormBorderStyle.Sizable,
                    ShowInTaskbar = true,
                    TopMost = false
                };
                
                // Add some content to make it look like a game window
                var label = new System.Windows.Forms.Label
                {
                    Text = "Mock Elite Dangerous Window\n\nThis window simulates the target process.\nFocus changes will be automated to test overlay behavior.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = System.Drawing.Color.Black,
                    ForeColor = System.Drawing.Color.White,
                    Font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold)
                };
                mockTargetWindow.Controls.Add(label);
                
                // Show the window
                mockTargetWindow.Show();
                
                // Get window handle and process info
                mockTargetHandle = mockTargetWindow.Handle;
                mockTargetProcessId = (uint)Process.GetCurrentProcess().Id;
                
                // Set up UI Automation element
                targetAutomationElement = AutomationElement.FromHandle(mockTargetHandle);
                
                LogMessage($"Mock target window created successfully!");
                LogMessage($"Window Handle: {mockTargetHandle}");
                LogMessage($"Process ID: {mockTargetProcessId}");
                
                // Update UI state
                CreateMockWindowButton.IsEnabled = false;
                StartOverlayButton.IsEnabled = true;
                StatusText.Text = "Mock window created";
                StatusText.Foreground = System.Windows.Media.Brushes.Yellow;
            }
            catch (Exception ex)
            {
                LogMessage($"Error creating mock window: {ex.Message}");
                StatusText.Text = "Error creating mock window";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private async void StartOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (mockTargetWindow == null)
                {
                    LogMessage("No mock target window available. Create one first.");
                    return;
                }
                
                LogMessage("Starting overlay application...");
                
                // Start a new process to simulate the target (using notepad as a simple example)
                var processInfo = new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                
                var mockProcess = Process.Start(processInfo);
                if (mockProcess != null)
                {
                    // Wait a moment for the process to start
                    await Task.Delay(1000);
                    
                    // Create overlay targeting the notepad process
                    overlayWindow = new ED_Inara_Overlay.MainWindow("notepad");
                    overlayWindow.Show();
                    
                    LogMessage($"Overlay started targeting process: notepad (PID: {mockProcess.Id})");
                    
                    // Update UI state
                    StartOverlayButton.IsEnabled = false;
                    StartAutomationButton.IsEnabled = true;
                    StatusText.Text = "Overlay running";
                    StatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    LogMessage("Failed to start mock target process");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting overlay: {ex.Message}");
                StatusText.Text = "Error starting overlay";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void StartAutomationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!automationRunning)
            {
                StartFocusAutomation();
            }
            else
            {
                StopFocusAutomation();
            }
        }

        private void StartFocusAutomation()
        {
            try
            {
                // Create timer for focus automation
                focusAutomationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(IntervalSlider.Value)
                };
                focusAutomationTimer.Tick += FocusAutomationTimer_Tick;
                focusAutomationTimer.Start();
                
                automationRunning = true;
                StartAutomationButton.Content = "Stop Focus Automation";
                StatusText.Text = "Automation running";
                StatusText.Foreground = System.Windows.Media.Brushes.Cyan;
                
                LogMessage("Focus automation started. Will alternate focus between target and test harness.");
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting focus automation: {ex.Message}");
            }
        }

        private void StopFocusAutomation()
        {
            if (focusAutomationTimer != null)
            {
                focusAutomationTimer.Stop();
                focusAutomationTimer = null;
            }
            
            automationRunning = false;
            StartAutomationButton.Content = "Start Focus Automation";
            StatusText.Text = "Automation stopped";
            StatusText.Foreground = System.Windows.Media.Brushes.Yellow;
            
            LogMessage("Focus automation stopped.");
        }

        private void FocusAutomationTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (mockTargetWindow != null && targetAutomationElement != null)
                {
                    // Toggle focus between target window and test harness
                    if (focusToggleState)
                    {
                        // Focus on mock target window
                        SetForegroundWindow(mockTargetHandle);
                        ShowWindow(mockTargetHandle, SW_RESTORE);
                        LogMessage("Focus set to mock target window");
                    }
                    else
                    {
                        // Focus on test harness
                        this.Activate();
                        LogMessage("Focus set to test harness");
                    }
                    
                    focusToggleState = !focusToggleState;
                    
                    // Log current foreground window for debugging
                    var currentForeground = GetForegroundWindow();
                    LogMessage($"Current foreground window: {currentForeground}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in focus automation: {ex.Message}");
            }
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}\n";
            
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText(logEntry);
                LogScrollViewer.ScrollToBottom();
            });
            
            // Also log to the logger system
            Logger.Logger.Info($"TestHarness: {message}");
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up resources
            StopFocusAutomation();
            
            // Close overlay window
            if (overlayWindow != null)
            {
                overlayWindow.Close();
                overlayWindow = null;
            }
            
            // Close mock target window
            if (mockTargetWindow != null)
            {
                mockTargetWindow.Close();
                mockTargetWindow.Dispose();
                mockTargetWindow = null;
            }
            
            LogMessage("Test harness shutting down. All resources cleaned up.");
            
            base.OnClosed(e);
        }
    }
}
