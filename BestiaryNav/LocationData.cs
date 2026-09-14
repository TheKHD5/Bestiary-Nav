using System;
using System.Collections.Generic;

namespace BestiaryNav;

public sealed class LocationDatabase
{
    public int SchemaVersion { get; set; }
    // Published Bestiary numbers (1..50), not native IDs, BNpcName IDs or renderer indices.
    public string IdSpace { get; set; } = "masters-bestiary-number";
    public List<MonsterEntry> Monsters { get; set; } = [];

    public Dictionary<uint, MonsterEntry> BuildIndex()
    {
        if (SchemaVersion != 2 || Monsters == null || IdSpace != "masters-bestiary-number")
            throw new InvalidOperationException("Invalid location database header.");

        var index = new Dictionary<uint, MonsterEntry>();
        foreach (var entry in Monsters)
        {
            if (entry == null || entry.BestiaryNumber == 0 || entry.Locations == null || string.IsNullOrWhiteSpace(entry.DisplayName))
                throw new InvalidOperationException("A beast needs a nonzero Bestiary number and a display name.");
            if (!index.TryAdd(entry.BestiaryNumber, entry))
                throw new InvalidOperationException($"Duplicate Bestiary number: {entry.BestiaryNumber}.");
            switch (entry.NavigationKind)
            {
                case "map" when entry.Locations.Count > 0 && entry.Duty == null:
                case "duty" when entry.Locations.Count == 0 && entry.Duty is { ContentFinderConditionId: > 0, TerritoryTypeId: > 0 }:
                case "quest" when entry.Locations.Count == 0 && entry.Duty == null && !string.IsNullOrWhiteSpace(entry.AcquisitionNote):
                    break;
                default:
                    throw new InvalidOperationException($"Invalid destination for Bestiary #{entry.BestiaryNumber}.");
            }
            foreach (var point in entry.Locations)
                if (point == null || point.TerritoryTypeId == 0 || point.MapId == 0 ||
                    !float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.TravelFloor is { IsValid: false })
                    throw new InvalidOperationException($"Invalid location for Bestiary #{entry.BestiaryNumber}.");
        }
        return index;
    }
}

public sealed class MonsterEntry
{
    public uint BestiaryNumber { get; set; }
    public string DisplayName { get; set; } = ""; // Informational; never used to identify a native selection.
    public string Classification { get; set; } = "";
    public string CaptureTarget { get; set; } = "";
    public string NavigationKind { get; set; } = "map";
    public string AcquisitionNote { get; set; } = "";
    public List<string> Sources { get; set; } = [];
    public List<MapLocation> Locations { get; set; } = [];
    public DutyDestination? Duty { get; set; }
}

public sealed class DutyDestination
{
    public uint ContentFinderConditionId { get; set; }
    public uint TerritoryTypeId { get; set; }
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed record NavigationRequest(MapLocation? Location, DutyDestination? Duty, string Notice)
{
    public uint BestiaryNumber { get; init; }
    public bool FromBestiaryClick { get; init; }
    public bool FromCaptureAll { get; init; }
    public static NavigationRequest ForBeast(MonsterEntry entry) => entry.NavigationKind switch
    {
        "map" => new(entry.Locations[0], null,
            $"{entry.DisplayName}: {entry.CaptureTarget} — {entry.Locations[0].Note}"),
        "duty" => new(null, entry.Duty,
            $"{entry.DisplayName}: {entry.CaptureTarget} — {entry.Duty!.Name}. {entry.Duty.Note}"),
        "quest" => new(null, null, $"{entry.DisplayName}: {entry.AcquisitionNote}"),
        _ => throw new InvalidOperationException("Unknown navigation kind."),
    };
}

public sealed class MapLocation
{
    // Optional verified world-height band for a stacked underground destination.
    // Such locations use ground paths to enter through the connected tunnel.
    public TravelFloor? TravelFloor { get; set; }
    public uint TerritoryTypeId { get; set; }
    public uint MapId { get; set; }
    // Human-readable map coordinates, not world coordinates or payload raw integers.
    public float X { get; set; }
    public float Y { get; set; }
    public string Note { get; set; } = "";
    public string Area { get; set; } = "";
    public string Source { get; set; } = "";
    public string Precision { get; set; } = "reported-map-coordinate";
}

public sealed class TravelFloor
{
    public float MinimumY { get; set; }
    public float MaximumY { get; set; }
    public bool IsValid => float.IsFinite(MinimumY) && float.IsFinite(MaximumY) &&
        MinimumY >= -1024 && MaximumY <= 1024 && MaximumY >= MinimumY && MaximumY - MinimumY <= 10;
    public bool Contains(float y) => IsValid && float.IsFinite(y) && y >= MinimumY && y <= MaximumY;
}

public static class MapCoordinates
{
    public static float MarkerToWorld(short pixel, ushort sizeFactor, short offset)
    {
        if (sizeFactor == 0) throw new ArgumentOutOfRangeException(nameof(sizeFactor));
        return (pixel - 1024f) / (sizeFactor / 100f) - offset;
    }

    // World X maps to map X; world Z maps to map Y. World Y is altitude.
    public static float WorldToMap(float worldAxis, ushort sizeFactor, short offset)
    {
        if (!float.IsFinite(worldAxis) || sizeFactor == 0)
            throw new ArgumentOutOfRangeException(nameof(worldAxis));
        var scale = sizeFactor / 100f;
        return 1f + (41f / scale) * (((worldAxis + offset) * scale + 1024f) / 2048f);
    }

    public static float MapToWorld(float mapAxis, ushort sizeFactor, short offset)
    {
        if (!float.IsFinite(mapAxis) || sizeFactor == 0)
            throw new ArgumentOutOfRangeException(nameof(mapAxis));
        var scale = sizeFactor / 100f;
        return (((mapAxis - 1f) * scale * 2048f / 41f) - 1024f) / scale - offset;
    }

    public static bool IsOnMap(float coordinate, ushort sizeFactor) =>
        sizeFactor != 0 && float.IsFinite(coordinate) && coordinate >= 1f &&
        coordinate <= 1f + 41f / (sizeFactor / 100f);
}
