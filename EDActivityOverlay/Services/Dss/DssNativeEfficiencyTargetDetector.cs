using System;
using System.Collections.Generic;

namespace EDActivityOverlay.Services.Dss;

internal readonly record struct DssNativeEfficiencyTargetSnapshot(
    int Target,
    double Confidence,
    DateTimeOffset LastObservedUtc);

internal readonly record struct DssNativeEfficiencyTargetObservation(
    bool Available,
    int Target,
    double Confidence)
{
    public static DssNativeEfficiencyTargetObservation Empty { get; } =
        new(false, 0, 0d);
}

internal readonly record struct DssNativeDigitClassification(
    bool Available,
    int Digit,
    double Confidence,
    double BestError,
    double SecondError,
    double ClassMargin)
{
    public static DssNativeDigitClassification Empty { get; } =
        new(
            false,
            -1,
            0d,
            double.PositiveInfinity,
            double.PositiveInfinity,
            0d);
}

/// <summary>
/// Stable runtime latch for Elite's native "Optimal probes / Зондов: N" value.
/// The CV detector is intentionally narrow and advisory: it cannot throw out
/// of the capture path, and a target change is hidden while the new number is
/// being reconfirmed.
/// </summary>
internal static class DssNativeEfficiencyTargetRuntime
{
    private static readonly object Gate =
        new();

    // Keep a confirmed native N across short/medium CV dropouts. The old
    // 1.2-second expiry produced TARGET_UNAVAILABLE at step 7 in the supplied
    // v51 run even though the body had not changed. A genuinely different N
    // still replaces the latch after four stable observations.
    private static readonly TimeSpan Freshness =
        TimeSpan.FromSeconds(90);

    private static readonly TimeSpan MinimumAttemptInterval =
        TimeSpan.FromMilliseconds(45);

    private const int RequiredStableFrames = 4;
    private const double MinimumObservationConfidence = 0.42d;

    private static int latchedTarget;
    private static double latchedConfidence;
    private static DateTimeOffset lastObservedUtc =
        DateTimeOffset.MinValue;
    private static DateTimeOffset lastAttemptUtc =
        DateTimeOffset.MinValue;

    private static int pendingTarget;
    private static int pendingCount;

    internal static void Observe(
        DssCapturedFrame frame)
    {
        // Native scan progress uses the same already-normalized capture frame.
        // It owns its own throttling and must continue to run even when the
        // efficiency-target detector skips this frame.
        DssNativeScanProgressRuntime.Observe(
            frame);

        if (frame.Bgra32.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            if (frame.TimestampUtc
                - lastAttemptUtc
                < MinimumAttemptInterval)
            {
                return;
            }

            lastAttemptUtc =
                frame.TimestampUtc;
        }

        DssNativeEfficiencyTargetObservation observation;

        try
        {
            observation =
                DssNativeEfficiencyTargetDetector.Detect(
                    frame);
        }
        catch
        {
            // Native-target CV must never break WGC/GDI frame publication.
            return;
        }

        if (!observation.Available
            || observation.Confidence
               < MinimumObservationConfidence)
        {
            return;
        }

        bool changed = false;

        lock (Gate)
        {
            if (observation.Target == latchedTarget
                && latchedTarget > 0)
            {
                latchedConfidence =
                    Math.Max(
                        observation.Confidence,
                        latchedConfidence * 0.85d);

                lastObservedUtc =
                    frame.TimestampUtc;

                pendingTarget = 0;
                pendingCount = 0;
                return;
            }

            if (observation.Target == pendingTarget)
            {
                pendingCount++;
            }
            else
            {
                pendingTarget =
                    observation.Target;
                pendingCount = 1;
            }

            if (pendingCount < RequiredStableFrames)
            {
                return;
            }

            changed =
                latchedTarget
                != observation.Target;

            latchedTarget =
                observation.Target;
            latchedConfidence =
                observation.Confidence;
            lastObservedUtc =
                frame.TimestampUtc;

            pendingTarget = 0;
            pendingCount = 0;
        }

        if (changed)
        {
            Logger.Logger.Info(
                $"DSS NATIVE TARGET CV locked: N={observation.Target}; " +
                $"confidence={observation.Confidence:0.00}.");
        }
    }

    internal static bool TryGetFresh(
        out DssNativeEfficiencyTargetSnapshot snapshot)
    {
        lock (Gate)
        {
            bool changingTarget =
                pendingTarget > 0
                && latchedTarget > 0
                && pendingTarget != latchedTarget;

            if (changingTarget
                || latchedTarget < 2
                || DateTimeOffset.UtcNow
                   - lastObservedUtc
                   > Freshness)
            {
                snapshot = default;
                return false;
            }

            snapshot =
                new DssNativeEfficiencyTargetSnapshot(
                    latchedTarget,
                    latchedConfidence,
                    lastObservedUtc);

            return true;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            latchedTarget = 0;
            latchedConfidence = 0d;
            lastObservedUtc =
                DateTimeOffset.MinValue;
            lastAttemptUtc =
                DateTimeOffset.MinValue;
            pendingTarget = 0;
            pendingCount = 0;
        }
    }

    internal static void SetForTests(
        int target,
        double confidence = 1d)
    {
        lock (Gate)
        {
            latchedTarget = target;
            latchedConfidence = confidence;
            lastObservedUtc =
                DateTimeOffset.UtcNow;
            lastAttemptUtc =
                DateTimeOffset.MinValue;
            pendingTarget = 0;
            pendingCount = 0;
        }
    }
}

/// <summary>
/// Fixed-layout CV classifier for Elite's native DSS efficiency target.
///
/// It does not OCR localized words. It verifies the cyan "Optimal probes"
/// label in the lower-right DSS HUD, extracts the right-aligned neutral-gray
/// number, and classifies its one/two glyphs against Elite HUD digit
/// prototypes. The prototypes were built from this project's own DSS research
/// frames: the native "Ударов" counter supplied 0..9 in the same HUD font, and
/// archived "Зондов: 6" / "Зондов: 18" frames were used as held-out checks.
///
/// v49 normalizes >1080p captures into this same 1080p reference space.
/// </summary>
internal static class DssNativeEfficiencyTargetDetector
{
    private const int ReferenceHeight = 1080;
    private const int MinimumSupportedTarget = 2;
    // Real v53 research captured a Frontier native efficiency target N=21.
    // Keep headroom above the first observed >20 body so another large gas
    // giant does not hit a new artificial ceiling immediately.
    private const int MaximumSupportedTarget = 32;

    private const int GlyphCanvasWidth = 12;
    private const int GlyphCanvasHeight = 14;

    private static readonly byte[][] DigitTemplates =
    {
        new byte[]
        {
            0,0,0,0,139,185,185,156,0,0,0,0,0,0,0,172,205,172,172,212,202,0,0,0,
            0,0,139,229,113,0,0,0,232,146,0,0,0,0,189,185,0,0,0,0,179,232,0,0,
            0,0,205,139,0,0,0,0,126,252,0,0,0,0,219,119,0,0,0,0,96,252,83,0,
            0,93,225,113,0,0,0,0,0,248,99,0,0,93,225,113,0,0,0,0,0,248,99,0,
            0,0,219,119,0,0,0,0,89,252,86,0,0,0,209,132,0,0,0,0,116,255,0,0,
            0,0,195,169,0,0,0,0,166,245,0,0,0,0,162,232,86,0,0,0,229,169,0,0,
            0,0,0,199,202,142,142,209,229,0,0,0,0,0,0,0,179,229,229,199,0,0,0,0
        },
        new byte[]
        {
            0,0,0,0,0,0,110,145,0,0,0,0,0,0,0,0,94,201,255,224,0,0,0,0,
            0,0,0,0,116,148,192,222,0,0,0,0,0,0,0,0,0,0,152,219,0,0,0,0,
            0,0,0,0,0,0,149,219,0,0,0,0,0,0,0,0,0,0,149,219,0,0,0,0,
            0,0,0,0,0,0,149,219,0,0,0,0,0,0,0,0,0,0,149,219,0,0,0,0,
            0,0,0,0,0,0,149,219,0,0,0,0,0,0,0,0,0,0,149,219,0,0,0,0,
            0,0,0,0,0,0,149,219,0,0,0,0,0,0,0,0,0,0,149,219,0,0,0,0,
            0,0,0,0,0,0,150,219,0,0,0,0,0,0,0,0,0,0,135,199,0,0,0,0
        },
        new byte[]
        {
            0,0,0,128,183,196,196,155,0,0,0,0,0,0,159,221,190,200,200,252,210,0,0,0,
            0,0,103,90,0,0,0,131,255,100,0,0,0,0,0,0,0,0,0,0,231,179,0,0,
            0,0,0,0,0,0,0,0,224,183,0,0,0,0,0,0,0,0,0,0,234,114,0,0,
            0,0,0,0,0,0,0,155,231,0,0,0,0,0,0,0,0,0,0,155,231,0,0,0,
            0,0,0,0,0,0,0,238,124,0,0,0,0,0,0,0,0,203,203,183,0,0,0,0,
            0,0,0,0,176,210,210,0,0,0,0,0,0,0,0,138,227,90,90,0,0,0,0,0,
            0,0,117,248,196,145,145,138,145,155,0,0,0,0,176,241,241,241,241,241,245,241,0,0
        },
        new byte[]
        {
            0,0,0,129,182,176,176,119,0,0,0,0,0,0,132,199,179,199,199,235,139,0,0,0,
            0,0,0,0,0,0,0,189,225,0,0,0,0,0,0,0,0,0,0,99,238,0,0,0,
            0,0,0,0,0,0,0,119,235,0,0,0,0,0,0,0,0,0,0,215,166,0,0,0,
            0,0,0,0,176,255,255,182,0,0,0,0,0,0,0,0,176,255,255,182,0,0,0,0,
            0,0,0,0,0,123,123,235,172,0,0,0,0,0,0,0,0,0,0,0,238,83,0,0,
            0,0,0,0,0,0,0,0,225,129,0,0,0,0,0,0,0,0,0,83,238,103,0,0,
            0,0,146,162,109,129,129,232,212,0,0,0,0,0,89,182,229,219,219,169,0,0,0,0
        },
        new byte[]
        {
            0,0,0,0,0,0,0,0,111,0,0,0,0,0,0,0,0,0,0,163,181,0,0,0,
            0,0,0,0,0,117,117,233,190,0,0,0,0,0,0,0,0,175,175,230,187,0,0,0,
            0,0,0,0,172,123,123,200,184,0,0,0,0,0,0,132,147,0,0,184,184,0,0,0,
            0,0,95,178,77,0,0,184,184,0,0,0,0,0,95,178,77,0,0,184,184,0,0,0,
            0,0,181,117,0,0,0,184,184,0,0,0,0,144,246,246,240,243,243,255,255,249,197,0,
            0,0,0,0,0,0,0,194,197,0,0,0,0,0,0,0,0,0,0,184,184,0,0,0,
            0,0,0,0,0,0,0,184,187,0,0,0,0,0,0,0,0,0,0,163,172,0,0,0
        },
        new byte[]
        {
            0,0,0,0,163,187,187,187,187,0,0,0,0,0,0,126,218,184,184,167,167,0,0,0,
            0,0,0,163,160,0,0,0,0,0,0,0,0,0,0,184,119,0,0,0,0,0,0,0,
            0,0,0,194,99,0,0,0,0,0,0,0,0,0,85,228,255,224,224,173,0,0,0,0,
            0,0,99,133,109,153,153,252,238,0,0,0,0,0,99,133,109,153,153,252,238,0,0,0,
            0,0,0,0,0,0,0,99,252,150,0,0,0,0,0,0,0,0,0,0,224,228,0,0,
            0,0,0,0,0,0,0,0,228,214,0,0,0,0,0,0,0,0,0,109,252,119,0,0,
            0,0,170,180,136,177,177,255,214,0,0,0,0,0,109,197,238,231,231,156,0,0,0,0
        },
        new byte[]
        {
            0,0,0,0,0,0,0,121,163,0,0,0,0,0,0,0,0,80,80,214,134,0,0,0,
            0,0,0,0,0,182,182,156,0,0,0,0,0,0,0,0,159,188,188,0,0,0,0,0,
            0,0,0,118,210,96,96,0,0,0,0,0,0,0,0,204,233,226,226,210,131,0,0,0,
            0,0,163,255,191,131,131,172,242,150,0,0,0,0,163,255,191,131,131,172,242,150,0,0,
            0,0,207,163,0,0,0,0,159,242,0,0,0,92,217,108,0,0,0,0,0,239,99,0,
            0,86,214,108,0,0,0,0,0,239,96,0,0,0,194,172,0,0,0,0,153,236,0,0,
            0,0,115,223,188,131,131,169,236,128,0,0,0,0,0,86,188,226,226,201,105,0,0,0
        },
        new byte[]
        {
            0,139,189,189,189,189,189,189,185,99,0,0,0,129,172,169,169,172,172,222,255,0,0,0,
            0,0,0,0,0,0,0,209,172,0,0,0,0,0,0,0,0,0,0,222,0,0,0,0,
            0,0,0,0,0,182,182,179,0,0,0,0,0,0,0,0,0,225,225,96,0,0,0,0,
            0,0,0,0,159,192,192,0,0,0,0,0,0,0,0,0,159,192,192,0,0,0,0,0,
            0,0,0,0,212,99,99,0,0,0,0,0,0,0,0,166,205,0,0,0,0,0,0,0,
            0,0,0,215,126,0,0,0,0,0,0,0,0,0,142,215,0,0,0,0,0,0,0,0,
            0,0,202,129,0,0,0,0,0,0,0,0,0,132,202,0,0,0,0,0,0,0,0,0
        },
        new byte[]
        {
            0,0,0,0,136,171,171,142,0,0,0,0,0,0,0,178,210,190,190,223,194,0,0,0,
            0,0,136,210,107,0,0,81,223,107,0,0,0,0,174,178,0,0,0,0,194,178,0,0,
            0,0,171,184,0,0,0,0,203,161,0,0,0,0,103,213,123,0,0,123,229,0,0,0,
            0,0,0,139,236,255,255,255,129,0,0,0,0,0,0,139,236,255,255,255,129,0,0,0,
            0,0,90,213,178,119,119,174,229,0,0,0,0,0,187,165,0,0,0,0,174,219,0,0,
            0,0,207,123,0,0,0,0,123,239,0,0,0,0,184,168,0,0,0,0,174,219,0,0,
            0,0,119,226,181,123,123,174,236,113,0,0,0,0,0,103,200,232,232,207,103,0,0,0
        },
        new byte[]
        {
            0,0,0,0,140,177,177,159,81,0,0,0,0,0,87,193,202,171,171,196,211,93,0,0,
            0,0,174,190,84,0,0,0,183,208,0,0,0,78,208,124,0,0,0,0,96,236,81,0,
            0,93,211,106,0,0,0,0,0,233,100,0,0,0,205,134,0,0,0,0,109,239,0,0,
            0,0,165,227,121,0,0,81,215,227,0,0,0,0,165,227,121,0,0,81,215,227,0,0,
            0,0,0,162,243,255,255,249,255,106,0,0,0,0,0,0,0,0,0,202,174,0,0,0,
            0,0,0,0,0,140,140,211,0,0,0,0,0,0,0,0,103,205,205,84,0,0,0,0,
            0,0,0,0,202,128,128,0,0,0,0,0,0,0,0,159,162,0,0,0,0,0,0,0
        }
    };

    // Recorded from the supplied Arietis Sector KJ-F a12-2 9 DSS frame:
    // native "Зондов: 21" at 1920x1080. The original generic digit-2
    // prototype was consistently closer to digit 3 on this real glyph.
    // Keep this as an alternate ONLY for digit 2; existing prototypes remain.
    private static readonly byte[] RecordedNativeDigit2Template =
    {
        0,60,170,238,255,211,211,99,0,0,0,0,0,132,213,148,146,223,223,234,103,0,0,0,
        0,48,0,0,0,90,90,221,152,0,0,0,0,0,0,0,0,38,38,173,173,36,0,0,
        0,0,0,0,0,39,39,172,165,0,0,0,0,0,0,0,0,86,86,201,129,0,0,0,
        0,0,0,0,0,152,152,188,63,0,0,0,0,0,0,0,0,152,152,188,63,0,0,0,
        0,0,0,0,107,197,197,111,0,0,0,0,0,0,0,75,181,145,145,0,0,0,0,0,
        0,0,54,162,165,48,48,0,0,0,0,0,0,43,158,208,110,56,56,58,56,0,0,0,
        0,126,244,232,188,187,187,191,177,78,0,0,0,91,138,138,138,138,138,140,128,56,0,0
    };

    // Recorded from the supplied Algol A 3 DSS frame at 1920x1080:
    // native "Зондов: 7". v54-r1's recorded digit-2 variant was closer to
    // this real 7 than the generic 7 prototype, causing a stable false N=2.
    // Keep a real class-specific variant for 7 so alternate templates expand
    // their own digit class without stealing another class's glyph.
    private static readonly byte[] RecordedNativeDigit7Template =
    {
        0,36,183,251,250,248,248,248,252,255,131,0,0,0,90,122,121,121,121,122,201,238,49,0,
        0,0,0,0,0,0,0,0,206,133,0,0,0,0,0,0,0,0,0,106,212,53,0,0,
        0,0,0,0,0,0,0,202,151,0,0,0,0,0,0,0,0,97,97,215,73,0,0,0,
        0,0,0,0,0,178,178,159,0,0,0,0,0,0,0,0,0,178,178,159,0,0,0,0,
        0,0,0,0,86,206,206,78,0,0,0,0,0,0,0,0,179,171,171,40,0,0,0,0,
        0,0,0,84,211,104,104,0,0,0,0,0,0,0,0,161,179,50,50,0,0,0,0,0,
        0,0,89,207,108,0,0,0,0,0,0,0,0,0,78,120,42,0,0,0,0,0,0,0
    };

    internal static DssNativeEfficiencyTargetObservation Detect(
        DssCapturedFrame frame)
    {
        if (frame.Bgra32.Length == 0
            || frame.Width < 640
            || frame.Height < 480)
        {
            return
                DssNativeEfficiencyTargetObservation.Empty;
        }

        double scale =
            frame.Height
            / (double)ReferenceHeight;

        if (!HasNativeTargetLabel(
                frame,
                scale))
        {
            return
                DssNativeEfficiencyTargetObservation.Empty;
        }

        int left =
            Math.Max(
                0,
                frame.Width
                - Scale(
                    182d,
                    scale));

        int right =
            Math.Min(
                frame.Width,
                frame.Width
                - Scale(
                    132d,
                    scale));

        int top =
            Math.Max(
                0,
                Scale(
                    816d,
                    scale));

        int bottom =
            Math.Min(
                frame.Height,
                Scale(
                    838d,
                    scale));

        if (right <= left
            || bottom <= top)
        {
            return
                DssNativeEfficiencyTargetObservation.Empty;
        }

        List<GlyphComponent> components =
            FindGlyphComponents(
                frame,
                left,
                top,
                right,
                bottom,
                scale);

        if (components.Count == 0)
        {
            return
                DssNativeEfficiencyTargetObservation.Empty;
        }

        int expectedRightEdge =
            frame.Width
            - Scale(
                140d,
                scale);

        int rightTolerance =
            Math.Max(
                3,
                Scale(
                    6d,
                    scale));

        GlyphComponent? ones =
            null;

        int bestRightError =
            int.MaxValue;

        foreach (GlyphComponent component
                 in components)
        {
            int rightEdge =
                component.X
                + component.Width;

            int error =
                Math.Abs(
                    rightEdge
                    - expectedRightEdge);

            if (error > rightTolerance)
            {
                continue;
            }

            if (ones is null
                || error < bestRightError
                || (error == bestRightError
                    && component.Height
                       > ones.Value.Height))
            {
                ones =
                    component;
                bestRightError =
                    error;
            }
        }

        if (ones is null)
        {
            return
                DssNativeEfficiencyTargetObservation.Empty;
        }

        int mergedDigitWidthThreshold =
            Math.Max(
                8,
                Scale(
                    12d,
                    scale));

        if (ones.Value.Width
                >= mergedDigitWidthThreshold
            && TryClassifyMergedTwoDigitComponent(
                frame,
                ones.Value,
                out int mergedTarget,
                out double mergedConfidence)
            && mergedTarget
               >= MinimumSupportedTarget
            && mergedTarget
               <= MaximumSupportedTarget)
        {
            return
                new DssNativeEfficiencyTargetObservation(
                    true,
                    mergedTarget,
                    mergedConfidence);
        }

        DigitMatch onesMatch =
            ClassifyDigit(
                frame,
                ones.Value);

        if (!onesMatch.Available)
        {
            return
                DssNativeEfficiencyTargetObservation.Empty;
        }

        GlyphComponent? tens =
            null;

        int maximumTensGap =
            Math.Max(
                3,
                Scale(
                    7d,
                    scale));

        foreach (GlyphComponent component
                 in components)
        {
            if (component.X
                >= ones.Value.X)
            {
                continue;
            }

            int gap =
                ones.Value.X
                - (component.X
                   + component.Width);

            if (gap < 0
                || gap > maximumTensGap)
            {
                continue;
            }

            if (component.Height
                < ones.Value.Height * 0.82d)
            {
                continue;
            }

            if (tens is null
                || component.X
                   > tens.Value.X)
            {
                tens =
                    component;
            }
        }

        int target =
            onesMatch.Digit;

        double confidence =
            onesMatch.Confidence;

        if (tens is not null)
        {
            DigitMatch tensMatch =
                ClassifyDigit(
                    frame,
                    tens.Value);

            if (!tensMatch.Available)
            {
                return
                    DssNativeEfficiencyTargetObservation.Empty;
            }

            target =
                tensMatch.Digit * 10
                + onesMatch.Digit;

            confidence =
                Math.Min(
                    confidence,
                    tensMatch.Confidence);
        }

        if (target
                < MinimumSupportedTarget
            || target
               > MaximumSupportedTarget)
        {
            return
                DssNativeEfficiencyTargetObservation.Empty;
        }

        return
            new DssNativeEfficiencyTargetObservation(
                true,
                target,
                confidence);
    }

    private static bool HasNativeTargetLabel(
        DssCapturedFrame frame,
        double scale)
    {
        int left =
            Math.Max(
                0,
                frame.Width
                - Scale(
                    390d,
                    scale));

        int right =
            Math.Min(
                frame.Width,
                frame.Width
                - Scale(
                    125d,
                    scale));

        int top =
            Math.Max(
                0,
                Scale(
                    785d,
                    scale));

        int bottom =
            Math.Min(
                frame.Height,
                Scale(
                    820d,
                    scale));

        int cyanPixels = 0;

        for (int y = top;
             y < bottom;
             y++)
        {
            int row =
                y * frame.Stride;

            for (int x = left;
                 x < right;
                 x++)
            {
                int index =
                    row
                    + x * 4;

                int blue =
                    frame.Bgra32[index];

                int green =
                    frame.Bgra32[index + 1];

                int red =
                    frame.Bgra32[index + 2];

                if (blue >= 90
                    && green >= 55
                    && blue >= red + 55
                    && green >= red + 25)
                {
                    cyanPixels++;
                }
            }
        }

        int minimum =
            Math.Max(
                10,
                (int)Math.Round(
                    22d
                    * scale
                    * scale));

        return
            cyanPixels >= minimum;
    }

    private static List<GlyphComponent> FindGlyphComponents(
        DssCapturedFrame frame,
        int left,
        int top,
        int right,
        int bottom,
        double scale)
    {
        int width =
            right - left;

        int height =
            bottom - top;

        var mask =
            new bool[
                width
                * height];

        for (int y = 0;
             y < height;
             y++)
        {
            int frameY =
                top + y;

            int row =
                frameY
                * frame.Stride;

            for (int x = 0;
                 x < width;
                 x++)
            {
                mask[y * width + x] =
                    GetNeutralLuma(
                        frame,
                        row
                        + (left + x) * 4)
                    > 0;
            }
        }

        var visited =
            new bool[
                mask.Length];

        var result =
            new List<GlyphComponent>();

        int minimumHeight =
            Math.Max(
                6,
                Scale(
                    8d,
                    scale));

        int minimumArea =
            Math.Max(
                5,
                (int)Math.Round(
                    5d
                    * scale
                    * scale));

        int[] queue =
            new int[
                mask.Length];

        for (int seed = 0;
             seed < mask.Length;
             seed++)
        {
            if (!mask[seed]
                || visited[seed])
            {
                continue;
            }

            int head = 0;
            int tail = 0;

            queue[tail++] =
                seed;

            visited[seed] =
                true;

            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;
            int area = 0;

            while (head < tail)
            {
                int current =
                    queue[head++];

                int cy =
                    current / width;

                int cx =
                    current
                    - cy * width;

                minX =
                    Math.Min(
                        minX,
                        cx);

                minY =
                    Math.Min(
                        minY,
                        cy);

                maxX =
                    Math.Max(
                        maxX,
                        cx);

                maxY =
                    Math.Max(
                        maxY,
                        cy);

                area++;

                for (int dy = -1;
                     dy <= 1;
                     dy++)
                {
                    for (int dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        if (dx == 0
                            && dy == 0)
                        {
                            continue;
                        }

                        int nx =
                            cx + dx;

                        int ny =
                            cy + dy;

                        if ((uint)nx
                                >= (uint)width
                            || (uint)ny
                               >= (uint)height)
                        {
                            continue;
                        }

                        int next =
                            ny * width
                            + nx;

                        if (!mask[next]
                            || visited[next])
                        {
                            continue;
                        }

                        visited[next] =
                            true;

                        queue[tail++] =
                            next;
                    }
                }
            }

            int componentWidth =
                maxX - minX + 1;

            int componentHeight =
                maxY - minY + 1;

            if (componentHeight
                    < minimumHeight
                || componentWidth < 2
                || area < minimumArea)
            {
                continue;
            }

            result.Add(
                new GlyphComponent(
                    left + minX,
                    top + minY,
                    componentWidth,
                    componentHeight,
                    area));
        }

        return result;
    }

    private static bool TryClassifyMergedTwoDigitComponent(
        DssCapturedFrame frame,
        GlyphComponent component,
        out int value,
        out double confidence)
    {
        value = 0;
        confidence = 0d;

        int minimumPartWidth =
            Math.Max(
                3,
                (int)Math.Floor(
                    component.Height * 0.30d));

        int lastSplit =
            component.Width
            - minimumPartWidth;

        if (lastSplit <= minimumPartWidth)
        {
            return false;
        }

        double bestScore =
            double.NegativeInfinity;

        for (int split =
                 minimumPartWidth;
             split <= lastSplit;
             split++)
        {
            var left =
                new GlyphComponent(
                    component.X,
                    component.Y,
                    split,
                    component.Height,
                    0);

            var right =
                new GlyphComponent(
                    component.X + split,
                    component.Y,
                    component.Width - split,
                    component.Height,
                    0);

            DigitMatch tens =
                ClassifyDigit(
                    frame,
                    left);

            DigitMatch ones =
                ClassifyDigit(
                    frame,
                    right);

            if (!tens.Available
                || !ones.Available)
            {
                continue;
            }

            int candidate =
                tens.Digit * 10
                + ones.Digit;

            if (candidate < 10
                || candidate > 99)
            {
                continue;
            }

            double pairConfidence =
                Math.Min(
                    tens.Confidence,
                    ones.Confidence);

            double widthImbalance =
                Math.Abs(
                    left.Width
                    - right.Width)
                / (double)Math.Max(
                    1,
                    component.Width);

            double score =
                pairConfidence
                - widthImbalance * 0.08d;

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            value = candidate;
            confidence = pairConfidence;
        }

        return value > 0;
    }

    private static DigitMatch ClassifyDigit(
        DssCapturedFrame frame,
        GlyphComponent component)
    {
        Span<byte> normalized =
            stackalloc byte[
                GlyphCanvasWidth
                * GlyphCanvasHeight];

        int maximumLuma = 0;

        for (int y = component.Y;
             y < component.Y
                 + component.Height;
             y++)
        {
            int row =
                y * frame.Stride;

            for (int x = component.X;
                 x < component.X
                     + component.Width;
                 x++)
            {
                maximumLuma =
                    Math.Max(
                        maximumLuma,
                        GetNeutralLuma(
                            frame,
                            row + x * 4));
            }
        }

        if (maximumLuma < 35)
        {
            return
                DigitMatch.Empty;
        }

        int normalizedWidth =
            Math.Clamp(
                (int)Math.Round(
                    component.Width
                    * GlyphCanvasHeight
                    / (double)component.Height),
                1,
                GlyphCanvasWidth);

        int xOffset =
            (GlyphCanvasWidth
             - normalizedWidth)
            / 2;

        for (int y = 0;
             y < GlyphCanvasHeight;
             y++)
        {
            int sourceY =
                component.Y
                + Math.Min(
                    component.Height - 1,
                    (int)(
                        (2L * y + 1L)
                        * component.Height
                        / (2L * GlyphCanvasHeight)));

            int row =
                sourceY * frame.Stride;

            for (int x = 0;
                 x < normalizedWidth;
                 x++)
            {
                int sourceX =
                    component.X
                    + Math.Min(
                        component.Width - 1,
                        (int)(
                            (2L * x + 1L)
                            * component.Width
                            / (2L * normalizedWidth)));

                int luma =
                    GetNeutralLuma(
                        frame,
                        row + sourceX * 4);

                normalized[
                    y * GlyphCanvasWidth
                    + xOffset
                    + x] =
                    (byte)Math.Clamp(
                        (int)Math.Round(
                            luma
                            * 255d
                            / maximumLuma),
                        0,
                        255);
            }
        }

        DssNativeDigitClassification classification =
            ClassifyNormalizedDigit(
                normalized,
                maximumAcceptedError: 0.16d,
                useRecordedSmallHudVariants: true);

        if (!classification.Available)
        {
            return
                DigitMatch.Empty;
        }

        return
            new DigitMatch(
                true,
                classification.Digit,
                classification.Confidence);
    }

    internal static DssNativeDigitClassification ClassifyNormalizedDigit(
        ReadOnlySpan<byte> normalized,
        double maximumAcceptedError,
        bool useRecordedSmallHudVariants)
    {
        if (normalized.Length
                != GlyphCanvasWidth
                   * GlyphCanvasHeight
            || maximumAcceptedError <= 0d)
        {
            return
                DssNativeDigitClassification.Empty;
        }

        double bestError =
            double.PositiveInfinity;

        double secondError =
            double.PositiveInfinity;

        int bestDigit = -1;

        for (int digit = 0;
             digit < DigitTemplates.Length;
             digit++)
        {
            double classError =
                CalculateDigitClassError(
                    normalized,
                    digit,
                    useRecordedSmallHudVariants);

            if (classError < bestError)
            {
                secondError =
                    bestError;

                bestError =
                    classError;

                bestDigit =
                    digit;
            }
            else if (classError < secondError)
            {
                secondError =
                    classError;
            }
        }

        double classMargin =
            double.IsFinite(secondError)
                ? secondError
                  - bestError
                : double.PositiveInfinity;

        // The false Algol N7 -> N2 lock had only ~0.0077 normalized MSE
        // between the winning recorded-2 variant and the correct generic 7.
        // A class-level ambiguity gate prevents a newly-added template from
        // stealing another digit merely because both are below the broad
        // absolute-error acceptance threshold.
        const double MinimumClassMargin = 0.010d;

        if (bestDigit < 0
            || !double.IsFinite(bestError)
            || bestError > maximumAcceptedError
            || (useRecordedSmallHudVariants
                && double.IsFinite(classMargin)
                && classMargin < MinimumClassMargin))
        {
            return
                DssNativeDigitClassification.Empty;
        }

        double quality =
            Math.Clamp(
                1d
                - bestError
                  / maximumAcceptedError,
                0d,
                1d);

        double marginScore =
            double.IsFinite(classMargin)
                ? Math.Clamp(
                    classMargin
                    / 0.10d,
                    0d,
                    1d)
                : 1d;

        double confidence =
            quality * 0.70d
            + marginScore * 0.30d;

        return
            new DssNativeDigitClassification(
                true,
                bestDigit,
                confidence,
                bestError,
                secondError,
                classMargin);
    }

    private static double CalculateDigitClassError(
        ReadOnlySpan<byte> normalized,
        int digit,
        bool useRecordedSmallHudVariants)
    {
        double error =
            CalculateNormalizedTemplateError(
                normalized,
                DigitTemplates[
                    Math.Clamp(
                        digit,
                        0,
                        DigitTemplates.Length - 1)]);

        if (!useRecordedSmallHudVariants)
        {
            return
                error;
        }

        if (digit == 2)
        {
            error =
                Math.Min(
                    error,
                    CalculateNormalizedTemplateError(
                        normalized,
                        RecordedNativeDigit2Template));
        }
        else if (digit == 7)
        {
            error =
                Math.Min(
                    error,
                    CalculateNormalizedTemplateError(
                        normalized,
                        RecordedNativeDigit7Template));
        }

        return
            error;
    }

    private static double CalculateNormalizedTemplateError(
        ReadOnlySpan<byte> normalized,
        ReadOnlySpan<byte> template)
    {
        if (normalized.Length != template.Length
            || normalized.Length == 0)
        {
            return double.PositiveInfinity;
        }

        double error = 0d;

        for (int i = 0;
             i < normalized.Length;
             i++)
        {
            double delta =
                normalized[i]
                - template[i];

            error +=
                delta * delta;
        }

        return
            error
            / (normalized.Length
               * 255d
               * 255d);
    }

    private static int GetNeutralLuma(
        DssCapturedFrame frame,
        int index)
    {
        if (index < 0
            || index + 2
               >= frame.Bgra32.Length)
        {
            return 0;
        }

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        int maximum =
            Math.Max(
                red,
                Math.Max(
                    green,
                    blue));

        int minimum =
            Math.Min(
                red,
                Math.Min(
                    green,
                    blue));

        if (maximum - minimum > 110)
        {
            return 0;
        }

        int luma =
            (red * 54
             + green * 183
             + blue * 19) >> 8;

        return luma >= 25
            ? luma
            : 0;
    }

    private static int Scale(
        double referencePixels,
        double scale) =>
        Math.Max(
            1,
            (int)Math.Round(
                referencePixels
                * scale));

    internal static ReadOnlySpan<byte>
        GetDigitTemplateForTests(
            int digit) =>
        DigitTemplates[
            Math.Clamp(
                digit,
                0,
                9)];


    internal static ReadOnlySpan<byte>
        GetRecordedSmallHudTemplateForTests(
            int digit) =>
        digit switch
        {
            2 => RecordedNativeDigit2Template,
            7 => RecordedNativeDigit7Template,
            _ => ReadOnlySpan<byte>.Empty
        };

    private readonly record struct GlyphComponent(
        int X,
        int Y,
        int Width,
        int Height,
        int Area);

    private readonly record struct DigitMatch(
        bool Available,
        int Digit,
        double Confidence)
    {
        public static DigitMatch Empty { get; } =
            new(
                false,
                -1,
                0d);
    }
}
