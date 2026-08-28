using System;
using System.Collections.Generic;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Point on the unit sphere S^2.
/// Coordinates:
///   Theta (colatitude/polar angle): 0 at front sub-observer pole (facing player),
///                                  pi/2 at visible horizon limb,
///                                  pi at rear antipode.
///   Phi (azimuth): angle around line of sight in radians [-pi, pi].
///                  Matches screen azimuth: 0 = +X (right), pi/2 = +Y (down).
///   X, Y, Z: Cartesian unit vector where Z is towards player (sub-observer axis).
/// </summary>
public readonly record struct SphericalPoint
{
    public double Theta { get; }
    public double Phi { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public SphericalPoint(double theta, double phi)
    {
        Theta = Math.Clamp(theta, 0d, Math.PI);
        Phi = NormalizeAngle(phi);

        double sinTheta = Math.Sin(Theta);
        X = sinTheta * Math.Cos(Phi);
        Y = sinTheta * Math.Sin(Phi);
        Z = Math.Cos(Theta);
    }

    public static SphericalPoint FromDegrees(double thetaDegrees, double phiDegrees) =>
        new(thetaDegrees * Math.PI / 180d, phiDegrees * Math.PI / 180d);

    public static SphericalPoint FromCartesian(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length < 1e-9d)
        {
            return new SphericalPoint(0, 0);
        }

        double unitX = x / length;
        double unitY = y / length;
        double unitZ = Math.Clamp(z / length, -1d, 1d);

        double theta = Math.Acos(unitZ);
        double phi = Math.Atan2(unitY, unitX);
        return new SphericalPoint(theta, phi);
    }

    public double AngularDistanceTo(SphericalPoint other)
    {
        double dot = X * other.X + Y * other.Y + Z * other.Z;
        return Math.Acos(Math.Clamp(dot, -1d, 1d));
    }

    public double AngularDistanceToDegrees(SphericalPoint other) =>
        AngularDistanceTo(other) * 180d / Math.PI;

    private static double NormalizeAngle(double radians)
    {
        while (radians > Math.PI) radians -= 2d * Math.PI;
        while (radians < -Math.PI) radians += 2d * Math.PI;
        return radians;
    }

    public override string ToString() =>
        $"SphericalPoint(θ={Theta * 180d / Math.PI:0.0}°, φ={Phi * 180d / Math.PI:0.0}°)";
}

/// <summary>
/// Known efficient polyhedral configurations on the unit sphere for validation and ground truth.
/// </summary>
public static class PolyhedralValidationCatalog
{
    /// <summary>
    /// N=4 Regular Tetrahedron.
    /// 1 point at front center (theta=0), 3 points at theta = arccos(-1/3) ~ 109.4712 deg.
    /// All pairwise distances equal arccos(-1/3) ~ 109.4712 deg.
    /// </summary>
    public static IReadOnlyList<SphericalPoint> GetTetrahedron()
    {
        double theta = Math.Acos(-1d / 3d); // ~109.4712 degrees
        return new[]
        {
            new SphericalPoint(0, 0),
            new SphericalPoint(theta, 0),
            new SphericalPoint(theta, 2d * Math.PI / 3d),
            new SphericalPoint(theta, 4d * Math.PI / 3d)
        };
    }

    /// <summary>
    /// N=6 Regular Octahedron.
    /// 1 front pole (theta=0), 1 rear pole (theta=pi), 4 equatorial points (theta=pi/2, phi=0, 90, 180, 270 deg).
    /// All nearest-neighbor distances equal 90 deg (pi/2 rad).
    /// </summary>
    public static IReadOnlyList<SphericalPoint> GetOctahedron()
    {
        double halfPi = Math.PI / 2d;
        return new[]
        {
            new SphericalPoint(0, 0),
            new SphericalPoint(halfPi, 0),
            new SphericalPoint(halfPi, halfPi),
            new SphericalPoint(halfPi, Math.PI),
            new SphericalPoint(halfPi, -halfPi),
            new SphericalPoint(Math.PI, 0)
        };
    }

    /// <summary>
    /// N=8 Cube / Hexahedron.
    /// 4 points at theta = arccos(1/sqrt(3)) ~ 54.7356 deg (phi = 45, 135, 225, 315 deg),
    /// 4 points at theta = pi - 54.7356 deg ~ 125.2644 deg (phi = 45, 135, 225, 315 deg).
    /// </summary>
    public static IReadOnlyList<SphericalPoint> GetCube()
    {
        double thetaNear = Math.Acos(1d / Math.Sqrt(3d)); // ~54.7356 deg
        double thetaFar = Math.PI - thetaNear;            // ~125.2644 deg
        double quarterPi = Math.PI / 4d;

        return new[]
        {
            new SphericalPoint(thetaNear, quarterPi),
            new SphericalPoint(thetaNear, 3d * quarterPi),
            new SphericalPoint(thetaNear, -3d * quarterPi),
            new SphericalPoint(thetaNear, -quarterPi),
            new SphericalPoint(thetaFar, quarterPi),
            new SphericalPoint(thetaFar, 3d * quarterPi),
            new SphericalPoint(thetaFar, -3d * quarterPi),
            new SphericalPoint(thetaFar, -quarterPi)
        };
    }

    /// <summary>
    /// N=12 Regular Icosahedron.
    /// 1 front pole (theta=0),
    /// 5 vertices at theta = arctan(2) ~ 63.4349 deg (phi = 0, 72, 144, 216, 288 deg),
    /// 5 vertices at theta = pi - arctan(2) ~ 116.5651 deg (phi = 36, 108, 180, 252, 324 deg),
    /// 1 rear pole (theta=pi).
    /// All nearest-neighbor distances equal arccos(1/sqrt(5)) ~ 63.4349 deg.
    /// </summary>
    public static IReadOnlyList<SphericalPoint> GetIcosahedron()
    {
        double thetaNear = Math.Atan(2d);       // ~63.4349 deg
        double thetaFar = Math.PI - thetaNear;  // ~116.5651 deg
        double step72 = 2d * Math.PI / 5d;
        double offset36 = Math.PI / 5d;

        var points = new List<SphericalPoint>(12)
        {
            new(0, 0)
        };

        for (int i = 0; i < 5; i++)
        {
            points.Add(new SphericalPoint(thetaNear, i * step72));
        }

        for (int i = 0; i < 5; i++)
        {
            points.Add(new SphericalPoint(thetaFar, offset36 + i * step72));
        }

        points.Add(new SphericalPoint(Math.PI, 0));
        return points;
    }
}

