# Automatic travel

Bestiary Nav connects to **vnavmesh** and **Lifestream** through Dalamud IPC. Both must be installed, enabled, and expose the required IPC endpoints. They are runtime dependencies of automatic travel; spawn-area circles, Duty Finder selection and capture markers still work without them. The plugin does not download or enable other plugins.

Enable **Auto Navigate** in `/bnav config` settings. Left-clicking an outdoor Bestiary entry then opens its spawn-area circles and starts travel to the selected circle's center. `/bnav beast <number/name>` follows the same setting. `/bnav go <number/name>` explicitly requests travel for that selection, without changing the saved setting. **Stop travel** or `/bnav stop` cancels the active route and pending Bestiary request. Turning the travel setting off also stops it. Turning Location pop-up off also disables Auto Navigate and stops travel.

From 0.5.3, a single **Auto Navigate** checkbox is anchored above the top-left corner of Master's Bestiary. It follows window movement and scaling, stays within the viewport, and hides when the Bestiary is closed or the game UI is unavailable. It uses the same saved `AutoTravel` setting as the settings window. Turning it on also enables entry click handling; turning it off stops current automatic travel while leaving ordinary entry map/Duty Finder navigation enabled. The control is a small ImGui overlay, with no native UI node writes or extra click hooks. The user confirmed the checkbox appeared and followed window movement in 0.5.2; 0.5.3 moves the horizontal anchor from the center to the left as requested.

## Route behavior

1. If already in the destination territory, start from the current position without teleporting.
2. Otherwise, select the nearest normal aetheryte in that territory from the player's unlocked teleport list. Distance uses the aetheryte's MapMarker entry, with map scale and offsets applied. Housing destinations are excluded. Lifestream performs the teleport; normal game costs and restrictions apply.
3. Wait for the correct territory, the end of loading, and the destination navigation mesh. Use `SpawnAreaCoordinates.Convert` for the exact rounded X/Z used by the selected circle. Resolve altitude using `PointOnFloor` with optional reachability filtering disabled. Prefer the center itself, widening only when necessary, up to 20 yalms and never outside the circle radius. A map flag is not read or required.
4. If unmounted and Mount Roulette is available, request it once and wait for mounting to finish. Use the game's takeoff-permission check after mounting, in the destination territory. Already mounted players keep their mount. A failed mount falls back to ground travel after five seconds; an unfinished cast times out after fifteen seconds.
5. Request a cancellable flying path when mounted and flight is allowed (or already airborne); otherwise use a ground path. Pass the same flight mode to `Path.MoveTo`, allowing vnavmesh to handle takeoff. A missing flying route falls back to ground once only if still on land. Reject empty, nonfinite and partial routes, or routes whose start no longer matches the player. Start movement only while dependencies are available and idle.
6. Stop within five yalms of the resolved destination. Bestiary Nav does not target, attack, capture, or automatically dismount at the destination.

Flight depends on the character's unlocks, current mount, takeoff restrictions and a usable vnavmesh flight volume. Otherwise a ground route is used, mounted when possible. There is no dedicated swimming mode. Floor projection is intended for outdoor capture areas, not ambiguous stacked interiors. Nearest-aetheryte selection uses straight-line distance, not a cross-zone route graph. A missing unlock, missing mesh, or incomplete route leaves the circles available for manual travel. Duty entries open/select their duty without joining or interrupting a queue. Quest-only entries show guidance.

## Lifecycle and cancellation

From 0.5.4, **Cancel travel on manual movement** is a saved setting in `/bnav config`, off by default. It cancels the active trip during teleport, mesh waiting, pathfinding or walking when movement input is detected. Loading screens ignore input. The Auto Navigate setting stays enabled for the next selection. A canceled pathfinding result cannot start walking later.

The input reader uses the game's configured movement bindings through `UIInputData.IsInputIdDown`, the UI-filtered left controller stick (20/99 dead zone), and both UI-filtered mouse buttons. It ignores background keyboard input and text entry; camera-only mouse movement does not trigger cancellation. It reads input rather than character displacement, so ordinary vnavmesh walking is not mistaken for manual movement. Input reading is restricted to the verified game/Dalamud binding. No movement hooks or dependency configuration changes are added. Contracts: [FFXIVClientStructs InputData](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/System/Input/InputData.cs) and [UIInputData](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/UIInputData.cs). Manual-input cancellation was confirmed working in-game by the user on 0.5.4.

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

No direct DLL references to those plugins are required. The installed test versions were vnavmesh 1.2.3.14 and Lifestream 2.5.4.16. Their IPC availability was confirmed in-game. The user confirmed automatic navigation working on 0.5.1; logs independently show teleporting to Lower La Noscea via Moraby Drydocks, waiting for the destination mesh, finding a route, and starting to walk toward map coordinates (27.0, 15.9). The user confirmed manual-movement cancellation working in 0.5.4.

Automated checks also cover mount waits, takeoff eligibility, matching flying path/movement flags, ground fallback, cancellation during mounting, lost flight permission, and floor-search bounds. The native fixture checks are separate from live travel testing.

## 0.6.1 local validation

No. 31 (Gigantoad / Rivertoad) uses Lower La Noscea map 16 at (25, 23), whose displayed circle center is world X/Z (175, 75). Read-only inspection with installed vnavmesh 1.2.3.14 against the cached `s1f2__11F3D____0.navmesh` reproduced the old floor result (182.02705, 35.75525, 66.31952), over ten yalms from the center. The new lookup returns (174.82219, 32.750523, 74.846214), about 0.24 yalms away. The cache includes a flight volume. This verifies projection, not live path execution.

No. 40 (Ghost/Bogy) uses Middle La Noscea map 15 at (20, 19), circle center world X/Z (-75, -125). The current cached `s1f1__11F41____0.navmesh` projects a top-down query to Y=45.51031 on the surface. The optional `travelFloor` band (world Y=25..27) instead resolves to (-71, 26.75, -129), 5.66 yalms from the circle center. A read-only Detour ground-path query from that surface position reaches the underground destination polygon through a 40-polygon corridor; its waypoints go around to the entrance before descending. A higher candidate at Y=29.25 was rejected during research because the corridor did not reach its polygon. These results verify mesh connectivity, not live execution or enemy position.

Locations with a `travelFloor` use ground pathfinding and movement, retaining an existing mount. They require landing before starting, reject takeoff during the route, and never fall back to another height outside their band. Unannotated outdoor locations retain the usual surface lookup and flight behavior. The map circle keeps the catalog X/Z; this metadata controls only the travel destination's height.

Local GeneralAction row 9 is Mount Roulette. Installed FFXIVClientStructs signatures for `Control.GetFlightAllowedStatus`, `ActionManager.GetActionStatus`, and `ActionManager.UseAction` each match once in game executable `2026.09.01.0000.0000`. Native calls remain behind the existing game/Dalamud compatibility guard. Release build: zero warnings/errors. Regression suites: 772 functional and 36 native fixture checks. Live flight, underground arrival, and the final title-bar layout have not yet received explicit confirmation.
