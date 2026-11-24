using System.Numerics;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

public enum BulbCameraMode
{
    Orbit = 0,
    Fly = 1,
}

/// <summary>
/// Gimbal-lock-free 6DOF camera pose using a right-handed OpenGL camera convention:
/// local +X = right, local +Y = up, local -Z = forward.
/// </summary>
public readonly record struct CameraPose(
    Vector3 Position,
    Quaternion Orientation,
    float FovDegrees)
{
    public const float MinFov = 20f;
    public const float MaxFov = 95f;

    public void GetBasis(out Vector3 position, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        Quaternion q = Quaternion.Normalize(Orientation);
        position = Position;
        right = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, q));
        up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, q));
        forward = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, q));
    }

    public static Quaternion CreateOrientationFromBasis(Vector3 right, Vector3 up, Vector3 forward)
    {
        Vector3 r = Vector3.Normalize(right);
        Vector3 f = Vector3.Normalize(forward);
        Vector3 u = Vector3.Normalize(Vector3.Cross(r, f));
        r = Vector3.Normalize(Vector3.Cross(f, u));

        // System.Numerics transforms row vectors (v * M), so rows 1..3 are local +X, +Y, +Z (-forward).
        var m = new Matrix4x4(
            r.X, r.Y, r.Z, 0f,
            u.X, u.Y, u.Z, 0f,
            -f.X, -f.Y, -f.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    public static CameraPose FromLookAt(Vector3 position, Vector3 target, Vector3 worldUp, float fovDegrees = 42f)
    {
        Vector3 forward = target - position;
        if (forward.LengthSquared() < 1e-10f)
            forward = -Vector3.UnitZ;
        else
            forward = Vector3.Normalize(forward);

        Vector3 upRef = worldUp.LengthSquared() < 1e-10f ? Vector3.UnitY : Vector3.Normalize(worldUp);
        if (MathF.Abs(Vector3.Dot(forward, upRef)) > 0.999f)
            upRef = Vector3.UnitZ;

        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, upRef));
        Vector3 up = Vector3.Cross(right, forward);
        return new CameraPose(position, CreateOrientationFromBasis(right, up, forward), Math.Clamp(fovDegrees, MinFov, MaxFov));
    }

    public static CameraPose FromYawPitchRoll(Vector3 position, float yawRad, float pitchRad, float rollRad, float fovDegrees = 42f)
    {
        // Intrinsic Y-X-Z (yaw around +Y, pitch around local +X, roll around local +Z).
        Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawRad)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchRad)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rollRad);
        return new CameraPose(position, Quaternion.Normalize(q), Math.Clamp(fovDegrees, MinFov, MaxFov));
    }

    /// <summary>
    /// Applies intrinsic (body-frame / local-axis) 6DOF rotation:
    /// +yaw turns left around local +Y, +pitch tilts nose up around local +X,
    /// +roll banks CCW around local +Z (-forward).
    /// </summary>
    public CameraPose RotateLocal(float yawRad, float pitchRad, float rollRad)
    {
        Quaternion q = Quaternion.Normalize(Orientation);
        if (yawRad != 0f)
            q *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawRad);
        if (pitchRad != 0f)
            q *= Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchRad);
        if (rollRad != 0f)
            q *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rollRad);
        return this with { Orientation = Quaternion.Normalize(q) };
    }

    /// <summary>
    /// Translates along the camera's local basis axes (right, up, forward).
    /// </summary>
    public CameraPose TranslateLocal(float rightDelta, float upDelta, float forwardDelta)
    {
        GetBasis(out _, out Vector3 forward, out Vector3 right, out Vector3 up);
        Vector3 nextPos = Position + right * rightDelta + up * upDelta + forward * forwardDelta;
        return this with { Position = nextPos };
    }

    /// <summary>
    /// Levels the horizon (zeroes roll relative to world +Y) while preserving look direction.
    /// </summary>
    public CameraPose LevelHorizon()
    {
        GetBasis(out _, out Vector3 forward, out _, out Vector3 currentUp);
        Vector3 upRef = MathF.Abs(forward.Y) > 0.995f ? currentUp : Vector3.UnitY;
        return FromLookAt(Position, Position + forward, upRef, FovDegrees);
    }

    /// <summary>
    /// Extracts approximate world-relative Yaw, Pitch, Roll in degrees for telemetry display.
    /// </summary>
    public void GetEulerDegrees(out float yawDeg, out float pitchDeg, out float rollDeg)
    {
        GetBasis(out _, out Vector3 forward, out Vector3 right, out Vector3 up);
        float pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
        float yaw = MathF.Atan2(-forward.X, -forward.Z);

        Vector3 levelRight = Vector3.Cross(forward, Vector3.UnitY);
        float roll;
        if (levelRight.LengthSquared() > 1e-6f)
        {
            levelRight = Vector3.Normalize(levelRight);
            Vector3 levelUp = Vector3.Cross(levelRight, forward);
            roll = MathF.Atan2(Vector3.Dot(right, levelUp), Vector3.Dot(right, levelRight));
        }
        else
        {
            roll = 0f;
        }

        const float radToDeg = 180f / MathF.PI;
        yawDeg = yaw * radToDeg;
        pitchDeg = pitch * radToDeg;
        rollDeg = roll * radToDeg;
    }
}

/// <summary>
/// Interactive 6DOF spacecraft-style fly camera for exploring the 3D Mandelbulb
/// from any angle, roll, or close-up canyon without gimbal lock.
/// </summary>
public sealed class FlyCamera
{
    public const float MinSpeed = 0.05f;
    public const float MaxSpeed = 6.0f;
    const double InteractionGraceSeconds = 0.22;
    const float MaxPositionRadius = 48.0f;
    const float LinearAccelRate = 7.5f;
    const float LinearDecelRate = 10.0f;
    const float AngularAccelRate = 9.0f;
    const float AngularDecelRate = 12.0f;
    const float MotionStopEpsilon = 0.002f;

    bool _looking;
    bool _rolling;
    bool _strafing;
    Vector2 _lastMouse;
    double _lastInteractTime = -10;
    Vector3 _linearVelocity;  // Smoothed local linear velocity factor (X=right, Y=up, Z=forward)
    Vector3 _angularVelocity; // Smoothed local angular velocity in rad/s (X=pitch, Y=yaw, Z=roll)

    public CameraPose Pose { get; private set; } = OrbitCameraState.Default.ToPose();
    public float Speed { get; private set; } = 1.0f;
    public bool AutoSdfSpeed { get; set; } = true;
    public float LastSurfaceDistance { get; private set; } = 2.0f;
    public Vector3 LinearVelocity => _linearVelocity;
    public Vector3 AngularVelocity => _angularVelocity;
    public bool HasActiveMotion => _linearVelocity != Vector3.Zero || _angularVelocity != Vector3.Zero;

    public bool IsInteracting =>
        _looking
        || _rolling
        || _strafing
        || HasActiveMotion
        || Raylib.GetTime() - _lastInteractTime < InteractionGraceSeconds;

    public void Touch() => _lastInteractTime = Raylib.GetTime();

    public void StopMotion()
    {
        _linearVelocity = Vector3.Zero;
        _angularVelocity = Vector3.Zero;
    }

    public void SetSpeed(float speed)
    {
        Speed = Math.Clamp(speed, MinSpeed, MaxSpeed);
    }

    public void SetPose(CameraPose pose)
    {
        Vector3 pos = pose.Position;
        if (pos.Length() > MaxPositionRadius)
            pos = Vector3.Normalize(pos) * MaxPositionRadius;

        Quaternion q = pose.Orientation.LengthSquared() < 1e-10f
            ? Quaternion.Identity
            : Quaternion.Normalize(pose.Orientation);

        Pose = new CameraPose(pos, q, Math.Clamp(pose.FovDegrees, CameraPose.MinFov, CameraPose.MaxFov));
        StopMotion();
    }

    public void Reset(BulbShape shape = BulbShape.Mandelbulb)
    {
        Pose = OrbitCameraState.DefaultFor(shape).ToPose();
        Speed = 1.0f;
        StopMotion();
        CancelPointerInteraction();
        Touch();
    }

    public void LevelHorizon()
    {
        Pose = Pose.LevelHorizon();
        _angularVelocity = Vector3.Zero;
        Touch();
    }

    public void CancelPointerInteraction()
    {
        _looking = false;
        _rolling = false;
        _strafing = false;
    }

    /// <summary>
    /// Computes the current linear movement speed, optionally scaled by the local
    /// SDF distance estimate so approaching intricate fractal canyons automatically decelerates.
    /// </summary>
    public float ComputeEffectiveSpeed(in MandelbulbParams parameters)
    {
        float de = MathF.Max(0f, MandelbulbMath.EstimateDistance(Pose.Position, parameters));
        LastSurfaceDistance = de;

        if (!AutoSdfSpeed)
            return Speed * (parameters.Shape == BulbShape.Mandelbox ? 2.5f : 1.0f);

        // Smoothly adapt speed from fine canyon detail (0.004) up to open-space cruise scaled by scene size.
        float maxAdaptive = MathF.Max(2.4f, parameters.BoundingRadius * 1.1f);
        float adaptive = Math.Clamp(de * 1.15f, 0.004f, maxAdaptive);
        return Speed * adaptive;
    }

    public bool HandlePointer(int canvasWidth, int canvasHeight, in MandelbulbParams parameters)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        bool onCanvas = mouse.X >= 0 && mouse.X < canvasWidth && mouse.Y >= 0 && mouse.Y < canvasHeight;
        bool shift = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
        bool changed = false;

        if (onCanvas && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
        {
            _lastMouse = mouse;
            if (shift)
                _strafing = true;
            else
                _looking = true;
        }

        if (onCanvas && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_RIGHT))
        {
            _lastMouse = mouse;
            _rolling = true;
        }

        if (onCanvas && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_MIDDLE))
        {
            _lastMouse = mouse;
            _strafing = true;
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT))
        {
            _looking = false;
            if (!Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_MIDDLE))
                _strafing = false;
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_RIGHT))
            _rolling = false;

        if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_MIDDLE))
            _strafing = false;

        if (_looking && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
        {
            Vector2 delta = mouse - _lastMouse;
            if (delta != Vector2.Zero)
            {
                float sensitivity = 2.6f / Math.Max(canvasHeight, 1);
                Pose = Pose.RotateLocal(yawRad: -delta.X * sensitivity, pitchRad: -delta.Y * sensitivity, rollRad: 0f);
                _lastMouse = mouse;
                Touch();
                changed = true;
            }
        }
        else if (_rolling && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_RIGHT))
        {
            Vector2 delta = mouse - _lastMouse;
            if (delta != Vector2.Zero)
            {
                float rollSens = 3.0f / Math.Max(canvasHeight, 1);
                float effectiveSpeed = ComputeEffectiveSpeed(parameters);
                float dollySens = effectiveSpeed * 2.2f / Math.Max(canvasHeight, 1);

                Pose = Pose.RotateLocal(0f, 0f, rollRad: -delta.X * rollSens);
                if (MathF.Abs(delta.Y) > 0f)
                {
                    Pose = Pose.TranslateLocal(0f, 0f, forwardDelta: -delta.Y * dollySens);
                    ClampPosition();
                }

                _lastMouse = mouse;
                Touch();
                changed = true;
            }
        }
        else if (_strafing && (Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_MIDDLE)
                               || Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT)))
        {
            Vector2 delta = mouse - _lastMouse;
            if (delta != Vector2.Zero)
            {
                float effectiveSpeed = ComputeEffectiveSpeed(parameters);
                float strafeSens = effectiveSpeed * 1.8f / Math.Max(canvasHeight, 1);
                Pose = Pose.TranslateLocal(rightDelta: -delta.X * strafeSens, upDelta: delta.Y * strafeSens, forwardDelta: 0f);
                ClampPosition();
                _lastMouse = mouse;
                Touch();
                changed = true;
            }
        }

        if (onCanvas)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (MathF.Abs(wheel) > 0.001f)
            {
                SetSpeed(Speed * MathF.Pow(1.18f, wheel));
                changed = true;
            }
        }

        return changed;
    }

    public bool HandleKeys(in MandelbulbParams parameters)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_R))
        {
            Reset(parameters.Shape);
            return true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_X))
        {
            LevelHorizon();
            return true;
        }

        float dt = Math.Clamp(Raylib.GetFrameTime(), 1f / 240f, 0.05f);
        bool boost = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
        bool slow = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_ALT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_ALT);
        float speedMod = boost ? 3.0f : (slow ? 0.25f : 1.0f);

        float moveFwd = 0f;
        float moveRight = 0f;
        float moveUp = 0f;

        if (Raylib.IsKeyDown(KeyboardKey.KEY_W)) moveFwd += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_S)) moveFwd -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_D)) moveRight += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_A)) moveRight -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_SPACE)) moveUp += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_CONTROL) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_CONTROL)) moveUp -= 1f;

        float yawInput = 0f;
        float pitchInput = 0f;
        float rollInput = 0f;

        if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT)) yawInput += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT)) yawInput -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_UP)) pitchInput += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_DOWN)) pitchInput -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_Q)) rollInput += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_E)) rollInput -= 1f;

        var localMove = new Vector3(moveRight, moveUp, moveFwd);
        var localRotate = new Vector3(pitchInput, yawInput, rollInput);
        bool moved = StepKeyboardMotion(localMove, localRotate, speedMod, slow, dt, parameters);
        if (moved)
            Touch();

        return moved;
    }

    /// <summary>
    /// Integrates smoothed keyboard acceleration and deceleration for both 3-axis translation
    /// and 3-axis rotation (mouse pointer look remains direct/instant in HandlePointer).
    /// </summary>
    public bool StepKeyboardMotion(
        Vector3 localMoveInput,
        Vector3 localRotateInput,
        float speedMod,
        bool slowRotate,
        float dt,
        in MandelbulbParams parameters)
    {
        if (localMoveInput.LengthSquared() > 1f)
            localMoveInput = Vector3.Normalize(localMoveInput);

        Vector3 targetLinear = localMoveInput * speedMod;
        float targetRotSpeed = slowRotate ? 0.55f : 1.35f;
        Vector3 targetAngular = localRotateInput * targetRotSpeed;

        _linearVelocity = SmoothVector(_linearVelocity, targetLinear, LinearAccelRate, LinearDecelRate, MotionStopEpsilon, dt);
        _angularVelocity = SmoothVector(_angularVelocity, targetAngular, AngularAccelRate, AngularDecelRate, MotionStopEpsilon, dt);

        bool moved = false;

        if (_angularVelocity != Vector3.Zero)
        {
            Pose = Pose.RotateLocal(
                yawRad: _angularVelocity.Y * dt,
                pitchRad: _angularVelocity.X * dt,
                rollRad: _angularVelocity.Z * dt);
            moved = true;
        }

        if (_linearVelocity != Vector3.Zero)
        {
            float baseDist = ComputeEffectiveSpeed(parameters) * dt;
            Pose = Pose.TranslateLocal(
                rightDelta: _linearVelocity.X * baseDist,
                upDelta: _linearVelocity.Y * baseDist,
                forwardDelta: _linearVelocity.Z * baseDist);
            ClampPosition();
            moved = true;
        }

        return moved;
    }

    static Vector3 SmoothVector(Vector3 current, Vector3 target, float accelRate, float decelRate, float stopEps, float dt)
    {
        return new Vector3(
            SmoothAxis(current.X, target.X, accelRate, decelRate, stopEps, dt),
            SmoothAxis(current.Y, target.Y, accelRate, decelRate, stopEps, dt),
            SmoothAxis(current.Z, target.Z, accelRate, decelRate, stopEps, dt));
    }

    static float SmoothAxis(float current, float target, float accelRate, float decelRate, float stopEps, float dt)
    {
        bool accelerating = MathF.Abs(target) > MathF.Abs(current) && current * target >= 0f;
        float rate = accelerating ? accelRate : decelRate;
        float blend = 1f - MathF.Exp(-rate * dt);
        float next = current + (target - current) * blend;
        if (target == 0f && MathF.Abs(next) < stopEps)
            return 0f;
        return next;
    }

    /// <summary>
    /// Converts the current 6DOF pose into an OrbitCameraState targeting the fractal
    /// surface directly ahead along the camera's forward ray (for seamless Fly -> Orbit switching).
    /// </summary>
    public OrbitCameraState ToOrbitState(in MandelbulbParams parameters)
    {
        Pose.GetBasis(out Vector3 pos, out Vector3 forward, out _, out _);

        // Probe along the view ray to anchor the orbit pivot on the fractal feature ahead.
        float hitDist = ProbeForwardDistance(pos, forward, parameters);
        Vector3 target = pos + forward * hitDist;
        Vector3 offset = Vector3.Normalize(pos - target);
        float pitch = Math.Clamp(MathF.Asin(Math.Clamp(offset.Y, -1f, 1f)), -OrbitCameraState.MaxPitch, OrbitCameraState.MaxPitch);
        float yaw = MathF.Atan2(offset.X, offset.Z);

        return new OrbitCameraState(
            Target: target,
            Yaw: yaw,
            Pitch: pitch,
            Distance: Math.Clamp(hitDist, OrbitCameraState.MinDistance, OrbitCameraState.MaxDistance),
            FovDegrees: Math.Clamp(Pose.FovDegrees, 20f, 80f));
    }

    static float ProbeForwardDistance(Vector3 origin, Vector3 forward, in MandelbulbParams parameters)
    {
        float maxProbe = MathF.Max(6.0f, parameters.BoundingRadius * 2.2f);
        float t = 0.002f;
        for (int i = 0; i < 80; i++)
        {
            float d = MandelbulbMath.EstimateDistance(origin + forward * t, parameters);
            if (d < 0.001f)
                return Math.Clamp(t, 0.08f, maxProbe);
            t += MathF.Max(d * 0.85f, 0.002f);
            if (t > maxProbe)
                break;
        }

        // If looking into open space, orbit around the distance to origin (or a comfortable default ahead).
        float toOrigin = origin.Length();
        float fallback = parameters.Shape == BulbShape.Mandelbox ? 10.0f : 2.0f;
        return Math.Clamp(toOrigin > 0.2f && toOrigin < maxProbe ? toOrigin : fallback, 0.25f, maxProbe);
    }

    void ClampPosition()
    {
        float len = Pose.Position.Length();
        if (len > MaxPositionRadius)
            Pose = Pose with { Position = Pose.Position * (MaxPositionRadius / len) };
    }
}
