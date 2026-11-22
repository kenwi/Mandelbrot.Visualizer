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
uniform int fractalFamily;
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

void stepDerivInteger(float zx, float zy, int n, bool addOne, inout vec2 d, inout bool dScaled, out float pzx, out float pzy)
{
    if (n == 2)
    {
        d = vec2(2.0 * (zx * d.x - zy * d.y), 2.0 * (zx * d.y + zy * d.x));
        if (addOne && !dScaled) d.x += 1.0;
        renormDeriv(d, dScaled);
        pzx = zx * zx - zy * zy;
        pzy = 2.0 * zx * zy;
        return;
    }

    float pmx, pmy;
    complexPow(zx, zy, n - 1, pmx, pmy);
    d = float(n) * vec2(pmx * d.x - pmy * d.y, pmx * d.y + pmy * d.x);
    if (addOne && !dScaled) d.x += 1.0;
    renormDeriv(d, dScaled);
    pzx = pmx * zx - pmy * zy;
    pzy = pmx * zy + pmy * zx;
}

vec2 computeUnitNormalJacobian(float zx, float zy, vec4 J)
{
    vec2 u = vec2(J.x * zx + J.z * zy, J.y * zx + J.w * zy);
    float len = length(u);
    return len > 1e-18 ? u / len : vec2(0.0);
}

bool stepFamilyF32(inout float zx, inout float zy, float cx, float cy, bool addOne, bool trackNormal, inout vec4 J, inout bool jScaled)
{
    float wx = zx;
    float wy = zy;
    float preX = 1.0;
    float preY = 1.0;
    if (fractalFamily == 1)
    {
        if (wx < 0.0) { wx = -wx; preX = -1.0; }
        if (wy >= 0.0) { wy = -wy; preY = -1.0; }
    }
    else if (fractalFamily == 2)
    {
        wy = -wy;
        preY = -1.0;
    }

    if (trackNormal)
    {
        J.xy *= preX;
        J.zw *= preY;
    }

    float px;
    float py;
    float pr = 0.0;
    float pi = 0.0;
    if (exponent == 100)
    {
        float r2 = wx * wx + wy * wy;
        if (r2 == 0.0)
        {
            if (power > 0.0)
            {
                if (trackNormal)
                    J = (addOne && !jScaled) ? vec4(1.0, 0.0, 0.0, 1.0) : vec4(0.0);
                zx = cx;
                zy = cy;
                return true;
            }
            return false;
        }

        float radius = sqrt(r2);
        float theta = atan(wy, wx);
        if (trackNormal)
        {
            float rp = power * pow(radius, power - 1.0);
            float ap = (power - 1.0) * theta;
            pr = rp * cos(ap);
            pi = rp * sin(ap);
        }

        float raised = pow(radius, power);
        float ang = power * theta;
        px = raised * cos(ang);
        py = raised * sin(ang);
    }
    else if (exponent == 2)
    {
        pr = 2.0 * wx;
        pi = 2.0 * wy;
        px = wx * wx - wy * wy;
        py = 2.0 * wx * wy;
    }
    else if (trackNormal)
    {
        float pmx, pmy;
        complexPow(wx, wy, exponent - 1, pmx, pmy);
        pr = float(exponent) * pmx;
        pi = float(exponent) * pmy;
        px = pmx * wx - pmy * wy;
        py = pmx * wy + pmy * wx;
    }
    else
    {
        complexPow(wx, wy, exponent, px, py);
    }

    if (trackNormal)
        J = vec4(pr * J.x - pi * J.z, pr * J.y - pi * J.w, pi * J.x + pr * J.z, pi * J.y + pr * J.w);

    float postX = 1.0;
    float postY = 1.0;
    if (fractalFamily == 3)
    {
        if (px < 0.0) { px = -px; postX = -1.0; }
    }
    else if (fractalFamily == 4)
    {
        if (px < 0.0) { px = -px; postX = -1.0; }
        if (py >= 0.0) { py = -py; postY = -1.0; }
    }

    if (trackNormal)
    {
        J.xy *= postX;
        J.zw *= postY;
        if (addOne && !jScaled) J.xw += 1.0;
        float f2 = dot(J, J);
        if (f2 > 1e20 || (jScaled && f2 > 0.0 && f2 < 1e-20))
        {
            J *= inversesqrt(f2);
            jScaled = true;
        }
    }

    zx = px + cx;
    zy = py + cy;
    return zx < 1e20 && zx > -1e20 && zy < 1e20 && zy > -1e20;
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
    if (fractalFamily == 0 && interiorStyle == 0 && juliaSet == 0 && exponent == 2)
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
    float minTrap = 1e20;
    vec2 d = juliaSet == 1 ? vec2(1.0, 0.0) : vec2(0.0, 0.0);
    bool dScaled = false;
    bool addOne = juliaSet == 0;
    vec2 normal2D = vec2(0.0);

    if (fractalFamily != 0)
    {
        vec4 J = juliaSet == 1 ? vec4(1.0, 0.0, 0.0, 1.0) : vec4(0.0);
        bool jScaled = false;
        float ox = zx;
        float oy = zy;
        int period = 8;
        int check = 0;

        for (int i = 0; i < steps; i++)
        {
            float r2 = zx * zx + zy * zy;
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(zx, zy));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    if (r2 < 1e12)
                        stepFamilyF32(zx, zy, cx, cy, addOne, true, J, jScaled);
                    normal2D = computeUnitNormalJacobian(zx, zy, J);
                }
                break;
            }

            if (!stepFamilyF32(zx, zy, cx, cy, addOne, normalLighting == 1, J, jScaled))
            {
                mu = float(i);
                escaped = true;
                break;
            }

            if (interiorStyle == 0 && orbitTrap == 0 && exponent != 100)
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
    else if (exponent == 100)
    {
        for (int i = 0; i < steps; i++)
        {
            float r2 = zx * zx + zy * zy;
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(zx, zy));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    if (r2 < 1e12 && r2 > 0.0)
                    {
                        float r = sqrt(r2);
                        float th = atan(zy, zx);
                        float rp = power * pow(r, power - 1.0);
                        float ap = (power - 1.0) * th;
                        vec2 dp = vec2(rp * cos(ap), rp * sin(ap));
                        d = vec2(dp.x * d.x - dp.y * d.y, dp.x * d.y + dp.y * d.x);
                        if (addOne && !dScaled) d.x += 1.0;
                        renormDeriv(d, dScaled);
                        float raised = pow(r, power);
                        float ang = power * th;
                        zx = raised * cos(ang) + cx;
                        zy = raised * sin(ang) + cy;
                    }
                    normal2D = computeUnitNormal(zx, zy, d);
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
            float theta = atan(zy, zx);
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
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(zx, zy));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    if (r2 < 1e12)
                    {
                        d = vec2(2.0 * (zx * d.x - zy * d.y), 2.0 * (zx * d.y + zy * d.x));
                        if (addOne && !dScaled) d.x += 1.0;
                        renormDeriv(d, dScaled);
                        zy = 2.0 * zx * zy + cy;
                        zx = x2 - y2 + cx;
                    }
                    normal2D = computeUnitNormal(zx, zy, d);
                }
                break;
            }

            if (normalLighting == 1)
            {
                d = vec2(2.0 * (zx * d.x - zy * d.y), 2.0 * (zx * d.y + zy * d.x));
                if (addOne && !dScaled) d.x += 1.0;
                renormDeriv(d, dScaled);
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
            if (orbitTrap != 0 && i > 0) minTrap = min(minTrap, trapDistance(zx, zy));
            if (escapedAt(r2, i, minR, mu))
            {
                escaped = true;
                if (normalLighting == 1)
                {
                    if (r2 < 1e12)
                    {
                        float pzx, pzy;
                        stepDerivInteger(zx, zy, exponent, addOne, d, dScaled, pzx, pzy);
                        zx = pzx + cx;
                        zy = pzy + cy;
                    }
                    normal2D = computeUnitNormal(zx, zy, d);
                }
                break;
            }

            float nx;
            float ny;
            if (normalLighting == 1)
                stepDerivInteger(zx, zy, exponent, addOne, d, dScaled, nx, ny);
            else
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

    float aspect = resolution.x / max(resolution.y, 1.0);
    vec2 pixPos = vec2((px / resolution.x - 0.5) * aspect, -(py / resolution.y - 0.5));
    float finalTrap = orbitTrap != 0 ? (minTrap < 1e15 ? minTrap : trapDistance(zx, zy)) : 0.0;
    finalColor = vec4(colorOf(escaped, mu, minR, finalTrap, normal2D, pixPos), 1.0);
}
