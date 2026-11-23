using System.Diagnostics;
using System.Numerics;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Rendering;

/// <summary>
/// Draws the set with tiered embedded GLSL fragment shaders (f32, double-float, and deep-zoom perturbation),
/// caches rendered frames when idle, and renders high-iteration / deep-zoom frames progressively in tiles.
/// </summary>
public sealed class GpuFractalRenderer : IFractalBackend
{
    const int PolarExponent = 100;
    const double Float32ScaleThreshold = 1e-5;
    const int OrbitTexWidth = 1024;
    const int TileSize = 256;
    const double TileFrameBudgetMs = 10.0;

    readonly PaletteLut _paletteLut = new();
    readonly GpuFrameTimer _timer = new();

    ShaderProgram _f32;
    ShaderProgram _df;
    PerturbProgram _perturb;
    ResolveProgram _resolve;

    RenderTexture _target;
    bool _hasTarget;
    int _width;
    int _height;
    bool _disposed;

    RenderTexture _ssaaAccum;
    bool _hasSsaaAccum;
    int _ssaaAccumWidth;
    int _ssaaAccumHeight;
    bool _ssaaAccumFailed;

    Texture _orbitTexture;
    bool _hasOrbitTexture;
    ReferenceOrbit? _uploadedOrbit;

    RenderRequest? _cachedRequest;
    ReferenceOrbit? _cachedOrbit;
    int _cachedWidth;
    int _cachedHeight;
    int _nextTileIndex;
    int _totalTiles;
    int _nextSampleIndex;
    int _totalSamples = 1;
    int _completedSamples;
    bool _tileComplete = true;

    public bool IsAvailable { get; private set; }
    public bool SupportsPerturbation => _perturb.IsReady;
    public bool HasImage { get; private set; }
    public bool IsRenderingTiles => !_tileComplete;
    public int CompletedSsaaSamples => _completedSamples;
    public int TotalSsaaSamples => _totalSamples;
    public bool HasFrameTime => _timer.HasFrameTime;
    public double LastFrameMs => _timer.LastFrameMs;

    public bool IsReadyForFinalCapture(SsaaMode expectedSsaa)
        => HasImage
        && _tileComplete
        && _cachedRequest is RenderRequest r
        && r.FinalQuality
        && r.Ssaa == expectedSsaa;

    public void Load()
    {
        if (_disposed || IsAvailable) return;

        string vertexSource = LoadEmbeddedResource("Shaders.multibrot.vert");
        string f32Source = LoadEmbeddedResource("Shaders.multibrot_f32.frag");
        string dfSource = LoadEmbeddedResource("Shaders.multibrot.frag");
        string perturbSource = LoadEmbeddedResource("Shaders.multibrot_perturb.frag");
        string resolveSource = LoadEmbeddedResource("Shaders.ssaa_resolve.frag");

        if (!ShaderProgram.TryLoad(vertexSource, dfSource, out _df))
        {
            Console.Error.WriteLine("Multibrot shader failed to compile; rendering on the CPU.");
            IsAvailable = false;
            return;
        }

        if (!ShaderProgram.TryLoad(vertexSource, f32Source, out _f32))
            Console.Error.WriteLine("Fast float32 shader failed to compile; falling back to double-float.");

        if (!PerturbProgram.TryLoad(vertexSource, perturbSource, out _perturb))
            Console.Error.WriteLine("Perturbation shader failed to compile; deep zoom will fall back.");

        if (!ResolveProgram.TryLoad(vertexSource, resolveSource, out _resolve))
            Console.Error.WriteLine("SSAA resolve shader failed to compile; GPU SSAA disabled.");

        _paletteLut.Load();
        IsAvailable = true;
    }

    public void Draw(int width, int height, in RenderRequest request, ReferenceOrbit? referenceOrbit = null)
    {
        if (!IsAvailable || width < 1 || height < 1) return;

        _timer.Poll();

        bool usePerturb = referenceOrbit != null && request.View.IsDeepZoom && _perturb.IsReady;
        SsaaMode effectiveSsaa = request.EffectiveSsaa;
        bool useSsaa = effectiveSsaa != SsaaMode.Off && _resolve.IsReady && EnsureSsaaAccum(width, height);
        ReadOnlySpan<Vector2> offsets = useSsaa
            ? SsaaPattern.GetOffsets(effectiveSsaa)
            : SsaaPattern.GetOffsets(SsaaMode.Off);

        bool requestChanged = !HasImage
            || _cachedWidth != width
            || _cachedHeight != height
            || _cachedRequest != request
            || !ReferenceEquals(_cachedOrbit, usePerturb ? referenceOrbit : null);

        if (requestChanged)
        {
            EnsureTarget(width, height);
            _cachedRequest = request;
            _cachedOrbit = usePerturb ? referenceOrbit : null;
            _cachedWidth = width;
            _cachedHeight = height;

            bool useTiling = request.FinalQuality && (usePerturb || request.Formula.MaxIterations > 8000);
            if (useTiling)
            {
                int cols = (width + TileSize - 1) / TileSize;
                int rows = (height + TileSize - 1) / TileSize;
                _totalTiles = cols * rows;
            }
            else
            {
                _totalTiles = 1;
            }

            _nextTileIndex = 0;
            _totalSamples = offsets.Length;
            _nextSampleIndex = 0;
            _completedSamples = 0;
            _tileComplete = false;
        }

        if (!_tileComplete)
        {
            if (_totalSamples <= 1)
            {
                if (usePerturb)
                {
                    EnsureOrbitTexture(referenceOrbit!);
                    RenderPassPerturb(_target, width, height, request, referenceOrbit!, Vector2.Zero, additive: false, clearTarget: false);
                }
                else
                {
                    RenderPassDirect(_target, width, height, request, Vector2.Zero, additive: false, clearTarget: _nextTileIndex == 0 && _totalTiles == 1);
                }

                if (_nextTileIndex >= _totalTiles)
                {
                    _completedSamples = 1;
                    _tileComplete = true;
                }

                HasImage = true;
            }
            else
            {
                if (usePerturb)
                    EnsureOrbitTexture(referenceOrbit!);

                long frameStart = Stopwatch.GetTimestamp();
                while (_nextSampleIndex < _totalSamples)
                {
                    Vector2 offset = offsets[_nextSampleIndex];
                    bool clearAccum = _nextSampleIndex == 0 && _nextTileIndex == 0;
                    if (usePerturb)
                        RenderPassPerturb(_ssaaAccum, width, height, request, referenceOrbit!, offset, additive: true, clearTarget: clearAccum);
                    else
                        RenderPassDirect(_ssaaAccum, width, height, request, offset, additive: true, clearTarget: clearAccum);

                    if (_nextTileIndex >= _totalTiles)
                    {
                        _nextSampleIndex++;
                        _completedSamples = _nextSampleIndex;
                        _nextTileIndex = 0;
                    }

                    if (Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds >= TileFrameBudgetMs)
                        break;
                }

                if (_completedSamples > 0)
                {
                    ResolveSsaaTarget(width, height, _completedSamples);
                    HasImage = true;
                }

                if (_nextSampleIndex >= _totalSamples)
                    _tileComplete = true;
            }
        }

        var source = new Rectangle { x = 0, y = 0, width = width, height = -height };
        Raylib.DrawTextureRec(_target.texture, source, Vector2.Zero, new Color(255, 255, 255, 255));
    }

    void RenderPassDirect(
        RenderTexture target,
        int width,
        int height,
        in RenderRequest request,
        Vector2 subpixelOffset,
        bool additive,
        bool clearTarget)
    {
        ref readonly ShaderProgram program = ref SelectDirectProgram(request);
        SetDirectUniforms(program, width, height, request, subpixelOffset);
        bool timing = _nextSampleIndex == 0 && _nextTileIndex == 0 && _timer.Begin();

        Raylib.BeginTextureMode(target);
        if (clearTarget)
            Raylib.ClearBackground(additive ? new Color(0, 0, 0, 0) : PanelTheme.CanvasBackground);

        if (additive)
        {
            RlGl.rlSetBlendFactors(RlGl.RL_ONE, RlGl.RL_ONE, RlGl.RL_FUNC_ADD);
            Raylib.BeginBlendMode(BlendMode.BLEND_CUSTOM);
        }

        Raylib.BeginShaderMode(program.Shader);
        Raylib.SetShaderValueTexture(program.Shader, program.PaletteLutLoc, _paletteLut.Texture);
        DrawTilesOrFull(width, height);
        Raylib.EndShaderMode();

        if (additive)
            Raylib.EndBlendMode();

        Raylib.EndTextureMode();
        _timer.End(timing);
    }

    void RenderPassPerturb(
        RenderTexture target,
        int width,
        int height,
        in RenderRequest request,
        ReferenceOrbit orbit,
        Vector2 subpixelOffset,
        bool additive,
        bool clearTarget)
    {
        SetPerturbUniforms(width, height, request, orbit, subpixelOffset);
        bool timing = _nextSampleIndex == 0 && _nextTileIndex == 0 && _timer.Begin();

        Raylib.BeginTextureMode(target);
        if (clearTarget)
            Raylib.ClearBackground(new Color(0, 0, 0, 0));

        if (additive)
        {
            RlGl.rlSetBlendFactors(RlGl.RL_ONE, RlGl.RL_ONE, RlGl.RL_FUNC_ADD);
            Raylib.BeginBlendMode(BlendMode.BLEND_CUSTOM);
        }

        Raylib.BeginShaderMode(_perturb.Shader);
        Raylib.SetShaderValueTexture(_perturb.Shader, _perturb.PaletteLutLoc, _paletteLut.Texture);
        Raylib.SetShaderValueTexture(_perturb.Shader, _perturb.RefOrbitLoc, _orbitTexture);
        DrawTilesOrFull(width, height);
        Raylib.EndShaderMode();

        if (additive)
            Raylib.EndBlendMode();

        Raylib.EndTextureMode();
        _timer.End(timing);
    }

    void ResolveSsaaTarget(int width, int height, int sampleCount)
    {
        float invCount = 1f / Math.Max(sampleCount, 1);
        _resolve.Set(_resolve.InvSampleCountLoc, invCount, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

        Raylib.BeginTextureMode(_target);
        Raylib.BeginShaderMode(_resolve.Shader);
        var source = new Rectangle { x = 0, y = 0, width = width, height = -height };
        Raylib.DrawTextureRec(_ssaaAccum.texture, source, Vector2.Zero, new Color(255, 255, 255, 255));
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    void DrawTilesOrFull(int width, int height)
    {
        if (_totalTiles <= 1)
        {
            Raylib.DrawRectangle(0, 0, width, height, new Color(255, 255, 255, 255));
            _nextTileIndex = _totalTiles;
            return;
        }

        int cols = (width + TileSize - 1) / TileSize;
        long started = Stopwatch.GetTimestamp();
        int drawn = 0;

        while (_nextTileIndex < _totalTiles)
        {
            int tx = (_nextTileIndex % cols) * TileSize;
            int ty = (_nextTileIndex / cols) * TileSize;
            int tw = Math.Min(TileSize, width - tx);
            int th = Math.Min(TileSize, height - ty);
            Raylib.DrawRectangle(tx, ty, tw, th, new Color(255, 255, 255, 255));
            _nextTileIndex++;
            drawn++;

            if (drawn >= 4 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= TileFrameBudgetMs)
                break;
        }
    }

    void EnsureOrbitTexture(ReferenceOrbit orbit)
    {
        if (_hasOrbitTexture && ReferenceEquals(_uploadedOrbit, orbit))
            return;

        if (_hasOrbitTexture)
        {
            Raylib.UnloadTexture(_orbitTexture);
            _hasOrbitTexture = false;
        }

        int rows = Math.Max(1, (orbit.Length + OrbitTexWidth - 1) / OrbitTexWidth);
        var data = new float[OrbitTexWidth * rows * 4];
        for (int i = 0; i < orbit.Length; i++)
        {
            int dst = i * 4;
            data[dst] = (float)orbit.Zx[i];
            data[dst + 1] = (float)orbit.Zy[i];
        }

        unsafe
        {
            fixed (float* ptr = data)
            {
                var image = new Image
                {
                    data = ptr,
                    width = OrbitTexWidth,
                    height = rows,
                    mipmaps = 1,
                    format = (int)PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32,
                };
                _orbitTexture = Raylib.LoadTextureFromImage(image);
            }
        }

        Raylib.SetTextureFilter(_orbitTexture, TextureFilter.TEXTURE_FILTER_POINT);
        Raylib.SetTextureWrap(_orbitTexture, TextureWrap.TEXTURE_WRAP_CLAMP);
        _uploadedOrbit = orbit;
        _hasOrbitTexture = true;
    }

    public void BlitCached(int width, int height)
    {
        if (!HasImage || width < 1 || height < 1)
        {
            Raylib.DrawRectangle(0, 0, Math.Max(width, 1), Math.Max(height, 1), PanelTheme.CanvasBackground);
            return;
        }

        var source = new Rectangle { x = 0, y = 0, width = _width, height = -_height };
        var dest = new Rectangle { x = 0, y = 0, width = width, height = height };
        Raylib.DrawTexturePro(_target.texture, source, dest, Vector2.Zero, 0, new Color(255, 255, 255, 255));
    }

    public bool RenderOffscreen(RenderTexture target, int width, int height, in RenderRequest request)
    {
        if (!IsAvailable || width < 1 || height < 1)
            return false;

        ref readonly ShaderProgram program = ref SelectDirectProgram(request);
        SetDirectUniforms(program, width, height, request, Vector2.Zero);

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(PanelTheme.CanvasBackground);
        Raylib.BeginShaderMode(program.Shader);
        Raylib.SetShaderValueTexture(program.Shader, program.PaletteLutLoc, _paletteLut.Texture);
        Raylib.DrawRectangle(0, 0, width, height, new Color(255, 255, 255, 255));
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
        return true;
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

        UnloadSsaaAccum();

        if (_hasOrbitTexture)
        {
            Raylib.UnloadTexture(_orbitTexture);
            _hasOrbitTexture = false;
            _uploadedOrbit = null;
        }

        _f32.Dispose();
        _df.Dispose();
        _perturb.Dispose();
        _resolve.Dispose();
        _paletteLut.Dispose();
        _timer.Dispose();
        _cachedRequest = null;
        _cachedOrbit = null;
        IsAvailable = false;
        HasImage = false;
    }

    ref readonly ShaderProgram SelectDirectProgram(in RenderRequest request)
    {
        bool useFastFloat = _f32.IsReady
            && (request.View.Scale > Float32ScaleThreshold
                || !FormulaParams.TryGetIntegerExponent(request.Formula.Power, out _));
        return ref (useFastFloat ? ref _f32 : ref _df);
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
        _cachedRequest = null;
    }

    bool EnsureSsaaAccum(int width, int height)
    {
        if (_ssaaAccumFailed)
            return false;

        if (_hasSsaaAccum && _ssaaAccumWidth == width && _ssaaAccumHeight == height)
            return true;

        UnloadSsaaAccum();

        if (TryCreateFloatTarget(width, height, PixelFormat.PIXELFORMAT_UNCOMPRESSED_R16G16B16A16, out _ssaaAccum)
            || TryCreateFloatTarget(width, height, PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32, out _ssaaAccum))
        {
            _ssaaAccumWidth = width;
            _ssaaAccumHeight = height;
            _hasSsaaAccum = true;
            return true;
        }

        _ssaaAccumFailed = true;
        return false;
    }

    void UnloadSsaaAccum()
    {
        if (!_hasSsaaAccum) return;
        Raylib.UnloadRenderTexture(_ssaaAccum);
        _hasSsaaAccum = false;
        _ssaaAccumWidth = 0;
        _ssaaAccumHeight = 0;
    }

    static unsafe bool TryCreateFloatTarget(int width, int height, PixelFormat format, out RenderTexture target)
    {
        target = default;
        uint fboId = RlGl.rlLoadFramebuffer(width, height);
        if (fboId == 0)
            return false;

        RlGl.rlEnableFramebuffer(fboId);
        uint texId = RlGl.rlLoadTexture(null, width, height, (int)format, 1);
        if (texId == 0)
        {
            RlGl.rlDisableFramebuffer();
            RlGl.rlUnloadFramebuffer(fboId);
            return false;
        }

        RlGl.rlFramebufferAttach(
            fboId,
            texId,
            (int)rlFramebufferAttachType.RL_ATTACHMENT_COLOR_CHANNEL0,
            (int)rlFramebufferAttachTextureType.RL_ATTACHMENT_TEXTURE2D,
            0);

        bool complete = RlGl.rlFramebufferComplete(fboId);
        RlGl.rlDisableFramebuffer();
        if (!complete)
        {
            RlGl.rlUnloadTexture(texId);
            RlGl.rlUnloadFramebuffer(fboId);
            return false;
        }

        target = new RenderTexture
        {
            id = fboId,
            texture = new Texture
            {
                id = texId,
                width = width,
                height = height,
                mipmaps = 1,
                format = (int)format,
            },
        };
        Raylib.SetTextureFilter(target.texture, TextureFilter.TEXTURE_FILTER_POINT);
        return true;
    }

    static void SetDirectUniforms(in ShaderProgram program, int width, int height, in RenderRequest request, Vector2 subpixelOffset)
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
        int normalLighting = request.Coloring.NormalLighting ? 1 : 0;
        int cursorLight = request.Coloring.CursorLight ? 1 : 0;
        var lightPos = new Vector2(request.Coloring.LightX, request.Coloring.LightY);
        float lightAngle = (float)(request.Coloring.LightAngle * (Math.PI / 180.0));
        int orbitTrap = (int)request.Coloring.OrbitTrap;
        float density = (float)request.Coloring.Density;
        float phase = (float)request.Coloring.Phase;
        int julia = request.Formula.Julia ? 1 : 0;
        int fractalFamily = (int)request.Formula.Family;

        program.Set(program.ResolutionLoc, resolution, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        program.Set(program.CenterLoc, center, ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        program.Set(program.JuliaCLoc, juliaC, ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        program.Set(program.ScaleLoc, scale, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        program.Set(program.PowerLoc, power, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        program.Set(program.ExponentLoc, exponent, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.MaxIterLoc, maxIter, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.PaletteLoc, palette, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.SmoothLoc, smooth, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.InteriorLoc, interior, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.NormalLightingLoc, normalLighting, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.CursorLightLoc, cursorLight, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.LightPosLoc, lightPos, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        program.Set(program.LightAngleLoc, lightAngle, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        program.Set(program.OrbitTrapLoc, orbitTrap, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.DensityLoc, density, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        program.Set(program.PhaseLoc, phase, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        program.Set(program.JuliaLoc, julia, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.FractalFamilyLoc, fractalFamily, ShaderUniformDataType.SHADER_UNIFORM_INT);
        program.Set(program.SubpixelOffsetLoc, subpixelOffset, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
    }

    void SetPerturbUniforms(int width, int height, in RenderRequest request, ReferenceOrbit orbit, Vector2 subpixelOffset)
    {
        int prec = orbit.PrecisionBits;
        FloatExp refOffX = BigFloat.Sub(request.View.CenterXExact, orbit.RefRe, prec).ToFloatExp();
        FloatExp refOffY = BigFloat.Sub(request.View.CenterYExact, orbit.RefIm, prec).ToFloatExp();
        ComplexExp refOffset = ComplexExp.FromFloatExp(refOffX, refOffY);

        var resolution = new Vector2(width, height);
        float scaleMant = (float)request.View.ScaleExact.Mantissa;
        int scaleExp = request.View.ScaleExact.Exponent;
        var refOffsetMant = new Vector2((float)refOffset.Re, (float)refOffset.Im);
        int refOffsetExp = refOffset.Exp;
        var juliaC = new Vector2((float)request.Formula.JuliaRe, (float)request.Formula.JuliaIm);

        int exponent = orbit.Exponent;
        int maxIter = Math.Clamp(request.Formula.MaxIterations, 1, FormulaParams.IterationMax);
        int refLength = orbit.Length;
        int palette = request.Coloring.Palette;
        int smooth = request.Coloring.Smooth ? 1 : 0;
        int interior = (int)request.Coloring.Interior;
        int normalLighting = request.Coloring.NormalLighting ? 1 : 0;
        int cursorLight = request.Coloring.CursorLight ? 1 : 0;
        var lightPos = new Vector2(request.Coloring.LightX, request.Coloring.LightY);
        float lightAngle = (float)(request.Coloring.LightAngle * (Math.PI / 180.0));
        int orbitTrap = (int)request.Coloring.OrbitTrap;
        float density = (float)request.Coloring.Density;
        float phase = (float)request.Coloring.Phase;
        int julia = request.Formula.Julia ? 1 : 0;

        _perturb.Set(_perturb.ResolutionLoc, resolution, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        _perturb.Set(_perturb.ScaleMantLoc, scaleMant, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        _perturb.Set(_perturb.ScaleExpLoc, scaleExp, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.RefOffsetMantLoc, refOffsetMant, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        _perturb.Set(_perturb.RefOffsetExpLoc, refOffsetExp, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.JuliaCLoc, juliaC, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        _perturb.Set(_perturb.ExponentLoc, exponent, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.MaxIterLoc, maxIter, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.RefLengthLoc, refLength, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.PaletteLoc, palette, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.SmoothLoc, smooth, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.InteriorLoc, interior, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.NormalLightingLoc, normalLighting, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.CursorLightLoc, cursorLight, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.LightPosLoc, lightPos, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        _perturb.Set(_perturb.LightAngleLoc, lightAngle, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        _perturb.Set(_perturb.OrbitTrapLoc, orbitTrap, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.DensityLoc, density, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        _perturb.Set(_perturb.PhaseLoc, phase, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        _perturb.Set(_perturb.JuliaLoc, julia, ShaderUniformDataType.SHADER_UNIFORM_INT);
        _perturb.Set(_perturb.SubpixelOffsetLoc, subpixelOffset, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
    }

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

    struct ShaderProgram
    {
        public Shader Shader;
        public bool IsReady;
        public int ResolutionLoc;
        public int CenterLoc;
        public int JuliaCLoc;
        public int ScaleLoc;
        public int PowerLoc;
        public int ExponentLoc;
        public int MaxIterLoc;
        public int PaletteLoc;
        public int PaletteLutLoc;
        public int SmoothLoc;
        public int InteriorLoc;
        public int NormalLightingLoc;
        public int CursorLightLoc;
        public int LightPosLoc;
        public int LightAngleLoc;
        public int OrbitTrapLoc;
        public int DensityLoc;
        public int PhaseLoc;
        public int JuliaLoc;
        public int FractalFamilyLoc;
        public int SubpixelOffsetLoc;

        public static bool TryLoad(string vertexSource, string fragmentSource, out ShaderProgram program)
        {
            program = default;
            Shader shader = Raylib.LoadShaderFromMemory(vertexSource, fragmentSource);
            if (!Raylib.IsShaderReady(shader))
            {
                if (shader.id != 0) Raylib.UnloadShader(shader);
                return false;
            }

            program = new ShaderProgram
            {
                Shader = shader,
                IsReady = true,
                ResolutionLoc = Raylib.GetShaderLocation(shader, "resolution"),
                CenterLoc = Raylib.GetShaderLocation(shader, "center"),
                JuliaCLoc = Raylib.GetShaderLocation(shader, "juliaC"),
                ScaleLoc = Raylib.GetShaderLocation(shader, "scale"),
                PowerLoc = Raylib.GetShaderLocation(shader, "power"),
                ExponentLoc = Raylib.GetShaderLocation(shader, "exponent"),
                MaxIterLoc = Raylib.GetShaderLocation(shader, "maxIter"),
                PaletteLoc = Raylib.GetShaderLocation(shader, "paletteId"),
                PaletteLutLoc = Raylib.GetShaderLocation(shader, "paletteLut"),
                SmoothLoc = Raylib.GetShaderLocation(shader, "smoothColor"),
                InteriorLoc = Raylib.GetShaderLocation(shader, "interiorStyle"),
                NormalLightingLoc = Raylib.GetShaderLocation(shader, "normalLighting"),
                CursorLightLoc = Raylib.GetShaderLocation(shader, "cursorLight"),
                LightPosLoc = Raylib.GetShaderLocation(shader, "lightPos"),
                LightAngleLoc = Raylib.GetShaderLocation(shader, "lightAngle"),
                OrbitTrapLoc = Raylib.GetShaderLocation(shader, "orbitTrap"),
                DensityLoc = Raylib.GetShaderLocation(shader, "density"),
                PhaseLoc = Raylib.GetShaderLocation(shader, "phase"),
                JuliaLoc = Raylib.GetShaderLocation(shader, "juliaSet"),
                FractalFamilyLoc = Raylib.GetShaderLocation(shader, "fractalFamily"),
                SubpixelOffsetLoc = Raylib.GetShaderLocation(shader, "subpixelOffset"),
            };
            return true;
        }

        public readonly void Set<T>(int location, T value, ShaderUniformDataType type) where T : unmanaged
        {
            if (location >= 0)
                Raylib.SetShaderValue(Shader, location, value, type);
        }

        public void Dispose()
        {
            if (!IsReady) return;
            Raylib.UnloadShader(Shader);
            IsReady = false;
        }
    }

    struct PerturbProgram
    {
        public Shader Shader;
        public bool IsReady;
        public int ResolutionLoc;
        public int ScaleMantLoc;
        public int ScaleExpLoc;
        public int RefOffsetMantLoc;
        public int RefOffsetExpLoc;
        public int JuliaCLoc;
        public int ExponentLoc;
        public int MaxIterLoc;
        public int RefLengthLoc;
        public int PaletteLoc;
        public int PaletteLutLoc;
        public int RefOrbitLoc;
        public int SmoothLoc;
        public int InteriorLoc;
        public int NormalLightingLoc;
        public int CursorLightLoc;
        public int LightPosLoc;
        public int LightAngleLoc;
        public int OrbitTrapLoc;
        public int DensityLoc;
        public int PhaseLoc;
        public int JuliaLoc;
        public int SubpixelOffsetLoc;

        public static bool TryLoad(string vertexSource, string fragmentSource, out PerturbProgram program)
        {
            program = default;
            Shader shader = Raylib.LoadShaderFromMemory(vertexSource, fragmentSource);
            if (!Raylib.IsShaderReady(shader))
            {
                if (shader.id != 0) Raylib.UnloadShader(shader);
                return false;
            }

            program = new PerturbProgram
            {
                Shader = shader,
                IsReady = true,
                ResolutionLoc = Raylib.GetShaderLocation(shader, "resolution"),
                ScaleMantLoc = Raylib.GetShaderLocation(shader, "scaleMant"),
                ScaleExpLoc = Raylib.GetShaderLocation(shader, "scaleExp"),
                RefOffsetMantLoc = Raylib.GetShaderLocation(shader, "refOffsetMant"),
                RefOffsetExpLoc = Raylib.GetShaderLocation(shader, "refOffsetExp"),
                JuliaCLoc = Raylib.GetShaderLocation(shader, "juliaC"),
                ExponentLoc = Raylib.GetShaderLocation(shader, "exponent"),
                MaxIterLoc = Raylib.GetShaderLocation(shader, "maxIter"),
                RefLengthLoc = Raylib.GetShaderLocation(shader, "refLength"),
                PaletteLoc = Raylib.GetShaderLocation(shader, "paletteId"),
                PaletteLutLoc = Raylib.GetShaderLocation(shader, "paletteLut"),
                RefOrbitLoc = Raylib.GetShaderLocation(shader, "refOrbit"),
                SmoothLoc = Raylib.GetShaderLocation(shader, "smoothColor"),
                InteriorLoc = Raylib.GetShaderLocation(shader, "interiorStyle"),
                NormalLightingLoc = Raylib.GetShaderLocation(shader, "normalLighting"),
                CursorLightLoc = Raylib.GetShaderLocation(shader, "cursorLight"),
                LightPosLoc = Raylib.GetShaderLocation(shader, "lightPos"),
                LightAngleLoc = Raylib.GetShaderLocation(shader, "lightAngle"),
                OrbitTrapLoc = Raylib.GetShaderLocation(shader, "orbitTrap"),
                DensityLoc = Raylib.GetShaderLocation(shader, "density"),
                PhaseLoc = Raylib.GetShaderLocation(shader, "phase"),
                JuliaLoc = Raylib.GetShaderLocation(shader, "juliaSet"),
                SubpixelOffsetLoc = Raylib.GetShaderLocation(shader, "subpixelOffset"),
            };
            return true;
        }

        public readonly void Set<T>(int location, T value, ShaderUniformDataType type) where T : unmanaged
        {
            if (location >= 0)
                Raylib.SetShaderValue(Shader, location, value, type);
        }

        public void Dispose()
        {
            if (!IsReady) return;
            Raylib.UnloadShader(Shader);
            IsReady = false;
        }
    }

    struct ResolveProgram
    {
        public Shader Shader;
        public bool IsReady;
        public int InvSampleCountLoc;

        public static bool TryLoad(string vertexSource, string fragmentSource, out ResolveProgram program)
        {
            program = default;
            Shader shader = Raylib.LoadShaderFromMemory(vertexSource, fragmentSource);
            if (!Raylib.IsShaderReady(shader))
            {
                if (shader.id != 0) Raylib.UnloadShader(shader);
                return false;
            }

            program = new ResolveProgram
            {
                Shader = shader,
                IsReady = true,
                InvSampleCountLoc = Raylib.GetShaderLocation(shader, "invSampleCount"),
            };
            return true;
        }

        public readonly void Set<T>(int location, T value, ShaderUniformDataType type) where T : unmanaged
        {
            if (location >= 0)
                Raylib.SetShaderValue(Shader, location, value, type);
        }

        public void Dispose()
        {
            if (!IsReady) return;
            Raylib.UnloadShader(Shader);
            IsReady = false;
        }
    }
}
