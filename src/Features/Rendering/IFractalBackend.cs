namespace Mandelbrot.Visualizer.Features.Rendering;

public interface IFractalBackend : IDisposable
{
    bool IsAvailable { get; }
    bool HasImage { get; }
    bool HasFrameTime { get; }
    double LastFrameMs { get; }
    void Save(string path);
}
