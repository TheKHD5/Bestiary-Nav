using System;
using System.Collections.Generic;
using BestiaryNav;

internal static class CaptureAllChecks
{
    public static void Run(Action<bool, string> check)
    {
        MonsterEntry Beast(uint n, string kind = "map") => new()
        {
            BestiaryNumber = n, DisplayName = $"Beast {n}", NavigationKind = kind,
            Locations = kind == "map" ? [new() { TerritoryTypeId = 1, MapId = 1, X = 21, Y = 21 }] : [],
        };
        MonsterEntry[] beasts = [Beast(1), Beast(2), Beast(3), Beast(4, "duty"), Beast(5, "quest"), Beast(6)];
        Dictionary<uint, BeastAcquisition> metadata = [];
        foreach (var b in beasts) metadata[b.BestiaryNumber] = new() { BestiaryNumber = b.BestiaryNumber, MinimumLevel = b.BestiaryNumber == 6 ? 20 : 10 };
        var state = new CollectionSnapshot(true, 0, 10, 1, 1, default, "ready");
        var all = new CaptureAllController();
        uint? Tick(long now, bool busy = false, string? wait = null) => all.Update(now, state, beasts, metadata, busy, wait, "test attempt");
        check(Tick(0) == null && !all.Enabled, "batch starts disabled");
        all.Start();
        state = state with { Ready = false };
        check(Tick(0) == null && all.Enabled, "unloaded capture records do not complete batch");
        state = state with { Ready = true };
        check(Tick(0, wait: "equip BST") == null, "wrong job waits without travel");
        check(Tick(1) == 1, "first eligible outdoor entry selected");
        check(Tick(100000, true, "world loading") == null && all.Current == 1, "teleport loading keeps owned attempt");
        check(Tick(100001) == null, "finished attempt begins three-second settlement");
        check(Tick(103000) == null, "cannot engage one millisecond before settlement");
        state = state with { Captured = 1 };
        check(Tick(103001) == 2, "late capture record advances to another beast");
        check(all.Failures(1) == 0, "confirmed capture is not recorded as failure");
        check(Tick(103002) == null, "synchronous failed Start also settles");
        check(Tick(106002) == 3 && all.Failures(2) == 1, "failed entry deferred while other eligible entry runs");
        Tick(106003);
        state = state with { Captured = 5 };
        check(Tick(109003) == null && all.Enabled && all.Current == 0, "cooling down entries are not mistaken for completion");
        check(Tick(121001) == null, "failed entry waits full retry delay");
        check(Tick(121002) == 2, "failed entry retries after fifteen seconds");
        Tick(121003);
        state = state with { Captured = 7 };
        check(Tick(124003) == null && !all.Enabled, "stop when only duties quests or above-level beasts remain");
        state = state with { Level = 20 };
        check(Tick(200000) == null, "completed batch does not silently restart after leveling");
        all.Start();
        check(Tick(200001) == 6, "explicit restart includes newly eligible level");
        check(all.Update(200002, state, beasts, metadata, false, null,
            "Capture attempt stopped because the main target changed or cleared.") == null && all.Enabled,
            "lost target keeps batch toggled on while records settle");
        check(Tick(203002) == null && all.Enabled && all.Failures(6) == 1,
            "lost target schedules a retry rather than canceling the batch");
        check(Tick(218002) == 6 && all.Enabled, "lost target automatically retries when safe");
        all.Stop();
        check(Tick(9999999) == null && all.Current == 0, "explicit stop cancels retries");

        // One perpetually inaccessible beast must remain retryable without a busy loop
        // or an attempt limit, even through long dependency outages.
        beasts = [Beast(1)]; state = state with { Captured = 0, Level = 10 };
        all.Start(); long clock = 1;
        check(Tick(clock) == 1, "single target batch starts");
        for (var attempt = 1; attempt <= 40; attempt++)
        {
            Tick(++clock);
            clock += 3000;
            check(Tick(clock) == null && all.Enabled && all.Failures(1) == attempt, "failure keeps batch enabled and counts attempts");
            var delay = 15000L << Math.Min(attempt - 1, 3);
            check(Tick(clock + delay - 1) == null, "exponential cooldown enforced");
            clock += delay;
            check(Tick(clock, wait: "dependency missing") == null && all.Enabled, "dependency failure waits without stopping batch");
            check(Tick(clock) == 1, "dependency recovery resumes retry");
        }
        all.Stop(); all.Start();
        check(all.Failures(1) == 0, "new session clears failure history");
        check(Tick(++clock) == 1, "new session starts clean");
        Tick(++clock);
        state = state with { Ready = false };
        check(Tick(clock + 10000) == null && all.Current == 1, "lost capture records do not schedule speculative retry");
        state = state with { Ready = true, Level = 5 };
        check(Tick(clock + 10001) == null && !all.Enabled, "current level rechecked before retry");
        all.Start(); state = state with { Level = 0 };
        check(Tick(++clock) == null && all.Enabled, "temporary level zero does not mark collection complete");
    }
}
