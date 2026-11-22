using System.Diagnostics;
using System.Numerics;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Rendering;

/// <summary>
/// Renders the set off the UI thread with orbit-buffer caching for instant recoloring,
/// reusable frame buffers, dynamic row scheduling, real-axis symmetry, and deep-zoom perturbation.
/// </summary>
public sealed class CpuFractalRenderer : IFractalBackend
{
    readonly record struct OrbitKey(
        int Width,
        int Height,
        ViewportState View,
        FormulaParams Formula,
        bool UseSimd,
        bool NeedsMinRadius,
        bool NeedsNormals,
        OrbitTrapMode OrbitTrap,
        ReferenceOrbit? ReferenceOrbit);

    readonly object _gate = new();
    RenderRequest? _latest;
    ReferenceOrbit? _latestOrbit;
    int _generation;
    bool _running;
    bool _inflightIsFinal;

    Color[]? _readyPixels;
    Color[]? _sparePixels;
    Color[]? _uploadPixels;
    int _readyWidth;
    int _readyHeight;

    float[]? _mu;
    float[]? _minRadius;
    float[]? _trapDist;
    float[]? _normalX;
    float[]? _normalY;
    OrbitKey? _cachedOrbitKey;

    Texture _texture;
    bool _hasTexture;
    int _textureWidth;
    int _textureHeight;

    public bool IsAvailable => true;
    public bool IsBusy { get; private set; }
    public bool HasImage => _hasTexture;
    public bool HasFrameTime { get; private set; }
    public double LastFrameMs { get; private set; }

    public void Request(RenderRequest request, ReferenceOrbit? referenceOrbit = null)
    {
        if (request.Width < 1 || request.Height < 1) return;

        lock (_gate)
        {
            _latest = request;
            _latestOrbit = referenceOrbit;
            if (_running && _inflightIsFinal && !request.FinalQuality)
                _generation++;

            if (!_running)
                StartLocked();
        }
    }

    void StartLocked()
    {
        RenderRequest request = _latest!.Value;
        ReferenceOrbit? orbit = _latestOrbit;
        _latest = null;
        _latestOrbit = null;
        _running = true;
        _inflightIsFinal = request.FinalQuality;
        IsBusy = true;
        int generation = _generation;
        Task.Run(() => Render(request, orbit, generation));
    }

    void Render(RenderRequest request, ReferenceOrbit? orbit, int generation)
    {
        long started = Stopwatch.GetTimestamp();
        Color[]? pixels = Compute(request, orbit, generation);
        lock (_gate)
        {
            if (pixels != null)
            {
                if (_readyPixels != null)
                    _sparePixels = _readyPixels;
                _readyPixels = pixels;
                _readyWidth = request.Width;
                _readyHeight = request.Height;
                LastFrameMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                HasFrameTime = true;
            }

            _running = false;
            if (_latest != null)
                StartLocked();
            else
                IsBusy = false;
        }
    }

    Color[]? Compute(RenderRequest request, ReferenceOrbit? orbit, int generation)
    {
        int width = request.Width;
        int height = request.Height;
        int count = width * height;
        bool needsMinRadius = request.Coloring.Interior == InteriorStyle.Glow;
        bool needsNormals = request.Coloring.NormalLighting;
        OrbitTrapMode orbitTrap = request.Coloring.OrbitTrap;
        bool useShaded = needsNormals || orbitTrap != OrbitTrapMode.None;

        var orbitKey = new OrbitKey(
            width,
            height,
            request.View,
            request.Formula,
            !useShaded && request.UseSimd && SimdMultibrot.IsSupported,
            needsMinRadius,
            needsNormals,
            orbitTrap,
            orbit);

        if (_mu == null || _mu.Length < count)
        {
            _mu = new float[count];
            _minRadius = new float[count];
            _cachedOrbitKey = null;
        }

        if (useShaded && (_trapDist == null || _trapDist.Length < count))
        {
            _trapDist = new float[count];
            _normalX = new float[count];
            _normalY = new float[count];
            _cachedOrbitKey = null;
        }

        float[] mu = _mu;
        float[] minRadius = _minRadius!;

        if (_cachedOrbitKey != orbitKey)
        {
            if (!EvaluateOrbits(request, orbit, generation, needsMinRadius, needsNormals, orbitTrap, mu, minRadius, _trapDist, _normalX, _normalY))
            {
                _cachedOrbitKey = null;
                return null;
            }

            _cachedOrbitKey = orbitKey;
        }

        Color[]? recycled;
        lock (_gate)
        {
            recycled = _sparePixels;
            _sparePixels = null;
        }

        Color[] pixels = recycled != null && recycled.Length >= count
            ? recycled
            : new Color[count];

        ReadOnlySpan<float> trapSpan = orbitTrap != OrbitTrapMode.None && _trapDist != null ? _trapDist.AsSpan(0, count) : ReadOnlySpan<float>.Empty;
        ReadOnlySpan<float> nxSpan = needsNormals && _normalX != null ? _normalX.AsSpan(0, count) : ReadOnlySpan<float>.Empty;
        ReadOnlySpan<float> nySpan = needsNormals && _normalY != null ? _normalY.AsSpan(0, count) : ReadOnlySpan<float>.Empty;

        Palette.Map(
            mu.AsSpan(0, count),
            minRadius.AsSpan(0, count),
            trapSpan,
            nxSpan,
            nySpan,
            pixels.AsSpan(0, count),
            request.Coloring,
            request.Formula.MaxIterations,
            width,
            height);

        return pixels;
    }

    bool EvaluateOrbits(
        RenderRequest request,
        ReferenceOrbit? orbit,
        int generation,
        bool needsMinRadius,
        bool needsNormals,
        OrbitTrapMode orbitTrap,
        float[] mu,
        float[] minRadius,
        float[]? trapDist,
        float[]? normalX,
        float[]? normalY)
    {
        int width = request.Width;
        int height = request.Height;
        FormulaParams formula = request.Formula;
        bool usePerturbation = orbit != null && request.View.IsDeepZoom;
        bool useShaded = needsNormals || orbitTrap != OrbitTrapMode.None;
        int trapMode = (int)orbitTrap;

        FloatExp refOffsetX = FloatExp.Zero;
        FloatExp refOffsetY = FloatExp.Zero;
        FloatExp halfHExact = request.View.ScaleExact;
        FloatExp halfWExact = request.View.HalfWidthExact(width, height);
        if (usePerturbation)
        {
            int prec = orbit!.PrecisionBits;
            refOffsetX = BigFloat.Sub(request.View.CenterXExact, orbit.RefRe, prec).ToFloatExp();
            refOffsetY = BigFloat.Sub(request.View.CenterYExact, orbit.RefIm, prec).ToFloatExp();
        }

        double halfH = request.View.Scale;
        double halfW = request.View.HalfWidth(width, height);
        double originX = request.View.CenterX;
        double originY = request.View.CenterY;
        bool useSimd = !useShaded && request.UseSimd && SimdMultibrot.IsSupported;

        bool mirrorY = !usePerturbation
            && !formula.Julia
            && Math.Abs(originY) < 1e-15
            && FormulaParams.TryGetIntegerExponent(formula.Power, out _);
        int rowLimit = mirrorY ? (height + 1) / 2 : height;

        const int chunk = 4;
        int nextRow = 0;
        int abandoned = 0;
        int workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (rowLimit + chunk - 1) / chunk));

        Parallel.For(0, workers, _ =>
        {
            while (Volatile.Read(ref abandoned) == 0)
            {
                int start = Interlocked.Add(ref nextRow, chunk) - chunk;
                if (start >= rowLimit)
                    break;

                if (Volatile.Read(ref _generation) != generation)
                {
                    Volatile.Write(ref abandoned, 1);
                    break;
                }

                int end = Math.Min(start + chunk, rowLimit);
                for (int y = start; y < end; y++)
                {
                    if (useShaded)
                    {
                        if (usePerturbation)
                            PerturbationFormula.EvaluateShadedRow(width, height, y, refOffsetX, refOffsetY, halfWExact, halfHExact, orbit!, formula, mu, minRadius, trapDist!, normalX!, normalY!, needsMinRadius, needsNormals, trapMode);
                        else
                            MultibrotFormula.EvaluateShadedRow(width, height, y, originX, originY, halfW, halfH, formula, mu, minRadius, trapDist!, normalX!, normalY!, needsMinRadius, needsNormals, trapMode);
                    }
                    else if (usePerturbation)
                    {
                        PerturbationFormula.EvaluateRow(width, height, y, refOffsetX, refOffsetY, halfWExact, halfHExact, orbit!, formula, mu, minRadius, needsMinRadius);
                    }
                    else if (useSimd)
                    {
                        SimdMultibrot.EvaluateRow(width, height, y, originX, originY, halfW, halfH, formula, mu, minRadius, needsMinRadius);
                    }
                    else
                    {
                        MultibrotFormula.EvaluateScalarRow(width, height, y, originX, originY, halfW, halfH, formula, mu, minRadius, needsMinRadius);
                    }

                    if (mirrorY)
                    {
                        int mirror = height - 1 - y;
                        if (mirror != y)
                        {
                            int srcOff = y * width;
                            int dstOff = mirror * width;
                            Array.Copy(mu, srcOff, mu, dstOff, width);
                            Array.Copy(minRadius, srcOff, minRadius, dstOff, width);
                            if (useShaded)
                            {
                                Array.Copy(trapDist!, srcOff, trapDist!, dstOff, width);
                                Array.Copy(normalX!, srcOff, normalX!, dstOff, width);
                                for (int x = 0; x < width; x++)
                                    normalY![dstOff + x] = -normalY[srcOff + x];
                            }
                        }
                    }
                }
            }
        });

        return Volatile.Read(ref abandoned) == 0 && Volatile.Read(ref _generation) == generation;
    }

    public void Upload()
    {
        Color[]? pixels;
        int width;
        int height;
        lock (_gate)
        {
            pixels = _readyPixels;
            if (pixels == null) return;

            width = _readyWidth;
            height = _readyHeight;
            _readyPixels = null;
            if (_uploadPixels != null)
                _sparePixels = _uploadPixels;
            _uploadPixels = pixels;
        }

        EnsureTexture(width, height);
        unsafe
        {
            fixed (Color* pointer = pixels)
                Raylib.UpdateTexture(_texture, pointer);
        }
    }

    public void Draw(int canvasWidth, int canvasHeight)
    {
        if (!_hasTexture || canvasWidth < 1 || canvasHeight < 1)
        {
            Raylib.DrawRectangle(0, 0, Math.Max(canvasWidth, 1), Math.Max(canvasHeight, 1), PanelTheme.CanvasBackground);
            return;
        }

        var source = new Rectangle { x = 0, y = 0, width = _textureWidth, height = _textureHeight };
        var destination = new Rectangle { x = 0, y = 0, width = canvasWidth, height = canvasHeight };
        Raylib.DrawTexturePro(_texture, source, destination, Vector2.Zero, 0, new Color(255, 255, 255, 255));
    }

    public void Save(string path)
    {
        if (!_hasTexture) return;
        Image image = Raylib.LoadImageFromTexture(_texture);
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
    }

    void EnsureTexture(int width, int height)
    {
        if (_hasTexture && _textureWidth == width && _textureHeight == height) return;
        if (_hasTexture) Raylib.UnloadTexture(_texture);

        Image image = Raylib.GenImageColor(width, height, PanelTheme.CanvasBackground);
        _texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        Raylib.SetTextureFilter(_texture, TextureFilter.TEXTURE_FILTER_BILINEAR);
        _textureWidth = width;
        _textureHeight = height;
        _hasTexture = true;
    }

    public void Dispose()
    {
        if (_hasTexture)
        {
            Raylib.UnloadTexture(_texture);
            _hasTexture = false;
        }
    }
}
