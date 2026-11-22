using Mandelbrot.Visualizer.Coloring;

namespace Mandelbrot.Visualizer.Rendering;

public readonly record struct RenderRequest(
    int Width,
    int Height,
    double CenterX,
    double CenterY,
    double Scale,
    double Power,
    int MaxIterations,
    int Palette,
    ColoringMode Mode,
    bool Smooth,
    InteriorStyle Interior,
    double Density,
    double Phase,
    bool Julia,
    double JuliaRe,
    double JuliaIm,
    bool UseSimd,
    bool FinalQuality);
