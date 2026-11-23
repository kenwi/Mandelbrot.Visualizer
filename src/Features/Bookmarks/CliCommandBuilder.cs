using System.Globalization;
using System.Text;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Bookmarks;

/// <summary>
/// Builds a reproducible CLI command that launches the visualizer directly at the
/// current deep-zoom viewport, fractal formula, coloring, and render configuration.
/// </summary>
public static class CliCommandBuilder
{
    public const string DefaultCommandPrefix = "dotnet run --project src/Mandelbrot.Visualizer.csproj --";

    public static string Build(
        ViewportState view,
        FormulaParams formula,
        ColoringParams coloring,
        bool useGpu,
        SsaaMode ssaa)
    {
        var sb = new StringBuilder(DefaultCommandPrefix);

        if (formula.Family != FractalFamily.Multibrot)
            sb.Append(" --fractal ").Append(formula.Family);

        if (Math.Abs(formula.Power - 2.0) > 1e-9)
            sb.Append(" --power ").Append(formula.Power.ToString("G12", CultureInfo.InvariantCulture));

        int digits = BigFloat.RecommendedDecimalDigits(view.ScaleExact);
        string cx = view.CenterXExact.ToDecimalString(digits);
        string cy = view.CenterYExact.ToDecimalString(digits);
        string scale = view.ScaleExact.ToScientificString();

        sb.Append(" --center ").Append(cx).Append(',').Append(cy);
        sb.Append(" --scale ").Append(scale);

        if (formula.Julia)
        {
            string jRe = formula.JuliaRe.ToString("G12", CultureInfo.InvariantCulture);
            string jIm = formula.JuliaIm.ToString("G12", CultureInfo.InvariantCulture);
            sb.Append(" --julia ").Append(jRe).Append(',').Append(jIm);
        }

        if (formula.MaxIterations != FormulaParams.Default.MaxIterations)
            sb.Append(" --iter ").Append(formula.MaxIterations.ToString(CultureInfo.InvariantCulture));

        if (coloring.Palette != 0)
            sb.Append(" --palette ").Append(Palette.NameOf(coloring.Palette));

        if (coloring.Mode != ColoringMode.Cyclic)
            sb.Append(" --coloring ").Append(coloring.Mode);

        if (Math.Abs(coloring.Density - 1.0) > 0.005)
            sb.Append(" --density ").Append(coloring.Density.ToString("0.###", CultureInfo.InvariantCulture));

        if (Math.Abs(coloring.Phase) > 0.005)
            sb.Append(" --phase ").Append(coloring.Phase.ToString("0.###", CultureInfo.InvariantCulture));

        if (coloring.Interior != InteriorStyle.Black)
            sb.Append(" --interior ").Append(coloring.Interior);

        if (coloring.OrbitTrap != OrbitTrapMode.None)
            sb.Append(" --trap ").Append(coloring.OrbitTrap);

        if (coloring.NormalLighting)
        {
            if (coloring.CursorLight)
                sb.Append(" --mouse-light");
            else
                sb.Append(" --lighting --angle ").Append(coloring.LightAngle.ToString("0.#", CultureInfo.InvariantCulture));
        }

        if (ssaa != SsaaMode.Off)
            sb.Append(" --ssaa ").Append(SsaaPattern.ShortLabel(ssaa));

        if (!useGpu)
            sb.Append(" --cpu");

        return sb.ToString();
    }
}
