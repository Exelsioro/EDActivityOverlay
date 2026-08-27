using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Projection between unit-sphere impact coordinates (theta, phi) and
/// normalized DSS screen aim offsets (K, phi).
///
/// Calibrated facts currently used:
/// - theta = 0 (front sub-observer point) maps to K = 0.
/// - theta = pi/2 (visible horizon) maps to K = 1.
/// - the native MISS boundary K_miss(thetaScreen) comes from the clean v23 sweeps.
/// - according to the Efficient Planetary Mapping firing-pattern guide, the
///   circle halfway between the visible horizon and MISS corresponds to the
///   exact rear antipode. Therefore theta = pi maps to
///   K_rear = (1 + K_miss) / 2.
///
/// The exact non-linear trajectory between horizon and antipode is not yet
/// measured from live landing points. Until that calibration exists, the
/// far-side branch uses the least-assumptive linear interpolation between
/// those two supported endpoints. Base spherical plans intentionally do not
/// use the outer half of the extended impact annulus (K > K_rear).
/// </summary>
public static class DssSphericalProjection
{
    public const double MinimumAngularDiameterDegrees = 21d;
    public const double MaximumAngularDiameterDegrees = 28d;
    public const double SafetyMarginNormalized = 0.05d;

    // Linear fit to clean pre-shot v23 native-MISS boundary sweeps:
    //   21.39 deg -> 1.7402 Rh
    //   22.48 deg -> 1.7414 Rh
    //   23.21 deg -> 1.7326 Rh
    //   24.22 deg -> 1.7225 Rh
    private const double BoundaryIntercept = 1.88392783d;
    private const double BoundarySlope = -0.00656091d;

    public static bool IsWithinCalibration(double angularDiameterDegrees) =>
        double.IsFinite(angularDiameterDegrees)
        && angularDiameterDegrees >= MinimumAngularDiameterDegrees
        && angularDiameterDegrees <= MaximumAngularDiameterDegrees;

    public static double EstimateBoundaryNormalizedRadius(
        double angularDiameterDegrees)
    {
        double clamped = Math.Clamp(
            angularDiameterDegrees,
            MinimumAngularDiameterDegrees,
            MaximumAngularDiameterDegrees);

        return BoundaryIntercept + BoundarySlope * clamped;
    }

    /// <summary>
    /// Conservative outer feasibility bound. This remains useful for research
    /// and correction shots, but the base spherical plan no longer maps the
    /// rear antipode to this near-MISS radius.
    /// </summary>
    public static double EstimateSafeNormalizedRadius(
        double angularDiameterDegrees) =>
        EstimateBoundaryNormalizedRadius(angularDiameterDegrees)
        - SafetyMarginNormalized;

    /// <summary>
    /// Aim radius that lands at the point exactly opposite the observer.
    /// Source-derived constraint from the supplied firing-pattern guide:
    /// rear antipode lies halfway between horizon K=1 and native MISS.
    /// </summary>
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
    /// Maps surface polar angle theta in [0, pi] to DSS aim radius K=r/Rh.
    ///
    /// Front hemisphere:
    ///   theta=0     -> K=0
    ///   theta=pi/2  -> K=1 (visible horizon)
    ///
    /// Far hemisphere:
    ///   theta=pi/2  -> K=1
    ///   theta=pi    -> K=(1+K_miss)/2 (rear antipode)
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
    /// Inverse of the currently modelled branch. K values beyond the rear
    /// antipode circle belong to the guide's outer extended-impact region and
    /// are trajectory-ambiguous without additional live calibration; they are
    /// conservatively clamped to the rear antipode here.
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

        double clampedK =
            Math.Min(k, rearRadius);

        double farRatio =
            Math.Clamp(
                (clampedK - 1d)
                / Math.Max(
                    1e-6d,
                    rearRadius - 1d),
                0d,
                1d);

        return
            Math.PI / 2d
            + Math.PI / 2d
              * farRatio;
    }

    public static (
        double NormalizedX,
        double NormalizedY,
        double AimRadiusNormalized)
        ProjectSphericalToScreenAim(
            SphericalPoint point,
            double angularDiameterDegrees)
    {
        double k =
            ProjectSurfacePolarAngleToDssAim(
                point.Theta,
                angularDiameterDegrees);

        double nx =
            k * Math.Cos(point.Phi);

        double ny =
            k * Math.Sin(point.Phi);

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

        double phi =
            Math.Atan2(
                normalizedY,
                normalizedX);

        double theta =
            ProjectDssAimToSurfacePolarAngle(
                k,
                angularDiameterDegrees);

        return
            new SphericalPoint(
                theta,
                phi);
    }
}
