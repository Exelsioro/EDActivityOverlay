using System.Windows;
using System.Windows.Interop;
using EDActivityOverlay.Services;
using EDActivityOverlay.Windows;

namespace EDActivityOverlay.Utils;

/// <summary>
/// Owns the selected overlay rendering backend. Individual windows remain the
/// historical desktop backend. Composite owns one stable top-level HWND and is
/// also the only backend that can enable VR compatibility behavior.
/// </summary>
internal static class OverlayRenderCoordinator
{
    private static EDActivityOverlay.MainWindow? controller;
    private static CompositeOverlayWindow? compositeWindow;
    private static int initialized;

    public static bool IsCompositeMode =>
        OverlayRenderModes.IsComposite(
            SettingsService.Instance.Settings.OverlayRenderMode);

    internal static CompositeOverlayWindow? CompositeWindow => compositeWindow;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        Logger.Logger.Info(
            $"Overlay renderer initialized: mode={SettingsService.Instance.Settings.OverlayRenderMode}.");
    }

    internal static void Attach(EDActivityOverlay.MainWindow mainWindow)
    {
        controller = mainWindow;
        ApplyMode();
    }

    internal static void Detach(EDActivityOverlay.MainWindow mainWindow)
    {
        if (!ReferenceEquals(controller, mainWindow))
        {
            return;
        }

        CloseComposite();
        controller = null;
    }

    internal static void RefreshMode() => ApplyMode();

    internal static void RefreshLocalization() =>
        compositeWindow?.RefreshLocalization();

    internal static void ApplyInteractionMode(
        bool interactive,
        bool showCursor) =>
        compositeWindow?.ApplyInteractionMode(
            interactive,
            showCursor);

    internal static void ActivateComposite()
    {
        if (compositeWindow is { IsLoaded: true, IsVisible: true })
        {
            compositeWindow.Activate();
        }
    }

    internal static bool IsCompositeWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero
            || compositeWindow is not { IsLoaded: true })
        {
            return false;
        }

        return new WindowInteropHelper(compositeWindow).Handle == handle;
    }

    private static void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.BeginInvoke(new Action(ApplyMode));
            return;
        }

        ApplyMode();
    }

    private static void ApplyMode()
    {
        if (!IsCompositeMode)
        {
            CloseComposite();
            return;
        }

        if (controller is null)
        {
            return;
        }

        if (compositeWindow is { IsLoaded: true })
        {
            compositeWindow.RefreshWindowMode();
            return;
        }

        var window = new CompositeOverlayWindow(controller);
        compositeWindow = window;
        window.Closed += OnCompositeClosed;
        window.Show();
        Logger.Logger.Info("Composite overlay renderer opened.");
    }

    private static void OnCompositeClosed(
        object? sender,
        EventArgs e)
    {
        if (sender is not CompositeOverlayWindow window)
        {
            return;
        }

        window.Closed -= OnCompositeClosed;
        if (ReferenceEquals(compositeWindow, window))
        {
            compositeWindow = null;
        }
    }

    private static void CloseComposite()
    {
        if (compositeWindow is null)
        {
            return;
        }

        CompositeOverlayWindow closing = compositeWindow;
        compositeWindow = null;
        closing.Closed -= OnCompositeClosed;
        closing.Close();
        Logger.Logger.Info("Composite overlay renderer closed.");
    }
}
