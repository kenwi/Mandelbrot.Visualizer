using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Evaluates neighboring pixels together with AVX2 / AVX-512 doubles.
/// Whole-number powers from 1 to 16 use the vector loop; other powers stay on the scalar path.
/// </summary>
public static class SimdMultibrot
{
    const double Bailout2 = MultibrotFormula.Bailout * MultibrotFormula.Bailout;

    [ThreadStatic] static double[]? _zx;
    [ThreadStatic] static double[]? _zy;
    [ThreadStatic] static double[]? _cx;
    [ThreadStatic] static double[]? _cy;
    [ThreadStatic] static double[]? _minR;

    public static bool IsSupported => Avx.IsSupported;

    public static void EvaluateRow(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        in FormulaParams formula,
        float[] mu,
        float[] minRadius)
    {
        if (!IsSupported || !FormulaParams.TryGetPositiveSimdExponent(formula.Power, out int exponent) || formula.MaxIterations < 1)
        {
            MultibrotFormula.EvaluateScalarRow(width, height, y, originX, originY, halfW, halfH, formula, mu, minRadius);
            return;
        }

        Ensure(width);
        if (Avx512F.IsSupported && Avx512DQ.IsSupported)
            Dispatch<Vector512<double>, Simd512>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius);
        else
            Dispatch<Vector256<double>, Simd256>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius);
    }

    static void Dispatch<TVec, TOps>(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        int exponent,
        in FormulaParams formula,
        float[] mu,
        float[] minRadius)
        where TVec : struct
        where TOps : ISimdDouble<TVec>
    {
        switch (exponent)
        {
            case 2:
                EvaluateVectorized<TVec, TOps, SquarePower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius);
                break;
            case 3:
                EvaluateVectorized<TVec, TOps, CubePower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius);
                break;
            case 4:
                EvaluateVectorized<TVec, TOps, FourthPower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius);
                break;
            default:
                EvaluateVectorized<TVec, TOps, IntegerPower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void EvaluateVectorized<TVec, TOps, TPower>(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        int exponent,
        in FormulaParams formula,
        float[] mu,
        float[] minRadius)
        where TVec : struct
        where TOps : ISimdDouble<TVec>
        where TPower : IPowerStep<TVec>
    {
        double[] zxBuf = _zx!;
        double[] zyBuf = _zy!;
        double[] cxBuf = _cx!;
        double[] cyBuf = _cy!;
        double[] minBuf = _minR!;

        double rowIm = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
        int row = y * width;
        for (int x = 0; x < width; x++)
        {
            double re = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
            if (formula.Julia)
            {
                zxBuf[x] = re;
                zyBuf[x] = rowIm;
                cxBuf[x] = formula.JuliaRe;
                cyBuf[x] = formula.JuliaIm;
            }
            else
            {
                zxBuf[x] = 0;
                zyBuf[x] = 0;
                cxBuf[x] = re;
                cyBuf[x] = rowIm;
            }

            minBuf[x] = double.MaxValue;
        }

        TVec bailout = TOps.Create(Bailout2);
        TVec two = TOps.Create(2.0);
        int maxIterations = formula.MaxIterations;
        double power = exponent;
        int lanes = TOps.LaneCount;

        int p = 0;
        for (; p + lanes <= width; p += lanes)
        {
            TVec zx = TOps.Load(ref zxBuf[p]);
            TVec zy = TOps.Load(ref zyBuf[p]);
            TVec cx = TOps.Load(ref cxBuf[p]);
            TVec cy = TOps.Load(ref cyBuf[p]);
            TVec minR = TOps.Load(ref minBuf[p]);
            TVec notDone = TOps.AllBitsSet;
            TVec escapeR = TOps.Zero;
            TVec escapeI = TOps.Zero;
            TVec hard = TOps.Zero;

            int block = 0;
            while (block < maxIterations && TOps.ExtractMsb(notDone) != 0)
            {
                int stop = Math.Min(block + 8, maxIterations);
                for (int i = block; i < stop; i++)
                {
                    TVec x2 = TOps.Mul(zx, zx);
                    TVec y2 = TOps.Mul(zy, zy);
                    TVec r2 = TOps.Add(x2, y2);
                    TVec over = TOps.GreaterThan(r2, bailout);
                    minR = TOps.Blend(minR, TOps.Min(minR, r2), notDone);
                    TVec just = TOps.And(over, notDone);
                    TVec at = TOps.Create(i);
                    escapeR = TOps.Blend(escapeR, r2, just);
                    escapeI = TOps.Blend(escapeI, at, just);
                    notDone = TOps.AndNot(over, notDone);
                    TPower.Step(ref zx, ref zy, cx, cy, x2, y2, two, exponent);
                    if (TPower.CheckFinite)
                        StopBroken<TVec, TOps>(ref zx, ref zy, ref notDone, ref escapeI, ref hard, at);
                }

                block = stop;
            }

            ulong alive = TOps.ExtractMsb(notDone);
            ulong broken = TPower.CheckFinite ? TOps.ExtractMsb(hard) : 0;
            for (int lane = 0; lane < lanes; lane++)
            {
                Commit(
                    row + p + lane,
                    (alive & (1UL << lane)) != 0,
                    (broken & (1UL << lane)) != 0,
                    (int)TOps.GetElement(escapeI, lane),
                    TOps.GetElement(escapeR, lane),
                    TOps.GetElement(minR, lane),
                    power,
                    mu,
                    minRadius);
            }
        }

        for (; p < width; p++)
        {
            var escape = MultibrotFormula.Evaluate(zxBuf[p], zyBuf[p], cxBuf[p], cyBuf[p], power, maxIterations);
            mu[row + p] = escape.Mu;
            minRadius[row + p] = escape.MinRadius;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void StopBroken<TVec, TOps>(
        ref TVec zx,
        ref TVec zy,
        ref TVec notDone,
        ref TVec escapeI,
        ref TVec hard,
        TVec iter)
        where TVec : struct
        where TOps : ISimdDouble<TVec>
    {
        TVec bad = TOps.IsNotFinite(zx);
        TVec justBad = TOps.And(bad, notDone);
        escapeI = TOps.Blend(escapeI, iter, justBad);
        hard = TOps.Or(hard, justBad);
        notDone = TOps.AndNot(bad, notDone);
        zx = TOps.And(zx, notDone);
        zy = TOps.And(zy, notDone);
    }

    static void Commit(int pixel, bool alive, bool hard, int iteration, double r2, double minR, double power, float[] mu, float[] minRadius)
    {
        var sample = alive
            ? MultibrotFormula.Inside(minR)
            : hard
                ? MultibrotFormula.Stopped(iteration, minR)
                : MultibrotFormula.Escaped(iteration, r2, power, minR);
        mu[pixel] = sample.Mu;
        minRadius[pixel] = sample.MinRadius;
    }

    static void Ensure(int width)
    {
        if (_zx != null && _zx.Length >= width)
            return;

        _zx = new double[width];
        _zy = new double[width];
        _cx = new double[width];
        _cy = new double[width];
        _minR = new double[width];
    }

    interface ISimdDouble<TVec> where TVec : struct
    {
        static abstract int LaneCount { get; }
        static abstract TVec Zero { get; }
        static abstract TVec AllBitsSet { get; }
        static abstract TVec Create(double value);
        static abstract TVec Load(ref double source);
        static abstract double GetElement(TVec value, int index);
        static abstract ulong ExtractMsb(TVec value);
        static abstract TVec Add(TVec a, TVec b);
        static abstract TVec Sub(TVec a, TVec b);
        static abstract TVec Mul(TVec a, TVec b);
        static abstract TVec Min(TVec a, TVec b);
        static abstract TVec GreaterThan(TVec a, TVec b);
        static abstract TVec Blend(TVec a, TVec b, TVec mask);
        static abstract TVec And(TVec a, TVec b);
        static abstract TVec AndNot(TVec a, TVec b);
        static abstract TVec Or(TVec a, TVec b);
        static abstract TVec IsNotFinite(TVec value);
    }

    readonly struct Simd256 : ISimdDouble<Vector256<double>>
    {
        public static int LaneCount => 4;
        public static Vector256<double> Zero => Vector256<double>.Zero;
        public static Vector256<double> AllBitsSet => Vector256<double>.AllBitsSet;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Create(double value) => Vector256.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Load(ref double source) => Vector256.LoadUnsafe(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetElement(Vector256<double> value, int index) => value.GetElement(index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ExtractMsb(Vector256<double> value) => value.ExtractMostSignificantBits();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Add(Vector256<double> a, Vector256<double> b) => Avx.Add(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Sub(Vector256<double> a, Vector256<double> b) => Avx.Subtract(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Mul(Vector256<double> a, Vector256<double> b) => Avx.Multiply(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Min(Vector256<double> a, Vector256<double> b) => Avx.Min(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> GreaterThan(Vector256<double> a, Vector256<double> b) => Avx.CompareGreaterThan(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Blend(Vector256<double> a, Vector256<double> b, Vector256<double> mask) => Avx.BlendVariable(a, b, mask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> And(Vector256<double> a, Vector256<double> b) => Avx.And(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> AndNot(Vector256<double> a, Vector256<double> b) => Avx.AndNot(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Or(Vector256<double> a, Vector256<double> b) => Avx.Or(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> IsNotFinite(Vector256<double> value)
        {
            var inf = Vector256.Create(double.PositiveInfinity);
            var ninf = Vector256.Create(double.NegativeInfinity);
            return Avx.Or(Avx.CompareUnordered(value, value), Avx.Or(Avx.CompareEqual(value, inf), Avx.CompareEqual(value, ninf)));
        }
    }

    readonly struct Simd512 : ISimdDouble<Vector512<double>>
    {
        public static int LaneCount => 8;
        public static Vector512<double> Zero => Vector512<double>.Zero;
        public static Vector512<double> AllBitsSet => Vector512<double>.AllBitsSet;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Create(double value) => Vector512.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Load(ref double source) => Vector512.LoadUnsafe(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetElement(Vector512<double> value, int index) => value.GetElement(index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ExtractMsb(Vector512<double> value) => value.ExtractMostSignificantBits();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Add(Vector512<double> a, Vector512<double> b) => Avx512F.Add(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Sub(Vector512<double> a, Vector512<double> b) => Avx512F.Subtract(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Mul(Vector512<double> a, Vector512<double> b) => Avx512F.Multiply(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Min(Vector512<double> a, Vector512<double> b) => Avx512F.Min(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> GreaterThan(Vector512<double> a, Vector512<double> b) => Avx512F.CompareGreaterThan(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Blend(Vector512<double> a, Vector512<double> b, Vector512<double> mask) => Avx512F.BlendVariable(a, b, mask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> And(Vector512<double> a, Vector512<double> b) => Avx512DQ.And(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> AndNot(Vector512<double> a, Vector512<double> b) => Avx512DQ.AndNot(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Or(Vector512<double> a, Vector512<double> b) => Avx512DQ.Or(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> IsNotFinite(Vector512<double> value)
        {
            var inf = Vector512.Create(double.PositiveInfinity);
            var ninf = Vector512.Create(double.NegativeInfinity);
            return Avx512DQ.Or(Avx512F.CompareUnordered(value, value), Avx512DQ.Or(Avx512F.CompareEqual(value, inf), Avx512F.CompareEqual(value, ninf)));
        }
    }

    interface IPowerStep<TVec> where TVec : struct
    {
        static abstract bool CheckFinite { get; }
        static abstract void Step(ref TVec zx, ref TVec zy, TVec cx, TVec cy, TVec x2, TVec y2, TVec two, int exponent);
    }

    readonly struct SquarePower<TVec, TOps> : IPowerStep<TVec>
        where TVec : struct
        where TOps : ISimdDouble<TVec>
    {
        public static bool CheckFinite => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref TVec zx, ref TVec zy, TVec cx, TVec cy, TVec x2, TVec y2, TVec two, int exponent)
        {
            TVec ny = TOps.Add(TOps.Mul(TOps.Mul(two, zx), zy), cy);
            zx = TOps.Add(TOps.Sub(x2, y2), cx);
            zy = ny;
        }
    }

    readonly struct CubePower<TVec, TOps> : IPowerStep<TVec>
        where TVec : struct
        where TOps : ISimdDouble<TVec>
    {
        public static bool CheckFinite => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref TVec zx, ref TVec zy, TVec cx, TVec cy, TVec x2, TVec y2, TVec two, int exponent)
        {
            TVec real = TOps.Sub(x2, y2);
            TVec imag = TOps.Mul(TOps.Mul(two, zx), zy);
            TVec nx = TOps.Add(TOps.Sub(TOps.Mul(real, zx), TOps.Mul(imag, zy)), cx);
            TVec ny = TOps.Add(TOps.Add(TOps.Mul(real, zy), TOps.Mul(imag, zx)), cy);
            zx = nx;
            zy = ny;
        }
    }

    readonly struct FourthPower<TVec, TOps> : IPowerStep<TVec>
        where TVec : struct
        where TOps : ISimdDouble<TVec>
    {
        public static bool CheckFinite => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref TVec zx, ref TVec zy, TVec cx, TVec cy, TVec x2, TVec y2, TVec two, int exponent)
        {
            TVec real = TOps.Sub(x2, y2);
            TVec imag = TOps.Mul(TOps.Mul(two, zx), zy);
            TVec nx = TOps.Add(TOps.Sub(TOps.Mul(real, real), TOps.Mul(imag, imag)), cx);
            TVec ny = TOps.Add(TOps.Mul(TOps.Mul(two, real), imag), cy);
            zx = nx;
            zy = ny;
        }
    }

    readonly struct IntegerPower<TVec, TOps> : IPowerStep<TVec>
        where TVec : struct
        where TOps : ISimdDouble<TVec>
    {
        public static bool CheckFinite => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref TVec zx, ref TVec zy, TVec cx, TVec cy, TVec x2, TVec y2, TVec two, int exponent)
        {
            if (exponent > 1)
            {
                TVec rx = TOps.Create(1.0);
                TVec ry = TOps.Zero;
                TVec bx = zx;
                TVec by = zy;
                int e = exponent;
                while (e > 0)
                {
                    if ((e & 1) != 0)
                    {
                        TVec nextX = TOps.Sub(TOps.Mul(rx, bx), TOps.Mul(ry, by));
                        TVec nextY = TOps.Add(TOps.Mul(rx, by), TOps.Mul(ry, bx));
                        rx = nextX;
                        ry = nextY;
                    }

                    e >>= 1;
                    if (e == 0)
                        break;

                    TVec squareX = TOps.Sub(TOps.Mul(bx, bx), TOps.Mul(by, by));
                    TVec squareY = TOps.Mul(TOps.Mul(two, bx), by);
                    bx = squareX;
                    by = squareY;
                }

                zx = rx;
                zy = ry;
            }

            zx = TOps.Add(zx, cx);
            zy = TOps.Add(zy, cy);
        }
    }
}
