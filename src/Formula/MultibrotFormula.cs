namespace Mandelbrot.Visualizer.Formula;

/// <summary>
/// One orbit of z = z^n + c. Integer powers use complex multiplication.
/// Other powers, including fractions such as 1.4, use the polar form and the
/// principal branch of the argument.
/// </summary>
public static class MultibrotFormula
{
    public const double Bailout = 16.0;

    public readonly record struct Escape(float Mu, float MinRadius)
    {
        public bool Inside => Mu < 0f;
    }

    public static Escape Evaluate(double zx, double zy, double cx, double cy, double power, int maxIterations)
    {
        if (maxIterations < 1 || !double.IsFinite(power) || Math.Abs(power) < 1e-6)
            return new Escape(-1f, 0f);

        double rounded = Math.Round(power);
        if (Math.Abs(power - rounded) < 1e-8 && rounded is >= -16 and <= 16)
        {
            int n = (int)rounded;
            if (n == 2) return EvaluateSquare(zx, zy, cx, cy, maxIterations);
            if (n == 3) return EvaluateCube(zx, zy, cx, cy, maxIterations);
            if (n == 4) return EvaluateFourth(zx, zy, cx, cy, maxIterations);
            return EvaluateInteger(zx, zy, cx, cy, n, maxIterations);
        }

        return EvaluateReal(zx, zy, cx, cy, power, maxIterations);
    }

    static Escape EvaluateSquare(double zx, double zy, double cx, double cy, int maxIterations)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;

        for (int i = 0; i < maxIterations; i++)
        {
            double x2 = zx * zx;
            double y2 = zy * zy;
            double r2 = x2 + y2;
            if (r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, 2.0, minR);

            zy = 2.0 * zx * zy + cy;
            zx = x2 - y2 + cx;
        }

        return Inside(minR);
    }

    static Escape EvaluateCube(double zx, double zy, double cx, double cy, int maxIterations)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, 3.0, minR);

            double x2 = zx * zx - zy * zy;
            double y2 = 2.0 * zx * zy;
            double nx = x2 * zx - y2 * zy + cx;
            double ny = x2 * zy + y2 * zx + cy;
            zx = nx;
            zy = ny;
        }

        return Inside(minR);
    }

    static Escape EvaluateFourth(double zx, double zy, double cx, double cy, int maxIterations)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, 4.0, minR);

            double x2 = zx * zx - zy * zy;
            double y2 = 2.0 * zx * zy;
            zx = x2 * x2 - y2 * y2 + cx;
            zy = 2.0 * x2 * y2 + cy;
        }

        return Inside(minR);
    }

    static Escape EvaluateInteger(double zx, double zy, double cx, double cy, int power, int maxIterations)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, power, minR);

            if (!PowInteger(ref zx, ref zy, power))
                return new Escape(i, (float)Math.Sqrt(minR == double.MaxValue ? 0 : minR));

            zx += cx;
            zy += cy;
            if (double.IsNaN(zx) || double.IsInfinity(zx))
                return new Escape(i, Radius(minR));
        }

        return Inside(minR);
    }

    static Escape EvaluateReal(double zx, double zy, double cx, double cy, double power, int maxIterations)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, power, minR);

            if (r2 == 0)
            {
                if (power > 0)
                {
                    zx = cx;
                    zy = cy;
                    continue;
                }

                return new Escape(i, 0f);
            }

            double radius = Math.Sqrt(r2);
            double theta = Math.Atan2(zy, zx);
            double raised = Math.Pow(radius, power);
            double angle = power * theta;
            zx = raised * Math.Cos(angle) + cx;
            zy = raised * Math.Sin(angle) + cy;

            if (double.IsNaN(zx) || double.IsInfinity(zx))
                return new Escape(i, Radius(minR));
        }

        return Inside(minR);
    }

    static bool PowInteger(ref double zx, ref double zy, int power)
    {
        if (power == 0)
        {
            zx = 1;
            zy = 0;
            return true;
        }

        bool negative = power < 0;
        int exponent = Math.Abs(power);
        double rx = 1, ry = 0;
        double bx = zx, by = zy;

        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                double nextX = rx * bx - ry * by;
                double nextY = rx * by + ry * bx;
                rx = nextX;
                ry = nextY;
            }

            exponent >>= 1;
            if (exponent == 0) break;

            double squareX = bx * bx - by * by;
            double squareY = 2 * bx * by;
            bx = squareX;
            by = squareY;
        }

        if (!negative)
        {
            zx = rx;
            zy = ry;
            return true;
        }

        double denom = rx * rx + ry * ry;
        if (denom == 0) return false;
        zx = rx / denom;
        zy = -ry / denom;
        return true;
    }

    internal static Escape Escaped(int iteration, double radiusSquared, double power, double minRadiusSquared)
    {
        double logZn = 0.5 * Math.Log(radiusSquared);
        double logPower = Math.Log(Math.Abs(power));
        double nu = logPower > 1e-8 ? Math.Log(logZn / Math.Log(Bailout)) / logPower : 0;
        float mu = (float)(iteration + 1 - nu);
        if (float.IsNaN(mu) || mu < 0) mu = iteration;
        return new Escape(mu, Radius(minRadiusSquared));
    }

    internal static Escape Inside(double minRadiusSquared) => new(-1f, Radius(minRadiusSquared));

    internal static Escape Stopped(int iteration, double minRadiusSquared) => new(iteration, Radius(minRadiusSquared));

    internal static float Radius(double radiusSquared)
    {
        if (radiusSquared == double.MaxValue || radiusSquared < 0) return 0f;
        return (float)Math.Sqrt(radiusSquared);
    }
}
