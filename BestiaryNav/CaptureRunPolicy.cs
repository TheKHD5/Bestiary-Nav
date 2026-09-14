using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BestiaryNav;

internal readonly record struct CaptureCandidate(ulong Id, uint Number, Vector3 Position, byte Level,
    bool Alive, bool Targetable, bool Engaged);

// Monotonic, nonblocking post-defeat gate. Missing/refreshing capture records
// can only delay another pull; they never mean "uncaptured".
internal sealed class CaptureRetryGate
{
    public const long MinimumDelay = 3000;
    public long ReadyAt { get; private set; }
    public void Defeated(long now) => ReadyAt = now + MinimumDelay;
    public bool CanRetry(long now, ulong? records, uint number, bool inCombat) =>
        now >= ReadyAt && records.HasValue && CaptureRules.IsUncaptured(records.Value, number) && !inCombat;
    public void Reset() => ReadyAt = 0;
}

internal static class CaptureRunPolicy
{
    // A defeated object's ID is not a permanent exclusion: an outdoor spawn
    // can return alive with the same ID. Corpse and eligibility checks still apply.
    public static void RefreshDefeated(IEnumerable<CaptureCandidate> candidates, ISet<ulong> defeated)
    {
        foreach (var candidate in candidates)
            if (candidate.Alive && candidate.Targetable) defeated.Remove(candidate.Id);
    }

    public static bool InArea(Vector3 position, Vector3 center, float radius, TravelFloor? floor) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
        float.IsFinite(center.X) && float.IsFinite(center.Z) && float.IsFinite(radius) && radius is >= 10 and <= 200 &&
        Vector2.DistanceSquared(new(position.X, position.Z), new(center.X, center.Z)) <= radius * radius &&
        (floor == null || floor.Contains(position.Y));

    public static CaptureCandidate? Closest(IEnumerable<CaptureCandidate> candidates, uint number, byte level,
        Vector3 player, Vector3 center, float radius, TravelFloor? floor, ISet<ulong> defeated) =>
        candidates.Where(c => c.Id is not (0 or 0xE0000000) && c.Number == number && c.Alive && c.Targetable &&
                !c.Engaged && c.Level > 0 && c.Level <= level && !defeated.Contains(c.Id) && InArea(c.Position, center, radius, floor))
            .OrderBy(c => Vector3.DistanceSquared(player, c.Position)).Select(c => (CaptureCandidate?)c).FirstOrDefault();
}
