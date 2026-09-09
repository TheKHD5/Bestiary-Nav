using System;
using System.Collections.Generic;
using System.Linq;

namespace BestiaryNav;

public sealed class CaptureTargetDatabase
{
    public string GameVersion { get; set; } = "";
    public List<CaptureTargetRecord> Targets { get; set; } = [];

    public Dictionary<(uint Territory, uint NameId), uint> BuildIndex(IReadOnlyDictionary<uint, MonsterEntry> monsters)
    {
        var result = new Dictionary<(uint, uint), uint>();
        foreach (var target in Targets)
        {
            if (!monsters.TryGetValue(target.BestiaryNumber, out var beast) ||
                beast.CaptureTarget != target.CaptureTarget || target.BNpcNameIds.Length == 0 ||
                target.BNpcNameIds.Any(id => id == 0))
                throw new InvalidOperationException("Invalid capture target mapping.");
            var territories = beast.Locations.Select(p => p.TerritoryTypeId).ToHashSet();
            if (beast.Duty != null)
                territories.Add(beast.Duty.TerritoryTypeId);
            foreach (var territory in territories)
            foreach (var nameId in target.BNpcNameIds)
            {
                var key = (territory, nameId);
                if (result.TryGetValue(key, out var other) && other != target.BestiaryNumber)
                    throw new InvalidOperationException("Ambiguous capture target mapping.");
                result[key] = target.BestiaryNumber;
            }
        }
        return result;
    }
}

public sealed class CaptureTargetRecord
{
    public uint BestiaryNumber { get; set; }
    public string CaptureTarget { get; set; } = "";
    public uint[] BNpcNameIds { get; set; } = [];
}

public static class CaptureRules
{
    public static bool IsUncaptured(ulong captured, uint bestiaryNumber) =>
        bestiaryNumber is >= 1 and <= 50 && (captured & (1UL << (int)(bestiaryNumber - 1))) == 0;

    public static bool IsInRange(float distanceSquared, float range) =>
        float.IsFinite(distanceSquared) && distanceSquared >= 0 && float.IsFinite(range) &&
        range is >= 10 and <= 100 && distanceSquared <= range * range;
}
