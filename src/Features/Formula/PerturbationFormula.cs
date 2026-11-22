using System.Runtime.CompilerServices;
using Mandelbrot.Visualizer.Shared;

namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Evaluates pixels relative to a high-precision ReferenceOrbit using perturbation theory
/// (Mandelbrot n=2, integer Multibrot n=2..16 via binomial expansion, and Julia sets)
/// with Zhuoran rebasing to eliminate glitches and ComplexExp below double underflow (1e-280).
/// </summary>
public static class PerturbationFormula
{
    const double Bailout2 = MultibrotFormula.Bailout * MultibrotFormula.Bailout;
    static readonly FloatExp DoubleUnderflowThreshold = FloatExp.FromDouble(1e-280);

    // Precomputed Pascal's triangle for integer powers up to 16.
    static readonly double[,] Binom = BuildPascalTriangle(FormulaParams.IntegerExponentMax);

    static double[,] BuildPascalTriangle(int maxN)
    {
        var table = new double[maxN + 1, maxN + 1];
        for (int n = 0; n <= maxN; n++)
        {
            table[n, 0] = 1.0;
            table[n, n] = 1.0;
            for (int k = 1; k < n; k++)
                table[n, k] = table[n - 1, k - 1] + table[n - 1, k];
        }

        return table;
    }

    public static void EvaluateRow(
        int width,
        int height,
        int y,
        FloatExp refOffsetX,
        FloatExp refOffsetY,
        FloatExp halfW,
        FloatExp halfH,
        ReferenceOrbit orbit,
        in FormulaParams formula,
        float[] mu,
        float[] minRadius,
        bool needsMinRadius)
    {
        int row = y * width;
        double v = ((y + 0.5) / height - 0.5) * 2.0;
        FloatExp dcy = refOffsetY - halfH * v;
        bool useExtended = halfH <= DoubleUnderflowThreshold;

        if (!useExtended)
        {
            double dcyD = dcy.ToDouble();
            double refOffXD = refOffsetX.ToDouble();
            double halfWD = halfW.ToDouble();

            for (int x = 0; x < width; x++)
            {
                double u = ((x + 0.5) / width - 0.5) * 2.0;
                double dcxD = refOffXD + halfWD * u;
                var escape = EvaluateDouble(dcxD, dcyD, orbit, formula, needsMinRadius);
                mu[row + x] = escape.Mu;
                minRadius[row + x] = escape.MinRadius;
            }
        }
        else
        {
            for (int x = 0; x < width; x++)
            {
                double u = ((x + 0.5) / width - 0.5) * 2.0;
                FloatExp dcx = refOffsetX + halfW * u;
                var escape = EvaluateExtended(ComplexExp.FromFloatExp(dcx, dcy), orbit, formula, needsMinRadius);
                mu[row + x] = escape.Mu;
                minRadius[row + x] = escape.MinRadius;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static MultibrotFormula.Escape EvaluateDouble(
        double dcx,
        double dcy,
        ReferenceOrbit orbit,
        in FormulaParams formula,
        bool needsMinRadius)
    {
        double[] refZx = orbit.Zx;
        double[] refZy = orbit.Zy;
        int refLimit = orbit.Length - 1;
        int exponent = orbit.Exponent;
        int maxIter = formula.MaxIterations;
        bool julia = formula.Julia;

        double dzx = julia ? dcx : 0.0;
        double dzy = julia ? dcy : 0.0;
        double addCx = julia ? 0.0 : dcx;
        double addCy = julia ? 0.0 : dcy;

        int refIdx = 0;
        double minR = double.MaxValue;
        Span<double> powRe = stackalloc double[17];
        Span<double> powIm = stackalloc double[17];

        for (int i = 0; i < maxIter; i++)
        {
            double Zx = refZx[refIdx];
            double Zy = refZy[refIdx];
            double zx = Zx + dzx;
            double zy = Zy + dzy;
            double r2 = zx * zx + zy * zy;

            if (needsMinRadius && r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > Bailout2)
                return MultibrotFormula.Escaped(i, r2, exponent, needsMinRadius ? minR : 0);

            if (!julia)
            {
                double dz2 = dzx * dzx + dzy * dzy;
                if (r2 < dz2 || refIdx >= refLimit)
                {
                    dzx = zx;
                    dzy = zy;
                    refIdx = 0;
                    Zx = 0.0;
                    Zy = 0.0;
                }
            }
            else if (refIdx >= refLimit)
            {
                // Reference Julia orbit ended; continue exact direct iteration from current z.
                return ContinueDirect(zx, zy, formula.JuliaRe, formula.JuliaIm, exponent, i, maxIter, minR, needsMinRadius);
            }

            if (exponent == 2)
            {
                // dz' = dz * (2*Z + dz) + dc
                double tx = 2.0 * Zx + dzx;
                double ty = 2.0 * Zy + dzy;
                double nx = dzx * tx - dzy * ty + addCx;
                double ny = dzx * ty + dzy * tx + addCy;
                dzx = nx;
                dzy = ny;
            }
            else
            {
                PerturbStepDouble(Zx, Zy, ref dzx, ref dzy, exponent, powRe, powIm);
                dzx += addCx;
                dzy += addCy;
            }

            refIdx++;
        }

        return needsMinRadius ? MultibrotFormula.Inside(minR) : new MultibrotFormula.Escape(-1f, 0f);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static MultibrotFormula.Escape EvaluateExtended(
        ComplexExp dc,
        ReferenceOrbit orbit,
        in FormulaParams formula,
        bool needsMinRadius)
    {
        double[] refZx = orbit.Zx;
        double[] refZy = orbit.Zy;
        int refLimit = orbit.Length - 1;
        int exponent = orbit.Exponent;
        int maxIter = formula.MaxIterations;
        bool julia = formula.Julia;

        ComplexExp dz = julia ? dc : ComplexExp.Zero;
        ComplexExp addC = julia ? ComplexExp.Zero : dc;

        int refIdx = 0;
        double minR = double.MaxValue;
        Span<double> powRe = stackalloc double[17];
        Span<double> powIm = stackalloc double[17];

        for (int i = 0; i < maxIter; i++)
        {
            double Zx = refZx[refIdx];
            double Zy = refZy[refIdx];

            ComplexExp fullZ = ComplexExp.AddDouble(dz, Zx, Zy);
            fullZ.ToDouble(out double zx, out double zy);
            double r2 = zx * zx + zy * zy;

            if (needsMinRadius && r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > Bailout2)
                return MultibrotFormula.Escaped(i, r2, exponent, needsMinRadius ? minR : 0);

            if (!julia)
            {
                FloatExp fullNorm2 = fullZ.NormSquared();
                FloatExp dzNorm2 = dz.NormSquared();
                if (fullNorm2 < dzNorm2 || refIdx >= refLimit)
                {
                    dz = fullZ;
                    refIdx = 0;
                    Zx = 0.0;
                    Zy = 0.0;
                }
            }
            else if (refIdx >= refLimit)
            {
                return ContinueDirect(zx, zy, formula.JuliaRe, formula.JuliaIm, exponent, i, maxIter, minR, needsMinRadius);
            }

            if (exponent == 2)
            {
                ComplexExp twoZPlusDz = ComplexExp.AddDouble(dz, 2.0 * Zx, 2.0 * Zy);
                dz = ComplexExp.Add(ComplexExp.Mul(dz, twoZPlusDz), addC);
            }
            else
            {
                dz = ComplexExp.Add(PerturbStepExtended(Zx, Zy, dz, exponent, powRe, powIm), addC);
            }

            refIdx++;
        }

        return needsMinRadius ? MultibrotFormula.Inside(minR) : new MultibrotFormula.Escape(-1f, 0f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void PerturbStepDouble(
        double Zx,
        double Zy,
        ref double dzx,
        ref double dzy,
        int n,
        Span<double> powRe,
        Span<double> powIm)
    {
        powRe[0] = 1.0;
        powIm[0] = 0.0;
        for (int k = 1; k < n; k++)
        {
            powRe[k] = powRe[k - 1] * Zx - powIm[k - 1] * Zy;
            powIm[k] = powRe[k - 1] * Zy + powIm[k - 1] * Zx;
        }

        // Horner in dz: sum_{j=1..n} C(n, j) * Z^(n-j) * dz^j
        double accX = 1.0;
        double accY = 0.0;
        for (int j = n - 1; j >= 1; j--)
        {
            double nx = accX * dzx - accY * dzy + Binom[n, j] * powRe[n - j];
            double ny = accX * dzy + accY * dzx + Binom[n, j] * powIm[n - j];
            accX = nx;
            accY = ny;
        }

        double outX = accX * dzx - accY * dzy;
        double outY = accX * dzy + accY * dzx;
        dzx = outX;
        dzy = outY;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ComplexExp PerturbStepExtended(
        double Zx,
        double Zy,
        ComplexExp dz,
        int n,
        Span<double> powRe,
        Span<double> powIm)
    {
        powRe[0] = 1.0;
        powIm[0] = 0.0;
        for (int k = 1; k < n; k++)
        {
            powRe[k] = powRe[k - 1] * Zx - powIm[k - 1] * Zy;
            powIm[k] = powRe[k - 1] * Zy + powIm[k - 1] * Zx;
        }

        ComplexExp acc = ComplexExp.FromDouble(1.0, 0.0);
        for (int j = n - 1; j >= 1; j--)
        {
            double b = Binom[n, j];
            acc = ComplexExp.AddDouble(ComplexExp.Mul(acc, dz), b * powRe[n - j], b * powIm[n - j]);
        }

        return ComplexExp.Mul(acc, dz);
    }

    static MultibrotFormula.Escape ContinueDirect(
        double zx,
        double zy,
        double cx,
        double cy,
        int exponent,
        int startIteration,
        int maxIter,
        double minR,
        bool needsMinRadius)
    {
        for (int i = startIteration; i < maxIter; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (needsMinRadius && r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > Bailout2)
                return MultibrotFormula.Escaped(i, r2, exponent, needsMinRadius ? minR : 0);

            double rx = 1.0, ry = 0.0;
            double bx = zx, by = zy;
            int e = exponent;
            while (e > 0)
            {
                if ((e & 1) != 0)
                {
                    double nx = rx * bx - ry * by;
                    double ny = rx * by + ry * bx;
                    rx = nx;
                    ry = ny;
                }

                e >>= 1;
                if (e == 0) break;
                double sx = bx * bx - by * by;
                double sy = 2.0 * bx * by;
                bx = sx;
                by = sy;
            }

            zx = rx + cx;
            zy = ry + cy;
        }

        return needsMinRadius ? MultibrotFormula.Inside(minR) : new MultibrotFormula.Escape(-1f, 0f);
    }

    public static MultibrotFormula.OrbitProbe ProbeDouble(
        double dcx,
        double dcy,
        ReferenceOrbit orbit,
        in FormulaParams formula)
    {
        double[] refZx = orbit.Zx;
        double[] refZy = orbit.Zy;
        int refLimit = orbit.Length - 1;
        int exponent = orbit.Exponent;
        int maxIter = formula.MaxIterations;
        bool julia = formula.Julia;

        double dzx = julia ? dcx : 0.0;
        double dzy = julia ? dcy : 0.0;
        double addCx = julia ? 0.0 : dcx;
        double addCy = julia ? 0.0 : dcy;

        int refIdx = 0;
        double minR = double.MaxValue;
        Span<double> powRe = stackalloc double[17];
        Span<double> powIm = stackalloc double[17];

        for (int i = 0; i < maxIter; i++)
        {
            double Zx = refZx[refIdx];
            double Zy = refZy[refIdx];
            double zx = Zx + dzx;
            double zy = Zy + dzy;
            double r2 = zx * zx + zy * zy;

            if (r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > Bailout2)
            {
                var esc = MultibrotFormula.Escaped(i, r2, exponent, minR);
                return new MultibrotFormula.OrbitProbe(false, i, esc.Mu, zx, zy, esc.MinRadius);
            }

            if (!julia)
            {
                double dz2 = dzx * dzx + dzy * dzy;
                if (r2 < dz2 || refIdx >= refLimit)
                {
                    dzx = zx;
                    dzy = zy;
                    refIdx = 0;
                    Zx = 0.0;
                    Zy = 0.0;
                }
            }
            else if (refIdx >= refLimit)
            {
                return ContinueDirectProbe(zx, zy, formula.JuliaRe, formula.JuliaIm, exponent, i, maxIter, minR);
            }

            if (exponent == 2)
            {
                double tx = 2.0 * Zx + dzx;
                double ty = 2.0 * Zy + dzy;
                double nx = dzx * tx - dzy * ty + addCx;
                double ny = dzx * ty + dzy * tx + addCy;
                dzx = nx;
                dzy = ny;
            }
            else
            {
                PerturbStepDouble(Zx, Zy, ref dzx, ref dzy, exponent, powRe, powIm);
                dzx += addCx;
                dzy += addCy;
            }

            refIdx++;
        }

        double finalZx = refIdx < orbit.Length ? refZx[refIdx] + dzx : dzx;
        double finalZy = refIdx < orbit.Length ? refZy[refIdx] + dzy : dzy;
        return new MultibrotFormula.OrbitProbe(true, maxIter, -1f, finalZx, finalZy, MultibrotFormula.Radius(minR));
    }

    public static MultibrotFormula.OrbitProbe ProbeExtended(
        ComplexExp dc,
        ReferenceOrbit orbit,
        in FormulaParams formula)
    {
        double[] refZx = orbit.Zx;
        double[] refZy = orbit.Zy;
        int refLimit = orbit.Length - 1;
        int exponent = orbit.Exponent;
        int maxIter = formula.MaxIterations;
        bool julia = formula.Julia;

        ComplexExp dz = julia ? dc : ComplexExp.Zero;
        ComplexExp addC = julia ? ComplexExp.Zero : dc;

        int refIdx = 0;
        double minR = double.MaxValue;
        Span<double> powRe = stackalloc double[17];
        Span<double> powIm = stackalloc double[17];

        for (int i = 0; i < maxIter; i++)
        {
            double Zx = refZx[refIdx];
            double Zy = refZy[refIdx];

            ComplexExp fullZ = ComplexExp.AddDouble(dz, Zx, Zy);
            fullZ.ToDouble(out double zx, out double zy);
            double r2 = zx * zx + zy * zy;

            if (r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > Bailout2)
            {
                var esc = MultibrotFormula.Escaped(i, r2, exponent, minR);
                return new MultibrotFormula.OrbitProbe(false, i, esc.Mu, zx, zy, esc.MinRadius);
            }

            if (!julia)
            {
                FloatExp fullNorm2 = fullZ.NormSquared();
                FloatExp dzNorm2 = dz.NormSquared();
                if (fullNorm2 < dzNorm2 || refIdx >= refLimit)
                {
                    dz = fullZ;
                    refIdx = 0;
                    Zx = 0.0;
                    Zy = 0.0;
                }
            }
            else if (refIdx >= refLimit)
            {
                return ContinueDirectProbe(zx, zy, formula.JuliaRe, formula.JuliaIm, exponent, i, maxIter, minR);
            }

            if (exponent == 2)
            {
                ComplexExp twoZPlusDz = ComplexExp.AddDouble(dz, 2.0 * Zx, 2.0 * Zy);
                dz = ComplexExp.Add(ComplexExp.Mul(dz, twoZPlusDz), addC);
            }
            else
            {
                dz = ComplexExp.Add(PerturbStepExtended(Zx, Zy, dz, exponent, powRe, powIm), addC);
            }

            refIdx++;
        }

        double refX = refIdx < orbit.Length ? refZx[refIdx] : 0.0;
        double refY = refIdx < orbit.Length ? refZy[refIdx] : 0.0;
        ComplexExp.AddDouble(dz, refX, refY).ToDouble(out double finalZx, out double finalZy);
        return new MultibrotFormula.OrbitProbe(true, maxIter, -1f, finalZx, finalZy, MultibrotFormula.Radius(minR));
    }

    static MultibrotFormula.OrbitProbe ContinueDirectProbe(
        double zx,
        double zy,
        double cx,
        double cy,
        int exponent,
        int startIteration,
        int maxIter,
        double minR)
    {
        for (int i = startIteration; i < maxIter; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (r2 < minR) minR = r2;
            if (!double.IsFinite(r2) || r2 > Bailout2)
            {
                var esc = MultibrotFormula.Escaped(i, r2, exponent, minR);
                return new MultibrotFormula.OrbitProbe(false, i, esc.Mu, zx, zy, esc.MinRadius);
            }

            double rx = 1.0, ry = 0.0;
            double bx = zx, by = zy;
            int e = exponent;
            while (e > 0)
            {
                if ((e & 1) != 0)
                {
                    double nx = rx * bx - ry * by;
                    double ny = rx * by + ry * bx;
                    rx = nx;
                    ry = ny;
                }

                e >>= 1;
                if (e == 0) break;
                double sx = bx * bx - by * by;
                double sy = 2.0 * bx * by;
                bx = sx;
                by = sy;
            }

            zx = rx + cx;
            zy = ry + cy;
        }

        return new MultibrotFormula.OrbitProbe(true, maxIter, -1f, zx, zy, MultibrotFormula.Radius(minR));
    }

    public static void EvaluateShadedRow(
        int width,
        int height,
        int y,
        FloatExp refOffsetX,
        FloatExp refOffsetY,
        FloatExp halfW,
        FloatExp halfH,
        ReferenceOrbit orbit,
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
        int row = y * width;
        double v = ((y + 0.5) / height - 0.5) * 2.0;
        FloatExp dcy = refOffsetY - halfH * v;
        bool useExtended = halfH <= DoubleUnderflowThreshold;

        if (!useExtended)
        {
            double dcyD = dcy.ToDouble();
            double refOffXD = refOffsetX.ToDouble();
            double halfWD = halfW.ToDouble();

            for (int x = 0; x < width; x++)
            {
                double u = ((x + 0.5) / width - 0.5) * 2.0;
                double dcxD = refOffXD + halfWD * u;
                int idx = row + x;
                EvaluateShadedDouble(dcxD, dcyD, orbit, formula, needsMinRadius, computeNormals, orbitTrapMode, out mu[idx], out minRadius[idx], out trapDist[idx], out normalX[idx], out normalY[idx]);
            }
        }
        else
        {
            for (int x = 0; x < width; x++)
            {
                double u = ((x + 0.5) / width - 0.5) * 2.0;
                FloatExp dcx = refOffsetX + halfW * u;
                int idx = row + x;
                EvaluateShadedExtended(ComplexExp.FromFloatExp(dcx, dcy), orbit, formula, needsMinRadius, computeNormals, orbitTrapMode, out mu[idx], out minRadius[idx], out trapDist[idx], out normalX[idx], out normalY[idx]);
            }
        }
    }

    static void EvaluateShadedDouble(
        double dcx,
        double dcy,
        ReferenceOrbit orbit,
        in FormulaParams formula,
        bool needsMinRadius,
        bool computeNormals,
        int orbitTrapMode,
        out float outMu,
        out float outMinRadius,
        out float outTrapDist,
        out float outNx,
        out float outNy)
    {
        double[] refZx = orbit.Zx;
        double[] refZy = orbit.Zy;
        int refLimit = orbit.Length - 1;
        int exponent = orbit.Exponent;
        int maxIter = formula.MaxIterations;
        bool julia = formula.Julia;
        bool trackTrap = orbitTrapMode != 0;
        bool addOne = !julia;

        double dzx = julia ? dcx : 0.0;
        double dzy = julia ? dcy : 0.0;
        double addCx = julia ? 0.0 : dcx;
        double addCy = julia ? 0.0 : dcy;

        double derX = julia ? 1.0 : 0.0;
        double derY = 0.0;
        bool dScaled = false;
        int refIdx = 0;
        double minR = double.MaxValue;
        double minTrap = double.MaxValue;
        Span<double> powRe = stackalloc double[17];
        Span<double> powIm = stackalloc double[17];

        outNx = 0f;
        outNy = 0f;

        for (int i = 0; i < maxIter; i++)
        {
            double Zx = refZx[refIdx];
            double Zy = refZy[refIdx];
            double zx = Zx + dzx;
            double zy = Zy + dzy;
            double r2 = zx * zx + zy * zy;

            if (needsMinRadius && r2 < minR) minR = r2;
            if (trackTrap && i > 0)
            {
                double td = MultibrotFormula.TrapDistance(zx, zy, orbitTrapMode);
                if (td < minTrap) minTrap = td;
            }

            if (!double.IsFinite(r2) || r2 > Bailout2)
            {
                var esc = MultibrotFormula.Escaped(i, r2, exponent, needsMinRadius ? minR : 0);
                outMu = esc.Mu;
                outMinRadius = esc.MinRadius;
                outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : MultibrotFormula.TrapDistance(zx, zy, orbitTrapMode)) : 0f;

                if (computeNormals)
                {
                    if (r2 < 1e12)
                    {
                        MultibrotFormula.StepDerivativeInteger(zx, zy, exponent, addOne, ref derX, ref derY, ref dScaled, out double pzx, out double pzy);
                        zx = pzx;
                        zy = pzy;
                    }

                    MultibrotFormula.ComputeUnitNormal(zx, zy, derX, derY, out outNx, out outNy);
                }

                return;
            }

            if (!julia)
            {
                double dz2 = dzx * dzx + dzy * dzy;
                if (r2 < dz2 || refIdx >= refLimit)
                {
                    dzx = zx;
                    dzy = zy;
                    refIdx = 0;
                    Zx = 0.0;
                    Zy = 0.0;
                }
            }
            else if (refIdx >= refLimit)
            {
                ContinueDirectShaded(zx, zy, formula.JuliaRe, formula.JuliaIm, exponent, i, maxIter, minR, minTrap, derX, derY, dScaled, needsMinRadius, computeNormals, orbitTrapMode, out outMu, out outMinRadius, out outTrapDist, out outNx, out outNy);
                return;
            }

            if (computeNormals)
                MultibrotFormula.StepDerivativeInteger(zx, zy, exponent, addOne, ref derX, ref derY, ref dScaled, out _, out _);

            if (exponent == 2)
            {
                double tx = 2.0 * Zx + dzx;
                double ty = 2.0 * Zy + dzy;
                double nx = dzx * tx - dzy * ty + addCx;
                double ny = dzx * ty + dzy * tx + addCy;
                dzx = nx;
                dzy = ny;
            }
            else
            {
                PerturbStepDouble(Zx, Zy, ref dzx, ref dzy, exponent, powRe, powIm);
                dzx += addCx;
                dzy += addCy;
            }

            refIdx++;
        }

        outMu = -1f;
        outMinRadius = needsMinRadius ? MultibrotFormula.Radius(minR) : 0f;
        outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : 0.0) : 0f;
    }

    static void EvaluateShadedExtended(
        ComplexExp dc,
        ReferenceOrbit orbit,
        in FormulaParams formula,
        bool needsMinRadius,
        bool computeNormals,
        int orbitTrapMode,
        out float outMu,
        out float outMinRadius,
        out float outTrapDist,
        out float outNx,
        out float outNy)
    {
        double[] refZx = orbit.Zx;
        double[] refZy = orbit.Zy;
        int refLimit = orbit.Length - 1;
        int exponent = orbit.Exponent;
        int maxIter = formula.MaxIterations;
        bool julia = formula.Julia;
        bool trackTrap = orbitTrapMode != 0;
        bool addOne = !julia;

        ComplexExp dz = julia ? dc : ComplexExp.Zero;
        ComplexExp addC = julia ? ComplexExp.Zero : dc;

        double derX = julia ? 1.0 : 0.0;
        double derY = 0.0;
        bool dScaled = false;
        int refIdx = 0;
        double minR = double.MaxValue;
        double minTrap = double.MaxValue;
        Span<double> powRe = stackalloc double[17];
        Span<double> powIm = stackalloc double[17];

        outNx = 0f;
        outNy = 0f;

        for (int i = 0; i < maxIter; i++)
        {
            double Zx = refZx[refIdx];
            double Zy = refZy[refIdx];

            ComplexExp fullZ = ComplexExp.AddDouble(dz, Zx, Zy);
            fullZ.ToDouble(out double zx, out double zy);
            double r2 = zx * zx + zy * zy;

            if (needsMinRadius && r2 < minR) minR = r2;
            if (trackTrap && i > 0)
            {
                double td = MultibrotFormula.TrapDistance(zx, zy, orbitTrapMode);
                if (td < minTrap) minTrap = td;
            }

            if (!double.IsFinite(r2) || r2 > Bailout2)
            {
                var esc = MultibrotFormula.Escaped(i, r2, exponent, needsMinRadius ? minR : 0);
                outMu = esc.Mu;
                outMinRadius = esc.MinRadius;
                outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : MultibrotFormula.TrapDistance(zx, zy, orbitTrapMode)) : 0f;

                if (computeNormals)
                {
                    if (r2 < 1e12)
                    {
                        MultibrotFormula.StepDerivativeInteger(zx, zy, exponent, addOne, ref derX, ref derY, ref dScaled, out double pzx, out double pzy);
                        zx = pzx;
                        zy = pzy;
                    }

                    MultibrotFormula.ComputeUnitNormal(zx, zy, derX, derY, out outNx, out outNy);
                }

                return;
            }

            if (!julia)
            {
                FloatExp fullNorm2 = fullZ.NormSquared();
                FloatExp dzNorm2 = dz.NormSquared();
                if (fullNorm2 < dzNorm2 || refIdx >= refLimit)
                {
                    dz = fullZ;
                    refIdx = 0;
                    Zx = 0.0;
                    Zy = 0.0;
                }
            }
            else if (refIdx >= refLimit)
            {
                ContinueDirectShaded(zx, zy, formula.JuliaRe, formula.JuliaIm, exponent, i, maxIter, minR, minTrap, derX, derY, dScaled, needsMinRadius, computeNormals, orbitTrapMode, out outMu, out outMinRadius, out outTrapDist, out outNx, out outNy);
                return;
            }

            if (computeNormals)
                MultibrotFormula.StepDerivativeInteger(zx, zy, exponent, addOne, ref derX, ref derY, ref dScaled, out _, out _);

            if (exponent == 2)
            {
                ComplexExp twoZPlusDz = ComplexExp.AddDouble(dz, 2.0 * Zx, 2.0 * Zy);
                dz = ComplexExp.Add(ComplexExp.Mul(dz, twoZPlusDz), addC);
            }
            else
            {
                dz = ComplexExp.Add(PerturbStepExtended(Zx, Zy, dz, exponent, powRe, powIm), addC);
            }

            refIdx++;
        }

        outMu = -1f;
        outMinRadius = needsMinRadius ? MultibrotFormula.Radius(minR) : 0f;
        outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : 0.0) : 0f;
    }

    static void ContinueDirectShaded(
        double zx,
        double zy,
        double cx,
        double cy,
        int exponent,
        int startIteration,
        int maxIter,
        double minR,
        double minTrap,
        double derX,
        double derY,
        bool dScaled,
        bool needsMinRadius,
        bool computeNormals,
        int orbitTrapMode,
        out float outMu,
        out float outMinRadius,
        out float outTrapDist,
        out float outNx,
        out float outNy)
    {
        bool trackTrap = orbitTrapMode != 0;
        outNx = 0f;
        outNy = 0f;

        for (int i = startIteration; i < maxIter; i++)
        {
            double r2 = zx * zx + zy * zy;
            if (needsMinRadius && r2 < minR) minR = r2;
            if (trackTrap && i > 0)
            {
                double td = MultibrotFormula.TrapDistance(zx, zy, orbitTrapMode);
                if (td < minTrap) minTrap = td;
            }

            if (!double.IsFinite(r2) || r2 > Bailout2)
            {
                var esc = MultibrotFormula.Escaped(i, r2, exponent, needsMinRadius ? minR : 0);
                outMu = esc.Mu;
                outMinRadius = esc.MinRadius;
                outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : MultibrotFormula.TrapDistance(zx, zy, orbitTrapMode)) : 0f;
                if (computeNormals)
                {
                    if (r2 < 1e12)
                    {
                        MultibrotFormula.StepDerivativeInteger(zx, zy, exponent, false, ref derX, ref derY, ref dScaled, out double pzx, out double pzy);
                        zx = pzx + cx;
                        zy = pzy + cy;
                    }

                    MultibrotFormula.ComputeUnitNormal(zx, zy, derX, derY, out outNx, out outNy);
                }

                return;
            }

            MultibrotFormula.StepDerivativeInteger(zx, zy, exponent, false, ref derX, ref derY, ref dScaled, out double nextX, out double nextY);
            zx = nextX + cx;
            zy = nextY + cy;
        }

        outMu = -1f;
        outMinRadius = needsMinRadius ? MultibrotFormula.Radius(minR) : 0f;
        outTrapDist = trackTrap ? (float)(minTrap < 1e15 ? minTrap : 0.0) : 0f;
    }
}
