using System;
using System.Numerics;

namespace BestiaryNav;

public enum UiStyle { Modern, Classic, Compact }
public enum UiPalette { Midnight, Forest, Amethyst, Ruby, Ocean, Amber, Parchment }

[Serializable]
public sealed class Appearance
{
    public bool Enabled { get; set; } = true;
    public UiStyle Style { get; set; } = UiStyle.Modern;
    public UiPalette Palette { get; set; } = UiPalette.Midnight;
    public float Opacity { get; set; } = 0.97f;

    public void Normalize()
    {
        if (!Enum.IsDefined(Style)) Style = UiStyle.Modern;
        if (!Enum.IsDefined(Palette)) Palette = UiPalette.Midnight;
        Opacity = float.IsFinite(Opacity) ? Math.Clamp(Opacity, .8f, 1) : .97f;
    }
}

internal readonly record struct ThemeMetrics(float WindowRounding, float FrameRounding, float Border,
    Vector2 Padding, Vector2 FramePadding, Vector2 Spacing);

internal readonly record struct ThemePalette(Vector4 Background, Vector4 Surface, Vector4 Accent, Vector4 Text, Vector4 Muted)
{
    public Vector4 Hover => Vector4.Lerp(Surface, Accent, .22f);
    public Vector4 Active => Vector4.Lerp(Surface, Accent, .36f);
    public Vector4 Border => Vector4.Lerp(Surface, Accent, .45f);
}

internal static class ThemeCatalog
{
    public static readonly UiStyle[] Styles = Enum.GetValues<UiStyle>();
    public static readonly UiPalette[] Palettes = Enum.GetValues<UiPalette>();
    private static Vector4 Hex(uint rgb) => new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1);
    private static ThemePalette Colors(uint background, uint surface, uint accent, uint text, uint muted) =>
        new(Hex(background), Hex(surface), Hex(accent), Hex(text), Hex(muted));

    public static ThemePalette Palette(UiPalette palette) => palette switch
    {
        UiPalette.Forest => Colors(0x111E1A, 0x213A2D, 0x87D6A2, 0xEFF7F0, 0xB4C8B9),
        UiPalette.Amethyst => Colors(0x201A2D, 0x352A48, 0xC8A2EF, 0xF5EFFB, 0xC3B7D3),
        UiPalette.Ruby => Colors(0x27191E, 0x422931, 0xF19BAA, 0xFFF0F2, 0xD3B8BF),
        UiPalette.Ocean => Colors(0x112328, 0x1D3A43, 0x6DD6DA, 0xEAF9F9, 0xADCBD0),
        UiPalette.Amber => Colors(0x251F17, 0x3D3224, 0xEFC275, 0xFFF6E6, 0xD1C2A8),
        UiPalette.Parchment => Colors(0xF4ECDC, 0xE7DAC3, 0x724322, 0x30271E, 0x675846),
        _ => Colors(0x151D2B, 0x253247, 0x8ABEFF, 0xF0F5FF, 0xB1C1D8),
    };

    public static ThemeMetrics Metrics(UiStyle style) => style switch
    {
        UiStyle.Classic => new(2, 2, 1, new(12, 10), new(7, 4), new(8, 7)),
        UiStyle.Compact => new(0, 0, 0, new(8, 7), new(5, 2), new(6, 4)),
        _ => new(10, 6, 0, new(16, 14), new(10, 6), new(10, 9)),
    };

    // Dalamud's extra title button omits the native FramePadding.X offset.
    // Leave a real gap between expanded hit rectangles, including touch padding.
    public static float TitleButtonSpacing(UiStyle style, float scale, float touchPaddingX) =>
        (Metrics(style).FramePadding.X + 8) * scale + 2 * MathF.Max(0, touchPaddingX);

    public static string Description(UiStyle style) => style switch
    {
        UiStyle.Classic => "Defined borders, subtle corners, and balanced spacing.",
        UiStyle.Compact => "Flat, square controls with tighter spacing for a smaller window.",
        _ => "Rounded controls and generous spacing for a softer look.",
    };
}
