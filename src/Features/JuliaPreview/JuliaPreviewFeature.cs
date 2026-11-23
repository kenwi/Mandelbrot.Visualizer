using System.Globalization;
using System.Numerics;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.JuliaPreview;

public readonly record struct JuliaPreviewInput(
    Vector2 MousePosition,
    bool LeftPressed,
    bool LeftDown,
    bool LeftReleased,
    bool RightPressed,
    bool SpacePressed,
    bool TogglePreviewPressed);

/// <summary>
/// Vertical slice owning the optional corner live Julia preview overlay,
/// cached GPU/CPU thumbnail rendering, one-click / hotkey jump into Julia mode,
/// and return-view memory when switching back to the parameter map.
/// </summary>
public sealed class JuliaPreviewFeature : IDisposable
{
    public const int PreviewImageWidth = 240;
    public const int PreviewImageHeight = 192;
    public const int MaxPreviewIterations = 512;
    public const int MaxZoomedPreviewIterations = 2048;

    static readonly FloatExp PreviewMinScale = FloatExp.FromDouble(1e-12);

    const int CardMargin = 16;
    const int CardPadding = 8;
    const int FooterHeight = 36;
    const int CardWidth = PreviewImageWidth + CardPadding * 2;
    const int CardHeight = PreviewImageHeight + CardPadding * 2 + FooterHeight;
    public const float ReturnClickMaxDragPixels = 10f;

    static readonly ViewportState DefaultPreviewViewport = new(
        BigFloat.Zero,
        BigFloat.Zero,
        FloatExp.FromDouble(FractalView.DefaultJuliaScale));

    readonly record struct PreviewCacheKey(
        ViewportState View,
        FormulaParams Formula,
        ColoringParams Coloring,
        bool UseGpu);

    RenderTexture _gpuTarget;
    bool _hasGpuTarget;

    Texture _cpuTexture;
    bool _hasCpuTexture;
    float[]? _cpuMu;
    float[]? _cpuMinR;
    float[]? _cpuTrap;
    float[]? _cpuNx;
    float[]? _cpuNy;
    Color[]? _cpuPixels;

    PreviewCacheKey? _cacheKey;
    bool _cachedOnGpu;

    bool _hasFixedC;
    double _fixedRe;
    double _fixedIm;

    bool _hasPreviewC;
    double _previewRe;
    double _previewIm;
    bool _hasLockedPixel;
    int _lockedX;
    int _lockedY;

    bool _hasReturnView;
    ViewportState _returnView;
    FractalFamily _returnFamily;
    bool _returnPressActive;
    Vector2 _returnPressPos;
    bool _returnDragged;

    bool _disposed;

    public bool Enabled { get; set; }
    public bool MatchZoom { get; set; }
    public bool HasReturnView => _hasReturnView;

    public static bool IsCleanClick(Vector2 pressPos, Vector2 releasePos, bool dragged)
        => !dragged && Vector2.DistanceSquared(pressPos, releasePos) <= ReturnClickMaxDragPixels * ReturnClickMaxDragPixels;

    public void SetFixedPreviewC(double re, double im)
    {
        _hasFixedC = true;
        _fixedRe = re;
        _fixedIm = im;
        _previewRe = re;
        _previewIm = im;
        _hasPreviewC = true;
        _hasLockedPixel = false;
    }

    public static FormulaParams BuildPreviewFormula(in FormulaParams source, double cRe, double cIm, bool matchZoom = false)
    {
        int cap = matchZoom ? MaxZoomedPreviewIterations : MaxPreviewIterations;
        return source with
        {
            Julia = true,
            JuliaRe = cRe,
            JuliaIm = cIm,
            MaxIterations = Math.Clamp(source.MaxIterations, FormulaParams.IterationMin, cap),
        };
    }

    public ViewportState ResolvePreviewViewport(in ViewportState mapView)
    {
        if (!MatchZoom)
            return DefaultPreviewViewport;

        FloatExp scale = mapView.ScaleExact < PreviewMinScale ? PreviewMinScale : mapView.ScaleExact;
        return new ViewportState(BigFloat.Zero, BigFloat.Zero, scale);
    }

    public FloatExp ResolveJumpScale(in ViewportState mapView)
        => MatchZoom ? mapView.ScaleExact : FloatExp.FromDouble(FractalView.DefaultJuliaScale);

    public static ColoringParams BuildPreviewColoring(in ColoringParams source)
        => source with
        {
            Mode = ColoringMode.Cyclic,
            CursorLight = false,
        };

    public void SaveReturnView(in ViewportState mapView, FractalFamily family)
    {
        _returnView = mapView;
        _returnFamily = family;
        _hasReturnView = true;
        _returnPressActive = false;
        _returnDragged = false;
    }

    public bool TryConsumeReturnView(FractalFamily currentFamily, out ViewportState savedView)
    {
        _returnPressActive = false;
        _returnDragged = false;

        if (_hasReturnView && _returnFamily == currentFamily)
        {
            savedView = _returnView;
            _hasReturnView = false;
            return true;
        }

        _hasReturnView = false;
        savedView = default;
        return false;
    }

    public void ClearReturnView()
    {
        _hasReturnView = false;
        _returnPressActive = false;
        _returnDragged = false;
    }

    public void EnterJuliaFromJump(NavigationFeature navigation, FormulaFeature formula, double cRe, double cIm)
    {
        ViewportState mapView = navigation.State;
        SaveReturnView(mapView, formula.State.Family);
        formula.JumpToJulia(cRe, cIm);
        FloatExp jumpScale = ResolveJumpScale(mapView);
        navigation.SetExact(BigFloat.Zero, BigFloat.Zero, jumpScale);
        navigation.CancelPointerInteraction();
    }

    public bool ReturnToMap(NavigationFeature navigation, FormulaFeature formula)
    {
        if (!TryConsumeReturnView(formula.State.Family, out ViewportState returnView))
            return false;

        formula.ExitJulia();
        navigation.SetExact(returnView.CenterXExact, returnView.CenterYExact, returnView.ScaleExact);
        navigation.CancelPointerInteraction();
        return true;
    }

    public void OnJuliaToggled(NavigationFeature navigation, FormulaFeature formula, bool wasJulia)
    {
        if (!wasJulia && formula.State.Julia)
        {
            ViewportState mapView = navigation.State;
            SaveReturnView(mapView, formula.State.Family);
            FloatExp jumpScale = ResolveJumpScale(mapView);
            navigation.SetExact(BigFloat.Zero, BigFloat.Zero, jumpScale);
            navigation.CancelPointerInteraction();
            return;
        }

        if (wasJulia && !formula.State.Julia && TryConsumeReturnView(formula.State.Family, out ViewportState saved))
        {
            navigation.SetExact(saved.CenterXExact, saved.CenterYExact, saved.ScaleExact);
            navigation.CancelPointerInteraction();
            return;
        }

        formula.State.GetDefaultView(out double cx, out double cy, out double scale);
        navigation.ResetTo(cx, cy, scale);
        navigation.CancelPointerInteraction();
    }

    public void OnFamilyChanged(NavigationFeature navigation, FormulaFeature formula)
    {
        ClearReturnView();
        formula.State.GetDefaultView(out double cx, out double cy, out double scale);
        navigation.ResetTo(cx, cy, scale);
        navigation.CancelPointerInteraction();
    }

    public void OnResetView(NavigationFeature navigation, FormulaFeature formula)
    {
        // Resetting the Julia zoom should not erase the saved parameter-map return view.
        if (!formula.State.Julia)
            ClearReturnView();

        formula.State.GetDefaultView(out double cx, out double cy, out double scale);
        navigation.ResetTo(cx, cy, scale);
        navigation.CancelPointerInteraction();
    }

    public void OnMatchZoomToggled(NavigationFeature navigation, in FormulaParams formula)
    {
        if (!formula.Julia || !_hasReturnView || _returnFamily != formula.Family)
            return;

        FloatExp targetScale = ResolveJumpScale(_returnView);
        navigation.SetExact(navigation.State.CenterXExact, navigation.State.CenterYExact, targetScale);
    }

    public bool CanDisplay(int canvasWidth, int canvasHeight, in FormulaParams formula)
        => Enabled
        && !formula.Julia
        && canvasWidth >= CardWidth + CardMargin * 2
        && canvasHeight >= CardHeight + CardMargin * 2;

    public Rectangle GetCardRect(int canvasWidth, int canvasHeight)
        => new()
        {
            x = CardMargin,
            y = canvasHeight - CardHeight - CardMargin,
            width = CardWidth,
            height = CardHeight,
        };

    public bool IsPointerOverCard(int canvasWidth, int canvasHeight, in FormulaParams formula)
    {
        if (!CanDisplay(canvasWidth, canvasHeight, formula))
            return false;

        Vector2 mouse = Raylib.GetMousePosition();
        Rectangle card = GetCardRect(canvasWidth, canvasHeight);
        return mouse.X >= card.x
            && mouse.X <= card.x + card.width
            && mouse.Y >= card.y
            && mouse.Y <= card.y + card.height;
    }

    public void UpdatePreviewTarget(
        int canvasWidth,
        int canvasHeight,
        in FormulaParams formula,
        NavigationFeature navigation,
        bool pointerOverCard)
    {
        if (!Enabled || formula.Julia)
            return;

        if (_hasFixedC)
        {
            _previewRe = _fixedRe;
            _previewIm = _fixedIm;
            _hasPreviewC = true;
            return;
        }

        if (!pointerOverCard
            && navigation.TryGetHoveredPixel(canvasWidth, canvasHeight, out int px, out int py)
            && navigation.TryGetHoveredComplex(canvasWidth, canvasHeight, out double re, out double im))
        {
            _previewRe = re;
            _previewIm = im;
            _hasPreviewC = true;
            _lockedX = px;
            _lockedY = py;
            _hasLockedPixel = true;
        }
        else if (!_hasPreviewC)
        {
            _previewRe = navigation.State.CenterX;
            _previewIm = navigation.State.CenterY;
            _hasPreviewC = true;
            _hasLockedPixel = false;
        }
    }

    public bool HandleInput(
        int canvasWidth,
        int canvasHeight,
        in FormulaParams formula,
        NavigationFeature navigation,
        bool pointerOverCard,
        bool keyboardCaptured,
        out bool jumped,
        out double jumpRe,
        out double jumpIm,
        out bool returnedToMap)
    {
        var input = new JuliaPreviewInput(
            Raylib.GetMousePosition(),
            MouseClickLatch.IsPressed(MouseButton.MOUSE_BUTTON_LEFT),
            MouseClickLatch.IsDown(MouseButton.MOUSE_BUTTON_LEFT),
            MouseClickLatch.IsReleased(MouseButton.MOUSE_BUTTON_LEFT),
            MouseClickLatch.IsPressed(MouseButton.MOUSE_BUTTON_RIGHT),
            Raylib.IsKeyPressed(KeyboardKey.KEY_SPACE),
            Raylib.IsKeyPressed(KeyboardKey.KEY_P));

        bool hasHovered = navigation.TryGetHoveredComplex(canvasWidth, canvasHeight, out double hoverRe, out double hoverIm);
        return HandleInput(
            canvasWidth,
            canvasHeight,
            formula,
            hasHovered,
            hoverRe,
            hoverIm,
            pointerOverCard,
            keyboardCaptured,
            input,
            out jumped,
            out jumpRe,
            out jumpIm,
            out returnedToMap);
    }

    public bool HandleInput(
        int canvasWidth,
        int canvasHeight,
        in FormulaParams formula,
        bool hasHoveredComplex,
        double hoverRe,
        double hoverIm,
        bool pointerOverCard,
        bool keyboardCaptured,
        in JuliaPreviewInput input,
        out bool jumped,
        out double jumpRe,
        out double jumpIm,
        out bool returnedToMap)
    {
        bool changed = false;
        jumped = false;
        jumpRe = 0;
        jumpIm = 0;
        returnedToMap = false;

        if (!keyboardCaptured && input.TogglePreviewPressed)
        {
            Enabled = !Enabled;
            changed = true;
        }

        if (formula.Julia)
        {
            if (_hasReturnView)
            {
                Vector2 mouse = input.MousePosition;
                bool onCanvas = mouse.X >= 0 && mouse.X < canvasWidth && mouse.Y >= 0 && mouse.Y < canvasHeight;

                if (onCanvas && input.RightPressed)
                {
                    _returnPressActive = false;
                    _returnDragged = false;
                    returnedToMap = true;
                    return true;
                }

                if (input.LeftPressed && onCanvas)
                {
                    _returnPressActive = true;
                    _returnPressPos = mouse;
                    _returnDragged = false;
                }

                if (_returnPressActive && input.LeftDown)
                {
                    if (Vector2.DistanceSquared(_returnPressPos, mouse) > ReturnClickMaxDragPixels * ReturnClickMaxDragPixels)
                        _returnDragged = true;
                }

                if (input.LeftReleased)
                {
                    if (_returnPressActive && onCanvas && IsCleanClick(_returnPressPos, mouse, _returnDragged))
                    {
                        _returnPressActive = false;
                        _returnDragged = false;
                        returnedToMap = true;
                        return true;
                    }

                    _returnPressActive = false;
                    _returnDragged = false;
                }
            }
            else
            {
                _returnPressActive = false;
                _returnDragged = false;
            }

            return changed;
        }

        _returnPressActive = false;
        _returnDragged = false;

        // One-click jump by clicking the corner preview card.
        if (Enabled && pointerOverCard && _hasPreviewC && input.LeftPressed)
        {
            jumped = true;
            jumpRe = _previewRe;
            jumpIm = _previewIm;
            return true;
        }

        // One-click jump anywhere on the parameter map via right-click.
        if (input.RightPressed)
        {
            if (Enabled && pointerOverCard && _hasPreviewC)
            {
                jumped = true;
                jumpRe = _previewRe;
                jumpIm = _previewIm;
                return true;
            }

            if (hasHoveredComplex)
            {
                jumped = true;
                jumpRe = hoverRe;
                jumpIm = hoverIm;
                return true;
            }
        }

        // Hotkey jump via Space while hovering the parameter map (or preview card).
        if (!keyboardCaptured && input.SpacePressed)
        {
            if (Enabled && pointerOverCard && _hasPreviewC)
            {
                jumped = true;
                jumpRe = _previewRe;
                jumpIm = _previewIm;
                return true;
            }

            if (hasHoveredComplex)
            {
                jumped = true;
                jumpRe = hoverRe;
                jumpIm = hoverIm;
                return true;
            }

            if (Enabled && _hasPreviewC)
            {
                jumped = true;
                jumpRe = _previewRe;
                jumpIm = _previewIm;
                return true;
            }
        }

        return changed;
    }

    public bool DrawToggle(ref PanelLayout layout, out bool matchZoomChanged)
    {
        bool enabled = Enabled;
        bool matchZoom = MatchZoom;
        matchZoomChanged = false;
        if (!layout.TwoChecks("Julia preview", ref enabled, "Match zoom", ref matchZoom))
            return false;

        matchZoomChanged = matchZoom != MatchZoom;
        Enabled = enabled;
        MatchZoom = matchZoom;
        return true;
    }

    public void DrawOverlay(
        int canvasWidth,
        int canvasHeight,
        in ViewportState mapView,
        in FormulaParams formula,
        in ColoringParams coloring,
        RenderingFeature rendering,
        bool pointerOverCard)
    {
        if (!CanDisplay(canvasWidth, canvasHeight, formula) || !_hasPreviewC)
            return;

        ViewportState previewView = ResolvePreviewViewport(mapView);
        FormulaParams previewFormula = BuildPreviewFormula(formula, _previewRe, _previewIm, MatchZoom);
        ColoringParams previewColoring = BuildPreviewColoring(coloring);
        bool wantGpu = rendering.UsingGpu(ColoringMode.Cyclic);

        var key = new PreviewCacheKey(previewView, previewFormula, previewColoring, wantGpu);
        if (_cacheKey != key)
        {
            var request = new RenderRequest(
                PreviewImageWidth,
                PreviewImageHeight,
                previewView,
                previewFormula,
                previewColoring,
                rendering.UseSimd,
                true);

            bool renderedGpu = false;
            if (wantGpu)
            {
                EnsureGpuTarget();
                renderedGpu = rendering.RenderPreviewGpu(_gpuTarget, PreviewImageWidth, PreviewImageHeight, request);
            }

            if (renderedGpu)
            {
                _cachedOnGpu = true;
            }
            else
            {
                RenderCpuPreview(request);
                _cachedOnGpu = false;
            }

            _cacheKey = key;
        }

        Rectangle card = GetCardRect(canvasWidth, canvasHeight);
        int cx = (int)card.x;
        int cy = (int)card.y;

        // Subtle drop shadow and framed card background.
        Raylib.DrawRectangle(cx + 4, cy + 4, CardWidth, CardHeight, new Color(0, 0, 0, 120));
        Raylib.DrawRectangle(cx, cy, CardWidth, CardHeight, new Color(18, 22, 30, 242));

        Color border = pointerOverCard ? PanelTheme.CheckFill : PanelTheme.PanelBorder;
        Raylib.DrawRectangleLines(cx, cy, CardWidth, CardHeight, border);
        if (pointerOverCard)
            Raylib.DrawRectangleLines(cx + 1, cy + 1, CardWidth - 2, CardHeight - 2, border);

        int imgX = cx + CardPadding;
        int imgY = cy + CardPadding;
        if (_cachedOnGpu && _hasGpuTarget)
        {
            var source = new Rectangle { x = 0, y = 0, width = PreviewImageWidth, height = -PreviewImageHeight };
            Raylib.DrawTextureRec(_gpuTarget.texture, source, new Vector2(imgX, imgY), new Color(255, 255, 255, 255));
        }
        else if (_hasCpuTexture)
        {
            Raylib.DrawTexture(_cpuTexture, imgX, imgY, new Color(255, 255, 255, 255));
        }

        Raylib.DrawRectangleLines(imgX, imgY, PreviewImageWidth, PreviewImageHeight, PanelTheme.PanelBorder);

        int footerY = imgY + PreviewImageHeight + 6;
        string sign = _previewIm < -1e-9 ? "-" : "+";
        string cText = string.Create(
            CultureInfo.InvariantCulture,
            $"Julia  {_previewRe:0.####} {sign} {Math.Abs(_previewIm):0.####} i");
        Raylib.DrawText(cText, imgX + 2, footerY, 14, PanelTheme.TitleText);

        string hint = pointerOverCard
            ? "Click to open Julia set"
            : MatchZoom
                ? $"Zoom {previewView.ScaleExact.FormatZoom(FractalView.DefaultMandelbrotScale)}  Click, RMB or Space"
                : "Click preview, RMB or Space";
        Color hintColor = pointerOverCard ? PanelTheme.CheckFill : PanelTheme.MutedText;
        Raylib.DrawText(hint, imgX + 2, footerY + 17, 12, hintColor);

        if (pointerOverCard && _hasLockedPixel)
        {
            Raylib.DrawCircleLines(_lockedX, _lockedY, 7f, PanelTheme.CheckFill);
            Raylib.DrawLine(_lockedX - 10, _lockedY, _lockedX - 4, _lockedY, PanelTheme.CheckFill);
            Raylib.DrawLine(_lockedX + 4, _lockedY, _lockedX + 10, _lockedY, PanelTheme.CheckFill);
            Raylib.DrawLine(_lockedX, _lockedY - 10, _lockedX, _lockedY - 4, PanelTheme.CheckFill);
            Raylib.DrawLine(_lockedX, _lockedY + 4, _lockedX, _lockedY + 10, PanelTheme.CheckFill);
        }
    }

    void EnsureGpuTarget()
    {
        if (_hasGpuTarget) return;
        _gpuTarget = Raylib.LoadRenderTexture(PreviewImageWidth, PreviewImageHeight);
        Raylib.SetTextureFilter(_gpuTarget.texture, TextureFilter.TEXTURE_FILTER_POINT);
        _hasGpuTarget = true;
    }

    void EnsureCpuBuffers()
    {
        int count = PreviewImageWidth * PreviewImageHeight;
        _cpuMu ??= new float[count];
        _cpuMinR ??= new float[count];
        _cpuTrap ??= new float[count];
        _cpuNx ??= new float[count];
        _cpuNy ??= new float[count];
        _cpuPixels ??= new Color[count];

        if (!_hasCpuTexture)
        {
            Image image = Raylib.GenImageColor(PreviewImageWidth, PreviewImageHeight, PanelTheme.CanvasBackground);
            _cpuTexture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            Raylib.SetTextureFilter(_cpuTexture, TextureFilter.TEXTURE_FILTER_BILINEAR);
            _hasCpuTexture = true;
        }
    }

    void RenderCpuPreview(in RenderRequest request)
    {
        EnsureCpuBuffers();

        int width = PreviewImageWidth;
        int height = PreviewImageHeight;
        int count = width * height;
        double halfH = request.View.Scale;
        double halfW = request.View.HalfWidth(width, height);
        bool needsMinR = request.Coloring.Interior == InteriorStyle.Glow;
        bool needsNormals = request.Coloring.NormalLighting;
        OrbitTrapMode trap = request.Coloring.OrbitTrap;
        bool useShaded = needsNormals || trap != OrbitTrapMode.None;
        bool useSimd = !useShaded && request.UseSimd && SimdMultibrot.IsSupported;
        FormulaParams formula = request.Formula;

        float[] mu = _cpuMu!;
        float[] minR = _cpuMinR!;
        float[] trapBuf = _cpuTrap!;
        float[] nxBuf = _cpuNx!;
        float[] nyBuf = _cpuNy!;

        Parallel.For(0, height, y =>
        {
            if (useShaded)
                MultibrotFormula.EvaluateShadedRow(width, height, y, 0.0, 0.0, halfW, halfH, formula, mu, minR, trapBuf, nxBuf, nyBuf, needsMinR, needsNormals, (int)trap);
            else if (useSimd)
                SimdMultibrot.EvaluateRow(width, height, y, 0.0, 0.0, halfW, halfH, formula, mu, minR, needsMinR);
            else
                MultibrotFormula.EvaluateScalarRow(width, height, y, 0.0, 0.0, halfW, halfH, formula, mu, minR, needsMinR);
        });

        ReadOnlySpan<float> trapSpan = trap != OrbitTrapMode.None ? trapBuf.AsSpan(0, count) : ReadOnlySpan<float>.Empty;
        ReadOnlySpan<float> nxSpan = needsNormals ? nxBuf.AsSpan(0, count) : ReadOnlySpan<float>.Empty;
        ReadOnlySpan<float> nySpan = needsNormals ? nyBuf.AsSpan(0, count) : ReadOnlySpan<float>.Empty;

        Color[] pixels = _cpuPixels!;
        Palette.Map(
            mu.AsSpan(0, count),
            minR.AsSpan(0, count),
            trapSpan,
            nxSpan,
            nySpan,
            pixels.AsSpan(0, count),
            request.Coloring,
            formula.MaxIterations,
            width,
            height);

        unsafe
        {
            fixed (Color* ptr = pixels)
                Raylib.UpdateTexture(_cpuTexture, ptr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hasGpuTarget)
        {
            Raylib.UnloadRenderTexture(_gpuTarget);
            _hasGpuTarget = false;
        }

        if (_hasCpuTexture)
        {
            Raylib.UnloadTexture(_cpuTexture);
            _hasCpuTexture = false;
        }
    }
}
