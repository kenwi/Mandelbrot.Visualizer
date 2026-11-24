using System.Numerics;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

public readonly record struct OrbitCameraState(
    Vector3 Target,
    float Yaw,
    float Pitch,
    float Distance,
    float FovDegrees)
{
    public const float MinDistance = 0.0005f;
    public const float MaxDistance = 42.0f;
    public const float MaxPitch = 1.52f;

    public static OrbitCameraState Default => new(
        Target: Vector3.Zero,
        Yaw: 0.65f,
        Pitch: 0.36f,
        Distance: 3.25f,
        FovDegrees: 42.0f);

    public static OrbitCameraState DefaultMandelbox => new(
        Target: Vector3.Zero,
        Yaw: 0.62f,
        Pitch: 0.42f,
        Distance: 24.0f,
        FovDegrees: 42.0f);

    public static OrbitCameraState DefaultFor(BulbShape shape)
        => shape == BulbShape.Mandelbox ? DefaultMandelbox : Default;

    public void GetBasis(out Vector3 position, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        float cosP = MathF.Cos(Pitch);
        float sinP = MathF.Sin(Pitch);
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        var offset = new Vector3(cosP * sinY, sinP, cosP * cosY);
        position = Target + offset * Distance;
        forward = Vector3.Normalize(Target - position);
        right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        up = Vector3.Cross(right, forward);
    }

    public CameraPose ToPose()
    {
        GetBasis(out Vector3 pos, out Vector3 forward, out Vector3 right, out Vector3 up);
        return new CameraPose(pos, CameraPose.CreateOrientationFromBasis(right, up, forward), FovDegrees);
    }
}

/// <summary>
/// Interactive 3D orbit/pan/dolly camera with interaction grace tracking so the
/// raymarcher renders quickly while dragging and snaps to full detail when idle.
/// </summary>
public sealed class OrbitCamera
{
    const double InteractionGraceSeconds = 0.22;
    const double DoubleClickWindowSeconds = 0.35;
    const float DoubleClickDistance = 8f;

    bool _orbiting;
    bool _panning;
    Vector2 _lastMouse;
    double _lastClickTime = -10;
    Vector2 _lastClickPos;
    double _lastInteractTime = -10;

    public OrbitCameraState State { get; private set; } = OrbitCameraState.Default;

    public bool IsInteracting =>
        _orbiting
        || _panning
        || Raylib.GetTime() - _lastInteractTime < InteractionGraceSeconds;

    public void Touch() => _lastInteractTime = Raylib.GetTime();

    public void Reset(BulbShape shape = BulbShape.Mandelbulb)
    {
        State = OrbitCameraState.DefaultFor(shape);
        CancelPointerInteraction();
        Touch();
    }

    public void SetState(OrbitCameraState state)
    {
        State = state with
        {
            Pitch = Math.Clamp(state.Pitch, -OrbitCameraState.MaxPitch, OrbitCameraState.MaxPitch),
            Distance = Math.Clamp(state.Distance, OrbitCameraState.MinDistance, OrbitCameraState.MaxDistance),
            FovDegrees = Math.Clamp(state.FovDegrees, 20f, 80f),
        };
    }

    public void CancelPointerInteraction()
    {
        _orbiting = false;
        _panning = false;
    }

    public bool HandlePointer(int canvasWidth, int canvasHeight)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        bool onCanvas = mouse.X >= 0 && mouse.X < canvasWidth && mouse.Y >= 0 && mouse.Y < canvasHeight;
        bool shift = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
        bool changed = false;

        if (onCanvas && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
        {
            double now = Raylib.GetTime();
            if (!shift && now - _lastClickTime <= DoubleClickWindowSeconds && Vector2.Distance(mouse, _lastClickPos) <= DoubleClickDistance)
            {
                float factor = 0.55f;
                State = State with { Distance = Math.Clamp(State.Distance * factor, OrbitCameraState.MinDistance, OrbitCameraState.MaxDistance) };
                _lastClickTime = -10;
                _orbiting = false;
                _panning = false;
                Touch();
                return true;
            }

            _lastClickTime = now;
            _lastClickPos = mouse;
            _lastMouse = mouse;
            if (shift)
                _panning = true;
            else
                _orbiting = true;
        }

        if (onCanvas && (Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_RIGHT) || Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_MIDDLE)))
        {
            _panning = true;
            _lastMouse = mouse;
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT))
        {
            _orbiting = false;
            if (shift) _panning = false;
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_RIGHT) || Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_MIDDLE))
            _panning = false;

        if (_orbiting && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
        {
            Vector2 delta = mouse - _lastMouse;
            if (delta != Vector2.Zero)
            {
                float sensitivity = 3.2f / Math.Max(canvasHeight, 1);
                State = State with
                {
                    Yaw = State.Yaw - delta.X * sensitivity,
                    Pitch = Math.Clamp(State.Pitch + delta.Y * sensitivity, -OrbitCameraState.MaxPitch, OrbitCameraState.MaxPitch),
                };
                _lastMouse = mouse;
                Touch();
                changed = true;
            }
        }
        else if (_panning && (Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_RIGHT)
                              || Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_MIDDLE)
                              || Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT)))
        {
            Vector2 delta = mouse - _lastMouse;
            if (delta != Vector2.Zero)
            {
                State.GetBasis(out _, out _, out Vector3 right, out Vector3 up);
                float panScale = State.Distance * 1.1f / Math.Max(canvasHeight, 1);
                Vector3 nextTarget = State.Target - right * (delta.X * panScale) + up * (delta.Y * panScale);
                State = State with { Target = nextTarget };
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
                float factor = MathF.Pow(0.86f, wheel);
                State = State with { Distance = Math.Clamp(State.Distance * factor, OrbitCameraState.MinDistance, OrbitCameraState.MaxDistance) };
                Touch();
                changed = true;
            }
        }

        return changed;
    }

    public bool HandleKeys(BulbShape shape = BulbShape.Mandelbulb)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_R))
        {
            Reset(shape);
            return true;
        }

        bool shift = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
        float dt = Math.Clamp(Raylib.GetFrameTime(), 1f / 240f, 0.05f);
        bool changed = false;

        if (shift)
        {
            if (Raylib.IsKeyDown(KeyboardKey.KEY_UP))
            {
                State = State with { Distance = Math.Clamp(State.Distance * MathF.Pow(0.35f, dt), OrbitCameraState.MinDistance, OrbitCameraState.MaxDistance) };
                Touch();
                changed = true;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_DOWN))
            {
                State = State with { Distance = Math.Clamp(State.Distance * MathF.Pow(2.85f, dt), OrbitCameraState.MinDistance, OrbitCameraState.MaxDistance) };
                Touch();
                changed = true;
            }
        }
        else
        {
            float rotSpeed = 1.4f * dt;
            if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT))
            {
                State = State with { Yaw = State.Yaw - rotSpeed };
                Touch();
                changed = true;
            }
            if (Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT))
            {
                State = State with { Yaw = State.Yaw + rotSpeed };
                Touch();
                changed = true;
            }
            if (Raylib.IsKeyDown(KeyboardKey.KEY_UP))
            {
                State = State with { Pitch = Math.Clamp(State.Pitch + rotSpeed, -OrbitCameraState.MaxPitch, OrbitCameraState.MaxPitch) };
                Touch();
                changed = true;
            }
            if (Raylib.IsKeyDown(KeyboardKey.KEY_DOWN))
            {
                State = State with { Pitch = Math.Clamp(State.Pitch - rotSpeed, -OrbitCameraState.MaxPitch, OrbitCameraState.MaxPitch) };
                Touch();
                changed = true;
            }
        }

        return changed;
    }
}
