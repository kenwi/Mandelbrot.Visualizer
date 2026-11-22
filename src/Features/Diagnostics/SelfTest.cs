using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;

namespace Mandelbrot.Visualizer.Features.Diagnostics;

/// <summary>
/// Headless --self-test suite validating formula accuracy, SIMD-vs-scalar parity,
/// and palette LUT endpoint behavior.
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
        if (square.Inside == almost.Inside)
        {
            Console.WriteLine("pass  integer and real power agree on 0.2+0.55i");
        }
        else
        {
            Console.WriteLine("fail  integer and real power disagree on 0.2+0.55i");
            ok = false;
        }

        if (SimdMatchesScalar(out string simd))
        {
            Console.WriteLine($"pass  {simd}");
        }
        else
        {
            Console.WriteLine($"fail  {simd}");
            ok = false;
        }

        if (PaletteEndpointsConsistent(out string paletteMessage))
        {
            Console.WriteLine($"pass  {paletteMessage}");
        }
        else
        {
            Console.WriteLine($"fail  {paletteMessage}");
            ok = false;
        }

        Console.WriteLine(ok ? "self-test passed" : "self-test failed");
        return ok;
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
            const int width = 13;
            const int height = 9;
            const int iterations = 80;
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
                MultibrotFormula.EvaluateScalarRow(width, height, y, originX, 0, halfW, halfH, formula, scalarMu, scalarRadius);
                SimdMultibrot.EvaluateRow(width, height, y, originX, 0, halfW, halfH, formula, vectorMu, vectorRadius);
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
}
