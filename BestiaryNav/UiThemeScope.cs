using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace BestiaryNav;

// ImGui has no PushStyleVar for this enum. Keep the override scoped to our
// WindowSystem draw so other plugins retain their own title-bar layout.
internal readonly ref struct UiTitleBarLayoutScope
{
    private readonly ImGuiDir previousPosition;
    public UiTitleBarLayoutScope()
    {
        var style = ImGui.GetStyle();
        previousPosition = style.WindowMenuButtonPosition;
        style.WindowMenuButtonPosition = ImGuiDir.Left;
    }
    public void Dispose()
    {
        var style = ImGui.GetStyle();
        style.WindowMenuButtonPosition = previousPosition;
    }
}

// Scoped before Begin and restored after End, including exceptions. No global
// style mutation, per-frame allocations, custom fonts, or texture resources.
internal ref struct UiThemeScope
{
    private int colors;
    private int variables;

    public UiThemeScope(Appearance appearance, bool titleBar = false)
    {
        colors = variables = 0;
        if (!appearance.Enabled) return;
        try
        {
            var p = ThemeCatalog.Palette(appearance.Palette);
            var m = ThemeCatalog.Metrics(appearance.Style);
            var scale = ImGuiHelpers.GlobalScale;
            var background = p.Background;
            background.W = appearance.Opacity;
            Color(ImGuiCol.Text, p.Text);
            Color(ImGuiCol.TextDisabled, p.Muted);
            Color(ImGuiCol.WindowBg, background);
            Color(ImGuiCol.ChildBg, background);
            Color(ImGuiCol.PopupBg, p.Background);
            Color(ImGuiCol.Border, p.Border);
            Color(ImGuiCol.BorderShadow, Vector4.Zero);
            Color(ImGuiCol.ResizeGrip, p.Border);
            Color(ImGuiCol.ResizeGripHovered, p.Accent);
            Color(ImGuiCol.ResizeGripActive, p.Accent);
            Color(ImGuiCol.FrameBg, p.Surface);
            Color(ImGuiCol.FrameBgHovered, p.Hover);
            Color(ImGuiCol.FrameBgActive, p.Active);
            Color(ImGuiCol.TitleBg, p.Surface);
            Color(ImGuiCol.TitleBgActive, p.Hover);
            Color(ImGuiCol.TitleBgCollapsed, p.Background);
            Color(ImGuiCol.MenuBarBg, p.Surface);
            Color(ImGuiCol.Button, p.Surface);
            Color(ImGuiCol.ButtonHovered, p.Hover);
            Color(ImGuiCol.ButtonActive, p.Active);
            Color(ImGuiCol.Header, p.Surface);
            Color(ImGuiCol.HeaderHovered, p.Hover);
            Color(ImGuiCol.HeaderActive, p.Active);
            Color(ImGuiCol.CheckMark, p.Accent);
            Color(ImGuiCol.SliderGrab, p.Accent);
            Color(ImGuiCol.SliderGrabActive, p.Accent);
            Color(ImGuiCol.Separator, p.Border);
            Color(ImGuiCol.SeparatorHovered, p.Accent);
            Color(ImGuiCol.SeparatorActive, p.Accent);
            Color(ImGuiCol.Tab, p.Surface);
            Color(ImGuiCol.TabHovered, p.Hover);
            Color(ImGuiCol.TabActive, p.Active);
            Color(ImGuiCol.TabUnfocused, p.Surface);
            Color(ImGuiCol.TabUnfocusedActive, p.Hover);
            Color(ImGuiCol.NavHighlight, p.Accent);
            Color(ImGuiCol.ScrollbarBg, p.Background);
            Color(ImGuiCol.ScrollbarGrab, p.Border);
            Color(ImGuiCol.ScrollbarGrabHovered, p.Accent);
            Color(ImGuiCol.ScrollbarGrabActive, p.Accent);
            Color(ImGuiCol.TableHeaderBg, p.Surface);
            Color(ImGuiCol.TableBorderStrong, p.Border);
            Color(ImGuiCol.TableBorderLight, p.Surface);
            Color(ImGuiCol.TextSelectedBg, p.Active);
            Var(ImGuiStyleVar.WindowRounding, m.WindowRounding * scale);
            Var(ImGuiStyleVar.ChildRounding, m.FrameRounding * scale);
            Var(ImGuiStyleVar.PopupRounding, m.FrameRounding * scale);
            Var(ImGuiStyleVar.FrameRounding, m.FrameRounding * scale);
            Var(ImGuiStyleVar.GrabRounding, m.FrameRounding * scale);
            Var(ImGuiStyleVar.TabRounding, m.FrameRounding * scale);
            Var(ImGuiStyleVar.WindowBorderSize, m.Border * scale);
            Var(ImGuiStyleVar.FrameBorderSize, m.Border * scale);
            Var(ImGuiStyleVar.WindowPadding, m.Padding * scale);
            Var(ImGuiStyleVar.FramePadding, m.FramePadding * scale);
            Var(ImGuiStyleVar.ItemSpacing, m.Spacing * scale);
            var innerSpacing = new Vector2(6, 4) * scale;
            if (titleBar) innerSpacing.X = ThemeCatalog.TitleButtonSpacing(appearance.Style, scale, ImGui.GetStyle().TouchExtraPadding.X);
            Var(ImGuiStyleVar.ItemInnerSpacing, innerSpacing);
        }
        catch { Dispose(); throw; }
    }

    private void Color(ImGuiCol name, Vector4 value) { ImGui.PushStyleColor(name, value); colors++; }
    private void Var(ImGuiStyleVar name, float value) { ImGui.PushStyleVar(name, value); variables++; }
    private void Var(ImGuiStyleVar name, Vector2 value) { ImGui.PushStyleVar(name, value); variables++; }

    public void Dispose()
    {
        if (variables > 0) ImGui.PopStyleVar(variables);
        if (colors > 0) ImGui.PopStyleColor(colors);
        variables = colors = 0;
    }
}

// The host draws title-bar controls after Window.Draw returns. Restore the
// title-bar spacing before that happens, including early returns/exceptions.
internal readonly ref struct UiContentSpacingScope
{
    private readonly bool pushed;
    public UiContentSpacingScope(Appearance appearance)
    {
        pushed = appearance.Enabled;
        if (pushed) ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(6, 4) * ImGuiHelpers.GlobalScale);
    }
    public void Dispose() { if (pushed) ImGui.PopStyleVar(); }
}
