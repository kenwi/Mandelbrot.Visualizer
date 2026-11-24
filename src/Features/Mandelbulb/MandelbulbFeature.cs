using System.Globalization;
using System.Numerics;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

/// <summary>
/// Vertical slice owning 3D SDF raymarched Mandelbulb mode: parameters, Orbit and
/// 6DOF Fly cameras, GPU raymarcher, keyboard/pointer controls, side-panel section,
/// and status telemetry.
/// </summary>
public sealed class MandelbulbFeature : IDisposable
{
    readonly OrbitCamera _orbitCamera = new();
    readonly FlyCamera _flyCamera = new();
    readonly MandelbulbRenderer _renderer = new();
    readonly MandelbulbBookmarks _bookmarks = new();
    readonly DoubleField _powerField = new();
    readonly DoubleField _boxScaleField = new();
    readonly IntField _iterationsField = new();

    public bool Active { get; private set; }
    public BulbCameraMode CameraMode { get; private set; } = BulbCameraMode.Orbit;
    public bool IsFlyMode => CameraMode == BulbCameraMode.Fly;
    public MandelbulbParams State { get; private set; } = MandelbulbParams.Default;
    public OrbitCameraState Camera => _orbitCamera.State;
    public CameraPose FlyPose => _flyCamera.Pose;
    public float FlySpeed => _flyCamera.Speed;
    public CameraPose ActivePose => IsFlyMode ? _flyCamera.Pose : _orbitCamera.State.ToPose();
    public bool IsInteracting => IsFlyMode ? _flyCamera.IsInteracting : _orbitCamera.IsInteracting;
    public bool IsAvailable => _renderer.IsAvailable;
    public MandelbulbBookmarks Bookmarks => _bookmarks;
    public bool CapturesInput => Active && _bookmarks.CapturesInput;
    public string? SelectedBookmarkName => _bookmarks.SelectedName;

    public void SetActive(bool active)
    {
        if (Active == active) return;
        Active = active;
        _orbitCamera.CancelPointerInteraction();
        _flyCamera.CancelPointerInteraction();
        _flyCamera.StopMotion();
        _bookmarks.DismissPopups();
    }

    public void DismissPopups() => _bookmarks.DismissPopups();

    public void ApplyBookmarkEntry(MandelbulbBookmarkEntry entry)
    {
        SetParams(entry.ToParams());
        _orbitCamera.SetState(entry.ToOrbitState());
        _flyCamera.SetSpeed(entry.FlySpeed);

        if (entry.CameraMode == BulbCameraMode.Fly)
        {
            CameraMode = BulbCameraMode.Fly;
            _flyCamera.SetPose(entry.ToFlyPose());
            _flyCamera.ComputeEffectiveSpeed(State);
        }
        else
        {
            CameraMode = BulbCameraMode.Orbit;
            _flyCamera.SetPose(_orbitCamera.State.ToPose());
        }

        _orbitCamera.CancelPointerInteraction();
        _flyCamera.CancelPointerInteraction();
        _flyCamera.StopMotion();
    }

    public bool ApplyLandmarkByName(string name, ColoringFeature coloring)
        => _bookmarks.ApplyByName(name, this, coloring);

    public bool HandleBookmarkKeys(ColoringFeature coloring)
        => Active && _bookmarks.HandleKeys(this, coloring);

    public void DrawBookmarksPanel(ref PanelLayout layout, ColoringParams coloring, SsaaMode ssaa, TextEditScope editScope)
        => _bookmarks.DrawPanel(ref layout, this, coloring, ssaa, editScope);

    public bool DrawBookmarksOverlay(int screenWidth, int screenHeight, int canvasWidth, ColoringFeature coloring, TextEditScope editScope)
        => Active && _bookmarks.DrawOverlay(screenWidth, screenHeight, canvasWidth, this, coloring, editScope);

    public void SetCameraMode(BulbCameraMode mode)
    {
        if (CameraMode == mode) return;

        if (mode == BulbCameraMode.Fly)
        {
            _flyCamera.SetPose(_orbitCamera.State.ToPose());
            _flyCamera.ComputeEffectiveSpeed(State);
        }
        else
        {
            _orbitCamera.SetState(_flyCamera.ToOrbitState(State));
        }

        CameraMode = mode;
        _orbitCamera.CancelPointerInteraction();
        _flyCamera.CancelPointerInteraction();
        _flyCamera.StopMotion();
    }

    public void SetShape(BulbShape shape, bool resetCamera = true)
    {
        if (State.Shape == shape) return;

        int nextIter = shape == BulbShape.Mandelbox
            ? (State.Iterations == 10 ? 14 : State.Iterations)
            : (State.Iterations == 14 ? 10 : State.Iterations);
        Vector3 nextJuliaC = shape == BulbShape.Mandelbox && State.JuliaC == MandelbulbParams.Default.JuliaC
            ? MandelbulbParams.DefaultMandelbox.JuliaC
            : (shape == BulbShape.Mandelbulb && State.JuliaC == MandelbulbParams.DefaultMandelbox.JuliaC
                ? MandelbulbParams.Default.JuliaC
                : State.JuliaC);

        SetParams(State with { Shape = shape, Iterations = nextIter, JuliaC = nextJuliaC });
        _bookmarks.ClearSelection();

        if (resetCamera)
        {
            OrbitCameraState defCam = OrbitCameraState.DefaultFor(shape);
            _orbitCamera.SetState(defCam);
            if (IsFlyMode)
            {
                _flyCamera.SetPose(defCam.ToPose());
                _flyCamera.ComputeEffectiveSpeed(State);
            }
        }
    }

    public void SetParams(MandelbulbParams parameters)
    {
        State = parameters with
        {
            Power = MandelbulbParams.ClampPower(parameters.Power),
            Iterations = MandelbulbParams.ClampIterations(parameters.Iterations),
            Detail = Math.Clamp(parameters.Detail, MandelbulbParams.DetailMin, MandelbulbParams.DetailMax),
            AoStrength = Math.Clamp(parameters.AoStrength, 0.0, 1.0),
            BoxScale = MandelbulbParams.ClampBoxScale(parameters.BoxScale),
            BoxMinRadius = MandelbulbParams.ClampBoxMinRadius(parameters.BoxMinRadius),
            BoxFoldLimit = MandelbulbParams.ClampBoxFoldLimit(parameters.BoxFoldLimit),
        };
        _powerField.Invalidate();
        _boxScaleField.Invalidate();
        _iterationsField.Invalidate();
    }

    public void SetCamera(OrbitCameraState camera)
    {
        _orbitCamera.SetState(camera);
        if (IsFlyMode)
            _flyCamera.SetPose(_orbitCamera.State.ToPose());
    }

    public void SetFlyPose(CameraPose pose)
    {
        CameraMode = BulbCameraMode.Fly;
        _flyCamera.SetPose(pose);
        _flyCamera.ComputeEffectiveSpeed(State);
    }

    public void ResetView()
    {
        if (IsFlyMode)
            _flyCamera.Reset(State.Shape);
        else
            _orbitCamera.Reset(State.Shape);
    }

    public void StopMotion() => _flyCamera.StopMotion();

    public void Load() => _renderer.Load();

    public bool HandlePointer(int canvasWidth, int canvasHeight)
    {
        if (!Active) return false;
        return IsFlyMode
            ? _flyCamera.HandlePointer(canvasWidth, canvasHeight, State)
            : _orbitCamera.HandlePointer(canvasWidth, canvasHeight);
    }

    public bool HandleKeys(out bool interactive)
    {
        interactive = false;
        bool changed = false;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_THREE))
        {
            SetActive(!Active);
            return true;
        }

        if (!Active)
            return false;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_F))
        {
            SetCameraMode(IsFlyMode ? BulbCameraMode.Orbit : BulbCameraMode.Fly);
            return true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_M))
        {
            SetShape(State.Shape == BulbShape.Mandelbulb ? BulbShape.Mandelbox : BulbShape.Mandelbulb);
            return true;
        }

        changed |= IsFlyMode
            ? _flyCamera.HandleKeys(State)
            : _orbitCamera.HandleKeys(State.Shape);

        bool incParam = Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_BRACKET);
        bool decParam = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_BRACKET);
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_RIGHT_BRACKET))
        {
            if (State.Shape == BulbShape.Mandelbox)
                SetParams(State with { BoxScale = Math.Round(State.BoxScale + 0.1, 2) });
            else
                SetParams(State with { Power = Math.Round(State.Power + 0.5, 2) });
            TouchActiveCamera();
            interactive = true;
            changed = true;
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.KEY_LEFT_BRACKET))
        {
            if (State.Shape == BulbShape.Mandelbox)
                SetParams(State with { BoxScale = Math.Round(State.BoxScale - 0.1, 2) });
            else
                SetParams(State with { Power = Math.Round(State.Power - 0.5, 2) });
            TouchActiveCamera();
            interactive = true;
            changed = true;
        }
        else if (incParam ^ decParam)
        {
            float dt = Math.Clamp(Raylib.GetFrameTime(), 1f / 240f, 0.05f);
            if (State.Shape == BulbShape.Mandelbox)
                SetParams(State with { BoxScale = State.BoxScale + (incParam ? 0.65 : -0.65) * dt });
            else
                SetParams(State with { Power = State.Power + (incParam ? 2.5 : -2.5) * dt });
            TouchActiveCamera();
            interactive = true;
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_MINUS) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_SUBTRACT))
        {
            SetParams(State with { Iterations = State.Iterations - 1 });
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_EQUAL) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_ADD))
        {
            SetParams(State with { Iterations = State.Iterations + 1 });
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_J))
        {
            SetParams(State with { Julia = !State.Julia });
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_L))
        {
            SetParams(State with { SoftShadows = !State.SoftShadows });
            changed = true;
        }

        return changed;
    }

    public void DrawCanvas(
        int canvasWidth,
        int canvasHeight,
        in ColoringParams coloring,
        bool finalQuality,
        SsaaMode ssaa)
    {
        if (!_renderer.IsAvailable)
        {
            Raylib.DrawText("3D Mandelbulb requires GPU shaders.", 24, 24, 20, PanelTheme.TitleText);
            return;
        }

        _renderer.Draw(canvasWidth, canvasHeight, ActivePose, State, coloring, finalQuality, ssaa);
    }

    public bool DrawPanel(ref PanelLayout layout, TextEditScope editScope)
    {
        bool changed = false;

        int shapeIndex = (int)State.Shape;
        if (layout.LabeledCombo("Shape", "Mandelbulb;Mandelbox", ref shapeIndex, labelWidth: 82))
        {
            editScope.CancelAll();
            SetShape((BulbShape)shapeIndex);
            changed = true;
        }

        int modeIndex = (int)CameraMode;
        if (layout.LabeledCombo("Camera", "Orbit;6DOF Fly", ref modeIndex, labelWidth: 82))
        {
            SetCameraMode((BulbCameraMode)modeIndex);
            changed = true;
        }

        if (IsFlyMode)
        {
            changed |= layout.LabeledSlider(
                $"Speed   {_flyCamera.Speed:0.00}x",
                _flyCamera.Speed,
                FlyCamera.MinSpeed,
                FlyCamera.MaxSpeed,
                value => _flyCamera.SetSpeed((float)value));

            // Compact row: Auto SDF speed checkbox on the left + Level horizon button on the right.
            float splitX = layout.X + layout.Width * 0.62f;
            float rowY = layout.Y;
            bool autoSpeed = _flyCamera.AutoSdfSpeed;
            bool autoBefore = autoSpeed;
            RayGui.GuiCheckBox(new Rectangle { x = layout.X, y = rowY + 1, width = 22, height = 22 }, "", ref autoSpeed);
            if (autoSpeed)
                Raylib.DrawRectangle((int)layout.X + 5, (int)rowY + 6, 12, 12, PanelTheme.CheckFill);
            RayGui.GuiLabel(new Rectangle { x = layout.X + 28, y = rowY + 1, width = splitX - layout.X - 32, height = 22 }, "Auto SDF speed");
            if (autoSpeed != autoBefore)
            {
                _flyCamera.AutoSdfSpeed = autoSpeed;
                changed = true;
            }

            if (RayGui.GuiButton(new Rectangle { x = splitX, y = rowY, width = layout.Width - (splitX - layout.X), height = 24 }, "Level (X)") != 0)
            {
                _flyCamera.LevelHorizon();
                changed = true;
            }
            layout.Y += 30;
        }

        if (State.Shape == BulbShape.Mandelbox)
        {
            layout.Label("Box scale");
            changed |= layout.Slider(State.BoxScale, MandelbulbParams.BoxScaleMin, MandelbulbParams.BoxScaleMax, value =>
            {
                SetParams(State with { BoxScale = value });
                TouchActiveCamera();
            });
            changed |= _boxScaleField.Draw(ref layout, editScope, State.BoxScale, value =>
            {
                SetParams(State with { BoxScale = value });
            });

            changed |= layout.LabeledSlider($"Min rad {State.BoxMinRadius:0.00}", State.BoxMinRadius, MandelbulbParams.BoxMinRadiusMin, MandelbulbParams.BoxMinRadiusMax, value =>
            {
                SetParams(State with { BoxMinRadius = value });
                TouchActiveCamera();
            });
            changed |= layout.LabeledSlider($"Fold    {State.BoxFoldLimit:0.00}", State.BoxFoldLimit, MandelbulbParams.BoxFoldLimitMin, MandelbulbParams.BoxFoldLimitMax, value =>
            {
                SetParams(State with { BoxFoldLimit = value });
                TouchActiveCamera();
            });
        }
        else
        {
            layout.Label("Bulb power n");
            changed |= layout.Slider(State.Power, MandelbulbParams.PowerMin, MandelbulbParams.PowerMax, value =>
            {
                SetParams(State with { Power = value });
                TouchActiveCamera();
            });
            changed |= _powerField.Draw(ref layout, editScope, State.Power, value =>
            {
                SetParams(State with { Power = value });
            });
        }

        layout.Label("3D iterations");
        changed |= layout.Slider(State.Iterations, MandelbulbParams.IterationMin, MandelbulbParams.IterationMax, value =>
        {
            SetParams(State with { Iterations = (int)Math.Round(value) });
            TouchActiveCamera();
        });
        changed |= _iterationsField.Draw(ref layout, editScope, State.Iterations, value =>
        {
            SetParams(State with { Iterations = value });
        });

        changed |= layout.LabeledSlider($"Detail  {State.Detail:0.00}", State.Detail, MandelbulbParams.DetailMin, MandelbulbParams.DetailMax, value =>
        {
            SetParams(State with { Detail = value });
            TouchActiveCamera();
        });
        changed |= layout.LabeledSlider($"AO      {State.AoStrength:0.00}", State.AoStrength, 0.0, 1.0, value =>
        {
            SetParams(State with { AoStrength = value });
            TouchActiveCamera();
        });

        bool shadows = State.SoftShadows;
        bool julia = State.Julia;
        if (layout.TwoChecks("Soft shadows", ref shadows, "3D Julia", ref julia, splitRatio: 0.54f))
        {
            SetParams(State with { SoftShadows = shadows, Julia = julia });
            changed = true;
        }

        if (State.Julia)
        {
            double jRange = State.Shape == BulbShape.Mandelbox ? 3.0 : 1.5;
            Vector3 jc = State.JuliaC;
            changed |= layout.LabeledSlider($"Julia X {jc.X:0.00}", jc.X, -jRange, jRange, value =>
            {
                SetParams(State with { JuliaC = jc with { X = (float)value } });
                TouchActiveCamera();
            });
            changed |= layout.LabeledSlider($"Julia Y {jc.Y:0.00}", jc.Y, -jRange, jRange, value =>
            {
                SetParams(State with { JuliaC = jc with { Y = (float)value } });
                TouchActiveCamera();
            });
            changed |= layout.LabeledSlider($"Julia Z {jc.Z:0.00}", jc.Z, -jRange, jRange, value =>
            {
                SetParams(State with { JuliaC = jc with { Z = (float)value } });
                TouchActiveCamera();
            });
        }

        return changed;
    }

    public void DrawTelemetry(ref PanelLayout layout, SsaaMode ssaa, bool showFrameTime)
    {
        if (IsFlyMode)
        {
            CameraPose pose = _flyCamera.Pose;
            pose.GetEulerDegrees(out float yaw, out float pitch, out float roll);
            float de = MathF.Max(0f, MandelbulbMath.EstimateDistance(pose.Position, State));
            layout.Label($"Pos  {pose.Position.X.ToString("0.00", CultureInfo.InvariantCulture)}, {pose.Position.Y.ToString("0.00", CultureInfo.InvariantCulture)}, {pose.Position.Z.ToString("0.00", CultureInfo.InvariantCulture)}");
            layout.Label($"YPR  {yaw:0}d, {pitch:0}d, {roll:0}d   DE {de.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
        else
        {
            OrbitCameraState cam = _orbitCamera.State;
            cam.GetBasis(out Vector3 pos, out _, out _, out _);
            layout.Label($"Cam  {pos.X.ToString("0.00", CultureInfo.InvariantCulture)}, {pos.Y.ToString("0.00", CultureInfo.InvariantCulture)}, {pos.Z.ToString("0.00", CultureInfo.InvariantCulture)}");
            string paramTag = State.Shape == BulbShape.Mandelbox
                ? $"s = {State.BoxScale.ToString("0.##", CultureInfo.InvariantCulture)}"
                : $"n = {State.Power.ToString("0.##", CultureInfo.InvariantCulture)}";
            layout.Label($"Dist {cam.Distance.ToString("0.###", CultureInfo.InvariantCulture)}   {paramTag}");
        }

        layout.Label(StatusLine(ssaa));
        if (showFrameTime && _renderer.HasFrameTime)
            layout.Label($"Frame {_renderer.LastFrameMs:0.0} ms");
    }

    public void DrawHud(SsaaMode ssaa, bool showFrameTime)
    {
        string modeTag = IsFlyMode ? "  [6DOF Fly]" : string.Empty;
        string title = State.Subtitle + modeTag;
        Raylib.DrawText(title, 18, 16, 20, PanelTheme.HudPrimaryText);

        if (IsFlyMode)
        {
            _flyCamera.Pose.GetEulerDegrees(out float yaw, out float pitch, out float roll);
            float de = MathF.Max(0f, MandelbulbMath.EstimateDistance(_flyCamera.Pose.Position, State));
            Raylib.DrawText($"DE {de:0.###}  YPR {yaw:0}d/{pitch:0}d/{roll:0}d   {StatusLine(ssaa)}", 18, 42, 16, PanelTheme.HudSecondaryText);
        }
        else
        {
            Raylib.DrawText($"Dist {_orbitCamera.State.Distance:0.###}   {StatusLine(ssaa)}", 18, 42, 16, PanelTheme.HudSecondaryText);
        }

        if (showFrameTime && _renderer.HasFrameTime)
            Raylib.DrawText($"Frame {_renderer.LastFrameMs:0.0} ms", 18, 64, 16, PanelTheme.HudSecondaryText);
    }

    public string StatusLine(SsaaMode ssaa)
    {
        int fps = Raylib.GetFPS();
        string ssaaTag = ssaa != SsaaMode.Off ? $" {SsaaPattern.ShortLabel(ssaa)}" : string.Empty;
        if (!_renderer.IsAvailable)
            return "No GPU shader";
        if (_renderer.IsRenderingTiles)
        {
            if (_renderer.TotalSsaaSamples > 1)
                return $"GPU 3D{ssaaTag} ({_renderer.CompletedSsaaSamples}/{_renderer.TotalSsaaSamples})...";
            return "GPU 3D tiling...";
        }

        return $"GPU 3D{ssaaTag}    {fps} fps";
    }

    public bool IsReadyForCapture(SsaaMode expectedSsaa)
        => _renderer.IsReadyForFinalCapture(expectedSsaa);

    public void Save(string path) => _renderer.Save(path);

    public void Dispose() => _renderer.Dispose();

    void TouchActiveCamera()
    {
        if (IsFlyMode)
            _flyCamera.Touch();
        else
            _orbitCamera.Touch();
    }
}
