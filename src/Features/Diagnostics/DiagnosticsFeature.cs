using System.Globalization;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Diagnostics;

/// <summary>
/// Vertical slice owning frame-time telemetry toggle/formatting, status readout,
/// and the minimal on-canvas HUD shown when the side panel is hidden.
/// </summary>
public sealed class DiagnosticsFeature
{
    public bool ShowFrameTime { get; private set; }

    public bool DrawToggle(ref PanelLayout layout)
    {
        bool show = ShowFrameTime;
        if (!layout.Check("Frame time", ref show))
            return false;

        ShowFrameTime = show;
        return true;
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
        Raylib.DrawText(
            $"n = {formula.Power.ToString("0.####", CultureInfo.InvariantCulture)}    iter {formula.MaxIterations}    {device}",
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
