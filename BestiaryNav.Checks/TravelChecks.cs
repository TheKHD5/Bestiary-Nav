using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BestiaryNav;

internal static class TravelChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckAreaFallback(check);
        var player = new TravelPlayer(134, Vector3.Zero, true, false, false, null);
        var target = new TravelPlan(134, new(100, 0, 100), 8, 0, "Lamb habitat");
        FakeTravel ipc = new();
        using var controller = new TravelController(ipc);
        void Reset()
        {
            controller.Stop();
            ipc.Available = true; ipc.Busy = false; ipc.MovementBusy = false; ipc.MeshReady = true;
            ipc.TeleportAccepted = true; ipc.Ground = target.MapPoint; ipc.Completion = new();
            ipc.Moves = ipc.Teleports = ipc.Stops = 0;
            ipc.Mounts = 0; ipc.MountAccepted = ipc.FindFlying = ipc.MoveFlying = false;
        }
        void BeginPath()
        {
            controller.Start(target, player, 0);
            controller.Update(player, 1000);
        }
        void BeginMove()
        {
            BeginPath();
            ipc.Completion.SetResult([player.Position, target.MapPoint]);
            controller.Update(player, 1100);
        }
        Reset(); BeginMove();
        check(ipc.Teleports == 0 && ipc.Moves == 1 && controller.Phase == TravelPhase.Moving, "same-zone route walks without teleport");
        controller.Update(player with { Position = target.MapPoint }, 2000);
        check(!controller.Active && ipc.Stops == 1 && controller.Status.StartsWith("Arrived"), "arrival stops owned movement");
        check(controller.Arrived, "arrival has an explicit completion result for capture runs");
        controller.Stop();
        check(!controller.Arrived, "manual cancellation never appears to be successful arrival");

        Reset(); ipc.Ground = null; ipc.MountAccepted = true;
        var approach = target with { ExactDestination = true, AllowMount = false, AllowFlight = false, ArrivalDistance = 1.5f };
        controller.Start(approach, player, 0); controller.Update(player, 1000);
        check(controller.Phase == TravelPhase.FindingPath && ipc.Mounts == 0 && !ipc.FindFlying,
            "capture approach uses live actor altitude and stays on foot");
        ipc.Completion.SetResult([player.Position, approach.MapPoint]); controller.Update(player, 1100);
        controller.Update(player with { Position = approach.MapPoint + new Vector3(4, 0, 0) }, 2000);
        check(controller.Active && !controller.Arrived, "capture approach continues inside normal five-yalm arrival radius");
        controller.Update(player with { Position = approach.MapPoint + new Vector3(1, 0, 0) }, 3000);
        check(controller.Arrived, "capture approach finishes within melee distance");
        foreach (var radius in new[] { float.NaN, 0.1f, 6f })
        {
            Reset(); controller.Start(approach with { ArrivalDistance = radius }, player, 0);
            check(!controller.Active && !controller.Arrived, "invalid capture arrival tolerance cannot start movement");
        }

        Reset(); controller.Start(target with { TerritoryId = 148 }, player, 0);
        check(ipc.Teleports == 1 && controller.Phase == TravelPhase.Teleporting, "cross-zone route teleports first");
        controller.Update(player with { Casting = true }, 500);
        controller.Update(player with { Loading = true }, 6000);
        check(ipc.Moves == 0 && controller.Phase == TravelPhase.Teleporting, "no movement during teleport loading");
        controller.Update(player with { Territory = 148 }, 7000);
        check(controller.Phase == TravelPhase.WaitingForMesh, "destination arrival waits for mesh");

        // Coeurl #33: Upper La Noscea. The player object and UI can return later
        // than the territory/BetweenAreas updates. Navigation must retain its plan.
        Reset(); var coeurl = target with { TerritoryId = 139, Name = "Coeurl #33" };
        controller.Start(coeurl, player, 0);
        controller.Update(player with { Casting = true }, 1000);
        controller.Update(player with { Territory = 0, Loading = true, WorldReady = false }, 5000);
        controller.Update(player with { Territory = 139, Loading = false, WorldReady = false }, 11000);
        check(controller.Active && controller.Phase == TravelPhase.Teleporting && ipc.Moves == 0,
            "Coeurl teleport survives player/UI absence after loading flags clear");
        controller.Update(player with { Territory = 139 }, 12000);
        check(controller.Phase == TravelPhase.WaitingForMesh, "Coeurl arrival resumes into mesh wait");
        controller.Update(player with { Territory = 139, WorldReady = false }, 12500);
        ipc.Busy = true;
        controller.Update(player with { Territory = 139 }, 14000);
        check(controller.Active && controller.Phase == TravelPhase.WaitingForMesh,
            "Lifestream finishing after teleport waits instead of canceling navigation");
        ipc.Busy = false;
        controller.Update(player with { Territory = 139 }, 15000);
        ipc.Completion.SetResult([player.Position, coeurl.MapPoint]);
        controller.Update(player with { Territory = 139 }, 16000);
        check(controller.Phase == TravelPhase.Moving && ipc.Moves == 1, "Coeurl navigation continues toward destination after teleport");
        controller.Update(player with { Territory = 139, Position = coeurl.MapPoint }, 17000);
        check(controller.Arrived, "Coeurl cross-zone trip completes at its designated location");

        Reset(); controller.Start(coeurl, player, 0);
        controller.Update(player with { Loading = true, WorldReady = false }, 61000);
        check(!controller.Active, "failed world transition remains bounded by teleport timeout");
        Reset(); controller.Start(coeurl, player, 0);
        controller.Update(player with { LoggedIn = false }, 500);
        check(!controller.Active, "actual logout still cancels a teleport");

        Reset(); var survey = target with { AllowMount = false, AllowFlight = false,
            SearchBoundary = new(Vector3.Zero, 200, null) };
        ipc.Ground = null; controller.Start(survey, player, 0); controller.Update(player, 1000);
        check(controller.NoRoute && !controller.Active, "unmapped survey point is skippable");
        Reset(); controller.Start(survey, player, 0); controller.Update(player, 1000);
        ipc.Completion.SetResult([player.Position, new(300, 0, 0), survey.MapPoint]); controller.Update(player, 1100);
        check(controller.NoRoute && ipc.Moves == 0, "survey route cannot leave selected spawn circle");
        Reset(); controller.Start(survey, player, 0); controller.Update(player, 1000); controller.Update(player, 16000);
        check(controller.NoRoute && ipc.Token.IsCancellationRequested, "unreachable survey route times out and cancels its path task");
        Reset(); controller.Start(survey, player, 0); controller.Update(player with { ManualMovement = true }, 500, true);
        check(!controller.Active && !controller.NoRoute, "manual cancellation stops search instead of skipping to another point");

        Reset(); ipc.MeshReady = false; controller.Start(target, player, 0); controller.Update(player, 1000);
        check(controller.Phase == TravelPhase.WaitingForMesh && ipc.Moves == 0, "mesh not ready waits");
        controller.Update(player, 120001);
        check(!controller.Active && ipc.Moves == 0, "mesh timeout stops");

        Reset(); BeginPath(); controller.Stop();
        check(ipc.Token.IsCancellationRequested, "stop cancels owned pathfinding");
        ipc.Completion.SetResult([Vector3.Zero, target.MapPoint]); controller.Update(player, 2000);
        check(ipc.Moves == 0 && !controller.Active, "late result after cancellation never moves");

        Reset(); BeginMove(); controller.Update(player with { BlockReason = "Combat" }, 1200);
        check(!controller.Active && ipc.Stops == 1, "combat cancels movement");

        Reset(); BeginMove();
        controller.Update(player with { ManualMovement = true }, 1101, true);
        check(!controller.Active && ipc.Stops == 1 && controller.Status.Contains("manually"),
            "manual input stops walking immediately, before the next IPC poll");
        Reset(); BeginMove();
        controller.Update(player with { ManualMovement = true }, 1200, false);
        check(controller.Active && ipc.Stops == 0, "manual movement option off preserves travel");
        controller.Update(player with { ManualMovement = true }, 1201, true);
        check(!controller.Active && ipc.Stops == 1, "enabling cancellation during a trip takes effect immediately");
        Reset(); BeginMove();
        controller.Update(player with { Position = new(10, 0, 10) }, 1200, true);
        check(controller.Active && ipc.Stops == 0, "automated position changes do not cancel travel");
        Reset(); BeginPath();
        controller.Update(player with { ManualMovement = true }, 1001, true);
        check(!controller.Active && ipc.Token.IsCancellationRequested, "manual input cancels pending pathfinding");
        ipc.Completion.SetResult([player.Position, target.MapPoint]);
        controller.Update(player, 2000, true);
        check(!controller.Active && ipc.Moves == 0, "late result after manual cancellation never restarts walking");
        Reset(); BeginPath(); ipc.Completion.SetResult([player.Position, target.MapPoint]);
        controller.Update(player with { ManualMovement = true }, 1100, true);
        check(!controller.Active && ipc.Moves == 0, "manual input wins over a completed path in the same update");
        Reset(); controller.Start(target, player, 0);
        controller.Update(player with { ManualMovement = true }, 1, true);
        check(!controller.Active && ipc.Moves == 0, "manual input cancels while waiting for the mesh");
        Reset(); controller.Start(target with { TerritoryId = 148 }, player, 0);
        controller.Update(player with { ManualMovement = true, Casting = true }, 1, true);
        controller.Update(player with { Territory = 148 }, 7000, true);
        check(!controller.Active && ipc.Moves == 0, "manual cancellation during teleport prevents follow-up walking");
        Reset(); controller.Start(target with { TerritoryId = 148 }, player, 0);
        controller.Update(player with { ManualMovement = true, Loading = true }, 500, true);
        check(controller.Phase == TravelPhase.Teleporting, "stale input during loading does not cancel the expected teleport");

        Reset(); BeginMove(); controller.Update(player with { LoggedIn = false }, 1200);
        check(!controller.Active && ipc.Stops == 1, "logout cancels movement");
        Reset(); BeginMove(); controller.Update(player with { Territory = 999, Loading = true }, 1200);
        check(!controller.Active && ipc.Stops == 1, "unexpected zoning cancels movement");
        Reset(); BeginMove(); controller.Update(player, 12000);
        check(!controller.Active && ipc.Stops == 1, "stalled movement cancels");

        Reset(); ipc.Available = false; controller.Start(target, player, 0);
        check(!controller.Active && ipc.Teleports == 0, "missing dependencies cannot start");
        Reset(); ipc.Busy = true; controller.Start(target, player, 0);
        check(!controller.Active && ipc.Stops == 0 && ipc.Moves == 0, "foreign travel is not interrupted");
        Reset(); controller.Start(target with { TerritoryId = 148, AetheryteId = 0 }, player, 0);
        check(!controller.Active && ipc.Teleports == 0, "locked teleport rejected");
        Reset(); ipc.TeleportAccepted = false; controller.Start(target with { TerritoryId = 148 }, player, 0);
        check(!controller.Active && ipc.Moves == 0, "declined teleport stops");
        Reset(); controller.Start(target with { TerritoryId = 148 }, player, 0); controller.Update(player, 9000);
        check(!controller.Active && ipc.Moves == 0, "teleport that never starts is not retried");

        Reset(); ipc.Ground = null; BeginPath();
        check(!controller.Active && ipc.Moves == 0, "missing floor stops");
        Reset(); ipc.Ground = new(500, 0, 500); BeginPath();
        check(!controller.Active, "ground too far from flag rejected");
        Reset(); BeginPath(); ipc.Completion.SetResult([Vector3.Zero, new(10, 0, 10)]); controller.Update(player, 1100);
        check(!controller.Active && ipc.Moves == 0, "partial route rejected");
        Reset(); BeginPath(); ipc.Available = false; controller.Update(player, 1100);
        check(!controller.Active && ipc.Token.IsCancellationRequested, "dependency unload cancels pending path");
        Reset(); BeginPath(); ipc.Completion.SetException(new InvalidOperationException("No path")); controller.Update(player, 1100);
        check(!controller.Active && ipc.Moves == 0, "pathfinding exception contained");
        Reset(); BeginPath(); ipc.Completion.SetResult([Vector3.Zero, target.MapPoint]); ipc.MovementBusy = true; controller.Update(player, 1100);
        check(!controller.Active && ipc.Moves == 0, "foreign movement starting during path search respected");
        check(!TravelController.ValidPath([new(float.NaN, 0, 0)], Vector3.Zero, target.MapPoint), "nonfinite path rejected");
        check(!TravelController.ValidPath([], Vector3.Zero, target.MapPoint), "empty path rejected");

        Reset(); ipc.MountAccepted = true; BeginPath();
        check(controller.Phase == TravelPhase.Mounting && ipc.Mounts == 1 && ipc.Moves == 0,
            "mount roulette requested once before pathfinding");
        controller.Update(player with { Casting = true, MountTransition = true }, 2000);
        check(controller.Phase == TravelPhase.Mounting, "owned mount cast does not cancel travel");
        var mounted = player with { Mounted = true, CanFly = true };
        controller.Update(mounted, 4000);
        check(controller.Phase == TravelPhase.FindingPath && ipc.FindFlying, "flight checked after mounting in destination territory");
        ipc.Completion.SetResult([player.Position, target.MapPoint]); controller.Update(mounted, 4100);
        check(ipc.Moves == 1 && ipc.MoveFlying && controller.CompactStatus == "Flying…", "flight mode passed to movement as well as pathfinding");
        controller.Update(mounted with { InFlight = true, CanFly = false, Position = new(50, 20, 50) }, 5000);
        check(controller.Active, "already airborne does not require a fresh takeoff permission");
        controller.Update(player, 5100);
        check(!controller.Active && ipc.Stops == 1, "dismount stops the owned flying route");

        Reset(); controller.Start(target, mounted, 0); controller.Update(mounted, 1000);
        check(ipc.Mounts == 0 && ipc.FindFlying, "already mounted player is never dismounted by roulette");
        ipc.Completion.SetException(new InvalidOperationException("No flying volume"));
        var groundCompletion = new TaskCompletionSource<List<Vector3>>();
        ipc.Completion = groundCompletion; controller.Update(mounted, 1100);
        check(controller.Active && !ipc.FindFlying, "unavailable flying route falls back to ground while still on land");
        groundCompletion.SetResult([player.Position, target.MapPoint]); controller.Update(mounted, 1200);
        check(ipc.Moves == 1 && !ipc.MoveFlying, "fallback uses ground movement");

        Reset(); var airborne = mounted with { InFlight = true };
        controller.Start(target, airborne, 0); controller.Update(airborne, 1000);
        ipc.Completion.SetResult([]); controller.Update(airborne, 1100);
        check(!controller.Active && ipc.Moves == 0, "failed airborne route never starts ground movement in midair");

        Reset(); var noFlight = mounted with { CanFly = false };
        controller.Start(target, noFlight, 0); controller.Update(noFlight, 1000);
        ipc.Completion.SetResult([player.Position, target.MapPoint]); controller.Update(noFlight, 1100);
        check(!ipc.FindFlying && !ipc.MoveFlying && ipc.Moves == 1, "locked flight uses a mounted ground route");

        Reset(); ipc.MountAccepted = true; BeginPath(); controller.Update(player, 6000);
        check(controller.Phase == TravelPhase.FindingPath && !ipc.FindFlying && ipc.Mounts == 1,
            "failed mount falls back without repeatedly summoning mounts");
        Reset(); ipc.MountAccepted = true; BeginPath();
        controller.Update(player with { Casting = true }, 16001);
        check(!controller.Active && ipc.Moves == 0, "mount timeout cannot start movement during casting");
        Reset(); ipc.MountAccepted = true; BeginPath();
        controller.Update(player with { ManualMovement = true, Casting = true }, 1001, true);
        controller.Update(mounted, 5000, true);
        check(!controller.Active && ipc.Moves == 0, "manual cancellation during mounting prevents later movement");
        Reset(); ipc.MountAccepted = true; BeginPath();
        controller.Update(player with { BlockReason = "Combat" }, 1001);
        check(!controller.Active && ipc.Moves == 0, "combat cancels the mount wait");
        Reset(); controller.Start(target, mounted, 0); controller.Update(mounted, 1000);
        ipc.Completion.SetResult([player.Position, target.MapPoint]); controller.Update(noFlight, 1100);
        check(!controller.Active && ipc.Moves == 0, "lost flight availability before path completion prevents takeoff");

        var center = SpawnAreaCoordinates.Convert(new MapLocation { X = 24, Y = 12 }, 100, 0, 0, 60);
        check(center.WorldPoint == new Vector3(center.X, 0, center.Y), "travel X/Z equals native spawn-circle center exactly");
        Reset(); controller.Start(target with { MapPoint = center.WorldPoint }, player, 0); controller.Update(player, 1000);
        check(ipc.RequestedCenter == center.WorldPoint, "floor query receives selected circle center without a map flag");
        var queries = new List<(Vector3 Origin, bool Include, float Extent)>();
        Vector3? Floor(Vector3 origin, bool include, float extent)
        {
            queries.Add((origin, include, extent));
            return extent < 5 ? null : new Vector3(origin.X, 72, origin.Z);
        }
        var resolved = TravelGroundResolver.Resolve(center.WorldPoint, 60, Floor);
        check(resolved == new Vector3(center.X, 72, center.Y) && queries.Count == 2,
            "center altitude resolution expands a narrow lookup only when needed");
        check(queries.TrueForAll(q => q.Include && q.Origin == new Vector3(center.X, 1024, center.Y)),
            "floor lookup includes mesh polygons filtered by optional reachability classification");
        check(TravelGroundResolver.Resolve(Vector3.Zero, 10, (_, _, _) => new Vector3(11, 0, 0)) == null,
            "ground snapping never leaves a small spawn circle");
        check(TravelGroundResolver.Resolve(Vector3.Zero, 60, (_, _, _) => new Vector3(21, 0, 0)) == null,
            "ground snapping stays within twenty yalms of the center");
        check(TravelGroundResolver.Resolve(Vector3.Zero, 60, (_, _, _) => new Vector3(0, float.NaN, 0)) == null,
            "invalid floor altitude is rejected");

        var caveFloor = new TravelFloor { MinimumY = 25, MaximumY = 27 };
        var caveCenter = new Vector3(-75, 0, -125);
        var cavePoint = new Vector3(-71, 26.75f, -129);
        queries.Clear();
        resolved = TravelGroundResolver.Resolve(caveCenter, 60, (origin, include, extent) =>
        {
            queries.Add((origin, include, extent));
            return extent < 5 ? null : cavePoint;
        }, caveFloor);
        check(resolved == cavePoint && queries.Count == 3 && queries.TrueForAll(q => q.Origin.Y == 27),
            "Ghost resolves verified underground floor, expanding to contain the diagonal snap");
        check(TravelGroundResolver.Resolve(caveCenter, 60, (_, _, _) => new Vector3(-75, 45.51f, -125), caveFloor) == null,
            "stacked floor projection never falls back to surface above Ghost");
        check(TravelGroundResolver.Resolve(caveCenter, 60, (_, _, _) => new Vector3(-75, 20, -125), caveFloor) == null,
            "stacked floor projection rejects unrelated deeper terrain");
        check(TravelGroundResolver.Resolve(caveCenter, 60, (_, _, _) => null, caveFloor) == null,
            "missing underground mesh does not invent a destination");
        check(TravelGroundResolver.Resolve(caveCenter, 60, (_, _, _) => cavePoint,
            new TravelFloor { MinimumY = 30, MaximumY = 20 }) == null, "reversed floor bounds rejected");
        check(!new TravelFloor { MinimumY = float.NaN, MaximumY = 27 }.IsValid &&
            !new TravelFloor { MinimumY = 25, MaximumY = float.PositiveInfinity }.IsValid,
            "nonfinite floor data rejected");
        var underground = target with { MapPoint = caveCenter, TargetFloor = caveFloor };
        var onRoof = player with { Position = new(-75, 45.51f, -125), Mounted = true, CanFly = true };
        Reset(); ipc.Ground = cavePoint;
        controller.Start(underground, onRoof, 0); controller.Update(onRoof, 1000);
        check(controller.Active && ipc.RequestedFloor == caveFloor && !ipc.FindFlying,
            "Ghost's floor metadata reaches backend and forces ground path despite flight unlock");
        ipc.Completion.SetResult([onRoof.Position, new(-60.25f, 39, -60.5f), cavePoint]);
        controller.Update(onRoof, 1100);
        check(ipc.Moves == 1 && !ipc.MoveFlying, "underground movement uses ground route through entrance");
        controller.Update(onRoof, 1200);
        check(controller.Active, "same map X/Z on the roof does not count as underground arrival");
        controller.Update(onRoof with { Position = cavePoint }, 1300);
        check(!controller.Active && controller.Status.StartsWith("Arrived"), "arrival requires underground altitude");
        Reset(); ipc.Ground = onRoof.Position;
        controller.Start(underground, onRoof, 0); controller.Update(onRoof, 1000);
        check(!controller.Active && ipc.Moves == 0, "controller rejects backend returning roof for underground plan");
        Reset(); controller.Start(underground, onRoof with { InFlight = true }, 0);
        check(!controller.Active && ipc.Mounts == 0 && ipc.Moves == 0 && controller.Status.StartsWith("Land"),
            "airborne start cannot initiate a ground path through the cave roof");
        Reset(); ipc.Ground = cavePoint; ipc.MountAccepted = true;
        controller.Start(underground, player, 0); controller.Update(player, 1000);
        controller.Update(onRoof, 1100);
        check(ipc.Mounts == 1 && !ipc.FindFlying && controller.Phase == TravelPhase.FindingPath,
            "mounting preserves underground ground-only route");
        controller.Update(onRoof with { InFlight = true }, 1200);
        check(!controller.Active && ipc.Moves == 0, "taking flight during underground route cancels it");
    }

    private static void CheckAreaFallback(Action<bool, string> check)
    {
        var player = new TravelPlayer(137, Vector3.Zero, true, false, false, null);
        var plan = new TravelPlan(137, new(1000, 0, 1000), 0, 0, "Apkallu circle", 123,
            AllowMount: false, AllowFlight: false, AllowAreaFallback: true);
        var ipc = new FakeTravel { Available = true, MeshReady = true };
        ipc.FloorProjection = p => p == plan.MapPoint ? null : p with { Y = 20 };
        using var controller = new TravelController(ipc);
        controller.Start(plan, player, 0); controller.Update(player, 1000);
        check(controller.Active && ipc.Moves == 0, "unmapped circle center tries another point instead of ending travel");
        controller.Update(player, 1100);
        check(controller.Phase == TravelPhase.FindingPath && ipc.RequestedCenter != plan.MapPoint,
            "fallback projects a different point inside the same circle");
        var endpoint = ipc.RequestedCenter with { Y = 20 };
        check(CaptureRunPolicy.InArea(endpoint, plan.MapPoint, plan.SearchRadius, null), "alternate arrival remains inside spawn circle");
        ipc.Completion.SetResult([player.Position, endpoint]); controller.Update(player, 1200);
        check(controller.Phase == TravelPhase.Moving && ipc.Moves == 1, "validated alternate route continues after teleport arrival");
        controller.Update(player with { Position = endpoint }, 1300);
        check(controller.Arrived, "alternate ground point completes arrival");

        ipc.FloorProjection = p => null;
        controller.Start(plan, player, 0);
        for (var now = 1000; now <= 30000 && controller.Active; now += 100) controller.Update(player, now);
        check(!controller.Active && controller.NoRoute, "wholly unmapped area has a bounded sweep then reports failure");

        ipc.FloorProjection = p => p;
        ipc.Completion = new();
        controller.Start(plan, player, 0); controller.Update(player, 1000);
        ipc.Completion.SetResult([]); controller.Update(player, 1100);
        check(controller.Active && controller.Phase == TravelPhase.WaitingForMesh,
            "disconnected center path also falls back to another candidate");
        ipc.Completion = new(); controller.Update(player, 1200);
        endpoint = ipc.RequestedCenter;
        ipc.Completion.SetResult([player.Position, endpoint]); controller.Update(player, 1300);
        check(controller.Phase == TravelPhase.Moving, "alternate point needs its own complete path before moving");
        controller.Stop();
        ipc.FloorProjection = p => p with { Y = 100 };
        controller.Start(plan with { TargetFloor = new() { MinimumY = 25, MaximumY = 27 } }, player, 0);
        for (var now = 1000; now <= 30000 && controller.Active; now += 100) controller.Update(player, now);
        check(controller.NoRoute, "fallback never substitutes surface terrain for a specified underground floor");
    }

    private sealed class FakeTravel : ITravelBackend
    {
        public bool Available { get; set; }
        public bool Busy { get; set; }
        public bool MovementBusy { get; set; }
        public bool MeshReady { get; set; }
        public bool OwnPathRunning { get; private set; }
        public bool TeleportAccepted;
        public int Moves, Teleports, Stops;
        public Vector3? Ground;
        public CancellationToken Token;
        public TaskCompletionSource<List<Vector3>> Completion = new();
        public bool Teleport(uint id, byte sub) { Teleports++; return TeleportAccepted; }
        public bool MountAccepted;
        public int Mounts;
        public bool FindFlying, MoveFlying;
        public Vector3 RequestedCenter;
        public bool Mount() { Mounts++; return MountAccepted; }
        public TravelFloor? RequestedFloor;
        public Func<Vector3, Vector3?>? FloorProjection;
        public Vector3? GroundPoint(Vector3 point, float radius, TravelFloor? targetFloor) { RequestedCenter = point; RequestedFloor = targetFloor; return FloorProjection != null ? FloorProjection(point) : Ground; }
        public Task<List<Vector3>> FindPath(Vector3 start, Vector3 end, bool fly, CancellationToken token) { FindFlying = fly; Token = token; return Completion.Task; }
        public void Move(List<Vector3> path, bool fly) { Moves++; MoveFlying = fly; OwnPathRunning = true; }
        public void StopOwnedMovement() { if (OwnPathRunning) { Stops++; OwnPathRunning = false; } }
    }
}
