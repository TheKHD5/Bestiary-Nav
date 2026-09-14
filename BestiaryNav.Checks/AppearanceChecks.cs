using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using BestiaryNav;

internal static class AppearanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(ThemeCatalog.Styles.Length == 3 && ThemeCatalog.Palettes.Length == 7, "three styles and seven palettes available");
        check(ThemeCatalog.Styles.Select(ThemeCatalog.Metrics).Distinct().Count() == 3, "styles have distinct shape and spacing");
        check(ThemeCatalog.Palettes.Select(ThemeCatalog.Palette).Distinct().Count() == 7, "seven distinct color palettes");
        foreach (var style in ThemeCatalog.Styles)
        foreach (var palette in ThemeCatalog.Palettes)
        {
            var restored = JsonSerializer.Deserialize<Appearance>(JsonSerializer.Serialize(new Appearance
                { Style = style, Palette = palette, Opacity = .9f }))!;
            restored.Normalize();
            check(restored.Style == style && restored.Palette == palette && restored.Opacity == .9f,
                $"appearance combination survives reload: {style}/{palette}");
        }
        foreach (var palette in ThemeCatalog.Palettes)
        {
            var p = ThemeCatalog.Palette(palette);
            var backgrounds = new[] { p.Background, p.Surface, p.Hover, p.Active };
            check(backgrounds.All(bg => Contrast(p.Text, bg) >= 4.5), $"readable main text on {palette} control states");
            check(Contrast(p.Muted, p.Background) >= 4.5 && Contrast(p.Muted, p.Surface) >= 4.5,
                $"readable secondary text on {palette}");
            check(Contrast(p.Accent, p.Surface) >= 3, $"visible selected checkboxes on {palette}");
            check(backgrounds.Concat([p.Text, p.Muted, p.Accent, p.Border]).All(c =>
                float.IsFinite(c.X) && c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1 && c.W == 1),
                $"valid opaque colors for {palette}");
        }
        var broken = new Appearance { Style = (UiStyle)99, Palette = (UiPalette)99, Opacity = float.NaN };
        broken.Normalize();
        check(broken.Style == UiStyle.Modern && broken.Palette == UiPalette.Midnight && broken.Opacity == .97f,
            "invalid appearance preferences recover to defaults");
        broken.Opacity = -1; broken.Normalize(); check(broken.Opacity == .8f, "opacity has a readable lower bound");
        broken.Opacity = 3; broken.Normalize(); check(broken.Opacity == 1, "opacity cannot exceed opaque");
        broken.Enabled = false;
        var disabled = JsonSerializer.Deserialize<Appearance>(JsonSerializer.Serialize(broken))!;
        check(!disabled.Enabled, "using the Dalamud theme survives reload");
        foreach (var style in ThemeCatalog.Styles)
        foreach (var scale in new[] { .75f, 1, 1.5f, 2 })
        foreach (var touch in new[] { 0f, 4f })
        {
            // Collapse is on the left; only close and the options menu remain
            // on the right. Include expanded mouse/touch hit rectangles.
            const float right = 1000;
            var font = 17 * scale;
            var padding = ThemeCatalog.Metrics(style).FramePadding.X * scale;
            var gap = ThemeCatalog.TitleButtonSpacing(style, scale, touch);
            var closeLeft = right - padding - font;
            var collapseLeft = padding;
            var optionsLeft = right - (font + gap) - font;
            check(collapseLeft + font + touch < optionsLeft - touch &&
                optionsLeft + font + touch < closeLeft - touch,
                $"title bar hit targets separated: {style}, scale {scale}, touch padding {touch}");
        }
    }

    private static double Contrast(Vector4 a, Vector4 b)
    {
        static double Linear(float c) => c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        static double Luminance(Vector4 c) => .2126 * Linear(c.X) + .7152 * Linear(c.Y) + .0722 * Linear(c.Z);
        var first = Luminance(a); var second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
}
