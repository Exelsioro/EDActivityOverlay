using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Projection between unit-sphere impact coordinates (theta, phi) and
/// normalized DSS screen aim offsets (K, phi).
///
/// Calibrated facts:
/// - theta = 0 (front sub-observer point) maps to K = 0.
/// - theta = pi/2 (visible horizon) maps to K = 1.
/// - native MISS boundary K_miss comes from the clean v23 sweeps at
///   21..28 deg; v54 linearly extrapolates the same fit to 36 deg because a
///   real Ready N21 body was observed around 30.9 deg.
/// - the supplied Efficient Planetary Mapping guide places the rear antipode
///   halfway between horizon and MISS:
///
///       K_rear = (1 + K_miss) / 2.
///
/// The extended impact annulus therefore has two rear-hemisphere branches:
///
///   inner: horizon K=1      -> antipode K=K_rear
///   outer: MISS K=K_miss    -> antipode K=K_rear
///
/// For the same rear surface point the outer branch is aimed on the opposite
/// screen azimuth. v51 used only the inner branch. Live validation showed that
/// its rear targets at K~1.14..1.21 left the actual rear hemisphere
/// under-covered, while the successful manual finishing shot was K=1.474 on
/// almost the exact opposite azimuth of the planned deepest rear point.
///
/// v52 therefore preferred the outer branch for each rear point independently.
/// Two independent N21/N17 live runs then exposed a reproducible radial hole:
/// most rear aims clustered either near K~1.0 or K~1.45..1.65, while the
/// K~1.15..1.30 annulus remained under-served and manual shots there produced
/// large native coverage gains.
///
/// v56 keeps the automatic method for compatibility, but the live placement
/// planner now explicitly mixes safe inner and outer trajectories across the
/// whole rear batch.
/// </summary>
public static class DssSphericalProjection
{
    public const double MinimumAngularDiameterDegrees = 21d;

    // 21..28 deg is the range directly measured by the original MISS sweeps.
    // The N21 gas-giant research run produced valid/Ready DSS geometry around
    // 30.9 deg, proving 28 deg was only our calibration limit, not Elite's.
    public const double EmpiricalCalibrationMaximumAngularDiameterDegrees = 28d;

    // Operational headroom for controlled linear extrapolation. Values above
    // this are clamped rather than allowed to drive the MISS fit arbitrarily.
    public const double MaximumAngularDiameterDegrees = 36d;
    public const double SafetyMarginNormalized = 0.05d;

    private const double BoundaryIntercept = 1.88392783d;
    private const double BoundarySlope = -0.00656091d;

    public static bool IsWithinCalibration(double angularDiameterDegrees) =>
        double.IsFinite(angularDiameterDegrees)
        && angularDiameterDegrees >= MinimumAngularDiameterDegrees
        && angularDiameterDegrees <= EmpiricalCalibrationMaximumAngularDiameterDegrees;

    public static bool IsWithinOperationalRange(double angularDiameterDegrees) =>
        double.IsFinite(angularDiameterDegrees)
        && angularDiameterDegrees >= MinimumAngularDiameterDegrees
        && angularDiameterDegrees <= MaximumAngularDiameterDegrees;

    public static bool UsesExtrapolatedBoundary(double angularDiameterDegrees) =>
        double.IsFinite(angularDiameterDegrees)
        && angularDiameterDegrees > EmpiricalCalibrationMaximumAngularDiameterDegrees
        && angularDiameterDegrees <= MaximumAngularDiameterDegrees;

    public static double EstimateBoundaryNormalizedRadius(
        double angularDiameterDegrees)
    {
        // Preserve the measured linear fit. For 28..36 deg this is explicitly
        // an extrapolation, not a claim of new empirical calibration.
        double clamped = Math.Clamp(
            angularDiameterDegrees,
            MinimumAngularDiameterDegrees,
            MaximumAngularDiameterDegrees);

        return BoundaryIntercept + BoundarySlope * clamped;
    }

    public static double EstimateSafeNormalizedRadius(
        double angularDiameterDegrees) =>
        EstimateBoundaryNormalizedRadius(angularDiameterDegrees)
        - SafetyMarginNormalized;

    public static double EstimateRearAntipodeNormalizedRadius(
        double angularDiameterDegrees)
    {
        double missRadius =
            EstimateBoundaryNormalizedRadius(angularDiameterDegrees);

        return
            1d
            + (missRadius - 1d) * 0.5d;
    }

    /// <summary>
    /// Inner rear branch:
    /// theta=pi/2 -> K=1
    /// theta=pi   -> K=K_rear
    /// </summary>
    public static double ProjectSurfacePolarAngleToDssAim(
        double thetaRadians,
        double angularDiameterDegrees)
    {
        double theta =
            Math.Clamp(thetaRadians, 0d, Math.PI);

        if (theta <= Math.PI / 2d)
        {
            return Math.Sin(theta);
        }

        double rearRadius =
            EstimateRearAntipodeNormalizedRadius(
                angularDiameterDegrees);

        double farRatio =
            (theta - Math.PI / 2d)
            / (Math.PI / 2d);

        return
            1d
            + (rearRadius - 1d)
              * farRatio;
    }

    /// <summary>
    /// Outer rear branch:
    /// theta=pi/2 -> K=K_miss
    /// theta=pi   -> K=K_rear
    ///
    /// The screen azimuth for this branch is surfacePhi + pi.
    /// </summary>
    public static double ProjectSurfacePolarAngleToOuterDssAim(
        double thetaRadians,
        double angularDiameterDegrees)
    {
        double theta =
            Math.Clamp(
                thetaRadians,
                Math.PI / 2d,
                Math.PI);

        double missRadius =
            EstimateBoundaryNormalizedRadius(
                angularDiameterDegrees);

        double rearRadius =
            EstimateRearAntipodeNormalizedRadius(
                angularDiameterDegrees);

        double farRatio =
            (theta - Math.PI / 2d)
            / (Math.PI / 2d);

        return
            missRadius
            - (missRadius - rearRadius)
              * farRatio;
    }

    internal static bool ShouldUseOuterFarBranch(
        double thetaRadians,
        double angularDiameterDegrees)
    {
        if (!double.IsFinite(thetaRadians)
            || thetaRadians <= Math.PI / 2d)
        {
            return false;
        }

        double outerRadius =
            ProjectSurfacePolarAngleToOuterDssAim(
                thetaRadians,
                angularDiameterDegrees);

        return
            outerRadius
            <= EstimateSafeNormalizedRadius(
                angularDiameterDegrees);
    }

    /// <summary>
    /// Inverse polar mapping. K in (1, K_rear] is the inner rear branch.
    /// K in (K_rear, K_miss) is the outer rear branch.
    /// </summary>
    public static double ProjectDssAimToSurfacePolarAngle(
        double aimRadiusNormalized,
        double angularDiameterDegrees)
    {
        double k =
            Math.Max(0d, aimRadiusNormalized);

        if (k <= 1d)
        {
            return Math.Asin(
                Math.Clamp(k, 0d, 1d));
        }

        double rearRadius =
            EstimateRearAntipodeNormalizedRadius(
                angularDiameterDegrees);

        if (k <= rearRadius)
        {
            double innerRatio =
                Math.Clamp(
                    (k - 1d)
                    / Math.Max(
                        1e-6d,
                        rearRadius - 1d),
                    0d,
                    1d);

            return
                Math.PI / 2d
                + Math.PI / 2d
                  * innerRatio;
        }

        double missRadius =
            EstimateBoundaryNormalizedRadius(
                angularDiameterDegrees);

        double clampedK =
            Math.Min(
                k,
                missRadius);

        double outerRatio =
            Math.Clamp(
                (missRadius - clampedK)
                / Math.Max(
                    1e-6d,
                    missRadius - rearRadius),
                0d,
                1d);

        return
            Math.PI / 2d
            + Math.PI / 2d
              * outerRatio;
    }

    public static (
        double NormalizedX,
        double NormalizedY,
        double AimRadiusNormalized)
        ProjectSphericalToScreenAim(
            SphericalPoint point,
            double angularDiameterDegrees)
    {
        return
            ProjectSphericalToScreenAim(
                point,
                angularDiameterDegrees,
                ShouldUseOuterFarBranch(
                    point.Theta,
                    angularDiameterDegrees));
    }

    /// <summary>
    /// Explicit rear-trajectory projection.
    ///
    /// v52's automatic projection remains available above for compatibility.
    /// The live planner uses this overload so branch selection can be optimized
    /// across the whole rear batch instead of making an independent
    /// "outer whenever safe" decision for every rear point.
    /// </summary>
    internal static (
        double NormalizedX,
        double NormalizedY,
        double AimRadiusNormalized)
        ProjectSphericalToScreenAim(
            SphericalPoint point,
            double angularDiameterDegrees,
            bool useOuterRearBranch)
    {
        double aimPhi =
            point.Phi;

        bool useOuter =
            useOuterRearBranch
            && ShouldUseOuterFarBranch(
                point.Theta,
                angularDiameterDegrees);

        double k;

        if (useOuter)
        {
            k =
                ProjectSurfacePolarAngleToOuterDssAim(
                    point.Theta,
                    angularDiameterDegrees);

            aimPhi += Math.PI;
        }
        else
        {
            k =
                ProjectSurfacePolarAngleToDssAim(
                    point.Theta,
                    angularDiameterDegrees);
        }

        double nx =
            k * Math.Cos(aimPhi);

        double ny =
            k * Math.Sin(aimPhi);

        return (nx, ny, k);
    }

    public static SphericalPoint ProjectScreenAimToSpherical(
        double normalizedX,
        double normalizedY,
        double angularDiameterDegrees)
    {
        double k =
            Math.Sqrt(
                normalizedX * normalizedX
                + normalizedY * normalizedY);

        double aimPhi =
            Math.Atan2(
                normalizedY,
                normalizedX);

        double theta =
            ProjectDssAimToSurfacePolarAngle(
                k,
                angularDiameterDegrees);

        double rearRadius =
            EstimateRearAntipodeNormalizedRadius(
                angularDiameterDegrees);

        double surfacePhi =
            k > rearRadius
                ? NormalizeRadians(
                    aimPhi + Math.PI)
                : NormalizeRadians(
                    aimPhi);

        return
            new SphericalPoint(
                theta,
                surfacePhi);
    }

    private static double NormalizeRadians(
        double radians)
    {
        double twoPi =
            Math.PI * 2d;

        double normalized =
            radians % twoPi;

        if (normalized <= -Math.PI)
        {
            normalized += twoPi;
        }
        else if (normalized > Math.PI)
        {
            normalized -= twoPi;
        }

        return normalized;
    }
}
