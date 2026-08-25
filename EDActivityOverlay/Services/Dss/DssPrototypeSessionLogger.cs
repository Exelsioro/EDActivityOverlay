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
    private readonly string diagnosticFramesDirectory;
    private DateTimeOffset lastFrameFlushUtc =
        DateTimeOffset.MinValue;
    private DateTimeOffset lastDiagnosticFrameUtc =
        DateTimeOffset.MinValue;
    private int diagnosticSaveInProgress;
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

        frameWriter.WriteLine(
            "utc,sequence,width,height,gui_focus,system_address,body_id,body_name,fov_deg," +
            "patch_radius,body_radius_m,capture_ms,detect_ms,search_mode,center_state,horizon_state," +
            "readiness_state,target_selected,angular_radius_deg,angular_diameter_deg,readiness_age_ms," +
            "estimated_center_distance_m,ready_near_m,ready_target_m,ready_far_m," +
            "velocity_x,velocity_y,center_found,center_x,center_y,center_confidence,horizon_found," +
            "horizon_observed,horizon_age_ms,horizon_x,horizon_y,horizon_confidence,horizon_radius_px," +
            "horizon_aim_error_px,aim_offset_deg");
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
                    CultureInfo.InvariantCulture)));

            if (frame.TimestampUtc - lastFrameFlushUtc
                >= TimeSpan.FromSeconds(1))
            {
                frameWriter.Flush();
                lastFrameFlushUtc = frame.TimestampUtc;
            }

            bool periodic =
                frame.TimestampUtc - lastDiagnosticFrameUtc
                >= TimeSpan.FromSeconds(2);
            bool usefulObservation =
                geometry.HorizonMarkerObserved
                && frame.TimestampUtc - lastDiagnosticFrameUtc
                    >= TimeSpan.FromMilliseconds(650);

            if (sequence == 1
                || periodic
                || usefulObservation)
            {
                QueueDiagnosticFrame(sequence, frame);
                lastDiagnosticFrameUtc = frame.TimestampUtc;
            }
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
            frameWriter.Dispose();
            journalWriter.Dispose();
        }
    }
}
