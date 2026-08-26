using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssPrototypeSessionContext(
    string Commander,
    string SystemName,
    long SystemAddress,
    string BodyName,
    int BodyId,
    double BodyRadiusMeters,
    double VerticalFovDegrees,
    double DssPatchRadius,
    double DssPatchRadiusOriginal,
    string DssBlueprint,
    int DssEngineeringLevel,
    int CaptureWidth,
    int CaptureHeight);

internal sealed class DssPrototypeSessionLogger : IDisposable
{
    private readonly object sync = new();
    private readonly StreamWriter frameWriter;
    private readonly StreamWriter journalWriter;
    private readonly StreamWriter shotWriter;
    private readonly StreamWriter impactDetectorWriter;
    private readonly StreamWriter impactWriter;
    private readonly StreamWriter unresolvedLaunchWriter;
    private readonly string diagnosticFramesDirectory;
    private DateTimeOffset lastFrameFlushUtc =
        DateTimeOffset.MinValue;
    private DateTimeOffset lastDiagnosticFrameUtc =
        DateTimeOffset.MinValue;
    private const int MaxDiagnosticFramesPerSession = 96;

    private int diagnosticSaveInProgress;
    private int diagnosticFramesQueued;
    private bool disposed;

    public DssPrototypeSessionLogger(
        DssPrototypeSessionContext context)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "EDActivityOverlay",
            "Research",
            "DSS");

        DssResearchRetention.Prune(root);

        SessionId = DateTimeOffset.UtcNow
            .ToString(
                "yyyyMMdd-HHmmssfff",
                CultureInfo.InvariantCulture);
        SessionDirectory = Path.Combine(root, SessionId);
        diagnosticFramesDirectory =
            Path.Combine(SessionDirectory, "frames");

        Directory.CreateDirectory(SessionDirectory);
        Directory.CreateDirectory(
            diagnosticFramesDirectory);

        File.WriteAllText(
            Path.Combine(
                SessionDirectory,
                "session.json"),
            JsonSerializer.Serialize(
                context,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
            new UTF8Encoding(false));

        frameWriter = new StreamWriter(
            Path.Combine(
                SessionDirectory,
                "frames.csv"),
            append: false,
            new UTF8Encoding(false));

        journalWriter = new StreamWriter(
            Path.Combine(
                SessionDirectory,
                "journal-events.ndjson"),
            append: false,
            new UTF8Encoding(false));

        shotWriter = new StreamWriter(
            Path.Combine(
                SessionDirectory,
                "shots.csv"),
            append: false,
            new UTF8Encoding(false));

        shotWriter.WriteLine(
            "input_utc,launch_sequence,frame_utc,frame_age_ms,frame_sequence," +
            "fire_action,binding_slot,binding_device,binding_key,geometry_valid," +
            "readiness_state,angular_diameter_deg,center_x,center_y,horizon_radius_px," +
            "reticle_x,reticle_y,aim_norm_x,aim_norm_y,aim_norm_r,aim_angle_deg," +
            "nearest_point,nearest_point_x,nearest_point_y,nearest_error_norm," +
            "nearest_error_px,efficiency_target,pattern_source," +
            "targeting_step,target_available,target_norm_x,target_norm_y,target_norm_r,target_error_px," +
            "target_candidate_id,target_source,coverage_fraction,target_uncovered_score," +
            "hud_miss_visible,hud_miss_active_ratio");
        shotWriter.Flush();

        impactDetectorWriter = new StreamWriter(
            Path.Combine(
                SessionDirectory,
                "impact-detector.csv"),
            append: false,
            new UTF8Encoding(false));

        impactDetectorWriter.WriteLine(
            "frame_utc,frame_sequence,armed,change_ratio,active_pixels,event");
        impactDetectorWriter.Flush();

        impactWriter = new StreamWriter(
            Path.Combine(
                SessionDirectory,
                "impacts.csv"),
            append: false,
            new UTF8Encoding(false));

        impactWriter.WriteLine(
            "impact_utc,impact_sequence,frame_sequence,counter_change_ratio," +
            "matched_launch_sequence,launch_utc,flight_ms,correlation_method," +
            "candidate_count,launch_geometry_valid,aim_norm_x,aim_norm_y," +
            "aim_norm_r,aim_angle_deg,angular_diameter_deg,nearest_point," +
            "nearest_error_px");
        impactWriter.Flush();

        unresolvedLaunchWriter = new StreamWriter(
            Path.Combine(
                SessionDirectory,
                "unresolved-launches.csv"),
            append: false,
            new UTF8Encoding(false));

        unresolvedLaunchWriter.WriteLine(
            "expired_utc,launch_sequence,launch_utc,age_ms,reason," +
            "geometry_valid,aim_norm_r");
        unresolvedLaunchWriter.Flush();

        frameWriter.WriteLine(
            "utc,sequence,width,height,gui_focus,system_address,body_id,body_name,fov_deg," +
            "patch_radius,body_radius_m,capture_ms,detect_ms,search_mode,center_state,horizon_state," +
            "readiness_state,target_selected,angular_radius_deg,angular_diameter_deg,readiness_age_ms," +
            "estimated_center_distance_m,ready_near_m,ready_target_m,ready_far_m," +
            "velocity_x,velocity_y,center_found,center_x,center_y,center_confidence,horizon_found," +
            "horizon_observed,horizon_age_ms,horizon_x,horizon_y,horizon_confidence,horizon_radius_px," +
            "horizon_aim_error_px,aim_offset_deg,hud_miss_visible,hud_miss_active_ratio," +
            "coverage_available,coverage_settling,coverage_fraction,coverage_confidence," +
            "coverage_candidate_id,coverage_target_x,coverage_target_y,coverage_uncovered_score");
        frameWriter.Flush();

        Logger.Logger.Info(
            $"DSS prototype logger started: {SessionDirectory}");
    }

    public string SessionId { get; }
    public string SessionDirectory { get; }

    public void LogFrame(
        long sequence,
        GameStateSnapshot state,
        DssPrototypeSessionContext context,
        DssCapturedFrame frame,
        DssHudTrackResult tracking,
        DssAssistantReadinessSnapshot readiness,
        DssAimMissObservation missObservation,
        DssCoverageObservation coverageObservation,
        double captureMilliseconds,
        double detectionMilliseconds)
    {
        DssHudGeometry geometry = tracking.Geometry;

        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            frameWriter.WriteLine(string.Join(
                ",",
                Csv(frame.TimestampUtc.ToString("O")),
                sequence.ToString(CultureInfo.InvariantCulture),
                frame.Width.ToString(CultureInfo.InvariantCulture),
                frame.Height.ToString(CultureInfo.InvariantCulture),
                state.GuiFocus.ToString(CultureInfo.InvariantCulture),
                state.SystemAddress.ToString(CultureInfo.InvariantCulture),
                context.BodyId.ToString(CultureInfo.InvariantCulture),
                Csv(context.BodyName),
                context.VerticalFovDegrees.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                context.DssPatchRadius.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                context.BodyRadiusMeters.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                captureMilliseconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                detectionMilliseconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                Csv(tracking.SearchMode),
                Csv(tracking.CenterState.ToString()),
                Csv(tracking.HorizonState.ToString()),
                Csv(readiness.State.ToString()),
                readiness.BodyTargetSelected ? "1" : "0",
                readiness.AngularRadiusDegrees.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                readiness.AngularDiameterDegrees.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                readiness.MeasurementAgeMilliseconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                readiness.EstimatedCenterDistanceMeters.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                readiness.RecommendedNearCenterDistanceMeters.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                readiness.RecommendedTargetCenterDistanceMeters.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                readiness.RecommendedFarCenterDistanceMeters.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                tracking.CenterVelocityX.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                tracking.CenterVelocityY.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.BodyCenterFound ? "1" : "0",
                geometry.BodyCenterX.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.BodyCenterY.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.BodyCenterConfidence.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.HorizonMarkerFound ? "1" : "0",
                geometry.HorizonMarkerObserved ? "1" : "0",
                geometry.HorizonObservationAgeMilliseconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.HorizonMarkerX.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.HorizonMarkerY.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.HorizonMarkerConfidence.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.HorizonRadiusPixels.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.HorizonAimErrorPixels.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
                geometry.AimOffsetDegrees.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                missObservation.Visible
                    ? "1"
                    : "0",
                missObservation.ActiveRatio.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                coverageObservation.Available ? "1" : "0",
                coverageObservation.Settling ? "1" : "0",
                coverageObservation.CoveredFraction.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                coverageObservation.Confidence.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                coverageObservation.SuggestedCandidateId.ToString(
                    CultureInfo.InvariantCulture),
                coverageObservation.SuggestedNormalizedX.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                coverageObservation.SuggestedNormalizedY.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture),
                coverageObservation.SuggestedUncoveredScore.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture)));

            if (frame.TimestampUtc - lastFrameFlushUtc
                >= TimeSpan.FromSeconds(1))
            {
                frameWriter.Flush();
                lastFrameFlushUtc = frame.TimestampUtc;
            }

            bool periodic =
                frame.TimestampUtc - lastDiagnosticFrameUtc
                >= TimeSpan.FromSeconds(4);
            bool usefulObservation =
                geometry.HorizonMarkerObserved
                && frame.TimestampUtc - lastDiagnosticFrameUtc
                    >= TimeSpan.FromMilliseconds(1500);

            if ((sequence == 1
                 || periodic
                 || usefulObservation)
                && diagnosticFramesQueued
                   < MaxDiagnosticFramesPerSession)
            {
                diagnosticFramesQueued++;
                QueueDiagnosticFrame(sequence, frame);
                lastDiagnosticFrameUtc = frame.TimestampUtc;
            }
        }
    }

    public void LogFireBindings(
        DssFireBindingSet bindings)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            File.WriteAllText(
                Path.Combine(
                    SessionDirectory,
                    "fire-bindings.json"),
                JsonSerializer.Serialize(
                    bindings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }),
                new UTF8Encoding(false));
        }
    }

    public void LogProbeLaunch(
        DssProbeLaunchRecord launch,
        DssSequentialTargetTelemetry targeting)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            shotWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(
                        launch.InputUtc
                            .ToString("O")),
                    launch.LaunchSequence
                        .ToString(
                            CultureInfo.InvariantCulture),
                    Csv(
                        launch.FrameUtc
                            == DateTimeOffset.MinValue
                            ? string.Empty
                            : launch.FrameUtc
                                .ToString("O")),
                    launch.FrameAgeMilliseconds
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.FrameSequence
                        .ToString(
                            CultureInfo.InvariantCulture),
                    Csv(launch.FireAction),
                    Csv(launch.BindingSlot),
                    Csv(launch.BindingDevice),
                    Csv(launch.BindingKey),
                    launch.GeometryValid
                        ? "1"
                        : "0",
                    Csv(launch.ReadinessState),
                    launch.AngularDiameterDegrees
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.BodyCenterX
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.BodyCenterY
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.HorizonRadiusPixels
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.ReticleX
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.ReticleY
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.AimNormalizedX
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.AimNormalizedY
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.AimNormalizedRadius
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.AimAngleDegrees
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.NearestPatternPoint
                        .ToString(
                            CultureInfo.InvariantCulture),
                    launch.NearestPatternX
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.NearestPatternY
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.NearestErrorNormalized
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.NearestErrorPixels
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    launch.EfficiencyTarget
                        .ToString(
                            CultureInfo.InvariantCulture),
                    Csv(launch.PatternSource),
                    targeting.Step
                        .ToString(
                            CultureInfo.InvariantCulture),
                    targeting.Available
                        ? "1"
                        : "0",
                    targeting.NormalizedX
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    targeting.NormalizedY
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    targeting.NormalizedRadius
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    targeting.ErrorPixels
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    targeting.CandidateId
                        .ToString(
                            CultureInfo.InvariantCulture),
                    Csv(targeting.TargetSource),
                    targeting.CoverageFraction
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    targeting.UncoveredScore
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    launch.HudMissVisible
                        ? "1"
                        : "0",
                    launch.HudMissActiveRatio
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture)));

            shotWriter.Flush();
        }
    }

    public void LogImpactCounterObservation(
        long frameSequence,
        DateTimeOffset frameUtc,
        DssImpactCounterObservation observation)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            impactDetectorWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(
                        frameUtc
                            .ToString("O")),
                    frameSequence
                        .ToString(
                            CultureInfo.InvariantCulture),
                    observation.Armed
                        ? "1"
                        : "0",
                    observation.ChangeRatio
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    observation.ActivePixelCount
                        .ToString(
                            CultureInfo.InvariantCulture),
                    observation.Changed
                        ? "IMPACT_COUNTER_CHANGED"
                        : string.Empty));

            if (observation.Changed)
            {
                impactDetectorWriter.Flush();
            }
        }
    }

    public void LogProbeImpact(
        DssProbeImpactCorrelation impact)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            impactWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(
                        impact.ImpactUtc
                            .ToString("O")),
                    impact.ImpactSequence
                        .ToString(
                            CultureInfo.InvariantCulture),
                    impact.ImpactFrameSequence
                        .ToString(
                            CultureInfo.InvariantCulture),
                    impact.CounterChangeRatio
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    impact.MatchedLaunchSequence
                        .ToString(
                            CultureInfo.InvariantCulture),
                    Csv(
                        impact.LaunchUtc
                            == DateTimeOffset.MinValue
                            ? string.Empty
                            : impact.LaunchUtc
                                .ToString("O")),
                    impact.FlightMilliseconds
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    Csv(
                        impact.CorrelationMethod),
                    impact.CandidateCount
                        .ToString(
                            CultureInfo.InvariantCulture),
                    impact.LaunchGeometryValid
                        ? "1"
                        : "0",
                    impact.AimNormalizedX
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    impact.AimNormalizedY
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    impact.AimNormalizedRadius
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    impact.AimAngleDegrees
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    impact.AngularDiameterDegrees
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture),
                    impact.NearestPatternPoint
                        .ToString(
                            CultureInfo.InvariantCulture),
                    impact.NearestErrorPixels
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture)));

            impactWriter.Flush();
        }
    }

    public void LogUnresolvedLaunch(
        DssProbeUnresolvedLaunch unresolved)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            unresolvedLaunchWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(
                        unresolved.ExpiredUtc
                            .ToString("O")),
                    unresolved.LaunchSequence
                        .ToString(
                            CultureInfo.InvariantCulture),
                    Csv(
                        unresolved.LaunchUtc
                            .ToString("O")),
                    unresolved.AgeMilliseconds
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    Csv(
                        unresolved.Reason),
                    unresolved.GeometryValid
                        ? "1"
                        : "0",
                    unresolved.AimNormalizedRadius
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture)));

            unresolvedLaunchWriter.Flush();
        }
    }

    public void LogJournalEvent(
        JournalEventReceivedEventArgs e)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            var envelope = new
            {
                capturedUtc = DateTimeOffset.UtcNow,
                eventName = e.EventName,
                eventUtc = e.Timestamp,
                data = e.Data
            };

            journalWriter.WriteLine(
                JsonSerializer.Serialize(envelope));
            journalWriter.Flush();
        }
    }

    public void Complete(
        GameStateSnapshot finalState,
        string reason)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            frameWriter.Flush();
            journalWriter.Flush();

            string summary = string.Join(
                Environment.NewLine,
                $"SessionId: {SessionId}",
                $"EndedUtc: {DateTimeOffset.UtcNow:O}",
                $"Reason: {reason}",
                $"System: {finalState.StarSystem}",
                $"SystemAddress: {finalState.SystemAddress}",
                $"Destination: {finalState.DestinationName}",
                $"DestinationBodyId: {finalState.DestinationBodyId}");

            File.WriteAllText(
                Path.Combine(
                    SessionDirectory,
                    "summary.txt"),
                summary,
                new UTF8Encoding(false));
        }
    }

    private void QueueDiagnosticFrame(
        long sequence,
        DssCapturedFrame frame)
    {
        if (Interlocked.CompareExchange(
                ref diagnosticSaveInProgress,
                1,
                0) != 0)
        {
            return;
        }

        string path = Path.Combine(
            diagnosticFramesDirectory,
            $"{sequence:D6}-{frame.TimestampUtc:HHmmssfff}.png");

        _ = Task.Run(() =>
        {
            try
            {
                frame.SavePng(path);
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning(
                    $"DSS prototype diagnostic frame save failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(
                    ref diagnosticSaveInProgress,
                    0);
            }
        });
    }

    private static string Csv(string? value) =>
        "\"" + (value ?? string.Empty)
            .Replace("\"", "\"\"") + "\"";

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            frameWriter.Flush();
            journalWriter.Flush();
            shotWriter.Flush();
            impactDetectorWriter.Flush();
            impactWriter.Flush();
            unresolvedLaunchWriter.Flush();
            frameWriter.Dispose();
            journalWriter.Dispose();
            shotWriter.Dispose();
            impactDetectorWriter.Dispose();
            impactWriter.Dispose();
            unresolvedLaunchWriter.Dispose();
        }
    }
}
