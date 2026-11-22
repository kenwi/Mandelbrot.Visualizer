using System.Globalization;
using Mandelbrot.Visualizer.App;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Diagnostics;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Shared;

if (args.Contains("--self-test"))
    return SelfTest.Run() ? 0 : 1;

string? screenshot = null;
string? centerArg = null;
FloatExp? scale = null;
double? power = null;
int? maxIter = null;
bool forceCpu = false;
bool normalLighting = false;
bool cursorLight = false;
float? lightX = null;
float? lightY = null;
double? lightAngle = null;
bool fullscreen = false;
OrbitTrapMode orbitTrap = OrbitTrapMode.None;
InteriorStyle? interior = null;
FractalFamily? family = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--screenshot" when i + 1 < args.Length:
            screenshot = args[++i];
            break;
        case "--center" when i + 1 < args.Length:
            centerArg = args[++i];
            break;
        case "--scale" when i + 1 < args.Length:
            if (FloatExp.TryParse(args[++i], out FloatExp parsedScale))
                scale = parsedScale;
            break;
        case "--power" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedPower))
                power = parsedPower;
            break;
        case "--iter" when i + 1 < args.Length:
            if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIter))
                maxIter = parsedIter;
            break;
        case "--cpu":
            forceCpu = true;
            break;
        case "--fullscreen":
            fullscreen = true;
            break;
        case "--lighting":
        case "--3d":
            normalLighting = true;
            break;
        case "--mouse-light":
            normalLighting = true;
            cursorLight = true;
            break;
        case "--light" when i + 1 < args.Length:
            string[] lparts = args[++i].Split(',', 2);
            if (lparts.Length == 2
                && float.TryParse(lparts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float lx)
                && float.TryParse(lparts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ly))
            {
                normalLighting = true;
                cursorLight = true;
                lightX = lx;
                lightY = ly;
            }
            break;
        case "--angle" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAngle))
            {
                normalLighting = true;
                lightAngle = parsedAngle;
            }
            break;
        case "--trap" when i + 1 < args.Length:
            if (Enum.TryParse<OrbitTrapMode>(args[++i], true, out OrbitTrapMode parsedTrap))
                orbitTrap = parsedTrap;
            break;
        case "--interior" when i + 1 < args.Length:
            if (Enum.TryParse<InteriorStyle>(args[++i], true, out InteriorStyle parsedInterior))
                interior = parsedInterior;
            break;
        case "--fractal" when i + 1 < args.Length:
            if (Enum.TryParse<FractalFamily>(args[++i], true, out FractalFamily parsedFamily))
                family = parsedFamily;
            break;
    }
}

BigFloat? centerX = null;
BigFloat? centerY = null;
if (centerArg != null)
{
    string[] parts = centerArg.Split(',', 2);
    int prec = scale.HasValue ? BigFloat.RequiredBits(scale.Value) + 64 : 256;
    if (parts.Length == 2
        && BigFloat.TryParseDecimal(parts[0], prec, out BigFloat re)
        && BigFloat.TryParseDecimal(parts[1], prec, out BigFloat im))
    {
        centerX = re;
        centerY = im;
    }
}

var options = new LaunchOptions(screenshot, centerX, centerY, scale, power, maxIter, forceCpu, normalLighting, orbitTrap, interior, cursorLight, lightX, lightY, lightAngle, fullscreen, family);
using var host = new VisualizerHost(options);
host.Run();
return 0;
