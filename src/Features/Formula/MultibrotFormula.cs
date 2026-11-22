namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Evaluates orbits of z = z^n + c. Integer powers use complex multiplication;
/// fractional powers use the polar form and the principal branch of the argument.
/// </summary>
public static class MultibrotFormula
{
    public const double Bailout = 16.0;
    const double PeriodicityEpsilon = 1e-13;

    public readonly record struct Escape(float Mu, float MinRadius)
    {
        public bool Inside => Mu < 0f;
    }

    public readonly record struct OrbitProbe(
        bool Inside,
        int Iterations,
        float Mu,
        double FinalZx,
        double FinalZy,
        float MinRadius);

    public static bool IsInMandelbrotCardioidOrBulb(double cx, double cy)
    {
        double xm = cx - 0.25;
        double cy2 = cy * cy;
        double q = xm * xm + cy2;
        if (q * (q + xm) <= 0.25 * cy2)
            return true;

        double xp = cx + 1.0;
        return xp * xp + cy2 <= 0.0625;
    }

    public static Escape Evaluate(
        double zx,
        double zy,
        double cx,
        double cy,
        double power,
        int maxIterations,
        bool needsMinRadius = true)
    {
        if (maxIterations < 1 || !double.IsFinite(power) || Math.Abs(power) < FormulaParams.MinNonZeroPower)
            return new Escape(-1f, 0f);

        if (FormulaParams.TryGetIntegerExponent(power, out int n))
        {
            return n switch
            {
                2 => EvaluateSquare(zx, zy, cx, cy, maxIterations, needsMinRadius),
                3 => EvaluateCube(zx, zy, cx, cy, maxIterations, needsMinRadius),
                4 => EvaluateFourth(zx, zy, cx, cy, maxIterations, needsMinRadius),
                _ => EvaluateInteger(zx, zy, cx, cy, n, maxIterations, needsMinRadius),
            };
        }

        return EvaluateReal(zx, zy, cx, cy, power, maxIterations);
    }

    public static OrbitProbe Probe(
        double zx,
        double zy,
        double cx,
        double cy,
        double power,
        int maxIterations)
    {
        if (maxIterations < 1 || !double.IsFinite(power) || Math.Abs(power) < FormulaParams.MinNonZeroPower)
            return new OrbitProbe(true, 0, -1f, zx, zy, 0f);

        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;

        if (FormulaParams.TryGetIntegerExponent(power, out int n))
        {
            if (n == 2)
            {
                for (int i = 0; i < maxIterations; i++)
                {
                    double x2 = zx * zx;
                    double y2 = zy * zy;
                    double r2 = x2 + y2;
                    if (r2 < minR) minR = r2;
                    if (!double.IsFinite(r2) || r2 > bailout2)
                    {
                        Escape esc = Escaped(i, r2, 2.0, minR);
                        return new OrbitProbe(false, i, esc.Mu, zx, zy, esc.MinRadius);
                    }

                    zy = 2.0 * zx * zy + cy;
                    zx = x2 - y2 + cx;
                }

                return new OrbitProbe(true, maxIterations, -1f, zx, zy, Radius(minR));
            }

            for (int i = 0; i < maxIterations; i++)
            {
                double r2 = zx * zx + zy * zy;
                if (r2 < minR) minR = r2;
                if (!double.IsFinite(r2) || r2 > bailout2)
                {
                    Escape esc = Escaped(i, r2, n, minR);
                    return new OrbitProbe(false, i, esc.Mu, zx, zy, esc.MinRadius);
                }

                if (!PowInteger(ref zx, ref zy, n))
                    return new OrbitProbe(false, i, i, double.NaN, double.NaN, Radius(minR));

                zx += cx;
                zy += cy;
                if (!double.IsFinite(zx) || !double.IsFinite(zy))
                    return new OrbitProbe(false, i, i, zx, zy, Radius(minR));
            }

            return new OrbitProbe(true, maxIterations, -1f, zx, zy, Radius(minR));
        }

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > bailout2)
            {
                Escape esc = Escaped(i, r2, power, minR);
                return new OrbitProbe(false, i, esc.Mu, zx, zy, esc.MinRadius);
            }

            if (r2 == 0)
            {
                if (power > 0)
                {
                    zx = cx;
                    zy = cy;
                    continue;
                }

                return new OrbitProbe(false, i, i, 0, 0, 0f);
            }

            double radius = Math.Sqrt(r2);
            double theta = Math.Atan2(zy, zx);
            double raised = Math.Pow(radius, power);
            double angle = power * theta;
            zx = raised * Math.Cos(angle) + cx;
            zy = raised * Math.Sin(angle) + cy;

            if (!double.IsFinite(zx) || !double.IsFinite(zy))
                return new OrbitProbe(false, i, i, zx, zy, Radius(minR));
        }

        return new OrbitProbe(true, maxIterations, -1f, zx, zy, Radius(minR));
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
        float[] minRadius,
        bool needsMinRadius = true)
    {
        double cy = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
        int row = y * width;
        bool useCardioidCheck = !needsMinRadius
            && !formula.Julia
            && FormulaParams.TryGetIntegerExponent(formula.Power, out int n)
            && n == 2;

        for (int x = 0; x < width; x++)
        {
            double cx = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
            if (useCardioidCheck && IsInMandelbrotCardioidOrBulb(cx, cy))
            {
                mu[row + x] = -1f;
                minRadius[row + x] = 0f;
                continue;
            }

            Escape escape = formula.Julia
                ? Evaluate(cx, cy, formula.JuliaRe, formula.JuliaIm, formula.Power, formula.MaxIterations, needsMinRadius)
                : Evaluate(0, 0, cx, cy, formula.Power, formula.MaxIterations, needsMinRadius);
            mu[row + x] = escape.Mu;
            minRadius[row + x] = escape.MinRadius;
        }
    }

    static Escape EvaluateSquare(double zx, double zy, double cx, double cy, int maxIterations, bool needsMinRadius)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;
        double ox = zx, oy = zy;
        int period = 8;
        int check = 0;

        for (int i = 0; i < maxIterations; i++)
        {
            double x2 = zx * zx;
            double y2 = zy * zy;
            double r2 = x2 + y2;
            if (needsMinRadius && r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, 2.0, needsMinRadius ? minR : 0);

            zy = 2.0 * zx * zy + cy;
            zx = x2 - y2 + cx;

            if (!needsMinRadius)
            {
                if (Math.Abs(zx - ox) <= PeriodicityEpsilon && Math.Abs(zy - oy) <= PeriodicityEpsilon)
                    return new Escape(-1f, 0f);

                if (++check >= period)
                {
                    ox = zx;
                    oy = zy;
                    check = 0;
                    if (period < 1024) period <<= 1;
                }
            }
        }

        return needsMinRadius ? Inside(minR) : new Escape(-1f, 0f);
    }

    static Escape EvaluateCube(double zx, double zy, double cx, double cy, int maxIterations, bool needsMinRadius)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;
        double ox = zx, oy = zy;
        int period = 8;
        int check = 0;

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (needsMinRadius && r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, 3.0, needsMinRadius ? minR : 0);

            double x2 = zx * zx - zy * zy;
            double y2 = 2.0 * zx * zy;
            double nx = x2 * zx - y2 * zy + cx;
            double ny = x2 * zy + y2 * zx + cy;
            zx = nx;
            zy = ny;

            if (!needsMinRadius)
            {
                if (Math.Abs(zx - ox) <= PeriodicityEpsilon && Math.Abs(zy - oy) <= PeriodicityEpsilon)
                    return new Escape(-1f, 0f);

                if (++check >= period)
                {
                    ox = zx;
                    oy = zy;
                    check = 0;
                    if (period < 1024) period <<= 1;
                }
            }
        }

        return needsMinRadius ? Inside(minR) : new Escape(-1f, 0f);
    }

    static Escape EvaluateFourth(double zx, double zy, double cx, double cy, int maxIterations, bool needsMinRadius)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;
        double ox = zx, oy = zy;
        int period = 8;
        int check = 0;

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (needsMinRadius && r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, 4.0, needsMinRadius ? minR : 0);

            double x2 = zx * zx - zy * zy;
            double y2 = 2.0 * zx * zy;
            zx = x2 * x2 - y2 * y2 + cx;
            zy = 2.0 * x2 * y2 + cy;

            if (!needsMinRadius)
            {
                if (Math.Abs(zx - ox) <= PeriodicityEpsilon && Math.Abs(zy - oy) <= PeriodicityEpsilon)
                    return new Escape(-1f, 0f);

                if (++check >= period)
                {
                    ox = zx;
                    oy = zy;
                    check = 0;
                    if (period < 1024) period <<= 1;
                }
            }
        }

        return needsMinRadius ? Inside(minR) : new Escape(-1f, 0f);
    }

    static Escape EvaluateInteger(double zx, double zy, double cx, double cy, int power, int maxIterations, bool needsMinRadius)
    {
        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;
        double ox = zx, oy = zy;
        int period = 8;
        int check = 0;

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (needsMinRadius && r2 < minR) minR = r2;
            if (r2 > bailout2)
                return Escaped(i, r2, power, needsMinRadius ? minR : 0);

            if (!PowInteger(ref zx, ref zy, power))
                return Stopped(i, needsMinRadius ? minR : 0);

            zx += cx;
            zy += cy;
            if (double.IsNaN(zx) || double.IsInfinity(zx))
                return Stopped(i, needsMinRadius ? minR : 0);

            if (!needsMinRadius)
            {
                if (Math.Abs(zx - ox) <= PeriodicityEpsilon && Math.Abs(zy - oy) <= PeriodicityEpsilon)
                    return new Escape(-1f, 0f);

                if (++check >= period)
                {
                    ox = zx;
                    oy = zy;
                    check = 0;
                    if (period < 1024) period <<= 1;
                }
            }
        }

        return needsMinRadius ? Inside(minR) : new Escape(-1f, 0f);
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

    internal static double TrapDistance(double zx, double zy, int trapMode) => trapMode switch
    {
        1 => Math.Sqrt(zx * zx + zy * zy),
        2 => Math.Min(Math.Abs(zx), Math.Abs(zy)) + Math.Max(0.0, Math.Sqrt(zx * zx + zy * zy) - 2.0),
        3 => Math.Abs(Math.Sqrt(zx * zx + zy * zy) - 1.0),
        4 => Math.Abs(Math.Max(Math.Abs(zx), Math.Abs(zy)) - 1.0),
        _ => 0.0,
    };

    internal static void ComputeUnitNormal(double zx, double zy, double dx, double dy, out float nx, out float ny)
    {
        double ux = zx * dx + zy * dy;
        double uy = zy * dx - zx * dy;
        double len = Math.Sqrt(ux * ux + uy * uy);
        if (double.IsFinite(len) && len > 1e-18)
        {
            nx = (float)(ux / len);
            ny = (float)(uy / len);
        }
        else
        {
            nx = 0f;
            ny = 0f;
        }
    }

    internal static void StepDerivativeInteger(
        double zx,
        double zy,
        int n,
        bool addOne,
        ref double dx,
        ref double dy,
        ref bool dScaled,
        out double powZx,
        out double powZy)
    {
        if (n == 2)
        {
            double ndx = 2.0 * (zx * dx - zy * dy);
            double ndy = 2.0 * (zx * dy + zy * dx);
            if (addOne && !dScaled) ndx += 1.0;
            dx = ndx;
            dy = ndy;
            RenormalizeDerivative(ref dx, ref dy, ref dScaled);
            powZx = zx * zx - zy * zy;
            powZy = 2.0 * zx * zy;
            return;
        }

        double pmx = zx;
        double pmy = zy;
        if (!PowInteger(ref pmx, ref pmy, n - 1))
        {
            dx = double.NaN;
            dy = double.NaN;
            powZx = double.NaN;
            powZy = double.NaN;
            return;
        }

        double dnx = n * (pmx * dx - pmy * dy);
        double dny = n * (pmx * dy + pmy * dx);
        if (addOne && !dScaled) dnx += 1.0;
        dx = dnx;
        dy = dny;
        RenormalizeDerivative(ref dx, ref dy, ref dScaled);

        powZx = pmx * zx - pmy * zy;
        powZy = pmx * zy + pmy * zx;
    }

    internal static void RenormalizeDerivative(ref double dx, ref double dy, ref bool dScaled)
    {
        double d2 = dx * dx + dy * dy;
        if (d2 > 1e20 || (dScaled && d2 > 0.0 && d2 < 1e-20))
        {
            double inv = 1.0 / Math.Sqrt(d2);
            dx *= inv;
            dy *= inv;
            dScaled = true;
        }
    }

    public static void EvaluateShadedRow(
        int width,
        int height,
        int y,
        double originX,
        double originY,
        double halfW,
        double halfH,
        in FormulaParams formula,
        float[] mu,
        float[] minRadius,
        float[] trapDist,
        float[] normalX,
        float[] normalY,
        bool needsMinRadius,
        bool computeNormals,
        int orbitTrapMode)
    {
        double cy = originY - ((y + 0.5) / height - 0.5) * 2.0 * halfH;
        int row = y * width;
        bool useCardioidCheck = !needsMinRadius
            && !formula.Julia
            && FormulaParams.TryGetIntegerExponent(formula.Power, out int intExp)
            && intExp == 2;

        for (int x = 0; x < width; x++)
        {
            double cx = originX + ((x + 0.5) / width - 0.5) * 2.0 * halfW;
            int idx = row + x;

            if (useCardioidCheck && IsInMandelbrotCardioidOrBulb(cx, cy))
            {
                mu[idx] = -1f;
                minRadius[idx] = 0f;
                trapDist[idx] = 0f;
                normalX[idx] = 0f;
                normalY[idx] = 0f;
                continue;
            }

            if (formula.Julia)
                EvaluateShadedPoint(cx, cy, formula.JuliaRe, formula.JuliaIm, formula.Power, formula.MaxIterations, true, needsMinRadius, computeNormals, orbitTrapMode, out mu[idx], out minRadius[idx], out trapDist[idx], out normalX[idx], out normalY[idx]);
            else
                EvaluateShadedPoint(0, 0, cx, cy, formula.Power, formula.MaxIterations, false, needsMinRadius, computeNormals, orbitTrapMode, out mu[idx], out minRadius[idx], out trapDist[idx], out normalX[idx], out normalY[idx]);
        }
    }

    static void EvaluateShadedPoint(
        double zx,
        double zy,
        double cx,
        double cy,
        double power,
        int maxIterations,
        bool julia,
        bool needsMinRadius,
        bool computeNormals,
        int orbitTrapMode,
        out float outMu,
        out float outMinRadius,
        out float outTrapDist,
        out float outNx,
        out float outNy)
    {
        outNx = 0f;
        outNy = 0f;

        if (maxIterations < 1 || !double.IsFinite(power) || Math.Abs(power) < FormulaParams.MinNonZeroPower)
        {
            outMu = -1f;
            outMinRadius = 0f;
            outTrapDist = 0f;
            return;
        }

        const double bailout2 = Bailout * Bailout;
        double minR = double.MaxValue;
        double minTrap = double.MaxValue;
        double dx = julia ? 1.0 : 0.0;
        double dy = 0.0;
        bool dScaled = false;
        bool addOne = !julia;
        bool trackTrap = orbitTrapMode != 0;
        bool isInteger = FormulaParams.TryGetIntegerExponent(power, out int n);

        for (int i = 0; i < maxIterations; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (needsMinRadius && r2 < minR) minR = r2;

            if (!double.IsFinite(r2) || r2 > bailout2)
            {
                Escape esc = Escaped(i, r2, isInteger ? n : power, needsMinRadius ? minR : 0);
                outMu = esc.Mu;
                outMinRadius = esc.MinRadius;
                outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : TrapDistance(zx, zy, orbitTrapMode)) : 0f;

                if (computeNormals)
                {
                    if (r2 < 1e12)
                        RefineNormalStep(ref zx, ref zy, cx, cy, power, isInteger, n, addOne, ref dx, ref dy, ref dScaled);
                    ComputeUnitNormal(zx, zy, dx, dy, out outNx, out outNy);
                }

                return;
            }

            if (trackTrap && i > 0)
            {
                double td = TrapDistance(zx, zy, orbitTrapMode);
                if (td < minTrap) minTrap = td;
            }

            if (isInteger)
            {
                if (computeNormals)
                {
                    StepDerivativeInteger(zx, zy, n, addOne, ref dx, ref dy, ref dScaled, out double pzx, out double pzy);
                    zx = pzx + cx;
                    zy = pzy + cy;
                }
                else
                {
                    if (n == 2)
                    {
                        double nx = zx * zx - zy * zy + cx;
                        zy = 2.0 * zx * zy + cy;
                        zx = nx;
                    }
                    else
                    {
                        if (!PowInteger(ref zx, ref zy, n))
                        {
                            outMu = i;
                            outMinRadius = needsMinRadius ? Radius(minR) : 0f;
                            outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : 0.0) : 0f;
                            return;
                        }

                        zx += cx;
                        zy += cy;
                    }
                }
            }
            else
            {
                if (r2 == 0.0)
                {
                    if (power > 0.0)
                    {
                        if (computeNormals)
                        {
                            dx = addOne && !dScaled ? 1.0 : 0.0;
                            dy = 0.0;
                        }

                        zx = cx;
                        zy = cy;
                        continue;
                    }

                    outMu = i;
                    outMinRadius = needsMinRadius ? Radius(minR) : 0f;
                    outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : 0.0) : 0f;
                    return;
                }

                double radius = Math.Sqrt(r2);
                double theta = Math.Atan2(zy, zx);
                if (computeNormals)
                {
                    double rp = power * Math.Pow(radius, power - 1.0);
                    double ap = (power - 1.0) * theta;
                    double pr = rp * Math.Cos(ap);
                    double pi = rp * Math.Sin(ap);
                    double ndx = pr * dx - pi * dy;
                    double ndy = pr * dy + pi * dx;
                    if (addOne && !dScaled) ndx += 1.0;
                    dx = ndx;
                    dy = ndy;
                    RenormalizeDerivative(ref dx, ref dy, ref dScaled);
                }

                double raised = Math.Pow(radius, power);
                double angle = power * theta;
                zx = raised * Math.Cos(angle) + cx;
                zy = raised * Math.Sin(angle) + cy;
            }

            if (!double.IsFinite(zx) || !double.IsFinite(zy))
            {
                outMu = i;
                outMinRadius = needsMinRadius ? Radius(minR) : 0f;
                outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : 0.0) : 0f;
                return;
            }
        }

        outMu = -1f;
        outMinRadius = needsMinRadius ? Radius(minR) : 0f;
        outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : TrapDistance(zx, zy, orbitTrapMode)) : 0f;
    }

    static void RefineNormalStep(
        ref double zx,
        ref double zy,
        double cx,
        double cy,
        double power,
        bool isInteger,
        int n,
        bool addOne,
        ref double dx,
        ref double dy,
        ref bool dScaled)
    {
        if (isInteger)
        {
            StepDerivativeInteger(zx, zy, n, addOne, ref dx, ref dy, ref dScaled, out double pzx, out double pzy);
            zx = pzx + cx;
            zy = pzy + cy;
        }
        else
        {
            double r2 = zx * zx + zy * zy;
            if (r2 <= 0.0) return;
            double radius = Math.Sqrt(r2);
            double theta = Math.Atan2(zy, zx);
            double rp = power * Math.Pow(radius, power - 1.0);
            double ap = (power - 1.0) * theta;
            double pr = rp * Math.Cos(ap);
            double pi = rp * Math.Sin(ap);
            double ndx = pr * dx - pi * dy;
            double ndy = pr * dy + pi * dx;
            if (addOne && !dScaled) ndx += 1.0;
            dx = ndx;
            dy = ndy;
            RenormalizeDerivative(ref dx, ref dy, ref dScaled);
            double raised = Math.Pow(radius, power);
            double angle = power * theta;
            zx = raised * Math.Cos(angle) + cx;
            zy = raised * Math.Sin(angle) + cy;
        }
    }
}
