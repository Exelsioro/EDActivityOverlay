using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using EDActivityOverlay._0;

namespace MinimalTestHarness
{
    public class MinimalTestHarness
    {
        [STAThread]
        public static async Task Main()
        {
            Console.WriteLine("Starting Minimal Test Harness for ED Inara Overlay");
            
            try
            {
                // Initialize logger
                Logger.Logger.Info("Minimal Test Harness Starting");
                
                // Start a notepad process to serve as our test target
                Console.WriteLine("Starting notepad as test target...");
                var notepadProcess = Process.Start("notepad.exe");
                
                if (notepadProcess != null)
                {
                    // Wait a bit for notepad to fully start
                    await Task.Delay(2000);
                    
                    Console.WriteLine($"Notepad started with PID: {notepadProcess.Id}");
                    Console.WriteLine("Starting overlay targeting notepad...");
                    
                    // Create and show the overlay
                    var overlay = new EDActivityOverlay.MainWindow("notepad");
                    
                    // Start WPF application
                    var app = new System.Windows.Application();
                    app.Run(overlay);
                    
                    Console.WriteLine("Overlay closed. Cleaning up...");
                    
                    // Clean up - close notepad if it's still running
                    if (!notepadProcess.HasExited)
                    {
                        notepadProcess.CloseMainWindow();
                        notepadProcess.WaitForExit(5000);
                        if (!notepadProcess.HasExited)
                        {
                            notepadProcess.Kill();
                        }
                    }
                    notepadProcess.Dispose();
                }
                else
                {
                    Console.WriteLine("Failed to start notepad process");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in test harness: {ex.Message}");
                Logger.Logger.Error($"Test harness error: {ex}");
            }
            
            Console.WriteLine("Test harness completed. Press any key to exit.");
            Console.ReadKey();
        }
    }
}
