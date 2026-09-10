using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using EDActivityOverlay.Services;
using EDActivityOverlay.Windows;

namespace EDActivityOverlay.Utils;

internal enum VrOverlayWindowRole
{
    Main,
    ActivityWorkspace,
    ShipStatus,
    Notifications,
    PinnedRoute,
    Results,
    TradeRoute,
    Engineering,
    Settings,
    Dss,
    Waiting,
    X52Help,
    Composite,
    Other
}

internal sealed record VrOverlayWindowSnapshot(
    VrOverlayWindowRole Role,
    IntPtr Handle,
    string Title,
    bool IsVisible,
    bool ShowInTaskbar,
    bool AllowsTransparency);

/// <summary>
/// Makes every WPF top-level window in this process discoverable to desktop
/// capture tools while VR compatibility mode is enabled. This deliberately does
/// not introduce a Meta/OpenXR dependency: the VR runtime remains responsible
/// for selecting, pinning, scaling and positioning the captured HWNDs.
/// </summary>
internal static class VrOverlaySupport
{
    private sealed class OriginalWindowState(
        string title,
        bool showInTaskbar,
        double opacity)
    {
        public string Title { get; } = title;
        public bool ShowInTaskbar { get; } = showInTaskbar;
        public double Opacity { get; } = opacity;
        public bool ClosedHooked { get; set; }
        public bool VrApplied { get; set; }
    }

    private sealed record Registration(
        VrOverlayWindowRole Role,
        WeakReference<Window> Window);

    private static readonly ConditionalWeakTable<Window, OriginalWindowState> OriginalStates = new();
    private static readonly Dictionary<IntPtr, Registration> Registrations = new();
    private static readonly object Sync = new();
    private static VrCompositeOverlayWindow? compositeWindow;
    private static int initialized;

    public static bool IsEnabled =>
        SettingsService.Instance.Settings.EnableVrOverlaySupport;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));

        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        ApplyToOpenWindows();

        Logger.Logger.Info(
            $"VR overlay support initialized: enabled={IsEnabled}.");
    }

    public static IReadOnlyList<VrOverlayWindowSnapshot> Snapshot()
    {
        lock (Sync)
        {
            var result = new List<VrOverlayWindowSnapshot>();
            var stale = new List<IntPtr>();

            foreach ((IntPtr handle, Registration registration) in Registrations)
            {
                if (!registration.Window.TryGetTarget(out Window? window))
                {
                    stale.Add(handle);
                    continue;
                }

                result.Add(new VrOverlayWindowSnapshot(
                    registration.Role,
                    handle,
                    window.Title,
                    window.IsVisible,
                    window.ShowInTaskbar,
                    window.AllowsTransparency));
            }

            foreach (IntPtr handle in stale)
            {
                Registrations.Remove(handle);
            }

            return result;
        }
    }

    internal static (VrOverlayWindowRole Role, string Label) DescribeWindow(
        string typeName) =>
        typeName switch
        {
            "MainWindow" => (VrOverlayWindowRole.Main, "Main"),
            "ActivityWorkspaceOverlayWindow" => (VrOverlayWindowRole.ActivityWorkspace, "Activity"),
            "ShipStatusOverlayWindow" => (VrOverlayWindowRole.ShipStatus, "Ship Status"),
            "NotificationOverlayWindow" => (VrOverlayWindowRole.Notifications, "Notifications"),
            "PinnedRouteOverlay" => (VrOverlayWindowRole.PinnedRoute, "Pinned Route"),
            "ResultsOverlayWindow" => (VrOverlayWindowRole.Results, "Results"),
            "TradeRouteWindow" => (VrOverlayWindowRole.TradeRoute, "Trade Route"),
            "EngineeringWindow" => (VrOverlayWindowRole.Engineering, "Engineering"),
            "SettingsWindow" => (VrOverlayWindowRole.Settings, "Settings"),
            "DssPrototypeOverlayWindow" => (VrOverlayWindowRole.Dss, "DSS"),
            "WaitingWindow" => (VrOverlayWindowRole.Waiting, "Waiting"),
            "X52ControlHelpWindow" => (VrOverlayWindowRole.X52Help, "X52 Help"),
            "VrCompositeOverlayWindow" => (VrOverlayWindowRole.Composite, "Composite"),
            _ => (VrOverlayWindowRole.Other, typeName)
        };

    internal static bool IsCompositeOwnedRole(VrOverlayWindowRole role) =>
        role is VrOverlayWindowRole.ShipStatus
            or VrOverlayWindowRole.Notifications
            or VrOverlayWindowRole.PinnedRoute;

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            ConfigureWindow(window);
            UpdateCompositeHost();
        }
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
            application.Dispatcher.BeginInvoke(
                new Action(ApplyToOpenWindows));
            return;
        }

        ApplyToOpenWindows();
    }

    private static void ApplyToOpenWindows()
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        foreach (Window window in application.Windows)
        {
            ConfigureWindow(window);
        }

        UpdateCompositeHost();
    }

    private static void UpdateCompositeHost()
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (!IsEnabled)
        {
            if (compositeWindow is not null)
            {
                VrCompositeOverlayWindow closing = compositeWindow;
                compositeWindow = null;
                closing.Close();
                Logger.Logger.Info("VR composite capture host closed.");
            }

            return;
        }

        if (compositeWindow is { IsLoaded: true })
        {
            return;
        }

        EDActivityOverlay.MainWindow? mainWindow =
            application.MainWindow as EDActivityOverlay.MainWindow
            ?? application.Windows
                .OfType<EDActivityOverlay.MainWindow>()
                .FirstOrDefault();

        if (mainWindow is null)
        {
            return;
        }

        var window =
            new VrCompositeOverlayWindow(
                () => mainWindow.TargetWindowHandle);

        compositeWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(compositeWindow, window))
            {
                compositeWindow = null;
            }
        };

        window.Show();
        Logger.Logger.Info("VR composite capture host opened.");
    }

    private static void ConfigureWindow(Window window)
    {
        OriginalWindowState state = OriginalStates.GetValue(
            window,
            current => new OriginalWindowState(
                current.Title ?? string.Empty,
                current.ShowInTaskbar,
                current.Opacity));

        if (!state.ClosedHooked)
        {
            window.Closed += OnWindowClosed;
            state.ClosedHooked = true;
        }

        if (!IsEnabled)
        {
            if (state.VrApplied)
            {
                window.SetCurrentValue(Window.TitleProperty, state.Title);
                window.ShowInTaskbar = state.ShowInTaskbar;
                window.SetCurrentValue(Window.OpacityProperty, state.Opacity);
                state.VrApplied = false;
                RemoveRegistration(window);

                Logger.Logger.Info(
                    $"VR capture window restored: type={window.GetType().Name}.");
            }

            return;
        }

        (VrOverlayWindowRole role, string label) =
            DescribeWindow(window.GetType().Name);
        string captureTitle = $"EDAO VR | {label}";

        window.SetCurrentValue(Window.TitleProperty, captureTitle);

        bool compositeOwned =
            IsCompositeOwnedRole(role);

        window.ShowInTaskbar =
            role == VrOverlayWindowRole.Composite
            || !compositeOwned;

        // During migration, the legacy HWND continues running its existing
        // lifecycle so no production behavior is removed, but its pixels are
        // suppressed. The equivalent reusable panel is rendered by the stable
        // composite capture surface instead.
        window.SetCurrentValue(
            Window.OpacityProperty,
            compositeOwned ? 0d : state.Opacity);

        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            lock (Sync)
            {
                Registrations[handle] =
                    new Registration(
                        role,
                        new WeakReference<Window>(window));
            }
        }

        if (!state.VrApplied)
        {
            state.VrApplied = true;
            Logger.Logger.Info(
                $"VR capture window registered: role={role}, hwnd=0x{handle.ToInt64():X}, " +
                $"title='{captureTitle}', visible={window.IsVisible}, " +
                $"taskbar={window.ShowInTaskbar}, transparent={window.AllowsTransparency}.");
        }
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        RemoveRegistration(window);
    }

    private static void RemoveRegistration(Window window)
    {
        lock (Sync)
        {
            IntPtr[] handles = Registrations
                .Where(pair =>
                    pair.Value.Window.TryGetTarget(out Window? registered)
                    && ReferenceEquals(registered, window))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (IntPtr handle in handles)
            {
                Registrations.Remove(handle);
            }
        }
    }
}
