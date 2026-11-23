using System.Runtime.CompilerServices;

namespace Mandelbrot.Visualizer.Shared;

/// <summary>
/// Extended-exponent floating-point number (Mantissa * 2^Exponent) that never underflows
/// at double (1e-308) or float (1e-38) limits. Normalized so |Mantissa| is in [0.5, 1.0) or 0.
/// </summary>
public readonly record struct FloatExp : IComparable<FloatExp>
{
    public const double Log10Of2 = 0.30102999566398119521;

    public static readonly FloatExp Zero = new(0.0, 0);
    public static readonly FloatExp One = new(0.5, 1);

    public double Mantissa { get; }
    public int Exponent { get; }
    public bool IsZero => Mantissa == 0.0;

    FloatExp(double mantissa, int exponent)
    {
        Mantissa = mantissa;
        Exponent = exponent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatExp Create(double mantissa, int exponent)
    {
        if (mantissa == 0.0 || !double.IsFinite(mantissa))
            return Zero;

        int k = Math.ILogB(mantissa) + 1;
        return new FloatExp(Math.ScaleB(mantissa, -k), exponent + k);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatExp FromDouble(double value) => Create(value, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble()
    {
        if (Mantissa == 0.0) return 0.0;
        if (Exponent > 1024) return Mantissa > 0 ? double.PositiveInfinity : double.NegativeInfinity;
        if (Exponent < -1075) return 0.0;
        return Math.ScaleB(Mantissa, Exponent);
    }

    public double Log10()
        => Mantissa == 0.0 ? double.NegativeInfinity : Math.Log10(Math.Abs(Mantissa)) + Exponent * Log10Of2;

    public FloatExp Abs() => new(Math.Abs(Mantissa), Exponent);

    public static FloatExp operator -(FloatExp value) => new(-value.Mantissa, value.Exponent);

    public static FloatExp operator *(FloatExp a, FloatExp b)
        => a.IsZero || b.IsZero ? Zero : Create(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent);

    public static FloatExp operator *(FloatExp a, double b)
        => a.IsZero || b == 0.0 ? Zero : Create(a.Mantissa * b, a.Exponent);

    public static FloatExp operator /(FloatExp a, FloatExp b)
        => a.IsZero ? Zero : Create(a.Mantissa / b.Mantissa, a.Exponent - b.Exponent);

    public static FloatExp operator /(FloatExp a, double b)
        => a.IsZero ? Zero : Create(a.Mantissa / b, a.Exponent);

    public static FloatExp operator +(FloatExp a, FloatExp b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        if (a.Exponent < b.Exponent) (a, b) = (b, a);

        int diff = a.Exponent - b.Exponent;
        if (diff > 54) return a;
        return Create(a.Mantissa + Math.ScaleB(b.Mantissa, -diff), a.Exponent);
    }

    public static FloatExp operator -(FloatExp a, FloatExp b) => a + (-b);

    public int CompareTo(FloatExp other)
    {
        int sa = Math.Sign(Mantissa);
        int sb = Math.Sign(other.Mantissa);
        if (sa != sb) return sa.CompareTo(sb);
        if (sa == 0) return 0;

        int expCmp = Exponent.CompareTo(other.Exponent);
        if (expCmp != 0) return sa > 0 ? expCmp : -expCmp;
        return Mantissa.CompareTo(other.Mantissa);
    }

    public static bool operator <(FloatExp a, FloatExp b) => a.CompareTo(b) < 0;
    public static bool operator >(FloatExp a, FloatExp b) => a.CompareTo(b) > 0;
    public static bool operator <=(FloatExp a, FloatExp b) => a.CompareTo(b) <= 0;
    public static bool operator >=(FloatExp a, FloatExp b) => a.CompareTo(b) >= 0;

    public static FloatExp Clamp(FloatExp value, FloatExp min, FloatExp max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static bool TryParse(string text, out FloatExp value)
    {
        value = Zero;
        text = text.Trim();
        if (text.Length == 0) return false;

        int eIdx = text.IndexOfAny(['e', 'E']);
        if (eIdx < 0)
        {
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
                || !double.IsFinite(d))
                return false;
            value = FromDouble(d);
            return true;
        }

        if (!double.TryParse(text[..eIdx], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mant)
            || !double.IsFinite(mant)
            || !int.TryParse(text[(eIdx + 1)..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int exp10))
            return false;

        if (mant == 0.0)
        {
            value = Zero;
            return true;
        }

        // Convert mant * 10^exp10 to binary FloatExp without overflowing double.
        double log2 = exp10 * (1.0 / Log10Of2);
        int exp2 = (int)Math.Floor(log2);
        double frac2 = Math.Pow(2.0, log2 - exp2);
        value = Create(mant * frac2, exp2);
        return true;
    }

    public string ToScientificString()
    {
        if (IsZero) return "0";

        bool negative = Mantissa < 0.0;
        double log10 = Log10();
        int exp10 = (int)Math.Floor(log10);
        double mant10 = Math.Pow(10.0, log10 - exp10);
        if (mant10 >= 9.9999995)
        {
            mant10 = 1.0;
            exp10++;
        }

        string sign = negative ? "-" : "";
        if (exp10 is >= -3 and <= 4)
        {
            double val = Math.Pow(10.0, log10);
            return sign + val.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        }

        string mantStr = mant10.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
        return $"{sign}{mantStr}e{exp10}";
    }

    public string FormatZoom(double referenceScale)
    {
        if (IsZero) return "inf";
        double logZoom = Math.Log10(referenceScale) - Log10();
        if (logZoom < 6.0)
        {
            double zoom = Math.Pow(10.0, logZoom);
            return $"{zoom:0.##}x";
        }

        int exp10 = (int)Math.Floor(logZoom);
        double mant10 = Math.Pow(10.0, logZoom - exp10);
        return $"{mant10:0.00}e+{exp10}x";
    }
}

/// <summary>
/// Shared-exponent complex number (Re + i*Im) * 2^Exp for deep perturbation arithmetic
/// below IEEE-754 double underflow (1e-308).
/// </summary>
public readonly record struct ComplexExp
{
    public static readonly ComplexExp Zero = new(0.0, 0.0, 0);

    public double Re { get; }
    public double Im { get; }
    public int Exp { get; }
    public bool IsZero => Re == 0.0 && Im == 0.0;

    ComplexExp(double re, double im, int exp)
    {
        Re = re;
        Im = im;
        Exp = exp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComplexExp Normalize(double re, double im, int exp)
    {
        double max = Math.Max(Math.Abs(re), Math.Abs(im));
        if (max == 0.0 || !double.IsFinite(max))
            return Zero;

        int k = Math.ILogB(max) + 1;
        return new ComplexExp(Math.ScaleB(re, -k), Math.ScaleB(im, -k), exp + k);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComplexExp FromDouble(double re, double im) => Normalize(re, im, 0);

    public static ComplexExp FromFloatExp(FloatExp re, FloatExp im)
    {
        if (re.IsZero && im.IsZero) return Zero;
        if (re.IsZero) return Normalize(0.0, im.Mantissa, im.Exponent);
        if (im.IsZero) return Normalize(re.Mantissa, 0.0, re.Exponent);

        int maxExp = Math.Max(re.Exponent, im.Exponent);
        int dr = maxExp - re.Exponent;
        int di = maxExp - im.Exponent;
        double r = dr > 54 ? 0.0 : Math.ScaleB(re.Mantissa, -dr);
        double i = di > 54 ? 0.0 : Math.ScaleB(im.Mantissa, -di);
        return Normalize(r, i, maxExp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComplexExp ScalePow2(int shift) => IsZero ? Zero : new ComplexExp(Re, Im, Exp + shift);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FloatExp NormSquared()
        => IsZero ? FloatExp.Zero : FloatExp.Create(Re * Re + Im * Im, Exp * 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ToDouble(out double re, out double im)
    {
        if (IsZero || Exp < -1075)
        {
            re = 0.0;
            im = 0.0;
            return;
        }

        re = Math.ScaleB(Re, Exp);
        im = Math.ScaleB(Im, Exp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComplexExp Add(ComplexExp a, ComplexExp b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        if (a.Exp < b.Exp) (a, b) = (b, a);

        int diff = a.Exp - b.Exp;
        if (diff > 54) return a;
        return Normalize(a.Re + Math.ScaleB(b.Re, -diff), a.Im + Math.ScaleB(b.Im, -diff), a.Exp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComplexExp AddDouble(ComplexExp a, double re, double im)
        => Add(a, Normalize(re, im, 0));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComplexExp Mul(ComplexExp a, ComplexExp b)
    {
        if (a.IsZero || b.IsZero) return Zero;
        return Normalize(
            a.Re * b.Re - a.Im * b.Im,
            a.Re * b.Im + a.Im * b.Re,
            a.Exp + b.Exp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComplexExp MulDouble(ComplexExp a, double re, double im)
    {
        if (a.IsZero || (re == 0.0 && im == 0.0)) return Zero;
        return Normalize(
            a.Re * re - a.Im * im,
            a.Re * im + a.Im * re,
            a.Exp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComplexExp Square(ComplexExp a)
    {
        if (a.IsZero) return Zero;
        return Normalize(a.Re * a.Re - a.Im * a.Im, 2.0 * a.Re * a.Im, a.Exp * 2);
    }
}
