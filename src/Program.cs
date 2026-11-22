using Mandelbrot.Visualizer.App;
using Mandelbrot.Visualizer.Formula;

if (args.Contains("--self-test"))
    return SelfTest() ? 0 : 1;

string? screenshot = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--screenshot")
        screenshot = args[i + 1];
}

using var host = new VisualizerHost(screenshot);
host.Run();
return 0;

static bool SelfTest()
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
        Console.WriteLine("pass  integer and real power agree on 0.2+0.55i");
    else
    {
        Console.WriteLine("fail  integer and real power disagree on 0.2+0.55i");
        ok = false;
    }

    if (SimdMultibrot.MatchesScalar(out string simd))
        Console.WriteLine($"pass  {simd}");
    else
    {
        Console.WriteLine($"fail  {simd}");
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
