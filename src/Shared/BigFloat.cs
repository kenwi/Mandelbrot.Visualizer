using System.Globalization;
using System.Numerics;

namespace Mandelbrot.Visualizer.Shared;

/// <summary>
/// Pure managed arbitrary-precision binary floating-point number (Mantissa * 2^Exponent)
/// used for deep-zoom viewport coordinates and reference orbits.
/// </summary>
public readonly struct BigFloat : IEquatable<BigFloat>
{
    public static readonly BigFloat Zero = new(BigInteger.Zero, 0);

    public BigInteger Mantissa { get; }
    public int Exponent { get; }
    public bool IsZero => Mantissa.IsZero;
    public int Sign => Mantissa.Sign;

    BigFloat(BigInteger mantissa, int exponent)
    {
        Mantissa = mantissa;
        Exponent = mantissa.IsZero ? 0 : exponent;
    }

    public static int RequiredBits(FloatExp scale)
        => Math.Max(96, Math.Max(0, -scale.Exponent) + 96);

    public static BigFloat FromDouble(double value)
    {
        if (value == 0.0 || !double.IsFinite(value))
            return Zero;

        long bits = BitConverter.DoubleToInt64Bits(value);
        bool neg = bits < 0;
        int biasedExp = (int)((bits >> 52) & 0x7FF);
        long frac = bits & 0x000F_FFFF_FFFF_FFFFL;

        int exp;
        long sig;
        if (biasedExp == 0)
        {
            exp = -1074;
            sig = frac;
        }
        else
        {
            exp = biasedExp - 1023 - 52;
            sig = (1L << 52) | frac;
        }

        return new BigFloat(neg ? -new BigInteger(sig) : new BigInteger(sig), exp);
    }

    public static BigFloat FromFloatExp(FloatExp value)
    {
        if (value.IsZero) return Zero;
        BigFloat baseVal = FromDouble(value.Mantissa);
        return new BigFloat(baseVal.Mantissa, baseVal.Exponent + value.Exponent);
    }

    public FloatExp ToFloatExp()
    {
        if (IsZero) return FloatExp.Zero;

        int sign = Mantissa.Sign;
        BigInteger abs = sign < 0 ? -Mantissa : Mantissa;
        int bitLen = (int)abs.GetBitLength();
        int shift = bitLen - 53;
        ulong top = shift > 0 ? (ulong)(abs >> shift) : (ulong)abs << (-shift);
        double mantissa = top * (1.0 / (1UL << 53));
        return FloatExp.Create(sign < 0 ? -mantissa : mantissa, Exponent + bitLen);
    }

    public double ToDouble() => ToFloatExp().ToDouble();

    public BigFloat Negate() => new(-Mantissa, Exponent);

    public BigFloat Round(int precisionBits)
        => Normalize(Mantissa, Exponent, precisionBits);

    public static BigFloat Add(BigFloat a, BigFloat b, int precisionBits)
    {
        if (a.IsZero) return b.Round(precisionBits);
        if (b.IsZero) return a.Round(precisionBits);

        int aBits = BitLengthAbs(a.Mantissa);
        int bBits = BitLengthAbs(b.Mantissa);
        int aTop = a.Exponent + aBits;
        int bTop = b.Exponent + bBits;
        int maxTop = Math.Max(aTop, bTop);

        if (aTop - bTop > precisionBits + 16) return a.Round(precisionBits);
        if (bTop - aTop > precisionBits + 16) return b.Round(precisionBits);

        int targetExp = Math.Max(Math.Min(a.Exponent, b.Exponent), maxTop - precisionBits - 16);
        BigInteger am = ShiftToExponent(a.Mantissa, a.Exponent, targetExp);
        BigInteger bm = ShiftToExponent(b.Mantissa, b.Exponent, targetExp);
        return Normalize(am + bm, targetExp, precisionBits);
    }

    public static BigFloat Sub(BigFloat a, BigFloat b, int precisionBits)
        => Add(a, b.Negate(), precisionBits);

    public static BigFloat Mul(BigFloat a, BigFloat b, int precisionBits)
    {
        if (a.IsZero || b.IsZero) return Zero;
        return Normalize(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent, precisionBits);
    }

    public static BigFloat Square(BigFloat a, int precisionBits)
    {
        if (a.IsZero) return Zero;
        return Normalize(a.Mantissa * a.Mantissa, a.Exponent * 2, precisionBits);
    }

    public static BigFloat MulPow2(BigFloat a, int shift)
        => a.IsZero ? Zero : new BigFloat(a.Mantissa, a.Exponent + shift);

    public static bool TryParseDecimal(string text, int precisionBits, out BigFloat result)
    {
        result = Zero;
        text = text.Trim();
        if (text.Length == 0) return false;

        bool negative = false;
        int pos = 0;
        if (text[0] == '-' || text[0] == '+')
        {
            negative = text[0] == '-';
            pos = 1;
        }

        int eIdx = text.IndexOfAny(['e', 'E'], pos);
        string mantStr = eIdx >= 0 ? text[pos..eIdx] : text[pos..];
        int exp10 = 0;
        if (eIdx >= 0 && !int.TryParse(text[(eIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out exp10))
            return false;

        int dot = mantStr.IndexOf('.');
        string digits = dot >= 0 ? mantStr[..dot] + mantStr[(dot + 1)..] : mantStr;
        if (digits.Length == 0) return false;

        int fracDigits = dot >= 0 ? mantStr.Length - dot - 1 : 0;
        int totalExp10 = exp10 - fracDigits;

        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger integer))
            return false;

        if (integer.IsZero)
        {
            result = Zero;
            return true;
        }

        if (totalExp10 >= 0)
        {
            BigInteger scaled = integer * BigInteger.Pow(10, totalExp10);
            result = Normalize(negative ? -scaled : scaled, 0, precisionBits);
            return true;
        }

        BigInteger denom = BigInteger.Pow(10, -totalExp10);
        int shift = precisionBits + 32 + (int)denom.GetBitLength() - (int)integer.GetBitLength();
        if (shift < 0) shift = 0;
        BigInteger quotient = (integer << shift) / denom;
        result = Normalize(negative ? -quotient : quotient, -shift, precisionBits);
        return true;
    }

    public string FormatSummary()
    {
        double approx = ToDouble();
        return approx.ToString("0.####", CultureInfo.InvariantCulture);
    }

    public bool Equals(BigFloat other)
    {
        if (IsZero && other.IsZero) return true;
        if (IsZero != other.IsZero || Sign != other.Sign) return false;
        if (Exponent == other.Exponent) return Mantissa == other.Mantissa;

        if (Exponent > other.Exponent)
            return (Mantissa << (Exponent - other.Exponent)) == other.Mantissa;
        return Mantissa == (other.Mantissa << (other.Exponent - Exponent));
    }

    public override bool Equals(object? obj) => obj is BigFloat other && Equals(other);

    public override int GetHashCode()
    {
        if (IsZero) return 0;
        FloatExp fe = ToFloatExp();
        return HashCode.Combine(fe.Mantissa, fe.Exponent);
    }

    public static bool operator ==(BigFloat a, BigFloat b) => a.Equals(b);
    public static bool operator !=(BigFloat a, BigFloat b) => !a.Equals(b);

    static BigFloat Normalize(BigInteger mantissa, int exponent, int precisionBits)
    {
        if (mantissa.IsZero) return Zero;

        int bitLen = BitLengthAbs(mantissa);
        int excess = bitLen - precisionBits;
        if (excess > 0)
        {
            mantissa >>= excess;
            exponent += excess;
        }

        return new BigFloat(mantissa, exponent);
    }

    static BigInteger ShiftToExponent(BigInteger mantissa, int currentExp, int targetExp)
    {
        int shift = currentExp - targetExp;
        if (shift > 0) return mantissa << shift;
        if (shift < 0) return mantissa >> (-shift);
        return mantissa;
    }

    static int BitLengthAbs(BigInteger value)
        => (int)(value.Sign < 0 ? -value : value).GetBitLength();
}
