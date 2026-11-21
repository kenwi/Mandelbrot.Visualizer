using System.Numerics;
using Mandelbrot.Visualizer.Coloring;
using Mandelbrot.Visualizer.Formula;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Rendering;

/// <summary>
/// Renders the set off the UI thread and uploads the finished frame on the main thread.
/// A full-quality pass is abandoned when a navigation preview is requested. Previews run to completion.
/// </summary>
public sealed class FractalRenderer : IDisposable
{
    readonly object _gate = new();
    RenderRequest? _latest;
    int _generation;
    bool _running;
    bool _inflightIsFinal;
    Color[]? _ready;
    int _readyWidth;
    int _readyHeight;
    Texture _texture;
    bool _hasTexture;
    int _textureWidth;
    int _textureHeight;

    public bool IsBusy { get; private set; }
    public bool HasImage => _hasTexture;

    public void Request(RenderRequest request)
    {
        if (request.Width < 1 || request.Height < 1) return;

        lock (_gate)
        {
            _latest = request;
            if (_running && _inflightIsFinal && !request.FinalQuality)
                _generation++;

            if (!_running)
                StartLocked();
        }
    }

    void StartLocked()
    {
        RenderRequest request = _latest!.Value;
        _latest = null;
        _running = true;
        _inflightIsFinal = request.FinalQuality;
        IsBusy = true;
        int generation = _generation;
        Task.Run(() => Render(request, generation));
    }

    void Render(RenderRequest request, int generation)
    {
        Color[]? pixels = Compute(request, generation);
        lock (_gate)
        {
            if (pixels != null)
            {
                _ready = pixels;
                _readyWidth = request.Width;
                _readyHeight = request.Height;
            }

            _running = false;
            if (_latest != null)
                StartLocked();
            else
                IsBusy = false;
        }
    }

    Color[]? Compute(RenderRequest request, int generation)
    {
        int width = request.Width;
        int height = request.Height;
        int count = width * height;
        var mu = new float[count];
        var minRadius = new float[count];
        double halfH = request.Scale;
        double halfW = request.Scale * width / (double)height;
        double originX = request.CenterX;
        double originY = request.CenterY;
        double power = request.Power;
        int maxIterations = request.MaxIterations;
        bool julia = request.Julia;
        double juliaRe = request.JuliaRe;
        double juliaIm = request.JuliaIm;
        int abandoned = 0;

        Parallel.For(0, height, y =>
        {
            if (Volatile.Read(ref abandoned) != 0) return;
            if ((y & 15) == 0 && Volatile.Read(ref _generation) != generation)
            {
                Volatile.Write(ref abandoned, 1);
                return;
            }

            double cy = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                double cx = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
                MultibrotFormula.Escape escape = julia
                    ? MultibrotFormula.Evaluate(cx, cy, juliaRe, juliaIm, power, maxIterations)
                    : MultibrotFormula.Evaluate(0, 0, cx, cy, power, maxIterations);
                mu[row + x] = escape.Mu;
                minRadius[row + x] = escape.MinRadius;
            }
        });

        if (Volatile.Read(ref abandoned) != 0 || Volatile.Read(ref _generation) != generation)
            return null;

        var pixels = new Color[count];
        Palette.Map(
            mu,
            minRadius,
            pixels,
            request.Palette,
            request.Mode,
            request.Smooth,
            request.Interior,
            request.Density,
            request.Phase,
            maxIterations);
        return pixels;
    }

    public void Upload()
    {
        Color[]? pixels;
        int width;
        int height;
        lock (_gate)
        {
            pixels = _ready;
            width = _readyWidth;
            height = _readyHeight;
            _ready = null;
        }

        if (pixels == null) return;

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
            Raylib.DrawRectangle(0, 0, Math.Max(canvasWidth, 1), Math.Max(canvasHeight, 1), new Color(4, 6, 12, 255));
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

        Image image = Raylib.GenImageColor(width, height, new Color(4, 6, 12, 255));
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
