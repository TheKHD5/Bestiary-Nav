using System;
using System.Collections.Generic;
using System.Linq;

namespace BestiaryNav;

public sealed class FarmingOptions
{
    public int MinimumAbove { get; set; } = 1;
    public int MaximumAbove { get; set; } = 5;
    public bool SummonChocobo { get; set; }
    public bool UseFood { get; set; }
    public uint FoodId { get; set; } // Includes HQ offset, so the selected quality is preserved.
    public bool ParticipateInFates { get; set; }
    public bool IgnoreNotoriousMonsters { get; set; } = true;
    public bool AutoRespawn { get; set; } = true;
    public int TargetLevel { get; set; } // 0 = no requested stop level.
    public void Normalize()
    {
        MinimumAbove = Math.Clamp(MinimumAbove, 1, 5);
        MaximumAbove = Math.Clamp(MaximumAbove, MinimumAbove, 5);
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
}

internal sealed record FarmingSelection(FarmingArea Area, int Minimum, int Maximum, int DepartureLevel = 0)
{
    // Lock the chosen band until it is outleveled. Reapplying the minimum offset
    // at each level-up would leave an area before its highest target is reached.
    public bool Complete(int playerLevel) => playerLevel >= (DepartureLevel > 0 ? DepartureLevel : Maximum);
    public bool Eligible(int mobLevel, int playerLevel) => mobLevel > playerLevel && mobLevel >= Minimum && mobLevel <= Maximum;
    public FarmingSelection Observe(int mobLevel, int playerLevel) => Eligible(mobLevel, playerLevel) && mobLevel > (DepartureLevel > 0 ? DepartureLevel : Maximum)
        ? this with { DepartureLevel = mobLevel } : this;
}

internal static class FarmingPolicy
{
    public static bool ReachedGoal(int level, FarmingOptions options) => options.TargetLevel > 0 && level >= options.TargetLevel;
    public static bool MayPull(bool notorious, uint fateId, uint selectedFate, FarmingOptions options) =>
        (!notorious || !options.IgnoreNotoriousMonsters) && (fateId == 0 || (options.ParticipateInFates && fateId == selectedFate));
    public static FarmingSelection? Select(IEnumerable<FarmingArea> areas, int level, FarmingOptions options,
        uint territory, Func<FarmingArea, bool> reachable) => areas
        .Where(a => a.MinimumLevel > 0 && a.MaximumLevel >= a.MinimumLevel && a.MaximumLevel <= 100 &&
            a.MinimumLevel <= level + options.MaximumAbove && a.MaximumLevel >= level + options.MinimumAbove && reachable(a))
        // The catalog chooses a destination; it must not narrow which enemy
        // species or levels can be pulled once inside that area's patrol circle.
        .Select(a => new FarmingSelection(a, level + options.MinimumAbove, level + options.MaximumAbove, Math.Min(a.MaximumLevel, level + options.MaximumAbove)))
        .OrderBy(a => a.Area.Location.TerritoryTypeId == territory ? 0 : 1)
        .ThenByDescending(a => a.DepartureLevel).ThenBy(a => a.Area.MinimumLevel).FirstOrDefault();
}
