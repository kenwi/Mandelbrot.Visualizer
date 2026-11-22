using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Shared;

public static class PanelTheme
{
    public static readonly Color CanvasBackground = new(4, 6, 12, 255);
    public static readonly Color PanelBackground = new(18, 20, 28, 255);
    public static readonly Color PanelBorder = new(72, 80, 98, 255);
    public static readonly Color TitleText = new(236, 238, 244, 255);
    public static readonly Color MutedText = new(150, 156, 170, 255);
    public static readonly Color HudPrimaryText = new(230, 232, 238, 255);
    public static readonly Color HudSecondaryText = new(170, 176, 188, 255);
    public static readonly Color CheckFill = new(86, 168, 232, 255);

    public static void Apply()
    {
        Set(GuiControl.DEFAULT, GuiControlProperty.BORDER_COLOR_NORMAL, PanelBorder);
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
        Set(GuiControl.DEFAULT, GuiDefaultProperty.BACKGROUND_COLOR, PanelBackground);
        Set(GuiControl.DEFAULT, GuiDefaultProperty.LINE_COLOR, PanelBorder);
        Set(GuiControl.SLIDER, GuiControlProperty.BORDER_COLOR_NORMAL, PanelBorder);
        Set(GuiControl.SLIDER, GuiControlProperty.BASE_COLOR_NORMAL, new Color(24, 28, 38, 255));
        Set(GuiControl.SLIDER, GuiControlProperty.BASE_COLOR_FOCUSED, new Color(32, 40, 56, 255));
        Set(GuiControl.SLIDER, GuiControlProperty.BASE_COLOR_PRESSED, CheckFill);
        Set(GuiControl.SLIDER, GuiControlProperty.TEXT_COLOR_FOCUSED, new Color(120, 196, 245, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.BORDER_COLOR_NORMAL, new Color(120, 130, 150, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.BASE_COLOR_NORMAL, new Color(24, 28, 38, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.TEXT_COLOR_NORMAL, new Color(226, 228, 234, 255));
        Set(GuiControl.CHECKBOX, GuiControlProperty.BORDER_COLOR_PRESSED, CheckFill);
        Set(GuiControl.CHECKBOX, GuiControlProperty.BASE_COLOR_PRESSED, CheckFill);
        Set(GuiControl.CHECKBOX, GuiControlProperty.TEXT_COLOR_PRESSED, new Color(12, 16, 24, 255));
    }

    static void Set(GuiControl control, GuiControlProperty property, Color color)
        => RayGui.GuiSetStyle((int)control, (int)property, Raylib.ColorToInt(color));

    static void Set(GuiControl control, GuiDefaultProperty property, Color color)
        => RayGui.GuiSetStyle((int)control, (int)property, Raylib.ColorToInt(color));

    static void Set(GuiControl control, GuiDefaultProperty property, int value)
        => RayGui.GuiSetStyle((int)control, (int)property, value);
}
