using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BestiaryNav;

public sealed class CollectionMetadata
{
    public List<BeastAcquisition> Beasts { get; set; } = [];
    public Dictionary<uint, BeastAcquisition> BuildIndex(IReadOnlyDictionary<uint, MonsterEntry> catalog)
    {
        var index = new Dictionary<uint, BeastAcquisition>();
        foreach (var beast in Beasts)
            if (!catalog.ContainsKey(beast.BestiaryNumber) || beast.MinimumLevel is < 1 or > 100 ||
                !index.TryAdd(beast.BestiaryNumber, beast))
                throw new InvalidOperationException("Invalid collection planning metadata.");
        if (index.Count != catalog.Count) throw new InvalidOperationException("Incomplete collection planning metadata.");
        return index;
    }
}

public sealed class BeastAcquisition
{
    public uint BestiaryNumber { get; set; }
    public int MinimumLevel { get; set; }
    public string Gourd { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed record CollectionSnapshot(bool Ready, ulong Captured, byte Level, uint Territory, uint Map,
    Vector2 MapPosition, string Status)
{
    public static readonly CollectionSnapshot Empty = new(false, 0, 0, 0, 0, default, "Open Master's Bestiary once to load capture records.");
}

public static class CollectionPlanner
{
    public static string AcquisitionLabel(MonsterEntry beast) => beast.NavigationKind switch
    {
        "duty" => "Capture / gourd encounter",
        "quest" => "Quest reward",
        _ => "Capture target",
    };

    public static string Area(MonsterEntry beast) => beast.Duty?.Name ?? beast.Locations.FirstOrDefault()?.Area ?? "Quest rewards";

    public static MapLocation? PreferredLocation(MonsterEntry beast, CollectionSnapshot state) => beast.Locations
        .OrderBy(p => p.TerritoryTypeId == state.Territory ? 0 : 1)
        .ThenBy(p => p.MapId == state.Map ? Vector2.DistanceSquared(new(p.X, p.Y), state.MapPosition) : float.MaxValue)
        .FirstOrDefault();

    public static MonsterEntry? Recommend(IEnumerable<MonsterEntry> beasts, IReadOnlyDictionary<uint, BeastAcquisition> metadata,
        CollectionSnapshot state)
    {
        if (!state.Ready || state.Level == 0 || state.Territory == 0) return null;
        return beasts.Where(b => CaptureRules.IsUncaptured(state.Captured, b.BestiaryNumber) &&
                metadata.TryGetValue(b.BestiaryNumber, out var info) && info.MinimumLevel <= state.Level)
            .OrderBy(b => b.Locations.Any(p => p.TerritoryTypeId == state.Territory) || b.Duty?.TerritoryTypeId == state.Territory ? 0 : 1)
            .ThenBy(b => b.NavigationKind == "map" ? 0 : b.NavigationKind == "duty" ? 1 : 2)
            .ThenBy(b => b.Locations.Where(p => p.MapId == state.Map)
                .Select(p => Vector2.DistanceSquared(new(p.X, p.Y), state.MapPosition)).DefaultIfEmpty(float.MaxValue).Min())
            .ThenBy(b => metadata[b.BestiaryNumber].MinimumLevel)
            .ThenBy(b => b.BestiaryNumber).FirstOrDefault();
    }
}
