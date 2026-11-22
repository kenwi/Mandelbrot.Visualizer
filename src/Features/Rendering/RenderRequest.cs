using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;

namespace Mandelbrot.Visualizer.Features.Rendering;

public readonly record struct RenderRequest(
    int Width,
    int Height,
    ViewportState View,
    FormulaParams Formula,
    ColoringParams Coloring,
    bool UseSimd,
    bool FinalQuality);
