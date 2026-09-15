using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace BestiaryNav;

public sealed class FarmingOptions
{
    public int MinimumAbove { get; set; } = 1;
    public int MaximumAbove { get; set; } = 5;
    public string SelectedGroup { get; set; } = ""; // Legacy single choice; migrated by Normalize.
    public List<string> SelectedGroups { get; set; } = []; // Empty = Automatic.
    public List<FarmingPatrol> Patrols { get; set; } = [];
    public string SelectedPatrol { get; set; } = ""; // Empty = catalog groups.
    public Dictionary<uint, FarmingTargetFilter> AreaTargets { get; set; } = []; // Territory -> target/ignore species.
    public bool HasEnabledTargets(uint territory) => !AreaTargets.TryGetValue(territory, out var filter) ||
        filter.DefaultTarget || filter.Overrides.Values.Any(v => v);
    public bool AllowsTarget(uint territory, uint nameId) => nameId != 0 &&
        (!AreaTargets.TryGetValue(territory, out var filter) || filter.Allows(nameId));
    public bool SummonChocobo { get; set; }
    public bool UseFood { get; set; }
    public uint FoodId { get; set; } // Includes HQ offset, so the selected quality is preserved.
    public bool ParticipateInFates { get; set; }
    public bool IgnoreNotoriousMonsters { get; set; } = true;
    public bool AutoRespawn { get; set; } = true;
    public int TargetLevel { get; set; } // 0 = no requested stop level.
    public void Normalize()
    {
        MinimumAbove = Math.Clamp(MinimumAbove, 1, 10);
        MaximumAbove = Math.Clamp(MaximumAbove, MinimumAbove, 10);
        SelectedGroups ??= [];
        if (SelectedGroups.Count == 0 && !string.IsNullOrWhiteSpace(SelectedGroup)) SelectedGroups.Add(SelectedGroup);
        SelectedGroups = SelectedGroups.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        SelectedGroup = "";
        Patrols ??= [];
        Patrols.RemoveAll(p => p == null);
        foreach (var patrol in Patrols) patrol.Normalize();
        Patrols = Patrols.DistinctBy(p => p.Id).ToList();
        SelectedPatrol ??= "";
        AreaTargets ??= [];
        foreach (var key in AreaTargets.Keys.ToArray())
        {
            if (key == 0 || AreaTargets[key] == null) AreaTargets.Remove(key);
            else AreaTargets[key].Normalize();
        }
        TargetLevel = Math.Clamp(TargetLevel, 0, 100);
    }
}

internal sealed class FarmingDatabase
{
    public List<FarmingArea> Areas { get; set; } = [];
}

internal sealed class FarmingArea
{
    public uint BestiaryNumber { get; set; }
    public string Name { get; set; } = "";
    public int MinimumLevel { get; set; }
    public int MaximumLevel { get; set; }
    public MapLocation Location { get; set; } = new();
    public string Source { get; set; } = "";
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Name}|{Location.Area}|{Location.X:R}|{Location.Y:R}");
    public string Label => $"{Name} · Lv. {MinimumLevel}–{MaximumLevel} · {Location.Area} (X:{Location.X:0.0}, Y:{Location.Y:0.0})";
}

internal sealed record FarmingSelection(FarmingArea Area, int Minimum, int Maximum, int DepartureLevel = 0)
{
    // Offsets always refer to the current BST level, not the level at departure.
    public FarmingSelection AtLevel(int playerLevel, FarmingOptions options) =>
        this with { Minimum = playerLevel + options.MinimumAbove, Maximum = playerLevel + options.MaximumAbove };
    public bool SupportsRange => (Area.MinimumLevel <= Maximum && Area.MaximumLevel >= Minimum) ||
        (DepartureLevel >= Minimum && DepartureLevel <= Maximum);
    public bool Eligible(int mobLevel, int playerLevel) => mobLevel > playerLevel && mobLevel >= Minimum && mobLevel <= Maximum;
    public FarmingSelection Observe(int mobLevel, int playerLevel) => Eligible(mobLevel, playerLevel) && mobLevel > (DepartureLevel > 0 ? DepartureLevel : Maximum)
        ? this with { DepartureLevel = mobLevel } : this;
}

internal static class FarmingPolicy
{
    public static bool InRange(FarmingArea area, int level, FarmingOptions options) =>
        level > 0 && area.MinimumLevel > 0 && area.MaximumLevel >= area.MinimumLevel && area.MaximumLevel <= 100 &&
        area.MinimumLevel <= level + options.MaximumAbove && area.MaximumLevel >= level + options.MinimumAbove;
    public static IEnumerable<FarmingArea> Choices(IEnumerable<FarmingArea> areas, int level, FarmingOptions options) =>
        areas.Where(a => InRange(a, level, options)).OrderBy(a => a.MinimumLevel).ThenBy(a => a.Location.Area).ThenBy(a => a.Name);
    public static bool MatchesGroup(FarmingArea area, FarmingOptions options) =>
        options.SelectedGroups.Count == 0 || options.SelectedGroups.Contains(area.Key);
    public static bool ReachedGoal(int level, FarmingOptions options) => options.TargetLevel > 0 && level >= options.TargetLevel;
    public static bool MayPull(bool notorious, uint fateId, uint selectedFate, FarmingOptions options) =>
        (!notorious || !options.IgnoreNotoriousMonsters) && (fateId == 0 || (options.ParticipateInFates && fateId == selectedFate));
    public static FarmingSelection? Select(IEnumerable<FarmingArea> areas, int level, FarmingOptions options,
        uint territory, Func<FarmingArea, bool> reachable) => areas
        .Where(a => InRange(a, level, options) && MatchesGroup(a, options) && reachable(a))
        // The catalog chooses a destination; it must not narrow which enemy
        // species or levels can be pulled once inside that area's patrol circle.
        .Select(a => new FarmingSelection(a, level + options.MinimumAbove, level + options.MaximumAbove, Math.Min(a.MaximumLevel, level + options.MaximumAbove)))
        .OrderBy(a => a.Area.Location.TerritoryTypeId == territory ? 0 : 1)
        .ThenByDescending(a => a.DepartureLevel).ThenBy(a => a.Area.MinimumLevel).FirstOrDefault();
}

// Empty sweeps may mean respawns or temporarily unavailable targets. Cool down
// this range briefly so another selected group can be tried, then patrol again.
internal sealed class FarmingEmptyAreas
{
    public const long RetryDelay = 60000;
    private readonly Dictionary<(FarmingArea Area, int Minimum, int Maximum), long> entries = [];
    public void Clear() => entries.Clear();
    public void Reject(FarmingSelection selection, long now) => entries[(selection.Area, selection.Minimum, selection.Maximum)] = now + RetryDelay;
    public long RetryAt(FarmingArea area, int level, FarmingOptions options) => entries.GetValueOrDefault((area, level + options.MinimumAbove, level + options.MaximumAbove));
    public bool Contains(FarmingArea area, int level, FarmingOptions options, long now) => RetryAt(area, level, options) > now;
}
