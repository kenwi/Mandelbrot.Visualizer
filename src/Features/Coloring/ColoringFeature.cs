using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Coloring;

/// <summary>
/// Vertical slice owning palette, coloring mode, density, phase, smoothing,
/// interior style, C hotkey, and side-panel coloring controls.
/// </summary>
public sealed class ColoringFeature
{
    public ColoringParams State { get; private set; } = ColoringParams.Default;

    public bool HandleKeys()
    {
        if (!Raylib.IsKeyPressed(KeyboardKey.KEY_C))
            return false;

        State = State with { Palette = (State.Palette + 1) % Palette.Count };
        return true;
    }

    public bool DrawPanel(ref PanelLayout layout)
    {
        bool changed = false;

        layout.Label("Palette");
        int palette = State.Palette;
        if (layout.Combo(Palette.ComboItems, ref palette))
        {
            State = State with { Palette = palette };
            changed = true;
        }

        layout.Label("Coloring");
        int mode = (int)State.Mode;
        if (layout.Combo("Cyclic;Histogram", ref mode))
        {
            State = State with { Mode = (ColoringMode)mode };
            changed = true;
        }

        layout.Label($"Density   {State.Density:0.00}");
        changed |= layout.Slider(State.Density, 0.25, 4, value => State = State with { Density = value });

        layout.Label($"Phase   {State.Phase:0.00}");
        changed |= layout.Slider(State.Phase, 0, 1, value => State = State with { Phase = value });

        bool smooth = State.Smooth;
        if (layout.Check("Smooth coloring", ref smooth))
        {
            State = State with { Smooth = smooth };
            changed = true;
        }

        layout.Label("Interior");
        int interior = (int)State.Interior;
        if (layout.Combo("Black;Glow", ref interior))
        {
            State = State with { Interior = (InteriorStyle)interior };
            changed = true;
        }

        return changed;
    }
}
