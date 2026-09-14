using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace BestiaryNav;

internal sealed class TravelPlanBuilder(IDataManager data, IAetheryteList unlocked)
{
    public TravelPlan Build(MapLocation target, float radius)
    {
        var map = data.GetExcelSheet<Map>().GetRowOrDefault(target.MapId);
        if (map == null || map.Value.TerritoryType.RowId != target.TerritoryTypeId ||
            !MapCoordinates.IsOnMap(target.X, map.Value.SizeFactor) || !MapCoordinates.IsOnMap(target.Y, map.Value.SizeFactor))
            throw new InvalidOperationException("Invalid map destination for automatic travel.");
        // Exactly the rounded world X/Z used by the native spawn-circle marker.
        var center = SpawnAreaCoordinates.Convert(target, map.Value.SizeFactor, map.Value.OffsetX, map.Value.OffsetY, radius);
        var world = center.WorldPoint;
        uint nearest = 0;
        byte subIndex = 0;
        var bestDistance = float.PositiveInfinity;
        foreach (var entry in unlocked)
        {
            if (entry.TerritoryId != target.TerritoryTypeId || entry.SubIndex != 0) continue;
            var aetheryte = data.GetExcelSheet<Aetheryte>().GetRowOrDefault(entry.AetheryteId);
            if (aetheryte == null || !aetheryte.Value.IsAetheryte || aetheryte.Value.Territory.RowId != target.TerritoryTypeId) continue;
            var markerMap = data.GetExcelSheet<Map>().GetRowOrDefault(aetheryte.Value.Map.RowId);
            if (markerMap == null || markerMap.Value.TerritoryType.RowId != target.TerritoryTypeId || markerMap.Value.SizeFactor == 0) continue;
            foreach (var marker in data.GetSubrowExcelSheet<MapMarker>().GetRow(markerMap.Value.MapMarkerRange))
            {
                if (marker.DataType != 3 || marker.DataKey.RowId != entry.AetheryteId) continue;
                var position = new Vector2(
                    MapCoordinates.MarkerToWorld(marker.X, markerMap.Value.SizeFactor, markerMap.Value.OffsetX),
                    MapCoordinates.MarkerToWorld(marker.Y, markerMap.Value.SizeFactor, markerMap.Value.OffsetY));
                var distance = Vector2.DistanceSquared(new(world.X, world.Z), position);
                if (!float.IsFinite(distance) || distance >= bestDistance) continue;
                nearest = entry.AetheryteId;
                subIndex = entry.SubIndex;
                bestDistance = distance;
            }
        }
        return new(target.TerritoryTypeId, world, nearest, subIndex, $"{target.Area} ({target.X:F1}, {target.Y:F1})", center.Radius, target.TravelFloor);
    }
}
