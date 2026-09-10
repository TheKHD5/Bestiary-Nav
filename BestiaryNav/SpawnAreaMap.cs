using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace BestiaryNav;

internal sealed class SpawnAreaMap(IDataManager data, bool compatible)
{
    private uint ownedMap;
    private string ownedTooltip = "";
    private int ownedCount;

    public unsafe bool Open(IReadOnlyList<MapLocation> locations, string beastName, float radius, out string reason)
    {
        reason = "";
        if (!compatible) { reason = "Spawn-area circles need a compatible Bestiary Nav update."; return false; }
        if (locations.Count == 0) { reason = "No outdoor spawn location is available."; return false; }
        var first = locations[0];
        var map = data.GetExcelSheet<Map>().GetRowOrDefault(first.MapId);
        if (map == null || data.GetExcelSheet<TerritoryType>().GetRowOrDefault(first.TerritoryTypeId) == null ||
            map.Value.TerritoryType.RowId != first.TerritoryTypeId)
        { reason = "Unknown or mismatched territory/map."; return false; }
        var points = locations.Where(p => p.MapId == first.MapId && p.TerritoryTypeId == first.TerritoryTypeId).ToArray();
        if (points.Length > 12) { reason = "This map has more spawn areas than the game can display at once."; return false; }
        var centers = points.Select(p => SpawnAreaCoordinates.Convert(p, map.Value.SizeFactor, map.Value.OffsetX, map.Value.OffsetY, radius)).ToArray();
        var agent = AgentMap.Instance();
        if (agent == null) { reason = "The map is unavailable."; return false; }
        // Like opening a gathering-log search, replace its temporary search context.
        // Leave the user's persistent <flag>, quest markers and party markers alone.
        agent->TempMapMarkerCount = 0;
        ownedTooltip = $"Bestiary Nav: {beastName} — approximate search area";
        foreach (var center in centers)
            agent->AddGatheringTempMarker(center.X, center.Y, center.Radius, 0, 4, ownedTooltip);
        ownedCount = centers.Length;
        ownedMap = first.MapId;
        agent->OpenMap(first.MapId, first.TerritoryTypeId, $"{beastName} — spawn area", FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.GatheringLog);
        return true;
    }

    public unsafe void ClearOwned()
    {
        if (!compatible || ownedCount == 0) return;
        var agent = AgentMap.Instance();
        if (agent != null && agent->SelectedMapId == ownedMap && agent->TempMapMarkerCount == ownedCount)
        {
            var allOwned = true;
            for (var i = 0; i < ownedCount; i++)
                allOwned &= agent->TempMapMarkers[i].TooltipText.ToString() == ownedTooltip;
            if (allOwned) agent->TempMapMarkerCount = 0;
        }
        ownedCount = 0;
    }
}
