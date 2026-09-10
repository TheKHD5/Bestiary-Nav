using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BestiaryNav;

internal sealed class SettingsWindow(Configuration configuration, string? bindingIssue, Action save, Func<string> markerStatus,
    TravelController travel, Func<bool> travelAvailable, Action stopTravel, Action<bool> setAutoTravel)
    : Window("Bestiary Nav###BestiaryNavSettings")
{
    public override void Draw()
    {
        var enabled = configuration.EnableClickNavigation;
        if (ImGui.Checkbox("Enable entry click navigation", ref enabled))
        {
            configuration.EnableClickNavigation = enabled;
            save();
        }
        Tip("Left-click a Bestiary entry to open its map flag, Duty Finder, or quest guidance. Includes all 50 beasts. Settings save automatically.");
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
        Tip("Travel when clicking an outdoor entry: teleport to an unlocked aetheryte, then walk to its capture area. Normal teleport costs apply. Duties open in Duty Finder.\n\n" +
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
        ImGui.Separator();
        var showMarkers = configuration.ShowUncapturedMarkers;
        if (ImGui.Checkbox("Highlight uncaptured beasts", ref showMarkers))
        {
            configuration.ShowUncapturedMarkers = showMarkers;
            save();
        }
        Tip("Labels appear above beasts and beside enemy-list rows. Green: at or below your level. Red: above your level. Labels disappear after capture. Duty beasts may award a gourd after the encounter.");
        var range = configuration.MarkerRange;
        ImGui.SetNextItemWidth(190);
        if (ImGui.SliderFloat("Marker range (yalms)", ref range, 10, 100, "%.0f"))
        {
            configuration.MarkerRange = range;
            save();
        }
        Tip("Maximum distance for nearby labels. Each beast name counts once, even when several of the same beast are nearby.");
        if (showMarkers) ImGui.TextWrapped(markerStatus());
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
