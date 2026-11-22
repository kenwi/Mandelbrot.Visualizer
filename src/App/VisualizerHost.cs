using System.Globalization;
using System.Numerics;
using Mandelbrot.Visualizer.Coloring;
using Mandelbrot.Visualizer.Controls;
using Mandelbrot.Visualizer.Formula;
using Mandelbrot.Visualizer.Rendering;
using Mandelbrot.Visualizer.Viewport;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.App;

public sealed class VisualizerHost : IDisposable
{
    readonly FractalView _view = new();
    readonly VisualizerSettings _settings = new();
    readonly ControlPanel _panel = new();
    readonly FractalRenderer _renderer = new();
    readonly GpuFractalRenderer _gpu = new();
    readonly string? _screenshotPath;

    bool _dragging;
    bool _quit;
    double _lastClickTime = -1;
    Vector2 _lastCursor;
    bool _hasCursor;
    DateTime _interactUntil = DateTime.MinValue;
    RenderRequest _submitted;
    bool _hasSubmitted;
    int _stableWidth = -1;
    int _stableHeight = -1;
    int _stableFrames;

    public VisualizerHost(string? screenshotPath) => _screenshotPath = screenshotPath;

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.FLAG_WINDOW_RESIZABLE | ConfigFlags.FLAG_VSYNC_HINT);
        Raylib.InitWindow(1440, 900, "Multibrot");
        Raylib.SetWindowMinSize(1100, 760);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.KEY_NULL);
        ControlPanel.ApplyTheme();
        _gpu.Load();

        var started = DateTime.UtcNow;
        while (!Raylib.WindowShouldClose() && !_quit)
        {
            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();
            int canvasWidth = _panel.Visible ? screenWidth - ControlPanel.Width : screenWidth;
            int canvasHeight = screenHeight;
            if (canvasWidth < 1) canvasWidth = 1;

            if (screenWidth == _stableWidth && screenHeight == _stableHeight)
                _stableFrames++;
            else
            {
                _stableWidth = screenWidth;
                _stableHeight = screenHeight;
                _stableFrames = 1;
            }

            bool viewChanged = HandleNavigation(canvasWidth, canvasHeight);
            bool finalQuality = DateTime.UtcNow >= _interactUntil && !_dragging;
            if (_screenshotPath != null) finalQuality = true;
            bool gpu = UsingGpu();

            if (!gpu) _renderer.Upload();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(4, 6, 12, 255));
            if (gpu)
                _gpu.Draw(canvasWidth, canvasHeight, FrameRequest(canvasWidth, canvasHeight));
            else
                _renderer.Draw(canvasWidth, canvasHeight);

            if (!_panel.Visible)
                DrawHud();

            Vector2 mouse = Raylib.GetMousePosition();
            bool cursorOnCanvas = mouse.X >= 0 && mouse.X < canvasWidth && mouse.Y >= 0 && mouse.Y < canvasHeight;
            _view.ScreenToComplex(mouse.X, mouse.Y, canvasWidth, canvasHeight, out double cursorRe, out double cursorIm);

            bool wasJulia = _settings.Julia;
            bool settingsChanged = _panel.Draw(
                _settings,
                _view,
                screenWidth,
                screenHeight,
                cursorRe,
                cursorIm,
                cursorOnCanvas,
                StatusLine(gpu),
                FrameTimeText(gpu),
                out bool resetView,
                out bool saveImage);

            if (resetView)
            {
                if (_settings.Julia) _view.ResetJulia();
                else _view.ResetMandelbrot();
                viewChanged = true;
            }

            if (wasJulia != _settings.Julia)
            {
                if (_settings.Julia) _view.ResetJulia();
                else _view.ResetMandelbrot();
                viewChanged = true;
            }

            if (saveImage) SaveImage();

            string? capturedShot = null;
            bool cpuReady = !gpu && _hasSubmitted && _submitted.FinalQuality && _renderer.HasImage && !_renderer.IsBusy;
            bool gpuReady = gpu && _gpu.HasImage;
            if (_screenshotPath is string shot && _stableFrames >= 45 && (cpuReady || gpuReady))
            {
                capturedShot = shot;
                RlGl.rlDrawRenderBatchActive();
                Image screen = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(screen, Path.ChangeExtension(shot, ".window.png"));
                Raylib.UnloadImage(screen);
            }

            Raylib.EndDrawing();

            if (!UsingGpu() && (viewChanged || settingsChanged || !_hasSubmitted || SizeChanged(canvasWidth, canvasHeight, finalQuality)))
                Submit(canvasWidth, canvasHeight, finalQuality);

            if (capturedShot != null)
            {
                if (gpu) _gpu.Save(capturedShot);
                else _renderer.Save(capturedShot);
                break;
            }

            if (_screenshotPath != null && DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
                break;
        }

        _gpu.Dispose();
        _renderer.Dispose();
        Raylib.CloseWindow();
    }

    bool HandleNavigation(int canvasWidth, int canvasHeight)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
        {
            if (_panel.CapturesKeyboard) _panel.CancelEdit();
            else _quit = true;
        }

        Vector2 mouse = Raylib.GetMousePosition();
        bool onCanvas = !_panel.Visible || mouse.X < canvasWidth;
        bool changed = false;

        if (Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT) && onCanvas)
        {
            double now = Raylib.GetTime();
            if (now - _lastClickTime < 0.3)
            {
                double factor = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) ? 2.0 : 0.5;
                _view.ZoomAt(mouse.X, mouse.Y, canvasWidth, canvasHeight, factor);
                changed = true;
                Touch();
            }
            _lastClickTime = now;
            _dragging = true;
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT))
            _dragging = false;

        if (_dragging && onCanvas)
        {
            Vector2 delta = Raylib.GetMouseDelta();
            if (delta.X != 0 || delta.Y != 0)
            {
                _view.PanPixels(delta.X, delta.Y, canvasWidth, canvasHeight);
                changed = true;
                Touch();
            }
        }

        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && onCanvas)
        {
            _view.ZoomAt(mouse.X, mouse.Y, canvasWidth, canvasHeight, Math.Exp(-wheel * 0.18));
            changed = true;
            Touch();
        }

        if (!_panel.CapturesKeyboard)
            changed |= HandleKeys(canvasWidth, canvasHeight);

        if (onCanvas)
        {
            _lastCursor = mouse;
            _hasCursor = true;
        }

        return changed;
    }

    bool HandleKeys(int canvasWidth, int canvasHeight)
    {
        bool changed = false;
        double aspect = canvasWidth / (double)Math.Max(canvasHeight, 1);

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_R))
        {
            if (_settings.Julia) _view.ResetJulia();
            else _view.ResetMandelbrot();
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_C))
        {
            _settings.CyclePalette();
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_J))
        {
            _settings.Julia = !_settings.Julia;
            if (_settings.Julia) _view.ResetJulia();
            else _view.ResetMandelbrot();
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_G))
        {
            _settings.UseGpu = !_settings.UseGpu;
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_V))
        {
            _settings.UseSimd = !_settings.UseSimd;
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_H))
            _panel.Visible = !_panel.Visible;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_F11))
            Raylib.ToggleFullscreen();

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_S))
            SaveImage();

        double powerDelta = 0;
        double dt = Math.Min(Raylib.GetFrameTime(), 0.05f);
        if (Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_BRACKET))
            powerDelta += 1.5 * dt;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_BRACKET))
            powerDelta -= 1.5 * dt;
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_RIGHT_BRACKET))
            powerDelta += 0.1;
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_LEFT_BRACKET))
            powerDelta -= 0.1;
        if (powerDelta != 0)
        {
            _settings.NudgePower(powerDelta);
            changed = true;
            Touch();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_MINUS) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_SUBTRACT))
        {
            _settings.NudgeIterations(-25);
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_EQUAL) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_ADD))
        {
            _settings.NudgeIterations(25);
            changed = true;
        }

        double nudge = _view.Scale * 0.02;
        bool shift = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
        bool up = Raylib.IsKeyDown(KeyboardKey.KEY_UP);
        bool down = Raylib.IsKeyDown(KeyboardKey.KEY_DOWN);
        if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT)) { _view.CenterX -= nudge * aspect; changed = true; Touch(); }
        if (Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT)) { _view.CenterX += nudge * aspect; changed = true; Touch(); }
        if (shift && up != down)
        {
            double step = Math.Min(Raylib.GetFrameTime(), 0.05f);
            double factor = Math.Pow(2.2, down ? step : -step);
            _view.ZoomAt(canvasWidth * 0.5, canvasHeight * 0.5, canvasWidth, canvasHeight, factor);
            changed = true;
            Touch();
        }
        else
        {
            if (up) { _view.CenterY += nudge; changed = true; Touch(); }
            if (down) { _view.CenterY -= nudge; changed = true; Touch(); }
        }

        return changed;
    }

    void Touch() => _interactUntil = DateTime.UtcNow.AddMilliseconds(180);

    void Submit(int canvasWidth, int canvasHeight, bool finalQuality)
    {
        int divisor = finalQuality ? 1 : 4;
        int width = Math.Max(1, canvasWidth / divisor);
        int height = Math.Max(1, canvasHeight / divisor);
        var request = new RenderRequest(
            width,
            height,
            _view.CenterX,
            _view.CenterY,
            _view.Scale,
            _settings.Power,
            _settings.MaxIterations,
            _settings.Palette,
            _settings.Mode,
            _settings.Smooth,
            _settings.Interior,
            _settings.Density,
            _settings.Phase,
            _settings.Julia,
            _settings.JuliaRe,
            _settings.JuliaIm,
            _settings.UseSimd,
            finalQuality);

        if (_hasSubmitted && request == _submitted) return;
        _submitted = request;
        _hasSubmitted = true;
        _renderer.Request(request);
    }

    bool SizeChanged(int canvasWidth, int canvasHeight, bool finalQuality)
    {
        int divisor = finalQuality ? 1 : 4;
        int width = Math.Max(1, canvasWidth / divisor);
        int height = Math.Max(1, canvasHeight / divisor);
        return !_hasSubmitted || _submitted.Width != width || _submitted.Height != height || _submitted.FinalQuality != finalQuality;
    }

    void DrawHud()
    {
        string device = UsingGpu() ? "GPU" : UsingSimd() ? "CPU SIMD" : "CPU";
        Raylib.DrawText($"n = {_settings.Power.ToString("0.####", CultureInfo.InvariantCulture)}    iter {_settings.MaxIterations}    {device}", 16, 16, 20, new Color(230, 232, 238, 255));
        Raylib.DrawText("H shows the panel", 16, 42, 16, new Color(170, 176, 188, 255));
        int hudY = 66;
        if (_hasCursor)
        {
            _view.ScreenToComplex(_lastCursor.X, _lastCursor.Y, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), out double re, out double im);
            Raylib.DrawText($"{re:0.######}  {(im >= 0 ? "+" : "-")} {Math.Abs(im):0.######} i", 16, hudY, 16, new Color(170, 176, 188, 255));
            hudY += 22;
        }

        if (_settings.ShowFrameTime && TryFrameTime(UsingGpu(), out double frameMs))
            Raylib.DrawText($"Frame  {FormatFrameTime(frameMs)}", 16, hudY, 16, new Color(170, 176, 188, 255));
    }

    void SaveImage()
    {
        Directory.CreateDirectory("captures");
        string path = Path.Combine("captures", $"multibrot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        if (UsingGpu() && _gpu.HasImage) _gpu.Save(path);
        else _renderer.Save(path);
    }

    bool UsingGpu()
        => _settings.UseGpu && _gpu.IsAvailable && _settings.Mode != ColoringMode.Histogram;

    string StatusLine(bool gpu)
    {
        int fps = Raylib.GetFPS();
        if (_settings.UseGpu && !_gpu.IsAvailable)
            return "No shader, using CPU";
        if (gpu)
            return $"GPU    {fps} fps";
        if (_settings.UseSimd && !SimdMultibrot.IsSupported)
            return "No SIMD, using CPU";

        bool simd = UsingSimd();
        string name = simd ? "CPU SIMD" : "CPU";
        if (_settings.UseGpu && _settings.Mode == ColoringMode.Histogram)
            return $"{(simd ? "SIMD" : "CPU")} histogram    {fps} fps";
        if (_renderer.IsBusy)
            return simd ? "SIMD rendering..." : "CPU rendering...";
        return $"{name}    {fps} fps";
    }

    bool UsingSimd() => _settings.UseSimd && SimdMultibrot.IsSupported;

    string? FrameTimeText(bool gpu)
    {
        if (!_settings.ShowFrameTime)
            return null;
        return TryFrameTime(gpu, out double ms) ? FormatFrameTime(ms) : "...";
    }

    bool TryFrameTime(bool gpu, out double milliseconds)
    {
        if (gpu && _gpu.HasFrameTime)
        {
            milliseconds = _gpu.LastFrameMs;
            return true;
        }

        if (!gpu && _renderer.HasFrameTime)
        {
            milliseconds = _renderer.LastFrameMs;
            return true;
        }

        milliseconds = 0;
        return false;
    }

    static string FormatFrameTime(double milliseconds)
    {
        if (milliseconds < 100)
            return $"{milliseconds:0.0} ms";
        if (milliseconds < 10_000)
            return $"{milliseconds:0} ms";
        return $"{milliseconds / 1000:0.00} s";
    }

    RenderRequest FrameRequest(int width, int height)
        => new(
            width,
            height,
            _view.CenterX,
            _view.CenterY,
            _view.Scale,
            _settings.Power,
            _settings.MaxIterations,
            _settings.Palette,
            _settings.Mode,
            _settings.Smooth,
            _settings.Interior,
            _settings.Density,
            _settings.Phase,
            _settings.Julia,
            _settings.JuliaRe,
            _settings.JuliaIm,
            false,
            true);

    public void Dispose()
    {
        _gpu.Dispose();
        _renderer.Dispose();
    }
}
