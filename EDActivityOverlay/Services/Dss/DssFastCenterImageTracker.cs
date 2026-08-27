using System;
using System.Collections.Generic;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Coarse-to-fine texture matcher for high-cadence visual DSS motion.
///
/// It deliberately uses the same annulus idea as DssCenterImageTracker, but
/// removes the expensive full 2 px grid x full-template search:
///   1) coarse 4 px search with half the template samples;
///   2) 1 px refinement only around the best coarse candidate.
///
/// Candidate coordinates are integers, so the hot inner loop contains no
/// Math.Round calls.
/// </summary>
internal sealed class DssFastCenterImageTracker
{
    private const int PatchRadius = 46;
    private const int InnerMaskRadius = 15;
    private const int TemplateSampleStep = 4;
    private const int DefaultSearchRadius = 34;
    private const int MaximumSearchRadius = 48;
    private const int CoarseSearchStep = 4;
    private const int CoarseSampleStride = 2;
    private const int FineSearchRadius = 4;

    private SamplePoint[] template =
        Array.Empty<SamplePoint>();

    internal bool HasTemplate =>
        template.Length > 0;

    public void Reset()
    {
        template =
            Array.Empty<SamplePoint>();
    }

    public bool CaptureTemplate(
        DssCapturedFrame frame,
        double centerX,
        double centerY)
    {
        int baseX =
            (int)Math.Round(centerX);

        int baseY =
            (int)Math.Round(centerY);

        var samples =
            new List<SamplePoint>(420);

        for (int dy = -PatchRadius;
             dy <= PatchRadius;
             dy += TemplateSampleStep)
        {
            for (int dx = -PatchRadius;
                 dx <= PatchRadius;
                 dx += TemplateSampleStep)
            {
                int radiusSquared =
                    dx * dx + dy * dy;

                if (radiusSquared
                        < InnerMaskRadius
                          * InnerMaskRadius
                    || radiusSquared
                       > PatchRadius
                         * PatchRadius)
                {
                    continue;
                }

                int x =
                    baseX + dx;

                int y =
                    baseY + dy;

                if ((uint)x
                        >= (uint)frame.Width
                    || (uint)y
                       >= (uint)frame.Height)
                {
                    continue;
                }

                samples.Add(
                    new SamplePoint(
                        dx,
                        dy,
                        GetLuma(
                            frame,
                            x,
                            y)));
            }
        }

        if (samples.Count < 140)
        {
            template =
                Array.Empty<SamplePoint>();

            return false;
        }

        template =
            samples.ToArray();

        return true;
    }

    public bool TryTrack(
        DssCapturedFrame frame,
        double predictedCenterX,
        double predictedCenterY,
        out DssImageTrackResult? result) =>
        TryTrack(
            frame,
            predictedCenterX,
            predictedCenterY,
            DefaultSearchRadius,
            out result);

    public bool TryTrack(
        DssCapturedFrame frame,
        double predictedCenterX,
        double predictedCenterY,
        int searchRadiusPixels,
        out DssImageTrackResult? result)
    {
        result = null;

        if (!HasTemplate)
        {
            return false;
        }

        int predictedX =
            (int)Math.Round(
                predictedCenterX);

        int predictedY =
            (int)Math.Round(
                predictedCenterY);

        int radius =
            Math.Clamp(
                searchRadiusPixels,
                8,
                MaximumSearchRadius);

        MatchCandidate? coarseBest = null;
        MatchCandidate? coarseSecond = null;

        for (int oy = -radius;
             oy <= radius;
             oy += CoarseSearchStep)
        {
            for (int ox = -radius;
                 ox <= radius;
                 ox += CoarseSearchStep)
            {
                MatchCandidate? candidate =
                    EvaluateCandidate(
                        frame,
                        predictedX + ox,
                        predictedY + oy,
                        CoarseSampleStride);

                RegisterCandidate(
                    candidate,
                    ref coarseBest,
                    ref coarseSecond);
            }
        }

        if (coarseBest is null)
        {
            return false;
        }

        double coarseUniqueness =
            coarseSecond is null
                ? 10d
                : coarseSecond.Value.Error
                  - coarseBest.Value.Error;

        // Reject obviously poor/ambiguous coarse locks before spending the
        // full sample set on fine refinement.
        if (coarseBest.Value.Error > 30d
            || coarseUniqueness < 0.25d)
        {
            return false;
        }

        MatchCandidate? fineBest = null;
        MatchCandidate? fineSecond = null;

        int fineBaseX =
            coarseBest.Value.CenterX;

        int fineBaseY =
            coarseBest.Value.CenterY;

        for (int oy = -FineSearchRadius;
             oy <= FineSearchRadius;
             oy++)
        {
            for (int ox = -FineSearchRadius;
                 ox <= FineSearchRadius;
                 ox++)
            {
                MatchCandidate? candidate =
                    EvaluateCandidate(
                        frame,
                        fineBaseX + ox,
                        fineBaseY + oy,
                        sampleStride: 1);

                RegisterCandidate(
                    candidate,
                    ref fineBest,
                    ref fineSecond);
            }
        }

        if (fineBest is null
            || fineBest.Value.Error > 27d)
        {
            return false;
        }

        // Fine candidates one pixel apart are expected to be similar, so the
        // ambiguity gate intentionally comes from the coarser 4 px lattice.
        double confidence =
            Math.Clamp(
                1d
                - fineBest.Value.Error
                  / 32d,
                0.15d,
                0.92d);

        result =
            new DssImageTrackResult(
                fineBest.Value.CenterX,
                fineBest.Value.CenterY,
                confidence,
                fineBest.Value.Error);

        return true;
    }

    private MatchCandidate? EvaluateCandidate(
        DssCapturedFrame frame,
        int candidateX,
        int candidateY,
        int sampleStride)
    {
        long sumAbsoluteDifference = 0;
        int valid = 0;
        int expected =
            (template.Length
             + sampleStride - 1)
            / sampleStride;

        for (int i = 0;
             i < template.Length;
             i += sampleStride)
        {
            SamplePoint sample =
                template[i];

            int x =
                candidateX
                + sample.OffsetX;

            int y =
                candidateY
                + sample.OffsetY;

            if ((uint)x
                    >= (uint)frame.Width
                || (uint)y
                   >= (uint)frame.Height)
            {
                continue;
            }

            int current =
                GetLuma(
                    frame,
                    x,
                    y);

            sumAbsoluteDifference +=
                Math.Abs(
                    current
                    - sample.Luma);

            valid++;
        }

        if (valid
            < expected * 0.72d)
        {
            return null;
        }

        return
            new MatchCandidate(
                candidateX,
                candidateY,
                (double)sumAbsoluteDifference
                / valid);
    }

    private static void RegisterCandidate(
        MatchCandidate? candidate,
        ref MatchCandidate? best,
        ref MatchCandidate? second)
    {
        if (candidate is null)
        {
            return;
        }

        if (best is null
            || candidate.Value.Error
               < best.Value.Error)
        {
            second =
                best;

            best =
                candidate;

            return;
        }

        if (second is null
            || candidate.Value.Error
               < second.Value.Error)
        {
            second =
                candidate;
        }
    }

    private static int GetLuma(
        DssCapturedFrame frame,
        int x,
        int y)
    {
        int index =
            y * frame.Stride
            + x * 4;

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        return (
            red * 54
            + green * 183
            + blue * 19) >> 8;
    }

    private readonly record struct SamplePoint(
        int OffsetX,
        int OffsetY,
        int Luma);

    private readonly record struct MatchCandidate(
        int CenterX,
        int CenterY,
        double Error);
}
