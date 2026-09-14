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
        // Apkallu regression: no candidate is loaded at arrival, but moving
        // deeper into the 73-yalm circle brings it into the scan range.
        var patrol = new SpawnSearchRoute();
        var habitat = Vector3.Zero;
        var apkallu = new CaptureCandidate(24, 24, new(45, 0, 45), 30, true, true, false);
        var visitedCount = 0;
        for (var pass = 0; pass < 3; pass++)
        {
            patrol.Reset(habitat, 73);
            var cursor = habitat;
            var found = false;
            while (patrol.Next(cursor) is { } point)
            {
                cursor = point; visitedCount++;
                var loaded = Vector3.Distance(cursor, apkallu.Position) <= 25;
                var match = CaptureRunPolicy.Closest(loaded ? [apkallu] : [], 24, 40, cursor, habitat, 73, null, new HashSet<ulong>());
                if (match != null) found = true;
                patrol.Complete();
            }
            check(found, "patrol reaches deeper Apkallu habitat on each repeated sweep");
        }
        check(visitedCount > 3, "arrival point alone is not treated as a complete habitat search");
        var reject = new SpawnSearchRoute();
        foreach (var radius in new[] { float.NaN, 0f, 201f })
        {
            try { reject.Reset(Vector3.Zero, radius); check(false, "invalid survey radius rejected"); }
            catch (ArgumentOutOfRangeException) { check(true, "invalid survey radius rejected"); }
        }
    }
}
