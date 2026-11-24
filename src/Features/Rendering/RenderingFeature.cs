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
    bool _lastPrecisionLimitIsFamily;
    bool _lastDrawnOnGpu;
    bool _holdGpuUntilCpuReady;

    public bool UseGpu { get; private set; } = true;
    public bool UseSimd { get; private set; }
    public SsaaMode Ssaa { get; private set; } = SsaaMode.Off;
    public ReferenceOrbit? ActiveOrbit => _submittedOrbit;

    public void SetUseGpu(bool value) => UseGpu = value;
    public void SetSsaa(SsaaMode mode) => Ssaa = mode;

    public void Load() => _gpu.Load();

    public bool UsingGpu(ColoringMode mode)
        => UseGpu && _gpu.IsAvailable && mode != ColoringMode.Histogram;

    public bool RenderPreviewGpu(RenderTexture target, int width, int height, in RenderRequest request)
        => UseGpu && _gpu.IsAvailable && _gpu.RenderOffscreen(target, width, height, request);

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

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_A))
        {
            Ssaa = (SsaaMode)(((int)Ssaa + 1) % SsaaPattern.ModeCount);
            changed = true;
        }

        return changed;
    }

    public bool DrawPanel(ref PanelLayout layout)
    {
        bool changed = false;

        bool gpu = UseGpu;
        bool simd = UseSimd;
        if (layout.TwoChecks("GPU shader", ref gpu, "SIMD", ref simd, splitRatio: 0.56f))
        {
            UseGpu = gpu;
            UseSimd = simd;
            changed = true;
        }

        changed |= DrawSsaaCombo(ref layout);
        return changed;
    }

    public bool DrawSsaaCombo(ref PanelLayout layout)
    {
        int ssaa = (int)Ssaa;
        if (layout.LabeledCombo("SSAA", SsaaPattern.ComboItems, ref ssaa))
        {
            Ssaa = (SsaaMode)ssaa;
            return true;
        }

        return false;
    }

    public void PrepareFrame(ColoringMode mode)
    {
        if (!UsingGpu(mode) && _cpu.Upload())
            _holdGpuUntilCpuReady = false;
    }

    public void DrawCanvas(
        int canvasWidth,
        int canvasHeight,
        in ViewportState view,
        in FormulaParams formula,
        in ColoringParams coloring,
        bool finalQuality)
    {
        bool wantsPerturb = view.IsDeepZoom && FormulaParams.SupportsPerturbation(formula, out _);
        _lastPrecisionLimit = view.IsDeepZoom && !wantsPerturb;
        _lastPrecisionLimitIsFamily = _lastPrecisionLimit && formula.Family != FractalFamily.Multibrot;
        _lastDeepPerturbation = wantsPerturb;

        if (UsingGpu(coloring.Mode))
        {
            if (!_lastDrawnOnGpu)
            {
                _cpu.CancelPending();
                _hasSubmitted = false;
            }

            ReferenceOrbit? orbit = wantsPerturb
                ? _orbitCache.TryGetOrSchedule(view.CenterXExact, view.CenterYExact, view.ScaleExact, formula)
                : null;

            if (wantsPerturb && orbit == null)
            {
                // Reference orbit is computing off-thread; keep showing the most recently drawn frame until ready.
                if (!_lastDrawnOnGpu && _cpu.HasImage)
                    _cpu.Draw(canvasWidth, canvasHeight);
                else
                    _gpu.BlitCached(canvasWidth, canvasHeight);
                return;
            }

            _submittedOrbit = orbit;
            var request = new RenderRequest(canvasWidth, canvasHeight, view, formula, coloring, false, finalQuality, Ssaa);
            _gpu.Draw(canvasWidth, canvasHeight, request, orbit);
            _lastDrawnOnGpu = _gpu.HasImage;
            _holdGpuUntilCpuReady = false;
        }
        else
        {
            if (_lastDrawnOnGpu)
            {
                _lastDrawnOnGpu = false;
                SsaaMode effectiveSsaa = finalQuality ? Ssaa : SsaaMode.Off;
                _holdGpuUntilCpuReady = _gpu.HasImage && !_cpu.MatchesUploadedScene(view, formula, coloring, effectiveSsaa);
            }

            if (_holdGpuUntilCpuReady && _gpu.HasImage)
                _gpu.BlitCached(canvasWidth, canvasHeight);
            else
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

        bool wantsPerturb = view.IsDeepZoom && FormulaParams.SupportsPerturbation(formula, out _);
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

        var request = new RenderRequest(width, height, view, formula, coloring, UseSimd, finalQuality, Ssaa);
        if (_hasSubmitted && request == _submitted && ReferenceEquals(_submittedOrbit, orbit))
            return;

        if (_cpu.HasUploadedRequest(request, orbit))
        {
            _submitted = request;
            _submittedOrbit = orbit;
            _hasSubmitted = true;
            return;
        }

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
            return _gpu.IsReadyForFinalCapture(Ssaa);

        return _hasSubmitted
            && _submitted.FinalQuality
            && _submitted.Ssaa == Ssaa
            && _cpu.HasImage
            && !_cpu.IsBusy
            && !_holdGpuUntilCpuReady;
    }

    public void Save(string path, ColoringMode mode)
    {
        bool useGpuImage = (UsingGpu(mode) || _holdGpuUntilCpuReady) && _gpu.HasImage;
        IFractalBackend backend = useGpuImage ? _gpu : _cpu;
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
        string ssaaTag = Ssaa != SsaaMode.Off ? $" {SsaaPattern.ShortLabel(Ssaa)}" : string.Empty;
        if (_orbitCache.IsComputing)
            return "Computing reference orbit...";
        if (_lastPrecisionLimit)
            return _lastPrecisionLimitIsFamily ? "Precision limit (no deep zoom)" : "Precision limit (fractional n)";
        if (UseGpu && !_gpu.IsAvailable)
            return "No shader, using CPU";
        if (UsingGpu(mode))
        {
            if (_gpu.IsRenderingTiles)
            {
                if (_gpu.TotalSsaaSamples > 1)
                    return $"GPU{ssaaTag} SSAA ({_gpu.CompletedSsaaSamples}/{_gpu.TotalSsaaSamples})...";
                return _lastDeepPerturbation ? "GPU deep tiling..." : "GPU tiling...";
            }

            return _lastDeepPerturbation ? $"GPU deep{ssaaTag}    {fps} fps" : $"GPU{ssaaTag}    {fps} fps";
        }

        if (UseSimd && !SimdMultibrot.IsSupported)
            return "No SIMD, using CPU";

        bool simd = UsingSimd();
        string name = _lastDeepPerturbation ? "CPU deep" : simd ? "CPU SIMD" : "CPU";
        if (_cpu.IsBusy)
        {
            if (_cpu.TotalSsaaSamples > 1)
                return $"{name}{ssaaTag} SSAA ({_cpu.CompletedSsaaSamples}/{_cpu.TotalSsaaSamples})...";
            return _lastDeepPerturbation ? "CPU deep rendering..." : simd ? "SIMD rendering..." : "CPU rendering...";
        }

        if (UseGpu && mode == ColoringMode.Histogram)
            return $"{(simd ? "SIMD" : "CPU")} histogram{ssaaTag}    {fps} fps";
        return $"{name}{ssaaTag}    {fps} fps";
    }

    public void Dispose()
    {
        _orbitCache.Dispose();
        _gpu.Dispose();
        _cpu.Dispose();
    }
}
