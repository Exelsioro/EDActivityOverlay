using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow
{
    private static readonly TimeSpan DssPresentationFreshness =
        TimeSpan.FromMilliseconds(700);

    private bool dssHudInitialized;
    private bool dssHudActive;
    private DispatcherTimer? dssHudTimer;

    private Border? dssContextPanel;
    private TextBlock? dssReadinessTitleText;
    private TextBlock? dssAngularText;
    private TextBlock? dssReadinessDetailText;
    private TextBlock? dssTrackingText;

    protected override void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(e);
        EnsureDssHudIntegration();
    }

    private void EnsureDssHudIntegration()
    {
        if (dssHudInitialized)
        {
            return;
        }

        dssHudInitialized = true;

        dssContextPanel =
            BuildDssContextPanel();

        int insertIndex =
            Math.Min(
                1,
                AdaptiveExplorationPanel.Children.Count);

        AdaptiveExplorationPanel.Children.Insert(
            insertIndex,
            dssContextPanel);

        dssHudTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(100)
            };

        dssHudTimer.Tick +=
            DssHudTimer_Tick;

        dssHudTimer.Start();

        Closed +=
            DssHudWindow_Closed;
    }

    private Border BuildDssContextPanel()
    {
        var panel =
            new Border
            {
                Visibility =
                    Visibility.Collapsed,
                Padding =
                    new Thickness(9, 7, 9, 7),
                Margin =
                    new Thickness(0, 0, 0, 7),
                Background =
                    GetDssBrush(
                        "SecondaryBackgroundColorBrush",
                        Brushes.Black),
                BorderBrush =
                    GetDssBrush(
                        "AccentColorBrush",
                        Brushes.DeepSkyBlue),
                BorderThickness =
                    new Thickness(2, 0, 0, 0)
            };

        var root =
            new StackPanel();

        var heading =
            new Grid();

        heading.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        heading.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        dssReadinessTitleText =
            new TextBlock
            {
                FontWeight =
                    FontWeights.Bold,
                FontSize = 13,
                Foreground =
                    GetDssBrush(
                        "AccentColorBrush",
                        Brushes.DeepSkyBlue),
                Text = "DSS CALIBRATING"
            };

        heading.Children.Add(
            dssReadinessTitleText);

        dssAngularText =
            new TextBlock
            {
                Margin =
                    new Thickness(10, 0, 0, 0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                FontSize = 10,
                Foreground =
                    GetDssBrush(
                        "SecondaryTextColorBrush",
                        Brushes.LightGray)
            };

        Grid.SetColumn(
            dssAngularText,
            1);

        heading.Children.Add(
            dssAngularText);

        root.Children.Add(
            heading);

        dssReadinessDetailText =
            new TextBlock
            {
                Margin =
                    new Thickness(0, 5, 0, 0),
                TextWrapping =
                    TextWrapping.Wrap,
                FontSize = 10,
                Foreground =
                    GetDssBrush(
                        "PrimaryTextColorBrush",
                        Brushes.White)
            };

        root.Children.Add(
            dssReadinessDetailText);

        dssTrackingText =
            new TextBlock
            {
                Margin =
                    new Thickness(0, 5, 0, 0),
                TextWrapping =
                    TextWrapping.Wrap,
                FontSize = 9,
                Foreground =
                    GetDssBrush(
                        "MutedTextColorBrush",
                        Brushes.Gray)
            };

        root.Children.Add(
            dssTrackingText);

        panel.Child =
            root;

        return panel;
    }

    private void DssHudTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (disposed
            || activity
               != ActivityType.Exploration)
        {
            DeactivateDssHud();
            return;
        }

        GameStateSnapshot state =
            JournalMonitorService.Instance.Current;

        DssAssistantLiveSnapshot? dss =
            DssAssistantStateService.Instance.Current;

        bool active =
            state.GuiFocus == 10
            && dss is not null
            && dss.IsFresh(
                DateTimeOffset.UtcNow,
                DssPresentationFreshness);

        if (!active
            || dss is null)
        {
            DeactivateDssHud();
            return;
        }

        ActivateDssHud(
            state,
            dss);
    }

    private void ActivateDssHud(
        GameStateSnapshot state,
        DssAssistantLiveSnapshot dss)
    {
        if (dssContextPanel is null
            || dssReadinessTitleText is null
            || dssAngularText is null
            || dssReadinessDetailText is null
            || dssTrackingText is null)
        {
            return;
        }

        dssHudActive = true;

        dssContextPanel.Visibility =
            Visibility.Visible;

        SystemContextPanel.Visibility =
            Visibility.Collapsed;

        BodyContextPanel.Visibility =
            Visibility.Collapsed;

        ExobioContextPanel.Visibility =
            Visibility.Collapsed;

        CompactRouteAlertPanel.Visibility =
            Visibility.Collapsed;

        CompactModeText.Text =
            "DSS";

        CompactContextTitleText.Text =
            !string.IsNullOrWhiteSpace(
                dss.BodyName)
                ? dss.BodyName
                : !string.IsNullOrWhiteSpace(
                    state.DestinationName)
                    ? state.DestinationName
                    : "BODY TARGET";

        CompactQueueCountText.Text =
            dss.DssPatchRadius > 0
                ? $"DSS {dss.DssPatchRadius:0.#}%"
                : string.Empty;

        DssAssistantReadinessSnapshot readiness =
            dss.Readiness;

        dssAngularText.Text =
            readiness.HasAngularMeasurement
                ? $"diam {readiness.AngularDiameterDegrees:0.0}°"
                : string.Empty;

        string distance =
            readiness.HasDistanceEstimate
                ? $"dist≈{FormatDssDistance(readiness.EstimatedCenterDistanceMeters)}"
                : "angular readiness only";

        string range =
            readiness.BodyRadiusMeters > 0
                ? $"ready {FormatDssDistance(readiness.RecommendedNearCenterDistanceMeters)}–" +
                  $"{FormatDssDistance(readiness.RecommendedFarCenterDistanceMeters)} · " +
                  $"target {FormatDssDistance(readiness.RecommendedTargetCenterDistanceMeters)}"
                : string.Empty;

        switch (readiness.State)
        {
            case DssAssistantReadinessState.SelectBodyTarget:
                SetDssReadiness(
                    "SELECT BODY AS TARGET",
                    "Select the planet or moon as the Elite navigation target.",
                    "FailureColorBrush",
                    Brushes.OrangeRed);
                break;

            case DssAssistantReadinessState.NeedBodyRadius:
                SetDssReadiness(
                    "DSS CALIBRATING",
                    "Body radius lookup is still pending.",
                    "AccentColorBrush",
                    Brushes.DeepSkyBlue);
                break;

            case DssAssistantReadinessState.Calibrating:
                SetDssReadiness(
                    "DSS CALIBRATING",
                    string.IsNullOrWhiteSpace(range)
                        ? "Waiting for a clean center + horizon observation."
                        : range,
                    "AccentColorBrush",
                    Brushes.DeepSkyBlue);
                break;

            case DssAssistantReadinessState.TooClose:
                SetDssReadiness(
                    "TOO CLOSE · MOVE AWAY",
                    JoinDssDetail(
                        distance,
                        range),
                    "FailureColorBrush",
                    Brushes.OrangeRed);
                break;

            case DssAssistantReadinessState.TooFar:
                SetDssReadiness(
                    "TOO FAR · MOVE CLOSER",
                    JoinDssDetail(
                        distance,
                        range),
                    "FailureColorBrush",
                    Brushes.Goldenrod);
                break;

            case DssAssistantReadinessState.Ready:
                SetDssReadiness(
                    readiness.IsFarReadyEdge
                        ? "DSS READY · FAR EDGE"
                        : "DSS ASSISTANT READY",
                    readiness.IsFarReadyEdge
                        ? JoinDssDetail(
                            "Usable far edge; do not move farther.",
                            distance,
                            range)
                        : JoinDssDetail(
                            distance,
                            range),
                    "SuccessColorBrush",
                    Brushes.LimeGreen);
                break;
        }

        dssTrackingText.Text =
            $"center {(dss.BodyCenterFound ? "OK" : "—")} · " +
            $"horizon {(dss.HorizonFound ? "OK" : "—")} · " +
            $"probe radius {dss.DssPatchRadius:0.#}%";

        ModuleStatusText.Text =
            readiness.IsReady
                ? "DSS ASSISTANT · READY"
                : "DSS ASSISTANT";

        FooterHintText.Text =
            readiness.IsReady
                ? "Geometry is ready for calculated probe guidance."
                : "Position the ship until DSS geometry is stable.";
    }

    private void SetDssReadiness(
        string title,
        string detail,
        string brushKey,
        Brush fallback)
    {
        if (dssReadinessTitleText is null
            || dssReadinessDetailText is null)
        {
            return;
        }

        dssReadinessTitleText.Text =
            title;

        dssReadinessTitleText.Foreground =
            GetDssBrush(
                brushKey,
                fallback);

        dssReadinessDetailText.Text =
            detail;
    }

    private void DeactivateDssHud()
    {
        if (!dssHudActive)
        {
            if (dssContextPanel is not null)
            {
                dssContextPanel.Visibility =
                    Visibility.Collapsed;
            }

            return;
        }

        dssHudActive = false;

        if (dssContextPanel is not null)
        {
            dssContextPanel.Visibility =
                Visibility.Collapsed;
        }

        if (activity
            != ActivityType.Exploration)
        {
            return;
        }

        GameStateSnapshot state =
            JournalMonitorService.Instance.Current;

        ExplorationDataState externalData =
            ExplorationDataService.Instance.Current;

        ExplorationVisitQueueSnapshot queue =
            ExplorationVisitStateService.Instance.Current;

        ModuleStatusText.Text =
            BuildAdaptiveExplorationHeader(
                state,
                externalData,
                queue);

        RefreshAdaptiveExploration(
            state,
            externalData,
            queue);

        FooterHintText.Text =
            BuildAdaptiveExplorationFooter(
                state,
                queue);
    }

    private void DssHudWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (dssHudTimer is not null)
        {
            dssHudTimer.Stop();
            dssHudTimer.Tick -=
                DssHudTimer_Tick;
            dssHudTimer = null;
        }

        Closed -=
            DssHudWindow_Closed;
    }

    private Brush GetDssBrush(
        string key,
        Brush fallback) =>
        TryFindResource(key) as Brush
        ?? fallback;

    private static string JoinDssDetail(
        params string[] values) =>
        string.Join(
            "  •  ",
            values.Where(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value)));

    private static string FormatDssDistance(
        double meters)
    {
        if (meters <= 0)
        {
            return "?";
        }

        const double lightSecondMeters =
            299_792_458d;

        if (meters >= lightSecondMeters)
        {
            return
                $"{meters / lightSecondMeters:0.00} ls";
        }

        if (meters >= 1_000_000d)
        {
            return
                $"{meters / 1_000_000d:0.0} Mm";
        }

        if (meters >= 1_000d)
        {
            return
                $"{meters / 1_000d:0.0} km";
        }

        return $"{meters:0} m";
    }
}
