using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

/// <summary>
/// GPU SDF raymarch renderer for the 3D Mandelbulb supporting half-res interactive
/// previews, tiled final frames, idle frame caching, and 2x/4x RGSS SSAA accumulation.
/// </summary>
public sealed class MandelbulbRenderer : IDisposable
{
    const int TileSize = 256;
    const double TileFrameBudgetMs = 12.0;

    readonly record struct CacheKey(
        int Width,
        int Height,
        CameraPose Camera,
        MandelbulbParams Params,
        int Palette,
        double Density,
        double Phase,
        double LightAngle,
        bool FinalQuality,
        SsaaMode Ssaa);

    readonly PaletteLut _paletteLut = new();
    readonly Stopwatch _frameWatch = new();

    Shader _shader;
    bool _shaderLoaded;
    int _resLoc, _subpixelLoc, _lensJitterLoc, _dofApertureLoc, _dofFocusLoc;
    int _camPosLoc, _camFwdLoc, _camRightLoc, _camUpLoc, _tanFovLoc;
    int _shapeLoc, _qualityLoc, _boundLoc, _powerLoc, _boxScaleLoc, _boxMinRLoc, _boxFoldLoc;
    int _iterLoc, _detailLoc, _aoLoc, _shadowLoc, _finalLoc, _juliaLoc, _juliaCLoc;
    int _lightElLoc, _headlampLoc, _fogLoc, _roughLoc, _metalLoc, _glowLoc;
    int _paletteLutLoc, _paletteIdxLoc, _densityLoc, _phaseLoc, _lightAngleLoc;

    Shader _resolveShader;
    bool _resolveLoaded;
    int _resolveInvSampleCountLoc;

    RenderTexture _target;
    bool _hasTarget;
    int _targetW;
    int _targetH;

    RenderTexture _ssaaAccum;
    bool _hasSsaaAccum;
    int _ssaaW;
    int _ssaaH;
    bool _ssaaFailed;

    CacheKey? _cachedKey;
    int _nextTile;
    int _totalTiles = 1;
    int _nextSample;
    int _totalSamples = 1;
    int _completedSamples;
    bool _dofActivePass;
    float _resolvedFocusDist = 2.0f;
    bool _complete = true;
    double _accumRenderMs;
    bool _disposed;

    public bool IsAvailable { get; private set; }
    public bool HasImage { get; private set; }
    public bool IsRenderingTiles => !_complete;
    public int CompletedSsaaSamples => _completedSamples;
    public int TotalSsaaSamples => _totalSamples;
    public bool IsDofPass => _dofActivePass;
    public float ResolvedFocusDistance => _resolvedFocusDist;
    public bool HasFrameTime { get; private set; }
    public double LastFrameMs { get; private set; }

    public bool IsReadyForFinalCapture(SsaaMode expectedSsaa)
        => HasImage
        && _complete
        && _cachedKey is CacheKey k
        && k.FinalQuality
        && k.Ssaa == expectedSsaa;

    public void Load()
    {
        if (_disposed || IsAvailable) return;

        string vert = LoadResource("Shaders.multibrot.vert");
        string frag = LoadResource("Shaders.mandelbulb.frag");
        string resolveFrag = LoadResource("Shaders.ssaa_resolve.frag");

        _shader = Raylib.LoadShaderFromMemory(vert, frag);
        if (_shader.id == 0)
        {
            Console.Error.WriteLine("Mandelbulb shader failed to compile.");
            return;
        }

        _shaderLoaded = true;
        _resLoc = Raylib.GetShaderLocation(_shader, "resolution");
        _subpixelLoc = Raylib.GetShaderLocation(_shader, "subpixelOffset");
        _lensJitterLoc = Raylib.GetShaderLocation(_shader, "lensJitter");
        _dofApertureLoc = Raylib.GetShaderLocation(_shader, "dofAperture");
        _dofFocusLoc = Raylib.GetShaderLocation(_shader, "dofFocusDist");
        _camPosLoc = Raylib.GetShaderLocation(_shader, "camPos");
        _camFwdLoc = Raylib.GetShaderLocation(_shader, "camForward");
        _camRightLoc = Raylib.GetShaderLocation(_shader, "camRight");
        _camUpLoc = Raylib.GetShaderLocation(_shader, "camUp");
        _tanFovLoc = Raylib.GetShaderLocation(_shader, "tanHalfFov");
        _shapeLoc = Raylib.GetShaderLocation(_shader, "shapeMode");
        _qualityLoc = Raylib.GetShaderLocation(_shader, "qualityTier");
        _boundLoc = Raylib.GetShaderLocation(_shader, "boundingRadius");
        _powerLoc = Raylib.GetShaderLocation(_shader, "power");
        _boxScaleLoc = Raylib.GetShaderLocation(_shader, "boxScale");
        _boxMinRLoc = Raylib.GetShaderLocation(_shader, "boxMinRadius");
        _boxFoldLoc = Raylib.GetShaderLocation(_shader, "boxFoldLimit");
        _iterLoc = Raylib.GetShaderLocation(_shader, "maxIterations");
        _detailLoc = Raylib.GetShaderLocation(_shader, "detail");
        _aoLoc = Raylib.GetShaderLocation(_shader, "aoStrength");
        _shadowLoc = Raylib.GetShaderLocation(_shader, "softShadows");
        _finalLoc = Raylib.GetShaderLocation(_shader, "finalQuality");
        _juliaLoc = Raylib.GetShaderLocation(_shader, "juliaMode");
        _juliaCLoc = Raylib.GetShaderLocation(_shader, "juliaC");
        _lightElLoc = Raylib.GetShaderLocation(_shader, "lightElevation");
        _headlampLoc = Raylib.GetShaderLocation(_shader, "headlamp");
        _fogLoc = Raylib.GetShaderLocation(_shader, "fogDensity");
        _roughLoc = Raylib.GetShaderLocation(_shader, "roughness");
        _metalLoc = Raylib.GetShaderLocation(_shader, "metallic");
        _glowLoc = Raylib.GetShaderLocation(_shader, "glowStrength");
        _paletteLutLoc = Raylib.GetShaderLocation(_shader, "paletteLut");
        _paletteIdxLoc = Raylib.GetShaderLocation(_shader, "paletteIndex");
        _densityLoc = Raylib.GetShaderLocation(_shader, "density");
        _phaseLoc = Raylib.GetShaderLocation(_shader, "phase");
        _lightAngleLoc = Raylib.GetShaderLocation(_shader, "lightAngle");

        _resolveShader = Raylib.LoadShaderFromMemory(vert, resolveFrag);
        if (_resolveShader.id != 0)
        {
            _resolveLoaded = true;
            _resolveInvSampleCountLoc = Raylib.GetShaderLocation(_resolveShader, "invSampleCount");
        }

        _paletteLut.Load();
        IsAvailable = true;
    }

    public void Draw(
        int canvasWidth,
        int canvasHeight,
        in CameraPose camera,
        in MandelbulbParams parameters,
        in ColoringParams coloring,
        bool finalQuality,
        SsaaMode ssaa)
    {
        if (!IsAvailable || canvasWidth < 1 || canvasHeight < 1)
            return;

        int renderW = finalQuality ? canvasWidth : Math.Max(1, canvasWidth / 2);
        int renderH = finalQuality ? canvasHeight : Math.Max(1, canvasHeight / 2);
        SsaaMode effectiveSsaa = finalQuality ? ssaa : SsaaMode.Off;
        bool wantDof = finalQuality && parameters.DofEnabled && parameters.DofAperture > 0.0001;
        bool needsAccum = (effectiveSsaa != SsaaMode.Off || wantDof) && _resolveLoaded && EnsureSsaaAccum(renderW, renderH);
        bool useDof = wantDof && needsAccum;
        ReadOnlySpan<Vector2> offsets = needsAccum
            ? SsaaPattern.GetOffsets(effectiveSsaa)
            : SsaaPattern.GetOffsets(SsaaMode.Off);

        int dofSampleCount = parameters.Quality switch
        {
            BulbQuality.Fast => 8,
            BulbQuality.Balanced => 12,
            _ => 16,
        };
        int sampleCount = useDof ? Math.Max(dofSampleCount, offsets.Length) : offsets.Length;

        var key = new CacheKey(
            renderW,
            renderH,
            camera,
            parameters,
            coloring.Palette,
            coloring.Density,
            coloring.Phase,
            coloring.LightAngle,
            finalQuality,
            effectiveSsaa);

        if (!HasImage || _cachedKey != key)
        {
            EnsureTarget(renderW, renderH);
            _cachedKey = key;

            if (finalQuality)
            {
                int cols = (renderW + TileSize - 1) / TileSize;
                int rows = (renderH + TileSize - 1) / TileSize;
                _totalTiles = Math.Max(1, cols * rows);
            }
            else
            {
                _totalTiles = 1;
            }

            if (parameters.DofEnabled && parameters.DofAutoFocus)
            {
                camera.GetBasis(out Vector3 pos, out Vector3 fwd, out _, out _);
                _resolvedFocusDist = MandelbulbMath.ProbeRayDistance(pos, fwd, parameters);
            }
            else
            {
                _resolvedFocusDist = (float)parameters.DofFocusDistance;
            }

            _nextTile = 0;
            _totalSamples = sampleCount;
            _nextSample = 0;
            _completedSamples = 0;
            _dofActivePass = useDof;
            _complete = false;
            _accumRenderMs = 0;
        }

        if (!_complete)
        {
            _frameWatch.Restart();
            if (_totalSamples <= 1)
            {
                RenderPass(_target, renderW, renderH, camera, parameters, coloring, finalQuality, Vector2.Zero, Vector2.Zero, 0f, additive: false, clearTarget: _nextTile == 0 && _totalTiles == 1);
                if (_nextTile >= _totalTiles)
                {
                    _completedSamples = 1;
                    _complete = true;
                }
                HasImage = true;
            }
            else
            {
                float worldAperture = useDof
                    ? (float)parameters.DofAperture * MathF.Max(_resolvedFocusDist * 0.35f, 0.12f)
                    : 0f;

                long frameStart = Stopwatch.GetTimestamp();
                while (_nextSample < _totalSamples)
                {
                    Vector2 subOffset = offsets[_nextSample % offsets.Length];
                    Vector2 lensJitter = useDof ? MandelbulbMath.SampleVogelDisk(_nextSample, _totalSamples) : Vector2.Zero;
                    bool clearAccum = _nextSample == 0 && _nextTile == 0;
                    RenderPass(_ssaaAccum, renderW, renderH, camera, parameters, coloring, finalQuality, subOffset, lensJitter, worldAperture, additive: true, clearTarget: clearAccum);
                    if (_nextTile >= _totalTiles)
                    {
                        _nextSample++;
                        _completedSamples = _nextSample;
                        _nextTile = 0;
                    }

                    if (Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds >= TileFrameBudgetMs)
                        break;
                }

                if (_completedSamples > 0)
                {
                    ResolveSsaaTarget(renderW, renderH, _completedSamples);
                    HasImage = true;
                }

                if (_nextSample >= _totalSamples)
                    _complete = true;
            }

            _frameWatch.Stop();
            _accumRenderMs += _frameWatch.Elapsed.TotalMilliseconds;
            if (_complete)
            {
                LastFrameMs = _accumRenderMs;
                HasFrameTime = true;
            }
        }

        var source = new Rectangle { x = 0, y = 0, width = _targetW, height = -_targetH };
        var dest = new Rectangle { x = 0, y = 0, width = canvasWidth, height = canvasHeight };
        Raylib.DrawTexturePro(_target.texture, source, dest, Vector2.Zero, 0f, new Color(255, 255, 255, 255));
    }

    public void Save(string path)
    {
        if (!HasImage || !_hasTarget) return;
        Image image = Raylib.LoadImageFromTexture(_target.texture);
        unsafe
        {
            Raylib.ImageFlipVertical(&image);
        }
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
    }

    void RenderPass(
        RenderTexture target,
        int width,
        int height,
        in CameraPose camera,
        in MandelbulbParams parameters,
        in ColoringParams coloring,
        bool finalQuality,
        Vector2 subpixelOffset,
        Vector2 lensJitter,
        float worldAperture,
        bool additive,
        bool clearTarget)
    {
        SetUniforms(width, height, camera, parameters, coloring, finalQuality, subpixelOffset, lensJitter, worldAperture);

        Raylib.BeginTextureMode(target);
        if (clearTarget)
            Raylib.ClearBackground(additive ? new Color(0, 0, 0, 0) : PanelTheme.CanvasBackground);

        if (additive)
        {
            RlGl.rlSetBlendFactors(RlGl.RL_ONE, RlGl.RL_ONE, RlGl.RL_FUNC_ADD);
            Raylib.BeginBlendMode(BlendMode.BLEND_CUSTOM);
        }

        Raylib.BeginShaderMode(_shader);
        Raylib.SetShaderValueTexture(_shader, _paletteLutLoc, _paletteLut.Texture);
        DrawTilesOrFull(width, height);
        Raylib.EndShaderMode();

        if (additive)
            Raylib.EndBlendMode();

        Raylib.EndTextureMode();
    }

    void DrawTilesOrFull(int width, int height)
    {
        if (_totalTiles <= 1)
        {
            Raylib.DrawRectangle(0, 0, width, height, new Color(255, 255, 255, 255));
            _nextTile = 1;
            return;
        }

        int cols = (width + TileSize - 1) / TileSize;
        long start = Stopwatch.GetTimestamp();
        while (_nextTile < _totalTiles)
        {
            int tx = _nextTile % cols;
            int ty = _nextTile / cols;
            int x = tx * TileSize;
            int y = ty * TileSize;
            int w = Math.Min(TileSize, width - x);
            int h = Math.Min(TileSize, height - y);
            Raylib.DrawRectangle(x, y, w, h, new Color(255, 255, 255, 255));
            _nextTile++;

            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= TileFrameBudgetMs)
                break;
        }
    }

    void SetUniforms(
        int width,
        int height,
        in CameraPose camera,
        in MandelbulbParams parameters,
        in ColoringParams coloring,
        bool finalQuality,
        Vector2 subpixelOffset,
        Vector2 lensJitter,
        float worldAperture)
    {
        camera.GetBasis(out Vector3 pos, out Vector3 fwd, out Vector3 right, out Vector3 up);
        float tanHalfFov = MathF.Tan(camera.FovDegrees * 0.5f * (MathF.PI / 180f));

        SetVec2(_resLoc, width, height);
        SetVec2(_subpixelLoc, subpixelOffset.X, subpixelOffset.Y);
        SetVec2(_lensJitterLoc, lensJitter.X, lensJitter.Y);
        SetFloat(_dofApertureLoc, worldAperture);
        SetFloat(_dofFocusLoc, _resolvedFocusDist);
        SetVec3(_camPosLoc, pos);
        SetVec3(_camFwdLoc, fwd);
        SetVec3(_camRightLoc, right);
        SetVec3(_camUpLoc, up);
        SetFloat(_tanFovLoc, tanHalfFov);

        SetInt(_shapeLoc, (int)parameters.Shape);
        SetInt(_qualityLoc, (int)parameters.Quality);
        SetFloat(_boundLoc, parameters.BoundingRadius);
        SetFloat(_powerLoc, (float)parameters.Power);
        SetFloat(_boxScaleLoc, (float)parameters.BoxScale);
        SetFloat(_boxMinRLoc, (float)parameters.BoxMinRadius);
        SetFloat(_boxFoldLoc, (float)parameters.BoxFoldLimit);
        SetInt(_iterLoc, parameters.Iterations);
        SetFloat(_detailLoc, (float)parameters.Detail);
        SetFloat(_aoLoc, (float)parameters.AoStrength);
        SetInt(_shadowLoc, parameters.SoftShadows ? 1 : 0);
        SetInt(_finalLoc, finalQuality ? 1 : 0);
        SetInt(_juliaLoc, parameters.Julia ? 1 : 0);
        SetVec3(_juliaCLoc, parameters.JuliaC);

        SetFloat(_lightElLoc, (float)parameters.LightElevation);
        SetInt(_headlampLoc, parameters.Headlamp ? 1 : 0);
        SetFloat(_fogLoc, (float)parameters.FogDensity);
        SetFloat(_roughLoc, (float)parameters.Roughness);
        SetFloat(_metalLoc, (float)parameters.Metallic);
        SetFloat(_glowLoc, (float)parameters.GlowStrength);

        SetInt(_paletteIdxLoc, coloring.Palette);
        SetFloat(_densityLoc, (float)coloring.Density);
        SetFloat(_phaseLoc, (float)coloring.Phase);
        SetFloat(_lightAngleLoc, (float)coloring.LightAngle);
    }

    void ResolveSsaaTarget(int width, int height, int sampleCount)
    {
        float inv = 1f / Math.Max(sampleCount, 1);
        Raylib.SetShaderValue(_resolveShader, _resolveInvSampleCountLoc, inv, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

        Raylib.BeginTextureMode(_target);
        Raylib.BeginShaderMode(_resolveShader);
        var source = new Rectangle { x = 0, y = 0, width = width, height = -height };
        Raylib.DrawTextureRec(_ssaaAccum.texture, source, Vector2.Zero, new Color(255, 255, 255, 255));
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    void EnsureTarget(int width, int height)
    {
        if (_hasTarget && _targetW == width && _targetH == height)
            return;

        if (_hasTarget)
            Raylib.UnloadRenderTexture(_target);

        _target = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureFilter(_target.texture, TextureFilter.TEXTURE_FILTER_BILINEAR);
        _targetW = width;
        _targetH = height;
        _hasTarget = true;
    }

    bool EnsureSsaaAccum(int width, int height)
    {
        if (_ssaaFailed) return false;
        if (_hasSsaaAccum && _ssaaW == width && _ssaaH == height)
            return true;

        if (_hasSsaaAccum)
        {
            Raylib.UnloadRenderTexture(_ssaaAccum);
            _hasSsaaAccum = false;
        }

        if (!TryCreateFloatRenderTexture(width, height, PixelFormat.PIXELFORMAT_UNCOMPRESSED_R16G16B16A16, out _ssaaAccum)
            && !TryCreateFloatRenderTexture(width, height, PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32, out _ssaaAccum))
        {
            _ssaaFailed = true;
            return false;
        }

        _ssaaW = width;
        _ssaaH = height;
        _hasSsaaAccum = true;
        return true;
    }

    static bool TryCreateFloatRenderTexture(int width, int height, PixelFormat format, out RenderTexture target)
    {
        target = default;
        uint fbo = RlGl.rlLoadFramebuffer(width, height);
        if (fbo == 0) return false;

        RlGl.rlEnableFramebuffer(fbo);
        uint colorTex;
        unsafe
        {
            colorTex = RlGl.rlLoadTexture(null, width, height, (int)format, 1);
        }

        if (colorTex == 0)
        {
            RlGl.rlDisableFramebuffer();
            RlGl.rlUnloadFramebuffer(fbo);
            return false;
        }

        RlGl.rlFramebufferAttach(fbo, colorTex, (int)rlFramebufferAttachType.RL_ATTACHMENT_COLOR_CHANNEL0, (int)rlFramebufferAttachTextureType.RL_ATTACHMENT_TEXTURE2D, 0);
        bool ok = RlGl.rlFramebufferComplete(fbo);
        RlGl.rlDisableFramebuffer();

        if (!ok)
        {
            RlGl.rlUnloadTexture(colorTex);
            RlGl.rlUnloadFramebuffer(fbo);
            return false;
        }

        target = new RenderTexture
        {
            id = fbo,
            texture = new Texture
            {
                id = colorTex,
                width = width,
                height = height,
                mipmaps = 1,
                format = (int)format,
            },
        };
        Raylib.SetTextureFilter(target.texture, TextureFilter.TEXTURE_FILTER_POINT);
        return true;
    }

    void SetFloat(int loc, float value)
    {
        if (loc >= 0) Raylib.SetShaderValue(_shader, loc, value, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
    }

    void SetInt(int loc, int value)
    {
        if (loc >= 0) Raylib.SetShaderValue(_shader, loc, value, ShaderUniformDataType.SHADER_UNIFORM_INT);
    }

    void SetVec2(int loc, float x, float y)
    {
        if (loc >= 0) Raylib.SetShaderValue(_shader, loc, new Vector2(x, y), ShaderUniformDataType.SHADER_UNIFORM_VEC2);
    }

    void SetVec3(int loc, Vector3 value)
    {
        if (loc >= 0) Raylib.SetShaderValue(_shader, loc, value, ShaderUniformDataType.SHADER_UNIFORM_VEC3);
    }

    static string LoadResource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded shader resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hasTarget) Raylib.UnloadRenderTexture(_target);
        if (_hasSsaaAccum) Raylib.UnloadRenderTexture(_ssaaAccum);
        if (_shaderLoaded) Raylib.UnloadShader(_shader);
        if (_resolveLoaded) Raylib.UnloadShader(_resolveShader);
        _paletteLut.Dispose();
    }
}
