using System.Text;
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
    string? _pendingInteractivePath;
    SsaaMode? _restoreSsaa;

    public bool ForceFinalQuality => _screenshotPath != null || _pendingInteractivePath != null;
    public bool IsSavingInteractive => _pendingInteractivePath != null;

    /// <summary>
    /// Resolves the initial SSAA mode for a launch. CLI --screenshot captures automatically
    /// upgrade to the highest SSAA tier unless an explicit --ssaa flag was provided.
    /// </summary>
    public static SsaaMode ResolveLaunchSsaa(string? screenshotPath, SsaaMode? explicitSsaa)
    {
        if (explicitSsaa.HasValue)
            return explicitSsaa.Value;

        return screenshotPath != null ? SsaaPattern.Highest : SsaaMode.Off;
    }

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

    public bool HandleKeys(RenderingFeature rendering, ColoringMode mode, string? bookmarkName = null)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_S))
            return SaveInteractive(rendering, mode, bookmarkName);

        return false;
    }

    public static string SanitizeFileNameSlug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        var sb = new StringBuilder(Math.Min(title.Length, 80));
        bool lastWasDash = false;
        foreach (char ch in title.Trim())
        {
            if (sb.Length >= 80)
                break;

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (ch is '\'' or '^')
            {
                // Drop apostrophes and exponent carets cleanly (e.g., z^3 -> z3).
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        return sb.ToString().TrimEnd('-');
    }

    public static string BuildInteractiveCapturePath(string? bookmarkName, string directory = "captures")
    {
        string slug = SanitizeFileNameSlug(bookmarkName);
        if (slug.Length == 0)
            return Path.Combine(directory, $"multibrot-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        string candidate = Path.Combine(directory, $"{slug}.png");
        if (!File.Exists(candidate))
            return candidate;

        for (int suffix = 2; suffix < 10_000; suffix++)
        {
            string numbered = Path.Combine(directory, $"{slug}-{suffix}.png");
            if (!File.Exists(numbered))
                return numbered;
        }

        return Path.Combine(directory, $"{slug}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
    }

    public bool SaveInteractive(RenderingFeature rendering, ColoringMode mode, string? bookmarkName = null)
    {
        Directory.CreateDirectory("captures");
        string path = BuildInteractiveCapturePath(bookmarkName);
        bool boosted = false;
        if (rendering.Ssaa != SsaaPattern.Highest)
        {
            _restoreSsaa ??= rendering.Ssaa;
            rendering.SetSsaa(SsaaPattern.Highest);
            boosted = true;
        }

        if (!boosted && rendering.IsReadyForCapture(mode))
        {
            _pendingInteractivePath = null;
            rendering.Save(path, mode);
            RestoreSsaaIfNeeded(rendering);
            return false;
        }

        _pendingInteractivePath = path;
        return true;
    }

    public bool FlushPendingInteractiveSave(RenderingFeature rendering, ColoringMode mode)
    {
        if (_pendingInteractivePath == null || !rendering.IsReadyForCapture(mode))
            return false;

        string path = _pendingInteractivePath;
        _pendingInteractivePath = null;
        rendering.Save(path, mode);
        return RestoreSsaaIfNeeded(rendering);
    }

    bool RestoreSsaaIfNeeded(RenderingFeature rendering)
    {
        if (!_restoreSsaa.HasValue)
            return false;

        SsaaMode previous = _restoreSsaa.Value;
        _restoreSsaa = null;
        if (rendering.Ssaa != previous)
        {
            rendering.SetSsaa(previous);
            return true;
        }

        return false;
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

        return _screenshotPath != null && DateTime.UtcNow - _started > TimeSpan.FromSeconds(60);
    }
}
