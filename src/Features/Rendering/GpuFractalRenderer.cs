using System.Numerics;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Rendering;

/// <summary>
/// Draws the set with an embedded GLSL fragment shader and a 256x8 PaletteLut texture.
/// Integer powers iterate in double-float; fractional powers use single-precision polar form.
/// </summary>
public sealed class GpuFractalRenderer : IFractalBackend
{
    const int PolarExponent = 100;

    readonly PaletteLut _paletteLut = new();
    readonly GpuFrameTimer _timer = new();

    Shader _shader;
    RenderTexture _target;
    bool _shaderReady;
    bool _hasTarget;
    int _width;
    int _height;
    bool _disposed;

    int _resolutionLoc;
    int _centerLoc;
    int _juliaCLoc;
    int _scaleLoc;
    int _powerLoc;
    int _exponentLoc;
    int _maxIterLoc;
    int _paletteLoc;
    int _paletteLutLoc;
    int _smoothLoc;
    int _interiorLoc;
    int _densityLoc;
    int _phaseLoc;
    int _juliaLoc;

    public bool IsAvailable { get; private set; }
    public bool HasImage { get; private set; }
    public bool HasFrameTime => _timer.HasFrameTime;
    public double LastFrameMs => _timer.LastFrameMs;

    public void Load()
    {
        if (_disposed || _shaderReady) return;

        string vertexSource = LoadEmbeddedResource("Shaders.multibrot.vert");
        string fragmentSource = LoadEmbeddedResource("Shaders.multibrot.frag");

        _shader = Raylib.LoadShaderFromMemory(vertexSource, fragmentSource);
        _shaderReady = true;
        if (!Raylib.IsShaderReady(_shader))
        {
            Console.Error.WriteLine("Multibrot shader failed to compile; rendering on the CPU.");
            if (_shader.id != 0) Raylib.UnloadShader(_shader);
            _shaderReady = false;
            IsAvailable = false;
            return;
        }

        _paletteLut.Load();

        _resolutionLoc = Raylib.GetShaderLocation(_shader, "resolution");
        _centerLoc = Raylib.GetShaderLocation(_shader, "center");
        _juliaCLoc = Raylib.GetShaderLocation(_shader, "juliaC");
        _scaleLoc = Raylib.GetShaderLocation(_shader, "scale");
        _powerLoc = Raylib.GetShaderLocation(_shader, "power");
        _exponentLoc = Raylib.GetShaderLocation(_shader, "exponent");
        _maxIterLoc = Raylib.GetShaderLocation(_shader, "maxIter");
        _paletteLoc = Raylib.GetShaderLocation(_shader, "paletteId");
        _paletteLutLoc = Raylib.GetShaderLocation(_shader, "paletteLut");
        _smoothLoc = Raylib.GetShaderLocation(_shader, "smoothColor");
        _interiorLoc = Raylib.GetShaderLocation(_shader, "interiorStyle");
        _densityLoc = Raylib.GetShaderLocation(_shader, "density");
        _phaseLoc = Raylib.GetShaderLocation(_shader, "phase");
        _juliaLoc = Raylib.GetShaderLocation(_shader, "juliaSet");
        IsAvailable = true;
    }

    public void Draw(int width, int height, in RenderRequest request)
    {
        if (!IsAvailable || width < 1 || height < 1) return;

        EnsureTarget(width, height);
        SetUniforms(width, height, request);
        _timer.Poll();
        bool timing = _timer.Begin();

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(PanelTheme.CanvasBackground);
        Raylib.BeginShaderMode(_shader);
        Raylib.SetShaderValueTexture(_shader, _paletteLutLoc, _paletteLut.Texture);
        Raylib.DrawRectangle(0, 0, width, height, new Color(255, 255, 255, 255));
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
        _timer.End(timing);
        HasImage = true;

        var source = new Rectangle { x = 0, y = 0, width = width, height = -height };
        Raylib.DrawTextureRec(_target.texture, source, Vector2.Zero, new Color(255, 255, 255, 255));
    }

    public void Save(string path)
    {
        if (!HasImage) return;
        Image image = Raylib.LoadImageFromTexture(_target.texture);
        unsafe
        {
            Raylib.ImageFlipVertical(&image);
        }

        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hasTarget)
        {
            Raylib.UnloadRenderTexture(_target);
            _hasTarget = false;
        }

        if (_shaderReady)
        {
            Raylib.UnloadShader(_shader);
            _shaderReady = false;
        }

        _paletteLut.Dispose();
        _timer.Dispose();
        IsAvailable = false;
        HasImage = false;
    }

    void EnsureTarget(int width, int height)
    {
        if (_hasTarget && _width == width && _height == height) return;
        if (_hasTarget) Raylib.UnloadRenderTexture(_target);

        _target = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureFilter(_target.texture, TextureFilter.TEXTURE_FILTER_POINT);
        _width = width;
        _height = height;
        _hasTarget = true;
    }

    void SetUniforms(int width, int height, in RenderRequest request)
    {
        var resolution = new Vector2(width, height);
        Split(request.View.CenterX, out float reHi, out float reLo);
        Split(request.View.CenterY, out float imHi, out float imLo);
        Split(request.Formula.JuliaRe, out float cReHi, out float cReLo);
        Split(request.Formula.JuliaIm, out float cImHi, out float cImLo);

        var center = new Vector4(reHi, reLo, imHi, imLo);
        var juliaC = new Vector4(cReHi, cReLo, cImHi, cImLo);
        float scale = (float)request.View.Scale;
        float power = (float)request.Formula.Power;
        int exponent = FormulaParams.TryGetIntegerExponent(request.Formula.Power, out int n) ? n : PolarExponent;
        int maxIter = Math.Clamp(request.Formula.MaxIterations, 1, FormulaParams.IterationMax);
        int palette = request.Coloring.Palette;
        int smooth = request.Coloring.Smooth ? 1 : 0;
        int interior = (int)request.Coloring.Interior;
        float density = (float)request.Coloring.Density;
        float phase = (float)request.Coloring.Phase;
        int julia = request.Formula.Julia ? 1 : 0;

        Set(_resolutionLoc, resolution, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        Set(_centerLoc, center, ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        Set(_juliaCLoc, juliaC, ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        Set(_scaleLoc, scale, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_powerLoc, power, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_exponentLoc, exponent, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_maxIterLoc, maxIter, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_paletteLoc, palette, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_smoothLoc, smooth, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_interiorLoc, interior, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_densityLoc, density, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_phaseLoc, phase, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_juliaLoc, julia, ShaderUniformDataType.SHADER_UNIFORM_INT);
    }

    void Set<T>(int location, T value, ShaderUniformDataType type) where T : unmanaged
        => Raylib.SetShaderValue(_shader, location, value, type);

    static void Split(double value, out float hi, out float lo)
    {
        hi = (float)value;
        lo = (float)(value - hi);
    }

    static string LoadEmbeddedResource(string logicalName)
    {
        using Stream stream = typeof(GpuFractalRenderer).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Missing embedded shader resource '{logicalName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
