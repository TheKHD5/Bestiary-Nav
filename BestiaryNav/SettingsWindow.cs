using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BestiaryNav;

internal sealed class SettingsWindow(Configuration configuration, bool bindingActive, Action save, Func<string> markerStatus,
    TravelController travel, Func<bool> travelAvailable, Action stopTravel)
    : Window("Bestiary Nav###BestiaryNavSettings")
{
    public override void Draw()
    {
        ImGui.TextWrapped("Left-click a numbered entry in Master's Bestiary to open its acquisition map flag or target duty.");
        ImGui.Spacing();
        var enabled = configuration.EnableClickNavigation;
        if (ImGui.Checkbox("Enable entry click navigation", ref enabled))
        {
            configuration.EnableClickNavigation = enabled;
            save();
        }
        var blockCombat = configuration.BlockInCombat;
        if (ImGui.Checkbox("Skip navigation while in combat", ref blockCombat))
        {
            configuration.BlockInCombat = blockCombat;
            save();
        }
        ImGui.Spacing();
        ImGui.TextWrapped(!bindingActive
            ? "Click navigation is unavailable for this game or Dalamud version. Update Bestiary Nav; manual commands remain available."
            : enabled ? "Entry click navigation is active." : "Entry click navigation is turned off.");
        ImGui.Separator();
        var autoTravel = configuration.AutoTravel;
        if (ImGui.Checkbox("Automatically travel to selected beasts", ref autoTravel))
        {
            configuration.AutoTravel = autoTravel;
            if (!autoTravel) stopTravel();
            save();
        }
        ImGui.TextWrapped(travelAvailable() ? "vnavmesh and Lifestream connected." : "Auto travel needs vnavmesh and Lifestream installed and enabled.");
        ImGui.TextWrapped("Teleports to an unlocked aetheryte, then walks to outdoor capture areas. Normal teleport costs apply. Duties open in Duty Finder.");
        ImGui.TextWrapped(travel.Status);
        if (ImGui.Button("Stop travel")) stopTravel();
        ImGui.Separator();
        var showMarkers = configuration.ShowUncapturedMarkers;
        if (ImGui.Checkbox("Tag nearby uncaptured beasts", ref showMarkers))
        {
            configuration.ShowUncapturedMarkers = showMarkers;
            save();
        }
        var range = configuration.MarkerRange;
        if (ImGui.SliderFloat("Marker range (yalms)", ref range, 10, 100, "%.0f"))
        {
            configuration.MarkerRange = range;
            save();
        }
        ImGui.TextWrapped(markerStatus());
        ImGui.TextWrapped("Green: at or below your level. Red: above your level.");
        ImGui.TextWrapped("Marks known capture targets above their models and beside existing enemy-list rows. Duty targets may award a gourd after the encounter.");
        ImGui.Separator();
        ImGui.TextWrapped("Includes all 50 beasts. Cu Sith shows quest guidance because it has no map or duty acquisition.");
        ImGui.TextWrapped("Changes are saved automatically.");
    }

    public void Open()
    {
        Size = new Vector2(500, 610);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(320, 240), MaximumSize = new Vector2(float.MaxValue) };
        IsOpen = true;
    }
}
