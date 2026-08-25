using System;
using System.IO;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssReadinessRadiusFallbackTests
{
    [Fact]
    public void ResolveBodyScanFindsScanOlderThanSixJournalFiles()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "EDActivityOverlay-DssJournalTest-"
                + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            directory);

        try
        {
            const long systemAddress =
                3382253294290;

            const int bodyId = 5;

            const double radius =
                1234567.89;

            // Matching Scan is deliberately the oldest of eight logs. v12's
            // six-file limit would miss it.
            string scanPath =
                Path.Combine(
                    directory,
                    "Journal.2026-01-01T000000.01.log");

            File.WriteAllText(
                scanPath,
                "{\"timestamp\":\"2026-01-01T00:00:00Z\"," +
                "\"event\":\"Scan\"," +
                "\"SystemAddress\":3382253294290," +
                "\"BodyID\":5," +
                "\"BodyName\":\"Eledolyaks 1\"," +
                "\"Radius\":1234567.89}\\n");

            File.SetLastWriteTimeUtc(
                scanPath,
                new DateTime(
                    2026,
                    1,
                    1,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc));

            for (int i = 2; i <= 8; i++)
            {
                string path =
                    Path.Combine(
                        directory,
                        $"Journal.2026-01-0{i}T000000.01.log");

                File.WriteAllText(
                    path,
                    "{\"timestamp\":\"2026-01-0" + i +
                    "T00:00:00Z\",\"event\":\"Music\"," +
                    "\"MusicTrack\":\"Supercruise\"}\\n");

                File.SetLastWriteTimeUtc(
                    path,
                    new DateTime(
                        2026,
                        1,
                        i,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc));
            }

            DssBodyScanSnapshot result =
                DssJournalContextReader.ResolveBodyScan(
                    directory,
                    systemAddress,
                    bodyId,
                    "Eledolyaks 1");

            Assert.Equal(
                bodyId,
                result.BodyId);

            Assert.InRange(
                result.RadiusMeters,
                radius - 0.01,
                radius + 0.01);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Fact]
    public void MissingRadiusDoesNotBlockAngularReadyState()
    {
        var evaluator =
            new DssAssistantReadinessEvaluator();

        var state =
            GameStateSnapshot.Empty with
            {
                SystemAddress = 123,
                DestinationSystemAddress = 123,
                DestinationBodyId = 10,
                DestinationName = "Test 10",
                JournalDirectory = string.Empty
            };

        var context =
            new DssPrototypeSessionContext(
                "Commander",
                "Test",
                123,
                "Test 10",
                10,
                0,
                56.817001,
                26,
                20,
                "Sensor_Expanded",
                3,
                1920,
                1080);

        var frame =
            new DssCapturedFrame(
                DateTimeOffset.UtcNow,
                0,
                0,
                1920,
                1080,
                1920 * 4,
                new byte[
                    1920 * 1080 * 4]);

        double focal =
            DssHudGeometryDetector.GetFocalPixels(
                frame.Height,
                56.817001);

        double horizonY =
            frame.Height / 2d
            + focal
              * Math.Tan(
                  14d
                  * Math.PI / 180d);

        var geometry =
            new DssHudGeometry(
                960,
                540,
                true,
                960,
                540,
                0.9,
                true,
                true,
                960,
                horizonY,
                0.9,
                0,
                Math.Abs(
                    horizonY - 540),
                0,
                0);

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                frame,
                geometry);

        Assert.Equal(
            DssAssistantReadinessState.Ready,
            result.State);

        Assert.Equal(
            0,
            result.BodyRadiusMeters);

        Assert.False(
            result.HasDistanceEstimate);
    }
}
