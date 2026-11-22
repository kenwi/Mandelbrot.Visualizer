namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Evaluates orbits of z = z^n + c. Integer powers use complex multiplication;
/// fractional powers use the polar form and the principal branch of the argument.
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
        if (maxIterations < 1 || !double.IsFinite(power) || Math.Abs(power) < FormulaParams.MinNonZeroPower)
            return new Escape(-1f, 0f);

        if (FormulaParams.TryGetIntegerExponent(power, out int n))
        {
            return n switch
            {
                2 => EvaluateSquare(zx, zy, cx, cy, maxIterations),
                3 => EvaluateCube(zx, zy, cx, cy, maxIterations),
                4 => EvaluateFourth(zx, zy, cx, cy, maxIterations),
                _ => EvaluateInteger(zx, zy, cx, cy, n, maxIterations),
            };
        }

        return EvaluateReal(zx, zy, cx, cy, power, maxIterations);
    }

    public static void EvaluateScalarRow(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        in FormulaParams formula,
        float[] mu,
        float[] minRadius)
    {
        double cy = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
        int row = y * width;
        for (int x = 0; x < width; x++)
        {
            double cx = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
            Escape escape = formula.Julia
                ? Evaluate(cx, cy, formula.JuliaRe, formula.JuliaIm, formula.Power, formula.MaxIterations)
                : Evaluate(0, 0, cx, cy, formula.Power, formula.MaxIterations);
            mu[row + x] = escape.Mu;
            minRadius[row + x] = escape.MinRadius;
        }
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
                return Stopped(i, minR);

            zx += cx;
            zy += cy;
            if (double.IsNaN(zx) || double.IsInfinity(zx))
                return Stopped(i, minR);
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
                return Stopped(i, minR);
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
