using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Rendering;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

/// <summary>
/// Serializable 3D Mandelbulb landmark / user bookmark supporting both Orbit and
/// lossless quaternion 6DOF Fly camera poses, fractal parameters, and lighting/palette state.
/// </summary>
public sealed record MandelbulbBookmarkEntry
{
    public string Name { get; init; } = "Untitled 3D Spot";
    public bool Curated { get; init; }
    public string Category { get; init; } = "Saved";

    public BulbCameraMode CameraMode { get; init; } = BulbCameraMode.Orbit;

    // Orbit camera state
    public float TargetX { get; init; }
    public float TargetY { get; init; }
    public float TargetZ { get; init; }
    public float Yaw { get; init; } = 0.65f;
    public float Pitch { get; init; } = 0.36f;
    public float Distance { get; init; } = 3.25f;

    // 6DOF Fly camera state (lossless unit quaternion)
    public float PosX { get; init; }
    public float PosY { get; init; }
    public float PosZ { get; init; } = 3.25f;
    public float Qx { get; init; }
    public float Qy { get; init; }
    public float Qz { get; init; }
    public float Qw { get; init; } = 1f;
    public float FlySpeed { get; init; } = 1.0f;

    public float Fov { get; init; } = 42.0f;

    // Fractal parameters
    public BulbShape Shape { get; init; } = BulbShape.Mandelbulb;
    public double Power { get; init; } = 8.0;
    public double BoxScale { get; init; } = 2.0;
    public double BoxMinRadius { get; init; } = 0.5;
    public double BoxFoldLimit { get; init; } = 1.0;
    public int Iterations { get; init; } = 10;
    public double Detail { get; init; } = 1.0;
    public double AoStrength { get; init; } = 0.78;
    public bool SoftShadows { get; init; } = true;
    public bool Julia { get; init; }
    public float JuliaX { get; init; } = -0.40f;
    public float JuliaY { get; init; } = 0.60f;
    public float JuliaZ { get; init; } = 0.20f;

    // Optional lighting, atmosphere, PBR material, and DoF
    public double LightElevation { get; init; } = 35.0;
    public bool Headlamp { get; init; }
    public double FogDensity { get; init; }
    public double Roughness { get; init; } = 0.42;
    public double Metallic { get; init; } = 0.12;
    public double GlowStrength { get; init; }
    public bool DofEnabled { get; init; }
    public double DofAperture { get; init; } = 0.04;
    public bool DofAutoFocus { get; init; } = true;
    public double DofFocusDistance { get; init; } = 2.0;

    // Coloring & lighting
    public string Palette { get; init; } = "Classic";
    public double Density { get; init; } = 1.0;
    public double Phase { get; init; }
    public double LightAngle { get; init; } = 135.0;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Untitled 3D Spot" : Name.Trim();

    public MandelbulbParams ToParams(BulbQuality preserveQuality = BulbQuality.Balanced) => new(
        Power: MandelbulbParams.ClampPower(Power),
        Iterations: MandelbulbParams.ClampIterations(Iterations),
        Detail: Math.Clamp(Detail, MandelbulbParams.DetailMin, MandelbulbParams.DetailMax),
        AoStrength: Math.Clamp(AoStrength, 0.0, 1.0),
        SoftShadows: SoftShadows,
        Julia: Julia,
        JuliaC: new Vector3(JuliaX, JuliaY, JuliaZ),
        Shape: Shape,
        BoxScale: MandelbulbParams.ClampBoxScale(BoxScale),
        BoxMinRadius: MandelbulbParams.ClampBoxMinRadius(BoxMinRadius),
        BoxFoldLimit: MandelbulbParams.ClampBoxFoldLimit(BoxFoldLimit),
        Quality: preserveQuality,
        LightElevation: MandelbulbParams.ClampLightElevation(LightElevation),
        Headlamp: Headlamp,
        FogDensity: MandelbulbParams.ClampUnit(FogDensity),
        Roughness: MandelbulbParams.ClampRoughness(Roughness),
        Metallic: MandelbulbParams.ClampUnit(Metallic, 0.12),
        GlowStrength: MandelbulbParams.ClampUnit(GlowStrength),
        DofEnabled: DofEnabled,
        DofAperture: MandelbulbParams.ClampDofAperture(DofAperture),
        DofAutoFocus: DofAutoFocus,
        DofFocusDistance: MandelbulbParams.ClampDofFocus(DofFocusDistance));

    public OrbitCameraState ToOrbitState() => new(
        Target: new Vector3(TargetX, TargetY, TargetZ),
        Yaw: Yaw,
        Pitch: Math.Clamp(Pitch, -OrbitCameraState.MaxPitch, OrbitCameraState.MaxPitch),
        Distance: Math.Clamp(Distance, OrbitCameraState.MinDistance, OrbitCameraState.MaxDistance),
        FovDegrees: Math.Clamp(Fov, 20f, 80f));

    public CameraPose ToFlyPose()
    {
        var q = new Quaternion(Qx, Qy, Qz, Qw);
        if (q.LengthSquared() < 1e-10f)
            q = Quaternion.Identity;
        else
            q = Quaternion.Normalize(q);

        return new CameraPose(
            Position: new Vector3(PosX, PosY, PosZ),
            Orientation: q,
            FovDegrees: Math.Clamp(Fov, CameraPose.MinFov, CameraPose.MaxFov));
    }

    public CameraPose ToActivePose()
        => CameraMode == BulbCameraMode.Fly ? ToFlyPose() : ToOrbitState().ToPose();

    public ColoringParams ApplyColoring(in ColoringParams current)
    {
        int palIndex = Coloring.Palette.TryParseName(Palette, out int parsed) ? parsed : current.Palette;
        double angle = ((LightAngle % 360.0) + 360.0) % 360.0;
        return current with
        {
            Palette = palIndex,
            Density = Math.Clamp(Density, 0.25, 4.0),
            Phase = Math.Clamp(Phase, 0.0, 1.0),
            LightAngle = angle,
        };
    }

    public static MandelbulbBookmarkEntry FromFlyLook(
        string name,
        string category,
        Vector3 position,
        float yawDeg,
        float pitchDeg,
        float rollDeg,
        double power = 8.0,
        int iterations = 10,
        double detail = 1.0,
        double aoStrength = 0.78,
        bool softShadows = true,
        bool julia = false,
        Vector3? juliaC = null,
        string palette = "Classic",
        double density = 1.0,
        double phase = 0.0,
        double lightAngle = 135.0,
        float fov = 42.0f,
        BulbShape shape = BulbShape.Mandelbulb,
        double boxScale = 2.0,
        double boxMinRadius = 0.5,
        double boxFoldLimit = 1.0,
        double lightElevation = 35.0,
        bool headlamp = false,
        double fogDensity = 0.0,
        double roughness = 0.42,
        double metallic = 0.12,
        double glowStrength = 0.0)
    {
        const float degToRad = MathF.PI / 180f;
        CameraPose pose = CameraPose.FromYawPitchRoll(position, yawDeg * degToRad, pitchDeg * degToRad, rollDeg * degToRad, fov);
        Vector3 jc = juliaC ?? new Vector3(-0.40f, 0.60f, 0.20f);
        return new MandelbulbBookmarkEntry
        {
            Name = name,
            Curated = true,
            Category = category,
            CameraMode = BulbCameraMode.Fly,
            PosX = pose.Position.X,
            PosY = pose.Position.Y,
            PosZ = pose.Position.Z,
            Qx = pose.Orientation.X,
            Qy = pose.Orientation.Y,
            Qz = pose.Orientation.Z,
            Qw = pose.Orientation.W,
            Fov = pose.FovDegrees,
            Shape = shape,
            Power = power,
            BoxScale = boxScale,
            BoxMinRadius = boxMinRadius,
            BoxFoldLimit = boxFoldLimit,
            Iterations = iterations,
            Detail = detail,
            AoStrength = aoStrength,
            SoftShadows = softShadows,
            Julia = julia,
            JuliaX = jc.X,
            JuliaY = jc.Y,
            JuliaZ = jc.Z,
            LightElevation = lightElevation,
            Headlamp = headlamp,
            FogDensity = fogDensity,
            Roughness = roughness,
            Metallic = metallic,
            GlowStrength = glowStrength,
            Palette = palette,
            Density = density,
            Phase = phase,
            LightAngle = lightAngle,
        };
    }

    public static MandelbulbBookmarkEntry FromCurrent(
        string name,
        BulbCameraMode cameraMode,
        OrbitCameraState orbit,
        CameraPose flyPose,
        float flySpeed,
        MandelbulbParams bulb,
        ColoringParams coloring)
    {
        return new MandelbulbBookmarkEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? BuildFallbackName(bulb, cameraMode) : name.Trim(),
            Curated = false,
            Category = "Saved",
            CameraMode = cameraMode,
            TargetX = orbit.Target.X,
            TargetY = orbit.Target.Y,
            TargetZ = orbit.Target.Z,
            Yaw = orbit.Yaw,
            Pitch = orbit.Pitch,
            Distance = orbit.Distance,
            PosX = flyPose.Position.X,
            PosY = flyPose.Position.Y,
            PosZ = flyPose.Position.Z,
            Qx = flyPose.Orientation.X,
            Qy = flyPose.Orientation.Y,
            Qz = flyPose.Orientation.Z,
            Qw = flyPose.Orientation.W,
            FlySpeed = flySpeed,
            Fov = cameraMode == BulbCameraMode.Fly ? flyPose.FovDegrees : orbit.FovDegrees,
            Shape = bulb.Shape,
            Power = bulb.Power,
            BoxScale = bulb.BoxScale,
            BoxMinRadius = bulb.BoxMinRadius,
            BoxFoldLimit = bulb.BoxFoldLimit,
            Iterations = bulb.Iterations,
            Detail = bulb.Detail,
            AoStrength = bulb.AoStrength,
            SoftShadows = bulb.SoftShadows,
            Julia = bulb.Julia,
            JuliaX = bulb.JuliaC.X,
            JuliaY = bulb.JuliaC.Y,
            JuliaZ = bulb.JuliaC.Z,
            LightElevation = bulb.LightElevation,
            Headlamp = bulb.Headlamp,
            FogDensity = bulb.FogDensity,
            Roughness = bulb.Roughness,
            Metallic = bulb.Metallic,
            GlowStrength = bulb.GlowStrength,
            DofEnabled = bulb.DofEnabled,
            DofAperture = bulb.DofAperture,
            DofAutoFocus = bulb.DofAutoFocus,
            DofFocusDistance = bulb.DofFocusDistance,
            Palette = Coloring.Palette.NameOf(coloring.Palette),
            Density = coloring.Density,
            Phase = coloring.Phase,
            LightAngle = coloring.LightAngle,
        };
    }

    public static string BuildFallbackName(MandelbulbParams bulb, BulbCameraMode mode)
    {
        string kind;
        if (bulb.Shape == BulbShape.Mandelbox)
        {
            string scaleText = bulb.BoxScale.ToString("0.##", CultureInfo.InvariantCulture);
            kind = bulb.Julia ? $"Julia Box s={scaleText}" : $"Mandelbox s={scaleText}";
        }
        else
        {
            string powerText = bulb.Power.ToString("0.##", CultureInfo.InvariantCulture);
            kind = bulb.Julia ? $"Julia Bulb n={powerText}" : $"Mandelbulb n={powerText}";
        }

        return mode == BulbCameraMode.Fly ? $"{kind} (Fly)" : kind;
    }

    public static string BuildCliCommand(
        BulbCameraMode mode,
        OrbitCameraState orbit,
        CameraPose flyPose,
        MandelbulbParams bulb,
        ColoringParams coloring,
        SsaaMode ssaa)
    {
        var sb = new StringBuilder();
        sb.Append(bulb.Shape == BulbShape.Mandelbox
            ? "dotnet run -c Release --project src/Mandelbrot.Visualizer.csproj -- --mandelbox"
            : "dotnet run -c Release --project src/Mandelbrot.Visualizer.csproj -- --mandelbulb");

        if (mode == BulbCameraMode.Fly)
        {
            flyPose.GetEulerDegrees(out float yaw, out float pitch, out float roll);
            sb.Append(" --fly");
            sb.Append($" --bulb-pos {Fmt(flyPose.Position.X)},{Fmt(flyPose.Position.Y)},{Fmt(flyPose.Position.Z)}");
            sb.Append($" --bulb-ypr {Fmt(yaw)},{Fmt(pitch)},{Fmt(roll)}");
        }
        else
        {
            sb.Append($" --bulb-camera {Fmt(orbit.Yaw)},{Fmt(orbit.Pitch)},{Fmt(orbit.Distance)}");
            if (orbit.Target.LengthSquared() > 1e-6f)
                sb.Append($" --bulb-target {Fmt(orbit.Target.X)},{Fmt(orbit.Target.Y)},{Fmt(orbit.Target.Z)}");
        }

        if (bulb.Shape == BulbShape.Mandelbox)
        {
            if (Math.Abs(bulb.BoxScale - 2.0) > 1e-4)
                sb.Append($" --box-scale {bulb.BoxScale.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (Math.Abs(bulb.BoxMinRadius - 0.5) > 1e-4)
                sb.Append($" --box-min-radius {bulb.BoxMinRadius.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (Math.Abs(bulb.BoxFoldLimit - 1.0) > 1e-4)
                sb.Append($" --box-fold {bulb.BoxFoldLimit.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (bulb.Iterations != 14)
                sb.Append($" --iter {bulb.Iterations}");
        }
        else
        {
            if (Math.Abs(bulb.Power - 8.0) > 1e-6)
                sb.Append($" --power {bulb.Power.ToString("0.####", CultureInfo.InvariantCulture)}");
            if (bulb.Iterations != 10)
                sb.Append($" --iter {bulb.Iterations}");
        }

        if (Math.Abs(bulb.Detail - 1.0) > 1e-3)
            sb.Append($" --bulb-detail {bulb.Detail.ToString("0.##", CultureInfo.InvariantCulture)}");

        if (!bulb.SoftShadows)
            sb.Append(" --no-shadows");

        if (bulb.Julia)
            sb.Append($" --bulb-julia {Fmt(bulb.JuliaC.X)},{Fmt(bulb.JuliaC.Y)},{Fmt(bulb.JuliaC.Z)}");

        string paletteName = Coloring.Palette.NameOf(coloring.Palette);
        if (!string.Equals(paletteName, "Classic", StringComparison.OrdinalIgnoreCase))
            sb.Append($" --palette {paletteName}");

        if (Math.Abs(coloring.Density - 1.0) > 1e-3)
            sb.Append($" --density {coloring.Density.ToString("0.###", CultureInfo.InvariantCulture)}");

        if (Math.Abs(coloring.Phase) > 1e-3)
            sb.Append($" --phase {coloring.Phase.ToString("0.###", CultureInfo.InvariantCulture)}");

        if (Math.Abs(coloring.LightAngle - 135.0) > 0.5)
            sb.Append($" --angle {coloring.LightAngle.ToString("0.#", CultureInfo.InvariantCulture)}");

        if (Math.Abs(bulb.LightElevation - 35.0) > 0.5)
            sb.Append($" --light-el {bulb.LightElevation.ToString("0.#", CultureInfo.InvariantCulture)}");

        if (bulb.Headlamp)
            sb.Append(" --headlamp");

        if (bulb.FogDensity > 0.005)
            sb.Append($" --fog {bulb.FogDensity.ToString("0.##", CultureInfo.InvariantCulture)}");

        if (Math.Abs(bulb.Roughness - 0.42) > 0.01)
            sb.Append($" --roughness {bulb.Roughness.ToString("0.##", CultureInfo.InvariantCulture)}");

        if (Math.Abs(bulb.Metallic - 0.12) > 0.01)
            sb.Append($" --metallic {bulb.Metallic.ToString("0.##", CultureInfo.InvariantCulture)}");

        if (bulb.GlowStrength > 0.005)
            sb.Append($" --glow {bulb.GlowStrength.ToString("0.##", CultureInfo.InvariantCulture)}");

        if (bulb.DofEnabled)
        {
            sb.Append($" --dof {bulb.DofAperture.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (!bulb.DofAutoFocus)
                sb.Append($" --focus {bulb.DofFocusDistance.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        if (bulb.Quality != BulbQuality.Balanced)
            sb.Append($" --bulb-quality {bulb.Quality.ToString().ToLowerInvariant()}");

        if (ssaa != SsaaMode.Off)
            sb.Append($" --ssaa {SsaaPattern.ShortLabel(ssaa)}");

        return sb.ToString();
    }

    static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
