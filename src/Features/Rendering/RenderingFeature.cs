using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Rendering;

/// <summary>
/// Vertical slice coordinating GPU and CPU backends, deep-zoom reference-orbit scheduling,
/// preview/final quality request dispatch, status reporting, and G/V controls.
/// </summary>
public sealed class RenderingFeature : IDisposable
{
    readonly CpuFractalRenderer _cpu = new();
    readonly GpuFractalRenderer _gpu = new();
    readonly ReferenceOrbitCache _orbitCache = new();

    RenderRequest _submitted;
    ReferenceOrbit? _submittedOrbit;
    bool _hasSubmitted;
    bool _lastDeepPerturbation;
    bool _lastPrecisionLimit;

    public bool UseGpu { get; private set; } = true;
    public bool UseSimd { get; private set; }
    public ReferenceOrbit? ActiveOrbit => _submittedOrbit;

    public void SetUseGpu(bool value) => UseGpu = value;

    public void Load() => _gpu.Load();

    public bool UsingGpu(ColoringMode mode)
        => UseGpu && _gpu.IsAvailable && mode != ColoringMode.Histogram;

    public bool UsingSimd()
        => UseSimd && SimdMultibrot.IsSupported;

    public string ActiveDeviceLabel(ColoringMode mode)
    {
        if (UsingGpu(mode))
            return _lastDeepPerturbation ? "GPU deep" : "GPU";
        if (_lastDeepPerturbation)
            return "CPU deep";
        return UsingSimd() ? "CPU SIMD" : "CPU";
    }

    public bool HandleKeys()
    {
        bool changed = false;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_G))
        {
            UseGpu = !UseGpu;
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_V))
        {
            UseSimd = !UseSimd;
            changed = true;
        }

        return changed;
    }

    public bool DrawPanel(ref PanelLayout layout)
    {
        bool changed = false;

        bool gpu = UseGpu;
        if (layout.Check("GPU shader", ref gpu))
        {
            UseGpu = gpu;
            changed = true;
        }

        bool simd = UseSimd;
        if (layout.Check("SIMD", ref simd))
        {
            UseSimd = simd;
            changed = true;
        }

        return changed;
    }

    public void PrepareFrame(ColoringMode mode)
    {
        if (!UsingGpu(mode))
            _cpu.Upload();
    }

    public void DrawCanvas(
        int canvasWidth,
        int canvasHeight,
        in ViewportState view,
        in FormulaParams formula,
        in ColoringParams coloring,
        bool finalQuality)
    {
        bool wantsPerturb = view.IsDeepZoom && FormulaParams.SupportsPerturbation(formula.Power, out _);
        _lastPrecisionLimit = view.IsDeepZoom && !wantsPerturb;
        _lastDeepPerturbation = wantsPerturb;

        if (UsingGpu(coloring.Mode))
        {
            ReferenceOrbit? orbit = wantsPerturb
                ? _orbitCache.TryGetOrSchedule(view.CenterXExact, view.CenterYExact, view.ScaleExact, formula)
                : null;

            if (wantsPerturb && orbit == null)
            {
                // Reference orbit is computing off-thread; keep showing the last frame until ready.
                _gpu.BlitCached(canvasWidth, canvasHeight);
                return;
            }

            _submittedOrbit = orbit;
            var request = new RenderRequest(canvasWidth, canvasHeight, view, formula, coloring, false, finalQuality);
            _gpu.Draw(canvasWidth, canvasHeight, request, orbit);
        }
        else
        {
            _cpu.Draw(canvasWidth, canvasHeight);
        }
    }

    public void SubmitIfNeeded(
        int canvasWidth,
        int canvasHeight,
        in ViewportState view,
        in FormulaParams formula,
        in ColoringParams coloring,
        bool finalQuality,
        bool dirty)
    {
        if (UsingGpu(coloring.Mode))
            return;

        bool wantsPerturb = view.IsDeepZoom && FormulaParams.SupportsPerturbation(formula.Power, out _);
        ReferenceOrbit? orbit = wantsPerturb
            ? _orbitCache.TryGetOrSchedule(view.CenterXExact, view.CenterYExact, view.ScaleExact, formula)
            : null;

        if (wantsPerturb && orbit == null)
            return;

        int divisor = finalQuality ? 1 : 4;
        int width = Math.Max(1, canvasWidth / divisor);
        int height = Math.Max(1, canvasHeight / divisor);

        bool sizeChanged = !_hasSubmitted
            || _submitted.Width != width
            || _submitted.Height != height
            || _submitted.FinalQuality != finalQuality
            || !ReferenceEquals(_submittedOrbit, orbit);

        if (!dirty && !sizeChanged)
            return;

        var request = new RenderRequest(width, height, view, formula, coloring, UseSimd, finalQuality);
        if (_hasSubmitted && request == _submitted && ReferenceEquals(_submittedOrbit, orbit))
            return;

        _submitted = request;
        _submittedOrbit = orbit;
        _hasSubmitted = true;
        _cpu.Request(request, orbit);
    }

    public bool IsReadyForCapture(ColoringMode mode)
    {
        if (_orbitCache.IsComputing)
            return false;

        if (UsingGpu(mode))
            return _gpu.HasImage && !_gpu.IsRenderingTiles;

        return _hasSubmitted && _submitted.FinalQuality && _cpu.HasImage && !_cpu.IsBusy;
    }

    public void Save(string path, ColoringMode mode)
    {
        IFractalBackend backend = UsingGpu(mode) && _gpu.HasImage ? _gpu : _cpu;
        backend.Save(path);
    }

    public bool TryGetFrameTime(ColoringMode mode, out double milliseconds)
    {
        IFractalBackend backend = UsingGpu(mode) ? _gpu : _cpu;
        if (backend.HasFrameTime)
        {
            milliseconds = backend.LastFrameMs;
            return true;
        }

        milliseconds = 0;
        return false;
    }

    public string StatusLine(ColoringMode mode)
    {
        int fps = Raylib.GetFPS();
        if (_orbitCache.IsComputing)
            return "Computing reference orbit...";
        if (_lastPrecisionLimit)
            return "Precision limit (fractional n)";
        if (UseGpu && !_gpu.IsAvailable)
            return "No shader, using CPU";
        if (UsingGpu(mode))
        {
            if (_gpu.IsRenderingTiles)
                return _lastDeepPerturbation ? "GPU deep tiling..." : "GPU tiling...";
            return _lastDeepPerturbation ? $"GPU deep    {fps} fps" : $"GPU    {fps} fps";
        }

        if (UseSimd && !SimdMultibrot.IsSupported)
            return "No SIMD, using CPU";

        bool simd = UsingSimd();
        string name = _lastDeepPerturbation ? "CPU deep" : simd ? "CPU SIMD" : "CPU";
        if (UseGpu && mode == ColoringMode.Histogram)
            return $"{(simd ? "SIMD" : "CPU")} histogram    {fps} fps";
        if (_cpu.IsBusy)
            return _lastDeepPerturbation ? "CPU deep rendering..." : simd ? "SIMD rendering..." : "CPU rendering...";
        return $"{name}    {fps} fps";
    }

    public void Dispose()
    {
        _orbitCache.Dispose();
        _gpu.Dispose();
        _cpu.Dispose();
    }
}
