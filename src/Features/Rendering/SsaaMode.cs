using System.Numerics;

namespace Mandelbrot.Visualizer.Features.Rendering;

public enum SsaaMode
{
    Off = 0,
    X2 = 1,
    X4 = 2,
}

/// <summary>
/// Shared rotated-grid supersampling (RGSS) subpixel offsets in [-0.5, +0.5] pixels,
/// used identically by GPU multi-pass accumulation and CPU supersampling.
/// </summary>
public static class SsaaPattern
{
    public const string ComboItems = "Off;2x (2x2);4x (4x4)";
    public const int ModeCount = 3;
    public const SsaaMode Highest = SsaaMode.X4;

    static readonly Vector2[] Offsets1 = [Vector2.Zero];
    static readonly Vector2[] Offsets2 = BuildRotatedGrid(2);
    static readonly Vector2[] Offsets4 = BuildRotatedGrid(4);

    public static ReadOnlySpan<Vector2> GetOffsets(SsaaMode mode) => mode switch
    {
        SsaaMode.X2 => Offsets2,
        SsaaMode.X4 => Offsets4,
        _ => Offsets1,
    };

    public static int SampleCount(SsaaMode mode) => mode switch
    {
        SsaaMode.X2 => 4,
        SsaaMode.X4 => 16,
        _ => 1,
    };

    public static string ShortLabel(SsaaMode mode) => mode switch
    {
        SsaaMode.X2 => "2x",
        SsaaMode.X4 => "4x",
        _ => string.Empty,
    };

    public static bool TryParse(string text, out SsaaMode mode)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "off":
            case "none":
            case "1":
            case "1x":
            case "1x1":
                mode = SsaaMode.Off;
                return true;

            case "2":
            case "2x":
            case "2x2":
                mode = SsaaMode.X2;
                return true;

            case "4":
            case "4x":
            case "4x4":
                mode = SsaaMode.X4;
                return true;

            default:
                mode = SsaaMode.Off;
                return false;
        }
    }

    static Vector2[] BuildRotatedGrid(int grid)
    {
        int total = grid * grid;
        var offsets = new Vector2[total];
        int index = 0;
        for (int iy = 0; iy < grid; iy++)
        {
            for (int ix = 0; ix < grid; ix++)
            {
                // Exact rotated grid (N-rooks stratification across both axes, zero mean).
                int xIdx = ix * grid + iy;
                int yIdx = iy * grid + (grid - 1 - ix);
                float ox = (xIdx + 0.5f) / total - 0.5f;
                float oy = (yIdx + 0.5f) / total - 0.5f;
                offsets[index++] = new Vector2(ox, oy);
            }
        }

        return offsets;
    }
}
