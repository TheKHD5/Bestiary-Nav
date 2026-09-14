using System;
using System.Collections.Generic;
using System.Numerics;
using BestiaryNav;

internal static class CaptureRunChecks
{
    public static void Run(Action<bool, string> check)
    {
        var gate = new CaptureRetryGate();
        gate.Defeated(10000);
        for (var elapsed = 0; elapsed < 3000; elapsed += 250)
            check(!gate.CanRetry(10000 + elapsed, 0, 3, false), $"no second pull {elapsed}ms after defeat");
        check(!gate.CanRetry(12999, 0, 3, false), "no second pull one millisecond before three seconds");
        check(gate.CanRetry(13000, 0, 3, false), "uncaptured target may retry at exactly three seconds");
        check(!gate.CanRetry(13000, 1UL << 2, 3, false), "delayed capture acknowledgement prevents second pull");
        check(!gate.CanRetry(20000, null, 3, false), "missing capture records never mean capture failed");
        check(!gate.CanRetry(20000, 0, 3, true), "remaining combat prevents a new pull after three seconds");
        check(gate.CanRetry(20000, 0, 3, false), "retry resumes when remaining combat ends");
        gate.Defeated(22000);
        check(!gate.CanRetry(24999, 0, 3, false) && gate.CanRetry(25000, 0, 3, false), "each defeat starts its own three-second delay");
        check(!gate.CanRetry(28000, 0, 0, false) && !gate.CanRetry(28000, 0, 51, false), "invalid entry cannot retry");

        var good = new CaptureCandidate(10, 3, new(8, 0, 0), 20, true, true, false);
        var invalid = new[] {
            good with { Id = 0 }, good with { Id = 0xE0000000 }, good with { Number = 4 },
            good with { Alive = false }, good with { Targetable = false }, good with { Engaged = true },
            good with { Level = 21 }, good with { Level = 0 }, good with { Position = new(61, 0, 0) },
            good with { Position = new(float.NaN, 0, 0) }, good with { Position = new(0, float.PositiveInfinity, 0) },
        };
        var used = new HashSet<ulong>();
        for (var i = 0; i < invalid.Length; i++)
            check(CaptureRunPolicy.Closest([invalid[i]], 3, 20, Vector3.Zero, Vector3.Zero, 60, null, used) == null,
                $"reject unsuitable capture candidate {i}");
        var farther = good with { Id = 11, Position = new(20, 0, 0) };
        check(CaptureRunPolicy.Closest([farther, good], 3, 20, Vector3.Zero, Vector3.Zero, 60, null, used)?.Id == 10,
            "closest matching eligible uncaptured beast is selected");
        used.Add(10);
        check(CaptureRunPolicy.Closest([good, farther], 3, 20, Vector3.Zero, Vector3.Zero, 60, null, used)?.Id == 11,
            "retry chooses another actor, not the defeated actor");
        used.Add(11);
        check(CaptureRunPolicy.Closest([good, farther], 3, 20, Vector3.Zero, Vector3.Zero, 60, null, used) == null,
            "no candidate waits without expanding to another spawn area");
        var floor = new TravelFloor { MinimumY = 25, MaximumY = 27 };
        check(!CaptureRunPolicy.InArea(new(0, 45, 0), Vector3.Zero, 60, floor), "underground run rejects same-name beast above the cave");
        check(CaptureRunPolicy.InArea(new(0, 26, 0), Vector3.Zero, 60, floor), "underground run accepts verified floor");
        check(CaptureRunPolicy.InArea(new(60, 0, 0), Vector3.Zero, 60, null), "spawn-area boundary inclusive");
        check(!CaptureRunPolicy.InArea(Vector3.Zero, Vector3.Zero, float.NaN, null), "invalid area radius rejected");

        // Repeated kills must not exhaust a small set of spawn IDs. Alternate
        // living respawns and corpses over more than five kills in the same area.
        used.Clear();
        for (var kill = 0; kill < 12; kill++)
        {
            var respawn = good with { Id = (ulong)(10 + kill % 5) };
            CaptureRunPolicy.RefreshDefeated([respawn], used);
            check(CaptureRunPolicy.Closest([respawn], 3, 20, Vector3.Zero, Vector3.Zero, 60, null, used)?.Id == respawn.Id,
                $"eligible respawn still selectable after {kill} kills");
            used.Add(respawn.Id);
            var corpse = respawn with { Alive = false };
            CaptureRunPolicy.RefreshDefeated([corpse], used);
            check(CaptureRunPolicy.Closest([corpse], 3, 20, Vector3.Zero, Vector3.Zero, 60, null, used) == null,
                "dead body is never selected as a respawn");
        }
        used.Add(good.Id);
        CaptureRunPolicy.RefreshDefeated([good with { Targetable = false }], used);
        check(used.Contains(good.Id), "non-targetable actor is not yet treated as a returned spawn");
    }
}
