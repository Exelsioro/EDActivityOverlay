using System;
using System.Collections.Generic;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssCoverageObservation(
    bool Available,
    bool Settling,
    double CoveredFraction,
    double Confidence,
    int SuggestedCandidateId,
    double SuggestedNormalizedX,
    double SuggestedNormalizedY,
    double SuggestedUncoveredScore)
{
    public static DssCoverageObservation Empty { get; } =
        new(
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0);
}

/// <summary>
/// Experimental DSS coverage observer.
///
/// It never tries to identify the physical colour of the planet. Before the
/// first confirmed impact it learns a normalized-body baseline at a fixed set
/// of candidate locations. After an impact it looks for the blue/cyan DSS
/// mapping overlay and ranks still-uncovered locations inside the reliable
/// interior of the projected disc.
///
/// The observer is intentionally coarse. The output is a stable candidate for
/// the next live experiment, not a claim that the complete spherical coverage
/// solution is already known.
/// </summary>
internal sealed class DssCoverageObserver
{
    private static readonly TimeSpan ImpactSettleDelay =
        TimeSpan.FromMilliseconds(1500);

    private const double SampleRadiusNormalized = 0.13d;
    private const double MinimumMaskEvidence = 0.18d;
    private const double MinimumUncoveredScore = 0.24d;
    private const double CandidateHysteresis = 0.08d;

    private readonly CoverageCandidate[] candidates =
        BuildCandidates();

    private readonly double[] baseline;
    private int baselineFrames;
    private int latchedCandidateId;
    private DateTimeOffset settleUntilUtc =
        DateTimeOffset.MinValue;

    public DssCoverageObserver()
    {
        baseline =
            new double[candidates.Length];
    }

    public void Reset()
    {
        Array.Clear(
            baseline,
            0,
            baseline.Length);

        baselineFrames = 0;
        latchedCandidateId = 0;
        settleUntilUtc =
            DateTimeOffset.MinValue;
    }

    public void NotifyImpact(
        DateTimeOffset timestampUtc)
    {
        settleUntilUtc =
            timestampUtc
            + ImpactSettleDelay;

        // A landed probe invalidates the previous best hole. Re-evaluate after
        // the native blue-mask animation has had a short time to settle.
        latchedCandidateId = 0;
    }

    public DssCoverageObservation Process(
        DssCapturedFrame frame,
        DssHudGeometry geometry,
        bool enabled,
        long excludedCandidateMask)
    {
        if (!geometry.BodyCenterFound
            || !geometry.HorizonMarkerFound
            || geometry.HorizonRadiusPixels < 80
            || !double.IsFinite(geometry.BodyCenterX)
            || !double.IsFinite(geometry.BodyCenterY))
        {
            return DssCoverageObservation.Empty;
        }

        double[] rawCoverage =
            new double[candidates.Length];

        for (int index = 0;
             index < candidates.Length;
             index++)
        {
            rawCoverage[index] =
                MeasureCoverageFraction(
                    frame,
                    geometry,
                    candidates[index]);
        }

        if (!enabled)
        {
            UpdateBaseline(
                rawCoverage);

            latchedCandidateId = 0;
            return DssCoverageObservation.Empty;
        }

        if (settleUntilUtc
                != DateTimeOffset.MinValue
            && frame.TimestampUtc
               < settleUntilUtc)
        {
            return DssCoverageObservation.Empty with
            {
                Settling = true
            };
        }

        bool hasBaseline =
            baselineFrames >= 3;

        double coveredSum = 0;
        double maximumCoverageEvidence = 0;
        double bestRank =
            double.NegativeInfinity;
        double bestUncovered = 0;
        CoverageCandidate? best = null;

        var adjusted =
            new Dictionary<int, double>();

        for (int index = 0;
             index < candidates.Length;
             index++)
        {
            CoverageCandidate candidate =
                candidates[index];

            double current =
                rawCoverage[index];

            double covered =
                CorrectForBaseline(
                    current,
                    baseline[index],
                    hasBaseline);

            adjusted[candidate.Id] =
                covered;

            coveredSum += covered;
            maximumCoverageEvidence =
                Math.Max(
                    maximumCoverageEvidence,
                    covered);

            if (IsExcluded(
                    excludedCandidateMask,
                    candidate.Id))
            {
                continue;
            }

            double uncovered =
                1d - covered;

            // Prefer the outer reliable ring when two holes look equally
            // uncovered; it tends to add more new surface area. The radius is
            // capped at 0.68 Rh so limb foreshortening does not dominate the
            // blue-mask classifier.
            double rank =
                uncovered
                * (1d
                   + candidate.Radius
                     * 0.12d);

            if (rank > bestRank)
            {
                bestRank = rank;
                bestUncovered = uncovered;
                best = candidate;
            }
        }

        if (best is null
            || maximumCoverageEvidence
               <= MinimumMaskEvidence)
        {
            latchedCandidateId = 0;

            return new DssCoverageObservation(
                false,
                false,
                coveredSum
                / Math.Max(1, candidates.Length),
                Math.Clamp(
                    maximumCoverageEvidence
                    / MinimumMaskEvidence,
                    0d,
                    1d),
                0,
                0,
                0,
                0);
        }

        // Small frame-to-frame changes in the animated DSS grid must not make
        // NEXT AIM jump around. Keep the previous candidate while it remains
        // nearly as good as the instantaneous best.
        if (latchedCandidateId > 0
            && !IsExcluded(
                excludedCandidateMask,
                latchedCandidateId))
        {
            CoverageCandidate? previous =
                FindCandidate(
                    latchedCandidateId);

            if (previous is not null
                && adjusted.TryGetValue(
                    previous.Id,
                    out double previousCovered))
            {
                double previousUncovered =
                    1d - previousCovered;

                if (previousUncovered
                    >= bestUncovered
                       - CandidateHysteresis)
                {
                    best = previous;
                    bestUncovered =
                        previousUncovered;
                }
            }
        }

        if (bestUncovered
            < MinimumUncoveredScore)
        {
            latchedCandidateId = 0;

            return new DssCoverageObservation(
                false,
                false,
                coveredSum
                / Math.Max(1, candidates.Length),
                1d,
                0,
                0,
                0,
                bestUncovered);
        }

        latchedCandidateId =
            best.Id;

        double confidence =
            Math.Clamp(
                (maximumCoverageEvidence
                 - MinimumMaskEvidence)
                / 0.55d
                + 0.35d,
                0d,
                1d);

        return new DssCoverageObservation(
            true,
            false,
            coveredSum
            / Math.Max(1, candidates.Length),
            confidence,
            best.Id,
            best.X,
            best.Y,
            bestUncovered);
    }

    private void UpdateBaseline(
        IReadOnlyList<double> current)
    {
        const double alpha = 0.18d;

        for (int index = 0;
             index < baseline.Length;
             index++)
        {
            baseline[index] =
                baselineFrames == 0
                    ? current[index]
                    : baseline[index]
                      * (1d - alpha)
                      + current[index]
                        * alpha;
        }

        baselineFrames++;
    }

    private static double CorrectForBaseline(
        double current,
        double baselineValue,
        bool hasBaseline)
    {
        if (!hasBaseline)
        {
            return current;
        }

        // A naturally blue planet can already satisfy the absolute hue test.
        // If that blue score did not materially change after the first impact,
        // treat most of it as body colour rather than DSS coverage.
        if (baselineValue >= 0.30d
            && current
               <= baselineValue + 0.08d)
        {
            return current * 0.18d;
        }

        return current;
    }

    private static double MeasureCoverageFraction(
        DssCapturedFrame frame,
        DssHudGeometry geometry,
        CoverageCandidate candidate)
    {
        double radiusPixels =
            geometry.HorizonRadiusPixels;

        double centerX =
            geometry.BodyCenterX
            + candidate.X
              * radiusPixels;

        double centerY =
            geometry.BodyCenterY
            + candidate.Y
              * radiusPixels;

        double sampleRadiusPixels =
            Math.Max(
                6d,
                radiusPixels
                * SampleRadiusNormalized);

        int left =
            Math.Max(
                0,
                (int)Math.Floor(
                    centerX
                    - sampleRadiusPixels));

        int right =
            Math.Min(
                frame.Width - 1,
                (int)Math.Ceiling(
                    centerX
                    + sampleRadiusPixels));

        int top =
            Math.Max(
                0,
                (int)Math.Floor(
                    centerY
                    - sampleRadiusPixels));

        int bottom =
            Math.Min(
                frame.Height - 1,
                (int)Math.Ceiling(
                    centerY
                    + sampleRadiusPixels));

        int samples = 0;
        int covered = 0;

        for (int y = top;
             y <= bottom;
             y += 3)
        {
            for (int x = left;
                 x <= right;
                 x += 3)
            {
                double dx =
                    x - centerX;

                double dy =
                    y - centerY;

                if (dx * dx + dy * dy
                    > sampleRadiusPixels
                      * sampleRadiusPixels)
                {
                    continue;
                }

                samples++;

                if (IsCoveragePixel(
                        frame,
                        x,
                        y))
                {
                    covered++;
                }
            }
        }

        return samples > 0
            ? covered / (double)samples
            : 0;
    }

    internal static bool IsCoveragePixel(
        DssCapturedFrame frame,
        int x,
        int y)
    {
        if ((uint)x >= (uint)frame.Width
            || (uint)y >= (uint)frame.Height)
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

        int blueExcess =
            blue - red;

        if (blue < 24
            || green - red < 5
            || blue - green < 5
            || blueExcess < 14)
        {
            return false;
        }

        double normalizedBlueExcess =
            blueExcess
            / (double)Math.Max(
                24,
                blue);

        return normalizedBlueExcess
               >= 0.30d;
    }

    private CoverageCandidate? FindCandidate(
        int id)
    {
        foreach (CoverageCandidate candidate
                 in candidates)
        {
            if (candidate.Id == id)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsExcluded(
        long mask,
        int candidateId)
    {
        if (candidateId <= 0
            || candidateId >= 63)
        {
            return false;
        }

        long bit =
            1L << candidateId;

        return (mask & bit) != 0;
    }

    private static CoverageCandidate[]
        BuildCandidates()
    {
        var result =
            new List<CoverageCandidate>();

        int id = 1;

        // Tie order intentionally follows the successful empirical prefix:
        // upper -> centre -> lower -> right -> left, then diagonals.
        Add(result, ref id, 0.68d, -90d);
        result.Add(new CoverageCandidate(id++, 0, 0));
        Add(result, ref id, 0.68d, 90d);
        Add(result, ref id, 0.68d, 0d);
        Add(result, ref id, 0.68d, 180d);

        foreach (double angle
                 in new[]
                 {
                     -45d, 45d, 135d, -135d,
                     -60d, -30d, 30d, 60d,
                     120d, 150d, -150d, -120d
                 })
        {
            Add(
                result,
                ref id,
                0.68d,
                angle);
        }

        foreach (double angle
                 in new[]
                 {
                     -90d, 90d, 0d, 180d,
                     -45d, 45d, 135d, -135d
                 })
        {
            Add(
                result,
                ref id,
                0.42d,
                angle);
        }

        return result.ToArray();
    }

    private static void Add(
        ICollection<CoverageCandidate> result,
        ref int id,
        double radius,
        double angleDegrees)
    {
        double angle =
            angleDegrees
            * Math.PI
            / 180d;

        result.Add(
            new CoverageCandidate(
                id++,
                radius * Math.Cos(angle),
                radius * Math.Sin(angle)));
    }

    private sealed record CoverageCandidate(
        int Id,
        double X,
        double Y)
    {
        public double Radius =>
            Math.Sqrt(
                X * X
                + Y * Y);
    }
}
