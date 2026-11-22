using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Evaluates pixels together with AVX2 / AVX-512 doubles using FMA intrinsics,
/// cardioid/period-2 bulb early exits, Brent periodicity checks, and dynamic lane refilling.
/// </summary>
public static class SimdMultibrot
{
    const double Bailout2 = MultibrotFormula.Bailout * MultibrotFormula.Bailout;
    const double PeriodicityEpsilon = 1e-13;

    [ThreadStatic] static double[]? _workCx;
    [ThreadStatic] static int[]? _workPix;

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
        float[] minRadius,
        bool needsMinRadius = true)
    {
        if (!IsSupported || formula.Family != FractalFamily.Multibrot || !FormulaParams.TryGetPositiveSimdExponent(formula.Power, out int exponent) || formula.MaxIterations < 1)
        {
            MultibrotFormula.EvaluateScalarRow(width, height, y, originX, originY, halfW, halfH, formula, mu, minRadius, needsMinRadius);
            return;
        }

        Ensure(width);
        if (Avx512F.IsSupported && Avx512DQ.IsSupported)
            Dispatch<Vector512<double>, Simd512>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius, needsMinRadius);
        else if (Fma.IsSupported)
            Dispatch<Vector256<double>, Simd256Fma>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius, needsMinRadius);
        else
            Dispatch<Vector256<double>, Simd256>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius, needsMinRadius);
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
        float[] minRadius,
        bool needsMinRadius)
        where TVec : struct
        where TOps : ISimdDouble<TVec>
    {
        switch (exponent)
        {
            case 2:
                EvaluateStreaming<TVec, TOps, SquarePower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius, needsMinRadius);
                break;
            case 3:
                EvaluateStreaming<TVec, TOps, CubePower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius, needsMinRadius);
                break;
            case 4:
                EvaluateStreaming<TVec, TOps, FourthPower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius, needsMinRadius);
                break;
            default:
                EvaluateStreaming<TVec, TOps, IntegerPower<TVec, TOps>>(width, height, y, originX, originY, halfW, halfH, exponent, formula, mu, minRadius, needsMinRadius);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void EvaluateStreaming<TVec, TOps, TPower>(
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
        float[] minRadius,
        bool needsMinRadius)
        where TVec : struct
        where TOps : ISimdDouble<TVec>
        where TPower : IPowerStep<TVec>
    {
        double[] workCx = _workCx!;
        int[] workPix = _workPix!;
        double rowIm = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
        int row = y * width;
        bool useCardioidCheck = !needsMinRadius && !formula.Julia && exponent == 2;

        int activeCount = 0;
        for (int x = 0; x < width; x++)
        {
            double re = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
            if (useCardioidCheck && MultibrotFormula.IsInMandelbrotCardioidOrBulb(re, rowIm))
            {
                mu[row + x] = -1f;
                minRadius[row + x] = 0f;
                continue;
            }

            workCx[activeCount] = re;
            workPix[activeCount] = row + x;
            activeCount++;
        }

        if (activeCount == 0)
            return;

        int lanes = TOps.LaneCount;
        int maxIterations = formula.MaxIterations;
        double power = exponent;
        bool julia = formula.Julia;
        double juliaRe = formula.JuliaRe;
        double juliaIm = formula.JuliaIm;

        Span<int> lanePix = stackalloc int[lanes];
        Span<int> laneNextCheckpoint = stackalloc int[lanes];
        Span<int> lanePeriod = stackalloc int[lanes];
        Span<double> zxArr = stackalloc double[lanes];
        Span<double> zyArr = stackalloc double[lanes];
        Span<double> cxArr = stackalloc double[lanes];
        Span<double> cyArr = stackalloc double[lanes];
        Span<double> minArr = stackalloc double[lanes];
        Span<double> iterArr = stackalloc double[lanes];
        Span<double> oxArr = stackalloc double[lanes];
        Span<double> oyArr = stackalloc double[lanes];
        Span<double> maskArr = stackalloc double[lanes];
        Span<double> escRArr = stackalloc double[lanes];
        Span<double> escIArr = stackalloc double[lanes];

        ulong activeMask = 0;
        int nextWork = 0;
        for (int lane = 0; lane < lanes; lane++)
        {
            if (nextWork < activeCount)
            {
                LoadLane(lane, workPix[nextWork], workCx[nextWork], rowIm, julia, juliaRe, juliaIm,
                    lanePix, laneNextCheckpoint, lanePeriod, zxArr, zyArr, cxArr, cyArr, minArr, iterArr, oxArr, oyArr, maskArr);
                activeMask |= 1UL << lane;
                nextWork++;
            }
            else
            {
                ClearLane(lane, lanePix, zxArr, zyArr, cxArr, cyArr, minArr, iterArr, oxArr, oyArr, maskArr);
            }
        }

        TVec zx = TOps.Load(ref zxArr[0]);
        TVec zy = TOps.Load(ref zyArr[0]);
        TVec cx = TOps.Load(ref cxArr[0]);
        TVec cy = TOps.Load(ref cyArr[0]);
        TVec minR = TOps.Load(ref minArr[0]);
        TVec iter = TOps.Load(ref iterArr[0]);
        TVec notDone = TOps.Load(ref maskArr[0]);

        TVec bailout = TOps.Create(Bailout2);
        TVec two = TOps.Create(2.0);
        TVec one = TOps.Create(1.0);
        TVec maxIterVec = TOps.Create(maxIterations);

        while (activeMask != 0)
        {
            TVec escapeR = TOps.Zero;
            TVec escapeI = TOps.Zero;
            TVec escapedMask = TOps.Zero;
            TVec maxedMask = TOps.Zero;
            TVec hardMask = TOps.Zero;

            for (int step = 0; step < 8; step++)
            {
                TVec x2 = TOps.Mul(zx, zx);
                TVec y2 = TOps.Mul(zy, zy);
                TVec r2 = TOps.Add(x2, y2);
                TVec over = TOps.GreaterThan(r2, bailout);
                if (needsMinRadius)
                    minR = TOps.Blend(minR, TOps.Min(minR, r2), notDone);

                TVec justEsc = TOps.And(over, notDone);
                escapeR = TOps.Blend(escapeR, r2, justEsc);
                escapeI = TOps.Blend(escapeI, iter, justEsc);
                escapedMask = TOps.Or(escapedMask, justEsc);
                notDone = TOps.AndNot(over, notDone);

                TPower.Step(ref zx, ref zy, cx, cy, x2, y2, two, exponent);
                if (TPower.CheckFinite)
                    StopBroken<TVec, TOps>(ref zx, ref zy, ref notDone, ref escapeI, ref hardMask, iter);

                iter = TOps.Add(iter, one);
                TVec hitMax = TOps.GreaterThanOrEqual(iter, maxIterVec);
                TVec justMax = TOps.And(hitMax, notDone);
                maxedMask = TOps.Or(maxedMask, justMax);
                notDone = TOps.AndNot(hitMax, notDone);
            }

            ulong aliveBits = TOps.ExtractMsb(notDone);
            ulong maxedBits = TOps.ExtractMsb(maxedMask);
            ulong hardBits = TPower.CheckFinite ? TOps.ExtractMsb(hardMask) : 0;

            if (!needsMinRadius && aliveBits != 0)
            {
                TOps.Store(zx, ref zxArr[0]);
                TOps.Store(zy, ref zyArr[0]);
                TOps.Store(iter, ref iterArr[0]);
                bool anyPeriodic = false;
                for (int lane = 0; lane < lanes; lane++)
                {
                    ulong bit = 1UL << lane;
                    if ((aliveBits & bit) == 0) continue;

                    if (Math.Abs(zxArr[lane] - oxArr[lane]) <= PeriodicityEpsilon
                        && Math.Abs(zyArr[lane] - oyArr[lane]) <= PeriodicityEpsilon)
                    {
                        aliveBits &= ~bit;
                        maxedBits |= bit;
                        anyPeriodic = true;
                        continue;
                    }

                    int currentIter = (int)iterArr[lane];
                    if (currentIter >= laneNextCheckpoint[lane])
                    {
                        oxArr[lane] = zxArr[lane];
                        oyArr[lane] = zyArr[lane];
                        int nextPeriod = Math.Min(lanePeriod[lane] << 1, 1024);
                        lanePeriod[lane] = nextPeriod;
                        laneNextCheckpoint[lane] = currentIter + nextPeriod;
                    }
                }

                if (anyPeriodic)
                {
                    for (int lane = 0; lane < lanes; lane++)
                        maskArr[lane] = (aliveBits & (1UL << lane)) != 0 ? AllBitsDouble : 0.0;
                    notDone = TOps.Load(ref maskArr[0]);
                }
            }

            ulong doneNow = activeMask & ~aliveBits;
            if (doneNow == 0)
                continue;

            TOps.Store(zx, ref zxArr[0]);
            TOps.Store(zy, ref zyArr[0]);
            TOps.Store(cx, ref cxArr[0]);
            TOps.Store(cy, ref cyArr[0]);
            TOps.Store(minR, ref minArr[0]);
            TOps.Store(iter, ref iterArr[0]);
            TOps.Store(escapeR, ref escRArr[0]);
            TOps.Store(escapeI, ref escIArr[0]);

            for (int lane = 0; lane < lanes; lane++)
            {
                ulong bit = 1UL << lane;
                if ((doneNow & bit) == 0)
                {
                    maskArr[lane] = (activeMask & bit) != 0 ? AllBitsDouble : 0.0;
                    continue;
                }

                bool isInside = (maxedBits & bit) != 0;
                bool isBroken = (hardBits & bit) != 0;
                Commit(
                    lanePix[lane],
                    isInside,
                    isBroken,
                    (int)escIArr[lane],
                    escRArr[lane],
                    needsMinRadius ? minArr[lane] : 0,
                    power,
                    mu,
                    minRadius);

                if (nextWork < activeCount)
                {
                    LoadLane(lane, workPix[nextWork], workCx[nextWork], rowIm, julia, juliaRe, juliaIm,
                        lanePix, laneNextCheckpoint, lanePeriod, zxArr, zyArr, cxArr, cyArr, minArr, iterArr, oxArr, oyArr, maskArr);
                    nextWork++;
                }
                else
                {
                    activeMask &= ~bit;
                    ClearLane(lane, lanePix, zxArr, zyArr, cxArr, cyArr, minArr, iterArr, oxArr, oyArr, maskArr);
                }
            }

            if (activeMask != 0)
            {
                zx = TOps.Load(ref zxArr[0]);
                zy = TOps.Load(ref zyArr[0]);
                cx = TOps.Load(ref cxArr[0]);
                cy = TOps.Load(ref cyArr[0]);
                minR = TOps.Load(ref minArr[0]);
                iter = TOps.Load(ref iterArr[0]);
                notDone = TOps.Load(ref maskArr[0]);
            }
        }
    }

    static readonly double AllBitsDouble = BitConverter.Int64BitsToDouble(-1L);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void LoadLane(
        int lane,
        int pixel,
        double pixelRe,
        double rowIm,
        bool julia,
        double juliaRe,
        double juliaIm,
        Span<int> lanePix,
        Span<int> laneNextCheckpoint,
        Span<int> lanePeriod,
        Span<double> zxArr,
        Span<double> zyArr,
        Span<double> cxArr,
        Span<double> cyArr,
        Span<double> minArr,
        Span<double> iterArr,
        Span<double> oxArr,
        Span<double> oyArr,
        Span<double> maskArr)
    {
        lanePix[lane] = pixel;
        lanePeriod[lane] = 8;
        laneNextCheckpoint[lane] = 8;
        if (julia)
        {
            zxArr[lane] = pixelRe;
            zyArr[lane] = rowIm;
            cxArr[lane] = juliaRe;
            cyArr[lane] = juliaIm;
        }
        else
        {
            zxArr[lane] = 0;
            zyArr[lane] = 0;
            cxArr[lane] = pixelRe;
            cyArr[lane] = rowIm;
        }

        oxArr[lane] = zxArr[lane];
        oyArr[lane] = zyArr[lane];
        minArr[lane] = double.MaxValue;
        iterArr[lane] = 0;
        maskArr[lane] = AllBitsDouble;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ClearLane(
        int lane,
        Span<int> lanePix,
        Span<double> zxArr,
        Span<double> zyArr,
        Span<double> cxArr,
        Span<double> cyArr,
        Span<double> minArr,
        Span<double> iterArr,
        Span<double> oxArr,
        Span<double> oyArr,
        Span<double> maskArr)
    {
        lanePix[lane] = -1;
        zxArr[lane] = 0;
        zyArr[lane] = 0;
        cxArr[lane] = 0;
        cyArr[lane] = 0;
        minArr[lane] = 0;
        iterArr[lane] = 0;
        oxArr[lane] = 0;
        oyArr[lane] = 0;
        maskArr[lane] = 0;
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

    static void Commit(int pixel, bool inside, bool hard, int iteration, double r2, double minR, double power, float[] mu, float[] minRadius)
    {
        var sample = inside
            ? MultibrotFormula.Inside(minR)
            : hard
                ? MultibrotFormula.Stopped(iteration, minR)
                : MultibrotFormula.Escaped(iteration, r2, power, minR);
        mu[pixel] = sample.Mu;
        minRadius[pixel] = sample.MinRadius;
    }

    static void Ensure(int width)
    {
        if (_workCx != null && _workCx.Length >= width)
            return;

        _workCx = new double[width];
        _workPix = new int[width];
    }

    interface ISimdDouble<TVec> where TVec : struct
    {
        static abstract int LaneCount { get; }
        static abstract TVec Zero { get; }
        static abstract TVec Create(double value);
        static abstract TVec Load(ref double source);
        static abstract void Store(TVec value, ref double destination);
        static abstract ulong ExtractMsb(TVec value);
        static abstract TVec Add(TVec a, TVec b);
        static abstract TVec Sub(TVec a, TVec b);
        static abstract TVec Mul(TVec a, TVec b);
        static abstract TVec MulAdd(TVec a, TVec b, TVec c);
        static abstract TVec Min(TVec a, TVec b);
        static abstract TVec GreaterThan(TVec a, TVec b);
        static abstract TVec GreaterThanOrEqual(TVec a, TVec b);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Create(double value) => Vector256.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Load(ref double source) => Vector256.LoadUnsafe(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Vector256<double> value, ref double destination) => value.StoreUnsafe(ref destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ExtractMsb(Vector256<double> value) => value.ExtractMostSignificantBits();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Add(Vector256<double> a, Vector256<double> b) => Avx.Add(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Sub(Vector256<double> a, Vector256<double> b) => Avx.Subtract(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Mul(Vector256<double> a, Vector256<double> b) => Avx.Multiply(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> MulAdd(Vector256<double> a, Vector256<double> b, Vector256<double> c) => Avx.Add(Avx.Multiply(a, b), c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Min(Vector256<double> a, Vector256<double> b) => Avx.Min(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> GreaterThan(Vector256<double> a, Vector256<double> b) => Avx.CompareGreaterThan(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> GreaterThanOrEqual(Vector256<double> a, Vector256<double> b) => Avx.CompareGreaterThanOrEqual(a, b);

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

    readonly struct Simd256Fma : ISimdDouble<Vector256<double>>
    {
        public static int LaneCount => 4;
        public static Vector256<double> Zero => Vector256<double>.Zero;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Create(double value) => Vector256.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Load(ref double source) => Vector256.LoadUnsafe(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Vector256<double> value, ref double destination) => value.StoreUnsafe(ref destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ExtractMsb(Vector256<double> value) => value.ExtractMostSignificantBits();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Add(Vector256<double> a, Vector256<double> b) => Avx.Add(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Sub(Vector256<double> a, Vector256<double> b) => Avx.Subtract(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Mul(Vector256<double> a, Vector256<double> b) => Avx.Multiply(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> MulAdd(Vector256<double> a, Vector256<double> b, Vector256<double> c) => Fma.MultiplyAdd(a, b, c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Min(Vector256<double> a, Vector256<double> b) => Avx.Min(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> GreaterThan(Vector256<double> a, Vector256<double> b) => Avx.CompareGreaterThan(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> GreaterThanOrEqual(Vector256<double> a, Vector256<double> b) => Avx.CompareGreaterThanOrEqual(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Blend(Vector256<double> a, Vector256<double> b, Vector256<double> mask) => Avx.BlendVariable(a, b, mask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> And(Vector256<double> a, Vector256<double> b) => Avx.And(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> AndNot(Vector256<double> a, Vector256<double> b) => Avx.AndNot(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> Or(Vector256<double> a, Vector256<double> b) => Avx.Or(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<double> IsNotFinite(Vector256<double> value) => Simd256.IsNotFinite(value);
    }

    readonly struct Simd512 : ISimdDouble<Vector512<double>>
    {
        public static int LaneCount => 8;
        public static Vector512<double> Zero => Vector512<double>.Zero;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Create(double value) => Vector512.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Load(ref double source) => Vector512.LoadUnsafe(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Vector512<double> value, ref double destination) => value.StoreUnsafe(ref destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ExtractMsb(Vector512<double> value) => value.ExtractMostSignificantBits();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Add(Vector512<double> a, Vector512<double> b) => Avx512F.Add(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Sub(Vector512<double> a, Vector512<double> b) => Avx512F.Subtract(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Mul(Vector512<double> a, Vector512<double> b) => Avx512F.Multiply(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> MulAdd(Vector512<double> a, Vector512<double> b, Vector512<double> c) => Avx512F.FusedMultiplyAdd(a, b, c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> Min(Vector512<double> a, Vector512<double> b) => Avx512F.Min(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> GreaterThan(Vector512<double> a, Vector512<double> b) => Avx512F.CompareGreaterThan(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<double> GreaterThanOrEqual(Vector512<double> a, Vector512<double> b) => Avx512F.CompareGreaterThanOrEqual(a, b);

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
            TVec ny = TOps.MulAdd(TOps.Mul(two, zx), zy, cy);
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
            TVec ny = TOps.MulAdd(real, zy, TOps.MulAdd(imag, zx, cy));
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
            TVec ny = TOps.MulAdd(TOps.Mul(two, real), imag, cy);
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
                        TVec nextY = TOps.MulAdd(rx, by, TOps.Mul(ry, bx));
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
