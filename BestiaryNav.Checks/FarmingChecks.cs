using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BestiaryNav;

internal static class FarmingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var options = new FarmingOptions { MinimumAbove = -3, MaximumAbove = 80, TargetLevel = -1 };
        options.Normalize();
        check(options.MinimumAbove == 1 && options.MaximumAbove == 10 && options.TargetLevel == 0, "normalize farming settings to the expanded range");
        options.MaximumAbove = 5;
        var low = new FarmingArea { MinimumLevel = 31, MaximumLevel = 35, Location = new() { TerritoryTypeId = 1 } };
        var high = new FarmingArea { MinimumLevel = 36, MaximumLevel = 40, Location = new() { TerritoryTypeId = 2 } };
        var chosen = FarmingPolicy.Select([low, high], 30, options, 1, _ => true)!;
        check(chosen.Area == low && chosen.Minimum == 31 && chosen.Maximum == 35, "choose area within requested levels");
        check(chosen.AtLevel(34, options).SupportsRange && !chosen.AtLevel(35, options).SupportsRange && !chosen.AtLevel(36, options).SupportsRange, "leave an area once the current offset band no longer overlaps its levels");
        check(chosen.AtLevel(34, options).Eligible(35, 34) && !chosen.AtLevel(34, options).Eligible(34, 34), "apply offsets after level-up and exclude equal-level targets");
        check(!chosen.Eligible(36, 30) && !chosen.Eligible(30, 30), "exclude enemies outside the current target band");
        check(FarmingPolicy.Select([low, high], 35, options, 1, _ => true)?.Area == high, "move to stronger zone after outleveling");
        check(FarmingPolicy.Select([low, high], 35, options, 1, _ => false) == null, "locked destinations are excluded");
        check(FarmingPolicy.Select([low, high], 40, options, 1, _ => true) == null, "no eligible documented zone does not invent a destination");
        options.MinimumAbove = options.MaximumAbove = 3;
        chosen = FarmingPolicy.Select([low], 30, options, 1, _ => true)!;
        check(chosen.Minimum == 33 && chosen.Maximum == 33 && !chosen.AtLevel(33, options).SupportsRange, "exact offset clamps target levels and updates area suitability");
        for (var min = 1; min <= 10; min++)
        for (var max = min; max <= 10; max++)
        {
            options.MinimumAbove = min; options.MaximumAbove = max;
            chosen = FarmingPolicy.Select([low, high], 30, options, 1, _ => true)!;
            check(chosen.Minimum == 30 + min && chosen.Maximum == 30 + max, "every configured offset pair stays within its range");
        }
        options.TargetLevel = 40;
        check(!FarmingPolicy.ReachedGoal(39, options) && FarmingPolicy.ReachedGoal(40, options) && FarmingPolicy.ReachedGoal(42, options), "target level is a hard stop threshold");
        options.TargetLevel = 0;
        check(!FarmingPolicy.ReachedGoal(100, options), "zero target level disables goal stop");
        check(FarmingPolicy.MayPull(false, 0, 0, options), "ordinary mobs remain eligible");
        check(!FarmingPolicy.MayPull(true, 0, 0, options), "notorious marks excluded by default");
        check(!FarmingPolicy.MayPull(false, 7, 7, options), "FATE enemies excluded while toggle off");
        options.ParticipateInFates = true;
        check(FarmingPolicy.MayPull(false, 7, 7, options) && !FarmingPolicy.MayPull(false, 8, 7, options), "participation restricted to selected FATE");
        check(!FarmingPolicy.MayPull(true, 7, 7, options), "NM exclusion also applies inside FATE");
        options.IgnoreNotoriousMonsters = false;
        check(FarmingPolicy.MayPull(true, 7, 7, options), "NM option can be disabled explicitly");
        options = new FarmingOptions();
        var narrow = new FarmingArea { Name = "Catalog species", MinimumLevel = 33, MaximumLevel = 33, Location = new() { TerritoryTypeId = 1 } };
        chosen = FarmingPolicy.Select([narrow], 30, options, 1, _ => true)!;
        check(chosen.Minimum == 31 && chosen.Maximum == 35 && chosen.DepartureLevel == 33,
            "single-species level data chooses a destination without narrowing the requested band");
        var mixed = new[] { (Name: "Catalog species", Level: 33), (Name: "Different lower species", Level: 31),
            (Name: "Different higher species", Level: 35), (Name: "Too strong", Level: 36), (Name: "Too weak", Level: 30) };
        var eligible = mixed.Where(m => chosen.Eligible(m.Level, 30)).ToArray();
        check(eligible.Length == 3 && eligible.Any(m => m.Name == "Different lower species") && eligible.Any(m => m.Name == "Different higher species"),
            "mixed species across the full range are eligible, while outside levels are rejected");
        check(!chosen.AtLevel(33, options).SupportsRange, "without other observations an out-of-band catalog is unsuitable");
        chosen = chosen.Observe(35, 30);
        check(chosen.AtLevel(33, options).SupportsRange && chosen.AtLevel(34, options).SupportsRange && !chosen.AtLevel(35, options).SupportsRange,
            "discovered higher eligible species postpones departure until its level is reached");
        check(chosen.Observe(40, 30) == chosen && chosen.Observe(30, 30) == chosen,
            "above-band and equal-level enemies cannot extend the stay");
        var fresh = FarmingPolicy.Select([narrow], 30, options, 1, _ => true)!;
        check(fresh.AtLevel(33, options).Observe(34, 33).SupportsRange, "observe additional species before departure on a level-up frame");
        check(chosen.AtLevel(34, options).Eligible(35, 34) && !chosen.AtLevel(34, options).Eligible(34, 34), "mixed-species levelling still excludes targets already outleveled");
        var exactFive = new FarmingOptions { MinimumAbove = 5, MaximumAbove = 5 };
        var beforeLevelUp = FarmingPolicy.Select([low, high], 30, exactFive, 1, _ => true)!;
        var afterLevelUp = beforeLevelUp.AtLevel(31, exactFive);
        check(afterLevelUp.Minimum == 36 && afterLevelUp.Maximum == 36 && !afterLevelUp.Eligible(35, 31), "+5/+5 at BST 31 never retains level 35 from departure");
        check(!afterLevelUp.SupportsRange, "immediately reject former level-35 zone for exact level-36 target");
        check(FarmingPolicy.Select([low, high], 31, exactFive, 1, _ => true)?.Area == high, "current territory preference cannot retain an incompatible area");
        check(!beforeLevelUp.AtLevel(36, exactFive).SupportsRange, "multi-level gain invalidates old destination too");
        var stillSuitable = FarmingPolicy.Select([high], 31, exactFive, 2, _ => true)!;
        check(stillSuitable.AtLevel(32, exactFive).SupportsRange && stillSuitable.AtLevel(32, exactFive).Eligible(37, 32), "keep same area only when it supports the newly requested level");
        check(FarmingPolicy.Select([low], 31, exactFive, 1, _ => true) == null, "no compatible destination does not fall back to lower-level mobs");
        var emptyAreas = new FarmingEmptyAreas();
        emptyAreas.Reject(beforeLevelUp, 1000);
        check(emptyAreas.Contains(low, 30, exactFive, 1001), "an empty completed patrol briefly cools down that range");
        check(!emptyAreas.Contains(high, 30, exactFive, 1001), "another area is still available");
        check(!emptyAreas.Contains(low, 31, exactFive, 1001), "new level range has independent search evidence");
        check(FarmingPolicy.Select([low], 30, exactFive, 1, a => !emptyAreas.Contains(a, 30, exactFive, 1001)) == null, "avoid an immediate repeated empty patrol");
        check(emptyAreas.RetryAt(low, 30, exactFive) == 61000 && emptyAreas.Contains(low, 30, exactFive, 60999), "empty-area wait has a finite 60-second deadline");
        check(FarmingPolicy.Select([low], 30, exactFive, 1, a => !emptyAreas.Contains(a, 30, exactFive, 61000))?.Area == low, "patrol resumes at the retry deadline without a manual restart");
        emptyAreas.Clear();
        check(!emptyAreas.Contains(low, 30, exactFive, 1001), "manual restart clears patrol cooldowns");
        var db = JsonSerializer.Deserialize<FarmingDatabase>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "farming-areas.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var specific = new FarmingOptions { MinimumAbove = 1, MaximumAbove = 10 };
        var groupA = new FarmingArea { Name = "Mob A", MinimumLevel = 34, MaximumLevel = 38, Location = new() { Area = "Zone A", TerritoryTypeId = 1, X = 10, Y = 20 } };
        var groupB = new FarmingArea { Name = "Mob A", MinimumLevel = 39, MaximumLevel = 40, Location = new() { Area = "Zone B", TerritoryTypeId = 2, X = 10, Y = 20 } };
        check(groupA.Key != groupB.Key, "same monster name in different zones has a distinct saved choice");
        check(FarmingPolicy.Choices([groupA, groupB], 30, specific).Count() == 2, "dropdown includes every matching documented group");
        specific.SelectedGroups = [groupB.Key];
        check(FarmingPolicy.Select([groupA, groupB], 30, specific, 1, _ => true)?.Area == groupB, "manual group overrides current-zone preference");
        check(FarmingPolicy.Select([groupA, groupB], 30, specific, 1, a => a != groupB) == null, "unreachable manual choice never silently falls back to another group");
        check(FarmingPolicy.MatchesEnemy(groupB, specific, "mob a", [groupA, groupB]) && !FarmingPolicy.MatchesEnemy(groupB, specific, "Mob B", [groupA, groupB]) &&
            !FarmingPolicy.MatchesEnemy(groupB, specific, null, [groupA, groupB]), "manual choice filters other species and unverified name IDs");
        check(FarmingPolicy.Choices([groupA, groupB], 30, specific).Count() == 2, "selected group does not hide alternatives from dropdown");
        specific.MinimumAbove = specific.MaximumAbove = 10;
        check(FarmingPolicy.Select([groupA, groupB], 30, specific, 1, _ => true)?.Area == groupB, "exact +10 chooses level-40 group at BST 30");
        check(FarmingPolicy.Select([groupA, groupB], 31, specific, 1, _ => true) == null, "outgrown manual group stops instead of changing the user's selection");
        specific.SelectedGroups.Clear();
        check(FarmingPolicy.MatchesEnemy(groupB, specific, "Different species", [groupA, groupB]), "Automatic preserves all-species combat");
        check(!FarmingPolicy.Choices(db.Areas, 0, specific).Any(), "no BST level does not produce misleading dropdown results");
        check(db.Areas.Select(a => a.Key).Distinct().Count() == db.Areas.Count, "all shipped group IDs are distinct");
        var saved = JsonSerializer.Deserialize<FarmingOptions>(JsonSerializer.Serialize(new FarmingOptions { MinimumAbove = 10, MaximumAbove = 10, SelectedGroup = groupB.Key }))!;
        saved.Normalize();
        check(saved.MinimumAbove == 10 && saved.MaximumAbove == 10 && saved.SelectedGroups.SequenceEqual([groupB.Key]) && saved.SelectedGroup == "", "legacy single group migrates without losing settings");
        saved.Normalize();
        check(saved.SelectedGroups.SequenceEqual([groupB.Key]), "migration is idempotent");
        saved.SelectedGroups.Clear(); saved.Normalize();
        check(saved.SelectedGroups.Count == 0, "clearing the last migrated choice stays Automatic");
        var multiple = new FarmingOptions { MinimumAbove = 3, MaximumAbove = 10, SelectedGroups = [groupA.Key, groupB.Key, groupA.Key, "", " "] };
        multiple.Normalize();
        check(multiple.SelectedGroups.SequenceEqual([groupA.Key, groupB.Key]), "duplicate and empty saved group keys are normalized");
        var roundTrip = JsonSerializer.Deserialize<FarmingOptions>(JsonSerializer.Serialize(multiple))!;
        roundTrip.Normalize();
        check(roundTrip.SelectedGroups.SequenceEqual(multiple.SelectedGroups), "multiple choices survive save and reload");
        var legacyAndNew = new FarmingOptions { SelectedGroup = groupA.Key, SelectedGroups = [groupB.Key] };
        legacyAndNew.Normalize();
        check(legacyAndNew.SelectedGroups.SequenceEqual([groupB.Key]), "new selections take precedence over a stale legacy key");
        var nullGroups = JsonSerializer.Deserialize<FarmingOptions>("{\"SelectedGroups\":null,\"SelectedGroup\":null}")!;
        nullGroups.Normalize();
        check(nullGroups.SelectedGroups.Count == 0, "null saved selections recover to Automatic");
        check(FarmingPolicy.Select([groupA, groupB], 30, multiple, 1, _ => true)?.Area == groupA, "multiple selection allows a qualifying current-zone group");
        check(FarmingPolicy.Select([groupA, groupB], 30, multiple, 1, a => a != groupA)?.Area == groupB, "another selected zone is chosen when the first is unavailable");
        var unselected = new FarmingArea { Name = "Unselected", MinimumLevel = 35, MaximumLevel = 40, Location = new() { Area = "Zone C", TerritoryTypeId = 3 } };
        check(FarmingPolicy.Select([groupA, groupB, unselected], 30, multiple, 3, a => a == unselected) == null, "never substitute an unselected group during retries");
        check(FarmingPolicy.Select([groupA, groupB], 36, multiple, 1, _ => true)?.Area == groupB, "level-up leaves an outgrown group for another selected eligible group");
        check(FarmingPolicy.Select([groupA, groupB], 38, multiple, 1, _ => true) == null, "all selected groups outgrown still ends the eligible destination set");
        var sameZone = new FarmingArea { Name = "Mob C", MinimumLevel = 35, MaximumLevel = 40, Location = new() { Area = "Zone A", TerritoryTypeId = 1, X = 11, Y = 20 } };
        multiple.SelectedGroups.Add(sameZone.Key);
        check(FarmingPolicy.MatchesEnemy(groupA, multiple, "Mob C", [groupA, groupB, sameZone]), "any selected species in the current zone can qualify inside the patrol circle");
        check(!FarmingPolicy.MatchesEnemy(groupB, multiple, "Mob C", [groupA, groupB, sameZone]), "selection of a species in another zone does not enable pulls here");
        check(!FarmingPolicy.MatchesEnemy(groupA, multiple, "Unselected", [groupA, groupB, sameZone, unselected]), "multiple groups do not enable unrelated species");
        var fleece = new FarmingArea { Name = "Golden Fleece", MinimumLevel = 40, MaximumLevel = 42, Location = new() { Area = "Eastern Thanalan", TerritoryTypeId = 145, X = 30, Y = 24 } };
        var fleeceOptions = new FarmingOptions { MinimumAbove = 3, MaximumAbove = 10, SelectedGroups = [fleece.Key] };
        var fleeceSelection = FarmingPolicy.Select([fleece], 38, fleeceOptions, 145, _ => true)!;
        check(fleeceSelection.SupportsRange && fleeceSelection.Eligible(41, 38) && fleeceSelection.Eligible(42, 38) && !fleeceSelection.Eligible(40, 38), "reported BST-38 Golden Fleece case retains levels 41-42");
        emptyAreas.Reject(fleeceSelection, 1000);
        check(emptyAreas.RetryAt(fleece, 38, fleeceOptions) > 1000, "only selected group being empty produces a scheduled retry");
        check(FarmingPolicy.Select([fleece], 38, fleeceOptions, 145, a => !emptyAreas.Contains(a, 38, fleeceOptions, 61000))?.Area == fleece, "reported Golden Fleece case resumes its same eligible patrol");
        emptyAreas.Reject(fleeceSelection, 62000);
        check(emptyAreas.Contains(fleece, 38, fleeceOptions, 121999) && !emptyAreas.Contains(fleece, 38, fleeceOptions, 122000), "successive empty sweeps remain retryable");
        check(db.Areas.Count >= 40, "farming catalog includes original and additional overworld areas");
        foreach (var area in db.Areas)
            check(area.MinimumLevel > 0 && area.MaximumLevel >= area.MinimumLevel && area.MaximumLevel <= 100 &&
                !string.IsNullOrWhiteSpace(area.Name) && area.Source.StartsWith("https://") && !string.IsNullOrWhiteSpace(area.Location.Area) &&
                float.IsFinite(area.Location.X) && float.IsFinite(area.Location.Y) && area.Location.X > 0 && area.Location.Y > 0,
                "farming location has a sourced level range and finite coordinates");
        check(db.Areas.Any(a => a.MinimumLevel == 35 && a.MaximumLevel == 39) && db.Areas.Any(a => a.MaximumLevel == 49), "catalog covers higher leveling bands");
    }
}
