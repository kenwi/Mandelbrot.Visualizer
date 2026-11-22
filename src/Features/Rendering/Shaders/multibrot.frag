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
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                break;
            }

            if (r2 == 0.0)
            {
                if (power > 0.0)
                {
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
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                break;
            }

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

    finalColor = vec4(colorOf(escaped, mu, minR), 1.0);
}
