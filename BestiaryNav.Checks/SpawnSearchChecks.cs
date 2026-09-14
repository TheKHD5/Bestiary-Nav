using System;
using System.Collections.Generic;
using System.Numerics;
using BestiaryNav;

internal static class SpawnSearchChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var radius in new[] { 10f, 60f, 100f, 200f })
        {
            var center = new Vector3(250, 26, -100);
            var route = new SpawnSearchRoute(); route.Reset(center, radius);
            var bounds = new SpawnSearchBoundary(center, radius, new TravelFloor { MinimumY = 25, MaximumY = 27 });
            var points = new HashSet<Vector3>();
            var cursor = center;
            while (route.Next(cursor) is { } point)
            {
                check(points.Add(point) && bounds.Contains(point), "survey point is unique, finite, and inside selected spawn area/floor");
                check(route.Next(cursor + new Vector3(1)) == point, "paused search retains current waypoint until it completes");
                cursor = point; route.Complete();
                if (points.Count > 150) throw new InvalidOperationException("Search route did not terminate.");
            }
            check(points.Count == route.Total && route.Visited == route.Total && route.Total is > 1 and <= 150,
                $"{radius}-yalm survey exhausts a bounded set of waypoints");
            check(route.Next(center) == null, "completed sweep does not restart indefinitely");
            route.Reset(center, radius);
            check(route.Visited == 0 && route.Next(center) == center, "retry after defeat can start a fresh sweep");
        }
        var reject = new SpawnSearchRoute();
        foreach (var radius in new[] { float.NaN, 0f, 201f })
        {
            try { reject.Reset(Vector3.Zero, radius); check(false, "invalid survey radius rejected"); }
            catch (ArgumentOutOfRangeException) { check(true, "invalid survey radius rejected"); }
        }
    }
}
