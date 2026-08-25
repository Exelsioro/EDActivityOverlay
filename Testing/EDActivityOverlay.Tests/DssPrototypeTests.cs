using System;
using System.IO;
using System.Text;
using System.Text.Json;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssPrototypeTests
{
    [Fact]
    public void FocalLengthUsesEliteVerticalFov()
    {
        double focal =
            DssHudGeometryDetector.GetFocalPixels(
                1080,
                56.817001);

        Assert.InRange(focal, 997.5, 999.2);
    }

    [Fact]
    public void DetectorFindsSyntheticBodyCenterAndHorizonDash()
    {
        const int width = 800;
        const int height = 600;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        DrawFilledCircle(
            pixels,
            width,
            height,
            stride,
            520,
            360,
            7);

        DrawLine(
            pixels,
            width,
            height,
            stride,
            520,
            360,
            400,
            300);

        DrawHorizonTriplet(
            pixels,
            width,
            height,
            stride,
            centerX: 420,
            centerY: 310,
            radialX: -120,
            radialY: -60);

        var frame = new DssCapturedFrame(
            DateTimeOffset.UtcNow,
            0,
            0,
            width,
            height,
            stride,
            pixels);

        var detector =
            new DssHudGeometryDetector();

        DssHudGeometry geometry =
            detector.Detect(
                frame,
                56.817001);

        Assert.True(geometry.BodyCenterFound);
        Assert.InRange(
            geometry.BodyCenterX,
            515,
            525);
        Assert.InRange(
            geometry.BodyCenterY,
            355,
            365);
        Assert.True(geometry.HorizonMarkerObserved);
    }

    [Fact]
    public void TrackerPredictsCenterAcrossShortDetectionGap()
    {
        const int width = 800;
        const int height = 600;
        int stride = width * 4;

        var detector = new DssHudGeometryDetector();
        var tracker = new DssHudGeometryTracker();

        byte[] firstPixels = new byte[stride * height];
        DrawFilledCircle(
            firstPixels,
            width,
            height,
            stride,
            520,
            360,
            7);
        DrawLine(
            firstPixels,
            width,
            height,
            stride,
            520,
            360,
            400,
            300);

        var first = new DssCapturedFrame(
            DateTimeOffset.UtcNow,
            0,
            0,
            width,
            height,
            stride,
            firstPixels);

        DssHudTrackResult acquiring =
            tracker.Process(first, detector, 56.817001);

        var confirmation = new DssCapturedFrame(
            first.TimestampUtc.AddMilliseconds(66),
            0,
            0,
            width,
            height,
            stride,
            firstPixels);

        DssHudTrackResult observed =
            tracker.Process(
                confirmation,
                detector,
                56.817001);

        byte[] gapPixels = new byte[stride * height];
        var gap = new DssCapturedFrame(
            first.TimestampUtc.AddMilliseconds(450),
            0,
            0,
            width,
            height,
            stride,
            gapPixels);

        DssHudTrackResult predicted =
            tracker.Process(
                gap,
                detector,
                56.817001);

        Assert.Equal(
            DssCenterTrackState.Acquiring,
            acquiring.CenterState);
        Assert.Equal(
            DssCenterTrackState.Tracking,
            observed.CenterState);
        Assert.Equal(
            DssCenterTrackState.Predicting,
            predicted.CenterState);
        Assert.True(
            predicted.Geometry.BodyCenterFound);
    }

    [Fact]
    public void GlobalCenterSearchRejectsIsolatedBrightStar()
    {
        const int width = 800;
        const int height = 600;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        DrawFilledCircle(
            pixels,
            width,
            height,
            stride,
            650,
            120,
            8);

        var frame = new DssCapturedFrame(
            DateTimeOffset.UtcNow,
            0,
            0,
            width,
            height,
            stride,
            pixels);

        var detector = new DssHudGeometryDetector();

        DssHudGeometry geometry =
            detector.DetectGlobal(
                frame,
                56.817001);

        Assert.False(geometry.BodyCenterFound);
    }

    [Fact]
    public void LoadoutParserReadsFrontierModifiersSpelling()
    {
        using JsonDocument document =
            JsonDocument.Parse(
                """
                {
                  "event":"Loadout",
                  "Modules":[
                    {
                      "Slot":"Slot01_Size1",
                      "Item":"int_detailedsurfacescanner_tiny",
                      "Engineering":{
                        "BlueprintName":"Sensor_Expanded",
                        "Level":3,
                        "Modifiers":[
                          {
                            "Label":"DSS_PatchRadius",
                            "Value":26.0,
                            "OriginalValue":20.0
                          }
                        ]
                      }
                    }
                  ]
                }
                """);

        DssModuleSnapshot module =
            DssJournalContextReader.ParseDssModule(
                document.RootElement);

        Assert.Equal(26, module.PatchRadius);
        Assert.Equal(
            20,
            module.OriginalPatchRadius);
        Assert.Equal(
            "Sensor_Expanded",
            module.Blueprint);
        Assert.Equal(3, module.EngineeringLevel);
    }

    [Fact]
    public void BodyScanResolverUsesSystemAddressAndBodyId()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dss-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string journal = Path.Combine(
                directory,
                "Journal.01.log");

            File.WriteAllText(
                journal,
                """
                {"event":"Scan","SystemAddress":111,"BodyID":4,"BodyName":"Old 4","Radius":1000}
                {"event":"Scan","SystemAddress":222,"BodyID":4,"BodyName":"Wanted 4","Radius":2500}
                """,
                new UTF8Encoding(false));

            DssBodyScanSnapshot body =
                DssJournalContextReader.ResolveBodyScan(
                    directory,
                    222,
                    4,
                    "Wanted 4");

            Assert.Equal(222, body.SystemAddress);
            Assert.Equal(4, body.BodyId);
            Assert.Equal("Wanted 4", body.BodyName);
            Assert.Equal(2500, body.RadiusMeters);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void DrawFilledCircle(
        byte[] pixels,
        int width,
        int height,
        int stride,
        int centerX,
        int centerY,
        int radius)
    {
        for (int y = centerY - radius;
             y <= centerY + radius;
             y++)
        {
            for (int x = centerX - radius;
                 x <= centerX + radius;
                 x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy
                    <= radius * radius)
                {
                    SetWhite(
                        pixels,
                        width,
                        height,
                        stride,
                        x,
                        y);
                }
            }
        }
    }

    private static void DrawLine(
        byte[] pixels,
        int width,
        int height,
        int stride,
        int x0,
        int y0,
        int x1,
        int y1)
    {
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;

        while (true)
        {
            SetWhite(
                pixels,
                width,
                height,
                stride,
                x0,
                y0);

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            int twice = 2 * error;
            if (twice >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (twice <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void DrawHorizonTriplet(
        byte[] pixels,
        int width,
        int height,
        int stride,
        int centerX,
        int centerY,
        int radialX,
        int radialY)
    {
        double length =
            Math.Sqrt(
                radialX * radialX
                + radialY * radialY);

        double nx =
            -radialY / length;

        double ny =
            radialX / length;

        void Segment(
            double from,
            double to)
        {
            int x0 =
                (int)Math.Round(
                    centerX + nx * from);

            int y0 =
                (int)Math.Round(
                    centerY + ny * from);

            int x1 =
                (int)Math.Round(
                    centerX + nx * to);

            int y1 =
                (int)Math.Round(
                    centerY + ny * to);

            DrawLine(
                pixels,
                width,
                height,
                stride,
                x0,
                y0,
                x1,
                y1);
        }

        Segment(-15, -10);
        Segment(-4, 4);
        Segment(10, 15);
    }

    private static void SetWhite(
        byte[] pixels,
        int width,
        int height,
        int stride,
        int x,
        int y)
    {
        if ((uint)x >= (uint)width
            || (uint)y >= (uint)height)
        {
            return;
        }

        int index = y * stride + x * 4;
        pixels[index] = 255;
        pixels[index + 1] = 255;
        pixels[index + 2] = 255;
        pixels[index + 3] = 255;
    }
}
