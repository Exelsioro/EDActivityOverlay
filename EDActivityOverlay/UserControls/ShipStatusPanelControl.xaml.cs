using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

/// <summary>
/// Reusable ship-status presentation surface. Window-specific visibility,
/// positioning and HWND behavior intentionally stay outside this control so the
/// same presentation can be hosted by the desktop shell and the VR composite.
/// </summary>
public partial class ShipStatusPanelControl : UserControl, IDisposable
{
    private readonly DispatcherTimer refreshTimer;
    private string currentSystem = string.Empty;
    private string nextSystem = string.Empty;
    private bool scoCooldownRefreshActive;
    private bool disposed;

    public event EventHandler? PreferredHeightChanged;
    public event EventHandler? DragRequested;

    public double PreferredHeight { get; private set; } = 92;

    public ShipStatusPanelControl()
    {
        InitializeComponent();
        JournalMonitorService.Instance.StateChanged += OnStateChanged;

        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        refreshTimer.Tick += RefreshTimer_Tick;
        refreshTimer.Start();

        ApplySettings(SettingsService.Instance.Settings);
        Refresh(JournalMonitorService.Instance.Current);
    }

    public void ApplySettings(AppSettings settings) =>
        OverlayChromeHelper.Apply(
            OverlayFrame,
            settings.OverlayChromeStyle);

    public void RefreshLocalization() =>
        Refresh(JournalMonitorService.Instance.Current);

    public void SetDragCursor(bool enabled) =>
        DragHandle.Cursor = enabled
            ? Cursors.SizeAll
            : Cursors.Arrow;

    private void OnStateChanged(
        object? sender,
        GameStateChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(() => Refresh(e.State)));
            return;
        }

        Refresh(e.State);
    }

    private void RefreshTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        GameStateSnapshot liveState =
            JournalMonitorService.Instance.Current;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool scoCooldownActive =
            liveState.GetScoCooldownRemainingSeconds(now) > 0;

        if (scoCooldownActive || scoCooldownRefreshActive)
        {
            Refresh(liveState, now);
        }
    }

    private void Refresh(
        GameStateSnapshot state,
        DateTimeOffset? now = null)
    {
        ShipStatusPresentation view =
            ShipStatusPresentationBuilder.Build(state);

        currentSystem = view.CurrentSystem;
        nextSystem = view.NextSystem;

        CurrentSystemText.Text =
            string.IsNullOrWhiteSpace(currentSystem)
                ? Loc.Get("Loc_WAITING_FOR_GAME")
                : currentSystem.ToUpperInvariant();

        DateTimeOffset displayUtc =
            now ?? DateTimeOffset.UtcNow;

        string driveStatus =
            FsdScoStatusPresentation.BuildOverlay(
                state,
                displayUtc);
        bool hasDriveStatus =
            !string.IsNullOrWhiteSpace(driveStatus);

        DriveStatusText.Text =
            hasDriveStatus ? driveStatus : "→";
        DriveStatusText.FontSize =
            hasDriveStatus ? 9 : 16;

        scoCooldownRefreshActive =
            state.GetScoCooldownRemainingSeconds(displayUtc) > 0;

        CurrentSystemCaptionText.Text =
            string.IsNullOrWhiteSpace(view.CurrentStarClass)
                ? Loc.Get("Loc_CURRENT_SYSTEM_SHORT")
                : Loc.Format(
                    "Loc_SHIP_STATUS_CURRENT_CAPTION_FORMAT",
                    view.CurrentStarClass,
                    view.CurrentStarScoopable
                        ? Loc.Get("Loc_SCOOPABLE_SHORT")
                        : Loc.Get("Loc_NOT_SCOOPABLE_SHORT"));

        NextSystemText.Text =
            string.IsNullOrWhiteSpace(nextSystem)
                ? Loc.Get("Loc_ROUTE_NOT_PLOTTED")
                : nextSystem.ToUpperInvariant();

        RouteCaptionText.Text =
            view.RemainingJumps > 0
                ? Loc.Format(
                    "Loc_SHIP_STATUS_ROUTE_CAPTION_FORMAT",
                    view.RemainingJumps,
                    view.NextStarClass,
                    view.NextStarScoopable
                        ? Loc.Get("Loc_SCOOPABLE_SHORT")
                        : Loc.Get("Loc_NOT_SCOOPABLE_SHORT"))
                : Loc.Get("Loc_NEXT_SYSTEM_SHORT");

        AdvisoryText.Text = view.Advisory switch
        {
            ShipStatusAdvisoryKind.FuelCritical =>
                Loc.Format(
                    "Loc_SHIP_STATUS_FUEL_CRITICAL_FORMAT",
                    view.FuelPercent),
            ShipStatusAdvisoryKind.FuelCaution =>
                Loc.Format(
                    "Loc_SHIP_STATUS_FUEL_CAUTION_FORMAT",
                    view.FuelPercent),
            ShipStatusAdvisoryKind.NoScoopableStars =>
                Loc.Get("Loc_FUEL_NO_SCOOPABLE_ON_ROUTE"),
            ShipStatusAdvisoryKind.HazardousNextStar =>
                Loc.Format(
                    "Loc_SHIP_STATUS_HAZARDOUS_STAR_FORMAT",
                    view.NextSystem,
                    view.NextStarClass),
            _ => view.RemainingJumps > 0
                ? Loc.Format(
                    "Loc_SHIP_STATUS_ROUTE_OK_FORMAT",
                    view.NextSystem,
                    view.NextStarClass)
                : Loc.Get("Loc_SHIP_STATUS_NO_ROUTE")
        };

        AdvisoryPanel.Visibility =
            view.Advisory == ShipStatusAdvisoryKind.None
                ? Visibility.Collapsed
                : Visibility.Visible;

        double preferredHeight =
            view.Advisory == ShipStatusAdvisoryKind.None
                ? 58
                : 92;

        if (Math.Abs(PreferredHeight - preferredHeight) > 0.1)
        {
            PreferredHeight = preferredHeight;
            Height = preferredHeight;
            PreferredHeightChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Height = preferredHeight;
        }

        AdvisoryPanel.BorderBrush =
            (System.Windows.Media.Brush)FindResource(
                view.Advisory is
                    ShipStatusAdvisoryKind.FuelCritical
                    or ShipStatusAdvisoryKind.HazardousNextStar
                    ? "FailureColorBrush"
                    : "AccentColorBrush");
    }

    private void DragHandle_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CurrentSystemText_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        Copy(currentSystem);

    private void NextSystemText_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        Copy(nextSystem);

    private static void Copy(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Clipboard.SetText(value);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        refreshTimer.Stop();
        refreshTimer.Tick -= RefreshTimer_Tick;
        JournalMonitorService.Instance.StateChanged -= OnStateChanged;
    }
}
