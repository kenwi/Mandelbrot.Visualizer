using System.Globalization;
using System.Text;
using Mandelbrot.Visualizer.Coloring;
using Mandelbrot.Visualizer.Viewport;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Controls;

/// <summary>
/// Raygui panel for the power, iterations, palette, and the rest of the view options.
/// </summary>
public sealed class ControlPanel
{
    public const int Width = 332;
    const int BufferSize = 64;

    readonly byte[] _power = new byte[BufferSize];
    readonly byte[] _iterations = new byte[BufferSize];
    readonly byte[] _juliaRe = new byte[BufferSize];
    readonly byte[] _juliaIm = new byte[BufferSize];

    bool _powerEdit;
    bool _iterationsEdit;
    bool _juliaReEdit;
    bool _juliaImEdit;
    double _shownPower = double.NaN;
    int _shownIterations = int.MinValue;
    double _shownJuliaRe = double.NaN;
    double _shownJuliaIm = double.NaN;

    public bool Visible { get; set; } = true;
    public bool CapturesKeyboard => _powerEdit || _iterationsEdit || _juliaReEdit || _juliaImEdit;

    public void CancelEdit()
    {
        _powerEdit = false;
        _iterationsEdit = false;
        _juliaReEdit = false;
        _juliaImEdit = false;
        _shownPower = double.NaN;
        _shownIterations = int.MinValue;
        _shownJuliaRe = double.NaN;
        _shownJuliaIm = double.NaN;
    }

    public static void ApplyTheme()
    {
        Set(GuiControl.DEFAULT, GuiControlProperty.BORDER_COLOR_NORMAL, new Color(72, 80, 98, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.BASE_COLOR_NORMAL, new Color(30, 34, 46, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.TEXT_COLOR_NORMAL, new Color(226, 228, 234, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.BORDER_COLOR_FOCUSED, new Color(96, 176, 232, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.BASE_COLOR_FOCUSED, new Color(38, 48, 68, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.TEXT_COLOR_FOCUSED, new Color(255, 255, 255, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.BORDER_COLOR_PRESSED, new Color(72, 150, 214, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.BASE_COLOR_PRESSED, new Color(24, 30, 44, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.TEXT_COLOR_PRESSED, new Color(186, 220, 255, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.BORDER_COLOR_DISABLED, new Color(52, 56, 68, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.BASE_COLOR_DISABLED, new Color(26, 28, 36, 255));
        Set(GuiControl.DEFAULT, GuiControlProperty.TEXT_COLOR_DISABLED, new Color(120, 124, 136, 255));
        Set(GuiControl.DEFAULT, GuiDefaultProperty.TEXT_SIZE, 18);
        Set(GuiControl.DEFAULT, GuiDefaultProperty.TEXT_SPACING, 1);
        Set(GuiControl.DEFAULT, GuiDefaultProperty.BACKGROUND_COLOR, new Color(18, 20, 28, 255));
        Set(GuiControl.DEFAULT, GuiDefaultProperty.LINE_COLOR, new Color(72, 80, 98, 255));
        Set(GuiControl.SLIDER, GuiControlProperty.BORDER_COLOR_NORMAL, new Color(72, 80, 98, 255));
        Set(GuiControl.SLIDER, GuiControlProperty.BASE_COLOR_NORMAL, new Color(24, 28, 38, 255));
        Set(GuiControl.SLIDER, GuiControlProperty.BASE_COLOR_FOCUSED, new Color(32, 40, 56, 255));
        Set(GuiControl.SLIDER, GuiControlProperty.BASE_COLOR_PRESSED, new Color(86, 168, 232, 255));
        Set(GuiControl.SLIDER, GuiControlProperty.TEXT_COLOR_FOCUSED, new Color(120, 196, 245, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.BORDER_COLOR_NORMAL, new Color(120, 130, 150, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.BASE_COLOR_NORMAL, new Color(24, 28, 38, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.TEXT_COLOR_NORMAL, new Color(226, 228, 234, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.BORDER_COLOR_PRESSED, new Color(86, 168, 232, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.BASE_COLOR_PRESSED, new Color(86, 168, 232, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.TEXT_COLOR_PRESSED, new Color(12, 16, 24, 255));
    }

    static void Set(GuiControl control, GuiControlProperty property, Color color)
        => RayGui.GuiSetStyle((int)control, (int)property, Raylib.ColorToInt(color));

    static void Set(GuiControl control, GuiControlProperty property, int value)
        => RayGui.GuiSetStyle((int)control, (int)property, value);

    static void Set(GuiControl control, GuiDefaultProperty property, Color color)
        => RayGui.GuiSetStyle((int)control, (int)property, Raylib.ColorToInt(color));

    static void Set(GuiControl control, GuiDefaultProperty property, int value)
        => RayGui.GuiSetStyle((int)control, (int)property, value);

    public bool Draw(
        VisualizerSettings settings,
        FractalView view,
        int screenWidth,
        int screenHeight,
        double cursorRe,
        double cursorIm,
        bool cursorValid,
        string status,
        out bool resetView,
        out bool saveImage)
    {
        resetView = false;
        saveImage = false;
        if (!Visible) return false;

        var panel = new Rectangle { x = screenWidth - Width, y = 0, width = Width, height = screenHeight };
        Raylib.DrawRectangleRec(panel, new Color(18, 20, 28, 255));
        Raylib.DrawRectangle(screenWidth - Width, 0, 1, screenHeight, new Color(72, 80, 98, 255));

        float x = screenWidth - Width + 16;
        float inner = Width - 32;
        float y = 14;
        bool changed = false;

        Raylib.DrawText("Multibrot", (int)x, (int)y, 28, new Color(236, 238, 244, 255));
        y += 36;
        RayGui.GuiLabel(Row(x, ref y, inner, 22), "z  =  z^n  +  c");
        bool gpu = settings.UseGpu;
        if (Check(x, ref y, inner, "GPU shader", ref gpu))
        {
            settings.UseGpu = gpu;
            changed = true;
        }

        RayGui.GuiLine(Row(x, ref y, inner, 12), "");

        RayGui.GuiLabel(Row(x, ref y, inner, 20), "Power n");
        changed |= Slider(x, ref y, inner, settings.Power, VisualizerSettings.PowerMin, VisualizerSettings.PowerMax, value =>
        {
            settings.Power = value;
            _shownPower = double.NaN;
        });
        changed |= NumberBox(x, ref y, inner, _power, ref _powerEdit, ref _shownPower, settings.Power, value =>
        {
            if (Math.Abs(value) < 1e-4) value = value < 0 ? -1e-4 : 1e-4;
            settings.Power = value;
        });

        RayGui.GuiLabel(Row(x, ref y, inner, 20), "Iterations");
        changed |= LogSlider(x, ref y, inner, settings.MaxIterations, VisualizerSettings.IterationMin, VisualizerSettings.IterationMax, value =>
        {
            settings.MaxIterations = (int)Math.Round(value);
            _shownIterations = int.MinValue;
        });
        changed |= IntBox(x, ref y, inner, _iterations, ref _iterationsEdit, ref _shownIterations, settings.MaxIterations, value =>
        {
            settings.MaxIterations = Math.Clamp(value, VisualizerSettings.IterationMin, VisualizerSettings.IterationMax);
        });

        RayGui.GuiLabel(Row(x, ref y, inner, 20), "Palette");
        int palette = settings.Palette;
        RayGui.GuiComboBox(Row(x, ref y, inner, 28), Palette.ComboItems, ref palette);
        if (palette != settings.Palette)
        {
            settings.Palette = palette;
            changed = true;
        }

        RayGui.GuiLabel(Row(x, ref y, inner, 20), "Coloring");
        int mode = (int)settings.Mode;
        RayGui.GuiComboBox(Row(x, ref y, inner, 28), "Cyclic;Histogram", ref mode);
        if (mode != (int)settings.Mode)
        {
            settings.Mode = (ColoringMode)mode;
            changed = true;
        }

        RayGui.GuiLabel(Row(x, ref y, inner, 20), $"Density   {settings.Density:0.00}");
        changed |= Slider(x, ref y, inner, settings.Density, 0.25, 4, value => settings.Density = value);

        RayGui.GuiLabel(Row(x, ref y, inner, 20), $"Phase   {settings.Phase:0.00}");
        changed |= Slider(x, ref y, inner, settings.Phase, 0, 1, value => settings.Phase = value);

        bool smooth = settings.Smooth;
        if (Check(x, ref y, inner, "Smooth coloring", ref smooth))
        {
            settings.Smooth = smooth;
            changed = true;
        }

        RayGui.GuiLabel(Row(x, ref y, inner, 20), "Interior");
        int interior = (int)settings.Interior;
        RayGui.GuiComboBox(Row(x, ref y, inner, 28), "Black;Glow", ref interior);
        if (interior != (int)settings.Interior)
        {
            settings.Interior = (InteriorStyle)interior;
            changed = true;
        }

        bool julia = settings.Julia;
        if (Check(x, ref y, inner, "Julia set", ref julia))
        {
            settings.Julia = julia;
            changed = true;
        }

        if (!settings.Julia) RayGui.GuiDisable();
        changed |= NumberBox(x, ref y, inner, _juliaRe, ref _juliaReEdit, ref _shownJuliaRe, settings.JuliaRe, value => settings.JuliaRe = value, "Re c");
        changed |= NumberBox(x, ref y, inner, _juliaIm, ref _juliaImEdit, ref _shownJuliaIm, settings.JuliaIm, value => settings.JuliaIm = value, "Im c");
        if (!settings.Julia) RayGui.GuiEnable();

        float buttonWidth = (inner - 8) / 2f;
        var reset = new Rectangle { x = x, y = y, width = buttonWidth, height = 30 };
        var save = new Rectangle { x = x + buttonWidth + 8, y = y, width = buttonWidth, height = 30 };
        y += 38;
        if (RayGui.GuiButton(reset, "Reset view") != 0) resetView = true;
        if (RayGui.GuiButton(save, "Save PNG") != 0) saveImage = true;

        RayGui.GuiLine(Row(x, ref y, inner, 12), "");
        string cursor = cursorValid
            ? $"{cursorRe:0.######}  {Signed(cursorIm)} i"
            : "over the panel";
        RayGui.GuiLabel(Row(x, ref y, inner, 20), $"Cursor  {cursor}");
        RayGui.GuiLabel(Row(x, ref y, inner, 20), $"Center  {view.CenterX:0.####}  {Signed(view.CenterY)} i");
        double zoom = 1.35 / Math.Max(view.Scale, FractalView.MinScale);
        RayGui.GuiLabel(Row(x, ref y, inner, 20), $"Zoom  {zoom:0.##}x    n = {settings.Power:0.####}");
        RayGui.GuiLabel(Row(x, ref y, inner, 20), status);

        y += 6;
        Raylib.DrawText("Drag pan   wheel zoom   double-click zoom", (int)x, (int)y, 14, new Color(150, 156, 170, 255));
        y += 18;
        Raylib.DrawText("hold [ ] power   - + iterations   C palette", (int)x, (int)y, 14, new Color(150, 156, 170, 255));
        y += 18;
        Raylib.DrawText("R reset   J julia   G gpu   H panel   S save", (int)x, (int)y, 14, new Color(150, 156, 170, 255));

        return changed;
    }

    static string Signed(double value) => value >= 0 ? $"+ {value:0.####}" : $"- {Math.Abs(value):0.####}";

    static Rectangle Row(float x, ref float y, float width, float height)
    {
        var row = new Rectangle { x = x, y = y, width = width, height = height };
        y += height + 6;
        return row;
    }

    static bool Check(float x, ref float y, float width, string label, ref bool value)
    {
        bool before = value;
        var box = new Rectangle { x = x, y = y, width = 22, height = 22 };
        RayGui.GuiCheckBox(box, "", ref value);
        if (value)
            Raylib.DrawRectangle((int)x + 5, (int)y + 5, 12, 12, new Color(86, 168, 232, 255));
        RayGui.GuiLabel(new Rectangle { x = x + 30, y = y, width = width - 30, height = 22 }, label);
        y += 28;
        return value != before;
    }

    static bool LogSlider(float x, ref float y, float width, double current, double min, double max, Action<double> apply)
    {
        double logMin = Math.Log(min);
        double logMax = Math.Log(max);
        double clamped = Math.Clamp(current, min, max);
        float value = (float)((Math.Log(clamped) - logMin) / (logMax - logMin));
        float before = value;
        RayGui.GuiSliderBar(Row(x, ref y, width, 22), "", "", ref value, 0, 1);
        if (Math.Abs(value - before) <= 0.0001f) return false;
        apply(Math.Exp(logMin + Math.Clamp(value, 0, 1) * (logMax - logMin)));
        return true;
    }

    static bool Slider(float x, ref float y, float width, double current, double min, double max, Action<double> apply)
    {
        float value = (float)Math.Clamp(current, min, max);
        float before = value;
        RayGui.GuiSliderBar(Row(x, ref y, width, 22), "", "", ref value, (float)min, (float)max);
        if (Math.Abs(value - before) <= 0.0001f) return false;
        apply(value);
        return true;
    }

    bool NumberBox(
        float x,
        ref float y,
        float width,
        byte[] buffer,
        ref bool edit,
        ref double shown,
        double current,
        Action<double> apply,
        string? label = null)
    {
        if (!edit && (double.IsNaN(shown) || Math.Abs(shown - current) > 1e-12))
        {
            Write(buffer, current.ToString("0.######", CultureInfo.InvariantCulture));
            shown = current;
        }

        float boxX = label == null ? x : x + 48;
        float boxWidth = label == null ? width : width - 48;
        if (label != null)
            RayGui.GuiLabel(new Rectangle { x = x, y = y, width = 44, height = 28 }, label);

        var bounds = new Rectangle { x = boxX, y = y, width = boxWidth, height = 28 };
        y += 34;
        bool wasEditing = edit;
        if (TextBox(bounds, buffer, edit) != 0)
        {
            edit = !edit;
            CloseOtherEditors(buffer);
        }

        if (!wasEditing || edit) return false;
        if (!TryParse(Read(buffer), out double value))
        {
            shown = double.NaN;
            return false;
        }

        apply(value);
        shown = value;
        return true;
    }

    bool IntBox(
        float x,
        ref float y,
        float width,
        byte[] buffer,
        ref bool edit,
        ref int shown,
        int current,
        Action<int> apply)
    {
        if (!edit && shown != current)
        {
            Write(buffer, current.ToString(CultureInfo.InvariantCulture));
            shown = current;
        }

        var bounds = Row(x, ref y, width, 28);
        bool wasEditing = edit;
        if (TextBox(bounds, buffer, edit) != 0)
        {
            edit = !edit;
            CloseOtherEditors(buffer);
        }

        if (!wasEditing || edit) return false;
        if (!int.TryParse(Read(buffer), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            shown = int.MinValue;
            return false;
        }

        apply(value);
        shown = value;
        return true;
    }

    void CloseOtherEditors(byte[] active)
    {
        if (!ReferenceEquals(active, _power)) _powerEdit = false;
        if (!ReferenceEquals(active, _iterations)) _iterationsEdit = false;
        if (!ReferenceEquals(active, _juliaRe)) _juliaReEdit = false;
        if (!ReferenceEquals(active, _juliaIm)) _juliaImEdit = false;
    }

    static unsafe int TextBox(Rectangle bounds, byte[] buffer, bool edit)
    {
        fixed (byte* pointer = buffer)
            return RayGui.GuiTextBox(bounds, (sbyte*)pointer, buffer.Length, edit);
    }

    static void Write(byte[] buffer, string text)
    {
        Array.Clear(buffer);
        int count = Encoding.UTF8.GetBytes(text, buffer.AsSpan(0, buffer.Length - 1));
        buffer[count] = 0;
    }

    static string Read(byte[] buffer)
    {
        int count = Array.IndexOf(buffer, (byte)0);
        if (count < 0) count = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, count);
    }

    static bool TryParse(string text, out double value)
    {
        text = text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}
