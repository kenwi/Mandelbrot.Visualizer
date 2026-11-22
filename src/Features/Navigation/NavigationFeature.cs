using System.Numerics;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Navigation;

/// <summary>
/// Vertical slice owning complex-plane pan/zoom/reset, mouse and arrow input,
/// preview-quality interaction debounce, and viewport coordinate telemetry.
/// </summary>
public sealed class NavigationFeature
{
    readonly FractalView _view = new();

    bool _dragging;
    double _lastClickTime = -1;
    Vector2 _lastCursor;
    bool _hasCursor;
    DateTime _interactUntil = DateTime.MinValue;

    public ViewportState State => _view.Snapshot;
    public bool IsInteracting => _dragging || DateTime.UtcNow < _interactUntil;

    public void Touch() => _interactUntil = DateTime.UtcNow.AddMilliseconds(180);

    public void ResetFor(bool julia) => _view.ResetFor(julia);

    public void SetExact(BigFloat centerX, BigFloat centerY, FloatExp scale)
        => _view.SetExact(centerX, centerY, scale);

    public bool HandlePointer(int canvasWidth, int canvasHeight, bool panelVisible)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        bool onCanvas = !panelVisible || mouse.X < canvasWidth;
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

        if (onCanvas)
        {
            _lastCursor = mouse;
            _hasCursor = true;
        }

        return changed;
    }

    public bool HandleKeys(int canvasWidth, int canvasHeight, bool julia)
    {
        bool changed = false;
        double aspect = canvasWidth / (double)Math.Max(canvasHeight, 1);

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_R))
        {
            _view.ResetFor(julia);
            changed = true;
        }

        const double nudge = 0.02;
        bool shift = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
        bool up = Raylib.IsKeyDown(KeyboardKey.KEY_UP);
        bool down = Raylib.IsKeyDown(KeyboardKey.KEY_DOWN);

        if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT))
        {
            _view.NudgeCenter(-nudge * aspect, 0);
            changed = true;
            Touch();
        }

        if (Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT))
        {
            _view.NudgeCenter(nudge * aspect, 0);
            changed = true;
            Touch();
        }

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
            if (up)
            {
                _view.NudgeCenter(0, nudge);
                changed = true;
                Touch();
            }

            if (down)
            {
                _view.NudgeCenter(0, -nudge);
                changed = true;
                Touch();
            }
        }

        return changed;
    }

    public bool TryGetLastCursorComplex(int canvasWidth, int canvasHeight, out double re, out double im)
    {
        if (!_hasCursor)
        {
            re = 0;
            im = 0;
            return false;
        }

        _view.ScreenToComplex(_lastCursor.X, _lastCursor.Y, canvasWidth, canvasHeight, out re, out im);
        return true;
    }

    public bool TryGetLastCursorNormalized(int canvasWidth, int canvasHeight, out float x, out float y)
    {
        if (!_hasCursor || canvasWidth < 1 || canvasHeight < 1)
        {
            x = 0f;
            y = 0f;
            return false;
        }

        float aspect = canvasWidth / (float)canvasHeight;
        x = (_lastCursor.X / canvasWidth - 0.5f) * aspect;
        y = -(_lastCursor.Y / canvasHeight - 0.5f);
        return true;
    }

    public bool TryGetHoveredPixel(int canvasWidth, int canvasHeight, out int pixelX, out int pixelY)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        if (mouse.X < 0 || mouse.X >= canvasWidth || mouse.Y < 0 || mouse.Y >= canvasHeight)
        {
            pixelX = 0;
            pixelY = 0;
            return false;
        }

        pixelX = (int)mouse.X;
        pixelY = (int)mouse.Y;
        return true;
    }

    public void DrawCursorTelemetry(ref PanelLayout layout, int canvasWidth, int canvasHeight)
    {
        if (TryGetHoveredPixel(canvasWidth, canvasHeight, out int px, out int py))
        {
            _view.ScreenToComplex(px + 0.5, py + 0.5, canvasWidth, canvasHeight, out double cursorRe, out double cursorIm);
            layout.Label($"Cursor  {cursorRe:0.######}  {FormatSigned(cursorIm)} i");
        }
        else
        {
            layout.Label("Cursor  over the panel");
        }
    }

    public void DrawViewportTelemetry(ref PanelLayout layout, double power)
    {
        layout.Label($"Center  {_view.CenterX.FormatSummary()}  {FormatSigned(_view.CenterY.ToDouble())} i");
        layout.Label($"Zoom  {_view.ZoomDisplay}    n = {power:0.####}");
    }

    static string FormatSigned(double value)
        => value >= 0 ? $"+ {value:0.####}" : $"- {Math.Abs(value):0.####}";
}
