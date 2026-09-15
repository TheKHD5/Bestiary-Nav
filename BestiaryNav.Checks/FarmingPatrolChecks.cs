using System;
using System.Linq;
using System.Text.Json;
using BestiaryNav;

internal static class FarmingPatrolChecks
{
    public static void Run(Action<bool, string> check)
    {
        FarmingCheckpoint Point(float x = 10, float y = -20) => new() { MapId = 10, X = x, Y = y, Z = 40, MapX = 20, MapY = 21 };
        var routine = new FarmingPatrol { Name = "Underground loop" };
        check(routine.Add(Point(), 145) && routine.TerritoryId == 145, "first registered checkpoint assigns the routine zone");
        check(!routine.Add(Point(30), 146) && routine.Checkpoints.Count == 1, "cross-zone recording cannot corrupt an existing route");
        check(!routine.Add(Point(11), 145) && routine.Checkpoints.Count == 1, "repeated click near the previous checkpoint does not add duplicates");
        check(routine.Add(Point(30), 145) && routine.Add(Point(60, -22), 145), "record walking positions in their original order");
        check(!routine.Add(Point(float.NaN), 145) && !routine.Add(Point(100, float.PositiveInfinity), 145) &&
            !routine.Add(new() { X = 100, MapX = 20, MapY = 21 }, 145), "invalid coordinates and missing maps cannot become checkpoints");
        var options = new FarmingOptions { SelectedPatrol = routine.Id, Patrols = [routine] };
        var serialized = JsonSerializer.Serialize(options);
        var saved = JsonSerializer.Deserialize<FarmingOptions>(serialized)!;
        saved.Normalize();
        check(saved.SelectedPatrol == routine.Id && saved.Patrols.Single().Name == routine.Name, "save and reload retain the selected named routine");
        check(saved.Patrols[0].Checkpoints.Select(p => p.X).SequenceEqual([10f, 30f, 60f]) && saved.Patrols[0].Checkpoints[2].Y == -22,
            "checkpoint ordering and underground height survive serialization");
        check(!serialized.Contains("Position") && !serialized.Contains("IsValid"), "saved routes contain explicit scalar coordinates without computed fields");
        routine.Name = "Renamed";
        check(routine.Id == options.SelectedPatrol, "renaming preserves selection and identity");
        var malformed = JsonSerializer.Deserialize<FarmingOptions>("{\"Patrols\":[null,{\"Name\":null,\"SearchRadius\":999,\"Checkpoints\":[null,{}]}],\"SelectedPatrol\":null}")!;
        malformed.Normalize();
        check(malformed.Patrols.Count == 1 && malformed.Patrols[0].Checkpoints.Count == 0 && malformed.Patrols[0].SearchRadius == 100 && malformed.SelectedPatrol == "",
            "malformed saved routes normalize safely without retaining broken checkpoints");
        var legacy = JsonSerializer.Deserialize<FarmingOptions>("{\"Patrols\":null}")!;
        legacy.Normalize();
        check(legacy.Patrols.Count == 0 && legacy.SelectedPatrol == "", "existing configurations keep catalog patrols");

        var cursor = new FarmingPatrolCursor();
        check(cursor.Index == 0 && !cursor.Arrived, "a new run travels to its first checkpoint before scanning");
        cursor.Reach();
        check(cursor.Arrived && cursor.Index == 0, "arrival enables the current checkpoint scan without advancing");
        cursor.Advance(3, 1000, false);
        check(cursor.Index == 1 && !cursor.Arrived, "exhausted scan advances exactly one checkpoint");
        cursor.ResumeTravel();
        check(cursor.Index == 1 && !cursor.Arrived, "revival or interrupted travel retains the pending checkpoint");
        cursor.Reach();
        check(cursor.Index == 1 && cursor.Arrived, "resuming after interruption does not restart from checkpoint one");
        cursor.Advance(3, 2000, false); cursor.Reach(); cursor.Advance(3, 3000, false);
        check(cursor.Index == 0, "last checkpoint loops back to the start");
        cursor.Reset();
        check(cursor.Advance(3, 1000, true) == 1500 && cursor.Advance(3, 2000, true) == 2500,
            "unreachable checkpoints skip to the next saved point");
        check(cursor.Advance(3, 3000, true) == 63000 && cursor.Index == 0, "an entirely unreachable circuit waits sixty seconds before retrying");
        cursor.Advance(3, 64000, true); cursor.Reach();
        check(cursor.ConsecutiveFailures == 0, "a reachable checkpoint clears consecutive route failures");
        cursor.Reset();
        check(cursor.Advance(1, 1000, false) == 4000 && cursor.Index == 0, "single-checkpoint patrol waits before scanning again");
        check(cursor.Advance(1, 5000, true) == 65000, "single unreachable checkpoint also gets a bounded retry delay");
        routine.SearchRadius = float.NaN; routine.Normalize();
        check(routine.SearchRadius == 30, "invalid radius resets to the usable default");
    }
}
