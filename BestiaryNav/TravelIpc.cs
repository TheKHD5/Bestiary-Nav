using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;

namespace BestiaryNav;

internal sealed class TravelIpc : ITravelBackend
{
    private readonly Func<bool> available, busy, movementBusy, ready, running, simplePending;
    private readonly Func<uint, byte, bool> teleport;
    private readonly Func<Vector3, bool, float, Vector3?> floor;
    private readonly Func<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> find;
    private readonly Action<List<Vector3>, bool> move;
    private readonly Action stop, cancelRetries;
    private readonly Func<List<Vector3>> waypoints;
    private Vector3? ownedDestination;

    public TravelIpc(IDalamudPluginInterface pi)
    {
        var navReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        var navFinding = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        var navSimple = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        var navRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        var navPoints = pi.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
        var navFloor = pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        var navFind = pi.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable");
        var navMove = pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        var navStop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        var navCancel = pi.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll");
        var lifeBusy = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        var lifeTeleport = pi.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        available = () => navReady.HasFunction && navFinding.HasFunction && navSimple.HasFunction &&
            navRunning.HasFunction && navPoints.HasFunction && navFloor.HasFunction && navFind.HasFunction &&
            navMove.HasAction && navStop.HasAction && navCancel.HasAction && lifeBusy.HasFunction && lifeTeleport.HasFunction;
        ready = navReady.InvokeFunc;
        running = navRunning.InvokeFunc;
        simplePending = navSimple.InvokeFunc;
        movementBusy = () => navRunning.InvokeFunc() || navSimple.InvokeFunc() || lifeBusy.InvokeFunc();
        busy = () => movementBusy() || navFinding.InvokeFunc();
        teleport = lifeTeleport.InvokeFunc;
        floor = navFloor.InvokeFunc;
        find = navFind.InvokeFunc;
        move = navMove.InvokeAction;
        stop = navStop.InvokeAction;
        cancelRetries = navCancel.InvokeAction;
        waypoints = navPoints.InvokeFunc;
    }

    public bool Available => available();
    public bool Busy => busy();
    public bool MovementBusy => movementBusy();
    public bool MeshReady => ready();
    public bool OwnPathRunning => ownedDestination is { } end && running() &&
        waypoints() is { Count: > 0 } points && Vector3.Distance(points[^1], end) < 1;
    public bool Teleport(uint aetheryteId, byte subIndex) => teleport(aetheryteId, subIndex);
    // The catalog is 2D. Resolve altitude on the destination mesh, not from the
    // source zone's player Y or a made-up ground height.
    public Vector3? GroundPoint(Vector3 point) => floor(new(point.X, 1024, point.Z), false, 5);
    public Task<List<Vector3>> FindPath(Vector3 start, Vector3 end, CancellationToken cancellation) => find(start, end, false, cancellation);
    public void Move(List<Vector3> path)
    {
        ownedDestination = path[^1];
        move(path, false);
    }

    public void StopOwnedMovement()
    {
        if (ownedDestination == null) return;
        try
        {
            if (OwnPathRunning) stop();
            // vnavmesh may queue its own retry after a stuck event clears the path.
            // Cancel that queued retry only while this controller owns the route.
            if (!running() && simplePending()) cancelRetries();
        }
        finally { ownedDestination = null; }
    }
}
