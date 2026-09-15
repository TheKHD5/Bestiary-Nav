using System;
using System.Collections.Generic;
using System.Linq;

namespace BestiaryNav;

public sealed class FarmingTargetFilter
{
    public bool DefaultTarget { get; set; } = true;
    public Dictionary<uint, bool> Overrides { get; set; } = [];
    public bool Allows(uint nameId) => nameId != 0 && Overrides.GetValueOrDefault(nameId, DefaultTarget);
    public void Set(uint nameId, bool target)
    {
        if (nameId == 0) return;
        if (target == DefaultTarget) Overrides.Remove(nameId);
        else Overrides[nameId] = target;
    }
    public void SetAll(bool target) { DefaultTarget = target; Overrides.Clear(); }
    public void Normalize() { Overrides ??= []; Overrides.Remove(0); }
}

internal sealed class FarmingTargetDatabase
{
    public List<FarmingTargetSpecies> Species { get; set; } = [];
}

internal sealed record FarmingTargetSpecies
{
    public uint PlaceNameId { get; init; }
    public uint NameId { get; init; }
    public string Name { get; init; } = "";
    public int MinimumLevel { get; init; }
    public int MaximumLevel { get; init; }
    public bool Observed { get; init; }
    public string Levels => MinimumLevel == 0 ? "level unknown" : MinimumLevel == MaximumLevel ? $"Lv. {MinimumLevel}" : $"Lv. {MinimumLevel}–{MaximumLevel}";
}

internal sealed record FarmingTargetZone(uint TerritoryId, uint PlaceNameId, string Name);

internal static class FarmingSpeciesChoices
{
    // Catalog ranges describe possible zone levels. Actual spawned levels and
    // combat filters still govern pulls; unknown levels remain in the full editor.
    public static IReadOnlyList<FarmingTargetSpecies> InRange(IEnumerable<FarmingTargetSpecies> species, int level, FarmingOptions options) =>
        level <= 0 ? [] : species.Where(s => s.MinimumLevel > 0 && s.MaximumLevel >= level + options.MinimumAbove &&
            s.MinimumLevel <= level + options.MaximumAbove).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.NameId).ToArray();
}

// Location records populate the list before travelling. Live sightings extend
// it without requiring a Bestiary entry or changing any saved targeting choice.
internal sealed class FarmingTargetCatalog
{
    private readonly Dictionary<(uint Zone, uint Name), FarmingTargetSpecies> species = [];
    public void Add(FarmingTargetSpecies entry)
    {
        if (entry.PlaceNameId == 0 || entry.NameId == 0 || string.IsNullOrWhiteSpace(entry.Name) ||
            entry.MinimumLevel < 0 || entry.MaximumLevel < entry.MinimumLevel || entry.MaximumLevel > 100) return;
        var key = (entry.PlaceNameId, entry.NameId);
        if (species.TryGetValue(key, out var old))
            entry = entry with
            {
                MinimumLevel = old.MinimumLevel == 0 ? entry.MinimumLevel : entry.MinimumLevel == 0 ? old.MinimumLevel : Math.Min(old.MinimumLevel, entry.MinimumLevel),
                MaximumLevel = Math.Max(old.MaximumLevel, entry.MaximumLevel),
                Observed = old.Observed || entry.Observed,
            };
        species[key] = entry;
    }
    public IReadOnlyList<FarmingTargetSpecies> Choices(uint placeNameId) => species.Values.Where(s => s.PlaceNameId == placeNameId)
        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.NameId).ToArray();
}
