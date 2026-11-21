namespace Mandelbrot.Visualizer.Viewport;

/// <summary>
/// Complex-plane window. Scale is the half-height of the view.
/// </summary>
public sealed class FractalView
{
    public const double MinScale = 1e-14;
    public const double MaxScale = 8;

    public double CenterX { get; set; } = -0.5;
    public double CenterY { get; set; }
    public double Scale { get; set; } = 1.35;

    public void ResetMandelbrot()
    {
        CenterX = -0.5;
        CenterY = 0;
        Scale = 1.35;
    }

    public void ResetJulia()
    {
        CenterX = 0;
        CenterY = 0;
        Scale = 1.6;
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
