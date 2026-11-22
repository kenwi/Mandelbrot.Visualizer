using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// High-precision reference orbit Z_0..Z_L computed in arbitrary-precision BigFloat
/// for perturbation rendering at arbitrary zoom depths.
/// </summary>
public sealed class ReferenceOrbit
{
    public BigFloat RefRe { get; }
    public BigFloat RefIm { get; }
    public int Exponent { get; }
    public bool Julia { get; }
    public double JuliaRe { get; }
    public double JuliaIm { get; }
    public int MaxIterations { get; }
    public int PrecisionBits { get; }
    public double[] Zx { get; }
    public double[] Zy { get; }
    public int Length { get; }
    public bool Escaped { get; }

    ReferenceOrbit(
        BigFloat refRe,
        BigFloat refIm,
        int exponent,
        bool julia,
        double juliaRe,
        double juliaIm,
        int maxIterations,
        int precisionBits,
        double[] zx,
        double[] zy,
        int length,
        bool escaped)
    {
        RefRe = refRe;
        RefIm = refIm;
        Exponent = exponent;
        Julia = julia;
        JuliaRe = juliaRe;
        JuliaIm = juliaIm;
        MaxIterations = maxIterations;
        PrecisionBits = precisionBits;
        Zx = zx;
        Zy = zy;
        Length = length;
        Escaped = escaped;
    }

    public bool IsReusableFor(BigFloat centerX, BigFloat centerY, FloatExp scale, in FormulaParams formula)
    {
        if (!FormulaParams.SupportsPerturbation(formula.Power, out int exp) || exp != Exponent)
            return false;
        if (Julia != formula.Julia)
            return false;
        if (Julia && (JuliaRe != formula.JuliaRe || JuliaIm != formula.JuliaIm))
            return false;
        if (MaxIterations < formula.MaxIterations)
            return false;
        if (PrecisionBits < BigFloat.RequiredBits(scale))
            return false;

        int prec = PrecisionBits;
        FloatExp dx = BigFloat.Sub(centerX, RefRe, prec).ToFloatExp().Abs();
        FloatExp dy = BigFloat.Sub(centerY, RefIm, prec).ToFloatExp().Abs();
        FloatExp limit = scale * 2.5;
        return dx <= limit && dy <= limit;
    }

    public static ReferenceOrbit? ComputeBest(
        BigFloat centerX,
        BigFloat centerY,
        FloatExp scale,
        in FormulaParams formula,
        CancellationToken ct = default)
    {
        if (!FormulaParams.SupportsPerturbation(formula.Power, out int exponent))
            return null;

        // Add 64 extra guard bits so several consecutive zoom steps reuse this orbit.
        int prec = BigFloat.RequiredBits(scale) + 64;
        int maxIter = Math.Max(1, formula.MaxIterations);

        ReferenceOrbit? best = ComputeAt(centerX, centerY, exponent, formula, maxIter, prec, ct);
        if (best == null || ct.IsCancellationRequested || !best.Escaped)
            return best;

        // Probe a small 3x3 ring around center to find a longer-lived reference orbit.
        (double ox, double oy)[] offsets =
        [
            (-0.35, 0.0), (0.35, 0.0), (0.0, -0.35), (0.0, 0.35),
            (-0.35, -0.35), (0.35, -0.35), (-0.35, 0.35), (0.35, 0.35),
        ];

        foreach (var (ox, oy) in offsets)
        {
            if (ct.IsCancellationRequested)
                return null;

            BigFloat px = BigFloat.Add(centerX, BigFloat.FromFloatExp(scale * ox), prec);
            BigFloat py = BigFloat.Add(centerY, BigFloat.FromFloatExp(scale * oy), prec);
            ReferenceOrbit? candidate = ComputeAt(px, py, exponent, formula, maxIter, prec, ct);
            if (candidate == null)
                return null;

            if (candidate.Length > best.Length)
            {
                best = candidate;
                if (!best.Escaped)
                    break;
            }
        }

        return best;
    }

    public static ReferenceOrbit? ComputeAt(
        BigFloat refRe,
        BigFloat refIm,
        int exponent,
        in FormulaParams formula,
        int maxIterations,
        int precisionBits,
        CancellationToken ct = default)
    {
        const double bailout2 = MultibrotFormula.Bailout * MultibrotFormula.Bailout;

        var zxArr = new double[maxIterations + 1];
        var zyArr = new double[maxIterations + 1];

        BigFloat zx;
        BigFloat zy;
        BigFloat cx;
        BigFloat cy;

        if (formula.Julia)
        {
            zx = refRe.Round(precisionBits);
            zy = refIm.Round(precisionBits);
            cx = BigFloat.FromDouble(formula.JuliaRe);
            cy = BigFloat.FromDouble(formula.JuliaIm);
        }
        else
        {
            zx = BigFloat.Zero;
            zy = BigFloat.Zero;
            cx = refRe.Round(precisionBits);
            cy = refIm.Round(precisionBits);
        }

        int count = 0;
        bool escaped = false;

        for (int i = 0; i <= maxIterations; i++)
        {
            if ((i & 63) == 0 && ct.IsCancellationRequested)
                return null;

            double dx = zx.ToDouble();
            double dy = zy.ToDouble();
            zxArr[count] = dx;
            zyArr[count] = dy;
            count++;

            double r2 = dx * dx + dy * dy;
            if (!double.IsFinite(r2) || r2 > bailout2)
            {
                escaped = true;
                break;
            }

            if (i == maxIterations)
                break;

            StepBigFloat(ref zx, ref zy, cx, cy, exponent, precisionBits);
        }

        return new ReferenceOrbit(
            refRe.Round(precisionBits),
            refIm.Round(precisionBits),
            exponent,
            formula.Julia,
            formula.JuliaRe,
            formula.JuliaIm,
            maxIterations,
            precisionBits,
            zxArr,
            zyArr,
            count,
            escaped);
    }

    /// <summary>
    /// Ground-truth arbitrary-precision escape evaluation used by SelfTest to verify perturbation parity.
    /// </summary>
    public static MultibrotFormula.Escape EvaluateExact(
        BigFloat pixelRe,
        BigFloat pixelIm,
        in FormulaParams formula,
        int precisionBits)
    {
        if (!FormulaParams.SupportsPerturbation(formula.Power, out int exponent))
            return MultibrotFormula.Evaluate(0, 0, pixelRe.ToDouble(), pixelIm.ToDouble(), formula.Power, formula.MaxIterations);

        const double bailout2 = MultibrotFormula.Bailout * MultibrotFormula.Bailout;
        BigFloat zx = formula.Julia ? pixelRe.Round(precisionBits) : BigFloat.Zero;
        BigFloat zy = formula.Julia ? pixelIm.Round(precisionBits) : BigFloat.Zero;
        BigFloat cx = formula.Julia ? BigFloat.FromDouble(formula.JuliaRe) : pixelRe.Round(precisionBits);
        BigFloat cy = formula.Julia ? BigFloat.FromDouble(formula.JuliaIm) : pixelIm.Round(precisionBits);

        double minR = double.MaxValue;
        for (int i = 0; i < formula.MaxIterations; i++)
        {
            double dx = zx.ToDouble();
            double dy = zy.ToDouble();
            double r2 = dx * dx + dy * dy;
            if (r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > bailout2)
                return MultibrotFormula.Escaped(i, r2, exponent, minR);

            StepBigFloat(ref zx, ref zy, cx, cy, exponent, precisionBits);
        }

        return MultibrotFormula.Inside(minR);
    }

    static void StepBigFloat(ref BigFloat zx, ref BigFloat zy, BigFloat cx, BigFloat cy, int exponent, int prec)
    {
        if (exponent == 2)
        {
            BigFloat x2 = BigFloat.Square(zx, prec);
            BigFloat y2 = BigFloat.Square(zy, prec);
            BigFloat xy = BigFloat.Mul(zx, zy, prec);
            zy = BigFloat.Add(BigFloat.MulPow2(xy, 1), cy, prec);
            zx = BigFloat.Add(BigFloat.Sub(x2, y2, prec), cx, prec);
            return;
        }

        BigFloat rx = BigFloat.FromDouble(1.0);
        BigFloat ry = BigFloat.Zero;
        BigFloat bx = zx;
        BigFloat by = zy;
        int e = exponent;

        while (e > 0)
        {
            if ((e & 1) != 0)
            {
                BigFloat nx = BigFloat.Sub(BigFloat.Mul(rx, bx, prec), BigFloat.Mul(ry, by, prec), prec);
                BigFloat ny = BigFloat.Add(BigFloat.Mul(rx, by, prec), BigFloat.Mul(ry, bx, prec), prec);
                rx = nx;
                ry = ny;
            }

            e >>= 1;
            if (e == 0) break;

            BigFloat sx = BigFloat.Sub(BigFloat.Square(bx, prec), BigFloat.Square(by, prec), prec);
            BigFloat sy = BigFloat.MulPow2(BigFloat.Mul(bx, by, prec), 1);
            bx = sx;
            by = sy;
        }

        zx = BigFloat.Add(rx, cx, prec);
        zy = BigFloat.Add(ry, cy, prec);
    }
}

/// <summary>
/// Manages asynchronous reference-orbit computation and reuse across frames so deep-zoom
/// orbit generation never stalls the UI thread.
/// </summary>
public sealed class ReferenceOrbitCache : IDisposable
{
    readonly object _gate = new();
    ReferenceOrbit? _ready;
    CancellationTokenSource? _cts;
    bool _computing;
    BigFloat _pendingCenterX;
    BigFloat _pendingCenterY;
    FloatExp _pendingScale;
    FormulaParams _pendingFormula;
    bool _hasPending;

    public bool IsComputing
    {
        get { lock (_gate) return _computing; }
    }

    public ReferenceOrbit? TryGetOrSchedule(BigFloat centerX, BigFloat centerY, FloatExp scale, in FormulaParams formula)
    {
        lock (_gate)
        {
            if (_ready != null && _ready.IsReusableFor(centerX, centerY, scale, formula))
                return _ready;

            bool samePending = _hasPending
                && _pendingCenterX == centerX
                && _pendingCenterY == centerY
                && _pendingScale == scale
                && _pendingFormula == formula;

            if (!samePending)
            {
                _pendingCenterX = centerX;
                _pendingCenterY = centerY;
                _pendingScale = scale;
                _pendingFormula = formula;
                _hasPending = true;

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                _computing = true;

                FormulaParams capturedFormula = formula;
                Task.Run(() =>
                {
                    ReferenceOrbit? orbit = ReferenceOrbit.ComputeBest(centerX, centerY, scale, capturedFormula, token);
                    lock (_gate)
                    {
                        if (!token.IsCancellationRequested)
                        {
                            if (orbit != null)
                                _ready = orbit;
                            _computing = false;
                        }
                    }
                });
            }

            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _computing = false;
        }
    }
}
