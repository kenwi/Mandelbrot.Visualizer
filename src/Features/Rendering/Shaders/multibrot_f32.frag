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

void complexPow(float zx, float zy, int n, out float ox, out float oy)
{
    int e = n < 0 ? -n : n;
    if (e == 0)
    {
        ox = 1.0;
        oy = 0.0;
        return;
    }

    if (e == 2)
    {
        ox = zx * zx - zy * zy;
        oy = 2.0 * zx * zy;
    }
    else if (e == 3)
    {
        float x2 = zx * zx - zy * zy;
        float y2 = 2.0 * zx * zy;
        ox = x2 * zx - y2 * zy;
        oy = x2 * zy + y2 * zx;
    }
    else if (e == 4)
    {
        float x2 = zx * zx - zy * zy;
        float y2 = 2.0 * zx * zy;
        ox = x2 * x2 - y2 * y2;
        oy = 2.0 * x2 * y2;
    }
    else
    {
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
    }

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
    float pixelRe = center.x + (px / resolution.x - 0.5) * 2.0 * halfW;
    float pixelIm = center.z - (py / resolution.y - 0.5) * 2.0 * halfH;

    float zx;
    float zy;
    float cx;
    float cy;
    if (juliaSet == 1)
    {
        zx = pixelRe;
        zy = pixelIm;
        cx = juliaC.x;
        cy = juliaC.z;
    }
    else
    {
        zx = 0.0;
        zy = 0.0;
        cx = pixelRe;
        cy = pixelIm;
    }

    int steps = max(maxIter, 1);

    if (abs(power) < 1e-4)
    {
        finalColor = vec4(interiorColor(0.0), 1.0);
        return;
    }

    // Fast cardioid and period-2 bulb rejection for standard Mandelbrot with solid interior.
    if (interiorStyle == 0 && juliaSet == 0 && exponent == 2)
    {
        float xm = cx - 0.25;
        float cy2 = cy * cy;
        float q = xm * xm + cy2;
        if (q * (q + xm) <= 0.25 * cy2)
        {
            finalColor = vec4(interiorColor(0.0), 1.0);
            return;
        }

        float xp = cx + 1.0;
        if (xp * xp + cy2 <= 0.0625)
        {
            finalColor = vec4(interiorColor(0.0), 1.0);
            return;
        }
    }

    bool escaped = false;
    float mu = -1.0;
    float minR = 1e20;

    if (exponent == 100)
    {
        for (int i = 0; i < steps; i++)
        {
            float r2 = zx * zx + zy * zy;
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
            float theta = atan(zy, zx);
            float raised = pow(radius, power);
            float ang = power * theta;
            zx = raised * cos(ang) + cx;
            zy = raised * sin(ang) + cy;
            if (!(zx < 1e20))
            {
                mu = float(i);
                escaped = true;
                break;
            }
        }
    }
    else if (exponent == 2)
    {
        float ox = zx;
        float oy = zy;
        int period = 8;
        int check = 0;

        for (int i = 0; i < steps; i++)
        {
            float x2 = zx * zx;
            float y2 = zy * zy;
            float r2 = x2 + y2;
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                break;
            }

            zy = 2.0 * zx * zy + cy;
            zx = x2 - y2 + cx;

            if (interiorStyle == 0)
            {
                if (abs(zx - ox) <= 1e-7 && abs(zy - oy) <= 1e-7)
                    break;

                check++;
                if (check >= period)
                {
                    ox = zx;
                    oy = zy;
                    check = 0;
                    period = min(period * 2, 1024);
                }
            }
        }
    }
    else
    {
        float ox = zx;
        float oy = zy;
        int period = 8;
        int check = 0;

        for (int i = 0; i < steps; i++)
        {
            float r2 = zx * zx + zy * zy;
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                break;
            }

            float nx;
            float ny;
            complexPow(zx, zy, exponent, nx, ny);
            zx = nx + cx;
            zy = ny + cy;
            if (!(zx < 1e20))
            {
                mu = float(i);
                escaped = true;
                break;
            }

            if (interiorStyle == 0)
            {
                if (abs(zx - ox) <= 1e-7 && abs(zy - oy) <= 1e-7)
                    break;

                check++;
                if (check >= period)
                {
                    ox = zx;
                    oy = zy;
                    check = 0;
                    period = min(period * 2, 1024);
                }
            }
        }
    }

    finalColor = vec4(colorOf(escaped, mu, minR), 1.0);
}
