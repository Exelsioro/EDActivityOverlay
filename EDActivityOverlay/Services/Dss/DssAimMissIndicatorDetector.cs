using System;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssAimMissObservation(
    bool Visible,
    double ActiveRatio,
    int ActivePixels);

/// <summary>
/// Detects Elite's native "MISS" / "Промах" DSS trajectory indicator.
///
/// The indicator is fixed directly below the DSS reticle. We do not OCR the
/// word; we only detect the appearance of its neutral white glyph pixels.
///
/// Calibration from the supplied 1920x1080 v17 run:
///
/// no MISS indicator: ~0-2 active pixels in the ROI
/// MISS visible:       ~185-195 active pixels
///
/// The ratio threshold is intentionally far below that measured separation so
/// the same logic remains usable with English text and moderate scaling.
/// </summary>
internal static class DssAimMissIndicatorDetector
{
    internal const double VisibleRatioThreshold = 0.012;

    // 1920x1080 equivalent: x=920..1000, y=580..620.
    private const double RoiLeft = 0.4791666667;
    private const double RoiTop = 0.5370370370;
    private const double RoiRight = 0.5208333333;
    private const double RoiBottom = 0.5740740741;

    public static DssAimMissObservation Detect(
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

        int active = 0;
        int total =
            Math.Max(
                1,
                (right - left)
                * (bottom - top));

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
                    row + x * 4;

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

                if (mean >= 85
                    && maximum - minimum <= 34)
                {
                    active++;
                }
            }
        }

        double ratio =
            active / (double)total;

        return new DssAimMissObservation(
            ratio >= VisibleRatioThreshold,
            ratio,
            active);
    }
}
