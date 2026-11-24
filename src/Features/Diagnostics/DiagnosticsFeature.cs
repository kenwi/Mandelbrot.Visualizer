using System.Globalization;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Diagnostics;

/// <summary>
/// Vertical slice owning frame-time telemetry toggle/formatting, cursor orbit probing,
/// status readout, and the minimal on-canvas HUD shown when the side panel is hidden.
/// </summary>
public sealed class DiagnosticsFeature
{
    static readonly FloatExp DoubleUnderflowThreshold = FloatExp.FromDouble(1e-280);

    readonly record struct ProbeCacheKey(
        int PixelX,
        int PixelY,
        int CanvasWidth,
        int CanvasHeight,
        ViewportState View,
        FormulaParams Formula,
        ReferenceOrbit? Orbit);

    ProbeCacheKey? _cachedProbeKey;
    MultibrotFormula.OrbitProbe _cachedProbe;

    public bool ShowFrameTime { get; private set; }

    public void SetShowFrameTime(bool value) => ShowFrameTime = value;

    public bool DrawToggle(ref PanelLayout layout)
    {
        bool show = ShowFrameTime;
        if (!layout.Check("Frame time", ref show))
            return false;

        ShowFrameTime = show;
        return true;
    }

    public void DrawCursorProbe(
        ref PanelLayout layout,
        int canvasWidth,
        int canvasHeight,
        NavigationFeature navigation,
        in FormulaParams formula,
        RenderingFeature rendering)
    {
        if (!navigation.TryGetHoveredPixel(canvasWidth, canvasHeight, out int px, out int py))
        {
            layout.Label("Value   -");
            layout.Label("Final z  -");
            return;
        }

        ViewportState view = navigation.State;
        bool wantsPerturb = view.IsDeepZoom && FormulaParams.SupportsPerturbation(formula, out _);
        ReferenceOrbit? orbit = wantsPerturb ? rendering.ActiveOrbit : null;

        if (wantsPerturb && (orbit == null || !orbit.IsReusableFor(view.CenterXExact, view.CenterYExact, view.ScaleExact, formula)))
        {
            layout.Label("Value   computing...");
            layout.Label("Final z  -");
            return;
        }

        var key = new ProbeCacheKey(px, py, canvasWidth, canvasHeight, view, formula, orbit);
        if (_cachedProbeKey != key)
        {
            _cachedProbe = ProbePixel(px, py, canvasWidth, canvasHeight, view, formula, orbit);
            _cachedProbeKey = key;
        }

        layout.Label($"Value   {FormatProbeValue(_cachedProbe)}");
        layout.Label($"Final z  {FormatComplex(_cachedProbe.FinalZx, _cachedProbe.FinalZy)}");
    }

    static MultibrotFormula.OrbitProbe ProbePixel(
        int px,
        int py,
        int canvasWidth,
        int canvasHeight,
        in ViewportState view,
        in FormulaParams formula,
        ReferenceOrbit? orbit)
    {
        double u = ((px + 0.5) / Math.Max(canvasWidth, 1) - 0.5) * 2.0;
        double v = ((py + 0.5) / Math.Max(canvasHeight, 1) - 0.5) * 2.0;

        if (orbit != null)
        {
            int prec = Math.Max(orbit.PrecisionBits, BigFloat.RequiredBits(view.ScaleExact));
            FloatExp refOffX = BigFloat.Sub(view.CenterXExact, orbit.RefRe, prec).ToFloatExp();
            FloatExp refOffY = BigFloat.Sub(view.CenterYExact, orbit.RefIm, prec).ToFloatExp();
            FloatExp halfH = view.ScaleExact;
            FloatExp halfW = view.HalfWidthExact(canvasWidth, canvasHeight);
            FloatExp dcx = refOffX + halfW * u;
            FloatExp dcy = refOffY - halfH * v;

            return halfH <= DoubleUnderflowThreshold
                ? PerturbationFormula.ProbeExtended(ComplexExp.FromFloatExp(dcx, dcy), orbit, formula)
                : PerturbationFormula.ProbeDouble(dcx.ToDouble(), dcy.ToDouble(), orbit, formula);
        }

        double halfHD = view.Scale;
        double halfWD = view.HalfWidth(canvasWidth, canvasHeight);
        double cx = view.CenterX + u * halfWD;
        double cy = view.CenterY - v * halfHD;

        return formula.Julia
            ? MultibrotFormula.Probe(cx, cy, formula.JuliaRe, formula.JuliaIm, formula.Power, formula.MaxIterations, formula.Family)
            : MultibrotFormula.Probe(0, 0, cx, cy, formula.Power, formula.MaxIterations, formula.Family);
    }

    static string FormatProbeValue(in MultibrotFormula.OrbitProbe probe)
    {
        if (probe.Inside)
        {
            double mag = Math.Sqrt(probe.FinalZx * probe.FinalZx + probe.FinalZy * probe.FinalZy);
            return double.IsFinite(mag)
                ? $"inside  (|z| {FormatCompact(mag)})"
                : "inside";
        }

        string mu = probe.Iterations >= 10_000
            ? probe.Mu.ToString("0.#", CultureInfo.InvariantCulture)
            : probe.Mu.ToString("0.##", CultureInfo.InvariantCulture);
        return $"iter {probe.Iterations}  ({mu})";
    }

    static string FormatComplex(double re, double im)
    {
        if (!double.IsFinite(re) || !double.IsFinite(im))
            return "overflow";

        string sign = im < -1e-9 ? "-" : "+";
        return $"{FormatCompact(re)} {sign} {FormatCompact(Math.Abs(im))} i";
    }

    static string FormatCompact(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 1e-9)
            return "0";
        if (abs >= 1000 || abs < 0.001)
            return value.ToString("0.#e+0", CultureInfo.InvariantCulture);
        if (abs >= 100)
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        if (abs >= 10)
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public void DrawTelemetry(ref PanelLayout layout, RenderingFeature rendering, ColoringMode mode)
    {
        layout.Label(rendering.StatusLine(mode));
        if (!ShowFrameTime)
            return;

        string text = rendering.TryGetFrameTime(mode, out double ms) ? FormatFrameTime(ms) : "...";
        layout.Label($"Frame  {text}");
    }

    public void DrawHud(
        int canvasWidth,
        int canvasHeight,
        in FormulaParams formula,
        ColoringMode mode,
        NavigationFeature navigation,
        RenderingFeature rendering)
    {
        string device = rendering.ActiveDeviceLabel(mode);
        string familyPrefix = formula.Family == FractalFamily.Multibrot ? "" : $"{formula.Family}    ";
        Raylib.DrawText(
            $"{familyPrefix}n = {formula.Power.ToString("0.####", CultureInfo.InvariantCulture)}    iter {formula.MaxIterations}    {device}",
            16,
            16,
            20,
            PanelTheme.HudPrimaryText);
        Raylib.DrawText("H shows the panel", 16, 42, 16, PanelTheme.HudSecondaryText);

        int hudY = 66;
        if (navigation.TryGetLastCursorComplex(canvasWidth, canvasHeight, out double re, out double im))
        {
            Raylib.DrawText(
                $"{re:0.######}  {(im >= 0 ? "+" : "-")} {Math.Abs(im):0.######} i",
                16,
                hudY,
                16,
                PanelTheme.HudSecondaryText);
            hudY += 22;
        }

        if (ShowFrameTime && rendering.TryGetFrameTime(mode, out double frameMs))
            Raylib.DrawText($"Frame  {FormatFrameTime(frameMs)}", 16, hudY, 16, PanelTheme.HudSecondaryText);
    }

    static string FormatFrameTime(double milliseconds)
    {
        if (milliseconds < 100)
            return $"{milliseconds:0.0} ms";
        if (milliseconds < 10_000)
            return $"{milliseconds:0} ms";
        return $"{milliseconds / 1000:0.00} s";
    }
}
