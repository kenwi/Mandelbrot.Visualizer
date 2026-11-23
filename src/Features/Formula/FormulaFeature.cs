using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Formula;

/// <summary>
/// Vertical slice owning power, iteration limit, auto-iterations, and Julia-set configuration,
/// keyboard shortcuts ([ ] - + J), and side-panel editors.
/// </summary>
public sealed class FormulaFeature
{
    readonly DoubleField _powerField = new();
    readonly IntField _iterationsField = new();
    readonly DoubleField _juliaReField = new();
    readonly DoubleField _juliaImField = new();

    public FormulaParams State { get; private set; } = FormulaParams.Default;
    public bool AutoIterations { get; private set; }

    public void SetAutoIterations(bool enabled) => AutoIterations = enabled;

    public void SetFormula(FormulaParams formula)
    {
        State = formula;
        _powerField.Invalidate();
        _iterationsField.Invalidate();
        _juliaReField.Invalidate();
        _juliaImField.Invalidate();
    }

    public void JumpToJulia(double re, double im)
    {
        State = State with
        {
            Julia = true,
            JuliaRe = re,
            JuliaIm = im,
        };
        _juliaReField.Invalidate();
        _juliaImField.Invalidate();
    }

    public void ExitJulia()
    {
        if (!State.Julia) return;
        State = State with { Julia = false };
    }

    public bool SyncAutoIterations(FloatExp scale)
    {
        if (!AutoIterations) return false;

        int target = FormulaParams.AutoIterationsForScale(scale);
        if (target == State.MaxIterations) return false;

        State = State with { MaxIterations = target };
        _iterationsField.Invalidate();
        return true;
    }

    public bool HandleKeys(out bool viewResetNeeded, out bool interactive)
    {
        bool changed = false;
        viewResetNeeded = false;
        interactive = false;

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_J))
        {
            State = State with { Julia = !State.Julia };
            viewResetNeeded = true;
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_F))
        {
            int next = ((int)State.Family + 1) % FormulaParams.FamilyCount;
            State = State with { Family = (FractalFamily)next };
            viewResetNeeded = true;
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
            AutoIterations = false;
            int step = State.MaxIterations >= 2000 ? 250 : 25;
            State = State with { MaxIterations = FormulaParams.ClampIterations(State.MaxIterations - step) };
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_EQUAL) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_ADD))
        {
            AutoIterations = false;
            int step = State.MaxIterations >= 2000 ? 250 : 25;
            State = State with { MaxIterations = FormulaParams.ClampIterations(State.MaxIterations + step) };
            changed = true;
        }

        return changed;
    }

    public bool DrawPowerAndIterations(ref PanelLayout layout, TextEditScope editScope, out bool familyChanged)
    {
        bool changed = false;
        familyChanged = false;

        int family = (int)State.Family;
        if (layout.LabeledCombo("Fractal", FormulaParams.FamilyComboItems, ref family))
        {
            State = State with { Family = (FractalFamily)family };
            familyChanged = true;
            changed = true;
        }

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
            AutoIterations = false;
            State = State with { MaxIterations = FormulaParams.ClampIterations((int)Math.Round(value)) };
            _iterationsField.Invalidate();
        });
        changed |= _iterationsField.Draw(ref layout, editScope, State.MaxIterations, value =>
        {
            AutoIterations = false;
            State = State with { MaxIterations = FormulaParams.ClampIterations(value) };
        });

        bool auto = AutoIterations;
        if (layout.Check("Auto iterations", ref auto))
        {
            AutoIterations = auto;
            changed = true;
        }

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
