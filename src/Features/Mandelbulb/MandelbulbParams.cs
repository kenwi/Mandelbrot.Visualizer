using System.Numerics;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

public enum BulbShape
{
    Mandelbulb = 0,
    Mandelbox = 1,
}

public enum BulbQuality
{
    Fast = 0,
    Balanced = 1,
    High = 2,
}

/// <summary>
/// Immutable parameter set for 3D SDF raymarched fractals (Mandelbulb & Mandelbox),
/// including optional PBR material, headlamp, depth fog, quality tier, and thin-lens DoF.
/// </summary>
public readonly record struct MandelbulbParams(
    double Power,
    int Iterations,
    double Detail,
    double AoStrength,
    bool SoftShadows,
    bool Julia,
    Vector3 JuliaC,
    BulbShape Shape = BulbShape.Mandelbulb,
    double BoxScale = 2.0,
    double BoxMinRadius = 0.5,
    double BoxFoldLimit = 1.0,
    BulbQuality Quality = BulbQuality.Balanced,
    double LightElevation = 35.0,
    bool Headlamp = false,
    double FogDensity = 0.0,
    double Roughness = 0.42,
    double Metallic = 0.12,
    double GlowStrength = 0.0,
    bool DofEnabled = false,
    double DofAperture = 0.04,
    bool DofAutoFocus = true,
    double DofFocusDistance = 2.0)
{
    public const double PowerMin = 2.0;
    public const double PowerMax = 16.0;
    public const int IterationMin = 4;
    public const int IterationMax = 24;
    public const double DetailMin = 0.4;
    public const double DetailMax = 3.0;
    public const double BoxScaleMin = -3.5;
    public const double BoxScaleMax = 3.5;
    public const double BoxMinRadiusMin = 0.10;
    public const double BoxMinRadiusMax = 1.00;
    public const double BoxFoldLimitMin = 0.50;
    public const double BoxFoldLimitMax = 2.00;
    public const double LightElevationMin = -60.0;
    public const double LightElevationMax = 85.0;
    public const double RoughnessMin = 0.06;
    public const double RoughnessMax = 1.0;
    public const double DofApertureMin = 0.005;
    public const double DofApertureMax = 0.25;
    public const double DofFocusMin = 0.05;
    public const double DofFocusMax = 36.0;

    public static MandelbulbParams Default => new(
        Power: 8.0,
        Iterations: 10,
        Detail: 1.0,
        AoStrength: 0.78,
        SoftShadows: true,
        Julia: false,
        JuliaC: new Vector3(-0.40f, 0.60f, 0.20f),
        Shape: BulbShape.Mandelbulb,
        BoxScale: 2.0,
        BoxMinRadius: 0.5,
        BoxFoldLimit: 1.0,
        Quality: BulbQuality.Balanced,
        LightElevation: 35.0,
        Headlamp: false,
        FogDensity: 0.0,
        Roughness: 0.42,
        Metallic: 0.12,
        GlowStrength: 0.0,
        DofEnabled: false,
        DofAperture: 0.04,
        DofAutoFocus: true,
        DofFocusDistance: 2.0);

    public static MandelbulbParams DefaultMandelbox => Default with
    {
        Shape = BulbShape.Mandelbox,
        Iterations = 14,
        BoxScale = 2.0,
        BoxMinRadius = 0.5,
        BoxFoldLimit = 1.0,
        JuliaC = new Vector3(0.0f, -1.5f, 1.5f),
    };

    public float BoundingRadius
    {
        get
        {
            if (Shape == BulbShape.Mandelbulb)
                return MandelbulbMath.BulbBoundingRadius;

            double absScale = Math.Abs(BoxScale);
            double denom = Math.Max(absScale - 1.0, 0.45);
            double halfExtent = Math.Clamp(2.0 * BoxFoldLimit * (absScale + 1.0) / denom, 4.0, 12.5);
            return (float)Math.Clamp(halfExtent * 1.78, 7.0, 22.0);
        }
    }

    public string Subtitle => Shape == BulbShape.Mandelbox
        ? (Julia ? $"3D Julia Box  s = {BoxScale:0.##}" : $"3D Mandelbox  s = {BoxScale:0.##}")
        : (Julia ? $"3D Julia Bulb  n = {Power:0.##}" : $"3D Mandelbulb  n = {Power:0.##}");

    public string CaptureStem => Shape == BulbShape.Mandelbox
        ? (Julia ? $"mandelbox-julia-s{BoxScale:0.##}" : $"mandelbox-s{BoxScale:0.##}")
        : (Julia ? $"mandelbulb-julia-n{Power:0.##}" : $"mandelbulb-n{Power:0.##}");

    public static double ClampPower(double value)
        => double.IsFinite(value) ? Math.Clamp(value, PowerMin, PowerMax) : 8.0;

    public static int ClampIterations(int value)
        => Math.Clamp(value, IterationMin, IterationMax);

    public static double ClampBoxScale(double value)
    {
        if (!double.IsFinite(value)) return 2.0;
        double clamped = Math.Clamp(value, BoxScaleMin, BoxScaleMax);
        if (Math.Abs(clamped) < 0.25)
            return clamped < 0 ? -0.25 : 0.25;
        return clamped;
    }

    public static double ClampBoxMinRadius(double value)
        => double.IsFinite(value) ? Math.Clamp(value, BoxMinRadiusMin, BoxMinRadiusMax) : 0.5;

    public static double ClampBoxFoldLimit(double value)
        => double.IsFinite(value) ? Math.Clamp(value, BoxFoldLimitMin, BoxFoldLimitMax) : 1.0;

    public static double ClampLightElevation(double value)
        => double.IsFinite(value) ? Math.Clamp(value, LightElevationMin, LightElevationMax) : 35.0;

    public static double ClampUnit(double value, double fallback = 0.0)
        => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : fallback;

    public static double ClampRoughness(double value)
        => double.IsFinite(value) ? Math.Clamp(value, RoughnessMin, RoughnessMax) : 0.42;

    public static double ClampDofAperture(double value)
        => double.IsFinite(value) ? Math.Clamp(value, DofApertureMin, DofApertureMax) : 0.04;

    public static double ClampDofFocus(double value)
        => double.IsFinite(value) ? Math.Clamp(value, DofFocusMin, DofFocusMax) : 2.0;
}

/// <summary>
/// CPU reference Distance Estimators matching Shaders/mandelbulb.frag (White/Nylander
/// Mandelbulb and Tom Lowe Mandelbox) for auto-SDF flight speed, Fly->Orbit probing,
/// DoF auto-focus, and headless --self-test verification.
/// </summary>
public static class MandelbulbMath
{
    public const float BulbBoundingRadius = 1.55f;

    public static float EstimateDistance(Vector3 pos, in MandelbulbParams p)
        => EstimateDistance(pos, p, out _);

    public static float EstimateDistance(Vector3 pos, in MandelbulbParams p, out float orbitTrap)
    {
        if (p.Shape == BulbShape.Mandelbox)
            return EstimateMandelbox(pos, p, out orbitTrap);

        return EstimateMandelbulb(pos, p, out orbitTrap);
    }

    /// <summary>
    /// Sphere-traces a ray to find the surface distance (used for Fly->Orbit pivoting and DoF auto-focus).
    /// </summary>
    public static float ProbeRayDistance(Vector3 origin, Vector3 forward, in MandelbulbParams p)
    {
        float boundR = p.BoundingRadius;
        float maxProbe = MathF.Max(6.0f, boundR * 2.6f);
        float t = 0.002f;
        float stepScale = p.Shape == BulbShape.Mandelbox ? 0.80f : 0.85f;
        float hitEps = p.Shape == BulbShape.Mandelbox ? 1.5e-3f : 5e-4f;

        for (int i = 0; i < 120; i++)
        {
            float d = EstimateDistance(origin + forward * t, p);
            if (d < hitEps)
                return Math.Clamp(t, (float)MandelbulbParams.DofFocusMin, (float)MandelbulbParams.DofFocusMax);
            t += MathF.Max(d * stepScale, 0.0015f);
            if (t > maxProbe)
                break;
        }

        float toOrigin = origin.Length();
        float fallback = p.Shape == BulbShape.Mandelbox ? 10.0f : 2.0f;
        return Math.Clamp(toOrigin > 0.2f && toOrigin < maxProbe ? toOrigin : fallback, 0.25f, maxProbe);
    }

    /// <summary>
    /// Generates a low-discrepancy golden-angle Vogel disk sample inside the unit circle for DoF bokeh.
    /// </summary>
    public static Vector2 SampleVogelDisk(int index, int count)
    {
        if (count <= 1)
            return Vector2.Zero;

        const float goldenAngle = 2.39996323f; // pi * (3 - sqrt(5))
        float r = MathF.Sqrt((index + 0.5f) / count);
        float theta = index * goldenAngle;
        return new Vector2(r * MathF.Cos(theta), r * MathF.Sin(theta));
    }

    static float EstimateMandelbulb(Vector3 pos, in MandelbulbParams p, out float orbitTrap)
    {
        Vector3 z = pos;
        Vector3 c = p.Julia ? p.JuliaC : pos;
        double power = p.Power;
        double dr = 1.0;
        double r = 0.0;
        double trapMin = 1e10;
        double trapAxis = 1e10;

        for (int i = 0; i < p.Iterations; i++)
        {
            r = Math.Sqrt((double)z.X * z.X + (double)z.Y * z.Y + (double)z.Z * z.Z);
            if (r > 2.5)
                break;

            if (r < trapMin) trapMin = r;
            double ax = Math.Min(Math.Abs(z.X), Math.Min(Math.Abs(z.Y), Math.Abs(z.Z)));
            if (ax < trapAxis) trapAxis = ax;

            double safeR = Math.Max(r, 1e-8);
            double theta = Math.Acos(Math.Clamp(z.Y / safeR, -1.0, 1.0));
            double phi = Math.Atan2(z.Z, z.X);
            dr = Math.Pow(safeR, power - 1.0) * power * dr + 1.0;

            double zr = Math.Pow(safeR, power);
            theta *= power;
            phi *= power;

            double sinT = Math.Sin(theta);
            z = new Vector3(
                (float)(zr * sinT * Math.Cos(phi)) + c.X,
                (float)(zr * Math.Cos(theta)) + c.Y,
                (float)(zr * sinT * Math.Sin(phi)) + c.Z);
        }

        double safeFinalR = Math.Max(r, 1e-8);
        orbitTrap = (float)(trapMin * 0.65 + trapAxis * 1.4 * 0.35);
        return (float)(0.5 * Math.Log(safeFinalR) * safeFinalR / Math.Max(dr, 1e-8));
    }

    static float EstimateMandelbox(Vector3 pos, in MandelbulbParams p, out float orbitTrap)
    {
        double zx = pos.X, zy = pos.Y, zz = pos.Z;
        double cx = p.Julia ? p.JuliaC.X : pos.X;
        double cy = p.Julia ? p.JuliaC.Y : pos.Y;
        double cz = p.Julia ? p.JuliaC.Z : pos.Z;

        double scale = p.BoxScale;
        double absScale = Math.Max(Math.Abs(scale), 0.1);
        double foldLimit = p.BoxFoldLimit;
        double minR2 = Math.Max(p.BoxMinRadius * p.BoxMinRadius, 0.01);
        double fixedR2 = Math.Max(1.0, minR2 + 0.01);

        double dr = 1.0;
        double trapMin = 1e10;
        double trapAxis = 1e10;

        for (int i = 0; i < p.Iterations; i++)
        {
            zx = Math.Clamp(zx, -foldLimit, foldLimit) * 2.0 - zx;
            zy = Math.Clamp(zy, -foldLimit, foldLimit) * 2.0 - zy;
            zz = Math.Clamp(zz, -foldLimit, foldLimit) * 2.0 - zz;

            double r2 = zx * zx + zy * zy + zz * zz;
            if (r2 < trapMin) trapMin = r2;
            double ax = Math.Min(Math.Abs(zx), Math.Min(Math.Abs(zy), Math.Abs(zz)));
            if (ax < trapAxis) trapAxis = ax;

            if (r2 < minR2)
            {
                double temp = fixedR2 / minR2;
                zx *= temp; zy *= temp; zz *= temp;
                dr *= temp;
            }
            else if (r2 < fixedR2)
            {
                double temp = fixedR2 / r2;
                zx *= temp; zy *= temp; zz *= temp;
                dr *= temp;
            }

            zx = zx * scale + cx;
            zy = zy * scale + cy;
            zz = zz * scale + cz;
            dr = dr * absScale + 1.0;

            if (zx * zx + zy * zy + zz * zz > 1e6)
                break;
        }

        double len = Math.Sqrt(zx * zx + zy * zy + zz * zz);
        orbitTrap = (float)(Math.Sqrt(trapMin) * 0.55 * 0.65 + trapAxis * 0.85 * 0.35);
        return (float)(len / Math.Max(Math.Abs(dr), 1e-8));
    }

    public static Vector3 EstimateNormal(Vector3 pos, in MandelbulbParams p, float eps = 1e-4f)
    {
        Vector3 e1 = new(eps, -eps, -eps);
        Vector3 e2 = new(-eps, -eps, eps);
        Vector3 e3 = new(-eps, eps, -eps);
        Vector3 e4 = new(eps, eps, eps);

        Vector3 n = e1 * EstimateDistance(pos + e1, p, out _)
                  + e2 * EstimateDistance(pos + e2, p, out _)
                  + e3 * EstimateDistance(pos + e3, p, out _)
                  + e4 * EstimateDistance(pos + e4, p, out _);

        float len = n.Length();
        return len > 1e-12f ? n / len : Vector3.UnitY;
    }
}
