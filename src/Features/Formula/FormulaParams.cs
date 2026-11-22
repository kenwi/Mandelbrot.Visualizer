using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Immutable parameters defining the z = z^n + c orbit and iteration limit.
/// Single source of truth for power clamping and integer-exponent classification.
/// </summary>
public readonly record struct FormulaParams(
    double Power,
    int MaxIterations,
    bool Julia,
    double JuliaRe,
    double JuliaIm)
{
    public const double PowerMin = -6;
    public const double PowerMax = 12;
    public const int IterationMin = 1;
    public const int IterationMax = 100_000;
    public const double MinNonZeroPower = 1e-4;
    public const int IntegerExponentMin = -16;
    public const int IntegerExponentMax = 16;
    public const double IntegerExponentTolerance = 1e-8;

    public static FormulaParams Default => new(2, 250, false, -0.8, 0.156);

    public static double ClampNonZeroPower(double value, double? direction = null)
    {
        if (Math.Abs(value) >= MinNonZeroPower)
            return value;

        bool positive = direction.HasValue ? direction.Value >= 0 : value >= 0;
        return positive ? MinNonZeroPower : -MinNonZeroPower;
    }

    public static int ClampIterations(int value)
        => Math.Clamp(value, IterationMin, IterationMax);

    public static int AutoIterationsForScale(FloatExp scale, double referenceScale = 1.35)
    {
        if (scale.IsZero) return IterationMax;
        double logZoom = Math.Max(0.0, Math.Log10(referenceScale) - scale.Log10());
        int computed = 250 + (int)(180.0 * Math.Pow(logZoom, 1.45));
        return ClampIterations(computed);
    }

    public static bool TryGetIntegerExponent(double power, out int exponent)
    {
        if (double.IsFinite(power))
        {
            double rounded = Math.Round(power);
            if (Math.Abs(power - rounded) < IntegerExponentTolerance
                && rounded is >= IntegerExponentMin and <= IntegerExponentMax)
            {
                exponent = (int)rounded;
                return true;
            }
        }

        exponent = 0;
        return false;
    }

    public static bool TryGetPositiveSimdExponent(double power, out int exponent)
        => TryGetIntegerExponent(power, out exponent) && exponent >= 1;

    public static bool SupportsPerturbation(double power, out int exponent)
        => TryGetIntegerExponent(power, out exponent) && exponent >= 2;
}
