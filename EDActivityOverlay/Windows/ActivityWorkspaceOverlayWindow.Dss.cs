using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow
{
    // Freshness controls only the contents of the DSS presentation.
    // GuiFocus=10 owns the presentation itself and must never fall back to the
    // normal Exploration HUD because one CV snapshot arrived late.
    private static readonly TimeSpan DssPresentationFreshness =
        TimeSpan.FromSeconds(2.5);

    private bool dssHudInitialized;
    private bool dssHudActive;
    private bool dssStandaloneLayer;
    private DispatcherTimer? dssHudTimer;

    private Border? dssContextPanel;
    private TextBlock? dssBodyText;
    private TextBlock? dssCoverageText;
    private TextBlock? dssReadinessTitleText;
    private TextBlock? dssReadinessDetailText;
    private TextBlock? dssMetricsText;
    private TextBlock? dssPlanText;
    private TextBlock? dssMappingValueText;
    private TextBlock? dssScannerText;
    private TextBlock? dssTrackingText;
    private Canvas? dssPlanCanvas;

    private string dssPresentationBodyKey =
        string.Empty;

    private DssModuleSnapshot dssPresentationModule =
        DssModuleSnapshot.Empty;

    private int dssLastCoveragePercent = -1;
    private int dssLastHitCount = -1;
    private int dssLastOfficialTarget;
    private string dssLastOfficialTargetSource =
        string.Empty;
    private string dssLastPlanSignature =
        string.Empty;

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

        // Product presentation is a sibling layer of the normal adaptive
        // Exploration HUD. This is deliberately not another child of
        // AdaptiveExplorationPanel: ordinary Exploration refreshes are allowed
        // to continue in the background, but they can no longer replace the
        // DSS structure for one rendered frame.
        if (AdaptiveExplorationPanel.Parent
            is Panel host)
        {
            host.Children.Add(
                dssContextPanel);

            Panel.SetZIndex(
                dssContextPanel,
                20);

            dssStandaloneLayer = true;
        }
        else
        {
            // Defensive fallback for an unexpected layout host.
            int insertIndex =
                Math.Min(
                    1,
                    AdaptiveExplorationPanel.Children.Count);

            AdaptiveExplorationPanel.Children.Insert(
                insertIndex,
                dssContextPanel);

            dssStandaloneLayer = false;
        }

        dssHudTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(100)
            };

        dssHudTimer.Tick +=
            DssHudTimer_Tick;

        dssHudTimer.Start();

        JournalMonitorService.Instance.StateChanged +=
            DssHudOwnershipStateChanged;

        Closed +=
            DssHudWindow_Closed;

        RefreshDssPresentation(
            JournalMonitorService.Instance.Current);
    }

    private Border BuildDssContextPanel()
    {
        var panel =
            new Border
            {
                Visibility =
                    Visibility.Collapsed,
                Padding =
                    new Thickness(10, 8, 10, 8),
                Margin =
                    new Thickness(0),
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Stretch,
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

        var header =
            new Grid();

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        var identity =
            new StackPanel();

        identity.Children.Add(
            new TextBlock
            {
                Text =
                    Loc.Get(
                        "Loc_DSS_ASSISTANT"),
                FontSize =
                    9,
                FontWeight =
                    FontWeights.SemiBold,
                Foreground =
                    GetDssBrush(
                        "AccentColorBrush",
                        Brushes.DeepSkyBlue)
            });

        dssBodyText =
            new TextBlock
            {
                Margin =
                    new Thickness(0, 2, 0, 0),
                FontSize =
                    13,
                FontWeight =
                    FontWeights.Bold,
                TextTrimming =
                    TextTrimming.CharacterEllipsis,
                Foreground =
                    GetDssBrush(
                        "PrimaryTextColorBrush",
                        Brushes.White)
            };

        identity.Children.Add(
            dssBodyText);

        header.Children.Add(
            identity);

        dssCoverageText =
            new TextBlock
            {
                Margin =
                    new Thickness(12, 0, 0, 0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextAlignment =
                    TextAlignment.Right,
                FontSize =
                    18,
                FontWeight =
                    FontWeights.Bold,
                Foreground =
                    GetDssBrush(
                        "AccentColorBrush",
                        Brushes.DeepSkyBlue),
                Text =
                    "—%"
            };

        Grid.SetColumn(
            dssCoverageText,
            1);

        header.Children.Add(
            dssCoverageText);

        root.Children.Add(
            header);

        dssReadinessTitleText =
            new TextBlock
            {
                Margin =
                    new Thickness(0, 7, 0, 0),
                FontSize =
                    12,
                FontWeight =
                    FontWeights.Bold,
                Foreground =
                    GetDssBrush(
                        "AccentColorBrush",
                        Brushes.DeepSkyBlue),
                Text =
                    Loc.Get(
                        "Loc_DSS_STARTING")
            };

        root.Children.Add(
            dssReadinessTitleText);

        dssReadinessDetailText =
            new TextBlock
            {
                Margin =
                    new Thickness(0, 3, 0, 0),
                TextWrapping =
                    TextWrapping.Wrap,
                FontSize =
                    10,
                Foreground =
                    GetDssBrush(
                        "PrimaryTextColorBrush",
                        Brushes.White)
            };

        root.Children.Add(
            dssReadinessDetailText);

        root.Children.Add(
            new Border
            {
                Height =
                    1,
                Margin =
                    new Thickness(0, 7, 0, 6),
                Background =
                    GetDssBrush(
                        "BorderColorBrush",
                        Brushes.DimGray),
                Opacity =
                    0.7
            });

        var content =
            new Grid();

        content.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        content.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(112)
            });

        var details =
            new StackPanel();

        dssMetricsText =
            CreateDssInfoText(
                9,
                "SecondaryTextColorBrush",
                Brushes.LightGray);

        dssPlanText =
            CreateDssInfoText(
                10,
                "PrimaryTextColorBrush",
                Brushes.White);

        dssPlanText.FontWeight =
            FontWeights.SemiBold;

        dssMappingValueText =
            CreateDssInfoText(
                9,
                "AccentColorBrush",
                Brushes.DeepSkyBlue);

        dssScannerText =
            CreateDssInfoText(
                9,
                "SecondaryTextColorBrush",
                Brushes.LightGray);

        dssTrackingText =
            CreateDssInfoText(
                9,
                "MutedTextColorBrush",
                Brushes.Gray);

        details.Children.Add(
            dssMetricsText);

        details.Children.Add(
            dssPlanText);

        details.Children.Add(
            dssMappingValueText);

        details.Children.Add(
            dssScannerText);

        details.Children.Add(
            dssTrackingText);

        content.Children.Add(
            details);

        dssPlanCanvas =
            new Canvas
            {
                Width =
                    108,
                Height =
                    96,
                Margin =
                    new Thickness(4, 0, 0, 0),
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Center,
                ClipToBounds =
                    false,
                IsHitTestVisible =
                    false
            };

        Grid.SetColumn(
            dssPlanCanvas,
            1);

        content.Children.Add(
            dssPlanCanvas);

        root.Children.Add(
            content);

        panel.Child =
            root;

        DrawDssPlanPlaceholder();

        return panel;
    }

    private TextBlock CreateDssInfoText(
        double fontSize,
        string brushKey,
        Brush fallback) =>
        new()
        {
            Margin =
                new Thickness(0, 0, 0, 4),
            TextWrapping =
                TextWrapping.Wrap,
            FontSize =
                fontSize,
            Foreground =
                GetDssBrush(
                    brushKey,
                    fallback)
        };

    private void DssHudOwnershipStateChanged(
        object? sender,
        GameStateChangedEventArgs e)
    {
        if (disposed
            || activity
               != ActivityType.Exploration)
        {
            return;
        }

        // The normal workspace StateChanged handler queues RefreshContent at
        // DispatcherPriority.Normal. Reassert DSS ownership at Render priority:
        // Normal state updates may finish first, but the normal HUD is hidden
        // again before WPF composes a frame.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(
                () =>
                    RefreshDssPresentation(
                        e.State)));
    }

    private void DssHudTimer_Tick(
        object? sender,
        EventArgs e)
    {
        RefreshDssPresentation(
            JournalMonitorService.Instance.Current);
    }

    private void RefreshDssPresentation(
        GameStateSnapshot state)
    {
        if (disposed
            || activity
               != ActivityType.Exploration
            || !SettingsService.Instance.Settings.EnableExperimentalDssAssistant
            || state.GuiFocus != 10)
        {
            DeactivateDssHud();
            return;
        }

        DssAssistantLiveSnapshot? dss =
            DssAssistantStateService.Instance.Current;

        EnsureDssPresentationContext(
            state,
            dss);

        if (dss is null)
        {
            ActivateDssHudWaiting(
                state,
                null);
            return;
        }

        if (!dss.IsFresh(
                DateTimeOffset.UtcNow,
                DssPresentationFreshness))
        {
            ActivateDssHudWaiting(
                state,
                dss);
            return;
        }

        ActivateDssHud(
            state,
            dss);
    }

    private void EnsureDssPresentationContext(
        GameStateSnapshot state,
        DssAssistantLiveSnapshot? dss)
    {
        int bodyId =
            dss?.BodyId >= 0
                ? dss.BodyId
                : state.DestinationBodyId;

        string bodyName =
            !string.IsNullOrWhiteSpace(
                dss?.BodyName)
                ? dss!.BodyName
                : state.DestinationName
                  ?? string.Empty;

        string key =
            $"{state.SystemAddress}|{bodyId}|{bodyName}";

        if (key.Equals(
                dssPresentationBodyKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        dssPresentationBodyKey =
            key;

        dssLastCoveragePercent = -1;
        dssLastHitCount = -1;
        dssLastOfficialTarget = 0;
        dssLastOfficialTargetSource =
            string.Empty;
        dssLastPlanSignature =
            string.Empty;

        try
        {
            dssPresentationModule =
                DssJournalContextReader.ReadLatestDssModule(
                    JournalMonitorService.Instance.JournalDirectory);
        }
        catch
        {
            // Presentation may lose engineering detail, but it must never be
            // allowed to destabilize the DSS assistant.
            dssPresentationModule =
                DssModuleSnapshot.Empty;
        }

        DrawDssPlanPlaceholder();
    }

    private void ActivateDssHudShell(
        GameStateSnapshot state,
        string? bodyName)
    {
        if (dssContextPanel is null
            || dssBodyText is null
            || dssCoverageText is null)
        {
            return;
        }

        dssHudActive =
            true;

        // Opacity is the ownership backstop. Ordinary Exploration refreshes
        // may still assign Visibility.Visible to AdaptiveExplorationPanel, but
        // while GuiFocus=10 they cannot become visible for one composed frame.
        if (dssStandaloneLayer)
        {
            AdaptiveExplorationPanel.Opacity =
                0;

            AdaptiveExplorationPanel.Visibility =
                Visibility.Collapsed;

            LegacyCompactScrollViewer.Opacity =
                0;

            LegacyCompactScrollViewer.Visibility =
                Visibility.Collapsed;
        }
        else
        {
            SystemContextPanel.Visibility =
                Visibility.Collapsed;

            BodyContextPanel.Visibility =
                Visibility.Collapsed;

            ExobioContextPanel.Visibility =
                Visibility.Collapsed;

            CompactRouteAlertPanel.Visibility =
                Visibility.Collapsed;
        }

        dssContextPanel.Visibility =
            Visibility.Visible;

        ModuleStatusText.Visibility =
            Visibility.Collapsed;

        FooterHintText.Visibility =
            Visibility.Collapsed;

        OpenExplorationAssistantButton.Opacity =
            0;

        OpenExplorationAssistantButton.IsHitTestVisible =
            false;

        OpenExplorationAssistantButton.Visibility =
            Visibility.Collapsed;

        string resolvedBodyName =
            !string.IsNullOrWhiteSpace(
                bodyName)
                ? bodyName!
                : !string.IsNullOrWhiteSpace(
                    state.DestinationName)
                    ? state.DestinationName
                    : Loc.Get(
                        "Loc_DSS_BODY_TARGET");

        dssBodyText.Text =
            resolvedBodyName;

        RefreshNativeProgress();

        dssCoverageText.Text =
            dssLastCoveragePercent >= 0
                ? $"{dssLastCoveragePercent}%"
                : "—%";

        if (dssMappingValueText is not null)
        {
            dssMappingValueText.Text =
                BuildDssMappingValueSummary(
                    state);
        }
    }

    private void ActivateDssHudWaiting(
        GameStateSnapshot state,
        DssAssistantLiveSnapshot? dss)
    {
        ActivateDssHudShell(
            state,
            dss?.BodyName);

        if (dssReadinessTitleText is null
            || dssReadinessDetailText is null
            || dssMetricsText is null
            || dssPlanText is null
            || dssScannerText is null
            || dssTrackingText is null)
        {
            return;
        }

        SetDssReadiness(
            Loc.Get(
                "Loc_DSS_STARTING"),
            Loc.Get(
                "Loc_DSS_CALIBRATE_HORIZON"),
            "AccentColorBrush",
            Brushes.DeepSkyBlue);

        dssMetricsText.Text =
            BuildAngularSummary(
                dss?.Readiness);

        (int officialTarget, string source) =
            ResolveOfficialTarget(
                state);

        dssPlanText.Text =
            officialTarget > 0
                ? Loc.Format(
                    "Loc_DSS_NATIVE_TARGET_WAITING_FORMAT",
                    officialTarget)
                : Loc.Get(
                    "Loc_DSS_PLAN_WAITING_NATIVE");

        dssScannerText.Text =
            BuildScannerSummary(
                dss);

        dssTrackingText.Text =
            Loc.Format(
                "Loc_DSS_TRACKING_FORMAT",
                dss?.BodyCenterFound == true
                    ? "✓"
                    : "—",
                dss?.HorizonFound == true
                    ? "✓"
                    : "—");

        if (source.Length == 0)
        {
            DrawDssPlanPlaceholder();
        }
    }

    private void ActivateDssHud(
        GameStateSnapshot state,
        DssAssistantLiveSnapshot dss)
    {
        ActivateDssHudShell(
            state,
            dss.BodyName);

        if (dssReadinessTitleText is null
            || dssReadinessDetailText is null
            || dssMetricsText is null
            || dssPlanText is null
            || dssScannerText is null
            || dssTrackingText is null)
        {
            return;
        }

        DssAssistantReadinessSnapshot readiness =
            dss.Readiness;

        if (dssLastCoveragePercent >= 100)
        {
            SetDssReadiness(
                Loc.Get(
                    "Loc_DSS_SCAN_COMPLETE"),
                Loc.Get(
                    "Loc_DSS_SCAN_COMPLETE_DETAIL"),
                "SuccessColorBrush",
                Brushes.LimeGreen);
        }
        else
        {
            switch (readiness.State)
            {
                case DssAssistantReadinessState.SelectBodyTarget:
                    SetDssReadiness(
                        Loc.Get(
                            "Loc_DSS_SELECT_BODY_TARGET"),
                        Loc.Get(
                            "Loc_DSS_SELECT_BODY_DETAIL"),
                        "FailureColorBrush",
                        Brushes.OrangeRed);
                    break;

                case DssAssistantReadinessState.NeedBodyRadius:
                    SetDssReadiness(
                        Loc.Get(
                            "Loc_DSS_BODY_DATA_REQUIRED"),
                        Loc.Get(
                            "Loc_DSS_BODY_DATA_DETAIL"),
                        "FailureColorBrush",
                        Brushes.Orange);
                    break;

                case DssAssistantReadinessState.Calibrating:
                    SetDssReadiness(
                        Loc.Get(
                            "Loc_DSS_CALIBRATING"),
                        Loc.Get(
                            "Loc_DSS_CALIBRATE_ANGULAR"),
                        "AccentColorBrush",
                        Brushes.DeepSkyBlue);
                    break;

                case DssAssistantReadinessState.TooClose:
                    SetDssReadiness(
                        Loc.Get(
                            "Loc_DSS_TOO_CLOSE"),
                        BuildReadinessDistanceDetail(
                            readiness),
                        "FailureColorBrush",
                        Brushes.OrangeRed);
                    break;

                case DssAssistantReadinessState.TooFar:
                    SetDssReadiness(
                        Loc.Get(
                            "Loc_DSS_TOO_FAR"),
                        BuildReadinessDistanceDetail(
                            readiness),
                        "FailureColorBrush",
                        Brushes.Goldenrod);
                    break;

                case DssAssistantReadinessState.Ready:
                    SetDssReadiness(
                        readiness.IsFarReadyEdge
                            ? Loc.Get(
                                "Loc_DSS_READY_FAR_EDGE")
                            : Loc.Get(
                                "Loc_DSS_READY"),
                        readiness.IsFarReadyEdge
                            ? Loc.Get(
                                "Loc_DSS_READY_FAR_EDGE_DETAIL")
                            : Loc.Get(
                                "Loc_DSS_READY_DETAIL"),
                        "SuccessColorBrush",
                        Brushes.LimeGreen);
                    break;
            }
        }

        dssMetricsText.Text =
            BuildAngularSummary(
                readiness);

        (int officialTarget, string targetSource) =
            ResolveOfficialTarget(
                state);

        int plannedTarget =
            0;

        DssEngineeringTargetResolution? resolution =
            null;

        DssModuleSnapshot module =
            ResolvePresentationModule(
                dss);

        if (officialTarget > 0)
        {
            try
            {
                resolution =
                    DssEngineeringTargetResolver.Resolve(
                        officialTarget,
                        targetSource,
                        module);

                plannedTarget =
                    resolution.TargetCount;
            }
            catch
            {
                plannedTarget =
                    officialTarget;
            }
        }

        string hits =
            dssLastHitCount >= 0
                ? " · "
                  + (plannedTarget > 0
                      ? Loc.Format(
                          "Loc_DSS_HITS_FORMAT",
                          dssLastHitCount,
                          plannedTarget)
                      : Loc.Format(
                          "Loc_DSS_HITS_COUNT_FORMAT",
                          dssLastHitCount))
                : string.Empty;

        dssPlanText.Text =
            plannedTarget > 0
                ? plannedTarget == officialTarget
                    ? Loc.Format(
                        "Loc_DSS_PLAN_FORMAT",
                        plannedTarget)
                      + hits
                    : Loc.Format(
                        "Loc_DSS_PLAN_REDUCED_FORMAT",
                        plannedTarget,
                        officialTarget)
                      + hits
                : officialTarget > 0
                    ? Loc.Format(
                        "Loc_DSS_NATIVE_TARGET_PLAN_PENDING_FORMAT",
                        officialTarget)
                    : Loc.Get(
                        "Loc_DSS_PLAN_WAITING_NATIVE");

        dssScannerText.Text =
            BuildScannerSummary(
                dss);

        dssTrackingText.Text =
            Loc.Format(
                "Loc_DSS_TRACKING_FORMAT",
                dss.BodyCenterFound
                    ? "✓"
                    : "—",
                dss.HorizonFound
                    ? "✓"
                    : "—");

        UpdateDssPlanPreview(
            dss,
            module,
            readiness,
            plannedTarget,
            resolution);
    }

    private void RefreshNativeProgress()
    {
        if (DssNativeScanProgressRuntime.TryGetFresh(
                out DssNativeScanProgressSnapshot progress))
        {
            if (progress.CoveragePercent >= 0)
            {
                dssLastCoveragePercent =
                    progress.CoveragePercent;
            }

            if (progress.HitCount >= 0)
            {
                dssLastHitCount =
                    progress.HitCount;
            }
        }
    }

    private (int Target, string Source)
        ResolveOfficialTarget(
            GameStateSnapshot state)
    {
        if (DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out DssNativeEfficiencyTargetSnapshot native))
        {
            dssLastOfficialTarget =
                native.Target;

            dssLastOfficialTargetSource =
                "HUD_CV";

            return (
                native.Target,
                "HUD_CV");
        }

        if (state.DestinationBodyId >= 0)
        {
            ExplorationBodySnapshot? body =
                state.ExplorationBodies
                    .FirstOrDefault(
                        item =>
                            item.BodyId
                            == state.DestinationBodyId);

            if (body?.EfficiencyTarget > 0)
            {
                dssLastOfficialTarget =
                    body.EfficiencyTarget;

                dssLastOfficialTargetSource =
                    "BODY";

                return (
                    body.EfficiencyTarget,
                    "BODY");
            }
        }

        return dssLastOfficialTarget > 0
            ? (
                dssLastOfficialTarget,
                string.IsNullOrWhiteSpace(
                    dssLastOfficialTargetSource)
                    ? "HUD_CV"
                    : dssLastOfficialTargetSource)
            : (0, string.Empty);
    }

    private DssModuleSnapshot ResolvePresentationModule(
        DssAssistantLiveSnapshot dss)
    {
        if (dssPresentationModule.PatchRadius > 0)
        {
            return dssPresentationModule;
        }

        // The live snapshot already carries the actual PatchRadius. If Journal
        // engineering detail could not be read for presentation, preserve the
        // safe stock-equivalent interpretation instead of inventing a bonus.
        return
            new DssModuleSnapshot(
                "presentation",
                "Detailed Surface Scanner",
                dss.DssPatchRadius,
                dss.DssPatchRadius,
                string.Empty,
                0);
    }

    private static string BuildDssMappingValueSummary(
        GameStateSnapshot state)
    {
        ExplorationBodySnapshot? body =
            null;

        if (state.DestinationBodyId >= 0)
        {
            body =
                state.ExplorationBodies
                    .FirstOrDefault(
                        candidate =>
                            candidate.BodyId
                            == state.DestinationBodyId);
        }

        if (body is null
            && !string.IsNullOrWhiteSpace(
                state.DestinationName))
        {
            body =
                state.ExplorationBodies
                    .FirstOrDefault(
                        candidate =>
                            candidate.Name.Equals(
                                state.DestinationName,
                                StringComparison.OrdinalIgnoreCase));
        }

        string value =
            body?.EstimatedMappingValue > 0
                ? Loc.Format(
                    "Loc_Credits_Short_Format",
                    body.EstimatedMappingValue)
                : "—";

        return
            Loc.Format(
                "Loc_DSS_MAPPING_VALUE_FORMAT",
                value);
    }

    private string BuildScannerSummary(
        DssAssistantLiveSnapshot? dss)
    {
        double patch =
            dssPresentationModule.PatchRadius > 0
                ? dssPresentationModule.PatchRadius
                : dss?.DssPatchRadius
                  ?? 0;

        double original =
            dssPresentationModule.OriginalPatchRadius;

        if (patch <= 0)
        {
            return
                Loc.Get(
                    "Loc_DSS_SCANNER_PENDING");
        }

        if (original <= 0)
        {
            return
                Loc.Format(
                    "Loc_DSS_SCANNER_PARAMETER_FORMAT",
                    patch);
        }

        double multiplier =
            patch / original;

        double bonus =
            (multiplier - 1d)
            * 100d;

        if (bonus > 0.05d)
        {
            string grade =
                dssPresentationModule.EngineeringLevel > 0
                    ? $" G{dssPresentationModule.EngineeringLevel}"
                    : string.Empty;

            return
                Loc.Format(
                    "Loc_DSS_SCANNER_ENGINEERED_FORMAT",
                    grade,
                    bonus);
        }

        return
            Loc.Get(
                "Loc_DSS_SCANNER_STOCK");
    }

    private static string BuildAngularSummary(
        DssAssistantReadinessSnapshot? readiness)
    {
        string measured =
            readiness is not null
            && readiness.HasAngularMeasurement
                ? Loc.Format(
                    "Loc_DSS_ANGLE_VALUE_FORMAT",
                    readiness.AngularDiameterDegrees)
                : "—";

        return
            Loc.Format(
                "Loc_DSS_ANGULAR_SUMMARY_FORMAT",
                measured,
                DssAssistantReadinessEvaluator.MinimumReadyAngularDiameterDegrees,
                DssAssistantReadinessEvaluator.MaximumReadyAngularDiameterDegrees,
                DssAssistantReadinessEvaluator.TargetAngularDiameterDegrees);
    }

    private static string BuildReadinessDistanceDetail(
        DssAssistantReadinessSnapshot readiness)
    {
        string distance =
            readiness.HasDistanceEstimate
                ? Loc.Format(
                    "Loc_DSS_DISTANCE_FORMAT",
                    FormatDssDistance(
                        readiness.EstimatedCenterDistanceMeters))
                : string.Empty;

        string target =
            readiness.RecommendedTargetCenterDistanceMeters > 0
                ? Loc.Format(
                    "Loc_DSS_TARGET_DISTANCE_FORMAT",
                    FormatDssDistance(
                        readiness.RecommendedTargetCenterDistanceMeters))
                : string.Empty;

        return
            JoinDssDetail(
                distance,
                target);
    }

    private void UpdateDssPlanPreview(
        DssAssistantLiveSnapshot dss,
        DssModuleSnapshot module,
        DssAssistantReadinessSnapshot readiness,
        int plannedTarget,
        DssEngineeringTargetResolution? resolution)
    {
        if (dssPlanCanvas is null)
        {
            return;
        }

        if (plannedTarget <= 0
            || !readiness.IsReady
            || !readiness.HasAngularMeasurement
            || resolution is null)
        {
            string unavailableSignature =
                $"none|{plannedTarget}|{readiness.State}";

            if (!unavailableSignature.Equals(
                    dssLastPlanSignature,
                    StringComparison.Ordinal))
            {
                dssLastPlanSignature =
                    unavailableSignature;

                DrawDssPlanPlaceholder();
            }

            return;
        }

        // Preview is a schematic, not another live targeting surface. Build it
        // once per resolved plan/cap instead of re-running the spherical
        // projection while angular size changes frame by frame.
        string signature =
            $"{plannedTarget}|" +
            $"{resolution.ActualCapAngularRadius:0.00000}";

        if (signature.Equals(
                dssLastPlanSignature,
                StringComparison.Ordinal))
        {
            return;
        }

        dssLastPlanSignature =
            signature;

        try
        {
            IReadOnlyList<DssSphericalAimTarget> plan =
                DssSphericalPlacementPlanner
                    .GenerateOrderedSphericalPlan(
                        plannedTarget,
                        readiness.AngularDiameterDegrees,
                        module,
                        readiness.BodyRadiusMeters,
                        resolution.ActualCapAngularRadius);

            DrawDssPlan(
                plan);
        }
        catch
        {
            DrawDssPlanPlaceholder();
        }
    }

    private void DrawDssPlan(
        IReadOnlyList<DssSphericalAimTarget> plan)
    {
        if (dssPlanCanvas is null)
        {
            return;
        }

        dssPlanCanvas.Children.Clear();

        const double centerX = 54d;
        const double centerY = 48d;
        const double horizonRadius = 26d;
        const double projectionScale = 26d;

        var rearGuide =
            new Ellipse
            {
                Width =
                    82,
                Height =
                    82,
                Stroke =
                    GetDssBrush(
                        "MutedTextColorBrush",
                        Brushes.Gray),
                StrokeThickness =
                    1,
                StrokeDashArray =
                    new DoubleCollection
                    {
                        2,
                        3
                    },
                Opacity =
                    0.35,
                Fill =
                    Brushes.Transparent
            };

        Canvas.SetLeft(
            rearGuide,
            centerX - 41);

        Canvas.SetTop(
            rearGuide,
            centerY - 41);

        dssPlanCanvas.Children.Add(
            rearGuide);

        var horizon =
            new Ellipse
            {
                Width =
                    horizonRadius * 2,
                Height =
                    horizonRadius * 2,
                Stroke =
                    GetDssBrush(
                        "BorderColorBrush",
                        Brushes.DimGray),
                StrokeThickness =
                    1.2,
                Fill =
                    Brushes.Transparent,
                Opacity =
                    0.9
            };

        Canvas.SetLeft(
            horizon,
            centerX - horizonRadius);

        Canvas.SetTop(
            horizon,
            centerY - horizonRadius);

        dssPlanCanvas.Children.Add(
            horizon);

        bool showNumbers =
            plan.Count <= 12;

        double markerSize =
            plan.Count <= 9
                ? 12d
                : plan.Count <= 16
                    ? 8d
                    : 6d;

        foreach (DssSphericalAimTarget point
                 in plan.OrderBy(
                     point => point.Sequence))
        {
            if (!point.Available)
            {
                continue;
            }

            double x =
                centerX
                + point.NormalizedX
                  * projectionScale;

            double y =
                centerY
                + point.NormalizedY
                  * projectionScale;

            Brush fill =
                GetDssBrush(
                    point.Zone switch
                    {
                        DssAimZone.FarSide =>
                            "SecondaryTextColorBrush",
                        DssAimZone.Limb =>
                            "AccentColorBrush",
                        _ =>
                            "PrimaryTextColorBrush"
                    },
                    point.Zone
                        == DssAimZone.Limb
                            ? Brushes.DeepSkyBlue
                            : Brushes.White);

            var marker =
                new Ellipse
                {
                    Width =
                        markerSize,
                    Height =
                        markerSize,
                    Fill =
                        fill,
                    Stroke =
                        GetDssBrush(
                            "PrimaryBackgroundColorBrush",
                            Brushes.Black),
                    StrokeThickness =
                        1.2,
                    Opacity =
                        0.95
                };

            Canvas.SetLeft(
                marker,
                x - markerSize / 2d);

            Canvas.SetTop(
                marker,
                y - markerSize / 2d);

            dssPlanCanvas.Children.Add(
                marker);

            if (!showNumbers)
            {
                continue;
            }

            var number =
                new TextBlock
                {
                    Text =
                        point.Sequence.ToString(),
                    FontSize =
                        7,
                    FontWeight =
                        FontWeights.Bold,
                    Foreground =
                        GetDssBrush(
                            "PrimaryBackgroundColorBrush",
                            Brushes.Black),
                    IsHitTestVisible =
                        false
                };

            number.Measure(
                new Size(
                    double.PositiveInfinity,
                    double.PositiveInfinity));

            Canvas.SetLeft(
                number,
                x - number.DesiredSize.Width / 2d);

            Canvas.SetTop(
                number,
                y - number.DesiredSize.Height / 2d);

            dssPlanCanvas.Children.Add(
                number);
        }
    }

    private void DrawDssPlanPlaceholder()
    {
        if (dssPlanCanvas is null)
        {
            return;
        }

        dssPlanCanvas.Children.Clear();

        const double centerX = 54d;
        const double centerY = 48d;
        const double horizonRadius = 26d;

        var horizon =
            new Ellipse
            {
                Width =
                    horizonRadius * 2,
                Height =
                    horizonRadius * 2,
                Stroke =
                    GetDssBrush(
                        "MutedTextColorBrush",
                        Brushes.Gray),
                StrokeThickness =
                    1,
                StrokeDashArray =
                    new DoubleCollection
                    {
                        2,
                        2
                    },
                Fill =
                    Brushes.Transparent,
                Opacity =
                    0.55
            };

        Canvas.SetLeft(
            horizon,
            centerX - horizonRadius);

        Canvas.SetTop(
            horizon,
            centerY - horizonRadius);

        dssPlanCanvas.Children.Add(
            horizon);

        var hint =
            new TextBlock
            {
                Text =
                    Loc.Get(
                        "Loc_DSS_CALIBRATE_SHORT"),
                FontSize =
                    8,
                FontWeight =
                    FontWeights.SemiBold,
                Foreground =
                    GetDssBrush(
                        "MutedTextColorBrush",
                        Brushes.Gray),
                Opacity =
                    0.8
            };

        hint.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        Canvas.SetLeft(
            hint,
            centerX - hint.DesiredSize.Width / 2d);

        Canvas.SetTop(
            hint,
            centerY - hint.DesiredSize.Height / 2d);

        dssPlanCanvas.Children.Add(
            hint);
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
        if (dssContextPanel is not null)
        {
            dssContextPanel.Visibility =
                Visibility.Collapsed;
        }

        if (!dssHudActive)
        {
            return;
        }

        dssHudActive =
            false;

        if (dssStandaloneLayer)
        {
            AdaptiveExplorationPanel.Opacity =
                1;

            LegacyCompactScrollViewer.Opacity =
                1;
        }

        ModuleStatusText.Visibility =
            Visibility.Visible;

        FooterHintText.Visibility =
            Visibility.Visible;

        if (activity
            != ActivityType.Exploration)
        {
            return;
        }

        AdaptiveExplorationPanel.Visibility =
            Visibility.Visible;

        LegacyCompactScrollViewer.Visibility =
            Visibility.Collapsed;

        OpenExplorationAssistantButton.Opacity =
            1;

        OpenExplorationAssistantButton.IsHitTestVisible =
            true;

        OpenExplorationAssistantButton.Visibility =
            Visibility.Visible;

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

        JournalMonitorService.Instance.StateChanged -=
            DssHudOwnershipStateChanged;

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
