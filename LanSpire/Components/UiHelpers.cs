
namespace LanSpire.Components;

/// <summary>
/// Visual polish helpers for the LAN screens. Style-only (no logic changes),
/// tuned to the game's warm parchment and gold palette so the custom inputs
/// and buttons read as intentional UI instead of bare Godot controls.
/// </summary>
internal static class UiHelpers
{
    internal static readonly Color LanGold = new Color(0.937255f, 0.784314f, 0.317647f);

    internal static StyleBoxFlat MakeLanStyleBox(Color bg, Color border, int borderWidth, int radius)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetBorderWidthAll(borderWidth);
        sb.BorderColor = border;
        sb.SetCornerRadiusAll(radius);
        sb.ContentMarginLeft = 14;
        sb.ContentMarginRight = 14;
        sb.ContentMarginTop = 8;
        sb.ContentMarginBottom = 8;
        return sb;
    }

    internal static void StyleLanInput(LineEdit input)
    {
        input.AddThemeStyleboxOverride("normal", MakeLanStyleBox(new Color(0.11f, 0.085f, 0.06f, 0.92f), new Color(0.45f, 0.37f, 0.23f), 2, 6));
        input.AddThemeStyleboxOverride("focus", MakeLanStyleBox(new Color(0.14f, 0.11f, 0.07f, 0.96f), LanGold, 2, 6));
        input.AddThemeStyleboxOverride("read_only", MakeLanStyleBox(new Color(0.09f, 0.07f, 0.05f, 0.85f), new Color(0.35f, 0.29f, 0.18f), 2, 6));
        input.AddThemeColorOverride("font_color", new Color(0.96f, 0.93f, 0.85f));
        input.AddThemeColorOverride("font_placeholder_color", new Color(0.60f, 0.55f, 0.45f));
        input.AddThemeColorOverride("caret_color", LanGold);
        input.AddThemeColorOverride("selection_color", new Color(0.937255f, 0.784314f, 0.317647f, 0.35f));
    }

    internal static void StyleLanButton(Button btn, bool accent = false)
    {
        btn.Flat = false;
        var baseBg = accent ? new Color(0.22f, 0.16f, 0.08f, 0.95f) : new Color(0.16f, 0.12f, 0.08f, 0.92f);
        var hoverBg = accent ? new Color(0.30f, 0.22f, 0.11f, 0.97f) : new Color(0.22f, 0.17f, 0.11f, 0.96f);
        var pressBg = new Color(0.10f, 0.08f, 0.05f, 1f);
        var border = accent ? new Color(0.62f, 0.50f, 0.28f) : new Color(0.42f, 0.35f, 0.22f);
        btn.AddThemeStyleboxOverride("normal", MakeLanStyleBox(baseBg, border, 2, 6));
        btn.AddThemeStyleboxOverride("hover", MakeLanStyleBox(hoverBg, LanGold, 2, 6));
        btn.AddThemeStyleboxOverride("pressed", MakeLanStyleBox(pressBg, border, 2, 6));
        btn.AddThemeStyleboxOverride("focus", MakeLanStyleBox(baseBg, LanGold, 2, 6));
        btn.AddThemeColorOverride("font_color", new Color(0.96f, 0.91f, 0.78f));
        btn.AddThemeColorOverride("font_hover_color", Colors.White);
        btn.AddThemeColorOverride("font_pressed_color", new Color(0.85f, 0.80f, 0.68f));
    }

    internal static Label MakeLabel(string name, string text, int fontSize, Color color)
    {
        var label = new Label { Name = name, Text = text };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }
}
