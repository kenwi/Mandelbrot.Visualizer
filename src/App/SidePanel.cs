using Mandelbrot.Visualizer.Features.Bookmarks;
using Mandelbrot.Visualizer.Features.Capture;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Diagnostics;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.JuliaPreview;
using Mandelbrot.Visualizer.Features.Mandelbulb;
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
        JuliaPreviewFeature juliaPreview,
        BookmarksFeature bookmarks,
        MandelbulbFeature mandelbulb)
    {
        if (!Visible)
            return false;

        bool panelBlocked = mandelbulb.Active ? mandelbulb.CapturesInput : bookmarks.CapturesInput;
        if (panelBlocked)
            RayGui.GuiDisable();

        var panel = new Rectangle { x = screenWidth - Width, y = 0, width = Width, height = screenHeight };
        Raylib.DrawRectangleRec(panel, PanelTheme.PanelBackground);
        Raylib.DrawRectangle(screenWidth - Width, 0, 1, screenHeight, PanelTheme.PanelBorder);

        var layout = new PanelLayout(screenWidth - Width + 16, 14, Width - 32);
        bool changed = false;

        layout.Title(mandelbulb.Active
            ? (mandelbulb.State.Shape == BulbShape.Mandelbox ? "Mandelbox 3D" : "Mandelbulb 3D")
            : "Multibrot");
        layout.Label(mandelbulb.Active ? mandelbulb.State.Subtitle : formula.State.FormulaSubtitle, 22);

        if (mandelbulb.Active)
            changed |= rendering.DrawSsaaCombo(ref layout);
        else
            changed |= rendering.DrawPanel(ref layout);

        bool showFrameTime = diagnostics.ShowFrameTime;
        bool bulbActive = mandelbulb.Active;
        if (layout.TwoChecks("Frame time", ref showFrameTime, "3D Bulb", ref bulbActive, splitRatio: 0.56f))
        {
            diagnostics.SetShowFrameTime(showFrameTime);
            if (bulbActive != mandelbulb.Active)
            {
                EditScope.CancelAll();
                bookmarks.DismissPopups();
                mandelbulb.SetActive(bulbActive);
            }
            changed = true;
        }

        layout.Separator();

        if (mandelbulb.Active)
        {
            changed |= mandelbulb.DrawPanel(ref layout, EditScope);
            layout.Separator();
            changed |= coloring.DrawPanel3D(ref layout);
            layout.Space(2);

            if (panelBlocked)
                RayGui.GuiEnable();

            mandelbulb.DrawBookmarksPanel(ref layout, coloring.State, rendering.Ssaa, EditScope);

            if (panelBlocked)
                RayGui.GuiDisable();

            string save3dLabel = capture.IsSavingInteractive
                ? $"Saving {SsaaPattern.ShortLabel(SsaaPattern.Highest)}..."
                : "Save PNG";
            layout.TwoButtons("Reset view", save3dLabel, out bool reset3d, out bool save3d);
            if (!panelBlocked && reset3d)
            {
                EditScope.CancelAll();
                mandelbulb.Bookmarks.ClearSelection();
                mandelbulb.ResetView();
                changed = true;
            }

            if (!panelBlocked && save3d)
            {
                string title = mandelbulb.SelectedBookmarkName ?? mandelbulb.State.CaptureStem;
                changed |= capture.SaveInteractive(
                    rendering,
                    () => mandelbulb.IsReadyForCapture(rendering.Ssaa),
                    mandelbulb.Save,
                    title);
            }

            layout.Separator();
            mandelbulb.DrawTelemetry(ref layout, rendering.Ssaa, diagnostics.ShowFrameTime);

            layout.Space(4);
            if (mandelbulb.IsFlyMode)
            {
                layout.Hint("WASD move   Space/Ctrl up/down");
                layout.Hint("LMB look  Q/E or RMB roll  MMB strafe");
                layout.Hint("Wheel speed   Shift boost   Alt slow");
                layout.Hint("F orbit  M shape  X level  B save");
                layout.Hint(", . spot  [ ] param  - + iter  F2 png");
            }
            else
            {
                layout.Hint("Drag orbit   RMB/Shift drag pan");
                layout.Hint("Wheel/dbl-click zoom   arrows orbit");
                layout.Hint("F 6dof fly  M shape  B save  , . spot");
                layout.Hint("[ ] param  - + iter  C pal  A ssaa");
                layout.Hint("3 2D/3D  J julia  L shadow  S png");
            }

            if (panelBlocked)
                RayGui.GuiEnable();

            return !panelBlocked && changed;
        }

        changed |= formula.DrawPowerAndIterations(ref layout, EditScope, out bool familyChanged);
        changed |= coloring.DrawPanel(ref layout);
        bool wasJulia = formula.State.Julia;
        changed |= formula.DrawJuliaControls(ref layout, EditScope, out bool juliaToggled);
        changed |= juliaPreview.DrawToggle(ref layout, out bool matchZoomChanged);
        if (familyChanged)
        {
            EditScope.CancelAll();
            bookmarks.ClearSelection();
            juliaPreview.OnFamilyChanged(navigation, formula);
            changed = true;
        }
        else if (juliaToggled)
        {
            EditScope.CancelAll();
            bookmarks.ClearSelection();
            juliaPreview.OnJuliaToggled(navigation, formula, wasJulia);
            changed = true;
        }

        if (matchZoomChanged)
            juliaPreview.OnMatchZoomToggled(navigation, formula.State);

        if (panelBlocked)
            RayGui.GuiEnable();

        bookmarks.DrawPanel(ref layout, navigation.State, formula.State, coloring.State, rendering, EditScope);

        if (panelBlocked)
            RayGui.GuiDisable();

        string savePngLabel = capture.IsSavingInteractive
            ? $"Saving {SsaaPattern.ShortLabel(SsaaPattern.Highest)}..."
            : "Save PNG";
        layout.TwoButtons("Reset view", savePngLabel, out bool resetView, out bool saveImage);
        if (!panelBlocked && resetView)
        {
            EditScope.CancelAll();
            bookmarks.ClearSelection();
            juliaPreview.OnResetView(navigation, formula);
            changed = true;
        }

        if (!panelBlocked && saveImage)
            changed |= capture.SaveInteractive(rendering, coloring.State.Mode, bookmarks.SelectedName);

        layout.Separator();
        navigation.DrawCursorTelemetry(ref layout, canvasWidth, canvasHeight);
        diagnostics.DrawCursorProbe(ref layout, canvasWidth, canvasHeight, navigation, formula.State, rendering);
        navigation.DrawViewportTelemetry(ref layout, formula.State.Power);
        diagnostics.DrawTelemetry(ref layout, rendering, coloring.State.Mode);

        layout.Space(2);
        layout.Hint("Drag/arrows pan  wheel/dbl-click zoom");
        layout.Hint("[ ] power  - + iter  C palette  F fractal");
        layout.Hint("R reset  J julia  P preview  B save  , . spot");
        layout.Hint("L 3d  M light  T trap  A ssaa  3 bulb");
        layout.Hint("RMB / Space julia  G gpu  H panel  S save");

        if (panelBlocked)
            RayGui.GuiEnable();

        return !panelBlocked && changed;
    }
}
