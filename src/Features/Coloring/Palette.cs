using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Coloring;

/// <summary>
/// Single source of truth for all palettes, escape-time mapping, cyclic shading, and histogram CDF.
/// Both the CPU renderer and the GPU PaletteLut texture sample directly from this class.
/// </summary>
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
        in ColoringParams coloring,
        int maxIterations)
        => Map(mu, minRadius, ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty, pixels, coloring, maxIterations, pixels.Length, 1);

    public static void Map(
        ReadOnlySpan<float> mu,
        ReadOnlySpan<float> minRadius,
        ReadOnlySpan<float> trapDist,
        ReadOnlySpan<float> normalX,
        ReadOnlySpan<float> normalY,
        Span<Color> pixels,
        in ColoringParams coloring,
        int maxIterations,
        int width,
        int height)
    {
        bool useTrap = coloring.OrbitTrap != OrbitTrapMode.None && trapDist.Length >= pixels.Length;
        bool useLight = coloring.NormalLighting && normalX.Length >= pixels.Length && normalY.Length >= pixels.Length;
        bool useCursorLight = useLight && coloring.CursorLight;
        double safeW = Math.Max(width, 1);
        double safeH = Math.Max(height, 1);
        double aspect = safeW / safeH;

        double[]? cdf = !useTrap && coloring.Mode == ColoringMode.Histogram
            ? BuildHistogram(mu, maxIterations)
            : null;

        double density = Math.Max(coloring.Density, 0.05);

        // Precompute directional light and Blinn-Phong halfway vector once per frame.
        const double elevCos = 0.7342162;
        const double elevSin = 0.6789146;
        double angleRad = coloring.LightAngle * (Math.PI / 180.0);
        double dirLx = Math.Cos(angleRad) * elevCos;
        double dirLy = Math.Sin(angleRad) * elevCos;
        double dirLz = elevSin;
        double dirHx = dirLx;
        double dirHy = dirLy;
        double dirHz = dirLz + 1.0;
        double dirHLen = Math.Sqrt(dirHx * dirHx + dirHy * dirHy + dirHz * dirHz);
        dirHx /= dirHLen;
        dirHy /= dirHLen;
        dirHz /= dirHLen;

        for (int i = 0; i < pixels.Length; i++)
        {
            float sample = mu[i];
            if (sample < 0f)
            {
                if (useTrap && coloring.Interior == InteriorStyle.Glow)
                    pixels[i] = Scale(SampleOrbitTrap(coloring.Palette, trapDist[i], density, coloring.Phase), 0.85);
                else
                    pixels[i] = Interior(coloring.Interior, minRadius[i]);
                continue;
            }

            Color color;
            if (useTrap)
            {
                color = SampleOrbitTrap(coloring.Palette, trapDist[i], density, coloring.Phase);
            }
            else
            {
                double t;
                if (cdf != null)
                {
                    double position = HistogramPosition(cdf, sample, coloring.Smooth);
                    t = position * density + coloring.Phase;
                }
                else
                {
                    double value = coloring.Smooth ? sample : Math.Floor(sample);
                    t = value * 0.035 * density + coloring.Phase;
                }

                color = Sample(coloring.Palette, t);
                if (coloring.Mode == ColoringMode.Cyclic)
                    color = Scale(color, Math.Tanh(sample / 9.0));
            }

            if (useLight)
            {
                if (useCursorLight)
                {
                    int x = i % width;
                    int y = i / width;
                    double pixX = ((x + 0.5) / safeW - 0.5) * aspect;
                    double pixY = -((y + 0.5) / safeH - 0.5);
                    color = ApplyPointLighting(color, normalX[i], normalY[i], coloring.LightX - pixX, coloring.LightY - pixY);
                }
                else
                {
                    color = ApplyNormalLighting(color, normalX[i], normalY[i], dirLx, dirLy, dirLz, dirHx, dirHy, dirHz);
                }
            }

            pixels[i] = color;
        }
    }

    static Color SampleOrbitTrap(int palette, float dist, double density, double phase)
    {
        double d = Math.Max(dist, 0.0);
        double t = Math.Log(1.0 + d * 6.0) * 0.55 * density + phase;
        Color baseCol = Sample(palette, t);
        double filament = Math.Exp(-d * 3.2);
        double falloff = 1.0 / (1.0 + d * 1.4);
        double shade = 0.35 + 0.65 * falloff;
        double blend = filament * 0.65;

        double r = baseCol.r / 255.0;
        double g = baseCol.g / 255.0;
        double b = baseCol.b / 255.0;

        double nr = r * shade * (1.0 - blend) + (r * 1.15 + 0.18) * blend;
        double ng = g * shade * (1.0 - blend) + (g * 1.15 + 0.18) * blend;
        double nb = b * shade * (1.0 - blend) + (b * 1.15 + 0.18) * blend;

        return new Color(
            (int)(Math.Clamp(nr, 0.0, 1.0) * 255.0),
            (int)(Math.Clamp(ng, 0.0, 1.0) * 255.0),
            (int)(Math.Clamp(nb, 0.0, 1.0) * 255.0),
            255);
    }

    static Color ApplyNormalLighting(Color color, float ux, float uy, double lx, double ly, double lz, double hx, double hy, double hz)
    {
        const double invRelief = 1.0 / 1.75;

        double nx = ux;
        double ny = uy;
        double nz = invRelief;
        double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len > 1e-12)
        {
            nx /= len;
            ny /= len;
            nz /= len;
        }
        else
        {
            nx = 0;
            ny = 0;
            nz = 1;
        }

        double diff = Math.Max(nx * lx + ny * ly + nz * lz, 0.0);
        double ndoth = Math.Max(nx * hx + ny * hy + nz * hz, 0.0);
        double spec = Math.Pow(ndoth, 24.0);
        double gain = 0.24 + 0.92 * diff;
        double add = 0.38 * spec;

        double r = Math.Clamp(color.r / 255.0 * gain + add, 0.0, 1.0);
        double g = Math.Clamp(color.g / 255.0 * gain + add, 0.0, 1.0);
        double b = Math.Clamp(color.b / 255.0 * gain + add, 0.0, 1.0);

        return new Color((int)(r * 255.0), (int)(g * 255.0), (int)(b * 255.0), 255);
    }

    static Color ApplyPointLighting(Color color, float ux, float uy, double dx, double dy)
    {
        const double invRelief = 1.0 / 1.75;
        const double lightZ = 0.52;

        double nx = ux;
        double ny = uy;
        double nz = invRelief;
        double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nLen > 1e-12)
        {
            nx /= nLen;
            ny /= nLen;
            nz /= nLen;
        }
        else
        {
            nx = 0;
            ny = 0;
            nz = 1;
        }

        double dist2 = dx * dx + dy * dy + lightZ * lightZ;
        double dist = Math.Sqrt(dist2);
        double lx = dx / dist;
        double ly = dy / dist;
        double lz = lightZ / dist;

        double hx = lx;
        double hy = ly;
        double hz = lz + 1.0;
        double hLen = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        hx /= hLen;
        hy /= hLen;
        hz /= hLen;

        double atten = 1.0 / (1.0 + 0.55 * dist2);
        double diff = Math.Max(nx * lx + ny * ly + nz * lz, 0.0);
        double ndoth = Math.Max(nx * hx + ny * hy + nz * hz, 0.0);
        double spec = Math.Pow(ndoth, 24.0);
        double gain = 0.22 + 0.98 * diff * atten;
        double add = 0.42 * spec * atten;

        double r = Math.Clamp(color.r / 255.0 * gain + add, 0.0, 1.0);
        double g = Math.Clamp(color.g / 255.0 * gain + add, 0.0, 1.0);
        double b = Math.Clamp(color.b / 255.0 * gain + add, 0.0, 1.0);

        return new Color((int)(r * 255.0), (int)(g * 255.0), (int)(b * 255.0), 255);
    }

    public static Color Sample(int palette, double t)
    {
        t -= Math.Floor(t);
        return SampleUnwrapped(palette, t);
    }

    /// <summary>
    /// Samples a palette across t in [0, 1] without wrapping t = 1 back to 0.
    /// Used when baking LUT rows so non-cyclic palettes preserve their endpoint stop.
    /// </summary>
    public static Color SampleUnwrapped(int palette, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        if (palette == 7)
            return Hsv(t >= 1.0 ? 0.0 : t, 0.82, 1.0);

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

    static double HistogramPosition(double[] cdf, float sample, bool smooth)
    {
        int bucket = (int)sample;
        if (bucket < 0) bucket = 0;
        if (bucket >= cdf.Length) bucket = cdf.Length - 1;
        double here = cdf[bucket];
        if (!smooth) return here;

        int next = Math.Min(bucket + 1, cdf.Length - 1);
        double frac = Math.Clamp(sample - bucket, 0, 1);
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
        int sector = (int)(h * 6) % 6;
        (double r, double g, double b) = sector switch
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
