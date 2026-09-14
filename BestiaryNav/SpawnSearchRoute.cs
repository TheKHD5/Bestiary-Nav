using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BestiaryNav;

internal sealed record SpawnSearchBoundary(Vector3 Center, float Radius, TravelFloor? Floor)
{
    public bool Contains(Vector3 point) => CaptureRunPolicy.InArea(point, Center, Radius, Floor);
}

// These are survey points, not invented monster spawn coordinates. Every point
// must resolve to a valid mesh path inside the configured spawn circle first.
internal sealed class SpawnSearchRoute
{
    private readonly List<Vector3> remaining = [];
    public Vector3? Current { get; private set; }
    public int Visited { get; private set; }
    public int Total { get; private set; }

    public void Reset(Vector3 center, float radius)
    {
        remaining.Clear(); Current = null; Visited = 0;
        if (!float.IsFinite(radius) || radius is < 10 or > 200 || !float.IsFinite(center.X) ||
            !float.IsFinite(center.Y) || !float.IsFinite(center.Z)) throw new ArgumentOutOfRangeException(nameof(radius));
        var spacing = MathF.Min(40, radius * 0.6f);
        var extent = (int)MathF.Floor((radius - 2) / spacing);
        for (var row = -extent; row <= extent; row++)
        for (var column = -extent; column <= extent; column++)
        {
            var offset = new Vector3(column * spacing, 0, row * spacing);
            if (offset.LengthSquared() <= (radius - 2) * (radius - 2)) remaining.Add(center + offset);
        }
        Total = remaining.Count;
    }

    public Vector3? Next(Vector3 player)
    {
        if (Current.HasValue || remaining.Count == 0) return Current;
        var next = remaining.MinBy(p => Vector3.DistanceSquared(p, player));
        remaining.Remove(next);
        return Current = next;
    }

    public void Complete()
    {
        if (Current.HasValue) Visited++;
        Current = null;
    }
}
