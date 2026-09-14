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
    IClientState client, ICondition conditions, bool bindingActive, Action<bool> setAutoTravel, Action openSettings,
    Action openCollection, TravelController travel, Action stopTravel, Action<bool> setLocationPopup, CaptureRun captureRun,
    Func<MonsterEntry?> nextTarget, Action<uint> goToNextTarget, CaptureAllController captureAll, Action<bool> setCaptureAll)
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

        using var theme = new UiThemeScope(configuration.Appearance);
        const string label = "Auto Navigate";
        const string trackingLabel = "Location pop-up";
        var viewport = ImGui.GetMainViewport();
        var padding = new Vector2(8, 4);
        var frameHeight = ImGui.GetFrameHeight();
        var size = new Vector2(ImGui.CalcTextSize(label).X + frameHeight +
            ImGui.GetStyle().ItemInnerSpacing.X + 2 * (ImGui.GetStyle().ItemSpacing.X + frameHeight),
            frameHeight) + padding * 2;
        size.X += ImGui.CalcTextSize(trackingLabel).X + frameHeight +
            ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().ItemSpacing.X;
        size.Y += 2 * (frameHeight + ImGui.GetStyle().ItemSpacing.Y);
        var nextButtonWidth = ImGui.CalcTextSize(label).X + ImGui.CalcTextSize(trackingLabel).X + 2 * frameHeight +
            2 * ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().ItemSpacing.X;
        var active = travel.Active || captureRun.Active || captureAll.Enabled;
        var status = captureRun.Active ? captureRun.CompactStatus : captureAll.Enabled ? "Capture all: waiting…" : travel.CompactStatus;
        if (active)
            size.X += ImGui.CalcTextSize(status).X + ImGui.CalcTextSize("Stop").X +
                2 * ImGui.GetStyle().ItemSpacing.X + 2 * ImGui.GetStyle().FramePadding.X;
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
        ImGui.SetNextWindowBgAlpha(configuration.Appearance.Enabled ? configuration.Appearance.Opacity : 0.9f);
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
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Travel to the selected beast. Turning this on also enables Location pop-up.");
                ImGui.SameLine();
                var tracking = configuration.MapTrackingOnClick;
                if (ImGui.Checkbox(trackingLabel, ref tracking)) setLocationPopup(tracking);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open the map or Duty Finder when clicking Bestiary entries.\nTurning this off also disables Auto Navigate and stops the current trip.");
                ImGui.SameLine();
                if (ImGuiComponents.IconButton("OpenSettings", FontAwesomeIcon.Cog, new Vector2(frameHeight)))
                    openSettings();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Settings (/bnav config)");
                ImGui.SameLine();
                if (ImGuiComponents.IconButton("OpenCollection", FontAwesomeIcon.Book, new Vector2(frameHeight))) openCollection();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Collection, favorites, and Where next?");
                if (active)
                {
                    ImGui.SameLine(); ImGui.TextUnformatted(status);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(captureRun.Active ? captureRun.Status : captureAll.Enabled ? captureAll.Status : travel.Status);
                    ImGui.SameLine(); if (ImGui.Button("Stop")) stopTravel();
                }
                // A separate second row keeps this action below both toggles,
                // without competing with their click targets or the gear button.
                var next = nextTarget();
                var unavailable = !configuration.MapTrackingOnClick || !configuration.EnableClickNavigation;
                var inCombat = conditions[ConditionFlag.InCombat];
                ImGui.BeginDisabled(next == null || unavailable || inCombat);
                if (ImGui.Button("Go to next target", new Vector2(nextButtonWidth, frameHeight)) && next != null)
                    goToNextTarget(next.BestiaryNumber);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(unavailable ? "Enable Location pop-up and entry click navigation first." :
                        inCombat ? "Finish combat before moving to the next target." :
                        next == null ? "No eligible uncaptured target. Open the Bestiary to load capture records." :
                        $"Go to #{next.BestiaryNumber} {next.DisplayName}.\nUses the collection's next uncaptured target at your level. Starts travel to outdoor targets; duties open in Duty Finder. Uses the capture run if enabled.");
                var all = captureAll.Enabled;
                if (ImGui.Checkbox("Capture all available", ref all)) setCaptureAll(all);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Capture eligible overworld entries in sequence. Retry failures while enabled.\nStops when none remain at your level. /bnav stop cancels the batch.\n" + captureAll.Status);
            }
            finally { ImGui.End(); }
        }
        finally { ImGui.PopStyleVar(2); }
    }
}
