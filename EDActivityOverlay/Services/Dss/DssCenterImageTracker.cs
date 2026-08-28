using System;
using System.Collections.Generic;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssImageTrackResult(
    double CenterX,
    double CenterY,
    double Confidence,
    double MeanError);

/// <summary>
/// Lightweight rendered-image motion tracker. It stores an annulus around the
/// last confirmed DSS body centre (the centre marker itself is masked out)
/// and matches that texture near the velocity-predicted location.
/// </summary>
internal sealed class DssCenterImageTracker
{
    private const int PatchRadius = 46;
    private const int InnerMaskRadius = 15;
    private const int SampleStep = 3;
    private const int SearchRadius = 34;
    private const int SearchStep = 2;

    private SamplePoint[] template =
        Array.Empty<SamplePoint>();

    private bool hasTemplate;

    public void Reset()
    {
        template = Array.Empty<SamplePoint>();
        hasTemplate = false;
    }

    public void CaptureTemplate(
        DssCapturedFrame frame,
        double centerX,
        double centerY)
    {
        var samples =
            new List<SamplePoint>();

        for (int dy = -PatchRadius;
             dy <= PatchRadius;
             dy += SampleStep)
        {
            for (int dx = -PatchRadius;
                 dx <= PatchRadius;
                 dx += SampleStep)
            {
                int radiusSquared =
                    dx * dx + dy * dy;

                if (radiusSquared
                    < InnerMaskRadius * InnerMaskRadius
                    || radiusSquared
                    > PatchRadius * PatchRadius)
                {
                    continue;
                }

                int x =
                    (int)Math.Round(centerX + dx);

                int y =
                    (int)Math.Round(centerY + dy);

                if ((uint)x >= (uint)frame.Width
                    || (uint)y >= (uint)frame.Height)
                {
                    continue;
                }

                samples.Add(
                    new SamplePoint(
                        dx,
                        dy,
                        GetLuma(frame, x, y)));
            }
        }

        if (samples.Count < 220)
        {
            return;
        }

        template = samples.ToArray();
        hasTemplate = true;
    }

    public bool TryTrack(
        DssCapturedFrame frame,
        double predictedCenterX,
        double predictedCenterY,
        out DssImageTrackResult? result)
    {
        result = null;

        if (!hasTemplate
            || template.Length == 0)
        {
            return false;
        }

        MatchCandidate? best = null;
        MatchCandidate? second = null;

        for (int oy = -SearchRadius;
             oy <= SearchRadius;
             oy += SearchStep)
        {
            for (int ox = -SearchRadius;
                 ox <= SearchRadius;
                 ox += SearchStep)
            {
                MatchCandidate? candidate =
                    EvaluateCandidate(
                        frame,
                        predictedCenterX + ox,
                        predictedCenterY + oy);

                if (candidate is null)
                {
                    continue;
                }

                if (best is null
                    || candidate.Error < best.Error)
                {
                    second = best;
                    best = candidate;
                }
                else if (second is null
                         || candidate.Error < second.Error)
                {
                    second = candidate;
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        double uniqueness =
            second is null
                ? 10
                : second.Error - best.Error;

        // Smooth/featureless patches are deliberately rejected instead of
        // inventing precise camera motion.
        if (best.Error > 27
            || uniqueness < 0.45)
        {
            return false;
        }

        double confidence =
            Math.Clamp(
                1d - best.Error / 32d,
                0.15,
                0.88);

        result = new DssImageTrackResult(
            best.CenterX,
            best.CenterY,
            confidence,
            best.Error);

        return true;
    }

    private MatchCandidate? EvaluateCandidate(
        DssCapturedFrame frame,
        double candidateX,
        double candidateY)
    {
        long sumAbsoluteDifference = 0;
        int valid = 0;

        foreach (SamplePoint sample
                 in template)
        {
            int x =
                (int)Math.Round(
                    candidateX
                    + sample.OffsetX);

            int y =
                (int)Math.Round(
                    candidateY
                    + sample.OffsetY);

            if ((uint)x >= (uint)frame.Width
                || (uint)y >= (uint)frame.Height)
            {
                continue;
            }

            int current =
                GetLuma(frame, x, y);

            sumAbsoluteDifference +=
                Math.Abs(
                    current - sample.Luma);

            valid++;
        }

        if (valid
            < template.Length * 0.72)
        {
            return null;
        }

        return new MatchCandidate(
            candidateX,
            candidateY,
            (double)sumAbsoluteDifference / valid);
    }

    private static int GetLuma(
        DssCapturedFrame frame,
        int x,
        int y)
    {
        int index =
            y * frame.Stride + x * 4;

        int blue = frame.Bgra32[index];
        int green = frame.Bgra32[index + 1];
        int red = frame.Bgra32[index + 2];

        return (red * 54
                + green * 183
                + blue * 19) >> 8;
    }

    private readonly record struct SamplePoint(
        int OffsetX,
        int OffsetY,
        int Luma);

    private sealed record MatchCandidate(
        double CenterX,
        double CenterY,
        double Error);
}
