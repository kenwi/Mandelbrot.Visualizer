namespace Mandelbrot.Visualizer.Features.Coloring;

public enum ColoringMode
{
    Cyclic = 0,
    Histogram = 1,
}

public enum InteriorStyle
{
    Black = 0,
    Glow = 1,
}

/// <summary>
/// Immutable coloring configuration shared by CPU palette mapping and GPU shader uniforms.
/// </summary>
public readonly record struct ColoringParams(
    int Palette,
    ColoringMode Mode,
    bool Smooth,
    InteriorStyle Interior,
    double Density,
    double Phase)
{
    public static ColoringParams Default => new(0, ColoringMode.Cyclic, true, InteriorStyle.Black, 1.0, 0.0);
}
