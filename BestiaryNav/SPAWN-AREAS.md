# Spawn-area circles

Outdoor selections open the native game map with temporary gathering-style area circles. No new `<flag>` is set. Existing player flags are preserved. Duty entries still open Duty Finder; quest entries show guidance.

The catalog provides reported location centers, not measured spawn boundaries. Circles are explicitly labeled **approximate search areas**. The default radius is 60 world yalms; settings allow 10–200. Increasing the radius does not establish additional verified spawn locations. Auto Navigate travels to the reported center.

`SpawnAreaCoordinates` converts map X/Y into world X/Z using the map's SizeFactor and offsets. `SpawnAreaMap` uses those integer world coordinates with `AgentMap.AddGatheringTempMarker` and opens `MapType.GatheringLog`. Markers follow the native map's pan and zoom. Only locations on the chosen map/territory are drawn, within the native 12-marker capacity. Selecting another outdoor entry replaces the temporary search context, as opening a gathering-log result does. Cleanup checks the map, marker count, and every tooltip before removing plugin-owned circles.

## Compatibility evidence

- Game: `2026.09.01.0000.0000`; Dalamud: `15.0.3.4`.
- API source: [FFXIVClientStructs AgentMap](https://github.com/aers/FFXIVClientStructs/blob/694dbbf6c0bda544d18a8e2a7431e799c3662c16/FFXIVClientStructs/FFXIV/Client/UI/Agent/AgentMap.cs).
- Coordinate/circle usage cross-check: [GatherBuddy Executor](https://github.com/Ottermandias/GatherBuddy/blob/1e39592f55e57774f287bfcea87dbd651a5cf2d8/GatherBuddy/Plugin/Executor.cs).
- Installed executable signature checks: one match each for AddGatheringTempMarker (resolved `0x140FBEBB0`) and OpenMap (`0x140FBD130`). Exact game/Dalamud compatibility gating remains enabled.
- Automated checks cover independent map-center/boundary anchors, offsets, scale, invalid coordinates and radii, plus collection readiness, level filtering, captured exclusion, current-territory preference and alternate locations.
- **Pending live verification:** circle position, visible radius, map pan/zoom, changing entries/duties, and cleanup when unloading. Signature matching and compilation do not establish visual correctness.

Collection suggestions use reviewed minimum acquisition levels from `sources/acquisition.json`; they cannot verify quest or duty unlocks. Capture progress is session data and is not stored as a cross-character cache. Favorites are plugin preferences shared across characters.
