using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Formula;

public enum FractalFamily
{
    Multibrot = 0,
    BurningShip = 1,
    Tricorn = 2,
    Celtic = 3,
    Buffalo = 4,
}

/// <summary>
/// Immutable parameters defining the fractal family, z = f(z, n) + c orbit, and iteration limit.
/// Single source of truth for power clamping, default framing, and integer-exponent classification.
/// </summary>
public readonly record struct FormulaParams(
    double Power,
    int MaxIterations,
    bool Julia,
    double JuliaRe,
    double JuliaIm,
    FractalFamily Family = FractalFamily.Multibrot)
{
    public const string FamilyComboItems = "Multibrot;Burning Ship;Tricorn;Celtic;Buffalo";
    public const int FamilyCount = 5;
    public const double PowerMin = -6;
    public const double PowerMax = 12;
    public const int IterationMin = 1;
    public const int IterationMax = 100_000;
    public const double MinNonZeroPower = 1e-4;
    public const int IntegerExponentMin = -16;
    public const int IntegerExponentMax = 16;
    public const double IntegerExponentTolerance = 1e-8;

    public static FormulaParams Default => new(2, 250, false, -0.8, 0.156, FractalFamily.Multibrot);

    public string FormulaSubtitle => Family switch
    {
        FractalFamily.BurningShip => "z = (|Re z| - i|Im z|)^n + c",
        FractalFamily.Tricorn => "z = conj(z)^n + c",
        FractalFamily.Celtic => "z = |Re(z^n)| + i Im(z^n) + c",
        FractalFamily.Buffalo => "z = |Re(z^n)| - i|Im(z^n)| + c",
        _ => "z  =  z^n  +  c",
    };

    public bool IsSymmetricAcrossRealAxis => Family is FractalFamily.Multibrot or FractalFamily.Tricorn or FractalFamily.Celtic;

    public void GetDefaultView(out double centerX, out double centerY, out double scale)
    {
        if (Julia)
        {
            centerX = 0.0;
            centerY = 0.0;
            scale = 1.6;
            return;
        }

        (centerX, centerY, scale) = Family switch
        {
            FractalFamily.BurningShip => (-0.45, 0.50, 1.65),
            FractalFamily.Tricorn => (-0.25, 0.0, 1.85),
            FractalFamily.Celtic => (-0.45, 0.0, 1.75),
            FractalFamily.Buffalo => (-0.35, 0.35, 1.65),
            _ => (-0.5, 0.0, 1.35),
        };
    }

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

    public static bool SupportsPerturbation(in FormulaParams formula, out int exponent)
    {
        if (formula.Family != FractalFamily.Multibrot)
        {
            exponent = 0;
            return false;
        }

        return SupportsPerturbation(formula.Power, out exponent);
    }
}
