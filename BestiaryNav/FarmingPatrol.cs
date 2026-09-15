using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BestiaryNav;

public sealed class FarmingPatrol
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New patrol";
    public uint TerritoryId { get; set; }
    public float SearchRadius { get; set; } = 30;
    public List<FarmingCheckpoint> Checkpoints { get; set; } = [];
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        Name = string.IsNullOrWhiteSpace(Name) ? "New patrol" : Name.Trim();
        SearchRadius = float.IsFinite(SearchRadius) ? Math.Clamp(SearchRadius, 10, 100) : 30;
        Checkpoints ??= [];
        Checkpoints.RemoveAll(p => p == null || !p.IsValid);
    }
    public bool Add(FarmingCheckpoint point, uint territory)
    {
        if (!point.IsValid || territory == 0 || (TerritoryId != 0 && TerritoryId != territory)) return false;
        if (Checkpoints.Count > 0 && Vector3.Distance(Checkpoints[^1].Position, point.Position) < 2) return false;
        TerritoryId = territory;
        Checkpoints.Add(point);
        return true;
    }
}

public sealed class FarmingCheckpoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public uint MapId { get; set; }
    public float MapX { get; set; }
    public float MapY { get; set; }
    internal Vector3 Position => new(X, Y, Z);
    internal bool IsValid => MapId != 0 && float.IsFinite(X) && float.IsFinite(Y) && Y is >= -1019 and <= 1019 && float.IsFinite(Z) &&
        float.IsFinite(MapX) && float.IsFinite(MapY) && MapX > 0 && MapY > 0;
}

// The cursor advances only after a checkpoint scan is exhausted, never just
// because a fight, healing pause or revival interrupted movement.
internal sealed class FarmingPatrolCursor
{
    public int Index { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public bool Arrived { get; private set; }
    public void Reset() { Index = ConsecutiveFailures = 0; Arrived = false; }
    public void Reach() { Arrived = true; ConsecutiveFailures = 0; }
    public void ResumeTravel() => Arrived = false;
    public long Advance(int count, long now, bool failed)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        Index = (Index + 1) % count;
        Arrived = false;
        ConsecutiveFailures = failed ? ConsecutiveFailures + 1 : 0;
        if (ConsecutiveFailures >= count) { ConsecutiveFailures = 0; return now + 60000; }
        return now + (count == 1 ? 3000 : 500);
    }
}
