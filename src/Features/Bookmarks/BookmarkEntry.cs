using System.Globalization;
using System.Text.Json.Serialization;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Bookmarks;

/// <summary>
/// Serializable bookmark representing either a curated fractal landmark or a user-saved spot.
/// Stores exact arbitrary-precision coordinates and extended-exponent scale as strings so
/// deep-zoom locations (1e-30, 1e-80+) round-trip cleanly through bookmarks.json.
/// </summary>
public sealed record BookmarkEntry
{
    public string Name { get; init; } = "";
    public bool Curated { get; init; }
    public string Category { get; init; } = "Saved";

    public FractalFamily Family { get; init; } = FractalFamily.Multibrot;
    public double Power { get; init; } = 2.0;
    public int MaxIterations { get; init; } = 250;
    public bool AutoIterations { get; init; }

    public bool Julia { get; init; }
    public double JuliaRe { get; init; } = -0.8;
    public double JuliaIm { get; init; } = 0.156;

    public string CenterX { get; init; } = "-0.5";
    public string CenterY { get; init; } = "0";
    public string Scale { get; init; } = "1.35";

    public string Palette { get; init; } = "Classic";
    public ColoringMode ColoringMode { get; init; } = ColoringMode.Cyclic;
    public bool Smooth { get; init; } = true;
    public double Density { get; init; } = 1.0;
    public double Phase { get; init; }
    public InteriorStyle Interior { get; init; } = InteriorStyle.Black;
    public bool NormalLighting { get; init; }
    public double LightAngle { get; init; } = 135.0;
    public OrbitTrapMode OrbitTrap { get; init; } = OrbitTrapMode.None;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed)" : Name;

    public bool TryParseView(out BigFloat centerX, out BigFloat centerY, out FloatExp scale)
    {
        centerX = BigFloat.Zero;
        centerY = BigFloat.Zero;
        if (!FloatExp.TryParse(Scale, out scale) || scale <= FloatExp.Zero)
            return false;

        int prec = BigFloat.RequiredBits(scale) + 64;
        return BigFloat.TryParseDecimal(CenterX, prec, out centerX)
            && BigFloat.TryParseDecimal(CenterY, prec, out centerY);
    }

    public FormulaParams ToFormulaParams()
    {
        double clampedPower = FormulaParams.ClampNonZeroPower(Power);
        int clampedIter = FormulaParams.ClampIterations(MaxIterations);
        return new FormulaParams(clampedPower, clampedIter, Julia, JuliaRe, JuliaIm, Family);
    }

    public ColoringParams ToColoringParams()
    {
        int paletteIndex = Coloring.Palette.TryParseName(Palette, out int parsed) ? parsed : 0;
        double normalizedAngle = ((LightAngle % 360.0) + 360.0) % 360.0;
        return new ColoringParams(
            paletteIndex,
            ColoringMode,
            Smooth,
            Interior,
            Math.Clamp(Density, 0.25, 4.0),
            Math.Clamp(Phase, 0.0, 1.0),
            NormalLighting,
            OrbitTrap,
            CursorLight: false,
            LightAngle: normalizedAngle);
    }

    public static BookmarkEntry FromCurrentState(
        string? name,
        ViewportState view,
        FormulaParams formula,
        bool autoIterations,
        ColoringParams coloring)
    {
        int digits = BigFloat.RecommendedDecimalDigits(view.ScaleExact);
        string resolvedName = string.IsNullOrWhiteSpace(name)
            ? BuildFallbackName(view, formula)
            : name.Trim();

        return new BookmarkEntry
        {
            Name = resolvedName,
            Curated = false,
            Category = "Saved",
            Family = formula.Family,
            Power = formula.Power,
            MaxIterations = formula.MaxIterations,
            AutoIterations = autoIterations,
            Julia = formula.Julia,
            JuliaRe = formula.JuliaRe,
            JuliaIm = formula.JuliaIm,
            CenterX = view.CenterXExact.ToDecimalString(digits),
            CenterY = view.CenterYExact.ToDecimalString(digits),
            Scale = view.ScaleExact.ToScientificString(),
            Palette = Coloring.Palette.NameOf(coloring.Palette),
            ColoringMode = coloring.Mode,
            Smooth = coloring.Smooth,
            Density = Math.Round(coloring.Density, 3),
            Phase = Math.Round(coloring.Phase, 3),
            Interior = coloring.Interior,
            NormalLighting = coloring.NormalLighting,
            LightAngle = Math.Round(coloring.LightAngle, 1),
            OrbitTrap = coloring.OrbitTrap,
        };
    }

    public static string BuildFallbackName(ViewportState view, FormulaParams formula)
    {
        string familyLabel = FormatFamilyShort(formula);
        formula.GetDefaultView(out _, out _, out double refScale);
        string zoom = view.ScaleExact.FormatZoom(refScale);

        if (formula.Julia)
        {
            string cRe = formula.JuliaRe.ToString("0.###", CultureInfo.InvariantCulture);
            string cIm = formula.JuliaIm.ToString("0.###", CultureInfo.InvariantCulture);
            return $"{familyLabel} Julia c=({cRe},{cIm}) {zoom}";
        }

        string cx = view.CenterXExact.FormatSummary();
        string cy = view.CenterYExact.FormatSummary();
        return $"{familyLabel} ({cx},{cy}) {zoom}";
    }

    static string FormatFamilyShort(FormulaParams formula) => formula.Family switch
    {
        FractalFamily.BurningShip => "Burning Ship",
        FractalFamily.Tricorn => "Tricorn",
        FractalFamily.Celtic => "Celtic",
        FractalFamily.Buffalo => "Buffalo",
        _ => Math.Abs(formula.Power - 2.0) < 1e-6
            ? "Mandelbrot"
            : $"Multibrot^{formula.Power.ToString("0.##", CultureInfo.InvariantCulture)}",
    };
}
