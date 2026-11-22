using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Diagnostics;

/// <summary>
/// Headless --self-test suite validating formula accuracy, SIMD-vs-scalar parity,
/// palette LUT endpoints, BigFloat/FloatExp math, and deep-zoom perturbation parity.
/// </summary>
public static class SelfTest
{
    public static bool Run()
    {
        bool ok = true;
        ok &= Inside(2, 0, 0, "origin");
        ok &= Inside(2, -1, 0, "period-2 bulb");
        ok &= Inside(2, -0.75, 0, "main cardioid edge");
        ok &= Outside(2, 1, 0, "c = 1");
        ok &= Outside(2, 0.4, 0.5, "c = 0.4+0.5i");
        ok &= Inside(3, 0, 0, "cubic origin");
        ok &= Inside(1.4, 0, 0, "fractional origin");
        ok &= Outside(1.4, 1.2, 1.2, "fractional exterior");
        ok &= Outside(4, 1, 1, "quartic exterior");

        var square = MultibrotFormula.Evaluate(0, 0, 0.2, 0.55, 2, 80);
        var almost = MultibrotFormula.Evaluate(0, 0, 0.2, 0.55, 2.0000001, 80);
        ok &= Report(square.Inside == almost.Inside, "integer and real power agree on 0.2+0.55i", "integer and real power disagree on 0.2+0.55i");

        ok &= Report(SimdMatchesScalar(out string simd), simd, simd);
        ok &= Report(PaletteEndpointsConsistent(out string paletteMessage), paletteMessage, paletteMessage);
        ok &= Report(BigFloatAndFloatExpValid(out string bigMsg), bigMsg, bigMsg);
        ok &= Report(DeepPerturbationMatchesBigFloat(out string perturbMsg), perturbMsg, perturbMsg);
        ok &= Report(CursorProbeMatchesFormula(out string probeMsg), probeMsg, probeMsg);
        ok &= Report(ShadedLightingAndOrbitTrapValid(out string shadedMsg), shadedMsg, shadedMsg);
        ok &= Report(FractalFamiliesValid(out string familyMsg), familyMsg, familyMsg);

        Console.WriteLine(ok ? "self-test passed" : "self-test failed");
        return ok;
    }

    static bool Report(bool passed, string passText, string failText)
    {
        Console.WriteLine(passed ? $"pass  {passText}" : $"fail  {failText}");
        return passed;
    }

    static bool Inside(double power, double re, double im, string name)
    {
        var escape = MultibrotFormula.Evaluate(0, 0, re, im, power, 200);
        if (escape.Inside)
        {
            Console.WriteLine($"pass  {name}");
            return true;
        }

        Console.WriteLine($"fail  {name} escaped at {escape.Mu}");
        return false;
    }

    static bool Outside(double power, double re, double im, string name)
    {
        var escape = MultibrotFormula.Evaluate(0, 0, re, im, power, 200);
        if (!escape.Inside)
        {
            Console.WriteLine($"pass  {name}");
            return true;
        }

        Console.WriteLine($"fail  {name} stayed inside");
        return false;
    }

    static bool SimdMatchesScalar(out string message)
    {
        if (!SimdMultibrot.IsSupported)
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
            foreach (bool needsMinRadius in new[] { true, false })
            {
                const int width = 23;
                const int height = 11;
                const int iterations = 120;
                var formula = new FormulaParams(power, iterations, julia, -0.8, 0.156);
                var scalarMu = new float[width * height];
                var scalarRadius = new float[width * height];
                var vectorMu = new float[width * height];
                var vectorRadius = new float[width * height];
                double halfH = julia ? 1.6 : 1.35;
                double halfW = halfH * width / height;
                double originX = julia ? 0 : -0.5;

                for (int y = 0; y < height; y++)
                {
                    MultibrotFormula.EvaluateScalarRow(width, height, y, originX, 0, halfW, halfH, formula, scalarMu, scalarRadius, needsMinRadius);
                    SimdMultibrot.EvaluateRow(width, height, y, originX, 0, halfW, halfH, formula, vectorMu, vectorRadius, needsMinRadius);
                }

                for (int i = 0; i < scalarMu.Length; i++)
                {
                    if (scalarMu[i] < 0 != vectorMu[i] < 0
                        || (scalarMu[i] >= 0 && Math.Abs(scalarMu[i] - vectorMu[i]) > 1e-3)
                        || (needsMinRadius && Math.Abs(scalarRadius[i] - vectorRadius[i]) > 1e-3))
                    {
                        message = $"simd disagrees with scalar at n={power} needsMinRadius={needsMinRadius} pixel {i} ({scalarMu[i]} vs {vectorMu[i]})";
                        return false;
                    }
                }
            }
        }

        message = "simd (FMA + lane refill + early exits) matches the scalar formula";
        return true;
    }

    static bool PaletteEndpointsConsistent(out string message)
    {
        var fireEnd = Palette.SampleUnwrapped(1, 1.0);
        var grayEnd = Palette.SampleUnwrapped(6, 1.0);
        if (fireEnd.r == 255 && fireEnd.g == 250 && fireEnd.b == 220
            && grayEnd.r == 255 && grayEnd.g == 255 && grayEnd.b == 255)
        {
            message = "palette LUT endpoints preserved without wrap";
            return true;
        }

        message = $"unexpected palette LUT endpoint (fire={fireEnd.r},{fireEnd.g},{fireEnd.b}, gray={grayEnd.r},{grayEnd.g},{grayEnd.b})";
        return false;
    }

    static bool BigFloatAndFloatExpValid(out string message)
    {
        // Verify FloatExp far below IEEE-754 double underflow (1e-500, 1e-1000).
        if (!FloatExp.TryParse("1e-500", out FloatExp tiny) || tiny.IsZero)
        {
            message = "FloatExp failed to parse 1e-500";
            return false;
        }

        FloatExp tinySq = tiny * tiny;
        if (tinySq.IsZero || Math.Abs(tinySq.Log10() - (-1000.0)) > 1e-6)
        {
            message = $"FloatExp 1e-500 squared log10 was {tinySq.Log10()}";
            return false;
        }

        // Verify BigFloat preserves tiny offsets added to O(1) values at 256-bit and 1024-bit precision.
        foreach (int prec in new[] { 256, 1024 })
        {
            BigFloat one = BigFloat.FromDouble(1.0);
            FloatExp eps = prec == 256 ? FloatExp.FromDouble(1e-50) : FloatExp.Create(1.0, -700);
            BigFloat sum = BigFloat.Add(one, BigFloat.FromFloatExp(eps), prec);
            BigFloat diff = BigFloat.Sub(sum, one, prec);
            FloatExp recovered = diff.ToFloatExp();
            double relErr = Math.Abs((recovered / eps).ToDouble() - 1.0);
            if (relErr > 1e-12)
            {
                message = $"BigFloat precision loss at {prec} bits (relErr={relErr})";
                return false;
            }
        }

        // Verify ComplexExp square at 1e-500 -> norm squared at 1e-2000 without underflow.
        var c = ComplexExp.FromFloatExp(tiny * 3.0, tiny * 4.0);
        ComplexExp c2 = ComplexExp.Square(c);
        FloatExp expectedNorm = tinySq * tinySq * 625.0; // |(3+4i)^2|^2 * (1e-500)^4 = 625 * 1e-2000
        double normRatio = (c2.NormSquared() / expectedNorm).ToDouble();
        if (Math.Abs(normRatio - 1.0) > 1e-12)
        {
            message = $"ComplexExp square norm mismatch ({normRatio})";
            return false;
        }

        message = "BigFloat (256/1024-bit) and FloatExp/ComplexExp (1e-1000) arithmetic valid";
        return true;
    }

    static bool DeepPerturbationMatchesBigFloat(out string message)
    {
        (string label, double power, bool julia, string cxStr, string cyStr, string scaleStr, int iters)[] cases =
        [
            ("Mandelbrot n=2 at 1e-25", 2, false, "-0.743643887037158704752191506114774", "0.131825904205311970493132056385139", "1e-25", 1200),
            ("Mandelbrot n=2 at 1e-80", 2, false, "-0.743643887037158704752191506114774", "0.131825904205311970493132056385139", "1e-80", 1500),
            ("Cubic n=3 at 1e-25", 3, false, "-0.54", "0.52", "1e-25", 400),
            ("Quartic n=4 at 1e-25", 4, false, "-0.58", "0.42", "1e-25", 400),
            ("Julia n=2 at 1e-25", 2, true, "0.1234567890123456789012345", "-0.2345678901234567890123456", "1e-25", 600),
        ];

        foreach (var (label, power, julia, cxStr, cyStr, scaleStr, iters) in cases)
        {
            FloatExp.TryParse(scaleStr, out FloatExp scale);
            int prec = BigFloat.RequiredBits(scale) + 96;
            BigFloat.TryParseDecimal(cxStr, prec, out BigFloat cx);
            BigFloat.TryParseDecimal(cyStr, prec, out BigFloat cy);

            var formula = new FormulaParams(power, iters, julia, -0.8, 0.156);
            ReferenceOrbit? orbit = ReferenceOrbit.ComputeBest(cx, cy, scale, formula);
            if (orbit == null)
            {
                message = $"failed to build reference orbit for {label}";
                return false;
            }

            // Sample 5 pixels across the deep viewport and compare against exact BigFloat evaluation.
            (double u, double v)[] samples = [(-0.7, -0.5), (-0.2, 0.3), (0.0, 0.0), (0.4, -0.2), (0.8, 0.6)];
            FloatExp refOffX = BigFloat.Sub(cx, orbit.RefRe, prec).ToFloatExp();
            FloatExp refOffY = BigFloat.Sub(cy, orbit.RefIm, prec).ToFloatExp();

            foreach (var (u, v) in samples)
            {
                BigFloat pixRe = BigFloat.Add(cx, BigFloat.FromFloatExp(scale * u), prec);
                BigFloat pixIm = BigFloat.Sub(cy, BigFloat.FromFloatExp(scale * v), prec);
                var exact = ReferenceOrbit.EvaluateExact(pixRe, pixIm, formula, prec);

                FloatExp dcx = refOffX + scale * u;
                FloatExp dcy = refOffY - scale * v;
                var ext = PerturbationFormula.EvaluateExtended(ComplexExp.FromFloatExp(dcx, dcy), orbit, formula, true);
                var dbl = PerturbationFormula.EvaluateDouble(dcx.ToDouble(), dcy.ToDouble(), orbit, formula, true);

                if (exact.Inside != ext.Inside || (!exact.Inside && Math.Abs(exact.Mu - ext.Mu) > 1e-2))
                {
                    message = $"extended perturbation disagreed with BigFloat on {label} ({exact.Mu} vs {ext.Mu})";
                    return false;
                }

                if (exact.Inside != dbl.Inside || (!exact.Inside && Math.Abs(exact.Mu - dbl.Mu) > 1e-2))
                {
                    message = $"double perturbation disagreed with BigFloat on {label} ({exact.Mu} vs {dbl.Mu})";
                    return false;
                }
            }
        }

        message = "deep perturbation + Zhuoran rebasing matches BigFloat at 1e-25 and 1e-80 (n=2,3,4,Julia)";
        return true;
    }

    static bool CursorProbeMatchesFormula(out string message)
    {
        // c = 1: z0=0, z1=1, z2=2, z3=5, z4=26 (> 16 bailout) -> escapes at iter 4 with z = 26 + 0i.
        var c1 = MultibrotFormula.Probe(0, 0, 1, 0, 2, 200);
        if (c1.Inside || c1.Iterations != 4 || Math.Abs(c1.FinalZx - 26.0) > 1e-9 || Math.Abs(c1.FinalZy) > 1e-9)
        {
            message = $"probe at c=1 expected iter 4 z=26+0i, got inside={c1.Inside} iter={c1.Iterations} z={c1.FinalZx}+{c1.FinalZy}i";
            return false;
        }

        // c = -1: period-2 cycle 0, -1, 0, -1... -> stays inside with z_200 = 0 + 0i.
        var cm1 = MultibrotFormula.Probe(0, 0, -1, 0, 2, 200);
        if (!cm1.Inside || cm1.Iterations != 200 || Math.Abs(cm1.FinalZx) > 1e-9 || Math.Abs(cm1.FinalZy) > 1e-9)
        {
            message = $"probe at c=-1 expected inside z=0+0i, got inside={cm1.Inside} iter={cm1.Iterations} z={cm1.FinalZx}+{cm1.FinalZy}i";
            return false;
        }

        // Parity against MultibrotFormula.Evaluate across integer, fractional, and Julia cases.
        (double zx, double zy, double cx, double cy, double power)[] cases =
        [
            (0, 0, 0.4, 0.5, 2),
            (0, 0, -0.5, 0.1, 2),
            (0, 0, 0.5, 0.6, 3),
            (0, 0, 1.2, 1.2, 1.4),
            (0.2, 0.3, -0.8, 0.156, 2),
        ];

        foreach (var (zx, zy, cx, cy, power) in cases)
        {
            var eval = MultibrotFormula.Evaluate(zx, zy, cx, cy, power, 120);
            var probe = MultibrotFormula.Probe(zx, zy, cx, cy, power, 120);
            if (eval.Inside != probe.Inside || (!eval.Inside && Math.Abs(eval.Mu - probe.Mu) > 1e-5))
            {
                message = $"probe disagreed with Evaluate at n={power} ({eval.Mu} vs {probe.Mu})";
                return false;
            }
        }

        // Deep-zoom perturbation probe parity at 1e-25 (double + extended).
        FloatExp.TryParse("1e-25", out FloatExp scale);
        int prec = BigFloat.RequiredBits(scale) + 96;
        BigFloat.TryParseDecimal("-0.743643887037158704752191506114774", prec, out BigFloat refX);
        BigFloat.TryParseDecimal("0.131825904205311970493132056385139", prec, out BigFloat refY);
        var formula = new FormulaParams(2, 1200, false, -0.8, 0.156);
        ReferenceOrbit? orbit = ReferenceOrbit.ComputeBest(refX, refY, scale, formula);
        if (orbit == null)
        {
            message = "probe deep-zoom test failed to build reference orbit";
            return false;
        }

        FloatExp dcx = BigFloat.Sub(refX, orbit.RefRe, prec).ToFloatExp() + scale * 0.4;
        FloatExp dcy = BigFloat.Sub(refY, orbit.RefIm, prec).ToFloatExp() - scale * 0.2;
        var evalDeep = PerturbationFormula.EvaluateDouble(dcx.ToDouble(), dcy.ToDouble(), orbit, formula, true);
        var probeDbl = PerturbationFormula.ProbeDouble(dcx.ToDouble(), dcy.ToDouble(), orbit, formula);
        var probeExt = PerturbationFormula.ProbeExtended(ComplexExp.FromFloatExp(dcx, dcy), orbit, formula);

        if (evalDeep.Inside != probeDbl.Inside
            || evalDeep.Inside != probeExt.Inside
            || (!evalDeep.Inside && (Math.Abs(evalDeep.Mu - probeDbl.Mu) > 1e-4 || Math.Abs(evalDeep.Mu - probeExt.Mu) > 1e-4)))
        {
            message = $"deep probe disagreed with perturbation evaluate ({evalDeep.Mu} vs {probeDbl.Mu} / {probeExt.Mu})";
            return false;
        }

        message = "cursor orbit probe matches scalar and deep-zoom perturbation formulas";
        return true;
    }

    static bool ShadedLightingAndOrbitTrapValid(out string message)
    {
        const int width = 32;
        const int height = 16;
        var formula = new FormulaParams(2, 160, false, -0.8, 0.156);
        var mu = new float[width * height];
        var minR = new float[width * height];
        var trap = new float[width * height];
        var nx = new float[width * height];
        var ny = new float[width * height];

        double halfH = 1.35;
        double halfW = halfH * width / height;

        for (int y = 0; y < height; y++)
            MultibrotFormula.EvaluateShadedRow(width, height, y, -0.5, 0.0, halfW, halfH, formula, mu, minR, trap, nx, ny, true, true, (int)OrbitTrapMode.Cross);

        int escapedChecked = 0;
        for (int y = 0; y < height / 2; y++)
        {
            int mirror = height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                int a = y * width + x;
                int b = mirror * width + x;
                if (mu[a] >= 0)
                {
                    escapedChecked++;
                    float len = MathF.Sqrt(nx[a] * nx[a] + ny[a] * ny[a]);
                    if (MathF.Abs(len - 1f) > 1e-3f)
                    {
                        message = $"normal at ({x},{y}) was not unit length ({len})";
                        return false;
                    }

                    if (MathF.Abs(nx[a] - nx[b]) > 1e-3f || MathF.Abs(ny[a] + ny[b]) > 1e-3f)
                    {
                        message = $"normal conjugate symmetry failed at ({x},{y})";
                        return false;
                    }

                    if (trap[a] < 0f || !float.IsFinite(trap[a]) || MathF.Abs(trap[a] - trap[b]) > 1e-3f)
                    {
                        message = $"orbit trap symmetry/validity failed at ({x},{y}) ({trap[a]} vs {trap[b]})";
                        return false;
                    }
                }
            }
        }

        if (escapedChecked == 0)
        {
            message = "shaded test had no escaped pixels";
            return false;
        }

        // Verify deep-zoom perturbation shaded row at 1e-25 produces finite unit normals and trap distances.
        FloatExp.TryParse("1e-25", out FloatExp scale);
        int prec = BigFloat.RequiredBits(scale) + 96;
        BigFloat.TryParseDecimal("-0.743643887037158704752191506114774", prec, out BigFloat refX);
        BigFloat.TryParseDecimal("0.131825904205311970493132056385139", prec, out BigFloat refY);
        var deepFormula = new FormulaParams(2, 1200, false, -0.8, 0.156);
        ReferenceOrbit? orbit = ReferenceOrbit.ComputeBest(refX, refY, scale, deepFormula);
        if (orbit == null)
        {
            message = "shaded deep-zoom test failed to build reference orbit";
            return false;
        }

        FloatExp refOffX = BigFloat.Sub(refX, orbit.RefRe, prec).ToFloatExp();
        FloatExp refOffY = BigFloat.Sub(refY, orbit.RefIm, prec).ToFloatExp();
        FloatExp deepHalfW = scale * (width / (double)height);
        var dMu = new float[width * height];
        var dMinR = new float[width * height];
        var dTrap = new float[width * height];
        var dNx = new float[width * height];
        var dNy = new float[width * height];

        PerturbationFormula.EvaluateShadedRow(width, height, height / 2, refOffX, refOffY, deepHalfW, scale, orbit, deepFormula, dMu, dMinR, dTrap, dNx, dNy, true, true, (int)OrbitTrapMode.Circle);
        int rowOff = (height / 2) * width;
        for (int x = 0; x < width; x++)
        {
            int idx = rowOff + x;
            if (dMu[idx] >= 0)
            {
                float len = MathF.Sqrt(dNx[idx] * dNx[idx] + dNy[idx] * dNy[idx]);
                if (MathF.Abs(len - 1f) > 1e-3f || !float.IsFinite(dTrap[idx]) || dTrap[idx] < 0f)
                {
                    message = $"deep shaded row invalid at x={x} (|N|={len}, trap={dTrap[idx]})";
                    return false;
                }
            }
        }

        message = "3D normal-map lighting and orbit traps valid across scalar and deep-zoom perturbation";
        return true;
    }

    static bool FractalFamiliesValid(out string message)
    {
        FractalFamily[] nonDefault =
        [
            FractalFamily.BurningShip,
            FractalFamily.Tricorn,
            FractalFamily.Celtic,
            FractalFamily.Buffalo,
        ];

        if (!FormulaParams.SupportsPerturbation(new FormulaParams(2, 128, false, -0.8, 0.156, FractalFamily.Multibrot), out _))
        {
            message = "Multibrot n=2 should support deep perturbation";
            return false;
        }

        const int width = 24;
        const int height = 16;
        var mu = new float[width * height];
        var minR = new float[width * height];
        var trap = new float[width * height];
        var nx = new float[width * height];
        var ny = new float[width * height];

        foreach (FractalFamily family in nonDefault)
        {
            var formula = new FormulaParams(2, 160, false, -0.8, 0.156, family);
            if (FormulaParams.SupportsPerturbation(formula, out _))
            {
                message = $"{family} should not claim Multibrot perturbation support";
                return false;
            }

            if (!MultibrotFormula.Evaluate(0, 0, 0, 0, 2, 160, true, family).Inside)
            {
                message = $"{family} origin should be inside";
                return false;
            }

            if (MultibrotFormula.Evaluate(0, 0, 2, 2, 2, 160, true, family).Inside)
            {
                message = $"{family} (2, 2) should escape";
                return false;
            }

            // Probe parity for integer and fractional powers.
            foreach (double power in new[] { 2.0, 3.0, 2.35 })
            {
                var eval = MultibrotFormula.Evaluate(0, 0, -0.65, 0.45, power, 120, true, family);
                var probe = MultibrotFormula.Probe(0, 0, -0.65, 0.45, power, 120, family);
                if (eval.Inside != probe.Inside || (!eval.Inside && Math.Abs(eval.Mu - probe.Mu) > 1e-5))
                {
                    message = $"{family} probe disagreed with Evaluate at n={power} ({eval.Mu} vs {probe.Mu})";
                    return false;
                }
            }

            // Shaded row with 2x2 real Jacobian normal lighting produces unit normals.
            formula.GetDefaultView(out double cx, out double cy, out double scale);
            double halfW = scale * width / height;
            int escaped = 0;
            for (int y = 0; y < height; y++)
                MultibrotFormula.EvaluateShadedRow(width, height, y, cx, cy, halfW, scale, formula, mu, minR, trap, nx, ny, true, true, (int)OrbitTrapMode.Point);

            for (int i = 0; i < width * height; i++)
            {
                if (mu[i] >= 0)
                {
                    escaped++;
                    float len = MathF.Sqrt(nx[i] * nx[i] + ny[i] * ny[i]);
                    if (MathF.Abs(len - 1f) > 1e-3f || !float.IsFinite(trap[i]) || trap[i] < 0f)
                    {
                        message = $"{family} shaded pixel {i} invalid (|N|={len}, trap={trap[i]})";
                        return false;
                    }
                }
            }

            if (escaped == 0)
            {
                message = $"{family} shaded test had no escaped pixels";
                return false;
            }
        }

        // Verify Burning Ship asymmetry vs Tricorn/Celtic real-axis symmetry.
        var bsPos = MultibrotFormula.Evaluate(0, 0, -1.75, 0.03, 2, 160, true, FractalFamily.BurningShip);
        var bsNeg = MultibrotFormula.Evaluate(0, 0, -1.75, -0.03, 2, 160, true, FractalFamily.BurningShip);
        if (bsPos.Inside == bsNeg.Inside && Math.Abs(bsPos.Mu - bsNeg.Mu) < 1e-3)
        {
            message = "Burning Ship should be asymmetric across the real axis at (-1.75, +/-0.03)";
            return false;
        }

        var trPos = MultibrotFormula.Evaluate(0, 0, -0.6, 0.45, 2, 160, true, FractalFamily.Tricorn);
        var trNeg = MultibrotFormula.Evaluate(0, 0, -0.6, -0.45, 2, 160, true, FractalFamily.Tricorn);
        if (trPos.Inside != trNeg.Inside || Math.Abs(trPos.Mu - trNeg.Mu) > 1e-5)
        {
            message = "Tricorn should be symmetric across the real axis";
            return false;
        }

        var cePos = MultibrotFormula.Evaluate(0, 0, -0.6, 0.45, 2, 160, true, FractalFamily.Celtic);
        var ceNeg = MultibrotFormula.Evaluate(0, 0, -0.6, -0.45, 2, 160, true, FractalFamily.Celtic);
        if (cePos.Inside != ceNeg.Inside || Math.Abs(cePos.Mu - ceNeg.Mu) > 1e-5)
        {
            message = "Celtic should be symmetric across the real axis";
            return false;
        }

        message = "all 5 fractal families valid across scalar, probe, symmetry, and 2x2 Jacobian lighting";
        return true;
    }
}
