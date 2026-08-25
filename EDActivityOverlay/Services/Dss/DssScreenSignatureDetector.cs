using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Visual DSS-screen gate.
///
/// A single "cyan near screen centre" test was too weak: the normal cockpit
/// can contain a selected blue planet. v8 requires several DSS-specific HUD
/// structures at their expected screen-relative locations.
///
/// Required:
/// - central DSS cyan reticle arcs;
/// - left DSS vertical scale near the left edge;
/// - right DSS vertical scale near the right edge.
///
/// Optional fourth vote:
/// - lower-right DSS icon/text region.
///
/// This is only a lifecycle safety gate; Status.GuiFocus == 10 remains the
/// journal/session signal.
/// </summary>
internal static class DssScreenSignatureDetector
{
    public static bool IsDssScreen(
        DssCapturedFrame frame)
    {
        bool centerReticle =
            HasCenterReticle(frame);

        bool leftScale =
            HasVerticalScale(
                frame,
                0.050,
                0.085,
                0.31,
                0.73);

        bool rightScale =
            HasVerticalScale(
                frame,
                0.905,
                0.940,
                0.31,
                0.73);

        bool lowerRightDss =
            HasCyanDensity(
                frame,
                0.875,
                0.970,
                0.885,
                0.985,
                0.010);

        int votes =
            (centerReticle ? 1 : 0)
            + (leftScale ? 1 : 0)
            + (rightScale ? 1 : 0)
            + (lowerRightDss ? 1 : 0);

        // Both side scales are particularly characteristic of DSS. Requiring
        // the centre plus both side scales rejected the ordinary cockpit exit
        // frame from the research captures.
        return centerReticle
               && leftScale
               && rightScale
               && votes >= 3;
    }

    private static bool HasCenterReticle(
        DssCapturedFrame frame)
    {
        int cx = frame.Width / 2;
        int cy = frame.Height / 2;

        double scale =
            frame.Height / 1080d;

        double inner =
            28d * scale;

        double outer =
            60d * scale;

        int radius =
            (int)Math.Ceiling(outer)
            + 2;

        int cyanHits = 0;
        int samples = 0;
        int quadrants = 0;

        bool[] quadrantHit =
            new bool[4];

        for (int y = cy - radius;
             y <= cy + radius;
             y += 2)
        {
            for (int x = cx - radius;
                 x <= cx + radius;
                 x += 2)
            {
                if ((uint)x
                    >= (uint)frame.Width
                    || (uint)y
                    >= (uint)frame.Height)
                {
                    continue;
                }

                double dx = x - cx;
                double dy = y - cy;

                double r =
                    Math.Sqrt(
                        dx * dx + dy * dy);

                if (r < inner
                    || r > outer)
                {
                    continue;
                }

                samples++;

                if (!IsFrontierCyan(
                        frame,
                        x,
                        y))
                {
                    continue;
                }

                cyanHits++;

                int quadrant =
                    dx >= 0
                        ? (dy >= 0 ? 0 : 1)
                        : (dy >= 0 ? 2 : 3);

                quadrantHit[quadrant] = true;
            }
        }

        foreach (bool hit
                 in quadrantHit)
        {
            if (hit)
            {
                quadrants++;
            }
        }

        if (samples == 0)
        {
            return false;
        }

        return cyanHits
                   >= Math.Max(
                       16,
                       (int)(samples
                             * 0.030))
               && quadrants >= 3;
    }

    private static bool HasVerticalScale(
        DssCapturedFrame frame,
        double leftRatio,
        double rightRatio,
        double topRatio,
        double bottomRatio)
    {
        int left =
            (int)Math.Round(
                frame.Width
                * leftRatio);

        int right =
            (int)Math.Round(
                frame.Width
                * rightRatio);

        int top =
            (int)Math.Round(
                frame.Height
                * topRatio);

        int bottom =
            (int)Math.Round(
                frame.Height
                * bottomRatio);

        int rowsWithCyan = 0;
        int totalCyan = 0;

        for (int y = top;
             y <= bottom;
             y += 3)
        {
            bool rowHit = false;

            for (int x = left;
                 x <= right;
                 x++)
            {
                if (!IsFrontierCyan(
                        frame,
                        x,
                        y))
                {
                    continue;
                }

                totalCyan++;
                rowHit = true;
            }

            if (rowHit)
            {
                rowsWithCyan++;
            }
        }

        int sampledRows =
            Math.Max(
                1,
                (bottom - top) / 3);

        return rowsWithCyan
                   >= sampledRows * 0.22
               && totalCyan >= 30;
    }

    private static bool HasCyanDensity(
        DssCapturedFrame frame,
        double leftRatio,
        double rightRatio,
        double topRatio,
        double bottomRatio,
        double minimumDensity)
    {
        int left =
            (int)Math.Round(
                frame.Width
                * leftRatio);

        int right =
            (int)Math.Round(
                frame.Width
                * rightRatio);

        int top =
            (int)Math.Round(
                frame.Height
                * topRatio);

        int bottom =
            (int)Math.Round(
                frame.Height
                * bottomRatio);

        int samples = 0;
        int hits = 0;

        for (int y = top;
             y <= bottom;
             y += 2)
        {
            for (int x = left;
                 x <= right;
                 x += 2)
            {
                samples++;

                if (IsFrontierCyan(
                        frame,
                        x,
                        y))
                {
                    hits++;
                }
            }
        }

        return samples > 0
               && hits
                  >= samples
                     * minimumDensity;
    }

    private static bool IsFrontierCyan(
        DssCapturedFrame frame,
        int x,
        int y)
    {
        if ((uint)x
            >= (uint)frame.Width
            || (uint)y
            >= (uint)frame.Height)
        {
            return false;
        }

        int index =
            y * frame.Stride
            + x * 4;

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        return red <= 55
               && green >= 60
               && blue >= 92
               && blue
                  >= green + 18
               && green
                  >= red + 28;
    }
}
