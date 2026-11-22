using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mandelbrot.Visualizer.Formula;

/// <summary>
/// Evaluates neighboring pixels together with AVX doubles.
/// Whole-number powers from 1 to 16 use the vector loop. Other powers stay on the scalar formula.
/// </summary>
public static class SimdMultibrot
{
    const double Bailout2 = MultibrotFormula.Bailout * MultibrotFormula.Bailout;

    [ThreadStatic] static double[]? _zx;
    [ThreadStatic] static double[]? _zy;
    [ThreadStatic] static double[]? _cx;
    [ThreadStatic] static double[]? _cy;
    [ThreadStatic] static double[]? _minR;
    [ThreadStatic] static int[]? _pix;
    [ThreadStatic] static int _exponent;

    public static bool IsSupported => Avx.IsSupported;

    public static void EvaluateRow(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        double power,
        int maxIterations,
        bool julia,
        double juliaRe,
        double juliaIm,
        float[] mu,
        float[] minRadius)
    {
        if (!IsSupported || !PositivePower(power, out int exponent) || maxIterations < 1)
        {
            EvaluateScalar(width, height, y, originX, originY, halfW, halfH, power, maxIterations, julia, juliaRe, juliaIm, mu, minRadius);
            return;
        }

        Ensure(width);
        _exponent = exponent;
        switch (exponent)
        {
            case 2:
                EvaluateFast<SquarePower>(width, height, y, originX, originY, halfW, halfH, maxIterations, julia, juliaRe, juliaIm, mu, minRadius);
                break;
            case 3:
                EvaluateFast<CubePower>(width, height, y, originX, originY, halfW, halfH, maxIterations, julia, juliaRe, juliaIm, mu, minRadius);
                break;
            case 4:
                EvaluateFast<FourthPower>(width, height, y, originX, originY, halfW, halfH, maxIterations, julia, juliaRe, juliaIm, mu, minRadius);
                break;
            default:
                EvaluateFast<IntegerPower>(width, height, y, originX, originY, halfW, halfH, maxIterations, julia, juliaRe, juliaIm, mu, minRadius);
                break;
        }
    }

    public static bool MatchesScalar(out string message)
    {
        if (!IsSupported)
        {
            message = "simd skipped, AVX is unavailable";
            return true;
        }

        (double power, bool julia)[] cases =
        [
            (2, false),
            (3, false),
            (4, false),
            (5, false),
            (1.4, false),
            (2, true),
        ];

        foreach (var (power, julia) in cases)
        {
            const int width = 13;
            const int height = 9;
            const int iterations = 80;
            var scalarMu = new float[width * height];
            var scalarRadius = new float[width * height];
            var vectorMu = new float[width * height];
            var vectorRadius = new float[width * height];
            double halfH = julia ? 1.6 : 1.35;
            double halfW = halfH * width / height;
            double originX = julia ? 0 : -0.5;

            for (int y = 0; y < height; y++)
            {
                EvaluateScalar(width, height, y, originX, 0, halfW, halfH, power, iterations, julia, -0.8, 0.156, scalarMu, scalarRadius);
                EvaluateRow(width, height, y, originX, 0, halfW, halfH, power, iterations, julia, -0.8, 0.156, vectorMu, vectorRadius);
            }

            for (int i = 0; i < scalarMu.Length; i++)
            {
                if (scalarMu[i] < 0 != vectorMu[i] < 0
                    || (scalarMu[i] >= 0 && Math.Abs(scalarMu[i] - vectorMu[i]) > 1e-3)
                    || Math.Abs(scalarRadius[i] - vectorRadius[i]) > 1e-3)
                {
                    message = $"simd disagrees with scalar at n={power} pixel {i} ({scalarMu[i]} vs {vectorMu[i]})";
                    return false;
                }
            }
        }

        message = "simd matches the scalar formula";
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void EvaluateFast<T>(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        int maxIterations,
        bool julia,
        double juliaRe,
        double juliaIm,
        float[] mu,
        float[] minRadius)
        where T : IPower
    {
        double[] zxBuf = _zx!;
        double[] zyBuf = _zy!;
        double[] cxBuf = _cx!;
        double[] cyBuf = _cy!;
        double[] minBuf = _minR!;
        int[] pix = _pix!;
        double rowIm = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
        int row = y * width;
        for (int x = 0; x < width; x++)
        {
            double re = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
            if (julia)
            {
                zxBuf[x] = re;
                zyBuf[x] = rowIm;
                cxBuf[x] = juliaRe;
                cyBuf[x] = juliaIm;
            }
            else
            {
                zxBuf[x] = 0;
                zyBuf[x] = 0;
                cxBuf[x] = re;
                cyBuf[x] = rowIm;
            }

            minBuf[x] = double.MaxValue;
            pix[x] = row + x;
        }

        if (Avx512F.IsSupported && Avx512DQ.IsSupported)
            Run512<T>(zxBuf, zyBuf, cxBuf, cyBuf, minBuf, pix, width, maxIterations, mu, minRadius);
        else
            Run256<T>(zxBuf, zyBuf, cxBuf, cyBuf, minBuf, pix, width, maxIterations, mu, minRadius);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Run512<T>(
        double[] zxBuf,
        double[] zyBuf,
        double[] cxBuf,
        double[] cyBuf,
        double[] minBuf,
        int[] pix,
        int active,
        int maxIterations,
        float[] mu,
        float[] minRadius)
        where T : IPower
    {
        var bailout = Vector512.Create(Bailout2);
        var two = Vector512.Create(2.0);
        double power = _exponent;
        int p = 0;
        for (; p + 8 <= active; p += 8)
        {
            var zx = Vector512.LoadUnsafe(ref zxBuf[p]);
            var zy = Vector512.LoadUnsafe(ref zyBuf[p]);
            var cx = Vector512.LoadUnsafe(ref cxBuf[p]);
            var cy = Vector512.LoadUnsafe(ref cyBuf[p]);
            var minR = Vector512.LoadUnsafe(ref minBuf[p]);
            var notDone = Vector512<double>.AllBitsSet;
            var escapeR = Vector512<double>.Zero;
            var escapeI = Vector512<double>.Zero;
            var hard = Vector512<double>.Zero;
            int block = 0;
            while (block < maxIterations && notDone.ExtractMostSignificantBits() != 0)
            {
                int stop = block + 8;
                if (stop > maxIterations)
                    stop = maxIterations;
                for (int i = block; i < stop; i++)
                {
                    var x2 = Avx512F.Multiply(zx, zx);
                    var y2 = Avx512F.Multiply(zy, zy);
                    var r2 = Avx512F.Add(x2, y2);
                    var over = Avx512F.CompareGreaterThan(r2, bailout);
                    minR = Avx512F.BlendVariable(minR, Avx512F.Min(minR, r2), notDone);
                    var just = Avx512DQ.And(over, notDone);
                    var at = Vector512.Create((double)i);
                    escapeR = Avx512F.BlendVariable(escapeR, r2, just);
                    escapeI = Avx512F.BlendVariable(escapeI, at, just);
                    notDone = Avx512DQ.AndNot(over, notDone);
                    T.Step512(ref zx, ref zy, cx, cy, x2, y2, two);
                    if (T.CheckFinite)
                        StopBroken512(ref zx, ref zy, ref notDone, ref escapeI, ref hard, at);
                }

                block = stop;
            }

            ulong alive = notDone.ExtractMostSignificantBits();
            ulong broken = T.CheckFinite ? hard.ExtractMostSignificantBits() : 0;
            for (int lane = 0; lane < 8; lane++)
                Commit(pix[p + lane], (alive & (1UL << lane)) != 0, (broken & (1UL << lane)) != 0, (int)escapeI.GetElement(lane), escapeR.GetElement(lane), minR.GetElement(lane), power, mu, minRadius);
        }

        FinishTail(zxBuf, zyBuf, cxBuf, cyBuf, pix, p, active, maxIterations, mu, minRadius);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Run256<T>(
        double[] zxBuf,
        double[] zyBuf,
        double[] cxBuf,
        double[] cyBuf,
        double[] minBuf,
        int[] pix,
        int active,
        int maxIterations,
        float[] mu,
        float[] minRadius)
        where T : IPower
    {
        var bailout = Vector256.Create(Bailout2);
        var two = Vector256.Create(2.0);
        double power = _exponent;
        int p = 0;
        for (; p + 4 <= active; p += 4)
        {
            var zx = Vector256.LoadUnsafe(ref zxBuf[p]);
            var zy = Vector256.LoadUnsafe(ref zyBuf[p]);
            var cx = Vector256.LoadUnsafe(ref cxBuf[p]);
            var cy = Vector256.LoadUnsafe(ref cyBuf[p]);
            var minR = Vector256.LoadUnsafe(ref minBuf[p]);
            var notDone = Vector256<double>.AllBitsSet;
            var escapeR = Vector256<double>.Zero;
            var escapeI = Vector256<double>.Zero;
            var hard = Vector256<double>.Zero;
            int block = 0;
            while (block < maxIterations && notDone.ExtractMostSignificantBits() != 0)
            {
                int stop = block + 8;
                if (stop > maxIterations)
                    stop = maxIterations;
                for (int i = block; i < stop; i++)
                {
                    var x2 = Avx.Multiply(zx, zx);
                    var y2 = Avx.Multiply(zy, zy);
                    var r2 = Avx.Add(x2, y2);
                    var over = Avx.CompareGreaterThan(r2, bailout);
                    minR = Avx.BlendVariable(minR, Avx.Min(minR, r2), notDone);
                    var just = Avx.And(over, notDone);
                    var at = Vector256.Create((double)i);
                    escapeR = Avx.BlendVariable(escapeR, r2, just);
                    escapeI = Avx.BlendVariable(escapeI, at, just);
                    notDone = Avx.AndNot(over, notDone);
                    T.Step(ref zx, ref zy, cx, cy, x2, y2, two);
                    if (T.CheckFinite)
                        StopBroken256(ref zx, ref zy, ref notDone, ref escapeI, ref hard, at);
                }

                block = stop;
            }

            uint alive = notDone.ExtractMostSignificantBits();
            uint broken = T.CheckFinite ? hard.ExtractMostSignificantBits() : 0;
            for (int lane = 0; lane < 4; lane++)
                Commit(pix[p + lane], (alive & (1U << lane)) != 0, (broken & (1U << lane)) != 0, (int)escapeI.GetElement(lane), escapeR.GetElement(lane), minR.GetElement(lane), power, mu, minRadius);
        }

        FinishTail(zxBuf, zyBuf, cxBuf, cyBuf, pix, p, active, maxIterations, mu, minRadius);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void StopBroken512(
        ref Vector512<double> zx,
        ref Vector512<double> zy,
        ref Vector512<double> notDone,
        ref Vector512<double> escapeI,
        ref Vector512<double> hard,
        Vector512<double> iter)
    {
        var inf = Vector512.Create(double.PositiveInfinity);
        var ninf = Vector512.Create(double.NegativeInfinity);
        var bad = Avx512DQ.Or(Avx512F.CompareUnordered(zx, zx), Avx512DQ.Or(Avx512F.CompareEqual(zx, inf), Avx512F.CompareEqual(zx, ninf)));
        var justBad = Avx512DQ.And(bad, notDone);
        escapeI = Avx512F.BlendVariable(escapeI, iter, justBad);
        hard = Avx512DQ.Or(hard, justBad);
        notDone = Avx512DQ.AndNot(bad, notDone);
        zx = Avx512DQ.And(zx, notDone);
        zy = Avx512DQ.And(zy, notDone);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void StopBroken256(
        ref Vector256<double> zx,
        ref Vector256<double> zy,
        ref Vector256<double> notDone,
        ref Vector256<double> escapeI,
        ref Vector256<double> hard,
        Vector256<double> iter)
    {
        var inf = Vector256.Create(double.PositiveInfinity);
        var ninf = Vector256.Create(double.NegativeInfinity);
        var bad = Avx.Or(Avx.CompareUnordered(zx, zx), Avx.Or(Avx.CompareEqual(zx, inf), Avx.CompareEqual(zx, ninf)));
        var justBad = Avx.And(bad, notDone);
        escapeI = Avx.BlendVariable(escapeI, iter, justBad);
        hard = Avx.Or(hard, justBad);
        notDone = Avx.AndNot(bad, notDone);
        zx = Avx.And(zx, notDone);
        zy = Avx.And(zy, notDone);
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

    static void FinishTail(
        double[] zxBuf,
        double[] zyBuf,
        double[] cxBuf,
        double[] cyBuf,
        int[] pix,
        int p,
        int active,
        int maxIterations,
        float[] mu,
        float[] minRadius)
    {
        double power = _exponent;
        for (; p < active; p++)
        {
            var escape = MultibrotFormula.Evaluate(zxBuf[p], zyBuf[p], cxBuf[p], cyBuf[p], power, maxIterations);
            mu[pix[p]] = escape.Mu;
            minRadius[pix[p]] = escape.MinRadius;
        }
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
        _pix = new int[width];
    }

    static bool PositivePower(double power, out int exponent)
    {
        double rounded = Math.Round(power);
        if (Math.Abs(power - rounded) < 1e-8 && rounded is >= 1 and <= 16)
        {
            exponent = (int)rounded;
            return true;
        }

        exponent = 0;
        return false;
    }

    static void EvaluateScalar(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        double power,
        int maxIterations,
        bool julia,
        double juliaRe,
        double juliaIm,
        float[] mu,
        float[] minRadius)
    {
        for (int x = 0; x < width; x++)
        {
            double cy = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
            double cx = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
            var escape = julia
                ? MultibrotFormula.Evaluate(cx, cy, juliaRe, juliaIm, power, maxIterations)
                : MultibrotFormula.Evaluate(0, 0, cx, cy, power, maxIterations);
            int pixel = y * width + x;
            mu[pixel] = escape.Mu;
            minRadius[pixel] = escape.MinRadius;
        }
    }

    interface IPower
    {
        static abstract bool CheckFinite { get; }
        static abstract void Step(ref Vector256<double> zx, ref Vector256<double> zy, Vector256<double> cx, Vector256<double> cy, Vector256<double> x2, Vector256<double> y2, Vector256<double> two);
        static abstract void Step512(ref Vector512<double> zx, ref Vector512<double> zy, Vector512<double> cx, Vector512<double> cy, Vector512<double> x2, Vector512<double> y2, Vector512<double> two);
    }

    struct SquarePower : IPower
    {
        public static bool CheckFinite => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref Vector256<double> zx, ref Vector256<double> zy, Vector256<double> cx, Vector256<double> cy, Vector256<double> x2, Vector256<double> y2, Vector256<double> two)
        {
            var ny = Avx.Add(Avx.Multiply(Avx.Multiply(two, zx), zy), cy);
            zx = Avx.Add(Avx.Subtract(x2, y2), cx);
            zy = ny;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step512(ref Vector512<double> zx, ref Vector512<double> zy, Vector512<double> cx, Vector512<double> cy, Vector512<double> x2, Vector512<double> y2, Vector512<double> two)
        {
            var ny = Avx512F.Add(Avx512F.Multiply(Avx512F.Multiply(two, zx), zy), cy);
            zx = Avx512F.Add(Avx512F.Subtract(x2, y2), cx);
            zy = ny;
        }
    }

    struct CubePower : IPower
    {
        public static bool CheckFinite => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref Vector256<double> zx, ref Vector256<double> zy, Vector256<double> cx, Vector256<double> cy, Vector256<double> x2, Vector256<double> y2, Vector256<double> two)
        {
            var real = Avx.Subtract(x2, y2);
            var imag = Avx.Multiply(Avx.Multiply(two, zx), zy);
            var nx = Avx.Add(Avx.Subtract(Avx.Multiply(real, zx), Avx.Multiply(imag, zy)), cx);
            var ny = Avx.Add(Avx.Add(Avx.Multiply(real, zy), Avx.Multiply(imag, zx)), cy);
            zx = nx;
            zy = ny;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step512(ref Vector512<double> zx, ref Vector512<double> zy, Vector512<double> cx, Vector512<double> cy, Vector512<double> x2, Vector512<double> y2, Vector512<double> two)
        {
            var real = Avx512F.Subtract(x2, y2);
            var imag = Avx512F.Multiply(Avx512F.Multiply(two, zx), zy);
            var nx = Avx512F.Add(Avx512F.Subtract(Avx512F.Multiply(real, zx), Avx512F.Multiply(imag, zy)), cx);
            var ny = Avx512F.Add(Avx512F.Add(Avx512F.Multiply(real, zy), Avx512F.Multiply(imag, zx)), cy);
            zx = nx;
            zy = ny;
        }
    }

    struct FourthPower : IPower
    {
        public static bool CheckFinite => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref Vector256<double> zx, ref Vector256<double> zy, Vector256<double> cx, Vector256<double> cy, Vector256<double> x2, Vector256<double> y2, Vector256<double> two)
        {
            var real = Avx.Subtract(x2, y2);
            var imag = Avx.Multiply(Avx.Multiply(two, zx), zy);
            var nx = Avx.Add(Avx.Subtract(Avx.Multiply(real, real), Avx.Multiply(imag, imag)), cx);
            var ny = Avx.Add(Avx.Multiply(Avx.Multiply(two, real), imag), cy);
            zx = nx;
            zy = ny;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step512(ref Vector512<double> zx, ref Vector512<double> zy, Vector512<double> cx, Vector512<double> cy, Vector512<double> x2, Vector512<double> y2, Vector512<double> two)
        {
            var real = Avx512F.Subtract(x2, y2);
            var imag = Avx512F.Multiply(Avx512F.Multiply(two, zx), zy);
            var nx = Avx512F.Add(Avx512F.Subtract(Avx512F.Multiply(real, real), Avx512F.Multiply(imag, imag)), cx);
            var ny = Avx512F.Add(Avx512F.Multiply(Avx512F.Multiply(two, real), imag), cy);
            zx = nx;
            zy = ny;
        }
    }

    struct IntegerPower : IPower
    {
        public static bool CheckFinite => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref Vector256<double> zx, ref Vector256<double> zy, Vector256<double> cx, Vector256<double> cy, Vector256<double> x2, Vector256<double> y2, Vector256<double> two)
        {
            PowPositive(ref zx, ref zy, _exponent);
            zx = Avx.Add(zx, cx);
            zy = Avx.Add(zy, cy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step512(ref Vector512<double> zx, ref Vector512<double> zy, Vector512<double> cx, Vector512<double> cy, Vector512<double> x2, Vector512<double> y2, Vector512<double> two)
        {
            PowPositive512(ref zx, ref zy, _exponent);
            zx = Avx512F.Add(zx, cx);
            zy = Avx512F.Add(zy, cy);
        }
    }

    static void PowPositive(ref Vector256<double> zx, ref Vector256<double> zy, int power)
    {
        if (power == 1)
            return;

        int exponent = power;
        var rx = Vector256.Create(1.0);
        var ry = Vector256<double>.Zero;
        var bx = zx;
        var by = zy;
        var two = Vector256.Create(2.0);
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                var nextX = Avx.Subtract(Avx.Multiply(rx, bx), Avx.Multiply(ry, by));
                var nextY = Avx.Add(Avx.Multiply(rx, by), Avx.Multiply(ry, bx));
                rx = nextX;
                ry = nextY;
            }

            exponent >>= 1;
            if (exponent == 0)
                break;

            var squareX = Avx.Subtract(Avx.Multiply(bx, bx), Avx.Multiply(by, by));
            var squareY = Avx.Multiply(Avx.Multiply(two, bx), by);
            bx = squareX;
            by = squareY;
        }

        zx = rx;
        zy = ry;
    }

    static void PowPositive512(ref Vector512<double> zx, ref Vector512<double> zy, int power)
    {
        if (power == 1)
            return;

        int exponent = power;
        var rx = Vector512.Create(1.0);
        var ry = Vector512<double>.Zero;
        var bx = zx;
        var by = zy;
        var two = Vector512.Create(2.0);
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                var nextX = Avx512F.Subtract(Avx512F.Multiply(rx, bx), Avx512F.Multiply(ry, by));
                var nextY = Avx512F.Add(Avx512F.Multiply(rx, by), Avx512F.Multiply(ry, bx));
                rx = nextX;
                ry = nextY;
            }

            exponent >>= 1;
            if (exponent == 0)
                break;

            var squareX = Avx512F.Subtract(Avx512F.Multiply(bx, bx), Avx512F.Multiply(by, by));
            var squareY = Avx512F.Multiply(Avx512F.Multiply(two, bx), by);
            bx = squareX;
            by = squareY;
        }

        zx = rx;
        zy = ry;
    }
}
