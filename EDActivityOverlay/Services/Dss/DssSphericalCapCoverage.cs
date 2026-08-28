using System;
using System.Collections.Generic;
using System.Numerics;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssCoverageCorrectionPoint(
    SphericalPoint Point,
    double IncrementalCoverage,
    double TotalCoverage);

/// <summary>
/// Numerical spherical-cap coverage model on S^2.
///
/// Important unit rule:
/// Journal DSS_PatchRadius is a scanner stat, not a physical length in metres.
/// Never divide PatchRadius by BodyRadiusMeters.
///
/// Empirical DSS engineering behavior is treated as a multiplier of the
/// covered spherical-cap AREA, not as a multiplier of the cap's angular
/// radius. Therefore an engineering multiplier m transforms
///
///     A(alpha_actual) = clamp(m * A(alpha_stock), 0, 4*pi)
///
/// where A(alpha)/(4*pi) = (1-cos(alpha))/2.
///
/// The absolute stock cap angle is inferred from the body's official
/// EfficiencyTarget: for the stock layout at official N, solve the cap angle
/// that yields 90% union coverage.
/// </summary>
internal static class DssSphericalCapCoverage
{
    private const int EvaluationSampleCount = 4096;
    // The correction search is independent from the 4096-point engineering
    // evaluator. A 3072-point mask plus 256 candidate centres is accurate
    // enough to preserve hemisphere-scale gaps, while bitset popcount keeps the
    // one-time correction-plan build cheap enough for the live HUD path.
    private const int CorrectionEvaluationSampleCount = 3072;
    private const int CorrectionCandidateCount = 256;

    // Base-plan optimizer. Unlike the correction grid, this runs once per
    // (N, cap radius) and is cached by the placement planner. 768 candidate
    // centres against the existing 4096-point evaluation grid are enough to
    // materially improve overlap while keeping the one-time solve cheap.
    private const int BasePlanCandidateCount = 384;
    private const int BasePlanRefinementPasses = 3;

    private static readonly SphericalPoint[] SampleGrid =
        GenerateFibonacciGrid(
            EvaluationSampleCount);

    private static readonly SphericalPoint[] CorrectionSampleGrid =
        GenerateFibonacciGrid(
            CorrectionEvaluationSampleCount);

    private static readonly SphericalPoint[] CorrectionCandidateGrid =
        GenerateCorrectionCandidateGrid();

    private static readonly SphericalPoint[] BasePlanCandidateGrid =
        GenerateBasePlanCandidateGrid();

    /// <summary>
    /// Dimensionless engineering footprint multiplier from Journal values.
    /// Missing/invalid data is deliberately conservative (1.0);
    /// EngineeringLevel alone is not used to guess a footprint.
    /// </summary>
    public static double ResolveProbeRadiusMultiplier(
        DssModuleSnapshot dssModule)
    {
        if (dssModule.PatchRadius <= 0d
            || dssModule.OriginalPatchRadius <= 0d)
        {
            return 1d;
        }

        double multiplier =
            dssModule.PatchRadius
            / dssModule.OriginalPatchRadius;

        if (!double.IsFinite(multiplier)
            || multiplier <= 0d)
        {
            return 1d;
        }

        return
            Math.Clamp(
                multiplier,
                0.50d,
                3.00d);
    }

    /// <summary>
    /// Compatibility helper used by tests/research. BodyRadiusMeters is
    /// intentionally ignored because PatchRadius is not measured in metres.
    /// </summary>
    public static double CalculateCapAngularRadius(
        DssModuleSnapshot dssModule,
        double bodyRadiusMeters,
        int targetProbeCount)
    {
        _ = bodyRadiusMeters;

        int n =
            Math.Clamp(
                targetProbeCount,
                DssSphericalPlacementPlanner.MinimumTargetCount,
                DssSphericalPlacementPlanner.MaximumTargetCount);

        IReadOnlyList<SphericalPoint> stockLayout =
            DssSphericalPlacementPlanner
                .GenerateOptimalSphericalPoints(n);

        double stockAlpha =
            SolveCapAngularRadiusForCoverage(
                stockLayout,
                0.90d);

        double multiplier =
            ResolveProbeRadiusMultiplier(
                dssModule);

        return
            ScaleCapAngularRadiusByArea(
                stockAlpha,
                multiplier);
    }

    /// <summary>
    /// Applies an engineering footprint multiplier to spherical-cap area.
    /// This is intentionally nonlinear in alpha.
    /// </summary>
    public static double ScaleCapAngularRadiusByArea(
        double stockCapAngularRadiusRadians,
        double areaMultiplier)
    {
        if (!double.IsFinite(
                stockCapAngularRadiusRadians)
            || stockCapAngularRadiusRadians <= 0d)
        {
            return 0d;
        }

        double multiplier =
            double.IsFinite(areaMultiplier)
                ? Math.Max(
                    0d,
                    areaMultiplier)
                : 1d;

        double stockAreaFraction =
            SingleCapAreaFraction(
                stockCapAngularRadiusRadians);

        double actualAreaFraction =
            Math.Clamp(
                stockAreaFraction
                * multiplier,
                0d,
                1d);

        double cosAlpha =
            Math.Clamp(
                1d
                - 2d * actualAreaFraction,
                -1d,
                1d);

        return
            Math.Acos(
                cosAlpha);
    }

    /// <summary>
    /// Finds the smallest cap angular radius whose union coverage reaches
    /// targetCoverageFraction for the supplied fixed layout.
    /// </summary>
    public static double SolveCapAngularRadiusForCoverage(
        IReadOnlyList<SphericalPoint> points,
        double targetCoverageFraction = 0.90d)
    {
        if (points is null
            || points.Count == 0)
        {
            return 0d;
        }

        double target =
            Math.Clamp(
                targetCoverageFraction,
                0d,
                1d);

        double low = 0d;
        double high = Math.PI / 2d;

        for (int iteration = 0;
             iteration < 48;
             iteration++)
        {
            double mid =
                (low + high) * 0.5d;

            double coverage =
                EvaluateUnionCoverage(
                    points,
                    mid);

            if (coverage >= target)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return high;
    }

    public static double EvaluateUnionCoverage(
        IReadOnlyList<SphericalPoint> points,
        double capAngularRadiusRadians)
    {
        if (points is null
            || points.Count == 0
            || capAngularRadiusRadians <= 0d)
        {
            return 0d;
        }

        double cosCapRadius =
            Math.Cos(
                capAngularRadiusRadians);

        int coveredCount = 0;

        for (int i = 0;
             i < SampleGrid.Length;
             i++)
        {
            if (IsCovered(
                    SampleGrid[i],
                    points,
                    cosCapRadius))
            {
                coveredCount++;
            }
        }

        return
            (double)coveredCount
            / SampleGrid.Length;
    }

    /// <summary>
    /// Coverage fraction added by one candidate on top of the supplied points.
    /// </summary>
    public static double EvaluateIncrementalCoverage(
        IReadOnlyList<SphericalPoint> existingPoints,
        SphericalPoint candidate,
        double capAngularRadiusRadians)
    {
        if (capAngularRadiusRadians <= 0d)
        {
            return 0d;
        }

        double cosCapRadius =
            Math.Cos(
                capAngularRadiusRadians);

        int added = 0;

        for (int i = 0;
             i < SampleGrid.Length;
             i++)
        {
            SphericalPoint sample =
                SampleGrid[i];

            if (IsCovered(
                    sample,
                    existingPoints,
                    cosCapRadius))
            {
                continue;
            }

            if (Dot(
                    sample,
                    candidate)
                >= cosCapRadius)
            {
                added++;
            }
        }

        return
            (double)added
            / SampleGrid.Length;
    }

    /// <summary>
    /// Builds a deterministic whole-sphere correction tail. Each next point
    /// maximizes NEW covered area after the base plan and all prior correction
    /// points. This is deliberately global: a small visible-side CV hole cannot
    /// displace a much larger missing region on the rear hemisphere.
    /// </summary>
    public static IReadOnlyList<DssCoverageCorrectionPoint>
        GenerateGreedyCorrectionPlan(
            IReadOnlyList<SphericalPoint> basePoints,
            double capAngularRadiusRadians,
            int maximumShots)
    {
        if (basePoints is null
            || capAngularRadiusRadians <= 0d
            || maximumShots <= 0)
        {
            return
                Array.Empty<DssCoverageCorrectionPoint>();
        }

        double cosCapRadius =
            Math.Cos(
                capAngularRadiusRadians);

        int wordCount =
            (CorrectionSampleGrid.Length + 63)
            / 64;

        var covered =
            new ulong[wordCount];

        MarkPointsCoverageMask(
            covered,
            basePoints,
            cosCapRadius);

        ulong[][] candidateMasks =
            BuildCandidateCoverageMasks(
                wordCount,
                cosCapRadius);

        var occupied =
            new List<SphericalPoint>(
                basePoints);

        var result =
            new List<DssCoverageCorrectionPoint>(
                maximumShots);

        var usedCandidates =
            new bool[
                CorrectionCandidateGrid.Length];

        for (int shot = 0;
             shot < maximumShots;
             shot++)
        {
            int bestIndex = -1;
            int bestAddedCount = -1;
            double bestMinimumSpacing =
                double.NegativeInfinity;

            for (int candidateIndex = 0;
                 candidateIndex
                 < CorrectionCandidateGrid.Length;
                 candidateIndex++)
            {
                if (usedCandidates[
                        candidateIndex])
                {
                    continue;
                }

                int addedCount =
                    CountNewMaskBits(
                        covered,
                        candidateMasks[
                            candidateIndex]);

                if (addedCount
                    < bestAddedCount)
                {
                    continue;
                }

                double minimumSpacing =
                    MinimumAngularDistance(
                        CorrectionCandidateGrid[
                            candidateIndex],
                        occupied);

                if (addedCount
                        > bestAddedCount
                    || (addedCount
                            == bestAddedCount
                        && minimumSpacing
                           > bestMinimumSpacing
                              + 1e-12d))
                {
                    bestIndex =
                        candidateIndex;

                    bestAddedCount =
                        addedCount;

                    bestMinimumSpacing =
                        minimumSpacing;
                }
            }

            if (bestIndex < 0
                || bestAddedCount <= 0)
            {
                break;
            }

            usedCandidates[bestIndex] =
                true;

            SphericalPoint selected =
                CorrectionCandidateGrid[
                    bestIndex];

            MergeCoverageMask(
                covered,
                candidateMasks[
                    bestIndex]);

            int coveredCount =
                CountMaskBits(
                    covered);

            occupied.Add(
                selected);

            result.Add(
                new DssCoverageCorrectionPoint(
                    selected,
                    bestAddedCount
                    / (double)
                      CorrectionSampleGrid.Length,
                    coveredCount
                    / (double)
                      CorrectionSampleGrid.Length));
        }

        return result;
    }


    /// <summary>
    /// Builds a deterministic whole-sphere base layout for a fixed probe count
    /// and known effective cap radius.
    ///
    /// The old arbitrary-N Fibonacci layout is a good uniform seed, but it is
    /// not optimized for the *actual* engineered footprint. This solver instead
    /// maximizes union coverage directly:
    ///
    /// 1. pin one probe at the visible front pole (easy centre shot);
    /// 2. greedily add the candidate with the largest new covered area;
    /// 3. run coordinate-swap refinement passes, replacing each non-centre
    ///    probe if another candidate increases total union coverage;
    /// 4. break equal-coverage ties by maximum minimum angular spacing.
    ///
    /// Because S^2 coverage is rotationally invariant, pinning the front pole
    /// does not conceptually reduce the solution space; it only chooses a useful
    /// orientation for the commander and guarantees a centre shot that can be
    /// fired last.
    /// </summary>
    public static IReadOnlyList<SphericalPoint>
        GenerateCoverageOptimizedLayout(
            int targetCount,
            double capAngularRadiusRadians)
    {
        int n =
            Math.Clamp(
                targetCount,
                DssSphericalPlacementPlanner.MinimumTargetCount,
                DssSphericalPlacementPlanner.MaximumTargetCount);

        if (!double.IsFinite(
                capAngularRadiusRadians)
            || capAngularRadiusRadians <= 0d)
        {
            return
                DssSphericalPlacementPlanner
                    .GenerateOptimalSphericalPoints(
                        n);
        }

        double cosCapRadius =
            Math.Cos(
                capAngularRadiusRadians);

        int wordCount =
            (SampleGrid.Length + 63)
            / 64;

        ulong[][] candidateMasks =
            BuildBasePlanCandidateCoverageMasks(
                wordCount,
                cosCapRadius);

        int frontPoleIndex =
            BasePlanCandidateGrid.Length - 2;

        var selected =
            new List<int>(
                n)
            {
                frontPoleIndex
            };

        var selectedFlags =
            new bool[
                BasePlanCandidateGrid.Length];

        selectedFlags[
            frontPoleIndex] =
            true;

        var covered =
            new ulong[
                wordCount];

        MergeCoverageMask(
            covered,
            candidateMasks[
                frontPoleIndex]);

        while (selected.Count < n)
        {
            int bestIndex =
                SelectBestBaseCandidate(
                    covered,
                    selected,
                    selectedFlags,
                    candidateMasks);

            if (bestIndex < 0)
            {
                break;
            }

            selected.Add(
                bestIndex);

            selectedFlags[
                bestIndex] =
                true;

            MergeCoverageMask(
                covered,
                candidateMasks[
                    bestIndex]);
        }

        // Coordinate-descent swap refinement. Keep slot 0 fixed at front
        // centre, but every other point may move to any unused candidate if it
        // improves the true union mask.
        for (int pass = 0;
             pass < BasePlanRefinementPasses;
             pass++)
        {
            bool improved =
                false;

            for (int slot = 1;
                 slot < selected.Count;
                 slot++)
            {
                int currentIndex =
                    selected[
                        slot];

                selectedFlags[
                    currentIndex] =
                    false;

                ulong[] baseCoverage =
                    BuildSelectedCoverageMask(
                        wordCount,
                        selected,
                        candidateMasks,
                        excludedSlot:
                            slot);

                int replacement =
                    SelectBestBaseCandidate(
                        baseCoverage,
                        selected,
                        selectedFlags,
                        candidateMasks,
                        excludedSlot:
                            slot);

                if (replacement < 0)
                {
                    selectedFlags[
                        currentIndex] =
                        true;
                    continue;
                }

                int currentTotal =
                    CountMergedMaskBits(
                        baseCoverage,
                        candidateMasks[
                            currentIndex]);

                int replacementTotal =
                    CountMergedMaskBits(
                        baseCoverage,
                        candidateMasks[
                            replacement]);

                bool accept =
                    replacementTotal
                    > currentTotal;

                if (!accept
                    && replacementTotal
                       == currentTotal)
                {
                    double currentSpacing =
                        MinimumAngularDistanceToSelected(
                            BasePlanCandidateGrid[
                                currentIndex],
                            selected,
                            excludedSlot:
                                slot);

                    double replacementSpacing =
                        MinimumAngularDistanceToSelected(
                            BasePlanCandidateGrid[
                                replacement],
                            selected,
                            excludedSlot:
                                slot);

                    accept =
                        replacementSpacing
                        > currentSpacing
                          + 1e-12d;
                }

                if (accept)
                {
                    selected[
                        slot] =
                        replacement;

                    selectedFlags[
                        replacement] =
                        true;

                    improved =
                        replacement
                        != currentIndex;
                }
                else
                {
                    selectedFlags[
                        currentIndex] =
                        true;
                }
            }

            if (!improved)
            {
                break;
            }
        }

        var result =
            new List<SphericalPoint>(
                selected.Count);

        for (int i = 0;
             i < selected.Count;
             i++)
        {
            result.Add(
                BasePlanCandidateGrid[
                    selected[i]]);
        }

        IReadOnlyList<SphericalPoint> legacy =
            DssSphericalPlacementPlanner
                .GenerateOptimalSphericalPoints(
                    n);

        // Extremely defensive fallback: the normal path always selects N
        // unique candidates, but never return a partial or numerically worse
        // live plan than the previous Fibonacci/polyhedral baseline.
        if (result.Count != n)
        {
            return legacy;
        }

        double optimizedCoverage =
            EvaluateUnionCoverage(
                result,
                capAngularRadiusRadians);

        double legacyCoverage =
            EvaluateUnionCoverage(
                legacy,
                capAngularRadiusRadians);

        if (optimizedCoverage
            + 1e-9d
            < legacyCoverage)
        {
            return legacy;
        }

        return result;
    }

    private static int SelectBestBaseCandidate(
        IReadOnlyList<ulong> covered,
        IReadOnlyList<int> selected,
        IReadOnlyList<bool> selectedFlags,
        IReadOnlyList<ulong[]> candidateMasks,
        int excludedSlot = -1)
    {
        int bestIndex = -1;
        int bestAddedCount = -1;
        double bestMinimumSpacing =
            double.NegativeInfinity;

        for (int candidateIndex = 0;
             candidateIndex
             < BasePlanCandidateGrid.Length;
             candidateIndex++)
        {
            if (selectedFlags[
                    candidateIndex])
            {
                continue;
            }

            int addedCount =
                CountNewMaskBits(
                    covered,
                    candidateMasks[
                        candidateIndex]);

            if (addedCount
                < bestAddedCount)
            {
                continue;
            }

            double minimumSpacing =
                MinimumAngularDistanceToSelected(
                    BasePlanCandidateGrid[
                        candidateIndex],
                    selected,
                    excludedSlot);

            if (addedCount
                    > bestAddedCount
                || (addedCount
                        == bestAddedCount
                    && minimumSpacing
                       > bestMinimumSpacing
                          + 1e-12d))
            {
                bestIndex =
                    candidateIndex;

                bestAddedCount =
                    addedCount;

                bestMinimumSpacing =
                    minimumSpacing;
            }
        }

        return bestIndex;
    }

    private static ulong[] BuildSelectedCoverageMask(
        int wordCount,
        IReadOnlyList<int> selected,
        IReadOnlyList<ulong[]> candidateMasks,
        int excludedSlot)
    {
        var result =
            new ulong[
                wordCount];

        for (int slot = 0;
             slot < selected.Count;
             slot++)
        {
            if (slot == excludedSlot)
            {
                continue;
            }

            MergeCoverageMask(
                result,
                candidateMasks[
                    selected[slot]]);
        }

        return result;
    }

    private static int CountMergedMaskBits(
        IReadOnlyList<ulong> baseMask,
        IReadOnlyList<ulong> candidateMask)
    {
        int count = 0;

        for (int word = 0;
             word < baseMask.Count;
             word++)
        {
            count +=
                BitOperations.PopCount(
                    baseMask[word]
                    | candidateMask[word]);
        }

        return count;
    }

    private static double MinimumAngularDistanceToSelected(
        SphericalPoint candidate,
        IReadOnlyList<int> selected,
        int excludedSlot)
    {
        if (selected.Count <= 1)
        {
            return Math.PI;
        }

        double minimum =
            Math.PI;

        for (int slot = 0;
             slot < selected.Count;
             slot++)
        {
            if (slot == excludedSlot)
            {
                continue;
            }

            SphericalPoint occupied =
                BasePlanCandidateGrid[
                    selected[slot]];

            double distance =
                Math.Acos(
                    Math.Clamp(
                        Dot(
                            candidate,
                            occupied),
                        -1d,
                        1d));

            minimum =
                Math.Min(
                    minimum,
                    distance);
        }

        return minimum;
    }

    private static ulong[][]
        BuildBasePlanCandidateCoverageMasks(
            int wordCount,
            double cosCapRadius)
    {
        var result =
            new ulong[
                BasePlanCandidateGrid.Length][];

        for (int candidateIndex = 0;
             candidateIndex
             < BasePlanCandidateGrid.Length;
             candidateIndex++)
        {
            var mask =
                new ulong[
                    wordCount];

            SphericalPoint candidate =
                BasePlanCandidateGrid[
                    candidateIndex];

            for (int sampleIndex = 0;
                 sampleIndex
                 < SampleGrid.Length;
                 sampleIndex++)
            {
                if (Dot(
                        SampleGrid[
                            sampleIndex],
                        candidate)
                    < cosCapRadius)
                {
                    continue;
                }

                SetMaskBit(
                    mask,
                    sampleIndex);
            }

            result[
                candidateIndex] =
                mask;
        }

        return result;
    }

    public static double SingleCapAreaFraction(
        double capAngularRadiusRadians) =>
        (1d
         - Math.Cos(
             Math.Clamp(
                 capAngularRadiusRadians,
                 0d,
                 Math.PI)))
        / 2d;

    private static bool IsCovered(
        SphericalPoint sample,
        IReadOnlyList<SphericalPoint> points,
        double cosCapRadius)
    {
        if (points is null)
        {
            return false;
        }

        for (int j = 0;
             j < points.Count;
             j++)
        {
            if (Dot(
                    sample,
                    points[j])
                >= cosCapRadius)
            {
                return true;
            }
        }

        return false;
    }

    private static void MarkPointsCoverageMask(
        ulong[] destination,
        IReadOnlyList<SphericalPoint> points,
        double cosCapRadius)
    {
        for (int sampleIndex = 0;
             sampleIndex
             < CorrectionSampleGrid.Length;
             sampleIndex++)
        {
            SphericalPoint sample =
                CorrectionSampleGrid[
                    sampleIndex];

            if (!IsCovered(
                    sample,
                    points,
                    cosCapRadius))
            {
                continue;
            }

            SetMaskBit(
                destination,
                sampleIndex);
        }
    }

    private static ulong[][] BuildCandidateCoverageMasks(
        int wordCount,
        double cosCapRadius)
    {
        var result =
            new ulong[
                CorrectionCandidateGrid.Length][];

        for (int candidateIndex = 0;
             candidateIndex
             < CorrectionCandidateGrid.Length;
             candidateIndex++)
        {
            var mask =
                new ulong[wordCount];

            SphericalPoint candidate =
                CorrectionCandidateGrid[
                    candidateIndex];

            for (int sampleIndex = 0;
                 sampleIndex
                 < CorrectionSampleGrid.Length;
                 sampleIndex++)
            {
                if (Dot(
                        CorrectionSampleGrid[
                            sampleIndex],
                        candidate)
                    < cosCapRadius)
                {
                    continue;
                }

                SetMaskBit(
                    mask,
                    sampleIndex);
            }

            result[candidateIndex] =
                mask;
        }

        return result;
    }

    private static int CountNewMaskBits(
        IReadOnlyList<ulong> covered,
        IReadOnlyList<ulong> candidate)
    {
        int count = 0;

        for (int word = 0;
             word < covered.Count;
             word++)
        {
            count +=
                BitOperations.PopCount(
                    candidate[word]
                    & ~covered[word]);
        }

        return count;
    }

    private static void MergeCoverageMask(
        ulong[] covered,
        IReadOnlyList<ulong> candidate)
    {
        for (int word = 0;
             word < covered.Length;
             word++)
        {
            covered[word] |=
                candidate[word];
        }
    }

    private static int CountMaskBits(
        IReadOnlyList<ulong> mask)
    {
        int count = 0;

        for (int word = 0;
             word < mask.Count;
             word++)
        {
            count +=
                BitOperations.PopCount(
                    mask[word]);
        }

        return count;
    }

    private static void SetMaskBit(
        ulong[] mask,
        int bitIndex)
    {
        int word =
            bitIndex >> 6;

        int offset =
            bitIndex & 63;

        mask[word] |=
            1UL << offset;
    }

    private static double MinimumAngularDistance(
        SphericalPoint candidate,
        IReadOnlyList<SphericalPoint> occupied)
    {
        if (occupied.Count == 0)
        {
            return Math.PI;
        }

        double minimum =
            Math.PI;

        for (int i = 0;
             i < occupied.Count;
             i++)
        {
            double distance =
                Math.Acos(
                    Math.Clamp(
                        Dot(
                            candidate,
                            occupied[i]),
                        -1d,
                        1d));

            minimum =
                Math.Min(
                    minimum,
                    distance);
        }

        return minimum;
    }

    private static double Dot(
        SphericalPoint a,
        SphericalPoint b) =>
        a.X * b.X
        + a.Y * b.Y
        + a.Z * b.Z;


    private static SphericalPoint[]
        GenerateBasePlanCandidateGrid()
    {
        SphericalPoint[] fibonacci =
            GenerateFibonacciGrid(
                BasePlanCandidateCount);

        var result =
            new SphericalPoint[
                fibonacci.Length + 2];

        Array.Copy(
            fibonacci,
            result,
            fibonacci.Length);

        // Stable orientation anchors. The optimizer pins +Z/front centre and
        // may independently select -Z/rear antipode when it is useful.
        result[^2] =
            new SphericalPoint(
                0d,
                0d);

        result[^1] =
            new SphericalPoint(
                Math.PI,
                0d);

        return result;
    }

    private static SphericalPoint[]
        GenerateCorrectionCandidateGrid()
    {
        SphericalPoint[] fibonacci =
            GenerateFibonacciGrid(
                CorrectionCandidateCount);

        var result =
            new SphericalPoint[
                fibonacci.Length + 2];

        Array.Copy(
            fibonacci,
            result,
            fibonacci.Length);

        result[^2] =
            new SphericalPoint(
                0d,
                0d);

        result[^1] =
            new SphericalPoint(
                Math.PI,
                0d);

        return result;
    }

    private static SphericalPoint[] GenerateFibonacciGrid(
        int sampleCount)
    {
        var grid =
            new SphericalPoint[sampleCount];

        double goldenRatio =
            (1d + Math.Sqrt(5d))
            / 2d;

        for (int i = 0;
             i < sampleCount;
             i++)
        {
            double z =
                1d
                - (2d * i + 1d)
                  / sampleCount;

            double theta =
                Math.Acos(
                    Math.Clamp(
                        z,
                        -1d,
                        1d));

            double phi =
                2d * Math.PI * i
                / goldenRatio;

            grid[i] =
                new SphericalPoint(
                    theta,
                    phi);
        }

        return grid;
    }
}
