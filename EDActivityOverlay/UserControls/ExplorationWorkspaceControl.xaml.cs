using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

/// <summary>
/// Compact Exploration activity surface used by the composite renderer.
/// On the desktop it consumes the presentation snapshots published by the
/// DSS runtime. In VR the DSS lifecycle policy keeps both the CV pipeline and
/// this surface disabled.
/// </summary>
public partial class ExplorationWorkspaceControl : UserControl, IDisposable
{
    private GameStateSnapshot currentJournal = GameStateSnapshot.Empty;
    private readonly DispatcherTimer dssPresentationTimer;
    private string dssPresentationBodyKey = string.Empty;
    private int dssLastCoveragePercent = -1;
    private int dssLastHitCount = -1;
    private int dssLastOfficialTarget;
    private string dssLastOfficialTargetSource = string.Empty;
    private string dssLastPlanSignature = string.Empty;
    private DssModuleSnapshot dssPresentationModule = DssModuleSnapshot.Empty;
    private bool disposed;

    public ExplorationWorkspaceControl()
    {
        InitializeComponent();
        InitializeFullWorkspace();

        ExplorationDataService.Instance.DataChanged += OnExplorationDataChanged;
        ExplorationHistoryService.Instance.HistoryChanged += OnExplorationHistoryChanged;
        ExplorationVisitStateService.Instance.Changed += OnExplorationVisitStateChanged;
        ExplorationRouteService.Instance.RouteChanged += OnExplorationRouteChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        dssPresentationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            OnDssPresentationTick,
            Dispatcher);
        dssPresentationTimer.Start();

        SetChromeStyle(SettingsService.Instance.Settings.OverlayChromeStyle);
        UpdateJournalState(JournalMonitorService.Instance.Current);
    }

    public event Action? DragRequested;

    public bool IsCompactDssMode =>
        !fullExplorationVisible && ShouldShowStateOnlyDss(currentJournal);

    public void SetChromeStyle(string? style)
    {
        dssLastPlanSignature = string.Empty;
        string normalized =
            OverlayChromeStyles.Normalize(style);

        OverlayChromeHelper.Apply(
            CompactExplorationPanel,
            normalized);

        OverlayChromeHelper.Apply(
            FullExplorationPanel,
            normalized);
    }

    public void UpdateJournalState(GameStateSnapshot state)
    {
        currentJournal = state ?? GameStateSnapshot.Empty;
        RefreshPresentation();
    }

    public void RefreshLocalization()
    {
        dssLastPlanSignature = string.Empty;
        RefreshFullLocalization();
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (disposed)
        {
            return;
        }

        GameStateSnapshot state = currentJournal;
        ExplorationDataState externalData = ExplorationDataService.Instance.Current;
        ExplorationVisitQueueSnapshot queue = ExplorationVisitStateService.Instance.Current;

        ApplySurfaceVisibility();

        if (fullExplorationVisible)
        {
            RefreshFullWorkspace(state);
            return;
        }

        TitleText.Text = string.IsNullOrWhiteSpace(state.StarSystem)
            ? Loc.Get("Loc_EXPLORATION")
            : state.StarSystem.ToUpperInvariant();

        ModuleStatusText.Text = BuildAdaptiveExplorationHeader(
            state,
            externalData,
            queue);

        string destinationVisitStatus = BuildDestinationVisitStatus(state);
        DestinationVisitStatusText.Text = destinationVisitStatus;
        DestinationVisitStatusText.Visibility =
            string.IsNullOrWhiteSpace(destinationVisitStatus)
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (ShouldShowStateOnlyDss(state))
        {
            RefreshStateOnlyDss(state);
            CompactRouteAlertPanel.Visibility = Visibility.Collapsed;
            FooterHintText.Text = string.Empty;
            OpenExplorationAssistantButton.Visibility = Visibility.Collapsed;
            return;
        }
        else
        {
            DssContextPanel.Visibility = Visibility.Collapsed;
            RefreshAdaptiveExploration(state, queue);
        }

        OpenExplorationAssistantButton.Visibility = Visibility.Visible;

        string routeAlert = BuildCompactRouteOrAlert(
            state,
            QueueMatchesSystem(queue, state) ? queue : null);

        CompactRouteAlertText.Text = routeAlert;
        CompactRouteAlertPanel.Visibility =
            string.IsNullOrWhiteSpace(routeAlert)
                ? Visibility.Collapsed
                : Visibility.Visible;

        FooterHintText.Text = BuildAdaptiveExplorationFooter(state, queue);
    }

    private static bool ShouldShowStateOnlyDss(GameStateSnapshot state) =>
        DssLifecyclePolicy.ShouldRun(
            SettingsService.Instance.Settings.EnableExperimentalDssAssistant,
            VrOverlaySupport.IsEnabled)
        && state.GuiFocus == 10;

    private void OnDssPresentationTick(object? sender, EventArgs e)
    {
        if (!disposed
            && !fullExplorationVisible
            && ShouldShowStateOnlyDss(currentJournal))
        {
            RefreshStateOnlyDss(currentJournal);
        }
    }

    private void RefreshStateOnlyDss(GameStateSnapshot state)
    {
        // FSS and visit summaries describe the system, not the live DSS body.
        ModuleStatusText.Text = string.Empty;
        DestinationVisitStatusText.Visibility = Visibility.Collapsed;
        SystemContextPanel.Visibility = Visibility.Collapsed;
        BodyContextPanel.Visibility = Visibility.Collapsed;
        ExobioContextPanel.Visibility = Visibility.Collapsed;
        DssContextPanel.Visibility = Visibility.Visible;

        CompactModeText.Text = Loc.Get("Loc_DSS_ASSISTANT");
        CompactQueueCountText.Text = string.Empty;

        ExplorationBodySnapshot? body = ResolveDestinationBody(state);
        string bodyName =
            !string.IsNullOrWhiteSpace(body?.Name)
                ? body!.Name
                : !string.IsNullOrWhiteSpace(state.DestinationName)
                    ? state.DestinationName
                    : !string.IsNullOrWhiteSpace(state.CurrentBody)
                        ? state.CurrentBody
                        : Loc.Get("Loc_DSS_BODY_TARGET");

        CompactContextTitleText.Text = bodyName;

        DssAssistantLiveSnapshot? live =
            DssAssistantStateService.Instance.Current;
        bool hasFreshLiveState =
            live?.IsFresh(
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(2.5)) == true;

        if (!string.IsNullOrWhiteSpace(live?.BodyName))
        {
            bodyName = live!.BodyName;
            CompactContextTitleText.Text = bodyName;
        }

        int bodyId = live?.BodyId >= 0
            ? live.BodyId
            : body?.BodyId ?? state.DestinationBodyId;
        string bodyKey = $"{state.SystemAddress}|{bodyId}|{bodyName}";
        if (!bodyKey.Equals(
                dssPresentationBodyKey,
                StringComparison.OrdinalIgnoreCase))
        {
            dssPresentationBodyKey = bodyKey;
            dssLastCoveragePercent = -1;
            dssLastHitCount = -1;
            dssLastOfficialTarget = 0;
            dssLastOfficialTargetSource = string.Empty;
            dssLastPlanSignature = string.Empty;
            try
            {
                dssPresentationModule =
                    DssJournalContextReader.ReadLatestDssModule(
                        JournalMonitorService.Instance.JournalDirectory);
            }
            catch
            {
                dssPresentationModule = DssModuleSnapshot.Empty;
            }
        }

        RefreshCompactDssDetails(state, body, hasFreshLiveState ? live : null);
    }

    private static string ResolveDssReadinessText(
        DssAssistantLiveSnapshot? live)
    {
        if (live is null
            || !live.IsFresh(
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(2.5)))
        {
            return Loc.Get("Loc_DSS_CALIBRATE_HORIZON");
        }

        return live.Readiness.State switch
        {
            DssAssistantReadinessState.SelectBodyTarget =>
                Loc.Get("Loc_DSS_SELECT_BODY_DETAIL"),
            DssAssistantReadinessState.NeedBodyRadius =>
                Loc.Get("Loc_DSS_BODY_DATA_DETAIL"),
            DssAssistantReadinessState.Calibrating =>
                Loc.Get("Loc_DSS_CALIBRATE_ANGULAR"),
            DssAssistantReadinessState.TooClose =>
                Loc.Get("Loc_DSS_TOO_CLOSE"),
            DssAssistantReadinessState.TooFar =>
                Loc.Get("Loc_DSS_TOO_FAR"),
            DssAssistantReadinessState.Ready when live.Readiness.IsFarReadyEdge =>
                Loc.Get("Loc_DSS_READY_FAR_EDGE_DETAIL"),
            DssAssistantReadinessState.Ready =>
                Loc.Get("Loc_DSS_READY_DETAIL"),
            _ => Loc.Get("Loc_DSS_STARTING")
        };
    }

    private static ExplorationBodySnapshot? ResolveDestinationBody(
        GameStateSnapshot state)
    {
        if (state.DestinationBodyId >= 0)
        {
            ExplorationBodySnapshot? byId = state.ExplorationBodies
                .FirstOrDefault(body => body.BodyId == state.DestinationBodyId);
            if (byId is not null)
            {
                return byId;
            }
        }

        string name = !string.IsNullOrWhiteSpace(state.DestinationName)
            ? state.DestinationName
            : state.CurrentBody;

        return string.IsNullOrWhiteSpace(name)
            ? null
            : state.ExplorationBodies.FirstOrDefault(
                body => body.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));
    }

    private Brush ResourceBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private void RefreshAdaptiveExploration(
        GameStateSnapshot state,
        ExplorationVisitQueueSnapshot queue)
    {
        DssContextPanel.Visibility = Visibility.Collapsed;

        bool queueMatchesSystem = QueueMatchesSystem(queue, state);
        ExplorationVisitBodyState? active =
            queueMatchesSystem ? queue.Active : null;
        OrganicScanProgressSnapshot? activeOrganic =
            OrganicSampleNavigation.ResolveActiveSurfaceSample(state);

        SystemContextPanel.Visibility =
            active is null && activeOrganic is null
                ? Visibility.Visible
                : Visibility.Collapsed;
        BodyContextPanel.Visibility =
            active is not null && activeOrganic is null
                ? Visibility.Visible
                : Visibility.Collapsed;
        ExobioContextPanel.Visibility =
            activeOrganic is not null
                ? Visibility.Visible
                : Visibility.Collapsed;

        CompactQueueCountText.Text = queueMatchesSystem
            ? Loc.Format(
                "Loc_EXPLORATION_QUEUE_FORMAT",
                queue.RemainingCount,
                queue.DeferredCount,
                queue.CompletedCount)
            : string.Empty;

        if (activeOrganic is not null)
        {
            CompactModeText.Text = Loc.Get("Loc_EXPLORATION_MODE_EXOBIO");
            CompactContextTitleText.Text =
                !string.IsNullOrWhiteSpace(activeOrganic.Variant) ? activeOrganic.Variant : activeOrganic.Species;
            SurfaceNavigationText.Text = BuildSurfaceNavigation(state, activeOrganic);
            ExobioBodyProgressText.Text = active?.BodyId == activeOrganic.BodyId
                ? BuildAdaptiveExobioProgress(active, activeOrganic)
                : Loc.Format("Loc_EXOBIO_SURFACE_SAMPLE_PROGRESS_FORMAT", activeOrganic.Stage, state.CurrentBody);
            return;
        }

        if (active is null)
        {
            CompactModeText.Text =
                Loc.Get("Loc_EXPLORATION_MODE_SYSTEM");
            CompactContextTitleText.Text =
                Loc.Get("Loc_EXPLORATION_TARGETS_HEADER");

            ExplorationVisitBodyState[] targets = queueMatchesSystem
                ? queue.Recommended.Take(3).ToArray()
                : Array.Empty<ExplorationVisitBodyState>();

            CompactTargetsItemsControl.ItemsSource =
                targets.Select(BuildAdaptiveTargetLine).ToArray();

            CompactEmptyTargetsText.Visibility =
                targets.Length == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            CompactEmptyTargetsText.Text =
                queueMatchesSystem
                && queue.DeferredCount > 0
                && queue.Recommended.Count == 0
                    ? Loc.Format(
                        "Loc_EXPLORATION_DEFERRED_ONLY_FORMAT",
                        queue.DeferredCount)
                    : state.FssProgress >= 0.999
                        ? Loc.Get(
                            "Loc_EXPLORATION_SYSTEM_COMPLETE_COMPACT")
                        : Loc.Get("Loc_EXPLORATION_NO_TARGETS");

            return;
        }

        CompactModeText.Text =
            Loc.Get("Loc_EXPLORATION_MODE_BODY");
        CompactContextTitleText.Text = active.BodyName;
        BodyStatusText.Text = BuildAdaptiveBodyStatus(active);
        BodyObjectiveText.Text = BuildAdaptiveBodyObjectives(active);
        BodyMissingText.Text = BuildAdaptiveMissingBiology(active);
        BodyMissingText.Visibility =
            string.IsNullOrWhiteSpace(BodyMissingText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        BodyMetaText.Text = BuildAdaptiveBodyMeta(active);
    }

    private static bool QueueMatchesSystem(
        ExplorationVisitQueueSnapshot queue,
        GameStateSnapshot state)
    {
        if (state.SystemAddress != 0 && queue.SystemAddress != 0)
        {
            return state.SystemAddress == queue.SystemAddress;
        }

        return string.Equals(
            queue.SystemName,
            state.StarSystem,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAdaptiveExplorationHeader(
        GameStateSnapshot state,
        ExplorationDataState externalData,
        ExplorationVisitQueueSnapshot queue)
    {
        if (!state.JournalAvailable)
        {
            return Loc.Get(
                "Loc_Waiting_for_Elite_Dangerous_journal");
        }

        int knownBodyCount = Math.Max(
            state.SystemBodyCount,
            externalData.System is { } system
            && string.Equals(
                system.SystemName,
                state.StarSystem,
                StringComparison.OrdinalIgnoreCase)
                ? system.BodyCount
                : 0);

        int resolvedBodyCount = knownBodyCount == 0
            ? state.ScannedBodies
            : Math.Clamp(
                (int)Math.Round(
                    knownBodyCount * state.FssProgress),
                0,
                knownBodyCount);

        string bodyProgress = knownBodyCount > 0
            ? $"{resolvedBodyCount}/{knownBodyCount}"
            : state.ScannedBodies.ToString();

        string result = Loc.Format(
            "Loc_EXPLORATION_HEADER_FORMAT",
            Math.Round(state.FssProgress * 100),
            bodyProgress,
            state.MappedBodies,
            state.BiologicalSignals);

        long localValue = state.ExplorationBodies.Sum(
            ExplorationPresentationValueResolver.ResolveCurrentVisitValue);
        if (localValue > 0)
        {
            result += "  •  "
                + Loc.Format(
                    "Loc_Credits_Short_Format",
                    localValue);
        }

        return result;
    }

    private static string BuildAdaptiveTargetLine(
        ExplorationVisitBodyState item)
    {
        var parts = new List<string>
        {
            item.BodyName,
            BuildCompactFssStatus(item),
            BuildCompactDssStatus(item),
            BuildCompactFootfallStatus(item)
        };

        if (item.BiologyRequired)
        {
            parts.Add(
                $"BIO {item.Progress.CompletedBiologicalSignals}/{item.Progress.BiologicalSignals}");
        }

        if (item.Body.DistanceFromArrivalLs > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Distance_Ls_Value",
                    item.Body.DistanceFromArrivalLs));
        }

        long value = item.Body.EstimatedMappingValue;
        if (value > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Credits_Short_Format",
                    value));
        }

        return string.Join("  •  ", parts);
    }

    private static string BuildCompactFssStatus(
        ExplorationVisitBodyState item)
    {
        if (!item.Progress.FssScanned)
        {
            return "FSS ○";
        }

        return ExplorationDiscoveryStatus.IsFirstDiscoveryCandidate(item.Body)
            ? "FSS ?"
            : "FSS ✓";
    }

    private static string BuildCompactDssStatus(
        ExplorationVisitBodyState item)
    {
        if (!ExplorationDiscoveryStatus.SupportsMapping(item.Body))
        {
            return "DSS —";
        }

        if (item.Progress.DssMapped)
        {
            return item.Progress.DssEfficient
                ? "DSS ◎"
                : "DSS ✓";
        }

        if (item.Body.MappingStatusKnown && item.Body.WasMapped)
        {
            return "DSS ✓";
        }

        return ExplorationDiscoveryStatus.IsFirstMappingCandidate(item.Body)
            ? "DSS ?"
            : "DSS ○";
    }

    private static string BuildCompactFootfallStatus(
        ExplorationVisitBodyState item)
    {
        if (!ExplorationDiscoveryStatus.SupportsFirstFootfall(item.Body))
        {
            return "FF —";
        }

        if (item.Body.FootfallStatusKnown && item.Body.WasFootfalled)
        {
            return "FF ✓";
        }

        return ExplorationDiscoveryStatus.IsFirstFootfallCandidate(item.Body)
            ? "FF ?"
            : "FF ○";
    }

    private static string[] BuildDiscoveryBadges(
        ExplorationCatalogBody body)
    {
        var badges = new List<string>(3);

        if (ExplorationDiscoveryStatus.IsFirstDiscoveryCandidate(body))
        {
            badges.Add(
                Loc.Get(
                    "Loc_EXPLORATION_FIRST_DISCOVERY_SHORT"));
        }

        if (ExplorationDiscoveryStatus.IsFirstMappingCandidate(body))
        {
            badges.Add(
                Loc.Get(
                    "Loc_EXPLORATION_FIRST_MAPPING_SHORT"));
        }

        if (ExplorationDiscoveryStatus.IsFirstFootfallCandidate(body))
        {
            badges.Add(
                Loc.Get(
                    "Loc_EXPLORATION_FIRST_FOOTFALL_SHORT"));
        }

        return badges.ToArray();
    }

    private static string BuildAdaptiveBodyStatus(
        ExplorationVisitBodyState active)
    {
        string fss = active.Progress.FssScanned
            ? "FSS ✓"
            : "FSS ○";

        string dss = !active.DssRequired
            ? "DSS —"
            : active.Progress.DssMapped
                ? active.Progress.DssEfficient
                    ? "DSS ◎"
                    : "DSS ✓"
                : "DSS ○";

        string bio = !active.BiologyRequired
            ? "BIO —"
            : $"BIO {active.Progress.CompletedBiologicalSignals}/{active.Progress.BiologicalSignals}";

        var parts = new List<string>
        {
            fss,
            dss,
            bio
        };

        parts.AddRange(
            BuildDiscoveryBadges(active.Body));

        return string.Join("  •  ", parts);
    }

    private static string BuildAdaptiveBodyObjectives(
        ExplorationVisitBodyState active)
    {
        var pending = new List<string>();

        if (active.FssRequired && !active.Progress.FssScanned)
        {
            pending.Add("FSS");
        }

        if (active.DssRequired && !active.Progress.DssMapped)
        {
            pending.Add("DSS");
        }

        if (active.BiologyRequired && !active.Progress.BiologyComplete)
        {
            pending.Add(
                $"BIO {active.Progress.CompletedBiologicalSignals}/{active.Progress.BiologicalSignals}");
        }

        return pending.Count == 0
            ? Loc.Get("Loc_EXPLORATION_ALL_OBJECTIVES_DONE")
            : Loc.Format(
                "Loc_EXPLORATION_PENDING_FORMAT",
                string.Join(" + ", pending));
    }

    private static string BuildAdaptiveMissingBiology(
        ExplorationVisitBodyState active)
    {
        if (!active.BiologyRequired || active.Progress.BiologyComplete)
        {
            return string.Empty;
        }

        string known = active.Progress.MissingGenuses.Count > 0
            ? string.Join(" · ", active.Progress.MissingGenuses)
            : string.Empty;
        int unknownCount = Math.Max(
            0,
            active.Progress.RemainingBiologicalSignals
                - active.Progress.MissingGenuses.Count);
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(known))
        {
            parts.Add(
                Loc.Format(
                    "Loc_EXPLORATION_MISSING_GENUSES_FORMAT",
                    known));
        }

        if (unknownCount > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_EXPLORATION_UNKNOWN_GENUSES_FORMAT",
                    unknownCount));
        }

        if (active.Progress.HistoricalBiologyDetailIncomplete)
        {
            parts.Add(
                Loc.Get(
                    "Loc_EXPLORATION_HISTORY_BIO_DETAIL_INCOMPLETE"));
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildAdaptiveBodyMeta(
        ExplorationVisitBodyState active)
    {
        var parts = new List<string>();
        string type = string.IsNullOrWhiteSpace(active.Body.Subtype)
            ? active.Body.Type
            : active.Body.Subtype;

        if (!string.IsNullOrWhiteSpace(type))
        {
            parts.Add(type);
        }

        if (active.Body.DistanceFromArrivalLs > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Distance_Ls_Value",
                    active.Body.DistanceFromArrivalLs));
        }

        if (active.Body.Landable && active.Body.GravityG > 0)
        {
            parts.Add($"{active.Body.GravityG:0.00} g");
        }

        if (active.Body.EstimatedMappingValue > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Credits_Short_Format",
                    active.Body.EstimatedMappingValue));
        }

        return string.Join("  •  ", parts);
    }

    private static string BuildAdaptiveExobioProgress(
        ExplorationVisitBodyState active,
        OrganicScanProgressSnapshot organic)
    {
        string result = Loc.Format(
            "Loc_EXPLORATION_EXOBIO_PROGRESS_FORMAT",
            $"{organic.Stage}/3",
            $"BIO {active.Progress.CompletedBiologicalSignals}/{active.Progress.BiologicalSignals}");
        string missing = BuildAdaptiveMissingBiology(active);

        return string.IsNullOrWhiteSpace(missing)
            ? result
            : result + Environment.NewLine + missing;
    }

    private string BuildSurfaceNavigation(
        GameStateSnapshot state,
        OrganicScanProgressSnapshot active)
    {
        OrganicSampleSeparation separation = OrganicSampleNavigation.Assess(state, active);
        OrganicSampleGuidancePresentation presentation = OrganicSampleGuidancePresentation.Create(separation);
        SurfaceNavigationDetailText.Text = Loc.Format(presentation.DetailKey, presentation.DetailArguments);
        SurfaceEscapeArrow.Visibility = separation.EscapeRelativeTurnDegrees is not null && !separation.IsReady
            && separation.HasCompleteHistory && active.ColonyRangeMeters > 0
            && state.HeadingDegrees is not null
                ? Visibility.Visible : Visibility.Collapsed;

        SurfaceEscapeArrowTransform.Angle =
            separation.EscapeRelativeTurnDegrees ?? 0;
        if (separation.EscapeRelativeTurnDegrees is { } turn)
            SurfaceNavigationDetailText.Text += Environment.NewLine + Loc.Format(
                "Loc_EXOBIO_ESCAPE_DIRECTION_FORMAT", separation.EscapeBearingDegrees, FormatRelativeTurn(turn));
        return Loc.Format(presentation.HeadlineKey, presentation.HeadlineArguments);
    }

    private static string FormatRelativeTurn(double degrees)
    {
        if (Math.Abs(degrees) < 5)
        {
            return Loc.Get("Loc_STRAIGHT_AHEAD");
        }

        return Loc.Format(
            degrees < 0
                ? "Loc_TURN_LEFT_FORMAT"
                : "Loc_TURN_RIGHT_FORMAT",
            Math.Abs(degrees));
    }

    private static string BuildCompactRouteOrAlert(
        GameStateSnapshot state,
        ExplorationVisitQueueSnapshot? queue)
    {
        FuelRouteAssessment fuel = FuelRouteAdvisor.Evaluate(state);

        if (fuel.Severity
            is FuelRouteSeverity.Critical
            or FuelRouteSeverity.Caution)
        {
            return BuildFuelAdvice(fuel);
        }

        ExplorationRoutePlan route =
            ExplorationRouteService.Instance.Current;
        if (route.NextStop is { } next)
        {
            return Loc.Format(
                "Loc_EXPLORATION_ROUTE_NEXT_HUD_FORMAT",
                next.System,
                Math.Min(
                    route.Stops.Count,
                    route.CurrentIndex + 2),
                route.Stops.Count);
        }

        if (queue is { DeferredCount: > 0 })
        {
            return Loc.Format(
                "Loc_EXPLORATION_DEFERRED_HUD_FORMAT",
                queue.DeferredCount);
        }

        return string.Empty;
    }

    private static string BuildFuelAdvice(FuelRouteAssessment fuel)
    {
        string status = Loc.Get(fuel.Severity switch
        {
            FuelRouteSeverity.Critical => "Loc_FUEL_CRITICAL",
            FuelRouteSeverity.Caution => "Loc_FUEL_CAUTION",
            FuelRouteSeverity.Safe => "Loc_FUEL_SAFE",
            _ => "Loc_FUEL_UNKNOWN"
        });

        string line = Loc.Format(
            "Loc_FUEL_ROUTE_STATUS_FORMAT",
            status,
            fuel.FuelPercent,
            fuel.RemainingJumps);

        if (fuel.JumpsToNextScoopable is not { } jumps)
        {
            return fuel.RemainingJumps > 0
                ? line + Environment.NewLine
                  + Loc.Get("Loc_FUEL_NO_SCOOPABLE_ON_ROUTE")
                : line;
        }

        string next = Loc.Format(
            "Loc_FUEL_NEXT_SCOOPABLE_FORMAT",
            fuel.NextScoopableSystem,
            jumps);

        if (fuel.EstimatedFuelToNextScoopable is not { } needed)
        {
            return line + Environment.NewLine + next;
        }

        return line
               + Environment.NewLine
               + next
               + Environment.NewLine
               + Loc.Format(
                   "Loc_FUEL_ESTIMATE_FORMAT",
                   needed,
                   fuel.EmergencyReserve);
    }

    private static string BuildDestinationVisitStatus(
        GameStateSnapshot state)
    {
        ExplorationJumpVisitStatusSnapshot jump =
            ExplorationHistoryService.Instance.JumpVisitStatus;
        if (!jump.Available)
        {
            return string.Empty;
        }

        if (jump.Arrived
            && !jump.SystemName.Equals(
                state.StarSystem,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string phase = Loc.Get(
            jump.Arrived
                ? "Loc_EXPLORATION_VISIT_PHASE_ARRIVED"
                : "Loc_EXPLORATION_VISIT_PHASE_JUMP");

        if (!jump.WasVisitedBeforeJump)
        {
            return Loc.Format(
                "Loc_EXPLORATION_NO_PERSONAL_HISTORY_FORMAT",
                phase,
                jump.SystemName);
        }

        string lastVisit = jump.LastVisitedUtc is { } timestamp
            ? timestamp.ToLocalTime().ToString("g")
            : Loc.Get("Loc_VALUE_UNKNOWN");

        return Loc.Format(
            "Loc_EXPLORATION_PERSONAL_HISTORY_FORMAT",
            phase,
            jump.SystemName,
            lastVisit);
    }

    private static string BuildAdaptiveExplorationFooter(
        GameStateSnapshot state,
        ExplorationVisitQueueSnapshot queue)
    {
        if (QueueMatchesSystem(queue, state)
            && queue.DeferredCount > 0)
        {
            return Loc.Format(
                "Loc_EXPLORATION_FOOTER_QUEUE_FORMAT",
                queue.DeferredCount,
                queue.CompletedCount);
        }

        return BuildExplorationFooter(state);
    }

    private static string BuildExplorationFooter(GameStateSnapshot state)
    {
        ExplorationBodySnapshot? notable =
            state.ExplorationBodies.LastOrDefault(
                body => body.IsNotable);
        if (notable is not null)
        {
            return Loc.Format(
                "Loc_Exploration_notable_format",
                notable.Name,
                Loc.Get(notable.Interest switch
                {
                    ExplorationInterest.EarthLike =>
                        "Loc_Interest_EarthLike",
                    ExplorationInterest.WaterWorld =>
                        "Loc_Interest_WaterWorld",
                    ExplorationInterest.AmmoniaWorld =>
                        "Loc_Interest_AmmoniaWorld",
                    ExplorationInterest.NeutronStar =>
                        "Loc_Interest_NeutronStar",
                    ExplorationInterest.BlackHole =>
                        "Loc_Interest_BlackHole",
                    _ => "Loc_Interest_Terraformable"
                }),
                notable.DistanceFromArrivalLs);
        }

        NavRouteStar? hazardous = state.NavRoute
            .Skip(1)
            .FirstOrDefault(
                star => star.IsNeutron || star.IsWhiteDwarf);
        if (hazardous is not null)
        {
            return Loc.Format(
                "Loc_Exploration_route_hazard_format",
                hazardous.System,
                hazardous.StarClass);
        }

        if (state.NavRoute.Count > 2
            && state.NavRoute
                .Skip(1)
                .Take(state.NavRoute.Count - 2)
                .All(star => !star.IsScoopable))
        {
            return Loc.Get(
                "Loc_Exploration_route_no_scoopable_stars");
        }

        return state.NewCodexEntries > 0
            ? Loc.Format(
                "Loc_Exploration_codex_format",
                state.NewCodexEntries)
            : Loc.Get("Loc_Exploration_scan_hint");
    }

    private void DragHandle_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragRequested?.Invoke();
        }
    }

    private void DispatchRefresh()
    {
        if (disposed)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            RefreshPresentation();
            return;
        }

        Dispatcher.BeginInvoke(new Action(RefreshPresentation));
    }

    private void OnExplorationDataChanged(
        object? sender,
        ExplorationDataChangedEventArgs e) =>
        DispatchRefresh();

    private void OnExplorationHistoryChanged(
        object? sender,
        ExplorationHistoryChangedEventArgs e) =>
        DispatchRefresh();

    private void OnExplorationVisitStateChanged(
        object? sender,
        ExplorationVisitStateChangedEventArgs e) =>
        DispatchRefresh();

    private void OnExplorationRouteChanged(
        object? sender,
        ExplorationRouteChangedEventArgs e) =>
        DispatchRefresh();

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () => OnSettingsChanged(sender, e)));
            return;
        }

        SetChromeStyle(e.Settings.OverlayChromeStyle);
        ApplyFullWorkspaceSettings(e.Settings);
        RefreshPresentation();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        dssPresentationTimer.Stop();
        DisposeFullWorkspace();
        ExplorationDataService.Instance.DataChanged -= OnExplorationDataChanged;
        ExplorationHistoryService.Instance.HistoryChanged -= OnExplorationHistoryChanged;
        ExplorationVisitStateService.Instance.Changed -= OnExplorationVisitStateChanged;
        ExplorationRouteService.Instance.RouteChanged -= OnExplorationRouteChanged;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
    }
}
