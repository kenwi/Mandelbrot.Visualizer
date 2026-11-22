namespace Mandelbrot.Visualizer.Features.Navigation;

/// <summary>
/// Immutable snapshot of the complex-plane viewport. Scale is half the visible height.
/// </summary>
public readonly record struct ViewportState(double CenterX, double CenterY, double Scale)
{
    public double HalfWidth(int width, int height)
        => Scale * width / (double)Math.Max(height, 1);
}

/// <summary>
/// Complex-plane camera supporting pan, zoom around a screen point, and Mandelbrot/Julia reset.
/// </summary>
public sealed class FractalView
{
    public const double MinScale = 1e-14;
    public const double MaxScale = 8;
    public const double DefaultMandelbrotScale = 1.35;
    public const double DefaultJuliaScale = 1.6;

    public double CenterX { get; set; } = -0.5;
    public double CenterY { get; set; }
    public double Scale { get; set; } = DefaultMandelbrotScale;

    public ViewportState Snapshot => new(CenterX, CenterY, Scale);
    public double ZoomFactor => DefaultMandelbrotScale / Math.Max(Scale, MinScale);

    public void ResetFor(bool julia)
    {
        if (julia)
        {
            CenterX = 0;
            CenterY = 0;
            Scale = DefaultJuliaScale;
        }
        else
        {
            CenterX = -0.5;
            CenterY = 0;
            Scale = DefaultMandelbrotScale;
        }
    }

    public void ScreenToComplex(double px, double py, int width, int height, out double cx, out double cy)
    {
        if (width < 1) width = 1;
        if (height < 1) height = 1;
        double halfH = Scale;
        double halfW = Scale * width / height;
        cx = CenterX + (px / width - 0.5) * 2.0 * halfW;
        cy = CenterY - (py / height - 0.5) * 2.0 * halfH;
    }

    public void ZoomAt(double px, double py, int width, int height, double factor)
    {
        ScreenToComplex(px, py, width, height, out double cx, out double cy);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        double halfH = Scale;
        double halfW = Scale * width / (double)Math.Max(height, 1);
        CenterX = cx - (px / width - 0.5) * 2.0 * halfW;
        CenterY = cy + (py / height - 0.5) * 2.0 * halfH;
    }

    public void PanPixels(double dx, double dy, int width, int height)
    {
        if (width < 1 || height < 1) return;
        CenterX -= dx / width * 2.0 * Scale * width / height;
        CenterY += dy / height * 2.0 * Scale;
    }
}
