using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Utils;
using EDActivityOverlay.Windows;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Live research prototype for DSS HUD geometry. Frontier GuiFocus=10 owns the
/// DSS session, while foreground/minimized state owns overlay visibility.
/// Capture/CV run off the WPF dispatcher so live rendering is not gated by
/// diagnostic disk I/O.
/// </summary>
internal sealed class DssPrototypeController : IDisposable
{
    private static readonly TimeSpan CaptureInterval =
        TimeSpan.FromMilliseconds(66);
    private static readonly TimeSpan HiddenPollInterval =
        TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ExitGrace =
        TimeSpan.FromSeconds(2);

    private readonly Func<IntPtr> targetWindowProvider;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer exitGraceTimer;
    private readonly DssHudGeometryDetector detector = new();
    private readonly DssHudGeometryTracker tracker = new();
    private readonly DssAssistantReadinessEvaluator readinessEvaluator = new();
    private readonly DssImpactCounterChangeDetector impactCounterDetector =
        new();
    private readonly DssProbeFlightCorrelator flightCorrelator =
        new();
    private readonly DssCoverageObserver coverageObserver =
        new();

    private DssWindowGraphicsCapture? windowGraphicsCapture;

    // Independent presentation-only motion path. It consumes the same newest
    // WGC CPU frame as heavy CV, but with its own version cursor and a much
    // cheaper coarse-to-fine matcher.
    private readonly DssFastVisualMotionTracker fastVisualMotionTracker =
        new();

    private CancellationTokenSource? fastVisualMotionCancellation;
    private Task? fastVisualMotionTask;

    private readonly object fastVisualUpdateGate =
        new();

    private DssFastVisualMotionSnapshot? latestFastVisualUpdate;
    private bool fastVisualUpdateDispatchPending;

    // Visual updates are latest-frame-wins. The old implementation queued one
    // Dispatcher operation per CV result, so the WPF overlay could render
    // frames that were already 2+ CV cycles old while Elite itself had moved
    // on. Keep at most one pending visual snapshot.
    private readonly object overlayUpdateGate = new();
    private Action? latestOverlayUpdateAction;
    private bool overlayUpdateDispatchPending;

    private DssPrototypeOverlayWindow? overlay;
    private DssPrototypeSessionLogger? sessionLogger;
    private DssFireInputMonitor? fireInputMonitor;
    private DssProbeLaunchFrameSnapshot? latestLaunchFrame;
    private EliteGraphicsSettingsSnapshot graphics =
        EliteGraphicsSettingsSnapshot.Default;
    private DssModuleSnapshot dssModule =
        DssModuleSnapshot.Empty;
    private GameStateSnapshot latestState =
        GameStateSnapshot.Empty;
    private DssPrototypeSessionContext? sessionContext;

    private CancellationTokenSource? captureCancellation;
    private Task? captureTask;
    private long frameSequence;
    private int launchSequence;

    // Step 1 is visible before the first shot. A non-MISS fire advances the
    // sequence; SAAScanComplete suppresses further targets.
    private int targetingSequentialStep = 1;
    private int targetingScanComplete;
    private int targetingConfirmedImpacts;
    private long targetingUsedCoverageCandidates;

    // Persist sequential progress across a finalized DSS overlay session when
    // the player re-enters the same body. The first v25 validation was split
    // by GuiFocus loss after step #5; resetting to #1 would discard real scan
    // progress even though Elite keeps already-landed probes.
    private long targetingProgressSystemAddress;
    private int targetingProgressBodyId = -1;
    private string targetingProgressBodyName = string.Empty;

    private bool sessionActive;
    private bool captureActive;
    private int requestedOverlayVisibility = -1;
    private int dssSignatureHits;
    private int dssSignatureMisses;
    private bool disposed;

    public DssPrototypeController(
        Func<IntPtr> targetWindowProvider,
        Dispatcher dispatcher)
    {
        this.targetWindowProvider = targetWindowProvider;
        this.dispatcher = dispatcher;

        exitGraceTimer = new DispatcherTimer(
            ExitGrace,
            DispatcherPriority.Background,
            ExitGraceTimer_Tick,
            dispatcher);
        exitGraceTimer.Stop();

        JournalMonitorService.Instance.StateChanged +=
            OnJournalStateChanged;
        JournalMonitorService.Instance.JournalEventReceived +=
            OnJournalEventReceived;

        latestState = JournalMonitorService.Instance.Current;
        dssModule =
            DssJournalContextReader.ReadLatestDssModule(
                JournalMonitorService.Instance.JournalDirectory);

        if (latestState.GuiFocus == 10)
        {
            dispatcher.BeginInvoke(
                new Action(() => EnterDss(latestState)));
        }
    }

    private void OnJournalStateChanged(
        object? sender,
        GameStateChangedEventArgs e)
    {
        latestState = e.State;

        dispatcher.BeginInvoke(
            new Action(() =>
                HandleDssState(e.State)));
    }

    private void HandleDssState(
        GameStateSnapshot state)
    {
        if (disposed)
        {
            return;
        }

        if (state.GuiFocus == 10)
        {
            exitGraceTimer.Stop();

            if (!sessionActive)
            {
                EnterDss(state);
            }
            else if (!captureActive)
            {
                ResumeDssCapture();
            }

            return;
        }

        if (sessionActive && captureActive)
        {
            LeaveDssCapture();
        }
    }

    private void EnterDss(
        GameStateSnapshot state)
    {
        IntPtr targetWindow = targetWindowProvider();
        if (targetWindow == IntPtr.Zero
            || !WindowsAPI.IsWindow(targetWindow))
        {
            Logger.Logger.Warning(
                "DSS prototype: GuiFocus=10 detected, but Elite target window is unavailable.");
            return;
        }

        graphics = EliteGraphicsSettingsReader.Read();
        dssModule =
            DssJournalContextReader.ReadLatestDssModule(
                JournalMonitorService.Instance.JournalDirectory);

        frameSequence = 0;
        launchSequence = 0;
        latestLaunchFrame = null;
        impactCounterDetector.Reset();
        flightCorrelator.Reset();
        sessionActive = true;
        captureActive = true;
        requestedOverlayVisibility = -1;
        dssSignatureHits = 0;
        dssSignatureMisses = 0;
        tracker.Reset();
        fastVisualMotionTracker.Reset();
        readinessEvaluator.Reset();

        windowGraphicsCapture?.Dispose();
        windowGraphicsCapture = null;

        bool wgcStarted =
            DssWindowGraphicsCapture.TryStart(
                targetWindow,
                out windowGraphicsCapture,
                out string wgcFailure);

        if (wgcStarted)
        {
            Logger.Logger.Info(
                "DSS capture source: Windows.Graphics.Capture HWND; " +
                "overlay is visible to external screenshots/recorders.");
        }
        else
        {
            Logger.Logger.Warning(
                "DSS WGC unavailable; falling back to desktop GDI and " +
                $"keeping overlay excluded from capture. Reason: {wgcFailure}");
        }

        int captureWidth = 1920;
        int captureHeight = 1080;
        if (WindowsAPI.GetWindowRect(
                targetWindow,
                out WindowsAPI.RECT rect))
        {
            captureWidth = Math.Max(
                1,
                rect.Right - rect.Left);
            captureHeight = Math.Max(
                1,
                rect.Bottom - rect.Top);
        }

        sessionContext = BuildSessionContext(
            state,
            captureWidth,
            captureHeight);

        ResumeOrResetSequentialTargeting(
            sessionContext);

        sessionLogger =
            new DssPrototypeSessionLogger(
                sessionContext);

        StartFireInputMonitor();

        overlay = new DssPrototypeOverlayWindow(
            targetWindow,
            excludeFromCapture:
                !wgcStarted);
        overlay.SetContext(
            state,
            sessionContext,
            sessionLogger.SessionId);

        StartCaptureLoop();

        Logger.Logger.Info(
            $"DSS prototype ENTER: system='{state.StarSystem}', " +
            $"body='{sessionContext.BodyName}', bodyId={sessionContext.BodyId}, " +
            $"radius={sessionContext.BodyRadiusMeters:0}, " +
            $"FOV={graphics.VerticalFovDegrees:0.###}, " +
            $"DSS_PatchRadius={dssModule.PatchRadius:0.###}.");
    }

    private void ResumeOrResetSequentialTargeting(
        DssPrototypeSessionContext context)
    {
        bool sameBody =
            IsSameTargetingBody(
                targetingProgressSystemAddress,
                targetingProgressBodyId,
                targetingProgressBodyName,
                context);

        if (!sameBody)
        {
            targetingProgressSystemAddress =
                context.SystemAddress;
            targetingProgressBodyId =
                context.BodyId;
            targetingProgressBodyName =
                context.BodyName;

            Interlocked.Exchange(
                ref targetingSequentialStep,
                1);
            Interlocked.Exchange(
                ref targetingScanComplete,
                0);
            Interlocked.Exchange(
                ref targetingConfirmedImpacts,
                0);
            Interlocked.Exchange(
                ref targetingUsedCoverageCandidates,
                0);
            coverageObserver.Reset();

            Logger.Logger.Info(
                $"DSS TARGETING new body: system={context.SystemAddress}; " +
                $"body={context.BodyId} '{context.BodyName}'; step=1.");
            return;
        }

        Logger.Logger.Info(
            $"DSS TARGETING resumed same body at step " +
            $"{Volatile.Read(ref targetingSequentialStep)}; " +
            $"complete={Volatile.Read(ref targetingScanComplete) != 0}.");
    }

    internal static bool IsSameTargetingBody(
        long previousSystemAddress,
        int previousBodyId,
        string previousBodyName,
        DssPrototypeSessionContext context)
    {
        if (previousSystemAddress == 0
            || context.SystemAddress == 0
            || previousSystemAddress
               != context.SystemAddress)
        {
            return false;
        }

        if (previousBodyId >= 0
            && context.BodyId >= 0)
        {
            return previousBodyId
                   == context.BodyId;
        }

        return !string.IsNullOrWhiteSpace(
                   previousBodyName)
               && !string.IsNullOrWhiteSpace(
                   context.BodyName)
               && previousBodyName.Equals(
                   context.BodyName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private DssPrototypeSessionContext BuildSessionContext(
        GameStateSnapshot state,
        int captureWidth,
        int captureHeight)
    {
        ExplorationBodySnapshot? body = null;

        if (state.DestinationBodyId >= 0)
        {
            body = state.ExplorationBodies.FirstOrDefault(
                item =>
                    item.BodyId == state.DestinationBodyId);
        }

        if (body is null
            && !string.IsNullOrWhiteSpace(
                state.DestinationName))
        {
            body = state.ExplorationBodies.FirstOrDefault(
                item =>
                    item.Name.Equals(
                        state.DestinationName,
                        StringComparison.OrdinalIgnoreCase));
        }

        string bodyName =
            !string.IsNullOrWhiteSpace(state.DestinationName)
                ? state.DestinationName
                : body?.Name ?? string.Empty;
        int bodyId =
            state.DestinationBodyId >= 0
                ? state.DestinationBodyId
                : body?.BodyId ?? -1;
        double radius =
            body?.RadiusMeters ?? 0;

        if (radius <= 0)
        {
            DssBodyScanSnapshot journalBody =
                DssJournalContextReader.ResolveBodyScan(
                    JournalMonitorService.Instance.JournalDirectory,
                    state.SystemAddress,
                    bodyId,
                    bodyName);

            if (journalBody.RadiusMeters > 0)
            {
                radius = journalBody.RadiusMeters;

                if (bodyId < 0)
                {
                    bodyId = journalBody.BodyId;
                }

                if (string.IsNullOrWhiteSpace(bodyName))
                {
                    bodyName = journalBody.BodyName;
                }
            }
        }

        return new DssPrototypeSessionContext(
            state.Commander,
            state.StarSystem,
            state.SystemAddress,
            bodyName,
            bodyId,
            radius,
            graphics.VerticalFovDegrees,
            dssModule.PatchRadius,
            dssModule.OriginalPatchRadius,
            dssModule.Blueprint,
            dssModule.EngineeringLevel,
            captureWidth,
            captureHeight);
    }

    private void ResumeDssCapture()
    {
        captureActive = true;
        impactCounterDetector.Reset();

        if (fireInputMonitor is not null)
        {
            fireInputMonitor.Enabled = true;
        }

        StartCaptureLoop();

        Logger.Logger.Info(
            "DSS prototype: DSS GuiFocus returned during exit grace; capture resumed.");
    }

    private void LeaveDssCapture()
    {
        captureActive = false;
        impactCounterDetector.Reset();

        if (fireInputMonitor is not null)
        {
            fireInputMonitor.Enabled = false;
        }

        StopCaptureLoop();
        RequestOverlayVisibility(false);

        exitGraceTimer.Stop();
        exitGraceTimer.Start();

        Logger.Logger.Info(
            "DSS prototype LEAVE: overlay hidden; logger kept open for 2 s for trailing journal events.");
    }

    private void ExitGraceTimer_Tick(
        object? sender,
        EventArgs e)
    {
        exitGraceTimer.Stop();
        FinalizeSession("GuiFocus left DSS");
    }

    private void StartCaptureLoop()
    {
        StopCaptureLoop();

        captureCancellation =
            new CancellationTokenSource();
        CancellationToken token =
            captureCancellation.Token;

        captureTask = Task.Run(
            () => CaptureLoopAsync(token),
            token);

        StartFastVisualMotionLoop();
    }

    private void StartFastVisualMotionLoop()
    {
        StopFastVisualMotionLoop();

        if (windowGraphicsCapture is null)
        {
            return;
        }

        fastVisualMotionCancellation =
            new CancellationTokenSource();

        CancellationToken token =
            fastVisualMotionCancellation.Token;

        fastVisualMotionTask =
            Task.Run(
                () => FastVisualMotionLoopAsync(
                    token),
                token);
    }

    private void StopFastVisualMotionLoop()
    {
        CancellationTokenSource? cancellation =
            fastVisualMotionCancellation;

        fastVisualMotionCancellation = null;
        fastVisualMotionTask = null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void StopCaptureLoop()
    {
        StopFastVisualMotionLoop();

        CancellationTokenSource? cancellation =
            captureCancellation;

        captureCancellation = null;
        captureTask = null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task CaptureLoopAsync(
        CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested
                   && sessionActive
                   && captureActive)
            {
                Stopwatch cycle = Stopwatch.StartNew();

                IntPtr targetWindow =
                    targetWindowProvider();

                bool foregroundVisible =
                    ShouldShowForTarget(targetWindow);

                if (!foregroundVisible)
                {
                    RequestOverlayVisibility(false);

                    await Task.Delay(
                        HiddenPollInterval,
                        token).ConfigureAwait(false);
                    continue;
                }

                Stopwatch captureWatch =
                    Stopwatch.StartNew();

                bool captured;
                DssCapturedFrame? frame;
                double captureMilliseconds;

                DssWindowGraphicsCapture? wgc =
                    windowGraphicsCapture;

                if (wgc is not null)
                {
                    captured =
                        wgc.TryGetLatestFrame(
                            out frame);

                    captureWatch.Stop();

                    captureMilliseconds =
                        wgc.LastCopyMilliseconds;
                }
                else
                {
                    captured =
                        DssScreenCapture.TryCaptureTarget(
                            targetWindow,
                            out frame);

                    captureWatch.Stop();

                    captureMilliseconds =
                        captureWatch.Elapsed
                            .TotalMilliseconds;
                }

                if (captured && frame is not null)
                {
                    bool dssScreen =
                        DssScreenSignatureDetector.IsDssScreen(
                            frame);

                    if (dssScreen)
                    {
                        dssSignatureHits++;
                        dssSignatureMisses = 0;

                        if (dssSignatureHits >= 2)
                        {
                            RequestOverlayVisibility(true);
                        }

                        // Even if the overlay is still hidden during the first
                        // confirmation frame, keep feeding valid DSS pixels to
                        // the persistent tracker so it can reacquire before
                        // the window becomes visible again.
                    }
                    else
                    {
                        dssSignatureHits = 0;
                        dssSignatureMisses++;

                        if (dssSignatureMisses >= 3)
                        {
                            RequestOverlayVisibility(false);

                            // IMPORTANT v9 change:
                            // visual signature controls only visibility.
                            // Never Reset() C/Rh here. In v8 false-negative
                            // signature gaps destroyed an otherwise correct
                            // Rh~400 track and allowed later false acquisition.
                            //
                            // Do not feed cockpit/menu pixels into the DSS CV
                            // while the visual signature is convincingly gone.
                            cycle.Stop();

                            TimeSpan hiddenDelay =
                                CaptureInterval - cycle.Elapsed;

                            if (hiddenDelay > TimeSpan.Zero)
                            {
                                await Task.Delay(
                                    hiddenDelay,
                                    token).ConfigureAwait(false);
                            }

                            continue;
                        }

                        // One/two visual misses are treated as signature
                        // uncertainty. Keep processing; the tracker itself is
                        // conservative and this avoids 1-2 second state holes
                        // from side-scale flicker.
                    }

                    Stopwatch detectWatch =
                        Stopwatch.StartNew();

                    DssHudTrackResult tracking =
                        tracker.Process(
                            frame,
                            detector,
                            graphics.VerticalFovDegrees);

                    detectWatch.Stop();

                    fastVisualMotionTracker.UpdateHeavyAnchor(
                        frame,
                        tracking);

                    long sequence =
                        Interlocked.Increment(
                            ref frameSequence);

                    GameStateSnapshot state =
                        latestState;
                    DssPrototypeSessionContext? context =
                        sessionContext;
                    DssPrototypeSessionLogger? logger =
                        sessionLogger;

                    if (context is not null)
                    {
                        DssAimMissObservation missObservation =
                            DssAimMissIndicatorDetector.Detect(
                                frame);

                        DssImpactCounterObservation impactObservation =
                            impactCounterDetector.Process(
                                frame);

                        logger?.LogImpactCounterObservation(
                            sequence,
                            frame.TimestampUtc,
                            impactObservation);

                        if (impactObservation.Changed
                            && flightCorrelator.HasPendingLaunches)
                        {
                            DssProbeImpactCorrelation impact =
                                flightCorrelator.RegisterImpact(
                                    frame.TimestampUtc,
                                    sequence,
                                    impactObservation.ChangeRatio);

                            logger?.LogProbeImpact(
                                impact);

                            if (impact.MatchedLaunchSequence > 0)
                            {
                                int confirmed =
                                    Interlocked.Increment(
                                        ref targetingConfirmedImpacts);

                                coverageObserver.NotifyImpact(
                                    frame.TimestampUtc);

                                Logger.Logger.Info(
                                    $"DSS TARGETING impact confirmed: {confirmed}; " +
                                    $"launch={impact.MatchedLaunchSequence}.");
                            }

                            Logger.Logger.Info(
                                $"DSS IMPACT #{impact.ImpactSequence}: " +
                                $"launch={impact.MatchedLaunchSequence}; " +
                                $"flight={impact.FlightMilliseconds:0} ms; " +
                                $"method={impact.CorrelationMethod}; " +
                                $"candidates={impact.CandidateCount}; " +
                                $"delta={impact.CounterChangeRatio:0.####}.");
                        }

                        foreach (DssProbeUnresolvedLaunch unresolved
                                 in flightCorrelator.Expire(
                                     frame.TimestampUtc))
                        {
                            logger?.LogUnresolvedLaunch(
                                unresolved);

                            Logger.Logger.Info(
                                $"DSS LAUNCH #{unresolved.LaunchSequence} " +
                                $"unresolved after {unresolved.AgeMilliseconds / 1000d:0.0}s.");
                        }

                        DssAssistantReadinessSnapshot readiness =
                            readinessEvaluator.Evaluate(
                                state,
                                context,
                                frame,
                                tracking.Geometry);

                        int confirmedImpacts =
                            Volatile.Read(
                                ref targetingConfirmedImpacts);

                        long usedCoverageCandidates =
                            Volatile.Read(
                                ref targetingUsedCoverageCandidates);

                        DssCoverageObservation coverageObservation =
                            coverageObserver.Process(
                                frame,
                                tracking.Geometry,
                                confirmedImpacts > 0,
                                usedCoverageCandidates);

                        logger?.LogFrame(
                            sequence,
                            state,
                            context,
                            frame,
                            tracking,
                            readiness,
                            missObservation,
                            coverageObservation,
                            captureMilliseconds,
                            detectWatch.Elapsed.TotalMilliseconds);

                        Volatile.Write(
                            ref latestLaunchFrame,
                            new DssProbeLaunchFrameSnapshot(
                                sequence,
                                frame.TimestampUtc,
                                state,
                                readiness,
                                tracking.Geometry,
                                missObservation,
                                coverageObservation,
                                confirmedImpacts,
                                usedCoverageCandidates));

                        QueueOverlayUpdate(
                            frame,
                            tracking,
                            readiness,
                            state,
                            context,
                            sequence,
                            coverageObservation);
                    }
                }

                cycle.Stop();
                TimeSpan delay =
                    CaptureInterval - cycle.Elapsed;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(
                        delay,
                        token).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS prototype capture loop failed: {ex.Message}");
        }
    }

    private async Task FastVisualMotionLoopAsync(
        CancellationToken token)
    {
        long wgcVersion = 0;
        long accepted = 0;
        long rejected = 0;
        double totalTrackMilliseconds = 0d;
        double totalFrameAgeMilliseconds = 0d;
        double maximumFrameAgeMilliseconds = 0d;
        long frameAgeSamples = 0;

        DateTimeOffset nextDiagnosticsUtc =
            DateTimeOffset.UtcNow
            + TimeSpan.FromSeconds(3);

        try
        {
            while (!token.IsCancellationRequested
                   && sessionActive
                   && captureActive)
            {
                DssWindowGraphicsCapture? wgc =
                    windowGraphicsCapture;

                if (wgc is null)
                {
                    return;
                }

                IntPtr targetWindow =
                    targetWindowProvider();

                if (!ShouldShowForTarget(
                        targetWindow))
                {
                    await Task.Delay(
                        25,
                        token).ConfigureAwait(false);

                    continue;
                }

                if (!wgc.TryGetLatestFrameAfter(
                        ref wgcVersion,
                        out DssCapturedFrame? frame)
                    || frame is null)
                {
                    await Task.Delay(
                        4,
                        token).ConfigureAwait(false);

                    continue;
                }

                double frameAgeMilliseconds =
                    Math.Max(
                        0d,
                        (DateTimeOffset.UtcNow
                         - frame.TimestampUtc)
                        .TotalMilliseconds);

                totalFrameAgeMilliseconds +=
                    frameAgeMilliseconds;

                maximumFrameAgeMilliseconds =
                    Math.Max(
                        maximumFrameAgeMilliseconds,
                        frameAgeMilliseconds);

                frameAgeSamples++;

                Stopwatch watch =
                    Stopwatch.StartNew();

                bool tracked =
                    fastVisualMotionTracker.TryTrack(
                        frame,
                        out DssFastVisualMotionSnapshot? motion);

                watch.Stop();

                totalTrackMilliseconds +=
                    watch.Elapsed.TotalMilliseconds;

                if (tracked
                    && motion is not null)
                {
                    accepted++;

                    QueueFastVisualMotion(
                        motion);
                }
                else
                {
                    rejected++;
                }

                DateTimeOffset now =
                    DateTimeOffset.UtcNow;

                if (now >= nextDiagnosticsUtc)
                {
                    long samples =
                        accepted + rejected;

                    double averageMilliseconds =
                        samples > 0
                            ? totalTrackMilliseconds
                              / samples
                            : 0d;

                    double averageFrameAgeMilliseconds =
                        frameAgeSamples > 0
                            ? totalFrameAgeMilliseconds
                              / frameAgeSamples
                            : 0d;

                    Logger.Logger.Info(
                        $"DSS FAST VISUAL: ok={accepted}; reject={rejected}; " +
                        $"avg={averageMilliseconds:0.00} ms; " +
                        $"frameAgeAvg={averageFrameAgeMilliseconds:0.0} ms; " +
                        $"frameAgeMax={maximumFrameAgeMilliseconds:0.0} ms; " +
                        $"copyAge={wgc.LastPublishedFrameAgeMilliseconds:0.0} ms.");

                    accepted = 0;
                    rejected = 0;
                    totalTrackMilliseconds = 0;
                    totalFrameAgeMilliseconds = 0;
                    maximumFrameAgeMilliseconds = 0;
                    frameAgeSamples = 0;

                    nextDiagnosticsUtc =
                        now
                        + TimeSpan.FromSeconds(3);
                }

                await Task.Delay(
                    4,
                    token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS fast visual motion loop failed: {ex.Message}");
        }
    }

    private void QueueFastVisualMotion(
        DssFastVisualMotionSnapshot motion)
    {
        bool schedule;

        lock (fastVisualUpdateGate)
        {
            latestFastVisualUpdate =
                motion;

            schedule =
                !fastVisualUpdateDispatchPending;

            if (schedule)
            {
                fastVisualUpdateDispatchPending =
                    true;
            }
        }

        if (schedule)
        {
            dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(
                    DrainLatestFastVisualMotion));
        }
    }

    private void DrainLatestFastVisualMotion()
    {
        while (true)
        {
            DssFastVisualMotionSnapshot? motion;

            lock (fastVisualUpdateGate)
            {
                motion =
                    latestFastVisualUpdate;

                latestFastVisualUpdate =
                    null;

                if (motion is null)
                {
                    fastVisualUpdateDispatchPending =
                        false;

                    return;
                }
            }

            DssPrototypeOverlayWindow? targetOverlay =
                overlay;

            if (!disposed
                && sessionActive
                && captureActive
                && targetOverlay is not null
                && targetOverlay.IsVisible)
            {
                targetOverlay.UpdateFastVisualMotion(
                    motion);
            }
        }
    }

    private void StartFireInputMonitor()
    {
        fireInputMonitor?.Dispose();
        fireInputMonitor = null;

        try
        {
            DssFireBindingSet bindings =
                DssFireBindingResolver.Resolve();

            sessionLogger?.LogFireBindings(
                bindings);

            if (bindings.Bindings.Count == 0)
            {
                Logger.Logger.Warning(
                    "DSS launch logger: no PrimaryFire/SecondaryFire input bindings were found.");
                return;
            }

            fireInputMonitor =
                new DssFireInputMonitor(
                    bindings,
                    targetWindowProvider);

            fireInputMonitor.FirePressed +=
                OnFireInputPressed;

            fireInputMonitor.Enabled =
                true;

            fireInputMonitor.Start();

            Logger.Logger.Info(
                $"DSS launch logger armed: preset='{bindings.PresetName}', " +
                $"file='{Path.GetFileName(bindings.FilePath)}', " +
                $"bindings={fireInputMonitor.DiagnosticSummary}");
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS launch logger could not resolve Elite fire bindings: {ex.Message}");
        }
    }

    private void OnFireInputPressed(
        object? sender,
        DssFireInputEvent e)
    {
        if (disposed
            || !sessionActive
            || !captureActive
            || latestState.GuiFocus != 10)
        {
            return;
        }

        int sequence =
            Interlocked.Increment(
                ref launchSequence);

        DssProbeLaunchFrameSnapshot? frame =
            Volatile.Read(
                ref latestLaunchFrame);

        int targetingStepAtFire =
            Volatile.Read(
                ref targetingSequentialStep);

        bool targetingCompleteAtFire =
            Volatile.Read(
                ref targetingScanComplete) != 0;

        DssProbeLaunchRecord launch =
            DssProbeLaunchCorrelator.Correlate(
                sequence,
                e,
                frame);

        DssSequentialTargetTelemetry targetingTelemetry =
            DssSequentialTargetTelemetryBuilder.Build(
                targetingStepAtFire,
                targetingCompleteAtFire,
                frame,
                launch);

        sessionLogger?.LogProbeLaunch(
            launch,
            targetingTelemetry);

        bool queuedForImpact =
            flightCorrelator.RegisterLaunch(
                launch);

        // Keep the current step on a native MISS so the user can retry it.
        // GeometryValid is intentionally not required: step #3 is the body
        // centre, where C may briefly become Predicting at convergence.
        if (!launch.HudMissVisible
            && targetingTelemetry.Available
            && Volatile.Read(ref targetingScanComplete) == 0)
        {
            if (targetingTelemetry.CandidateId > 0)
            {
                MarkCoverageCandidateUsed(
                    targetingTelemetry.CandidateId);
            }

            int currentStep = Volatile.Read(ref targetingSequentialStep);

            if (currentStep >= 1
                && currentStep <= (DssPredictiveBatchPlanner.MaximumBatchCount + DssPredictiveBatchPlanner.MaximumCorrectionShots))
            {
                int nextStep = Interlocked.Increment(ref targetingSequentialStep);

                Logger.Logger.Info(
                    $"DSS TARGETING sequence advanced: {currentStep} -> {nextStep}; " +
                    $"launch={sequence}; geometry={launch.GeometryValid}; " +
                    $"r/Rh={launch.AimNormalizedRadius:0.###}.");
            }
        }

        Logger.Logger.Info(
            $"DSS FIRE INPUT #{sequence}: " +
            $"{launch.FireAction}/{launch.BindingSlot} " +
            $"{launch.BindingDevice}:{launch.BindingKey}; " +
            $"geometry={launch.GeometryValid}; " +
            $"r/Rh={launch.AimNormalizedRadius:0.###}; " +
            $"nearest=P{launch.NearestPatternPoint}; " +
            $"error={launch.NearestErrorPixels:0.#} px; " +
            $"hudMiss={launch.HudMissVisible}; " +
            $"pendingHit={queuedForImpact}; " +
            $"frameAge={launch.FrameAgeMilliseconds:0.#} ms.");
    }

    private void MarkCoverageCandidateUsed(
        int candidateId)
    {
        if (candidateId <= 0
            || candidateId >= 63)
        {
            return;
        }

        long bit =
            1L << candidateId;

        while (true)
        {
            long current =
                Volatile.Read(
                    ref targetingUsedCoverageCandidates);

            long updated =
                current | bit;

            if (updated == current
                || Interlocked.CompareExchange(
                    ref targetingUsedCoverageCandidates,
                    updated,
                    current) == current)
            {
                return;
            }
        }
    }
    private void StopFireInputMonitor()
    {
        DssFireInputMonitor? monitor =
            fireInputMonitor;

        fireInputMonitor = null;

        if (monitor is null)
        {
            return;
        }

        monitor.FirePressed -=
            OnFireInputPressed;

        monitor.Dispose();
    }

    private bool ShouldShowForTarget(
        IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero
            || !WindowsAPI.IsWindow(targetWindow)
            || !WindowsAPI.IsWindowVisible(targetWindow)
            || WindowsAPI.IsIconic(targetWindow))
        {
            return false;
        }

        return WindowsAPI.GetForegroundWindow()
               == targetWindow;
    }

    private void RequestOverlayVisibility(
        bool visible)
    {
        int requested = visible ? 1 : 0;
        int previous =
            Interlocked.Exchange(
                ref requestedOverlayVisibility,
                requested);

        if (previous == requested)
        {
            return;
        }

        dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(() =>
            {
                if (disposed || overlay is null)
                {
                    return;
                }

                if (visible
                    && sessionActive
                    && captureActive
                    && latestState.GuiFocus == 10)
                {
                    overlay.ShowPassive();
                }
                else if (overlay.IsVisible)
                {
                    overlay.Hide();
                }
            }));
    }

    private void QueueOverlayUpdate(
        DssCapturedFrame frame,
        DssHudTrackResult tracking,
        DssAssistantReadinessSnapshot readiness,
        GameStateSnapshot state,
        DssPrototypeSessionContext context,
        long sequence,
        DssCoverageObservation coverageObservation)
    {
        DssPrototypeOverlayWindow? targetOverlay =
            overlay;

        // UpdateGeometry never reads frame pixels; it only needs the frame
        // timestamp and dimensions. Do not retain an 8.3 MB 1080p BGRA array
        // inside a queued WPF closure.
        var overlayFrame =
            new DssCapturedFrame(
                frame.TimestampUtc,
                frame.ScreenLeft,
                frame.ScreenTop,
                frame.Width,
                frame.Height,
                frame.Stride,
                Array.Empty<byte>());

        Action update =
            () =>
            {
                if (disposed
                    || !sessionActive
                    || targetOverlay is null
                    || !ReferenceEquals(
                        targetOverlay,
                        overlay)
                    || !targetOverlay.IsVisible)
                {
                    return;
                }

                targetOverlay.UpdateGeometry(
                    overlayFrame,
                    tracking,
                    readiness,
                    state,
                    context,
                    sequence,
                    Volatile.Read(ref targetingSequentialStep),
                    Volatile.Read(ref targetingScanComplete) != 0,
                    Volatile.Read(ref targetingConfirmedImpacts),
                    Volatile.Read(ref targetingUsedCoverageCandidates),
                    coverageObservation);
            };

        bool schedule;

        lock (overlayUpdateGate)
        {
            // Replacing this action is intentional. Logic, logging, MISS,
            // coverage and launch correlation have already consumed every
            // frame on the capture thread; only presentation is coalesced.
            latestOverlayUpdateAction =
                update;

            schedule =
                !overlayUpdateDispatchPending;

            if (schedule)
            {
                overlayUpdateDispatchPending =
                    true;
            }
        }

        if (schedule)
        {
            dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(
                    DrainLatestOverlayUpdate));
        }
    }

    private void DrainLatestOverlayUpdate()
    {
        // Do not return control to WPF after applying an already stale
        // snapshot. A CV result can arrive while UpdateGeometry is running.
        // In v35 that newer result was deferred to another Dispatcher
        // callback, allowing WPF to compose/render the older geometry in
        // between. Drain to the newest available snapshot inside this same
        // callback so only the final state can reach composition.
        while (true)
        {
            Action? update;

            lock (overlayUpdateGate)
            {
                update =
                    latestOverlayUpdateAction;

                latestOverlayUpdateAction =
                    null;

                if (update is null)
                {
                    overlayUpdateDispatchPending =
                        false;

                    return;
                }
            }

            update();
        }
    }

    private void OnJournalEventReceived(
        object? sender,
        JournalEventReceivedEventArgs e)
    {
        if (e.EventName.Equals(
                "Loadout",
                StringComparison.OrdinalIgnoreCase))
        {
            dssModule =
                DssJournalContextReader.ParseDssModule(
                    e.Data);
        }

        if (sessionActive
            && e.EventName.Equals(
                "SAAScanComplete",
                StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref targetingScanComplete, 1);

            Logger.Logger.Info(
                $"DSS TARGETING complete at step {Volatile.Read(ref targetingSequentialStep)}: " +
                "SAAScanComplete received.");
        }

        if (sessionActive)
        {
            sessionLogger?.LogJournalEvent(e);
        }
    }

    private void FinalizeSession(
        string reason)
    {
        if (!sessionActive)
        {
            return;
        }

        StopCaptureLoop();
        StopFireInputMonitor();
        exitGraceTimer.Stop();

        windowGraphicsCapture?.Dispose();
        windowGraphicsCapture = null;

        captureActive = false;
        sessionActive = false;
        requestedOverlayVisibility = -1;

        if (overlay is not null)
        {
            overlay.Close();
            overlay = null;
        }

        sessionLogger?.Complete(
            latestState,
            reason);
        sessionLogger?.Dispose();
        sessionLogger = null;
        sessionContext = null;
        latestLaunchFrame = null;
        // Keep targetingSequentialStep / targetingScanComplete in memory.
        // Elite keeps probe coverage when DSS is closed and reopened, so the
        // assistant must resume the same body instead of restarting at #1.
        impactCounterDetector.Reset();
        flightCorrelator.Reset();
        tracker.Reset();
        readinessEvaluator.Reset();

        Logger.Logger.Info(
            $"DSS prototype session finalized: {reason}");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        JournalMonitorService.Instance.StateChanged -=
            OnJournalStateChanged;
        JournalMonitorService.Instance.JournalEventReceived -=
            OnJournalEventReceived;

        FinalizeSession("Application closing");
        StopCaptureLoop();
        exitGraceTimer.Stop();
    }
}
