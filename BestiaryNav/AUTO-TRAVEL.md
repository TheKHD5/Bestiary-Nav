# Automatic travel

Bestiary Nav connects to **vnavmesh** and **Lifestream** through Dalamud IPC. Both must be installed, enabled, and expose the required IPC endpoints. They are runtime dependencies of automatic travel; map flags, Duty Finder selection and capture markers still work without them. The plugin does not download or enable other plugins.

Enable **Automatically travel to selected beasts** in `/bnav` settings. Left-clicking an outdoor Bestiary entry then opens its flag and starts travel. `/bnav beast <number/name>` follows the same setting. `/bnav go <number/name>` explicitly requests travel for that selection, without changing the saved setting. **Stop travel** or `/bnav stop` cancels the active route and pending Bestiary request. Turning the travel setting off also stops it.

From 0.5.3, a single **Auto Navigate** checkbox is anchored above the top-left corner of Master's Bestiary. It follows window movement and scaling, stays within the viewport, and hides when the Bestiary is closed or the game UI is unavailable. It uses the same saved `AutoTravel` setting as the settings window. Turning it on also enables entry click handling; turning it off stops current automatic travel while leaving ordinary entry map/Duty Finder navigation enabled. The control is a small ImGui overlay, with no native UI node writes or extra click hooks. The user confirmed the checkbox appeared and followed window movement in 0.5.2; 0.5.3 moves the horizontal anchor from the center to the left as requested.

## Route behavior

1. If already in the destination territory, start from the current position without teleporting.
2. Otherwise, select the nearest normal aetheryte in that territory from the player's unlocked teleport list. Distance uses the aetheryte's MapMarker entry, with map scale and offsets applied. Housing destinations are excluded. Lifestream performs the teleport; normal game costs and restrictions apply.
3. Wait for the correct territory, the end of loading, and the destination navigation mesh. Resolve the flag's X/Z position to walkable ground with vnavmesh; the catalog contains map coordinates, not terrain altitude.
4. Request a cancellable ground path. Reject empty, nonfinite and partial routes, or routes whose start no longer matches the player. Start movement only after the task completes and the dependencies are still available and idle.
5. Stop within five yalms of the resolved capture area. Bestiary Nav does not target, attack or capture a beast.

Travel is on foot; there is no automatic mounting, flight or swimming mode. The destination is the published capture area, not a tracked moving enemy. Floor projection is intended for the catalog's outdoor locations, not ambiguous stacked interiors. Nearest-aetheryte selection uses straight-line distance, not a cross-zone route graph. A missing unlock, missing mesh, or incomplete walking route leaves the map flag available for manual travel. Duty entries open Duty Finder; they do not queue, enter or navigate a dungeon. Quest-only entries show their existing guidance.

## Lifecycle and cancellation

From 0.5.4, **Cancel auto travel when manually moving** is a saved setting in `/bnav`, off by default. It cancels the active trip during teleport, mesh waiting, pathfinding or walking when movement input is detected. Loading screens ignore input. The Auto Navigate setting stays enabled for the next selection. A canceled pathfinding result cannot start walking later.

The input reader uses the game's configured movement bindings through `UIInputData.IsInputIdDown`, the UI-filtered left controller stick (20/99 dead zone), and both UI-filtered mouse buttons. It ignores background keyboard input and text entry; camera-only mouse movement does not trigger cancellation. It reads input rather than character displacement, so ordinary vnavmesh walking is not mistaken for manual movement. Input reading is restricted to the verified game/Dalamud binding. No movement hooks or dependency configuration changes are added. Contracts: [FFXIVClientStructs InputData](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/System/Input/InputData.cs) and [UIInputData](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/UIInputData.cs). Manual-input cancellation still needs an in-game check with the user's controls.

Travel refuses to start while another vnavmesh/Lifestream operation is active. Combat, death, events/cutscenes, logout, dependency loss, unexpected zone changes and manual casting stop it. Loading is allowed during an expected teleport. Teleports, mesh loading, pathfinding and movement each have a deadline; lack of position progress for ten seconds stops walking.

The controller polls task completion on framework updates rather than blocking the game thread. Cancellation invalidates the pending task and its token: a late pathfinding completion cannot restart movement. The controller tracks the endpoint of its own path before stopping it, so it does not deliberately stop a replacement route. If vnavmesh has cleared that owned route and queued a stuck retry, Bestiary Nav cancels pending vnavmesh pathfinding to prevent the retry from restarting it. IPC has no universal ownership lock; avoid running competing movement automation simultaneously.

`Lifestream.Teleport` requests an immediate teleport rather than creating a Lifestream task queue. Stopping Bestiary travel prevents follow-up walking but does not undo a teleport already cast. It never calls Lifestream's global Abort for unrelated work.

## Verified contracts

Source contracts were checked against [vnavmesh IPCProvider](https://github.com/awgil/ffxiv_navmesh/blob/master/vnavmesh/IPCProvider.cs), [vnavmesh MapUtils](https://github.com/awgil/ffxiv_navmesh/blob/master/vnavmesh/MapUtils.cs), [Lifestream IPCProvider](https://github.com/NightmareXIV/Lifestream/blob/main/Lifestream/IPC/IPCProvider.cs), and [Lifestream TeleportService](https://github.com/NightmareXIV/Lifestream/blob/main/Lifestream/Services/TeleportService.cs).

- `Lifestream.IsBusy`: `bool ()`
- `Lifestream.Teleport`: `bool (uint aetheryteId, byte subIndex)`
- `vnavmesh.Nav.IsReady`, `Nav.PathfindInProgress`, `SimpleMove.PathfindInProgress`, `Path.IsRunning`: `bool ()`
- `vnavmesh.Query.Mesh.PointOnFloor`: `Vector3? (Vector3, bool allowUnlandable, float halfExtentXZ)`
- `vnavmesh.Nav.PathfindCancelable`: `Task<List<Vector3>> (Vector3 start, Vector3 end, bool fly, CancellationToken)`
- `vnavmesh.Path.MoveTo`: action `(List<Vector3>, bool fly)`
- `vnavmesh.Path.ListWaypoints`: `List<Vector3> ()`
- `vnavmesh.Path.Stop`, `Nav.PathfindCancelAll`: actions `()`

No direct DLL references to those plugins are required. The installed test versions were vnavmesh 1.2.3.14 and Lifestream 2.5.4.16. Their IPC availability was confirmed in-game. The user confirmed automatic navigation working on 0.5.1; logs independently show teleporting to Lower La Noscea via Moraby Drydocks, waiting for the destination mesh, finding a route, and starting to walk toward map coordinates (27.0, 15.9). The live stop-control check is pending.

Automated checks cover same-zone and cross-zone state transitions, missing dependencies/unlocks/ground, foreign movement, interruption, timeouts, partial paths, cancellation and late task completion. The native checks cover enemy-list geometry and are separate from travel testing.
