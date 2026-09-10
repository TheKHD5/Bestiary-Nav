using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BestiaryNav;

internal sealed class SettingsWindow(Configuration configuration, string? bindingIssue, Action save, Func<string> markerStatus,
    TravelController travel, Func<bool> travelAvailable, Action stopTravel, Action<bool> setAutoTravel,
    Action openCollection, Func<string> diagnostics, Action<bool> setLocationPopup)
    : Window("Bestiary Nav###BestiaryNavSettings")
{
    public override void Draw()
    {
        if (ImGui.Button("Collection")) openCollection();
        ImGui.SameLine();
        if (ImGui.Button("Copy diagnostics")) ImGui.SetClipboardText(diagnostics());
        Tip("Copies plugin compatibility, capture records, selected target, and travel status for troubleshooting.");
        if (!ImGui.BeginTabBar("SettingsTabs")) return;
        if (ImGui.BeginTabItem("Navigation"))
        {
            var enabled = configuration.EnableClickNavigation;
            if (ImGui.Checkbox("Enable entry click navigation", ref enabled))
            {
                configuration.EnableClickNavigation = enabled;
                save();
            }
            Tip("Left-click a Bestiary entry to open its spawn area, Duty Finder, or quest guidance. Settings save automatically.");
            var tracking = configuration.MapTrackingOnClick;
            if (ImGui.Checkbox("Location pop-up", ref tracking)) setLocationPopup(tracking);
            Tip("Open the map or Duty Finder when clicking Master's Bestiary entries. Turning this off also disables Auto Navigate, stops the current trip, and clears the search circle. Bestiary clicks then have no navigation actions. Explicit commands and collection actions remain available.");
            var radius = configuration.SpawnAreaRadius;
            ImGui.SetNextItemWidth(170);
            if (ImGui.SliderFloat("Search radius (yalms)", ref radius, 10, 200, "%.0f"))
            {
                configuration.SpawnAreaRadius = radius;
                save();
            }
            Tip("Approximate search circles around reported spawn coordinates, not verified spawn boundaries. Applies when you next select a beast.");
            var blockCombat = configuration.BlockInCombat;
            if (ImGui.Checkbox("Skip navigation while in combat", ref blockCombat))
            {
                configuration.BlockInCombat = blockCombat;
                save();
            }
            Tip("Skip map and duty navigation during combat. Automatic travel always stops when combat starts.");
            if (bindingIssue != null) ImGui.TextWrapped(bindingIssue);
            ImGui.Separator();
            var autoTravel = configuration.AutoTravel;
            if (ImGui.Checkbox("Auto Navigate", ref autoTravel))
                setAutoTravel(autoTravel);
            Tip("Travel when clicking an outdoor entry: teleport to an unlocked aetheryte, then walk to its capture area. Turning this on also enables Location pop-up. Normal teleport costs apply. Duties open in Duty Finder.\n\n" +
                (travelAvailable() ? "Lifestream and vnavmesh connected." : "Requires Lifestream and vnavmesh installed and enabled."));
            var cancelOnMovement = configuration.CancelTravelOnManualMovement;
            if (ImGui.Checkbox("Cancel travel on manual movement", ref cancelOnMovement))
            {
                configuration.CancelTravelOnManualMovement = cancelOnMovement;
                save();
            }
            Tip("Movement keys, the controller movement stick, or both mouse buttons cancel the current trip. Auto Navigate stays enabled for your next selection.");
            if (autoTravel && !travelAvailable()) ImGui.TextWrapped("Auto Navigate needs Lifestream + vnavmesh.");
            if (travel.Active)
            {
                ImGui.TextWrapped(travel.Status);
                if (ImGui.Button("Stop travel")) stopTravel();
            }
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Labels"))
        {
            var showMarkers = configuration.ShowUncapturedMarkers;
            if (ImGui.Checkbox("Highlight uncaptured beasts", ref showMarkers))
            {
                configuration.ShowUncapturedMarkers = showMarkers;
                save();
            }
            Tip("Labels appear above beasts and beside enemy-list rows. Green: at or below your level. Red: above your level. Labels disappear after capture. Duty beasts may award a gourd after the encounter.");
            var onlyBeastmaster = configuration.LabelsOnlyOnBeastmaster;
            if (ImGui.Checkbox("Only show labels on Beastmaster (BST)", ref onlyBeastmaster))
            {
                configuration.LabelsOnlyOnBeastmaster = onlyBeastmaster;
                save();
            }
            Tip("Show beast and enemy-list labels only while your equipped job is Beastmaster. Updates automatically when you switch jobs. Collection progress and map navigation remain available on every job.");
            var models = configuration.ShowModelLabels;
            if (ImGui.Checkbox("Above beasts", ref models)) { configuration.ShowModelLabels = models; save(); }
            ImGui.SameLine();
            var rows = configuration.ShowEnemyListLabels;
            if (ImGui.Checkbox("Enemy list", ref rows)) { configuration.ShowEnemyListLabels = rows; save(); }
            var scale = configuration.LabelScale;
            ImGui.SetNextItemWidth(190);
            if (ImGui.SliderFloat("Label size", ref scale, .75f, 2, "%.2fx")) { configuration.LabelScale = scale; save(); }
            var eligible = new Vector3(configuration.EligibleColor.X, configuration.EligibleColor.Y, configuration.EligibleColor.Z);
            if (ImGui.ColorEdit3("At or below my level", ref eligible)) { configuration.EligibleColor = new(eligible, 1); save(); }
            var above = new Vector3(configuration.AboveLevelColor.X, configuration.AboveLevelColor.Y, configuration.AboveLevelColor.Z);
            if (ImGui.ColorEdit3("Above my level", ref above)) { configuration.AboveLevelColor = new(above, 1); save(); }
            var range = configuration.MarkerRange;
            ImGui.SetNextItemWidth(190);
            if (ImGui.SliderFloat("Marker range (yalms)", ref range, 10, 100, "%.0f"))
            {
                configuration.MarkerRange = range;
                save();
            }
            Tip("Maximum distance for nearby labels. Each beast name counts once, even when several of the same beast are nearby.");
            if (showMarkers) ImGui.TextWrapped(markerStatus());
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private static void Tip(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(i)");
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    public void Open()
    {
        Flags = ImGuiWindowFlags.AlwaysAutoResize;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 0), MaximumSize = new Vector2(600, float.MaxValue) };
        IsOpen = true;
    }
}
