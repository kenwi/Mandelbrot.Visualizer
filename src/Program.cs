using System.Globalization;
using System.Numerics;
using Mandelbrot.Visualizer.App;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Diagnostics;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Mandelbulb;
using Mandelbrot.Visualizer.Features.Rendering;
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
bool juliaPreview = false;
double? previewCRe = null;
double? previewCIm = null;
bool matchZoom = false;
SsaaMode? ssaa = null;
bool? julia = null;
double? juliaRe = null;
double? juliaIm = null;
int? palette = null;
ColoringMode? coloringMode = null;
double? density = null;
double? phase = null;
string? bookmarkName = null;
bool mandelbulb = false;
OrbitCameraState? bulbCamera = null;
double? bulbDetail = null;
Vector3? bulbJuliaC = null;
bool? bulbShadows = null;
bool bulbFly = false;
Vector3? bulbPos = null;
Vector3? bulbYprDeg = null;
Vector3? bulbTarget = null;
string? bulbSpotName = null;
BulbShape? bulbShape = null;
double? boxScale = null;
double? boxMinRadius = null;
double? boxFoldLimit = null;
BulbQuality? bulbQuality = null;
double? lightElevation = null;
bool? headlamp = null;
double? fogDensity = null;
double? roughness = null;
double? metallic = null;
double? glowStrength = null;
double? dofAperture = null;
double? dofFocusDistance = null;
BulbPanelTab? bulbTab = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--screenshot" when i + 1 < args.Length:
            screenshot = args[++i];
            break;
        case "--bookmark" when i + 1 < args.Length:
            bookmarkName = args[++i];
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
        case "--julia" when i + 1 < args.Length:
            string[] jparts = args[++i].Split(',', 2);
            if (jparts.Length == 2
                && double.TryParse(jparts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double jre)
                && double.TryParse(jparts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double jim))
            {
                julia = true;
                juliaRe = jre;
                juliaIm = jim;
            }
            break;
        case "--palette" when i + 1 < args.Length:
            if (Palette.TryParseName(args[++i], out int parsedPalette))
                palette = parsedPalette;
            break;
        case "--coloring" when i + 1 < args.Length:
            if (Enum.TryParse<ColoringMode>(args[++i], true, out ColoringMode parsedColoring))
                coloringMode = parsedColoring;
            break;
        case "--density" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDensity))
                density = parsedDensity;
            break;
        case "--phase" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedPhase))
                phase = parsedPhase;
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
        case "--julia-preview":
            juliaPreview = true;
            break;
        case "--match-zoom":
            matchZoom = true;
            break;
        case "--ssaa" when i + 1 < args.Length:
            if (SsaaPattern.TryParse(args[++i], out SsaaMode parsedSsaa))
                ssaa = parsedSsaa;
            break;
        case "--preview-c" when i + 1 < args.Length:
            string[] pparts = args[++i].Split(',', 2);
            if (pparts.Length == 2
                && double.TryParse(pparts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double pre)
                && double.TryParse(pparts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pim))
            {
                juliaPreview = true;
                previewCRe = pre;
                previewCIm = pim;
            }
            break;
        case "--mandelbulb":
        case "--3d-bulb":
            mandelbulb = true;
            break;
        case "--mandelbox":
        case "--3d-box":
            mandelbulb = true;
            bulbShape = BulbShape.Mandelbox;
            break;
        case "--box-scale" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedBoxScale))
            {
                mandelbulb = true;
                bulbShape = BulbShape.Mandelbox;
                boxScale = parsedBoxScale;
            }
            break;
        case "--box-min-radius" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedBoxMinR))
            {
                mandelbulb = true;
                bulbShape = BulbShape.Mandelbox;
                boxMinRadius = parsedBoxMinR;
            }
            break;
        case "--box-fold" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedBoxFold))
            {
                mandelbulb = true;
                bulbShape = BulbShape.Mandelbox;
                boxFoldLimit = parsedBoxFold;
            }
            break;
        case "--bulb-camera" when i + 1 < args.Length:
            string[] cparts = args[++i].Split(',', 3);
            if (cparts.Length == 3
                && float.TryParse(cparts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw)
                && float.TryParse(cparts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch)
                && float.TryParse(cparts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float dist))
            {
                mandelbulb = true;
                bulbCamera = OrbitCameraState.Default with { Yaw = yaw, Pitch = pitch, Distance = dist };
            }
            break;
        case "--bulb-detail" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDetail))
            {
                mandelbulb = true;
                bulbDetail = parsedDetail;
            }
            break;
        case "--bulb-julia" when i + 1 < args.Length:
            string[] bparts = args[++i].Split(',', 3);
            if (bparts.Length == 3
                && float.TryParse(bparts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float bx)
                && float.TryParse(bparts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float by)
                && float.TryParse(bparts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float bz))
            {
                mandelbulb = true;
                bulbJuliaC = new Vector3(bx, by, bz);
            }
            break;
        case "--no-shadows":
            bulbShadows = false;
            break;
        case "--fly":
        case "--bulb-fly":
            mandelbulb = true;
            bulbFly = true;
            break;
        case "--bulb-pos" when i + 1 < args.Length:
            string[] posParts = args[++i].Split(',', 3);
            if (posParts.Length == 3
                && float.TryParse(posParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                && float.TryParse(posParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float py)
                && float.TryParse(posParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float pz))
            {
                mandelbulb = true;
                bulbFly = true;
                bulbPos = new Vector3(px, py, pz);
            }
            break;
        case "--bulb-ypr" when i + 1 < args.Length:
            string[] yprParts = args[++i].Split(',', 3);
            if (yprParts.Length == 3
                && float.TryParse(yprParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float byaw)
                && float.TryParse(yprParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float bpitch)
                && float.TryParse(yprParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float broll))
            {
                mandelbulb = true;
                bulbFly = true;
                bulbYprDeg = new Vector3(byaw, bpitch, broll);
            }
            break;
        case "--bulb-target" when i + 1 < args.Length:
            string[] tgtParts = args[++i].Split(',', 3);
            if (tgtParts.Length == 3
                && float.TryParse(tgtParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float tx)
                && float.TryParse(tgtParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ty)
                && float.TryParse(tgtParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float tz))
            {
                mandelbulb = true;
                bulbTarget = new Vector3(tx, ty, tz);
            }
            break;
        case "--bulb-spot" when i + 1 < args.Length:
        case "--bulb-bookmark" when i + 1 < args.Length:
            mandelbulb = true;
            bulbSpotName = args[++i];
            break;
        case "--bulb-quality" when i + 1 < args.Length:
            if (Enum.TryParse<BulbQuality>(args[++i], true, out BulbQuality parsedQuality))
                bulbQuality = parsedQuality;
            break;
        case "--light-el" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedEl))
                lightElevation = parsedEl;
            break;
        case "--headlamp":
            headlamp = true;
            break;
        case "--fog" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedFog))
                fogDensity = parsedFog;
            break;
        case "--roughness" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedRough))
                roughness = parsedRough;
            break;
        case "--metallic" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedMetal))
                metallic = parsedMetal;
            break;
        case "--glow" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedGlow))
                glowStrength = parsedGlow;
            break;
        case "--dof" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDof))
                dofAperture = parsedDof;
            break;
        case "--focus" when i + 1 < args.Length:
            if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedFocus))
                dofFocusDistance = parsedFocus;
            break;
        case "--bulb-fx-tab":
            bulbTab = BulbPanelTab.LookFx;
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

var options = new LaunchOptions(
    screenshot,
    centerX,
    centerY,
    scale,
    power,
    maxIter,
    forceCpu,
    normalLighting,
    orbitTrap,
    interior,
    cursorLight,
    lightX,
    lightY,
    lightAngle,
    fullscreen,
    family,
    juliaPreview,
    previewCRe,
    previewCIm,
    matchZoom,
    ssaa,
    julia,
    juliaRe,
    juliaIm,
    palette,
    coloringMode,
    density,
    phase,
    bookmarkName,
    mandelbulb,
    bulbCamera,
    bulbDetail,
    bulbJuliaC,
    bulbShadows,
    bulbFly,
    bulbPos,
    bulbYprDeg,
    bulbTarget,
    bulbSpotName,
    bulbShape,
    boxScale,
    boxMinRadius,
    boxFoldLimit,
    bulbQuality,
    lightElevation,
    headlamp,
    fogDensity,
    roughness,
    metallic,
    glowStrength,
    dofAperture,
    dofFocusDistance,
    bulbTab);
using var host = new VisualizerHost(options);
host.Run();
return 0;
