using Mandelbrot.Visualizer.Coloring;

namespace Mandelbrot.Visualizer.Controls;

public sealed class VisualizerSettings
{
    public const double PowerMin = -6;
    public const double PowerMax = 12;
    public const int IterationMin = 16;
    public const int IterationMax = 4000;

    public double Power { get; set; } = 2;
    public int MaxIterations { get; set; } = 250;
    public int Palette { get; set; }
    public ColoringMode Mode { get; set; } = ColoringMode.Cyclic;
    public bool Smooth { get; set; } = true;
    public InteriorStyle Interior { get; set; } = InteriorStyle.Black;
    public double Density { get; set; } = 1;
    public double Phase { get; set; }
    public bool Julia { get; set; }
    public double JuliaRe { get; set; } = -0.8;
    public double JuliaIm { get; set; } = 0.156;
    public bool UseGpu { get; set; } = true;

    public void CyclePalette() => Palette = (Palette + 1) % Coloring.Palette.Count;

    public void NudgePower(double delta)
    {
        double next = Power + delta;
        if (Math.Abs(next) < 1e-4) next = delta >= 0 ? 1e-4 : -1e-4;
        Power = next;
    }

    public void NudgeIterations(int delta)
    {
        MaxIterations = Math.Clamp(MaxIterations + delta, IterationMin, IterationMax);
    }
}
