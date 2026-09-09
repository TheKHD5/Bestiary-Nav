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

        Reset(); controller.Start(target with { TerritoryId = 148 }, player, 0);
        check(ipc.Teleports == 1 && controller.Phase == TravelPhase.Teleporting, "cross-zone route teleports first");
        controller.Update(player with { Casting = true }, 500);
        controller.Update(player with { Loading = true }, 6000);
        check(ipc.Moves == 0 && controller.Phase == TravelPhase.Teleporting, "no movement during teleport loading");
        controller.Update(player with { Territory = 148 }, 7000);
        check(controller.Phase == TravelPhase.WaitingForMesh, "destination arrival waits for mesh");

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
        public Vector3? GroundPoint(Vector3 point) => Ground;
        public Task<List<Vector3>> FindPath(Vector3 start, Vector3 end, CancellationToken token) { Token = token; return Completion.Task; }
        public void Move(List<Vector3> path) { Moves++; OwnPathRunning = true; }
        public void StopOwnedMovement() { if (OwnPathRunning) { Stops++; OwnPathRunning = false; } }
    }
}
