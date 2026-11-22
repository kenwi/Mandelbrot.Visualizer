using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Rendering;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Capture;

/// <summary>
/// Vertical slice owning interactive PNG exports (S key / Save PNG button)
/// and headless/CLI --screenshot capture automation.
/// </summary>
public sealed class CaptureFeature(string? screenshotPath)
{
    readonly string? _screenshotPath = screenshotPath;

    DateTime _started;
    int _stableWidth = -1;
    int _stableHeight = -1;
    int _stableFrames;
    string? _pendingExportPath;

    public bool ForceFinalQuality => _screenshotPath != null;

    public void StartClock() => _started = DateTime.UtcNow;

    public void TrackWindowSize(int screenWidth, int screenHeight)
    {
        if (screenWidth == _stableWidth && screenHeight == _stableHeight)
        {
            _stableFrames++;
        }
        else
        {
            _stableWidth = screenWidth;
            _stableHeight = screenHeight;
            _stableFrames = 1;
        }
    }

    public void HandleKeys(RenderingFeature rendering, ColoringMode mode)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_S))
            SaveInteractive(rendering, mode);
    }

    public void SaveInteractive(RenderingFeature rendering, ColoringMode mode)
    {
        Directory.CreateDirectory("captures");
        string path = Path.Combine("captures", $"multibrot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        rendering.Save(path, mode);
    }

    public void CaptureWindowIfReady(RenderingFeature rendering, ColoringMode mode)
    {
        if (_screenshotPath is not string shot || _stableFrames < 45 || !rendering.IsReadyForCapture(mode))
            return;

        _pendingExportPath = shot;
        RlGl.rlDrawRenderBatchActive();
        Image screen = Raylib.LoadImageFromScreen();
        Raylib.ExportImage(screen, Path.ChangeExtension(shot, ".window.png"));
        Raylib.UnloadImage(screen);
    }

    public bool ShouldExitAfterFrame(RenderingFeature rendering, ColoringMode mode)
    {
        if (_pendingExportPath != null)
        {
            rendering.Save(_pendingExportPath, mode);
            return true;
        }

        return _screenshotPath != null && DateTime.UtcNow - _started > TimeSpan.FromSeconds(30);
    }
}
