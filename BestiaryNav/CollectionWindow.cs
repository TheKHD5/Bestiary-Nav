using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace BestiaryNav;

internal sealed class CollectionWindow(IReadOnlyDictionary<uint, MonsterEntry> catalog,
    IReadOnlyDictionary<uint, BeastAcquisition> metadata, Configuration config, Func<CollectionSnapshot> snapshot,
    Action<uint> navigate, Action save, Action openBestiary) : Window("Bestiary Nav — Collection###BestiaryNavCollection")
{
    private string search = "";
    private bool remainingOnly = true;
    private bool favoritesOnly;
    private bool levelOnly;

    public override void Draw()
    {
        var state = snapshot();
        if (!state.Ready)
        {
            ImGui.TextWrapped(state.Status);
            if (ImGui.Button("Open Master's Bestiary")) openBestiary();
        }
        else ImGui.TextUnformatted($"{BitOperations.PopCount(state.Captured & ((1UL << 50) - 1))}/50 collected · Level {state.Level}");
        var next = CollectionPlanner.Recommend(catalog.Values, metadata, state);
        ImGui.BeginDisabled(next == null);
        if (ImGui.Button("Where next?") && next != null) navigate(next.BestiaryNumber);
        ImGui.EndDisabled();
        if (next != null)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"#{next.BestiaryNumber} {next.DisplayName} · {CollectionPlanner.Area(next)}");
        }
        else if (state.Ready) { ImGui.SameLine(); ImGui.TextDisabled("No remaining target at this level."); }
        Tip("Suggests a remaining beast at your level, prioritizing your current area and nearby outdoor locations. Uses Auto Navigate if enabled. Quest and duty unlocks still apply.");
        ImGui.SetNextItemWidth(240);
        ImGui.InputTextWithHint("##SearchBeasts", "Search beast, target, or area…", ref search, 100);
        ImGui.Checkbox("Remaining", ref remainingOnly); ImGui.SameLine();
        ImGui.Checkbox("Favorites", ref favoritesOnly); ImGui.SameLine();
        ImGui.Checkbox("At my level", ref levelOnly);
        ImGui.Separator();
        var groups = catalog.Values.GroupBy(CollectionPlanner.Area).OrderBy(g =>
            g.Any(b => b.Locations.Any(p => p.TerritoryTypeId == state.Territory) || b.Duty?.TerritoryTypeId == state.Territory) ? 0 : 1)
            .ThenBy(g => g.Key);
        foreach (var group in groups)
        {
            var entries = group.Where(b => (!remainingOnly || !state.Ready || CaptureRules.IsUncaptured(state.Captured, b.BestiaryNumber)) &&
                (!favoritesOnly || config.Favorites.Contains(b.BestiaryNumber)) &&
                (!levelOnly || state.Level > 0 && metadata[b.BestiaryNumber].MinimumLevel <= state.Level) &&
                (search.Length == 0 || $"{b.BestiaryNumber} {b.DisplayName} {b.CaptureTarget} {group.Key}".Contains(search, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(b => b.BestiaryNumber).ToArray();
            if (entries.Length == 0) continue;
            var collected = state.Ready ? group.Count(b => !CaptureRules.IsUncaptured(state.Captured, b.BestiaryNumber)) : 0;
            var progress = state.Ready ? $"{collected}/{group.Count()}" : $"?/{group.Count()}";
            if (!ImGui.CollapsingHeader($"{group.Key} ({progress})", ImGuiTreeNodeFlags.DefaultOpen)) continue;
            foreach (var beast in entries)
            {
                ImGui.PushID((int)beast.BestiaryNumber);
                var favorite = config.Favorites.Contains(beast.BestiaryNumber);
                ImGui.PushStyleColor(ImGuiCol.Text, favorite ? new Vector4(1, .8f, .2f, 1) : new Vector4(.5f, .5f, .5f, 1));
                var toggleFavorite = ImGuiComponents.IconButton("Favorite", FontAwesomeIcon.Star);
                ImGui.PopStyleColor();
                if (toggleFavorite)
                {
                    if (favorite) config.Favorites.Remove(beast.BestiaryNumber); else config.Favorites.Add(beast.BestiaryNumber);
                    save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(favorite ? "Remove favorite" : "Favorite this beast");
                ImGui.SameLine();
                if (ImGui.Selectable($"#{beast.BestiaryNumber} {beast.DisplayName}  ·  Lv. {metadata[beast.BestiaryNumber].MinimumLevel}"))
                    navigate(beast.BestiaryNumber);
                if (ImGui.IsItemHovered())
                    Tooltip($"{CollectionPlanner.AcquisitionLabel(beast)}: {beast.CaptureTarget}\n{beast.AcquisitionNote}\n{metadata[beast.BestiaryNumber].Gourd}");
                ImGui.Indent(30);
                var collectedEntry = state.Ready && !CaptureRules.IsUncaptured(state.Captured, beast.BestiaryNumber);
                ImGui.TextDisabled((collectedEntry ? "Collected · " : "") + CollectionPlanner.AcquisitionLabel(beast));
                ImGui.Unindent(30);
                ImGui.PopID();
            }
        }
    }

    private static void Tip(string text)
    {
        ImGui.SameLine(); ImGui.TextDisabled("(i)");
        if (ImGui.IsItemHovered()) Tooltip(text);
    }

    private static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    public void Open()
    {
        Size = new Vector2(650, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(460, 300), MaximumSize = new(1000, 1000) };
        IsOpen = true;
    }
}
