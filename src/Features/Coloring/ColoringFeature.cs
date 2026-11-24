using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Coloring;

/// <summary>
/// Vertical slice owning palette, coloring mode, density, phase, smoothing,
/// interior style, 3D normal-map lighting, orbit traps, C/L/T hotkeys, and side-panel coloring controls.
/// </summary>
public sealed class ColoringFeature
{
    public ColoringParams State { get; private set; } = ColoringParams.Default;

    public void SetColoring(ColoringParams coloring) => State = coloring;

    public bool SyncCursorLight(bool hasCursor, float lightX, float lightY)
    {
        if (!State.NormalLighting || !State.CursorLight || !hasCursor)
            return false;

        if (MathF.Abs(State.LightX - lightX) < 1e-4f && MathF.Abs(State.LightY - lightY) < 1e-4f)
            return false;

        State = State with { LightX = lightX, LightY = lightY };
        return true;
    }

    public bool HandleKeys()
    {
        bool changed = false;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_C))
        {
            State = State with { Palette = (State.Palette + 1) % Palette.Count };
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_L))
        {
            State = State with { NormalLighting = !State.NormalLighting };
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_M))
        {
            bool next = !State.CursorLight;
            State = State with { CursorLight = next, NormalLighting = next || State.NormalLighting };
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_T))
        {
            int next = ((int)State.OrbitTrap + 1) % ColoringParams.OrbitTrapCount;
            State = State with { OrbitTrap = (OrbitTrapMode)next };
            changed = true;
        }

        return changed;
    }

    public bool DrawPanel(ref PanelLayout layout)
    {
        bool changed = false;

        int palette = State.Palette;
        if (layout.LabeledCombo("Palette", Palette.ComboItems, ref palette))
        {
            State = State with { Palette = palette };
            changed = true;
        }

        int mode = (int)State.Mode;
        if (layout.LabeledCombo("Coloring", "Cyclic;Histogram", ref mode))
        {
            State = State with { Mode = (ColoringMode)mode };
            changed = true;
        }

        int interior = (int)State.Interior;
        if (layout.LabeledCombo("Interior", "Black;Glow", ref interior))
        {
            State = State with { Interior = (InteriorStyle)interior };
            changed = true;
        }

        int trap = (int)State.OrbitTrap;
        if (layout.LabeledCombo("Orbit trap", ColoringParams.OrbitTrapComboItems, ref trap))
        {
            State = State with { OrbitTrap = (OrbitTrapMode)trap };
            changed = true;
        }

        changed |= layout.LabeledSlider($"Density {State.Density:0.00}", State.Density, 0.25, 4, value => State = State with { Density = value });
        changed |= layout.LabeledSlider($"Phase   {State.Phase:0.00}", State.Phase, 0, 1, value => State = State with { Phase = value });

        bool smooth = State.Smooth;
        if (layout.Check("Smooth coloring", ref smooth))
        {
            State = State with { Smooth = smooth };
            changed = true;
        }

        bool lighting = State.NormalLighting;
        bool cursorLight = State.CursorLight;
        if (layout.TwoChecks("3D lighting", ref lighting, "Mouse light", ref cursorLight, 0.50f))
        {
            if (cursorLight && !State.CursorLight && !lighting)
                lighting = true;
            if (!lighting)
                cursorLight = false;

            State = State with { NormalLighting = lighting, CursorLight = cursorLight };
            changed = true;
        }

        if (!State.NormalLighting || State.CursorLight) RayGui.GuiDisable();
        changed |= layout.LabeledSlider($"Angle   {State.LightAngle:0}d", State.LightAngle, 0, 360, value => State = State with { LightAngle = value });
        if (!State.NormalLighting || State.CursorLight) RayGui.GuiEnable();

        return changed;
    }

    public bool DrawPanel3D(ref PanelLayout layout)
    {
        bool changed = false;

        int palette = State.Palette;
        if (layout.LabeledCombo("Palette", Palette.ComboItems, ref palette))
        {
            State = State with { Palette = palette };
            changed = true;
        }

        changed |= layout.LabeledSlider($"Density {State.Density:0.00}", State.Density, 0.25, 4, value => State = State with { Density = value });
        changed |= layout.LabeledSlider($"Phase   {State.Phase:0.00}", State.Phase, 0, 1, value => State = State with { Phase = value });
        changed |= layout.LabeledSlider($"Light   {State.LightAngle:0}d", State.LightAngle, 0, 360, value => State = State with { LightAngle = value });

        return changed;
    }
}
