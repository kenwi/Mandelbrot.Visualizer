using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Shared;

/// <summary>
/// Vertical cursor helper for laying out RayGui controls inside the side panel.
/// </summary>
public struct PanelLayout(float x, float y, float width)
{
    public float X { get; } = x;
    public float Y { get; set; } = y;
    public float Width { get; } = width;

    public Rectangle Row(float height)
    {
        var row = new Rectangle { x = X, y = Y, width = Width, height = height };
        Y += height + 6;
        return row;
    }

    public void Space(float delta) => Y += delta;

    public void Title(string text)
    {
        Raylib.DrawText(text, (int)X, (int)Y, 28, PanelTheme.TitleText);
        Y += 36;
    }

    public void Label(string text, float height = 20)
        => RayGui.GuiLabel(Row(height), text);

    public void Hint(string text)
    {
        Raylib.DrawText(text, (int)X, (int)Y, 14, PanelTheme.MutedText);
        Y += 18;
    }

    public void Separator()
        => RayGui.GuiLine(Row(12), "");

    public bool Check(string label, ref bool value)
    {
        bool before = value;
        var box = new Rectangle { x = X, y = Y, width = 22, height = 22 };
        RayGui.GuiCheckBox(box, "", ref value);
        if (value)
            Raylib.DrawRectangle((int)X + 5, (int)Y + 5, 12, 12, PanelTheme.CheckFill);
        RayGui.GuiLabel(new Rectangle { x = X + 30, y = Y, width = Width - 30, height = 22 }, label);
        Y += 28;
        return value != before;
    }

    public bool Combo(string items, ref int selected)
    {
        int before = selected;
        RayGui.GuiComboBox(Row(28), items, ref selected);
        return selected != before;
    }

    public bool Slider(double current, double min, double max, Action<double> apply)
    {
        float value = (float)Math.Clamp(current, min, max);
        float before = value;
        RayGui.GuiSliderBar(Row(22), "", "", ref value, (float)min, (float)max);
        if (Math.Abs(value - before) <= 0.0001f) return false;
        apply(value);
        return true;
    }

    public bool LogSlider(double current, double min, double max, Action<double> apply)
    {
        double logMin = Math.Log(min);
        double logMax = Math.Log(max);
        double clamped = Math.Clamp(current, min, max);
        float value = (float)((Math.Log(clamped) - logMin) / (logMax - logMin));
        float before = value;
        RayGui.GuiSliderBar(Row(22), "", "", ref value, 0, 1);
        if (Math.Abs(value - before) <= 0.0001f) return false;
        apply(Math.Exp(logMin + Math.Clamp(value, 0, 1) * (logMax - logMin)));
        return true;
    }

    public void TwoButtons(string leftLabel, string rightLabel, out bool leftClicked, out bool rightClicked)
    {
        float buttonWidth = (Width - 8) / 2f;
        var left = new Rectangle { x = X, y = Y, width = buttonWidth, height = 30 };
        var right = new Rectangle { x = X + buttonWidth + 8, y = Y, width = buttonWidth, height = 30 };
        Y += 38;
        leftClicked = RayGui.GuiButton(left, leftLabel) != 0;
        rightClicked = RayGui.GuiButton(right, rightLabel) != 0;
    }
}
