using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BestiaryNav;

internal sealed class BestiaryQuickToggle(Configuration configuration, IGameGui gui,
    IClientState client, ICondition conditions, bool bindingActive, Action<bool> setAutoTravel, Action openSettings)
{
    public unsafe void Draw()
    {
        if (!bindingActive || !client.IsLoggedIn || gui.GameUiHidden ||
            conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51] ||
            conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78])
            return;
        var live = gui.GetAddonByName(BestiarySelectionReader.AddonName);
        if (live.IsNull || !live.IsReady || !live.IsVisible ||
            !NativeSnapshot.TryRead<AtkUnitBase>(live.Address, out var addon) ||
            !NativeSnapshot.TryRead<AtkResNode>((nint)addon.RootNode, out var root))
            return;
        var width = root.Width * root.Transform.M11;
        if (!float.IsFinite(root.ScreenX) || !float.IsFinite(root.ScreenY) || !float.IsFinite(width) || width <= 0)
            return;

        const string label = "Auto Navigate";
        var viewport = ImGui.GetMainViewport();
        var padding = new Vector2(8, 4);
        var frameHeight = ImGui.GetFrameHeight();
        var size = new Vector2(ImGui.CalcTextSize(label).X + frameHeight +
            ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().ItemSpacing.X + frameHeight,
            frameHeight) + padding * 2;
        var min = viewport.Pos + new Vector2(4);
        var max = viewport.Pos + viewport.Size - size - new Vector2(4);
        if (max.X < min.X || max.Y < min.Y) return;
        // Native screen coordinates are relative to the game viewport. Recompute
        // each draw so the control follows dragging and HUD scaling.
        var position = viewport.Pos + new Vector2(root.ScreenX,
            root.ScreenY - size.Y - 4);
        position = Vector2.Clamp(position, min, max);
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.9f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
        try
        {
            var visible = ImGui.Begin("##BestiaryNavQuickToggle", ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoFocusOnAppearing);
            try
            {
                if (!visible) return;
                var enabled = configuration.AutoTravel;
                if (ImGui.Checkbox(label, ref enabled)) setAutoTravel(enabled);
                ImGui.SameLine();
                if (ImGuiComponents.IconButton("OpenSettings", FontAwesomeIcon.Cog, new Vector2(frameHeight)))
                    openSettings();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Settings (/bnav config)");
            }
            finally { ImGui.End(); }
        }
        finally { ImGui.PopStyleVar(2); }
    }
}
