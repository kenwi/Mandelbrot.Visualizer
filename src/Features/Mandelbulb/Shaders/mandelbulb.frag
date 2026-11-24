#version 330

in vec2 fragTexCoord;
out vec4 finalColor;

uniform vec2 resolution;
uniform vec2 subpixelOffset;

uniform vec3 camPos;
uniform vec3 camForward;
uniform vec3 camRight;
uniform vec3 camUp;
uniform float tanHalfFov;

uniform int shapeMode;
uniform float boundingRadius;
uniform float power;
uniform float boxScale;
uniform float boxMinRadius;
uniform float boxFoldLimit;
uniform int maxIterations;
uniform float detail;
uniform float aoStrength;
uniform int softShadows;
uniform int finalQuality;
uniform int juliaMode;
uniform vec3 juliaC;

uniform sampler2D paletteLut;
uniform int paletteIndex;
uniform float density;
uniform float phase;
uniform float lightAngle;

const float PALETTE_COUNT = 8.0;

vec3 samplePalette(float t)
{
    float u = fract(t);
    float v = (float(paletteIndex) + 0.5) / PALETTE_COUNT;
    return texture(paletteLut, vec2(u, v)).rgb;
}

vec2 intersectSphere(vec3 ro, vec3 rd, float radius)
{
    float b = dot(ro, rd);
    float c = dot(ro, ro) - radius * radius;
    float h = b * b - c;
    if (h < 0.0) return vec2(-1.0);
    h = sqrt(h);
    return vec2(-b - h, -b + h);
}

// White/Nylander spherical-coordinate Mandelbulb DE with Y-up polar axis.
// Returns vec2(distanceEstimate, orbitTrapSignal).
vec2 mapBulb(vec3 pos)
{
    vec3 z = pos;
    vec3 c = juliaMode != 0 ? juliaC : pos;
    float dr = 1.0;
    float r = 0.0;
    float trapMin = 1e10;
    float trapAxis = 1e10;

    for (int i = 0; i < 24; i++)
    {
        if (i >= maxIterations) break;
        r = length(z);
        if (r > 2.5) break;

        trapMin = min(trapMin, r);
        trapAxis = min(trapAxis, min(abs(z.x), min(abs(z.y), abs(z.z))));

        float safeR = max(r, 1e-8);
        float theta = acos(clamp(z.y / safeR, -1.0, 1.0));
        float phi = atan(z.z, z.x);
        dr = pow(safeR, power - 1.0) * power * dr + 1.0;

        float zr = pow(safeR, power);
        theta *= power;
        phi *= power;

        float sinT = sin(theta);
        z = zr * vec3(sinT * cos(phi), cos(theta), sinT * sin(phi)) + c;
    }

    float safeFinalR = max(r, 1e-8);
    float dist = 0.5 * log(safeFinalR) * safeFinalR / max(dr, 1e-8);
    float trap = mix(trapMin, trapAxis * 1.4, 0.35);
    return vec2(dist, trap);
}

// Tom Lowe Mandelbox DE (box fold + sphere fold + scale/offset).
// Supports positive scales (classic cube), negative scales (sci-fi cathedral), and 3D Julia mode.
vec2 mapMandelbox(vec3 pos)
{
    vec3 z = pos;
    vec3 c = juliaMode != 0 ? juliaC : pos;
    float dr = 1.0;
    float absScale = max(abs(boxScale), 0.1);
    float minR2 = max(boxMinRadius * boxMinRadius, 0.01);
    float fixedR2 = max(1.0, minR2 + 0.01);
    float trapMin = 1e10;
    float trapAxis = 1e10;

    for (int i = 0; i < 24; i++)
    {
        if (i >= maxIterations) break;

        // Box fold
        z = clamp(z, -boxFoldLimit, boxFoldLimit) * 2.0 - z;

        // Sphere fold
        float r2 = dot(z, z);
        trapMin = min(trapMin, r2);
        trapAxis = min(trapAxis, min(abs(z.x), min(abs(z.y), abs(z.z))));

        if (r2 < minR2)
        {
            float temp = fixedR2 / minR2;
            z *= temp;
            dr *= temp;
        }
        else if (r2 < fixedR2)
        {
            float temp = fixedR2 / r2;
            z *= temp;
            dr *= temp;
        }

        z = z * boxScale + c;
        dr = dr * absScale + 1.0;

        if (dot(z, z) > 1e6) break;
    }

    float dist = length(z) / max(abs(dr), 1e-8);
    float trap = mix(sqrt(trapMin) * 0.55, trapAxis * 0.85, 0.35);
    return vec2(dist, trap);
}

vec2 mapScene(vec3 pos)
{
    return shapeMode != 0 ? mapMandelbox(pos) : mapBulb(pos);
}

float mapDistance(vec3 pos)
{
    return mapScene(pos).x;
}

// 4-tap tetrahedral normal for smooth, artifact-free surface gradients.
vec3 calcNormal(vec3 pos, float eps)
{
    vec2 e = vec2(1.0, -1.0) * eps;
    return normalize(
        e.xyy * mapDistance(pos + e.xyy) +
        e.yyx * mapDistance(pos + e.yyx) +
        e.yxy * mapDistance(pos + e.yxy) +
        e.xxx * mapDistance(pos + e.xxx));
}

float calcAO(vec3 pos, vec3 nor, float baseEps, float boundR)
{
    float occ = 0.0;
    float weight = 1.0;
    float stepBase = clamp(baseEps * 10.0, 0.0006, boundR * 0.018);
    for (int i = 1; i <= 5; i++)
    {
        float h = stepBase * float(i * i) * 0.45;
        float d = mapDistance(pos + nor * h);
        occ += clamp((h - d) / h, 0.0, 1.0) * weight;
        weight *= 0.62;
    }
    return clamp(1.0 - 0.85 * occ, 0.05, 1.0);
}

float calcSoftShadow(vec3 ro, vec3 rd, float minT, float maxT, float k, float boundR)
{
    float res = 1.0;
    float t = minT;
    float maxStep = boundR * 0.12;
    float boundSq = boundR * boundR + boundR * 0.25;
    for (int i = 0; i < 40; i++)
    {
        vec3 p = ro + rd * t;
        if (dot(p, p) > boundSq)
            break;
        float h = mapDistance(p);
        if (h < minT * 0.25)
            return 0.0;
        res = min(res, k * h / t);
        t += clamp(h * 0.82, minT * 0.5, maxStep);
        if (t > maxT)
            break;
    }
    return clamp(res, 0.0, 1.0);
}

vec3 background(vec2 uv)
{
    float radial = length(uv);
    vec3 deep = vec3(0.016, 0.024, 0.048);
    vec3 horizon = vec3(0.042, 0.068, 0.125);
    return mix(horizon, deep, smoothstep(0.0, 1.25, radial));
}

vec3 acesToneMap(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec2 px = gl_FragCoord.xy + subpixelOffset;
    vec2 ndc = (px / resolution) * 2.0 - 1.0;
    float aspect = resolution.x / max(resolution.y, 1.0);
    vec2 screen = vec2(ndc.x * aspect, ndc.y);

    vec3 rd = normalize(camForward + screen.x * tanHalfFov * camRight + screen.y * tanHalfFov * camUp);
    vec3 bg = background(screen);

    float boundR = max(boundingRadius, 1.55);
    vec2 hitBounds = intersectSphere(camPos, rd, boundR);
    if (hitBounds.y < 0.0)
    {
        finalColor = vec4(bg, 1.0);
        return;
    }

    float tMin = max(0.0, hitBounds.x);
    float tMax = hitBounds.y;
    float pixelAngle = (2.0 * tanHalfFov) / max(resolution.y, 1.0);
    float epsScale = (finalQuality != 0 ? 0.45 : 1.15) / max(detail, 0.1);
    int maxSteps = finalQuality != 0 ? 220 : 88;
    float stepScale = shapeMode != 0 ? 0.80 : 0.85;

    float t = tMin;
    bool hit = false;
    int steps = 0;
    float trap = 0.0;
    float minDistGlow = 1e10;

    for (int i = 0; i < 240; i++)
    {
        if (i >= maxSteps) break;
        steps = i;

        vec3 pos = camPos + rd * t;
        vec2 res = mapScene(pos);
        float d = res.x;
        trap = res.y;
        minDistGlow = min(minDistGlow, d / max(t, 0.1));

        float eps = max(1e-6, pixelAngle * t * epsScale);
        if (d < eps)
        {
            hit = true;
            break;
        }

        t += d * stepScale;
        if (t > tMax)
            break;
    }

    if (!hit)
    {
        // Subtle atmospheric halo hugging the fractal silhouette (d/t is angular, scale-invariant).
        float sphereFade = smoothstep(0.0, boundR * 0.35, tMax - tMin);
        float halo = exp(-minDistGlow * 20.0) * 0.22 * sphereFade;
        vec3 haloTint = mix(vec3(0.35, 0.62, 0.95), samplePalette(phase + 0.35), 0.55);
        finalColor = vec4(clamp(bg + haloTint * halo, 0.0, 1.0), 1.0);
        return;
    }

    vec3 pos = camPos + rd * t;
    float hitEps = max(1e-5, pixelAngle * t * epsScale * 1.4);
    vec3 nor = calcNormal(pos, hitEps);

    // Directional key, fill, and warm bounce lights controlled by lightAngle.
    float ang = radians(lightAngle);
    vec3 keyDir = normalize(vec3(cos(ang) * 0.82, 0.58, sin(ang) * 0.82));
    vec3 fillDir = normalize(vec3(-keyDir.x, 0.35, -keyDir.z));
    vec3 bounceDir = vec3(0.0, -1.0, 0.0);

    // Orbit-trap palette albedo.
    float palT = log(1.0 + max(trap, 0.0) * 2.8) * 0.62 * max(density, 0.05) + phase;
    vec3 albedo = samplePalette(palT);

    // Ambient occlusion: combine SDF AO taps with raymarch step-density darkening.
    float stepOcc = clamp(1.0 - float(steps) / float(maxSteps), 0.12, 1.0);
    float sdfAo = finalQuality != 0 ? calcAO(pos, nor, hitEps, boundR) : stepOcc;
    float ao = mix(1.0, mix(stepOcc, sdfAo, 0.72), clamp(aoStrength, 0.0, 1.0));

    float shadow = 1.0;
    if (softShadows != 0 && finalQuality != 0)
    {
        float shadowStart = max(hitEps * 5.0, boundR * 0.00028);
        shadow = calcSoftShadow(pos + nor * shadowStart, keyDir, shadowStart, boundR * 1.4, 8.5, boundR);
    }

    float keyDiff = max(dot(nor, keyDir), 0.0);
    float fillDiff = max(dot(nor, fillDir), 0.0);
    float bounceDiff = max(dot(nor, bounceDir), 0.0);

    vec3 halfVec = normalize(keyDir - rd);
    float spec = pow(max(dot(nor, halfVec), 0.0), 36.0) * shadow;
    float fresnel = pow(clamp(1.0 - max(dot(nor, -rd), 0.0), 0.0, 1.0), 3.0);

    vec3 lighting = vec3(0.0);
    lighting += albedo * vec3(1.18, 1.12, 1.04) * keyDiff * (0.18 + 0.82 * shadow);
    lighting += albedo * vec3(0.32, 0.44, 0.66) * fillDiff * ao;
    lighting += albedo * vec3(0.24, 0.16, 0.12) * bounceDiff * ao;
    lighting += albedo * vec3(0.14, 0.17, 0.22) * ao;
    lighting += vec3(0.55, 0.62, 0.72) * spec * 0.55;
    lighting += mix(vec3(0.30, 0.55, 0.90), albedo, 0.45) * fresnel * ao * 0.32;

    // Gentle distance fog blending deep interiors/far silhouettes into the backdrop.
    float fog = smoothstep(tMax + boundR * 0.35, tMax + boundR * 2.2, t);
    vec3 color = mix(lighting, bg, fog);
    color = acesToneMap(color * 1.15);
    color = pow(color, vec3(1.0 / 1.08));

    finalColor = vec4(color, 1.0);
}
