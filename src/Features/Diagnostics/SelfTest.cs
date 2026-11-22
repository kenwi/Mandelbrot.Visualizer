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
}
