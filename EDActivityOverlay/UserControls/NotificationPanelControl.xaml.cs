using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Threading;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Notifications;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

/// <summary>
/// Reusable notification presentation surface. It owns notification view state,
/// but has no knowledge of target HWNDs or desktop/VR visibility policy.
/// </summary>
public partial class NotificationPanelControl : UserControl, IDisposable
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
    private bool disposed;

    public event EventHandler? ContentChanged;

    public ObservableCollection<NotificationView> Notifications =>
        notifications;

    public bool HasNotifications =>
        notifications.Count > 0;

    public NotificationPanelControl()
    {
        InitializeComponent();
        DataContext = this;

        NotificationCenterService.Instance.NotificationPublished +=
            OnNotificationPublished;
        SettingsService.Instance.SettingsChanged +=
            OnSettingsChanged;

        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        timer.Tick += Timer_Tick;
        timer.Start();
    }

    public void RefreshLocalization()
    {
        for (int index = 0; index < notifications.Count; index++)
        {
            NotificationView current = notifications[index];
            notifications[index] = CreateView(current.Source);
        }

        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNotificationPublished(
        object? sender,
        OverlayNotificationEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                () => OnNotificationPublished(sender, e));
            return;
        }

        notifications.Insert(0, CreateView(e.Notification));
        while (notifications.Count > 3)
        {
            notifications.RemoveAt(notifications.Count - 1);
        }

        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static NotificationView CreateView(
        OverlayNotification source)
    {
        string message = source.Arguments.Length == 0
            ? Loc.Get(source.MessageKey)
            : Loc.Format(source.MessageKey, source.Arguments);

        string severity = Loc.Get(source.Severity switch
        {
            NotificationSeverity.Success =>
                "Loc_Notification_Severity_Success",
            NotificationSeverity.Warning =>
                "Loc_Notification_Severity_Warning",
            NotificationSeverity.Critical =>
                "Loc_Notification_Severity_Critical",
            _ => "Loc_Notification_Severity_Information"
        });

        return new NotificationView(
            source,
            Loc.Get(source.TitleKey),
            message,
            severity,
            OverlayChromeStyles.Normalize(
                SettingsService.Instance.Settings.OverlayChromeStyle),
            source.CreatedUtc + source.Duration);
    }

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(RefreshLocalization));
            return;
        }

        RefreshLocalization();
    }

    private void Timer_Tick(
        object? sender,
        EventArgs e)
    {
        bool changed = false;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int index = notifications.Count - 1; index >= 0; index--)
        {
            if (notifications[index].ExpiresUtc <= now)
            {
                notifications.RemoveAt(index);
                changed = true;
            }
        }

        if (changed)
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= Timer_Tick;
        NotificationCenterService.Instance.NotificationPublished -=
            OnNotificationPublished;
        SettingsService.Instance.SettingsChanged -=
            OnSettingsChanged;
    }
}
