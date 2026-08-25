using System;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssImpactCounterObservation(
    bool Armed,
    bool Changed,
    double ChangeRatio,
    int ActivePixelCount);

/// <summary>
/// Detects a change in Elite's on-screen DSS "Impacts" counter without OCR.
///
/// The counter is fixed in screen space. Only low-saturation bright HUD glyph
/// pixels are retained, so the moving star field contributes very little.
///
/// The v16 calibration session produced a clean separation:
///
/// true impact transitions: 0.0199 .. 0.0625
/// non-impact transitions:  <= ~0.0078
///
/// A normalized threshold of 0.015 is therefore deliberately conservative.
///
/// This detector reports only "counter changed", not the absolute number.
/// That is sufficient to correlate a successful impact with a prior launch.
/// </summary>
internal sealed class DssImpactCounterChangeDetector
{
    internal const double ChangeThreshold = 0.015;

    // Normalized screen-space ROI covering the right-aligned "Probes / Impacts"
    // numeric block on the DSS HUD. The static "Probes" line is harmless and
    // improves stability; only the "Impacts" digits change.
    private const double RoiLeft = 0.916;
    private const double RoiTop = 0.768;
    private const double RoiRight = 0.941;
    private const double RoiBottom = 0.790;

    private bool[]? previousSignature;
    private int previousWidth;
    private int previousHeight;
    private bool armed;

    public void Reset()
    {
        previousSignature = null;
        previousWidth = 0;
        previousHeight = 0;
        armed = false;
    }

    public DssImpactCounterObservation Process(
        DssCapturedFrame frame)
    {
        int left =
            Math.Clamp(
                (int)Math.Round(
                    frame.Width * RoiLeft),
                0,
                frame.Width - 1);

        int top =
            Math.Clamp(
                (int)Math.Round(
                    frame.Height * RoiTop),
                0,
                frame.Height - 1);

        int right =
            Math.Clamp(
                (int)Math.Round(
                    frame.Width * RoiRight),
                left + 1,
                frame.Width);

        int bottom =
            Math.Clamp(
                (int)Math.Round(
                    frame.Height * RoiBottom),
                top + 1,
                frame.Height);

        int width =
            right - left;

        int height =
            bottom - top;

        bool[] signature =
            BuildSignature(
                frame,
                left,
                top,
                width,
                height,
                out int activePixels);

        if (previousSignature is null
            || previousWidth != width
            || previousHeight != height)
        {
            previousSignature = signature;
            previousWidth = width;
            previousHeight = height;
            armed = true;

            return new DssImpactCounterObservation(
                Armed: true,
                Changed: false,
                ChangeRatio: 0,
                ActivePixelCount: activePixels);
        }

        int changedPixels = 0;

        for (int i = 0;
             i < signature.Length;
             i++)
        {
            if (signature[i]
                != previousSignature[i])
            {
                changedPixels++;
            }
        }

        double ratio =
            signature.Length > 0
                ? changedPixels
                  / (double)signature.Length
                : 0;

        previousSignature = signature;

        bool changed =
            armed
            && ratio >= ChangeThreshold;

        return new DssImpactCounterObservation(
            Armed: armed,
            Changed: changed,
            ChangeRatio: ratio,
            ActivePixelCount: activePixels);
    }

    private static bool[] BuildSignature(
        DssCapturedFrame frame,
        int left,
        int top,
        int width,
        int height,
        out int activePixels)
    {
        bool[] result =
            new bool[
                width * height];

        activePixels = 0;

        for (int y = 0;
             y < height;
             y++)
        {
            int sourceY =
                top + y;

            int row =
                sourceY
                * frame.Stride;

            for (int x = 0;
                 x < width;
                 x++)
            {
                int sourceX =
                    left + x;

                int index =
                    row
                    + sourceX * 4;

                byte b =
                    frame.Bgra32[index];

                byte g =
                    frame.Bgra32[index + 1];

                byte r =
                    frame.Bgra32[index + 2];

                int maximum =
                    Math.Max(
                        r,
                        Math.Max(
                            g,
                            b));

                int minimum =
                    Math.Min(
                        r,
                        Math.Min(
                            g,
                            b));

                int mean =
                    (r + g + b) / 3;

                // Elite's count text is neutral gray/white. Star-field color,
                // cyan/orange HUD elements and dark background are rejected.
                bool active =
                    mean > 55
                    && maximum - minimum < 28;

                result[
                    y * width + x] =
                    active;

                if (active)
                {
                    activePixels++;
                }
            }
        }

        return result;
    }
}
