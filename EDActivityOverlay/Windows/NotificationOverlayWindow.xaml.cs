using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Notifications;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class NotificationOverlayWindow : Window
{
    public sealed record NotificationView(
        OverlayNotification Source,
        string Title,
        string Message,
        string Severity,
        string ChromeStyle,
        DateTimeOffset ExpiresUtc);

    private readonly ObservableCollection<NotificationView> notifications = [];
    private readonly DispatcherTimer timer;
    private IntPtr targetWindow;
    private bool disposed;

    public ObservableCollection<NotificationView> Notifications => notifications;

    public NotificationOverlayWindow(IntPtr targetWindow)
    {
        InitializeComponent();
        this.targetWindow = targetWindow;
        DataContext = this;
        Loaded += (_, _) =>
        {
            WindowsAPI.SetupOverlayWindow(this);
            WindowsAPI.SetClickThrough(this, true);
            PositionOverlay();
        };
        NotificationCenterService.Instance.NotificationPublished += OnNotificationPublished;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += Timer_Tick;
        timer.Start();
    }

    public void SetTargetWindow(IntPtr value) => targetWindow = value;

    public void RefreshLocalization()
    {
        for (int index = 0; index < notifications.Count; index++)
        {
            NotificationView current = notifications[index];
            notifications[index] = CreateView(current.Source);
        }
    }

    private void OnNotificationPublished(object? sender, OverlayNotificationEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnNotificationPublished(sender, e));
            return;
        }

        notifications.Insert(0, CreateView(e.Notification));
        while (notifications.Count > 3)
        {
            notifications.RemoveAt(notifications.Count - 1);
        }
        UpdatePresentation();
    }

    private static NotificationView CreateView(OverlayNotification source)
    {
        string message = source.Arguments.Length == 0
            ? Loc.Get(source.MessageKey)
            : Loc.Format(source.MessageKey, source.Arguments);
        string severity = Loc.Get(source.Severity switch
        {
            NotificationSeverity.Success => "Loc_Notification_Severity_Success",
            NotificationSeverity.Warning => "Loc_Notification_Severity_Warning",
            NotificationSeverity.Critical => "Loc_Notification_Severity_Critical",
            _ => "Loc_Notification_Severity_Information"
        });
        return new NotificationView(source, Loc.Get(source.TitleKey), message, severity,
            OverlayChromeStyles.Normalize(SettingsService.Instance.Settings.OverlayChromeStyle),
            source.CreatedUtc + source.Duration);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshLocalization();
            UpdatePresentation();
        }));

    private void Timer_Tick(object? sender, EventArgs e)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = notifications.Count - 1; index >= 0; index--)
        {
            if (notifications[index].ExpiresUtc <= now)
            {
                notifications.RemoveAt(index);
            }
        }
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        bool targetReady = targetWindow != IntPtr.Zero
            && WindowsAPI.IsWindow(targetWindow)
            && WindowsAPI.IsWindowVisible(targetWindow)
            && !WindowsAPI.IsIconic(targetWindow);
        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        bool focused = foreground == targetWindow || WindowsAPI.IsOverlayWindow(foreground);
        if (notifications.Count == 0 || OverlayVisibilityState.SuppressAll || !targetReady || !focused)
        {
            if (IsVisible) Hide();
            return;
        }

        PositionOverlay();
        if (!IsVisible) Show();
        WindowsAPI.SetTopmost(this, true);
    }

    private void PositionOverlay()
    {
        if (!WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect)) return;
        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        double left = rect.Left + ((rect.Right - rect.Left - Width) / 2d);
        AppSettings settings = SettingsService.Instance.Settings;
        double top = settings.EnableShipStatusWidget
                     && settings.ShipStatusWidgetPosition.Equals("TopCenter", StringComparison.OrdinalIgnoreCase)
            ? rect.Top + 118
            : rect.Top + 72;
        OverlayLayoutHelper.ClampPosition(ref left, ref top, Width, Math.Max(ActualHeight, 80), workArea, 10, 10);
        Left = left;
        Top = top;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!disposed)
        {
            disposed = true;
            timer.Stop();
            timer.Tick -= Timer_Tick;
            NotificationCenterService.Instance.NotificationPublished -= OnNotificationPublished;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        }
        base.OnClosed(e);
    }
}
