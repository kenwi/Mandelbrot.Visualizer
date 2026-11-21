using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Coloring;

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

public static class Palette
{
    public const string ComboItems = "Classic;Fire;Electric;Ocean;Sunset;Ice;Grayscale;Rainbow";
    public const int Count = 8;

    readonly record struct Stop(float T, byte R, byte G, byte B);

    static readonly Stop[] Classic =
    [
        new(0.00f, 4, 6, 20),
        new(0.18f, 12, 28, 120),
        new(0.38f, 20, 140, 210),
        new(0.55f, 230, 240, 255),
        new(0.72f, 250, 210, 60),
        new(0.88f, 200, 50, 20),
        new(1.00f, 4, 6, 20),
    ];

    static readonly Stop[] Fire =
    [
        new(0.00f, 0, 0, 0),
        new(0.25f, 90, 0, 0),
        new(0.50f, 220, 40, 0),
        new(0.75f, 255, 180, 20),
        new(1.00f, 255, 250, 220),
    ];

    static readonly Stop[] Electric =
    [
        new(0.00f, 8, 0, 20),
        new(0.30f, 90, 0, 160),
        new(0.55f, 40, 220, 255),
        new(0.78f, 180, 255, 80),
        new(1.00f, 8, 0, 20),
    ];

    static readonly Stop[] Ocean =
    [
        new(0.00f, 2, 8, 20),
        new(0.35f, 8, 50, 110),
        new(0.62f, 20, 160, 170),
        new(0.85f, 180, 240, 220),
        new(1.00f, 2, 8, 20),
    ];

    static readonly Stop[] Sunset =
    [
        new(0.00f, 20, 8, 40),
        new(0.30f, 140, 30, 80),
        new(0.55f, 230, 90, 40),
        new(0.78f, 255, 190, 90),
        new(1.00f, 40, 20, 70),
    ];

    static readonly Stop[] Ice =
    [
        new(0.00f, 6, 10, 24),
        new(0.40f, 40, 80, 140),
        new(0.70f, 170, 210, 240),
        new(1.00f, 245, 250, 255),
    ];

    static readonly Stop[] Grayscale =
    [
        new(0.00f, 0, 0, 0),
        new(1.00f, 255, 255, 255),
    ];

    public static void Map(
        ReadOnlySpan<float> mu,
        ReadOnlySpan<float> minRadius,
        Span<Color> pixels,
        int palette,
        ColoringMode mode,
        bool smooth,
        InteriorStyle interior,
        double density,
        double phase,
        int maxIterations)
    {
        double[]? cdf = mode == ColoringMode.Histogram
            ? BuildHistogram(mu, maxIterations)
            : null;

        for (int i = 0; i < pixels.Length; i++)
        {
            float sample = mu[i];
            if (sample < 0f)
            {
                pixels[i] = Interior(interior, minRadius[i]);
                continue;
            }

            double t;
            if (cdf != null)
            {
                double position = HistogramPosition(cdf, sample, smooth, maxIterations);
                t = position * Math.Max(density, 0.05) + phase;
            }
            else
            {
                double value = smooth ? sample : Math.Floor(sample);
                t = value * 0.035 * Math.Max(density, 0.05) + phase;
            }

            Color color = Sample(palette, t);
            if (mode == ColoringMode.Cyclic)
                color = Scale(color, Math.Tanh(sample / 9.0));
            pixels[i] = color;
        }
    }

    static double[] BuildHistogram(ReadOnlySpan<float> mu, int maxIterations)
    {
        int buckets = Math.Max(maxIterations, 1) + 1;
        var counts = new int[buckets];
        int outside = 0;
        for (int i = 0; i < mu.Length; i++)
        {
            if (mu[i] < 0f) continue;
            int bucket = (int)mu[i];
            if (bucket < 0) bucket = 0;
            if (bucket >= buckets) bucket = buckets - 1;
            counts[bucket]++;
            outside++;
        }

        var cdf = new double[buckets];
        if (outside == 0) return cdf;

        long running = 0;
        for (int i = 0; i < buckets; i++)
        {
            running += counts[i];
            cdf[i] = running / (double)outside;
        }

        return cdf;
    }

    static double HistogramPosition(double[] cdf, float sample, bool smooth, int maxIterations)
    {
        int bucket = (int)sample;
        if (bucket < 0) bucket = 0;
        if (bucket >= cdf.Length) bucket = cdf.Length - 1;
        double here = cdf[bucket];
        if (!smooth) return here;

        int next = Math.Min(bucket + 1, cdf.Length - 1);
        double frac = sample - bucket;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;
        return here + (cdf[next] - here) * frac;
    }

    static Color Interior(InteriorStyle style, float minRadius)
    {
        if (style == InteriorStyle.Black)
            return new Color(4, 6, 12, 255);

        double t = Math.Clamp(minRadius / 0.85, 0, 1);
        return new Color(
            (int)(8 + 40 * t),
            (int)(12 + 28 * t),
            (int)(32 + 90 * t),
            255);
    }

    public static Color Sample(int palette, double t)
    {
        t -= Math.Floor(t);
        if (palette == 7) return Hsv(t, 0.82, 1.0);

        Stop[] stops = palette switch
        {
            1 => Fire,
            2 => Electric,
            3 => Ocean,
            4 => Sunset,
            5 => Ice,
            6 => Grayscale,
            _ => Classic,
        };

        for (int i = 1; i < stops.Length; i++)
        {
            if (t > stops[i].T) continue;
            float span = stops[i].T - stops[i - 1].T;
            double local = span <= 0 ? 0 : (t - stops[i - 1].T) / span;
            return Lerp(stops[i - 1], stops[i], local);
        }

        Stop last = stops[^1];
        return new Color((int)last.R, (int)last.G, (int)last.B, 255);
    }

    static Color Lerp(Stop a, Stop b, double t)
    {
        return new Color(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            255);
    }

    static Color Scale(Color color, double shade)
    {
        shade = Math.Clamp(shade, 0, 1);
        return new Color(
            (int)(color.r * shade),
            (int)(color.g * shade),
            (int)(color.b * shade),
            255);
    }

    static Color Hsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        double m = v - c;
        double r, g, b;
        int sector = (int)(h * 6) % 6;
        (r, g, b) = sector switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Color((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255), 255);
    }
}
