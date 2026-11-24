using System.Numerics;
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
    bool Fullscreen = false,
    FractalFamily? Family = null,
    bool JuliaPreview = false,
    double? PreviewCRe = null,
    double? PreviewCIm = null,
    bool MatchZoom = false,
    SsaaMode? Ssaa = null,
    bool? Julia = null,
    double? JuliaRe = null,
    double? JuliaIm = null,
    int? Palette = null,
    ColoringMode? ColoringMode = null,
    double? Density = null,
    double? Phase = null,
    string? BookmarkName = null,
    bool Mandelbulb = false,
    OrbitCameraState? BulbCamera = null,
    double? BulbDetail = null,
    Vector3? BulbJuliaC = null,
    bool? BulbShadows = null,
    bool BulbFly = false,
    Vector3? BulbPos = null,
    Vector3? BulbYprDeg = null,
    Vector3? BulbTarget = null,
    string? BulbSpotName = null,
    BulbShape? BulbShape = null,
    double? BoxScale = null,
    double? BoxMinRadius = null,
    double? BoxFoldLimit = null);

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
    readonly JuliaPreviewFeature _juliaPreview = new();
    readonly BookmarksFeature _bookmarks = new();
    readonly MandelbulbFeature _mandelbulb = new();
    readonly SidePanel _panel = new();
    readonly bool _fixedLight;
    readonly bool _startFullscreen;

    bool _quit;

    public VisualizerHost(LaunchOptions options)
    {
        _fixedLight = options.LightX.HasValue || options.LightY.HasValue;
        _startFullscreen = options.Fullscreen;
        _capture = new CaptureFeature(options.ScreenshotPath);

        if (!string.IsNullOrWhiteSpace(options.BookmarkName))
            _bookmarks.ApplyByName(options.BookmarkName, _navigation, _formula, _coloring);

        if (options.ForceCpu)
            _rendering.SetUseGpu(false);

        SsaaMode initialSsaa = CaptureFeature.ResolveLaunchSsaa(options.ScreenshotPath, options.Ssaa);
        if (initialSsaa != SsaaMode.Off)
            _rendering.SetSsaa(initialSsaa);

        if (options.JuliaPreview || (options.PreviewCRe.HasValue && options.PreviewCIm.HasValue))
            _juliaPreview.Enabled = true;

        if (options.MatchZoom)
            _juliaPreview.MatchZoom = true;

        if (options.PreviewCRe.HasValue && options.PreviewCIm.HasValue)
            _juliaPreview.SetFixedPreviewC(options.PreviewCRe.Value, options.PreviewCIm.Value);

        if (options.NormalLighting
            || options.CursorLight
            || options.LightAngle.HasValue
            || options.OrbitTrap != OrbitTrapMode.None
            || options.Interior.HasValue
            || options.Palette.HasValue
            || options.ColoringMode.HasValue
            || options.Density.HasValue
            || options.Phase.HasValue)
        {
            ColoringParams current = _coloring.State;
            double angle = options.LightAngle.HasValue
                ? ((options.LightAngle.Value % 360.0) + 360.0) % 360.0
                : current.LightAngle;
            _coloring.SetColoring(current with
            {
                Palette = options.Palette.HasValue ? Math.Clamp(options.Palette.Value, 0, Palette.Count - 1) : current.Palette,
                Mode = options.ColoringMode ?? current.Mode,
                Density = options.Density.HasValue ? Math.Clamp(options.Density.Value, 0.25, 4.0) : current.Density,
                Phase = options.Phase.HasValue ? Math.Clamp(options.Phase.Value, 0.0, 1.0) : current.Phase,
                NormalLighting = current.NormalLighting || options.NormalLighting || options.CursorLight || options.LightAngle.HasValue,
                CursorLight = current.CursorLight || options.CursorLight,
                LightX = options.LightX ?? current.LightX,
                LightY = options.LightY ?? current.LightY,
                LightAngle = angle,
                OrbitTrap = options.OrbitTrap != OrbitTrapMode.None ? options.OrbitTrap : current.OrbitTrap,
                Interior = options.Interior ?? current.Interior,
            });
        }

        if (options.Mandelbulb)
        {
            _mandelbulb.SetActive(true);
            if (!string.IsNullOrWhiteSpace(options.BulbSpotName))
                _mandelbulb.ApplyLandmarkByName(options.BulbSpotName, _coloring);

            if (options.BulbShape.HasValue)
                _mandelbulb.SetShape(options.BulbShape.Value, resetCamera: !options.BulbCamera.HasValue && !options.BulbPos.HasValue);

            MandelbulbParams bulb = _mandelbulb.State;
            _mandelbulb.SetParams(bulb with
            {
                Power = options.Power.HasValue ? MandelbulbParams.ClampPower(options.Power.Value) : bulb.Power,
                Iterations = options.MaxIterations.HasValue ? MandelbulbParams.ClampIterations(options.MaxIterations.Value) : bulb.Iterations,
                Detail = options.BulbDetail ?? bulb.Detail,
                SoftShadows = options.BulbShadows ?? bulb.SoftShadows,
                Julia = options.BulbJuliaC.HasValue || bulb.Julia,
                JuliaC = options.BulbJuliaC ?? bulb.JuliaC,
                BoxScale = options.BoxScale.HasValue ? MandelbulbParams.ClampBoxScale(options.BoxScale.Value) : bulb.BoxScale,
                BoxMinRadius = options.BoxMinRadius.HasValue ? MandelbulbParams.ClampBoxMinRadius(options.BoxMinRadius.Value) : bulb.BoxMinRadius,
                BoxFoldLimit = options.BoxFoldLimit.HasValue ? MandelbulbParams.ClampBoxFoldLimit(options.BoxFoldLimit.Value) : bulb.BoxFoldLimit,
            });
            if (options.BulbCamera.HasValue || options.BulbTarget.HasValue)
            {
                OrbitCameraState cam = options.BulbCamera ?? _mandelbulb.Camera;
                if (options.BulbTarget.HasValue)
                    cam = cam with { Target = options.BulbTarget.Value };
                _mandelbulb.SetCamera(cam);
            }
            if (options.BulbFly || options.BulbPos.HasValue || options.BulbYprDeg.HasValue)
            {
                if (options.BulbPos.HasValue || options.BulbYprDeg.HasValue)
                {
                    CameraPose basePose = _mandelbulb.ActivePose;
                    Vector3 pos = options.BulbPos ?? basePose.Position;
                    CameraPose flyPose;
                    if (options.BulbYprDeg.HasValue)
                    {
                        const float degToRad = MathF.PI / 180f;
                        Vector3 ypr = options.BulbYprDeg.Value;
                        flyPose = CameraPose.FromYawPitchRoll(pos, ypr.X * degToRad, ypr.Y * degToRad, ypr.Z * degToRad, basePose.FovDegrees);
                    }
                    else
                    {
                        flyPose = CameraPose.FromLookAt(pos, Vector3.Zero, Vector3.UnitY, basePose.FovDegrees);
                    }

                    _mandelbulb.SetFlyPose(flyPose);
                }
                else
                {
                    _mandelbulb.SetCameraMode(BulbCameraMode.Fly);
                }
            }
        }
        else if (options.Power.HasValue
            || options.MaxIterations.HasValue
            || options.Family.HasValue
            || options.Julia.HasValue
            || options.JuliaRe.HasValue
            || options.JuliaIm.HasValue)
        {
            FormulaParams current = _formula.State;
            _formula.SetFormula(current with
            {
                Power = options.Power.HasValue ? FormulaParams.ClampNonZeroPower(options.Power.Value) : current.Power,
                MaxIterations = options.MaxIterations.HasValue ? FormulaParams.ClampIterations(options.MaxIterations.Value) : current.MaxIterations,
                Family = options.Family ?? current.Family,
                Julia = options.Julia ?? current.Julia,
                JuliaRe = options.JuliaRe ?? current.JuliaRe,
                JuliaIm = options.JuliaIm ?? current.JuliaIm,
            });
            if (options.Family.HasValue || options.Julia == true)
            {
                _formula.State.GetDefaultView(out double cx, out double cy, out double scale);
                _navigation.ResetTo(cx, cy, scale);
            }
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
        MouseClickLatch.Install();
        PanelTheme.Apply();
        _rendering.Load();
        _mandelbulb.Load();
        _capture.StartClock();

        while (!Raylib.WindowShouldClose() && !_quit)
        {
            MouseClickLatch.BeginFrame();

            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();
            int canvasWidth = Math.Max(1, _panel.Visible ? screenWidth - SidePanel.Width : screenWidth);
            int canvasHeight = screenHeight;

            _capture.TrackWindowSize(screenWidth, screenHeight);

            bool dirty = HandleInput(canvasWidth, canvasHeight, out bool pointerOverPreview);
            bool is3d = _mandelbulb.Active;
            if (!is3d)
            {
                dirty |= _formula.SyncAutoIterations(_navigation.State.ScaleExact);
                if (!_fixedLight)
                {
                    bool hasCursorPos = _navigation.TryGetLastCursorNormalized(canvasWidth, canvasHeight, out float lightX, out float lightY);
                    dirty |= _coloring.SyncCursorLight(hasCursorPos, lightX, lightY);
                }
            }

            bool finalQuality = _capture.ForceFinalQuality || (is3d ? !_mandelbulb.IsInteracting : !_navigation.IsInteracting);

            if (!is3d)
                _rendering.PrepareFrame(_coloring.State.Mode);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(PanelTheme.CanvasBackground);
            if (is3d)
            {
                _mandelbulb.DrawCanvas(canvasWidth, canvasHeight, _coloring.State, finalQuality, _rendering.Ssaa);
                if (!_panel.Visible)
                    _mandelbulb.DrawHud(_rendering.Ssaa, _diagnostics.ShowFrameTime);
            }
            else
            {
                _rendering.DrawCanvas(canvasWidth, canvasHeight, _navigation.State, _formula.State, _coloring.State, finalQuality);
                _juliaPreview.DrawOverlay(canvasWidth, canvasHeight, _navigation.State, _formula.State, _coloring.State, _rendering, pointerOverPreview);

                if (!_panel.Visible)
                    _diagnostics.DrawHud(canvasWidth, canvasHeight, _formula.State, _coloring.State.Mode, _navigation, _rendering);
            }

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
                _diagnostics,
                _juliaPreview,
                _bookmarks,
                _mandelbulb);

            if (_mandelbulb.Active)
            {
                if (_mandelbulb.DrawBookmarksOverlay(screenWidth, screenHeight, canvasWidth, _coloring, _panel.EditScope))
                    dirty = true;
            }
            else if (_bookmarks.DrawOverlay(screenWidth, screenHeight, canvasWidth, _navigation, _formula, _coloring, _panel.EditScope))
            {
                _juliaPreview.ClearReturnView();
                dirty = true;
            }

            is3d = _mandelbulb.Active;
            if (!is3d)
                dirty |= _formula.SyncAutoIterations(_navigation.State.ScaleExact);

            Func<bool> captureReady = is3d
                ? () => _mandelbulb.IsReadyForCapture(_rendering.Ssaa)
                : () => _rendering.IsReadyForCapture(_coloring.State.Mode);
            Action<string> captureSave = is3d
                ? _mandelbulb.Save
                : path => _rendering.Save(path, _coloring.State.Mode);

            _capture.CaptureWindowIfReady(captureReady);
            Raylib.EndDrawing();

            _capture.FlushPendingInteractiveSave(_rendering, captureReady, captureSave);

            if (!is3d)
            {
                _rendering.SubmitIfNeeded(
                    canvasWidth,
                    canvasHeight,
                    _navigation.State,
                    _formula.State,
                    _coloring.State,
                    finalQuality || _capture.ForceFinalQuality,
                    dirty);
            }

            if (_capture.ShouldExitAfterFrame(captureSave))
                break;
        }

        MouseClickLatch.Uninstall();
        _mandelbulb.Dispose();
        _juliaPreview.Dispose();
        _rendering.Dispose();
        Raylib.CloseWindow();
    }

    bool HandleInput(int canvasWidth, int canvasHeight, out bool pointerOverPreview)
    {
        if (_mandelbulb.CapturesInput || (!_mandelbulb.Active && _bookmarks.CapturesInput))
        {
            if (_mandelbulb.CapturesInput)
                _mandelbulb.StopMotion();
            pointerOverPreview = false;
            return false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
        {
            if (_panel.EditScope.CapturesKeyboard)
                _panel.EditScope.CancelAll();
            else
                _quit = true;
        }

        if (_mandelbulb.Active)
        {
            pointerOverPreview = false;
            bool changed3d = _mandelbulb.HandlePointer(canvasWidth, canvasHeight);
            if (_panel.EditScope.CapturesKeyboard)
            {
                _mandelbulb.StopMotion();
                return changed3d;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.KEY_H))
            {
                _panel.Visible = !_panel.Visible;
                if (!_panel.Visible)
                    _mandelbulb.DismissPopups();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.KEY_F11))
                Raylib.ToggleFullscreen();

            if (_mandelbulb.HandleBookmarkKeys(_coloring))
                return true;
            if (_mandelbulb.CapturesInput)
            {
                _mandelbulb.StopMotion();
                return changed3d;
            }

            changed3d |= _mandelbulb.HandleKeys(out _);
            if (!_mandelbulb.Active)
            {
                _panel.EditScope.CancelAll();
                return true;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.KEY_C))
            {
                _coloring.SetColoring(_coloring.State with { Palette = (_coloring.State.Palette + 1) % Palette.Count });
                changed3d = true;
            }

            string bulbTitle = _mandelbulb.SelectedBookmarkName ?? _mandelbulb.State.CaptureStem;

            if (_mandelbulb.IsFlyMode)
            {
                // In 6DOF Fly mode, A and S drive strafe-left and thrust-back; use F2 for PNG capture.
                if (Raylib.IsKeyPressed(KeyboardKey.KEY_F2))
                {
                    changed3d |= _capture.SaveInteractive(
                        _rendering,
                        () => _mandelbulb.IsReadyForCapture(_rendering.Ssaa),
                        _mandelbulb.Save,
                        bulbTitle);
                }
            }
            else
            {
                if (Raylib.IsKeyPressed(KeyboardKey.KEY_A))
                {
                    _rendering.SetSsaa((SsaaMode)(((int)_rendering.Ssaa + 1) % SsaaPattern.ModeCount));
                    changed3d = true;
                }

                changed3d |= _capture.HandleKeys(
                    _rendering,
                    () => _mandelbulb.IsReadyForCapture(_rendering.Ssaa),
                    _mandelbulb.Save,
                    bulbTitle);
            }

            return changed3d;
        }

        pointerOverPreview = _juliaPreview.IsPointerOverCard(canvasWidth, canvasHeight, _formula.State);
        _juliaPreview.UpdatePreviewTarget(canvasWidth, canvasHeight, _formula.State, _navigation, pointerOverPreview);

        bool changed = _navigation.HandlePointer(canvasWidth, canvasHeight, _panel.Visible, pointerOverPreview);

        changed |= _juliaPreview.HandleInput(
            canvasWidth,
            canvasHeight,
            _formula.State,
            _navigation,
            pointerOverPreview,
            _panel.EditScope.CapturesKeyboard,
            out bool jumpedToJulia,
            out double jumpRe,
            out double jumpIm,
            out bool returnedToMap);
        if (jumpedToJulia)
        {
            _panel.EditScope.CancelAll();
            _bookmarks.ClearSelection();
            _juliaPreview.EnterJuliaFromJump(_navigation, _formula, jumpRe, jumpIm);
            pointerOverPreview = false;
            return true;
        }

        if (returnedToMap)
        {
            _panel.EditScope.CancelAll();
            _bookmarks.ClearSelection();
            if (_juliaPreview.ReturnToMap(_navigation, _formula))
                return true;
        }

        if (_panel.EditScope.CapturesKeyboard)
            return changed;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_H))
        {
            _panel.Visible = !_panel.Visible;
            if (!_panel.Visible)
                _bookmarks.DismissPopups();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_F11))
            Raylib.ToggleFullscreen();

        if (_mandelbulb.HandleKeys(out _))
        {
            _panel.EditScope.CancelAll();
            _bookmarks.DismissPopups();
            return true;
        }

        if (_bookmarks.HandleKeys(_navigation, _formula, _coloring))
        {
            _juliaPreview.ClearReturnView();
            return true;
        }
        if (_bookmarks.CapturesInput)
            return changed;

        _formula.State.GetDefaultView(out double defaultCx, out double defaultCy, out double defaultScale);
        changed |= _navigation.HandleKeys(canvasWidth, canvasHeight, defaultCx, defaultCy, defaultScale);

        bool wasJulia = _formula.State.Julia;
        FractalFamily prevFamily = _formula.State.Family;
        changed |= _formula.HandleKeys(out bool viewResetNeeded, out bool interactive);
        if (viewResetNeeded)
        {
            _bookmarks.ClearSelection();
            if (_formula.State.Family != prevFamily)
                _juliaPreview.OnFamilyChanged(_navigation, _formula);
            else if (_formula.State.Julia != wasJulia)
                _juliaPreview.OnJuliaToggled(_navigation, _formula, wasJulia);
            else
                _juliaPreview.OnResetView(_navigation, _formula);
        }
        if (interactive)
            _navigation.Touch();

        changed |= _coloring.HandleKeys();
        changed |= _rendering.HandleKeys();
        changed |= _capture.HandleKeys(_rendering, _coloring.State.Mode, _bookmarks.SelectedName);

        return changed;
    }

    public void Dispose()
    {
        _mandelbulb.Dispose();
        _juliaPreview.Dispose();
        _rendering.Dispose();
    }
}
