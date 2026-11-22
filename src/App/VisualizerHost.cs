using Mandelbrot.Visualizer.Features.Capture;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Diagnostics;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.App;

public sealed record LaunchOptions(
    string? ScreenshotPath = null,
    BigFloat? CenterX = null,
    BigFloat? CenterY = null,
    FloatExp? Scale = null,
    double? Power = null,
    int? MaxIterations = null,
    bool ForceCpu = false,
    bool NormalLighting = false,
    OrbitTrapMode OrbitTrap = OrbitTrapMode.None,
    InteriorStyle? Interior = null,
    bool CursorLight = false,
    float? LightX = null,
    float? LightY = null,
    double? LightAngle = null,
    bool Fullscreen = false);

/// <summary>
/// Thin application shell owning the Raylib window lifecycle and frame loop,
/// delegating all domain behavior to vertical feature slices.
/// </summary>
public sealed class VisualizerHost : IDisposable
{
    readonly NavigationFeature _navigation = new();
    readonly FormulaFeature _formula = new();
    readonly ColoringFeature _coloring = new();
    readonly RenderingFeature _rendering = new();
    readonly CaptureFeature _capture;
    readonly DiagnosticsFeature _diagnostics = new();
    readonly SidePanel _panel = new();
    readonly bool _fixedLight;
    readonly bool _startFullscreen;

    bool _quit;

    public VisualizerHost(LaunchOptions options)
    {
        _fixedLight = options.LightX.HasValue || options.LightY.HasValue;
        _startFullscreen = options.Fullscreen;
        _capture = new CaptureFeature(options.ScreenshotPath);
        if (options.ForceCpu)
            _rendering.SetUseGpu(false);

        if (options.NormalLighting || options.CursorLight || options.LightAngle.HasValue || options.OrbitTrap != OrbitTrapMode.None || options.Interior.HasValue)
        {
            ColoringParams current = _coloring.State;
            double angle = options.LightAngle.HasValue
                ? ((options.LightAngle.Value % 360.0) + 360.0) % 360.0
                : current.LightAngle;
            _coloring.SetColoring(current with
            {
                NormalLighting = options.NormalLighting || options.CursorLight || options.LightAngle.HasValue,
                CursorLight = options.CursorLight,
                LightX = options.LightX ?? current.LightX,
                LightY = options.LightY ?? current.LightY,
                LightAngle = angle,
                OrbitTrap = options.OrbitTrap,
                Interior = options.Interior ?? current.Interior,
            });
        }

        if (options.Power.HasValue || options.MaxIterations.HasValue)
        {
            FormulaParams current = _formula.State;
            _formula.SetFormula(current with
            {
                Power = options.Power.HasValue ? FormulaParams.ClampNonZeroPower(options.Power.Value) : current.Power,
                MaxIterations = options.MaxIterations.HasValue ? FormulaParams.ClampIterations(options.MaxIterations.Value) : current.MaxIterations,
            });
        }

        if (options.CenterX.HasValue || options.CenterY.HasValue || options.Scale.HasValue)
        {
            ViewportState current = _navigation.State;
            _navigation.SetExact(
                options.CenterX ?? current.CenterXExact,
                options.CenterY ?? current.CenterYExact,
                options.Scale ?? current.ScaleExact);
        }
    }

    public void Run()
    {
        ConfigFlags flags = ConfigFlags.FLAG_WINDOW_RESIZABLE | ConfigFlags.FLAG_VSYNC_HINT;
        if (_startFullscreen)
            flags |= ConfigFlags.FLAG_FULLSCREEN_MODE;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(0, 0, "Multibrot");
        if (_startFullscreen && !Raylib.IsWindowFullscreen())
            Raylib.ToggleFullscreen();
        Raylib.SetWindowMinSize(1100, 760);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.KEY_NULL);
        PanelTheme.Apply();
        _rendering.Load();
        _capture.StartClock();

        while (!Raylib.WindowShouldClose() && !_quit)
        {
            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();
            int canvasWidth = Math.Max(1, _panel.Visible ? screenWidth - SidePanel.Width : screenWidth);
            int canvasHeight = screenHeight;

            _capture.TrackWindowSize(screenWidth, screenHeight);

            bool dirty = HandleInput(canvasWidth, canvasHeight);
            dirty |= _formula.SyncAutoIterations(_navigation.State.ScaleExact);
            if (!_fixedLight)
            {
                bool hasCursorPos = _navigation.TryGetLastCursorNormalized(canvasWidth, canvasHeight, out float lightX, out float lightY);
                dirty |= _coloring.SyncCursorLight(hasCursorPos, lightX, lightY);
            }
            bool finalQuality = _capture.ForceFinalQuality || !_navigation.IsInteracting;

            _rendering.PrepareFrame(_coloring.State.Mode);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(PanelTheme.CanvasBackground);
            _rendering.DrawCanvas(canvasWidth, canvasHeight, _navigation.State, _formula.State, _coloring.State, finalQuality);

            if (!_panel.Visible)
                _diagnostics.DrawHud(canvasWidth, canvasHeight, _formula.State, _coloring.State.Mode, _navigation, _rendering);

            dirty |= _panel.Draw(
                screenWidth,
                screenHeight,
                canvasWidth,
                canvasHeight,
                _navigation,
                _formula,
                _coloring,
                _rendering,
                _capture,
                _diagnostics);

            dirty |= _formula.SyncAutoIterations(_navigation.State.ScaleExact);
            _capture.CaptureWindowIfReady(_rendering, _coloring.State.Mode);
            Raylib.EndDrawing();

            _rendering.SubmitIfNeeded(
                canvasWidth,
                canvasHeight,
                _navigation.State,
                _formula.State,
                _coloring.State,
                finalQuality,
                dirty);

            if (_capture.ShouldExitAfterFrame(_rendering, _coloring.State.Mode))
                break;
        }

        _rendering.Dispose();
        Raylib.CloseWindow();
    }

    bool HandleInput(int canvasWidth, int canvasHeight)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
        {
            if (_panel.EditScope.CapturesKeyboard)
                _panel.EditScope.CancelAll();
            else
                _quit = true;
        }

        bool changed = _navigation.HandlePointer(canvasWidth, canvasHeight, _panel.Visible);
        if (_panel.EditScope.CapturesKeyboard)
            return changed;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_H))
            _panel.Visible = !_panel.Visible;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_F11))
            Raylib.ToggleFullscreen();

        changed |= _navigation.HandleKeys(canvasWidth, canvasHeight, _formula.State.Julia);
        changed |= _formula.HandleKeys(out bool juliaToggled, out bool interactive);
        if (juliaToggled)
            _navigation.ResetFor(_formula.State.Julia);
        if (interactive)
            _navigation.Touch();

        changed |= _coloring.HandleKeys();
        changed |= _rendering.HandleKeys();
        _capture.HandleKeys(_rendering, _coloring.State.Mode);

        return changed;
    }

    public void Dispose() => _rendering.Dispose();
}
