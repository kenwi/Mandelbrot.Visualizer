using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Vertical slice owning power, iteration limit, and Julia-set configuration,
/// keyboard shortcuts ([ ] - + J), and side-panel editors.
/// </summary>
public sealed class FormulaFeature
{
    readonly DoubleField _powerField = new();
    readonly IntField _iterationsField = new();
    readonly DoubleField _juliaReField = new();
    readonly DoubleField _juliaImField = new();

    public FormulaParams State { get; private set; } = FormulaParams.Default;

    public bool HandleKeys(out bool juliaToggled, out bool interactive)
    {
        bool changed = false;
        juliaToggled = false;
        interactive = false;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_J))
        {
            State = State with { Julia = !State.Julia };
            juliaToggled = true;
            changed = true;
        }

        double powerDelta = 0;
        double dt = Math.Min(Raylib.GetFrameTime(), 0.05f);
        if (Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_BRACKET))
            powerDelta += 1.5 * dt;
        if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_BRACKET))
            powerDelta -= 1.5 * dt;
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_RIGHT_BRACKET))
            powerDelta += 0.1;
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_LEFT_BRACKET))
            powerDelta -= 0.1;
        if (powerDelta != 0)
        {
            State = State with { Power = FormulaParams.ClampNonZeroPower(State.Power + powerDelta, powerDelta) };
            changed = true;
            interactive = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_MINUS) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_SUBTRACT))
        {
            State = State with { MaxIterations = FormulaParams.ClampIterations(State.MaxIterations - 25) };
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_EQUAL) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_ADD))
        {
            State = State with { MaxIterations = FormulaParams.ClampIterations(State.MaxIterations + 25) };
            changed = true;
        }

        return changed;
    }

    public bool DrawPowerAndIterations(ref PanelLayout layout, TextEditScope editScope)
    {
        bool changed = false;

        layout.Label("Power n");
        changed |= layout.Slider(State.Power, FormulaParams.PowerMin, FormulaParams.PowerMax, value =>
        {
            State = State with { Power = FormulaParams.ClampNonZeroPower(value) };
            _powerField.Invalidate();
        });
        changed |= _powerField.Draw(ref layout, editScope, State.Power, value =>
        {
            State = State with { Power = FormulaParams.ClampNonZeroPower(value) };
        });

        layout.Label("Iterations");
        changed |= layout.LogSlider(State.MaxIterations, FormulaParams.IterationMin, FormulaParams.IterationMax, value =>
        {
            State = State with { MaxIterations = FormulaParams.ClampIterations((int)Math.Round(value)) };
            _iterationsField.Invalidate();
        });
        changed |= _iterationsField.Draw(ref layout, editScope, State.MaxIterations, value =>
        {
            State = State with { MaxIterations = FormulaParams.ClampIterations(value) };
        });

        return changed;
    }

    public bool DrawJuliaControls(ref PanelLayout layout, TextEditScope editScope, out bool juliaToggled)
    {
        bool changed = false;
        juliaToggled = false;

        bool julia = State.Julia;
        if (layout.Check("Julia set", ref julia))
        {
            State = State with { Julia = julia };
            juliaToggled = true;
            changed = true;
        }

        if (!State.Julia) RayGui.GuiDisable();
        changed |= _juliaReField.Draw(ref layout, editScope, State.JuliaRe, value => State = State with { JuliaRe = value }, "Re c");
        changed |= _juliaImField.Draw(ref layout, editScope, State.JuliaIm, value => State = State with { JuliaIm = value }, "Im c");
        if (!State.Julia) RayGui.GuiEnable();

        return changed;
    }
}
