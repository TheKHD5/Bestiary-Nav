using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BestiaryNav;

internal sealed record TravelPlan(uint TerritoryId, Vector3 MapPoint, uint AetheryteId, byte SubIndex, string Name);
internal readonly record struct TravelPlayer(uint Territory, Vector3 Position, bool LoggedIn, bool Loading, bool Casting, string? BlockReason);
internal enum TravelPhase { Idle, Teleporting, WaitingForMesh, FindingPath, Moving }

internal interface ITravelBackend
{
    bool Available { get; }
    bool Busy { get; }
    bool MovementBusy { get; }
    bool MeshReady { get; }
    bool OwnPathRunning { get; }
    bool Teleport(uint aetheryteId, byte subIndex);
    Vector3? GroundPoint(Vector3 point);
    Task<List<Vector3>> FindPath(Vector3 start, Vector3 end, CancellationToken cancellation);
    void Move(List<Vector3> path);
    void StopOwnedMovement();
}

// No game pointers or blocking waits: the framework supplies fresh player state.
internal sealed class TravelController(ITravelBackend backend) : IDisposable
{
    public TravelPhase Phase { get; private set; }
    public bool Active => Phase != TravelPhase.Idle;
    public string Status { get; private set; } = "Select a beast to travel to its capture area.";
    private TravelPlan? plan;
    private CancellationTokenSource? cancellation;
    private Task<List<Vector3>>? pathTask;
    private Vector3 destination;
    private Vector3 lastPosition;
    private long deadline, readyAfter, lastProgress, teleportStarted, lastCast, nextPoll;
    private uint sourceTerritory;
    private bool sawCast;

    public void Start(TravelPlan next, TravelPlayer player, long now)
    {
        Stop("Previous travel stopped.");
        try
        {
            if (!backend.Available) { Status = "Auto travel requires enabled vnavmesh and Lifestream plugins with compatible IPC."; return; }
            if (!player.LoggedIn || player.Loading || player.Casting || player.BlockReason != null)
            { Status = player.BlockReason ?? "Cannot start travel while loading or casting."; return; }
            if (backend.Busy) { Status = "vnavmesh or Lifestream is busy. Finish that travel first."; return; }
            if (next.TerritoryId == 0 || !Finite(next.MapPoint)) { Status = "Invalid travel destination."; return; }
            if (next.TerritoryId != player.Territory && next.AetheryteId == 0)
            { Status = "No unlocked aetheryte in the target territory. Travel there manually, then select the beast again."; return; }
            plan = next;
            sourceTerritory = player.Territory;
            if (next.TerritoryId != player.Territory)
            {
                if (!backend.Teleport(next.AetheryteId, next.SubIndex)) { Stop("Lifestream could not start the teleport."); return; }
                Phase = TravelPhase.Teleporting;
                teleportStarted = lastCast = now;
                sawCast = false;
                deadline = now + 60000;
                Status = $"Teleporting to {next.Name}…";
            }
            else WaitForMesh(now);
        }
        catch (Exception ex) { Stop($"Could not start travel: {ex.Message}"); }
    }

    public void Update(TravelPlayer player, long now)
    {
        if (!Active || plan == null) return;
        try
        {
            if (!player.LoggedIn || player.BlockReason != null)
            { Stop(player.BlockReason ?? "Travel stopped on logout."); return; }
            if (!backend.Available) { Stop("Travel stopped: a dependency was disabled or reloaded."); return; }
            if (now >= deadline) { Stop($"Travel timed out during {Phase}."); return; }
            // Stop/combat checks stay immediate; avoid copying vnavmesh's waypoint
            // list and querying its state on every rendered frame.
            if (now < nextPoll) return;
            nextPoll = now + 100;
            if (Phase == TravelPhase.Teleporting)
            {
                if (player.Casting) { sawCast = true; lastCast = now; }
                if (!player.Loading && !player.Casting && player.Territory == plan.TerritoryId) WaitForMesh(now);
                else if (!player.Loading && player.Territory != sourceTerritory && player.Territory != plan.TerritoryId)
                    Stop("Travel stopped after an unexpected territory change.");
                else if (!player.Loading && !player.Casting &&
                    ((!sawCast && now - teleportStarted > 8000) || (sawCast && now - lastCast > 3000)))
                    Stop("Teleport did not complete. Select the beast again to retry.");
                return;
            }
            if (player.Loading || player.Territory != plan.TerritoryId)
            { Stop("Travel stopped after a territory change."); return; }
            if (player.Casting) { Stop("Travel stopped because you started casting."); return; }
            if (Phase == TravelPhase.WaitingForMesh)
            {
                if (now < readyAfter || !backend.MeshReady) return;
                if (backend.Busy) { Stop("Another navigation request started. Bestiary travel stopped."); return; }
                var ground = backend.GroundPoint(plan.MapPoint);
                if (ground == null || !Finite(ground.Value) || HorizontalDistance(ground.Value, plan.MapPoint) > 10)
                { Stop("No nearby walkable ground at the flag. Use the map to approach manually."); return; }
                destination = ground.Value;
                if (Vector3.Distance(player.Position, destination) <= 5) { Stop($"Arrived at {plan.Name}."); return; }
                cancellation = new();
                pathTask = backend.FindPath(player.Position, destination, cancellation.Token);
                Phase = TravelPhase.FindingPath;
                deadline = now + 45000;
                Status = $"Finding a walking route to {plan.Name}…";
            }
            else if (Phase == TravelPhase.FindingPath)
            {
                if (pathTask == null || !pathTask.IsCompleted) return;
                var path = pathTask.GetAwaiter().GetResult();
                pathTask = null;
                cancellation?.Dispose(); cancellation = null;
                if (!ValidPath(path, player.Position, destination))
                { Stop("No complete walking route to the capture area. The map flag remains available."); return; }
                if (backend.MovementBusy) { Stop("Another plugin started moving. Bestiary travel stopped."); return; }
                backend.Move(path);
                Phase = TravelPhase.Moving;
                deadline = now + 600000;
                lastProgress = now;
                lastPosition = player.Position;
                Status = $"Walking to {plan.Name}. Use /bnav stop to cancel.";
            }
            else if (Phase == TravelPhase.Moving)
            {
                if (Vector3.Distance(player.Position, destination) <= 5) { Stop($"Arrived at {plan.Name}."); return; }
                if (!backend.OwnPathRunning) { Stop("Movement stopped or was replaced. Select the beast again to retry."); return; }
                if (Vector3.DistanceSquared(player.Position, lastPosition) >= 1)
                { lastPosition = player.Position; lastProgress = now; }
                else if (now - lastProgress > 10000) Stop("Travel stopped: no movement progress for 10 seconds.");
            }
        }
        catch (Exception ex) { Stop($"Travel stopped: {ex.Message}"); }
    }

    private void WaitForMesh(long now)
    {
        Phase = TravelPhase.WaitingForMesh;
        readyAfter = now + 1000;
        deadline = now + 120000;
        Status = $"Waiting for vnavmesh in {plan!.Name}…";
    }

    public void Stop(string message = "Auto travel stopped.")
    {
        Phase = TravelPhase.Idle;
        nextPoll = 0;
        plan = null;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        // Observe a late fault, but never start movement from a late completion.
        if (pathTask != null)
            _ = pathTask.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        pathTask = null;
        try { backend.StopOwnedMovement(); }
        catch (Exception ex) { message += $" Could not send movement stop: {ex.Message}"; }
        Status = message;
    }

    internal static bool ValidPath(List<Vector3>? path, Vector3 start, Vector3 end) =>
        path is { Count: > 0 and <= 10000 } && path.TrueForAll(Finite) &&
        Vector3.Distance(path[0], start) <= 10 && Vector3.Distance(path[^1], end) <= 5;
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
    public void Dispose() => Stop();
}
