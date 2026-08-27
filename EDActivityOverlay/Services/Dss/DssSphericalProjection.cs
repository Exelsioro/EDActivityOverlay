using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Calibrated projection between planetary unit-sphere impact coordinates (theta, phi)
/// and normalized DSS screen reticle aim offsets (K, phi).
///
/// Rotational symmetry:
///   Due to axial symmetry of the line of sight and planetary gravity, screen azimuth
///   corresponds directly to surface impact azimuth (phi_screen == phi_sphere).
///
/// Curved trajectory model:
///   Probes follow gravity-deflected trajectories. Visible hemisphere impacts (theta <= pi/2)
///   map to K in [0, 1.0]. Far hemisphere impacts (theta > pi/2) require aiming beyond the limb
///   (K in (1.0, K_safe]), where K_safe is bounded by the empirical native MISS boundary.
/// </summary>
public static class DssSphericalProjection
{
    public const double MinimumAngularDiameterDegrees = 21d;
    public const double MaximumAngularDiameterDegrees = 28d;
    public const double SafetyMarginNormalized = 0.05d;

    // Linear fit to clean pre-shot v23 MISS boundary sweeps:
    //   Kboundary(theta) ~= Intercept + Slope * theta
    private const double BoundaryIntercept = 1.88392783d;
    private const double BoundarySlope = -0.00656091d;

    // Empirical curvature power for gravitational far-side deflection:
    private const double CurvatureGamma = 0.94d;

    /// <summary>
    /// Checks whether the angular diameter is within calibrated range.
    /// </summary>
    public static bool IsWithinCalibration(double angularDiameterDegrees) =>
        double.IsFinite(angularDiameterDegrees)
        && angularDiameterDegrees >= MinimumAngularDiameterDegrees
        && angularDiameterDegrees <= MaximumAngularDiameterDegrees;

    /// <summary>
    /// Estimates the native HUD MISS boundary radius K_boundary = r_miss / R_h.
    /// </summary>
    public static double EstimateBoundaryNormalizedRadius(double angularDiameterDegrees)
    {
        double clamped = Math.Clamp(
            angularDiameterDegrees,
            MinimumAngularDiameterDegrees,
            MaximumAngularDiameterDegrees);

        return BoundaryIntercept + BoundarySlope * clamped;
    }

    /// <summary>
    /// Estimates the maximum safe aim radius K_safe = K_boundary - margin.
    /// Hard feasibility constraint: any far shot must not exceed this radius.
    /// </summary>
    public static double EstimateSafeNormalizedRadius(double angularDiameterDegrees) =>
        EstimateBoundaryNormalizedRadius(angularDiameterDegrees) - SafetyMarginNormalized;

    /// <summary>
    /// Maps surface polar angle theta in [0, pi] (radians) to normalized DSS aim radius K = r_aim / R_h.
    /// theta = 0 (sub-observer center) -> K = 0
    /// theta = pi/2 (horizon limb) -> K = 1.0
    /// theta = pi (rear antipode) -> K = K_safe
    /// </summary>
    public static double ProjectSurfacePolarAngleToDssAim(
        double thetaRadians,
        double angularDiameterDegrees)
    {
        double clampedTheta = Math.Clamp(thetaRadians, 0d, Math.PI);

        if (clampedTheta <= Math.PI / 2d)
        {
            // Visible front disc: orthogonal projection sin(theta).
            return Math.Sin(clampedTheta);
        }

        // Far hemisphere: gravity deflection past the limb.
        double safeRadius = EstimateSafeNormalizedRadius(angularDiameterDegrees);
        double farRatio = (clampedTheta - Math.PI / 2d) / (Math.PI / 2d);
        double deflected = 1.0d + (safeRadius - 1.0d) * Math.Pow(farRatio, CurvatureGamma);

        return Math.Min(deflected, safeRadius);
    }

    /// <summary>
    /// Inverse mapping: maps normalized DSS aim radius K = r_aim / R_h to surface polar angle theta (radians).
    /// </summary>
    public static double ProjectDssAimToSurfacePolarAngle(
        double aimRadiusNormalized,
        double angularDiameterDegrees)
    {
        double k = Math.Max(0d, aimRadiusNormalized);

        if (k <= 1.0d)
        {
            return Math.Asin(k);
        }

        double safeRadius = EstimateSafeNormalizedRadius(angularDiameterDegrees);
        double clampedK = Math.Min(k, safeRadius);
        double farRatio = Math.Clamp((clampedK - 1.0d) / Math.Max(1e-6d, safeRadius - 1.0d), 0d, 1d);

        double theta = Math.PI / 2d + (Math.PI / 2d) * Math.Pow(farRatio, 1d / CurvatureGamma);
        return Math.Clamp(theta, Math.PI / 2d, Math.PI);
    }

    /// <summary>
    /// Converts a spherical surface point (theta, phi) to normalized screen aim coordinates (Nx, Ny).
    /// Preserves rotational symmetry: phi_screen == phi_sphere.
    /// </summary>
    public static (double NormalizedX, double NormalizedY, double AimRadiusNormalized) ProjectSphericalToScreenAim(
        SphericalPoint point,
        double angularDiameterDegrees)
    {
        double k = ProjectSurfacePolarAngleToDssAim(point.Theta, angularDiameterDegrees);
        double nx = k * Math.Cos(point.Phi);
        double ny = k * Math.Sin(point.Phi);
        return (nx, ny, k);
    }

    /// <summary>
    /// Converts normalized screen aim coordinates (Nx, Ny) to a spherical surface impact point (theta, phi).
    /// </summary>
    public static SphericalPoint ProjectScreenAimToSpherical(
        double normalizedX,
        double normalizedY,
        double angularDiameterDegrees)
    {
        double k = Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
        double phi = Math.Atan2(normalizedY, normalizedX);
        double theta = ProjectDssAimToSurfacePolarAngle(k, angularDiameterDegrees);
        return new SphericalPoint(theta, phi);
    }
}

