using Mandelbrot.Visualizer.Features.Capture;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Diagnostics;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.JuliaPreview;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.App;

/// <summary>
/// Thin side-panel shell that frames the control drawer and delegates each section
/// to its owning vertical feature slice.
/// </summary>
public sealed class SidePanel
{
    public const int Width = 332;

    public bool Visible { get; set; } = true;
    public TextEditScope EditScope { get; } = new();

    public bool Draw(
        int screenWidth,
        int screenHeight,
        int canvasWidth,
        int canvasHeight,
        NavigationFeature navigation,
        FormulaFeature formula,
        ColoringFeature coloring,
        RenderingFeature rendering,
        CaptureFeature capture,
        DiagnosticsFeature diagnostics,
        JuliaPreviewFeature juliaPreview)
    {
        if (!Visible)
            return false;

        var panel = new Rectangle { x = screenWidth - Width, y = 0, width = Width, height = screenHeight };
        Raylib.DrawRectangleRec(panel, PanelTheme.PanelBackground);
        Raylib.DrawRectangle(screenWidth - Width, 0, 1, screenHeight, PanelTheme.PanelBorder);

        var layout = new PanelLayout(screenWidth - Width + 16, 14, Width - 32);
        bool changed = false;

        layout.Title("Multibrot");
        layout.Label(formula.State.FormulaSubtitle, 22);

        changed |= rendering.DrawPanel(ref layout);
        changed |= diagnostics.DrawToggle(ref layout);
        layout.Separator();

        changed |= formula.DrawPowerAndIterations(ref layout, EditScope, out bool familyChanged);
        changed |= coloring.DrawPanel(ref layout);
        bool wasJulia = formula.State.Julia;
        changed |= formula.DrawJuliaControls(ref layout, EditScope, out bool juliaToggled);
        changed |= juliaPreview.DrawToggle(ref layout, out bool matchZoomChanged);
        if (familyChanged)
        {
            EditScope.CancelAll();
            juliaPreview.OnFamilyChanged(navigation, formula);
            changed = true;
        }
        else if (juliaToggled)
        {
            EditScope.CancelAll();
            juliaPreview.OnJuliaToggled(navigation, formula, wasJulia);
            changed = true;
        }

        if (matchZoomChanged)
            juliaPreview.OnMatchZoomToggled(navigation, formula.State);

        layout.TwoButtons("Reset view", "Save PNG", out bool resetView, out bool saveImage);
        if (resetView)
        {
            EditScope.CancelAll();
            juliaPreview.OnResetView(navigation, formula);
            changed = true;
        }

        if (saveImage)
            capture.SaveInteractive(rendering, coloring.State.Mode);

        layout.Separator();
        navigation.DrawCursorTelemetry(ref layout, canvasWidth, canvasHeight);
        diagnostics.DrawCursorProbe(ref layout, canvasWidth, canvasHeight, navigation, formula.State, rendering);
        navigation.DrawViewportTelemetry(ref layout, formula.State.Power);
        diagnostics.DrawTelemetry(ref layout, rendering, coloring.State.Mode);

        layout.Space(6);
        layout.Hint("Drag or arrows pan   wheel zoom");
        layout.Hint("Shift up/down zoom   double-click zoom");
        layout.Hint("hold [ ] power   - + iterations   C palette");
        layout.Hint("R reset  J julia  P preview  G gpu  V simd");
        layout.Hint("F fractal  L 3d  M mouse light  T trap");
        layout.Hint("RMB / Space julia jump  H panel  S save");

        return changed;
    }
}
