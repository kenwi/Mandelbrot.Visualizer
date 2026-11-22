#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform vec2 resolution;
uniform float scaleMant;
uniform int scaleExp;
uniform vec2 refOffsetMant;
uniform int refOffsetExp;
uniform vec2 juliaC;
uniform int exponent;
uniform int maxIter;
uniform int refLength;
uniform int paletteId;
uniform int smoothColor;
uniform int interiorStyle;
uniform int normalLighting;
uniform int cursorLight;
uniform vec2 lightPos;
uniform float lightAngle;
uniform int orbitTrap;
uniform float density;
uniform float phase;
uniform int juliaSet;
uniform sampler2D paletteLut;
uniform sampler2D refOrbit;

out vec4 finalColor;

// Shared-exponent complex number (m * 2^e) implemented using GLSL 330 bit operations.
struct cxp
{
    vec2 m;
    int e;
};

int ilogb_pos(float v)
{
    int bits = floatBitsToInt(v);
    return ((bits >> 23) & 0xFF) - 127;
}

float pow2f(int e)
{
    if (e < -126) return 0.0;
    if (e > 127) return 1e38;
    return intBitsToFloat((e + 127) << 23);
}

cxp cxpNorm(vec2 m, int e)
{
    float mx = max(abs(m.x), abs(m.y));
    if (mx == 0.0) return cxp(vec2(0.0), 0);
    int k = ilogb_pos(mx) + 1;
    return cxp(m * pow2f(-k), e + k);
}

cxp cxpAdd(cxp a, cxp b)
{
    if (a.m == vec2(0.0)) return b;
    if (b.m == vec2(0.0)) return a;
    if (a.e < b.e)
    {
        cxp t = a;
        a = b;
        b = t;
    }

    int diff = a.e - b.e;
    if (diff > 26) return a;
    return cxpNorm(a.m + b.m * pow2f(-diff), a.e);
}

cxp cxpAddVec2(cxp a, vec2 v)
{
    return cxpAdd(a, cxpNorm(v, 0));
}

cxp cxpMul(cxp a, cxp b)
{
    if (a.m == vec2(0.0) || b.m == vec2(0.0)) return cxp(vec2(0.0), 0);
    vec2 prod = vec2(a.m.x * b.m.x - a.m.y * b.m.y, a.m.x * b.m.y + a.m.y * b.m.x);
    return cxpNorm(prod, a.e + b.e);
}

vec2 cxpToVec2(cxp a)
{
    if (a.m == vec2(0.0) || a.e < -126) return vec2(0.0);
    if (a.e > 120) return sign(a.m) * 1e35;
    return a.m * pow2f(a.e);
}

bool cxpNorm2Less(cxp a, cxp b)
{
    if (a.m == vec2(0.0)) return b.m != vec2(0.0);
    if (b.m == vec2(0.0)) return false;
    int ediff = a.e - b.e;
    if (ediff < -2) return true;
    if (ediff > 2) return false;
    return dot(a.m, a.m) * pow2f(2 * ediff) < dot(b.m, b.m);
}

vec2 fetchRef(int idx)
{
    return texelFetch(refOrbit, ivec2(idx & 1023, idx >> 10), 0).xy;
}

vec2 cmul(vec2 a, vec2 b)
{
    return vec2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

vec2 cpow(vec2 z, int n)
{
    vec2 r = vec2(1.0, 0.0);
    vec2 b = z;
    int e = n;
    for (int k = 0; k < 5; k++)
    {
        if ((e & 1) == 1) r = cmul(r, b);
        e >>= 1;
        if (e == 0) break;
        b = cmul(b, b);
    }

    return r;
}

vec2 perturbStepF32(vec2 Z, vec2 dz, int n)
{
    if (n == 2)
        return cmul(dz, 2.0 * Z + dz);

    vec2 zPow[17];
    zPow[0] = vec2(1.0, 0.0);
    for (int k = 1; k < n; k++)
        zPow[k] = cmul(zPow[k - 1], Z);

    vec2 acc = vec2(1.0, 0.0);
    float binom = 1.0;
    for (int j = n - 1; j >= 1; j--)
    {
        binom = binom * float(j + 1) / float(n - j);
        acc = cmul(acc, dz) + binom * zPow[n - j];
    }

    return cmul(acc, dz);
}

cxp perturbStepCxp(vec2 Z, cxp dz, int n)
{
    if (n == 2)
        return cxpMul(dz, cxpAddVec2(dz, 2.0 * Z));

    vec2 zPow[17];
    zPow[0] = vec2(1.0, 0.0);
    for (int k = 1; k < n; k++)
        zPow[k] = cmul(zPow[k - 1], Z);

    cxp acc = cxpNorm(vec2(1.0, 0.0), 0);
    float binom = 1.0;
    for (int j = n - 1; j >= 1; j--)
    {
        binom = binom * float(j + 1) / float(n - j);
        acc = cxpAddVec2(cxpMul(acc, dz), binom * zPow[n - j]);
    }

    return cxpMul(acc, dz);
}

bool escapedAt(float r2, int i, inout float minR, inout float mu)
{
    if (!(r2 < 1e30))
    {
        mu = float(i);
        return true;
    }

    if (r2 < minR) minR = r2;
    if (r2 <= 256.0) return false;

    float logZn = 0.5 * log(r2);
    float logPower = log(float(exponent));
    float nu = logPower > 1e-6 ? log(logZn / log(16.0)) / logPower : 0.0;
    mu = float(i) + 1.0 - nu;
    if (!(mu >= 0.0)) mu = float(i);
    return true;
}

vec3 samplePalette(int id, float t)
{
    float u = fract(t) * 255.0;
    int i0 = int(floor(u));
    int i1 = min(i0 + 1, 255);
    float frac = u - float(i0);
    int row = clamp(id, 0, 7);
    vec3 a = texelFetch(paletteLut, ivec2(i0, row), 0).rgb;
    vec3 b = texelFetch(paletteLut, ivec2(i1, row), 0).rgb;
    return mix(a, b, frac);
}

vec3 interiorColor(float minR)
{
    if (interiorStyle == 0) return vec3(4.0, 6.0, 12.0) / 255.0;
    float radius = sqrt(max(minR, 0.0));
    float g = clamp(radius / 0.85, 0.0, 1.0);
    return vec3(8.0 + 40.0 * g, 12.0 + 28.0 * g, 32.0 + 90.0 * g) / 255.0;
}

float trapDistance(vec2 z)
{
    if (orbitTrap == 1) return length(z);
    if (orbitTrap == 2) return min(abs(z.x), abs(z.y)) + max(0.0, length(z) - 2.0);
    if (orbitTrap == 3) return abs(length(z) - 1.0);
    if (orbitTrap == 4) return abs(max(abs(z.x), abs(z.y)) - 1.0);
    return 0.0;
}

vec3 sampleOrbitTrap(float dist)
{
    float d = max(dist, 0.0);
    float t = log(1.0 + d * 6.0) * 0.55 * max(density, 0.05) + phase;
    vec3 baseCol = samplePalette(paletteId, t);
    float filament = exp(-d * 3.2);
    float falloff = 1.0 / (1.0 + d * 1.4);
    float shade = 0.35 + 0.65 * falloff;
    return mix(baseCol * shade, baseCol * 1.15 + vec3(0.18), filament * 0.65);
}

vec3 applyNormalLighting(vec3 col, vec2 u, vec2 pixPos)
{
    vec3 N = normalize(vec3(u.x, u.y, 1.0 / 1.75));
    vec3 L;
    float atten = 1.0;
    if (cursorLight == 1)
    {
        vec3 toLight = vec3(lightPos - pixPos, 0.52);
        float dist = length(toLight);
        L = toLight / max(dist, 1e-5);
        atten = 1.0 / (1.0 + 0.55 * dist * dist);
    }
    else
    {
        L = vec3(cos(lightAngle) * 0.7342162, sin(lightAngle) * 0.7342162, 0.6789146);
    }

    vec3 H = normalize(L + vec3(0.0, 0.0, 1.0));
    float diff = max(dot(N, L), 0.0);
    float spec = pow(max(dot(N, H), 0.0), 24.0);
    if (cursorLight == 1)
        return clamp(col * (0.22 + 0.98 * diff * atten) + vec3(0.42 * spec * atten), 0.0, 1.0);
    return clamp(col * (0.24 + 0.92 * diff) + vec3(0.38 * spec), 0.0, 1.0);
}

vec2 computeUnitNormal(vec2 z, vec2 d)
{
    vec2 u = vec2(z.x * d.x + z.y * d.y, z.y * d.x - z.x * d.y);
    float len = length(u);
    return len > 1e-18 ? u / len : vec2(0.0);
}

void renormDeriv(inout vec2 d, inout bool dScaled)
{
    float d2 = dot(d, d);
    if (d2 > 1e20 || (dScaled && d2 > 0.0 && d2 < 1e-20))
    {
        d *= inversesqrt(d2);
        dScaled = true;
    }
}

void stepDeriv(vec2 z, int n, bool addOne, inout vec2 d, inout bool dScaled)
{
    if (n == 2)
    {
        d = 2.0 * cmul(z, d);
    }
    else
    {
        vec2 pm = cpow(z, n - 1);
        d = float(n) * cmul(pm, d);
    }

    if (addOne && !dScaled) d.x += 1.0;
    renormDeriv(d, dScaled);
}

vec3 colorOf(bool escaped, float mu, float minR, float trapDist, vec2 normal2D, vec2 pixPos)
{
    if (!escaped)
    {
        if (orbitTrap != 0 && interiorStyle == 1)
            return sampleOrbitTrap(trapDist) * 0.85;
        return interiorColor(minR);
    }

    vec3 color;
    if (orbitTrap != 0)
    {
        color = sampleOrbitTrap(trapDist);
    }
    else
    {
        float value = smoothColor == 1 ? mu : floor(mu);
        float t = value * 0.035 * max(density, 0.05) + phase;
        color = samplePalette(paletteId, t);
        float shade = clamp(tanh(mu / 9.0), 0.0, 1.0);
        color *= shade;
    }

    if (normalLighting == 1)
        color = applyNormalLighting(color, normal2D, pixPos);
    return color;
}

void main()
{
    float px = gl_FragCoord.x;
    float py = resolution.y - gl_FragCoord.y;
    float halfWMant = scaleMant * resolution.x / max(resolution.y, 1.0);
    vec2 pixFromCenter = vec2(
        (px / resolution.x - 0.5) * 2.0 * halfWMant,
        -((py / resolution.y - 0.5) * 2.0 * scaleMant));

    cxp dcExp = cxpAdd(cxpNorm(refOffsetMant, refOffsetExp), cxpNorm(pixFromCenter, scaleExp));

    int steps = max(maxIter, 1);
    int refLimit = max(refLength - 1, 0);
    bool escaped = false;
    float mu = -1.0;
    float minR = 1e20;
    float minTrap = 1e20;
    vec2 d = juliaSet == 1 ? vec2(1.0, 0.0) : vec2(0.0, 0.0);
    bool dScaled = false;
    bool addOne = juliaSet == 0;
    vec2 normal2D = vec2(0.0);
    vec2 lastZ = vec2(0.0);

    if (scaleExp > -105)
    {
        // Fast native float perturbation path down to ~1e-32.
        vec2 dc = cxpToVec2(dcExp);
        vec2 dz = juliaSet == 1 ? dc : vec2(0.0);
        vec2 addC = juliaSet == 1 ? vec2(0.0) : dc;
        int refIdx = 0;

        for (int i = 0; i < steps; i++)
        {
            vec2 Z = fetchRef(refIdx);
            vec2 fullZ = Z + dz;
            lastZ = fullZ;
            float r2 = dot(fullZ, fullZ);
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(fullZ));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    vec2 fz = fullZ;
                    if (r2 < 1e12)
                    {
                        stepDeriv(fz, exponent, addOne, d, dScaled);
                        fz = cpow(fz, exponent);
                    }
                    normal2D = computeUnitNormal(fz, d);
                }
                break;
            }

            if (juliaSet == 0)
            {
                if (r2 < dot(dz, dz) || refIdx >= refLimit)
                {
                    dz = fullZ;
                    refIdx = 0;
                    Z = vec2(0.0);
                }
            }
            else if (refIdx >= refLimit)
            {
                vec2 z = fullZ;
                for (int j = i; j < steps; j++)
                {
                    lastZ = z;
                    float zr2 = dot(z, z);
                    if (orbitTrap != 0 && j > 0) minTrap = min(minTrap, trapDistance(z));
                    if (escapedAt(zr2, j, minR, mu))
                    {
                        escaped = true;
                        if (normalLighting == 1)
                        {
                            vec2 fz = z;
                            if (zr2 < 1e12)
                            {
                                stepDeriv(fz, exponent, false, d, dScaled);
                                fz = cpow(fz, exponent) + juliaC;
                            }
                            normal2D = computeUnitNormal(fz, d);
                        }
                        break;
                    }

                    if (normalLighting == 1)
                        stepDeriv(z, exponent, false, d, dScaled);
                    z = cpow(z, exponent) + juliaC;
                }
                break;
            }

            if (normalLighting == 1)
                stepDeriv(fullZ, exponent, addOne, d, dScaled);
            dz = perturbStepF32(Z, dz, exponent) + addC;
            refIdx++;
        }
    }
    else
    {
        // Extended-exponent ComplexExp perturbation path below 1e-32 (no underflow).
        cxp dz = juliaSet == 1 ? dcExp : cxp(vec2(0.0), 0);
        cxp addC = juliaSet == 1 ? cxp(vec2(0.0), 0) : dcExp;
        int refIdx = 0;

        for (int i = 0; i < steps; i++)
        {
            vec2 Z = fetchRef(refIdx);
            cxp fullZExp = cxpAddVec2(dz, Z);
            vec2 fullZ = cxpToVec2(fullZExp);
            lastZ = fullZ;
            float r2 = dot(fullZ, fullZ);
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(fullZ));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    vec2 fz = fullZ;
                    if (r2 < 1e12)
                    {
                        stepDeriv(fz, exponent, addOne, d, dScaled);
                        fz = cpow(fz, exponent);
                    }
                    normal2D = computeUnitNormal(fz, d);
                }
                break;
            }

            if (juliaSet == 0)
            {
                if (cxpNorm2Less(fullZExp, dz) || refIdx >= refLimit)
                {
                    dz = fullZExp;
                    refIdx = 0;
                    Z = vec2(0.0);
                }
            }
            else if (refIdx >= refLimit)
            {
                vec2 z = fullZ;
                for (int j = i; j < steps; j++)
                {
                    lastZ = z;
                    float zr2 = dot(z, z);
                    if (orbitTrap != 0 && j > 0) minTrap = min(minTrap, trapDistance(z));
                    if (escapedAt(zr2, j, minR, mu))
                    {
                        escaped = true;
                        if (normalLighting == 1)
                        {
                            vec2 fz = z;
                            if (zr2 < 1e12)
                            {
                                stepDeriv(fz, exponent, false, d, dScaled);
                                fz = cpow(fz, exponent) + juliaC;
                            }
                            normal2D = computeUnitNormal(fz, d);
                        }
                        break;
                    }

                    if (normalLighting == 1)
                        stepDeriv(z, exponent, false, d, dScaled);
                    z = cpow(z, exponent) + juliaC;
                }
                break;
            }

            if (normalLighting == 1)
                stepDeriv(fullZ, exponent, addOne, d, dScaled);
            dz = cxpAdd(perturbStepCxp(Z, dz, exponent), addC);
            refIdx++;
        }
    }

    float aspect = resolution.x / max(resolution.y, 1.0);
    vec2 pixPos = vec2((px / resolution.x - 0.5) * aspect, -(py / resolution.y - 0.5));
    float finalTrap = orbitTrap != 0 ? (minTrap < 1e15 ? minTrap : trapDistance(lastZ)) : 0.0;
    finalColor = vec4(colorOf(escaped, mu, minR, finalTrap, normal2D, pixPos), 1.0);
}
