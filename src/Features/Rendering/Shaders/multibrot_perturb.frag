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

vec3 colorOf(bool escaped, float mu, float minR)
{
    if (!escaped) return interiorColor(minR);

    float value = smoothColor == 1 ? mu : floor(mu);
    float t = value * 0.035 * max(density, 0.05) + phase;
    vec3 color = samplePalette(paletteId, t);
    float shade = clamp(tanh(mu / 9.0), 0.0, 1.0);
    return color * shade;
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
            float r2 = dot(fullZ, fullZ);
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
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
                    float zr2 = dot(z, z);
                    if (escapedAt(zr2, j, minR, mu))
                    {
                        escaped = true;
                        break;
                    }

                    z = cpow(z, exponent) + juliaC;
                }
                break;
            }

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
            float r2 = dot(fullZ, fullZ);
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
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
                    float zr2 = dot(z, z);
                    if (escapedAt(zr2, j, minR, mu))
                    {
                        escaped = true;
                        break;
                    }

                    z = cpow(z, exponent) + juliaC;
                }
                break;
            }

            dz = cxpAdd(perturbStepCxp(Z, dz, exponent), addC);
            refIdx++;
        }
    }

    finalColor = vec4(colorOf(escaped, mu, minR), 1.0);
}
