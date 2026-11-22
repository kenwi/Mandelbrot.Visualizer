using System.Numerics;
using System.Runtime.InteropServices;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Rendering;

/// <summary>
/// Draws the set with a fragment shader into a render texture, then blits it.
/// Integer powers iterate in double-float so a deep zoom stays sharp.
/// Fractional powers use the polar form in single precision.
/// Histogram coloring stays on the CPU renderer.
/// </summary>
public sealed class GpuFractalRenderer : IDisposable
{
    const int PolarExponent = 100;

    Shader _shader;
    RenderTexture _target;
    bool _shaderReady;
    bool _hasTarget;
    int _width;
    int _height;
    bool _disposed;

    int _resolutionLoc;
    int _centerLoc;
    int _juliaCLoc;
    int _scaleLoc;
    int _powerLoc;
    int _exponentLoc;
    int _maxIterLoc;
    int _paletteLoc;
    int _smoothLoc;
    int _interiorLoc;
    int _densityLoc;
    int _phaseLoc;
    int _juliaLoc;

    public bool IsAvailable { get; private set; }
    public bool HasImage { get; private set; }
    public bool HasFrameTime { get; private set; }
    public double LastFrameMs { get; private set; }

    uint _query;
    bool _queryRunning;
    bool _timerFailed;

    public void Load()
    {
        if (_disposed || _shaderReady) return;

        _shader = Raylib.LoadShaderFromMemory(VertexSource, FragmentSource);
        _shaderReady = true;
        if (!Raylib.IsShaderReady(_shader))
        {
            Console.Error.WriteLine("Multibrot shader failed to compile; rendering on the CPU.");
            if (_shader.id != 0) Raylib.UnloadShader(_shader);
            _shaderReady = false;
            IsAvailable = false;
            return;
        }

        _resolutionLoc = Raylib.GetShaderLocation(_shader, "resolution");
        _centerLoc = Raylib.GetShaderLocation(_shader, "center");
        _juliaCLoc = Raylib.GetShaderLocation(_shader, "juliaC");
        _scaleLoc = Raylib.GetShaderLocation(_shader, "scale");
        _powerLoc = Raylib.GetShaderLocation(_shader, "power");
        _exponentLoc = Raylib.GetShaderLocation(_shader, "exponent");
        _maxIterLoc = Raylib.GetShaderLocation(_shader, "maxIter");
        _paletteLoc = Raylib.GetShaderLocation(_shader, "paletteId");
        _smoothLoc = Raylib.GetShaderLocation(_shader, "smoothColor");
        _interiorLoc = Raylib.GetShaderLocation(_shader, "interiorStyle");
        _densityLoc = Raylib.GetShaderLocation(_shader, "density");
        _phaseLoc = Raylib.GetShaderLocation(_shader, "phase");
        _juliaLoc = Raylib.GetShaderLocation(_shader, "juliaSet");
        IsAvailable = true;
    }

    public void Draw(int width, int height, in RenderRequest request)
    {
        if (!IsAvailable || width < 1 || height < 1) return;

        EnsureTarget(width, height);
        SetUniforms(width, height, request);
        PollGpuTime();
        bool timing = BeginGpuTime();

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(new Color(4, 6, 12, 255));
        Raylib.BeginShaderMode(_shader);
        Raylib.DrawRectangle(0, 0, width, height, new Color(255, 255, 255, 255));
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
        EndGpuTime(timing);
        HasImage = true;

        var source = new Rectangle { x = 0, y = 0, width = width, height = -height };
        Raylib.DrawTextureRec(_target.texture, source, Vector2.Zero, new Color(255, 255, 255, 255));
    }

    public void Save(string path)
    {
        if (!HasImage) return;
        Image image = Raylib.LoadImageFromTexture(_target.texture);
        unsafe
        {
            Raylib.ImageFlipVertical(&image);
        }

        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hasTarget)
        {
            Raylib.UnloadRenderTexture(_target);
            _hasTarget = false;
        }

        if (_shaderReady)
        {
            Raylib.UnloadShader(_shader);
            _shaderReady = false;
        }

        if (_query != 0)
        {
            DeleteQueries(1, ref _query);
            _query = 0;
        }

        IsAvailable = false;
        HasImage = false;
    }

    void EnsureTarget(int width, int height)
    {
        if (_hasTarget && _width == width && _height == height) return;
        if (_hasTarget) Raylib.UnloadRenderTexture(_target);

        _target = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureFilter(_target.texture, TextureFilter.TEXTURE_FILTER_POINT);
        _width = width;
        _height = height;
        _hasTarget = true;
    }

    void SetUniforms(int width, int height, in RenderRequest request)
    {
        var resolution = new Vector2(width, height);
        Split(request.CenterX, out float reHi, out float reLo);
        Split(request.CenterY, out float imHi, out float imLo);
        Split(request.JuliaRe, out float cReHi, out float cReLo);
        Split(request.JuliaIm, out float cImHi, out float cImLo);
        var center = new Vector4(reHi, reLo, imHi, imLo);
        var juliaC = new Vector4(cReHi, cReLo, cImHi, cImLo);
        float scale = (float)request.Scale;
        float power = (float)request.Power;
        int exponent = ShaderExponent(request.Power);
        int maxIter = Math.Clamp(request.MaxIterations, 1, 4000);
        int palette = request.Palette;
        int smooth = request.Smooth ? 1 : 0;
        int interior = (int)request.Interior;
        float density = (float)request.Density;
        float phase = (float)request.Phase;
        int julia = request.Julia ? 1 : 0;

        Set(_resolutionLoc, resolution, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
        Set(_centerLoc, center, ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        Set(_juliaCLoc, juliaC, ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        Set(_scaleLoc, scale, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_powerLoc, power, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_exponentLoc, exponent, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_maxIterLoc, maxIter, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_paletteLoc, palette, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_smoothLoc, smooth, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_interiorLoc, interior, ShaderUniformDataType.SHADER_UNIFORM_INT);
        Set(_densityLoc, density, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_phaseLoc, phase, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        Set(_juliaLoc, julia, ShaderUniformDataType.SHADER_UNIFORM_INT);
    }

    void Set<T>(int location, T value, ShaderUniformDataType type) where T : unmanaged
        => Raylib.SetShaderValue(_shader, location, value, type);

    static int ShaderExponent(double power)
    {
        if (!double.IsFinite(power)) return PolarExponent;
        double rounded = Math.Round(power);
        if (Math.Abs(power - rounded) < 1e-8 && rounded is >= -16 and <= 16)
            return (int)rounded;
        return PolarExponent;
    }

    void PollGpuTime()
    {
        if (_timerFailed || !_queryRunning)
            return;

        try
        {
            GetQueryObjectiv(_query, QueryResultAvailable, out int ready);
            if (ready == 0)
                return;

            GetQueryObjectui64(_query, QueryResult, out ulong nanoseconds);
            LastFrameMs = nanoseconds / 1_000_000.0;
            HasFrameTime = true;
            _queryRunning = false;
        }
        catch (DllNotFoundException)
        {
            _timerFailed = true;
        }
        catch (EntryPointNotFoundException)
        {
            _timerFailed = true;
        }
    }

    bool BeginGpuTime()
    {
        if (_timerFailed || _queryRunning)
            return false;

        try
        {
            if (_query == 0)
                GenQueries(1, ref _query);
            BeginQuery(TimeElapsed, _query);
            return true;
        }
        catch (DllNotFoundException)
        {
            _timerFailed = true;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            _timerFailed = true;
            return false;
        }
    }

    void EndGpuTime(bool timing)
    {
        if (!timing)
            return;

        try
        {
            EndQuery(TimeElapsed);
            _queryRunning = true;
        }
        catch (DllNotFoundException)
        {
            _timerFailed = true;
        }
        catch (EntryPointNotFoundException)
        {
            _timerFailed = true;
        }
    }

    const uint TimeElapsed = 0x88BF;
    const uint QueryResult = 0x8866;
    const uint QueryResultAvailable = 0x8867;

    [DllImport("libGL.so.1", EntryPoint = "glGenQueries")]
    static extern void GenQueries(int count, ref uint id);

    [DllImport("libGL.so.1", EntryPoint = "glDeleteQueries")]
    static extern void DeleteQueries(int count, ref uint id);

    [DllImport("libGL.so.1", EntryPoint = "glBeginQuery")]
    static extern void BeginQuery(uint target, uint id);

    [DllImport("libGL.so.1", EntryPoint = "glEndQuery")]
    static extern void EndQuery(uint target);

    [DllImport("libGL.so.1", EntryPoint = "glGetQueryObjectiv")]
    static extern void GetQueryObjectiv(uint id, uint name, out int param);

    [DllImport("libGL.so.1", EntryPoint = "glGetQueryObjectui64v")]
    static extern void GetQueryObjectui64(uint id, uint name, out ulong param);

    static void Split(double value, out float hi, out float lo)
    {
        hi = (float)value;
        lo = (float)(value - hi);
    }

    const string VertexSource = """
        #version 330

        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;

        uniform mat4 mvp;

        out vec2 fragTexCoord;
        out vec4 fragColor;

        void main()
        {
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    // Palette stops match Coloring/Palette.cs. Cyclic shading matches Palette.Map.
    const string FragmentSource = """
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

        vec3 band(float t, float a, vec3 ca, float b, vec3 cb)
        {
            float span = b - a;
            float local = span <= 0.0 ? 0.0 : clamp((t - a) / span, 0.0, 1.0);
            return mix(ca, cb, local);
        }

        vec3 hsvRainbow(float h)
        {
            float c = 0.82;
            float x = c * (1.0 - abs(mod(h * 6.0, 2.0) - 1.0));
            float m = 0.18;
            float sector = floor(h * 6.0);
            vec3 rgb;
            if (sector < 1.0) rgb = vec3(c, x, 0.0);
            else if (sector < 2.0) rgb = vec3(x, c, 0.0);
            else if (sector < 3.0) rgb = vec3(0.0, c, x);
            else if (sector < 4.0) rgb = vec3(0.0, x, c);
            else if (sector < 5.0) rgb = vec3(x, 0.0, c);
            else rgb = vec3(c, 0.0, x);
            return rgb + vec3(m);
        }

        vec3 classic(float t)
        {
            vec3 c0 = vec3(4.0, 6.0, 20.0) / 255.0;
            vec3 c1 = vec3(12.0, 28.0, 120.0) / 255.0;
            vec3 c2 = vec3(20.0, 140.0, 210.0) / 255.0;
            vec3 c3 = vec3(230.0, 240.0, 255.0) / 255.0;
            vec3 c4 = vec3(250.0, 210.0, 60.0) / 255.0;
            vec3 c5 = vec3(200.0, 50.0, 20.0) / 255.0;
            if (t <= 0.18) return band(t, 0.00, c0, 0.18, c1);
            if (t <= 0.38) return band(t, 0.18, c1, 0.38, c2);
            if (t <= 0.55) return band(t, 0.38, c2, 0.55, c3);
            if (t <= 0.72) return band(t, 0.55, c3, 0.72, c4);
            if (t <= 0.88) return band(t, 0.72, c4, 0.88, c5);
            return band(t, 0.88, c5, 1.00, c0);
        }

        vec3 fire(float t)
        {
            vec3 c0 = vec3(0.0, 0.0, 0.0);
            vec3 c1 = vec3(90.0, 0.0, 0.0) / 255.0;
            vec3 c2 = vec3(220.0, 40.0, 0.0) / 255.0;
            vec3 c3 = vec3(255.0, 180.0, 20.0) / 255.0;
            vec3 c4 = vec3(255.0, 250.0, 220.0) / 255.0;
            if (t <= 0.25) return band(t, 0.00, c0, 0.25, c1);
            if (t <= 0.50) return band(t, 0.25, c1, 0.50, c2);
            if (t <= 0.75) return band(t, 0.50, c2, 0.75, c3);
            return band(t, 0.75, c3, 1.00, c4);
        }

        vec3 electric(float t)
        {
            vec3 c0 = vec3(8.0, 0.0, 20.0) / 255.0;
            vec3 c1 = vec3(90.0, 0.0, 160.0) / 255.0;
            vec3 c2 = vec3(40.0, 220.0, 255.0) / 255.0;
            vec3 c3 = vec3(180.0, 255.0, 80.0) / 255.0;
            if (t <= 0.30) return band(t, 0.00, c0, 0.30, c1);
            if (t <= 0.55) return band(t, 0.30, c1, 0.55, c2);
            if (t <= 0.78) return band(t, 0.55, c2, 0.78, c3);
            return band(t, 0.78, c3, 1.00, c0);
        }

        vec3 ocean(float t)
        {
            vec3 c0 = vec3(2.0, 8.0, 20.0) / 255.0;
            vec3 c1 = vec3(8.0, 50.0, 110.0) / 255.0;
            vec3 c2 = vec3(20.0, 160.0, 170.0) / 255.0;
            vec3 c3 = vec3(180.0, 240.0, 220.0) / 255.0;
            if (t <= 0.35) return band(t, 0.00, c0, 0.35, c1);
            if (t <= 0.62) return band(t, 0.35, c1, 0.62, c2);
            if (t <= 0.85) return band(t, 0.62, c2, 0.85, c3);
            return band(t, 0.85, c3, 1.00, c0);
        }

        vec3 sunset(float t)
        {
            vec3 c0 = vec3(20.0, 8.0, 40.0) / 255.0;
            vec3 c1 = vec3(140.0, 30.0, 80.0) / 255.0;
            vec3 c2 = vec3(230.0, 90.0, 40.0) / 255.0;
            vec3 c3 = vec3(255.0, 190.0, 90.0) / 255.0;
            vec3 c4 = vec3(40.0, 20.0, 70.0) / 255.0;
            if (t <= 0.30) return band(t, 0.00, c0, 0.30, c1);
            if (t <= 0.55) return band(t, 0.30, c1, 0.55, c2);
            if (t <= 0.78) return band(t, 0.55, c2, 0.78, c3);
            return band(t, 0.78, c3, 1.00, c4);
        }

        vec3 ice(float t)
        {
            vec3 c0 = vec3(6.0, 10.0, 24.0) / 255.0;
            vec3 c1 = vec3(40.0, 80.0, 140.0) / 255.0;
            vec3 c2 = vec3(170.0, 210.0, 240.0) / 255.0;
            vec3 c3 = vec3(245.0, 250.0, 255.0) / 255.0;
            if (t <= 0.40) return band(t, 0.00, c0, 0.40, c1);
            if (t <= 0.70) return band(t, 0.40, c1, 0.70, c2);
            return band(t, 0.70, c2, 1.00, c3);
        }

        vec3 grayscale(float t)
        {
            return mix(vec3(0.0), vec3(1.0), clamp(t, 0.0, 1.0));
        }

        vec3 samplePalette(int id, float t)
        {
            t = t - floor(t);
            if (id == 1) return fire(t);
            if (id == 2) return electric(t);
            if (id == 3) return ocean(t);
            if (id == 4) return sunset(t);
            if (id == 5) return ice(t);
            if (id == 6) return grayscale(t);
            if (id == 7) return hsvRainbow(t);
            return classic(t);
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

            int steps = maxIter;
            if (steps < 1) steps = 1;
            if (steps > 4000) steps = 4000;

            bool escaped = false;
            float mu = -1.0;
            float minR = 1e20;

            if (abs(power) < 1e-4)
            {
                finalColor = vec4(interiorColor(0.0), 1.0);
                return;
            }

            if (exponent == 100)
            {
                for (int i = 0; i < 4000; i++)
                {
                    if (i >= steps) break;
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
                for (int i = 0; i < 4000; i++)
                {
                    if (i >= steps) break;
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
        """;
}
