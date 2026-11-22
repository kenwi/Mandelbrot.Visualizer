#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform vec2 resolution;
uniform vec4 center;
uniform vec4 juliaC;
uniform float scale;
uniform float power;
uniform int exponent;
uniform int maxIter;
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

out vec4 finalColor;

vec2 dfAdd(vec2 a, vec2 b)
{
    float s = a.x + b.x;
    float v = s - a.x;
    float err = (a.x - (s - v)) + (b.x - v);
    float t = err + a.y + b.y;
    float z = s + t;
    return vec2(z, t - (z - s));
}

vec2 dfSplit(float a)
{
    float c = 4097.0 * a;
    float hi = c - (c - a);
    return vec2(hi, a - hi);
}

vec2 dfMul(vec2 a, vec2 b)
{
    float p = a.x * b.x;
    vec2 as = dfSplit(a.x);
    vec2 bs = dfSplit(b.x);
    float e = ((as.x * bs.x - p) + as.x * bs.y + as.y * bs.x) + as.y * bs.y;
    float t = e + (a.x * b.y + a.y * b.x);
    float z = p + t;
    return vec2(z, t - (z - p));
}

vec2 dfSub(vec2 a, vec2 b)
{
    return dfAdd(a, vec2(-b.x, -b.y));
}

void dfSquare(vec2 zx, vec2 zy, out vec2 ox, out vec2 oy)
{
    vec2 x2 = dfMul(zx, zx);
    vec2 y2 = dfMul(zy, zy);
    ox = dfSub(x2, y2);
    oy = dfMul(dfMul(vec2(2.0, 0.0), zx), zy);
}

void dfMulComplex(vec2 ax, vec2 ay, vec2 bx, vec2 by, out vec2 ox, out vec2 oy)
{
    ox = dfSub(dfMul(ax, bx), dfMul(ay, by));
    oy = dfAdd(dfMul(ax, by), dfMul(ay, bx));
}

void dfPow(vec2 zx, vec2 zy, int n, out vec2 ox, out vec2 oy)
{
    int e = n < 0 ? -n : n;
    if (e == 0)
    {
        ox = vec2(1.0, 0.0);
        oy = vec2(0.0, 0.0);
        return;
    }

    if (e == 2)
    {
        dfSquare(zx, zy, ox, oy);
    }
    else if (e == 3)
    {
        vec2 sx, sy;
        dfSquare(zx, zy, sx, sy);
        dfMulComplex(sx, sy, zx, zy, ox, oy);
    }
    else if (e == 4)
    {
        vec2 sx, sy;
        dfSquare(zx, zy, sx, sy);
        dfSquare(sx, sy, ox, oy);
    }
    else
    {
        vec2 rx = vec2(1.0, 0.0);
        vec2 ry = vec2(0.0, 0.0);
        vec2 bx = zx;
        vec2 by = zy;
        for (int k = 0; k < 6; k++)
        {
            if ((e & 1) == 1)
            {
                vec2 nx, ny;
                dfMulComplex(rx, ry, bx, by, nx, ny);
                rx = nx;
                ry = ny;
            }

            e >>= 1;
            if (e == 0) break;
            vec2 nx, ny;
            dfSquare(bx, by, nx, ny);
            bx = nx;
            by = ny;
        }

        ox = rx;
        oy = ry;
    }

    if (n >= 0) return;

    float mag = ox.x * ox.x + oy.x * oy.x;
    if (mag == 0.0)
    {
        ox = vec2(0.0, 0.0);
        oy = vec2(0.0, 0.0);
        return;
    }

    ox = vec2(ox.x / mag, 0.0);
    oy = vec2(-oy.x / mag, 0.0);
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
    float logPower = log(abs(power));
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

float trapDistance(float zx, float zy)
{
    if (orbitTrap == 1) return length(vec2(zx, zy));
    if (orbitTrap == 2) return min(abs(zx), abs(zy)) + max(0.0, length(vec2(zx, zy)) - 2.0);
    if (orbitTrap == 3) return abs(length(vec2(zx, zy)) - 1.0);
    if (orbitTrap == 4) return abs(max(abs(zx), abs(zy)) - 1.0);
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

vec2 computeUnitNormal(float zx, float zy, vec2 d)
{
    vec2 u = vec2(zx * d.x + zy * d.y, zy * d.x - zx * d.y);
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

void complexPowF32(float zx, float zy, int n, out float ox, out float oy)
{
    int e = n < 0 ? -n : n;
    if (e == 0)
    {
        ox = 1.0;
        oy = 0.0;
        return;
    }

    float rx = 1.0;
    float ry = 0.0;
    float bx = zx;
    float by = zy;
    for (int k = 0; k < 6; k++)
    {
        if ((e & 1) == 1)
        {
            float nx = rx * bx - ry * by;
            float ny = rx * by + ry * bx;
            rx = nx;
            ry = ny;
        }

        e >>= 1;
        if (e == 0) break;
        float sx = bx * bx - by * by;
        float sy = 2.0 * bx * by;
        bx = sx;
        by = sy;
    }

    ox = rx;
    oy = ry;
    if (n >= 0) return;

    float mag = ox * ox + oy * oy;
    if (mag == 0.0)
    {
        ox = 0.0;
        oy = 0.0;
        return;
    }

    ox = ox / mag;
    oy = -oy / mag;
}

void stepDerivInteger(float zx, float zy, int n, bool addOne, inout vec2 d, inout bool dScaled)
{
    if (n == 2)
    {
        d = vec2(2.0 * (zx * d.x - zy * d.y), 2.0 * (zx * d.y + zy * d.x));
        if (addOne && !dScaled) d.x += 1.0;
        renormDeriv(d, dScaled);
        return;
    }

    float pmx, pmy;
    complexPowF32(zx, zy, n - 1, pmx, pmy);
    d = float(n) * vec2(pmx * d.x - pmy * d.y, pmx * d.y + pmy * d.x);
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
    float halfH = scale;
    float halfW = scale * resolution.x / max(resolution.y, 1.0);
    vec2 pixelRe = dfAdd(vec2(center.x, center.y), vec2((px / resolution.x - 0.5) * 2.0 * halfW, 0.0));
    vec2 pixelIm = dfAdd(vec2(center.z, center.w), vec2(-((py / resolution.y - 0.5) * 2.0 * halfH), 0.0));

    vec2 zx;
    vec2 zy;
    vec2 cx;
    vec2 cy;
    if (juliaSet == 1)
    {
        zx = pixelRe;
        zy = pixelIm;
        cx = vec2(juliaC.x, juliaC.y);
        cy = vec2(juliaC.z, juliaC.w);
    }
    else
    {
        zx = vec2(0.0, 0.0);
        zy = vec2(0.0, 0.0);
        cx = pixelRe;
        cy = pixelIm;
    }

    int steps = max(maxIter, 1);

    bool escaped = false;
    float mu = -1.0;
    float minR = 1e20;
    float minTrap = 1e20;
    vec2 d = juliaSet == 1 ? vec2(1.0, 0.0) : vec2(0.0, 0.0);
    bool dScaled = false;
    bool addOne = juliaSet == 0;
    vec2 normal2D = vec2(0.0);

    if (abs(power) < 1e-4)
    {
        finalColor = vec4(interiorColor(0.0), 1.0);
        return;
    }

    if (interiorStyle == 0 && juliaSet == 0 && exponent == 2)
    {
        float xm = cx.x - 0.25;
        float cy2 = cy.x * cy.x;
        float q = xm * xm + cy2;
        if (q * (q + xm) <= 0.25 * cy2)
        {
            finalColor = vec4(interiorColor(0.0), 1.0);
            return;
        }

        float xp = cx.x + 1.0;
        if (xp * xp + cy2 <= 0.0625)
        {
            finalColor = vec4(interiorColor(0.0), 1.0);
            return;
        }
    }

    if (exponent == 100)
    {
        for (int i = 0; i < steps; i++)
        {
            float r2 = zx.x * zx.x + zy.x * zy.x;
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(zx.x, zy.x));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    float fx = zx.x, fy = zy.x;
                    if (r2 < 1e12 && r2 > 0.0)
                    {
                        float r = sqrt(r2);
                        float th = atan(fy, fx);
                        float rp = power * pow(r, power - 1.0);
                        float ap = (power - 1.0) * th;
                        vec2 dp = vec2(rp * cos(ap), rp * sin(ap));
                        d = vec2(dp.x * d.x - dp.y * d.y, dp.x * d.y + dp.y * d.x);
                        if (addOne && !dScaled) d.x += 1.0;
                        renormDeriv(d, dScaled);
                        float raised = pow(r, power);
                        float ang = power * th;
                        fx = raised * cos(ang) + cx.x;
                        fy = raised * sin(ang) + cy.x;
                    }
                    normal2D = computeUnitNormal(fx, fy, d);
                }
                break;
            }

            if (r2 == 0.0)
            {
                if (power > 0.0)
                {
                    if (normalLighting == 1)
                        d = vec2(addOne && !dScaled ? 1.0 : 0.0, 0.0);
                    zx = cx;
                    zy = cy;
                    continue;
                }

                mu = float(i);
                escaped = true;
                break;
            }

            float radius = sqrt(r2);
            float theta = atan(zy.x, zx.x);
            if (normalLighting == 1)
            {
                float rp = power * pow(radius, power - 1.0);
                float ap = (power - 1.0) * theta;
                vec2 dp = vec2(rp * cos(ap), rp * sin(ap));
                d = vec2(dp.x * d.x - dp.y * d.y, dp.x * d.y + dp.y * d.x);
                if (addOne && !dScaled) d.x += 1.0;
                renormDeriv(d, dScaled);
            }

            float raised = pow(radius, power);
            float ang = power * theta;
            zx = dfAdd(cx, vec2(raised * cos(ang), 0.0));
            zy = dfAdd(cy, vec2(raised * sin(ang), 0.0));
            if (!(zx.x < 1e20))
            {
                mu = float(i);
                escaped = true;
                break;
            }
        }
    }
    else
    {
        for (int i = 0; i < steps; i++)
        {
            float r2 = zx.x * zx.x + zy.x * zy.x;
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(zx.x, zy.x));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    float fx = zx.x, fy = zy.x;
                    if (r2 < 1e12)
                    {
                        stepDerivInteger(fx, fy, exponent, addOne, d, dScaled);
                        float pzx, pzy;
                        complexPowF32(fx, fy, exponent, pzx, pzy);
                        fx = pzx + cx.x;
                        fy = pzy + cy.x;
                    }
                    normal2D = computeUnitNormal(fx, fy, d);
                }
                break;
            }

            if (normalLighting == 1)
                stepDerivInteger(zx.x, zy.x, exponent, addOne, d, dScaled);

            vec2 nx;
            vec2 ny;
            dfPow(zx, zy, exponent, nx, ny);
            zx = dfAdd(nx, cx);
            zy = dfAdd(ny, cy);
            if (!(zx.x < 1e20))
            {
                mu = float(i);
                escaped = true;
                break;
            }
        }
    }

    float aspect = resolution.x / max(resolution.y, 1.0);
    vec2 pixPos = vec2((px / resolution.x - 0.5) * aspect, -(py / resolution.y - 0.5));
    float finalTrap = orbitTrap != 0 ? (minTrap < 1e15 ? minTrap : trapDistance(zx.x, zy.x)) : 0.0;
    finalColor = vec4(colorOf(escaped, mu, minR, finalTrap, normal2D, pixPos), 1.0);
}
