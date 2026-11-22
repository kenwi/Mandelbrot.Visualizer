using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Navigation;

/// <summary>
/// Immutable snapshot of the complex-plane viewport with arbitrary-precision coordinates
/// and extended-exponent scale for unlimited zoom depth.
/// </summary>
public readonly record struct ViewportState(
    BigFloat CenterXExact,
    BigFloat CenterYExact,
    FloatExp ScaleExact)
{
    public static readonly FloatExp DeepZoomThreshold = FloatExp.FromDouble(1e-12);

    public double CenterX => CenterXExact.ToDouble();
    public double CenterY => CenterYExact.ToDouble();
    public double Scale => ScaleExact.ToDouble();
    public bool IsDeepZoom => ScaleExact <= DeepZoomThreshold;

    public double HalfWidth(int width, int height)
        => Scale * width / (double)Math.Max(height, 1);

    public FloatExp HalfWidthExact(int width, int height)
        => ScaleExact * (width / (double)Math.Max(height, 1));
}

/// <summary>
/// Arbitrary-precision complex-plane camera supporting unlimited zoom depth without precision loss.
/// </summary>
public sealed class FractalView
{
    public const double DefaultMandelbrotScale = 1.35;
    public const double DefaultJuliaScale = 1.6;
    public static readonly FloatExp MaxScale = FloatExp.FromDouble(8.0);

    public BigFloat CenterX { get; private set; } = BigFloat.FromDouble(-0.5);
    public BigFloat CenterY { get; private set; } = BigFloat.Zero;
    public FloatExp Scale { get; private set; } = FloatExp.FromDouble(DefaultMandelbrotScale);

    public ViewportState Snapshot => new(CenterX, CenterY, Scale);
    public string ZoomDisplay => Scale.FormatZoom(DefaultMandelbrotScale);

    public void SetExact(BigFloat centerX, BigFloat centerY, FloatExp scale)
    {
        if (scale.IsZero || scale < FloatExp.Zero)
            return;

        Scale = scale > MaxScale ? MaxScale : scale;
        int prec = BigFloat.RequiredBits(Scale);
        CenterX = centerX.Round(prec);
        CenterY = centerY.Round(prec);
    }

    public void ResetFor(bool julia)
    {
        if (julia)
        {
            CenterX = BigFloat.Zero;
            CenterY = BigFloat.Zero;
            Scale = FloatExp.FromDouble(DefaultJuliaScale);
        }
        else
        {
            CenterX = BigFloat.FromDouble(-0.5);
            CenterY = BigFloat.Zero;
            Scale = FloatExp.FromDouble(DefaultMandelbrotScale);
        }
    }

    public void ScreenToComplex(double px, double py, int width, int height, out double cx, out double cy)
    {
        if (width < 1) width = 1;
        if (height < 1) height = 1;
        double halfH = Scale.ToDouble();
        double halfW = halfH * width / height;
        cx = CenterX.ToDouble() + (px / width - 0.5) * 2.0 * halfW;
        cy = CenterY.ToDouble() - (py / height - 0.5) * 2.0 * halfH;
    }

    public void ZoomAt(double px, double py, int width, int height, double factor)
    {
        if (width < 1) width = 1;
        if (height < 1) height = 1;

        FloatExp oldScale = Scale;
        FloatExp newScale = oldScale * factor;
        if (newScale > MaxScale) newScale = MaxScale;
        if (newScale.IsZero) return;

        FloatExp deltaScale = oldScale - newScale;
        double u = px / width - 0.5;
        double v = py / height - 0.5;
        double aspect = width / (double)height;

        int prec = BigFloat.RequiredBits(newScale);
        BigFloat shiftX = BigFloat.FromFloatExp(deltaScale * (2.0 * u * aspect));
        BigFloat shiftY = BigFloat.FromFloatExp(deltaScale * (2.0 * v));

        Scale = newScale;
        CenterX = BigFloat.Add(CenterX, shiftX, prec);
        CenterY = BigFloat.Sub(CenterY, shiftY, prec);
    }

    public void PanPixels(double dx, double dy, int width, int height)
    {
        if (width < 1 || height < 1) return;

        int prec = BigFloat.RequiredBits(Scale);
        double invH = 2.0 / height;
        BigFloat deltaX = BigFloat.FromFloatExp(Scale * (dx * invH));
        BigFloat deltaY = BigFloat.FromFloatExp(Scale * (dy * invH));

        CenterX = BigFloat.Sub(CenterX, deltaX, prec);
        CenterY = BigFloat.Add(CenterY, deltaY, prec);
    }

    public void NudgeCenter(double dxScaleUnits, double dyScaleUnits)
    {
        int prec = BigFloat.RequiredBits(Scale);
        if (dxScaleUnits != 0.0)
            CenterX = BigFloat.Add(CenterX, BigFloat.FromFloatExp(Scale * dxScaleUnits), prec);
        if (dyScaleUnits != 0.0)
            CenterY = BigFloat.Add(CenterY, BigFloat.FromFloatExp(Scale * dyScaleUnits), prec);
    }
}
