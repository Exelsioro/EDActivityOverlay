using System;
using System.Diagnostics;
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

    private DssPrototypeOverlayWindow? overlay;
    private DssPrototypeSessionLogger? sessionLogger;
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
        sessionActive = true;
        captureActive = true;
        requestedOverlayVisibility = -1;
        dssSignatureHits = 0;
        dssSignatureMisses = 0;
        tracker.Reset();
        readinessEvaluator.Reset();

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

        sessionLogger =
            new DssPrototypeSessionLogger(
                sessionContext);

        overlay = new DssPrototypeOverlayWindow(
            targetWindow);
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
        StartCaptureLoop();

        Logger.Logger.Info(
            "DSS prototype: DSS GuiFocus returned during exit grace; capture resumed.");
    }

    private void LeaveDssCapture()
    {
        captureActive = false;
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
    }

    private void StopCaptureLoop()
    {
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

                bool captured =
                    DssScreenCapture.TryCaptureTarget(
                        targetWindow,
                        out DssCapturedFrame? frame);

                captureWatch.Stop();

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
                        DssAssistantReadinessSnapshot readiness =
                            readinessEvaluator.Evaluate(
                                state,
                                context,
                                frame,
                                tracking.Geometry);

                        logger?.LogFrame(
                            sequence,
                            state,
                            context,
                            frame,
                            tracking,
                            readiness,
                            captureWatch.Elapsed.TotalMilliseconds,
                            detectWatch.Elapsed.TotalMilliseconds);

                        QueueOverlayUpdate(
                            frame,
                            tracking,
                            readiness,
                            state,
                            context,
                            sequence);
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
        long sequence)
    {
        DssPrototypeOverlayWindow? targetOverlay =
            overlay;

        dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
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
                    frame,
                    tracking,
                    readiness,
                    state,
                    context,
                    sequence);
            }));
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
        exitGraceTimer.Stop();

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
