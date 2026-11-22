using Mandelbrot.Visualizer.App;
using Mandelbrot.Visualizer.Features.Diagnostics;

if (args.Contains("--self-test"))
    return SelfTest.Run() ? 0 : 1;

string? screenshot = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--screenshot")
        screenshot = args[i + 1];
}

using var host = new VisualizerHost(screenshot);
host.Run();
return 0;
