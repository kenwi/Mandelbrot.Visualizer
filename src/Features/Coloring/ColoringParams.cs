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

public enum OrbitTrapMode
{
    None = 0,
    Point = 1,
    Cross = 2,
    Circle = 3,
    Square = 4,
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
    double Phase,
    bool NormalLighting = false,
    OrbitTrapMode OrbitTrap = OrbitTrapMode.None,
    bool CursorLight = false,
    float LightX = -0.35f,
    float LightY = 0.35f,
    double LightAngle = 135.0)
{
    public const string OrbitTrapComboItems = "None;Point;Cross;Circle;Square";
    public const int OrbitTrapCount = 5;

    public static ColoringParams Default => new(0, ColoringMode.Cyclic, true, InteriorStyle.Black, 1.0, 0.0, false, OrbitTrapMode.None, false, -0.35f, 0.35f, 135.0);
}
