using System;
using System.Numerics;

namespace BestiaryNav;

internal static class TravelGroundResolver
{
    public static Vector3? Resolve(Vector3 center, float radius, Func<Vector3, bool, float, Vector3?> floor, TravelFloor? targetFloor = null)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Z) || !float.IsFinite(radius) || radius is < 10 or > 200 ||
            targetFloor is { IsValid: false })
            return null;
        // Map coordinates have no altitude. Match vnavmesh's map-flag projection,
        // including polygons excluded by its optional reachability classification.
        // A complete path is still required before movement can start.
        // For stacked terrain, query below the verified ceiling and reject all
        // other floors. Never fall back to the surface above an underground beast.
        var origin = new Vector3(center.X, targetFloor?.MaximumY ?? 1024, center.Z);
        var limit = MathF.Min(radius, 20);
        foreach (var extent in new[] { 0.5f, 5, 10, limit })
        {
            var point = floor(origin, true, extent);
            if (point is { } p && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) &&
                (targetFloor == null || targetFloor.Contains(p.Y)) &&
                Vector2.Distance(new(p.X, p.Z), new(center.X, center.Z)) <= MathF.Min(extent, limit))
                return p;
        }
        return null;
    }
}
