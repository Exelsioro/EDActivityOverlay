using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Services.Exploration;

namespace EDActivityOverlay.UserControls;

public partial class ExplorationWorkspaceControl
{
    private void RefreshCompactDssDetails(
        GameStateSnapshot state,
        ExplorationBodySnapshot? body,
        DssAssistantLiveSnapshot? live)
    {
        if (DssNativeScanProgressRuntime.TryGetFresh(
                out DssNativeScanProgressSnapshot progress))
        {
            if (progress.CoveragePercent >= 0)
                dssLastCoveragePercent = progress.CoveragePercent;
            if (progress.HitCount >= 0)
                dssLastHitCount = progress.HitCount;
        }

        CompactQueueCountText.Text = dssLastCoveragePercent >= 0
            ? $"{dssLastCoveragePercent}%"
            : "—%";

        DssAssistantReadinessSnapshot? readiness = live?.Readiness;
        DssReadinessTitleText.Text = ResolveCompactReadinessTitle(readiness);
        DssReadinessTitleText.Foreground = ResourceBrush(
            dssLastCoveragePercent >= 100
                || readiness?.State == DssAssistantReadinessState.Ready
                ? "SuccessColorBrush"
                : readiness?.State is DssAssistantReadinessState.TooClose
                    or DssAssistantReadinessState.TooFar
                    or DssAssistantReadinessState.SelectBodyTarget
                    or DssAssistantReadinessState.NeedBodyRadius
                    ? "FailureColorBrush"
                    : "AccentColorBrush",
            Brushes.DeepSkyBlue);
        DssTargetHintText.Text = dssLastCoveragePercent >= 100
            ? Loc.Get("Loc_DSS_SCAN_COMPLETE_DETAIL")
            : readiness?.State is DssAssistantReadinessState.TooClose
                or DssAssistantReadinessState.TooFar
                ? BuildCompactDistanceSummary(readiness)
                : ResolveDssReadinessText(live);

        DssMetricsText.Text = BuildCompactAngularSummary(readiness);
        DssDistanceText.Text = BuildCompactDistanceSummary(readiness);
        bool distanceIsReadinessDetail =
            readiness?.State is DssAssistantReadinessState.TooClose
                or DssAssistantReadinessState.TooFar;
        DssDistanceText.Visibility = distanceIsReadinessDetail
            || string.IsNullOrWhiteSpace(DssDistanceText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        (int officialTarget, string source) =
            ResolveCompactOfficialTarget(body);
        int plannedTarget = 0;
        DssEngineeringTargetResolution? resolution = null;
        DssModuleSnapshot module = ResolveCompactDssModule(live);
        if (officialTarget > 0)
        {
            try
            {
                resolution = DssEngineeringTargetResolver.Resolve(
                    officialTarget, source, module);
                plannedTarget = resolution.TargetCount;
            }
            catch
            {
                plannedTarget = officialTarget;
            }
        }

        DssTargetText.Text = plannedTarget > 0
            ? plannedTarget == officialTarget
                ? Loc.Format("Loc_DSS_PLAN_FORMAT", plannedTarget)
                : Loc.Format(
                    "Loc_DSS_PLAN_REDUCED_FORMAT",
                    plannedTarget,
                    officialTarget)
            : officialTarget > 0
                ? Loc.Format(
                    "Loc_DSS_NATIVE_TARGET_PLAN_PENDING_FORMAT",
                    officialTarget)
                : Loc.Get("Loc_DSS_PLAN_WAITING_NATIVE");

        DssResultText.Text =
            dssLastCoveragePercent >= 0 || dssLastHitCount >= 0
                ? Loc.Format(
                    "Loc_DSS_LIVE_PROGRESS_FORMAT",
                    dssLastCoveragePercent >= 0
                        ? $"{dssLastCoveragePercent}%"
                        : "—",
                    dssLastHitCount >= 0
                        ? dssLastHitCount.ToString()
                        : "—",
                    plannedTarget > 0 ? plannedTarget : "—")
                : Loc.Get("Loc_DSS_NO_RESULT_YET");

        DssPlanText.Text = live is not null
            ? Loc.Format("Loc_DSS_NEXT_AIM_FORMAT", live!.TargetingStep)
            : Loc.Get("Loc_DSS_STARTING");
        DssMappingText.Text = BuildCompactMappingValue(state);
        DssScannerText.Text = BuildCompactScannerSummary(module);
        DssTrackingText.Text = Loc.Format(
            "Loc_DSS_TRACKING_FORMAT",
            live?.BodyCenterFound == true ? "✓" : "—",
            live?.HorizonFound == true ? "✓" : "—");

        UpdateCompactDssPlanPreview(
            readiness, module, plannedTarget, resolution);
    }

    private string ResolveCompactReadinessTitle(
        DssAssistantReadinessSnapshot? readiness)
    {
        if (dssLastCoveragePercent >= 100)
            return Loc.Get("Loc_DSS_SCAN_COMPLETE");
        return readiness?.State switch
        {
            DssAssistantReadinessState.SelectBodyTarget =>
                Loc.Get("Loc_DSS_SELECT_BODY_TARGET"),
            DssAssistantReadinessState.NeedBodyRadius =>
                Loc.Get("Loc_DSS_BODY_DATA_REQUIRED"),
            DssAssistantReadinessState.Calibrating =>
                Loc.Get("Loc_DSS_CALIBRATING"),
            DssAssistantReadinessState.TooClose =>
                Loc.Get("Loc_DSS_TOO_CLOSE"),
            DssAssistantReadinessState.TooFar =>
                Loc.Get("Loc_DSS_TOO_FAR"),
            DssAssistantReadinessState.Ready
                when readiness.IsFarReadyEdge =>
                Loc.Get("Loc_DSS_READY_FAR_EDGE"),
            DssAssistantReadinessState.Ready =>
                Loc.Get("Loc_DSS_READY"),
            _ => Loc.Get("Loc_DSS_STARTING")
        };
    }

    private (int Target, string Source) ResolveCompactOfficialTarget(
        ExplorationBodySnapshot? body)
    {
        if (DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out DssNativeEfficiencyTargetSnapshot native))
        {
            dssLastOfficialTarget = native.Target;
            dssLastOfficialTargetSource = "HUD_CV";
        }
        else if (body?.EfficiencyTarget > 0)
        {
            dssLastOfficialTarget = body.EfficiencyTarget;
            dssLastOfficialTargetSource = "BODY";
        }

        return (
            dssLastOfficialTarget,
            dssLastOfficialTargetSource);
    }

    private DssModuleSnapshot ResolveCompactDssModule(
        DssAssistantLiveSnapshot? live)
    {
        if (dssPresentationModule.PatchRadius > 0)
            return dssPresentationModule;
        double radius = live?.DssPatchRadius ?? 0;
        return new DssModuleSnapshot(
            "presentation",
            "Detailed Surface Scanner",
            radius,
            radius,
            string.Empty,
            0);
    }

    private static string BuildCompactAngularSummary(
        DssAssistantReadinessSnapshot? readiness)
    {
        string measured = readiness?.HasAngularMeasurement == true
            ? Loc.Format(
                "Loc_DSS_ANGLE_VALUE_FORMAT",
                readiness.AngularDiameterDegrees)
            : "—";
        return Loc.Format(
            "Loc_DSS_ANGULAR_SUMMARY_FORMAT",
            measured,
            DssAssistantReadinessEvaluator
                .MinimumReadyAngularDiameterDegrees,
            DssAssistantReadinessEvaluator
                .MaximumReadyAngularDiameterDegrees,
            DssAssistantReadinessEvaluator.TargetAngularDiameterDegrees);
    }

    private static string BuildCompactDistanceSummary(
        DssAssistantReadinessSnapshot? readiness)
    {
        if (readiness is null)
            return string.Empty;
        string distance = readiness.HasDistanceEstimate
            ? Loc.Format(
                "Loc_DSS_DISTANCE_FORMAT",
                FormatCompactDssDistance(
                    readiness.EstimatedCenterDistanceMeters))
            : string.Empty;
        string target =
            readiness.RecommendedTargetCenterDistanceMeters > 0
                ? Loc.Format(
                    "Loc_DSS_TARGET_DISTANCE_FORMAT",
                    FormatCompactDssDistance(
                        readiness.RecommendedTargetCenterDistanceMeters))
                : string.Empty;
        return string.Join(
            "  •  ",
            new[] { distance, target }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatCompactDssDistance(double meters)
    {
        const double lightSecondMeters = 299_792_458d;
        if (meters >= lightSecondMeters)
            return $"{meters / lightSecondMeters:0.00} ls";
        if (meters >= 1_000_000d)
            return $"{meters / 1_000_000d:0.0} Mm";
        if (meters >= 1_000d)
            return $"{meters / 1_000d:0.0} km";
        return $"{meters:0} m";
    }

    private static string BuildCompactMappingValue(GameStateSnapshot state)
    {
        ExplorationBodySnapshot? body =
            state.DestinationBodyId >= 0
                ? state.ExplorationBodies.FirstOrDefault(
                    item => item.BodyId == state.DestinationBodyId)
                : null;
        if (body is null && !string.IsNullOrWhiteSpace(state.DestinationName))
        {
            body = state.ExplorationBodies.FirstOrDefault(
                item => item.Name.Equals(
                    state.DestinationName,
                    StringComparison.OrdinalIgnoreCase));
        }

        long estimate = body is null
            ? 0
            : ExplorationPresentationValueResolver.ResolveMappingEstimate(
                body);
        return Loc.Format(
            "Loc_DSS_MAPPING_VALUE_FORMAT",
            estimate > 0
                ? Loc.Format("Loc_Credits_Short_Format", estimate)
                : "—");
    }

    private static string BuildCompactScannerSummary(DssModuleSnapshot module)
    {
        if (module.PatchRadius <= 0)
            return Loc.Get("Loc_DSS_SCANNER_PENDING");
        if (module.OriginalPatchRadius <= 0)
            return Loc.Format(
                "Loc_DSS_SCANNER_PARAMETER_FORMAT",
                module.PatchRadius);

        double bonus =
            (module.PatchRadius / module.OriginalPatchRadius - 1d) * 100d;
        if (bonus <= 0.05d)
            return Loc.Get("Loc_DSS_SCANNER_STOCK");

        string grade = module.EngineeringLevel > 0
            ? $" G{module.EngineeringLevel}"
            : string.Empty;
        return Loc.Format(
            "Loc_DSS_SCANNER_ENGINEERED_FORMAT", grade, bonus);
    }

    private void UpdateCompactDssPlanPreview(
        DssAssistantReadinessSnapshot? readiness,
        DssModuleSnapshot module,
        int plannedTarget,
        DssEngineeringTargetResolution? resolution)
    {
        if (readiness?.IsReady != true
            || !readiness.HasAngularMeasurement
            || plannedTarget <= 0
            || resolution is null)
        {
            string unavailable =
                $"none|{plannedTarget}|{readiness?.State}";
            if (unavailable == dssLastPlanSignature)
                return;
            dssLastPlanSignature = unavailable;
            DrawCompactDssPlanPlaceholder();
            return;
        }

        string signature =
            $"{plannedTarget}|{resolution.ActualCapAngularRadius:0.00000}";
        if (signature == dssLastPlanSignature)
            return;
        dssLastPlanSignature = signature;

        try
        {
            IReadOnlyList<DssSphericalAimTarget> plan =
                DssSphericalPlacementPlanner.GenerateOrderedSphericalPlan(
                    plannedTarget,
                    readiness.AngularDiameterDegrees,
                    module,
                    readiness.BodyRadiusMeters,
                    resolution.ActualCapAngularRadius);
            DrawCompactDssPlan(plan);
        }
        catch
        {
            DrawCompactDssPlanPlaceholder();
        }
    }

    private void DrawCompactDssPlan(
        IReadOnlyList<DssSphericalAimTarget> plan)
    {
        DssPlanCanvas.Children.Clear();
        const double centerX = 54d;
        const double centerY = 48d;
        const double horizonRadius = 26d;

        var rearGuide = new Ellipse
        {
            Width = 82,
            Height = 82,
            Stroke = ResourceBrush(
                "MutedTextColorBrush", Brushes.Gray),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 3 },
            Opacity = 0.35,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(rearGuide, centerX - 41);
        Canvas.SetTop(rearGuide, centerY - 41);
        DssPlanCanvas.Children.Add(rearGuide);

        var horizon = new Ellipse
        {
            Width = horizonRadius * 2,
            Height = horizonRadius * 2,
            Stroke = ResourceBrush(
                "BorderColorBrush", Brushes.DimGray),
            StrokeThickness = 1.2,
            Fill = Brushes.Transparent,
            Opacity = 0.9
        };
        Canvas.SetLeft(horizon, centerX - horizonRadius);
        Canvas.SetTop(horizon, centerY - horizonRadius);
        DssPlanCanvas.Children.Add(horizon);

        bool showNumbers = plan.Count <= 12;
        double markerSize = plan.Count <= 9
            ? 12d
            : plan.Count <= 16 ? 8d : 6d;
        foreach (DssSphericalAimTarget point
                 in plan.OrderBy(item => item.Sequence))
        {
            if (!point.Available)
                continue;

            double x = centerX + point.NormalizedX * horizonRadius;
            double y = centerY + point.NormalizedY * horizonRadius;
            Brush fill = ResourceBrush(
                point.Zone switch
                {
                    DssAimZone.FarSide => "SecondaryTextColorBrush",
                    DssAimZone.Limb => "AccentColorBrush",
                    _ => "PrimaryTextColorBrush"
                },
                point.Zone == DssAimZone.Limb
                    ? Brushes.DeepSkyBlue
                    : Brushes.White);
            var marker = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = fill,
                Stroke = ResourceBrush(
                    "PrimaryBackgroundColorBrush", Brushes.Black),
                StrokeThickness = 1.2,
                Opacity = 0.95
            };
            Canvas.SetLeft(marker, x - markerSize / 2d);
            Canvas.SetTop(marker, y - markerSize / 2d);
            DssPlanCanvas.Children.Add(marker);

            if (!showNumbers)
                continue;
            var number = new TextBlock
            {
                Text = point.Sequence.ToString(),
                FontSize = 7,
                FontWeight = FontWeights.Bold,
                Foreground = ResourceBrush(
                    "PrimaryBackgroundColorBrush", Brushes.Black),
                IsHitTestVisible = false
            };
            number.Measure(new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));
            Canvas.SetLeft(number, x - number.DesiredSize.Width / 2d);
            Canvas.SetTop(number, y - number.DesiredSize.Height / 2d);
            DssPlanCanvas.Children.Add(number);
        }
    }

    private void DrawCompactDssPlanPlaceholder()
    {
        DssPlanCanvas.Children.Clear();
        var horizon = new Ellipse
        {
            Width = 52,
            Height = 52,
            Stroke = ResourceBrush(
                "MutedTextColorBrush", Brushes.Gray),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            Fill = Brushes.Transparent,
            Opacity = 0.55
        };
        Canvas.SetLeft(horizon, 28);
        Canvas.SetTop(horizon, 22);
        DssPlanCanvas.Children.Add(horizon);

        var hint = new TextBlock
        {
            Text = Loc.Get("Loc_DSS_CALIBRATE_SHORT"),
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush(
                "MutedTextColorBrush", Brushes.Gray),
            Opacity = 0.8
        };
        hint.Measure(new Size(
            double.PositiveInfinity,
            double.PositiveInfinity));
        Canvas.SetLeft(hint, 54 - hint.DesiredSize.Width / 2d);
        Canvas.SetTop(hint, 48 - hint.DesiredSize.Height / 2d);
        DssPlanCanvas.Children.Add(hint);
    }
}
