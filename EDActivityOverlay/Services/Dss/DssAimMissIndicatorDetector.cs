using System;
using System.Collections.Generic;

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

        int roiWidth =
            right - left;

        int roiHeight =
            bottom - top;

        int total =
            Math.Max(
                1,
                roiWidth * roiHeight);

        bool[] activeMask =
            new bool[total];

        int active = 0;

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

                    activeMask[
                        (y - top) * roiWidth
                        + (x - left)] = true;
                }
            }
        }

        double ratio =
            active / (double)total;

        double scale =
            Math.Clamp(
                frame.Height / 1080d,
                0.50,
                2.00);

        IndicatorShape shape =
            MeasureIndicatorShape(
                activeMask,
                roiWidth,
                roiHeight,
                scale);

        bool visible =
            IsIndicatorShapeAccepted(
                ratio,
                shape.QualifyingComponents,
                shape.SpanWidth,
                shape.SpanHeight,
                scale);

        return new DssAimMissObservation(
            visible,
            ratio,
            active);
    }

    internal static bool IsIndicatorShapeAccepted(
        double activeRatio,
        int qualifyingComponents,
        int spanWidth,
        int spanHeight,
        double scale)
    {
        if (activeRatio < VisibleRatioThreshold
            || qualifyingComponents < 3)
        {
            return false;
        }

        double clampedScale =
            Math.Clamp(
                scale,
                0.50,
                2.00);

        int minimumSpanWidth =
            Math.Max(
                14,
                (int)Math.Round(
                    28d * clampedScale));

        int maximumSpanHeight =
            Math.Max(
                16,
                (int)Math.Round(
                    28d * clampedScale));

        return spanWidth >= minimumSpanWidth
               && spanHeight <= maximumSpanHeight;
    }

    private static IndicatorShape MeasureIndicatorShape(
        bool[] activeMask,
        int width,
        int height,
        double scale)
    {
        if (width <= 0
            || height <= 0
            || activeMask.Length < width * height)
        {
            return IndicatorShape.Empty;
        }

        double areaScale =
            Math.Clamp(
                scale * scale,
                0.25,
                4.00);

        int minimumComponentPixels =
            Math.Max(
                4,
                (int)Math.Round(
                    8d * areaScale));

        bool[] visited =
            new bool[width * height];

        var queue =
            new Queue<int>();

        int qualifyingComponents = 0;
        int unionMinX = width;
        int unionMinY = height;
        int unionMaxX = -1;
        int unionMaxY = -1;

        for (int y = 0;
             y < height;
             y++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                int startIndex =
                    y * width + x;

                if (!activeMask[startIndex]
                    || visited[startIndex])
                {
                    continue;
                }

                visited[startIndex] = true;
                queue.Enqueue(startIndex);

                int componentPixels = 0;
                int minX = x;
                int minY = y;
                int maxX = x;
                int maxY = y;

                while (queue.Count > 0)
                {
                    int current =
                        queue.Dequeue();

                    int currentY =
                        current / width;

                    int currentX =
                        current - currentY * width;

                    componentPixels++;
                    minX = Math.Min(minX, currentX);
                    minY = Math.Min(minY, currentY);
                    maxX = Math.Max(maxX, currentX);
                    maxY = Math.Max(maxY, currentY);

                    for (int offsetY = -1;
                         offsetY <= 1;
                         offsetY++)
                    {
                        for (int offsetX = -1;
                             offsetX <= 1;
                             offsetX++)
                        {
                            if (offsetX == 0
                                && offsetY == 0)
                            {
                                continue;
                            }

                            int nextX =
                                currentX + offsetX;

                            int nextY =
                                currentY + offsetY;

                            if ((uint)nextX >= (uint)width
                                || (uint)nextY >= (uint)height)
                            {
                                continue;
                            }

                            int nextIndex =
                                nextY * width + nextX;

                            if (!activeMask[nextIndex]
                                || visited[nextIndex])
                            {
                                continue;
                            }

                            visited[nextIndex] = true;
                            queue.Enqueue(nextIndex);
                        }
                    }
                }

                if (componentPixels
                    < minimumComponentPixels)
                {
                    continue;
                }

                qualifyingComponents++;
                unionMinX = Math.Min(unionMinX, minX);
                unionMinY = Math.Min(unionMinY, minY);
                unionMaxX = Math.Max(unionMaxX, maxX);
                unionMaxY = Math.Max(unionMaxY, maxY);
            }
        }

        if (qualifyingComponents == 0)
        {
            return IndicatorShape.Empty;
        }

        return new IndicatorShape(
            qualifyingComponents,
            unionMaxX - unionMinX + 1,
            unionMaxY - unionMinY + 1);
    }

    private readonly record struct IndicatorShape(
        int QualifyingComponents,
        int SpanWidth,
        int SpanHeight)
    {
        public static IndicatorShape Empty { get; } =
            new(0, 0, 0);
    }
}
