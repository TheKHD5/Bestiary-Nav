# Bestiary Nav

An MIT-licensed Dalamud API 15 / .NET 10 plugin. Left-click an entry in the native Master's Bestiary to open its acquisition map with an active flag, or open its target duty in Duty Finder. The data covers all 50 beasts: 37 outdoor capture-area flags, 12 duty entries and quest guidance for Cu Sith. See [LOCATIONS.md](LOCATIONS.md) for the sourced destinations.

## Build and use

Install the .NET 10 SDK and use Dalamud 15 libraries updated by XIVLauncher. The project uses `Dalamud.NET.Sdk/15.0.0`. On Windows the SDK normally resolves `%APPDATA%\XIVLauncher\addon\Hooks\dev`; `DALAMUD_HOME` can override this location.

```powershell
dotnet build .\BestiaryNav.csproj -c Release
```

Add `bin/Release/BestiaryNav.dll` to Dalamud's development plugin locations, then enable Bestiary Nav in Installed Dev Plugins. After rebuilding, disable and re-enable it to load the new DLL. Open Master's Bestiary and left-click a beast icon. Uncaught question-mark entries work too because navigation uses their displayed number. Cu Sith has no map or duty acquisition and displays its starting-quest guidance.

Commands remain available for diagnostics:

- `/bnav` opens settings and also opens Master's Bestiary if capture records have not loaded yet. `/bnav config` opens only settings. The plugin installer's main-window and configuration buttons open this same settings window. Entry click navigation and combat blocking can be changed here; preferences save automatically through Dalamud.
- `/bnav beast <number or name>` navigates directly. Examples: `/bnav beast 2`, `/bnav beast Pugil`.
- `/bnav go <number or name>` starts automatic travel to an outdoor entry using Lifestream and vnavmesh.
- `/bnav map <territoryId> <mapId> <x> <y>` tests a map flag using displayed coordinates.
- `/bnav probe XBMMonsterNotebook` logs sampled native receive events and the first 32 integer AtkValues.
- `/bnav stop` stops automatic travel and removes the probe.

## Automatic travel

Install and enable **vnavmesh** and **Lifestream**, then use **Auto Navigate** above Master's Bestiary or **Automatically travel to selected beasts** in settings. The two controls share the same saved setting. Outdoor entry selections teleport to an unlocked destination-zone aetheryte when needed, then walk to the capture area. Settings show dependency availability, progress and a **Stop travel** button. Turning Auto Navigate off stops current travel. This option is off by default; `/bnav go` requests it for one selection. Duty entries still open Duty Finder. See [AUTO-TRAVEL.md](AUTO-TRAVEL.md) for the IPC contracts, route limitations and cancellation behavior.

## Uncaptured beast markers

In settings, enable **Tag nearby uncaptured beasts** to show labels above nearby acquisition targets and beside their existing enemy-list rows. Green means the enemy is at or below your current level; red means it is above your level. The option is off by default; range is adjustable from 10 to 100 yalms. Capture status updates from the game's collection records even with the Bestiary closed. The markers use numeric enemy IDs and the sourced habitats/duties in the catalog. They are local visual overlays and do not change targeting or place shared party markers. See [CAPTURE-MARKERS.md](CAPTURE-MARKERS.md) for coverage and validation details.

## Native entry clicks

The observed native addon is `XBMMonsterNotebook`. Bestiary Nav listens to `IAddonLifecycle.PostReceiveEvent`, allowing the original game handler to finish. Entry collision nodes emit `MouseDown` with parameters 4–28; the callback accepts only left-button events (`MouseData.ButtonId == 0`). Hover, right-click, page buttons and controller focus changes do not navigate.

The 25 grid components have node IDs 27–51. Each has collision node 12 and number text node 11. `BestiarySelectionReader` reacquires the live addon, checks its readiness, visibility and address, resolves the component for that event parameter, verifies node types and the existing native event registration, and reads the current number label. It does not infer a monster identity from the grid slot or cache a row number. The visible label therefore remains the identity when pages, filters or sorting reuse those controls.

Only the resulting managed navigation request survives the callback. The map/Duty Finder action runs on `IFramework.Update`, outside native event dispatch. Closing the addon clears pending navigation, and unloading unregisters the listeners. A short duplicate-event guard permits later clicks on the same beast to reopen its destination.

Entry click handling uses no invisible input target, synthetic click forwarding, custom native detour or per-frame node scan. The game retains its normal entry selection and details panel. Native clicks are version-gated by `binding.json` to game `2026.09.01.0000.0000` and Dalamud `15.0.3.3`; commands remain usable when an update disables the binding. Revalidate the layout before updating the gate. See [NATIVE-BINDING.md](NATIVE-BINDING.md) for discovery evidence and live test cases.

The public bestiary numbers are deliberately separate from NPC row IDs. The adapter reads the game's displayed number directly, so an unverified native monster-ID mapping is unnecessary.

## Map and active flag

The preferred API is:

```csharp
using Dalamud.Game.Text.SeStringHandling.Payloads;

// territoryId/mapId are uint; mapX/mapY are float in displayed map units.
var payload = new MapLinkPayload(territoryId, mapId, mapX, mapY);
bool opened = GameGui.OpenMapWithMapLink(payload);
```

`IGameGui.OpenMapWithMapLink` is documented to open the game map **with a flag at the supplied position**. There is no second flag-setting call or chat message needed. A successful call sets/replaces the active flag used by `<flag>`; merely constructing the payload or printing it in chat does not perform this action. It does not send `<flag>` to other players. [IGameGui reference](https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/IGameGui/)

Do not separately call `AgentMap.SetFlagMapMarker` for this workflow. Low-level AgentMap calls add patch-sensitive native dependencies and may require their own map-open and flag-state handling. Keep the public Dalamud operation as the single authority.

The `float` constructor takes displayed X/Y and applies the map's scale/offset conversion, including its normal `0.05f` adjustment for game display truncation. The `int` constructor takes **raw world coordinates multiplied by 1000**, not displayed integers. Thus `new MapLinkPayload(t, m, 20, 25)` is different from `new MapLinkPayload(t, m, 20f, 25f)`. [MapLinkPayload implementation](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/Text/SeStringHandling/Payloads/MapLinkPayload.cs)

If the source database contains world coordinates, world **X/Z** form the two map axes; world Y is altitude. For an axis `w`, offset `o`, and `s = SizeFactor / 100`:

```text
display = 1 + (41 / s) * (((w + o) * s + 1024) / 2048)
world   = (((display - 1) * s * 2048 / 41) - 1024) / s - o
```

`MapCoordinates.WorldToMap` and `MapToWorld` implement these transforms without display-rounding fudge. Use `OffsetX` for world X and `OffsetY` for world Z. Do not convert displayed coordinates twice.

For precise world positions, the integer payload overload also avoids a display-coordinate round trip:

```csharp
var payload = new MapLinkPayload(territoryId, mapId,
    checked((int)MathF.Round(worldX * 1000f)),
    checked((int)MathF.Round(worldZ * 1000f)));
bool opened = GameGui.OpenMapWithMapLink(payload);
```

Validate finite/range inputs first. Map IDs select the correct floor; X/Y or territory alone cannot distinguish all map layers. A flag records a point, not a guaranteed live spawn or a particular field instance.

## Data

Use embedded JSON deserialized once into `Dictionary<uint, MonsterEntry>`. This is small, reviewable, contributor-friendly and needs no native database dependency. Compiled records are reasonable for a tiny immutable catalog; SQLite is useful only when relational queries, large datasets or user-editable persistence justify its additional machinery.

The `idSpace` is `masters-bestiary-number`: public numbers 1 through 50 from the user's Icy Veins roster. These are explicitly separate from native UI/NPC IDs. Schema version 2 supports `navigationKind` values `map`, `duty`, and `quest`. Each map entry has a reported capture point with territory/map IDs and source; each duty has a `ContentFinderCondition` ID, territory and encounter note; Cu Sith carries quest guidance. Names/classifications come from Icy Veins, capture targets/coordinates from the community Bestiary and individual enemy pages, and numeric map/duty relationships from current XIVAPI game data. Every record carries source URLs.

This abbreviated real record illustrates the schema (the shipped file also includes provenance):

```json
{
  "schemaVersion": 2,
  "idSpace": "masters-bestiary-number",
  "monsters": [
    {
      "bestiaryNumber": 2,
      "displayName": "Squirrel",
      "captureTarget": "Ground Squirrel",
      "navigationKind": "map",
      "locations": [
        {
          "territoryTypeId": 148,
          "mapId": 4,
          "x": 24,
          "y": 16,
          "note": "Central Shroud, level 1+; reported capture area."
        }
      ]
    }
  ]
}
```

`LocationData.cs` defines the C# schema and validates the header, duplicate/zero IDs, missing locations, and non-finite coordinates. Runtime sheet validation checks both rows exist, that `Map.TerritoryType.RowId` matches, and that `SizeFactor` is nonzero. Checking `TerritoryType.Map.RowId == mapId` would wrongly reject valid secondary floors. The coordinate bounds check tests the map texture extent, not navigability or spawn accuracy. Exceptional/shared-map datasets may need a deliberately reviewed relationship override; the sample fails closed.

The coordinates retain the precision reported by their source. They mark capture areas; roaming enemies are not guaranteed to occupy one exact point. No additional decimal places or dungeon coordinates were invented. To rebuild the embedded catalog and source table from the reviewed snapshots, run `node ../tools/build-bestiary-data.mjs` from this directory. `sources/` contains factual evidence; raw research HTML stays in the ignored workspace cache.

## Duty Finder navigation

For the 12 duty beasts, navigation opens the corresponding duty detail screen as requested:

```csharp
var agent = AgentContentsFinder.Instance();
if (agent != null)
    agent->OpenRegularDuty(contentFinderConditionId, false);
```

The integer is `ContentFinderCondition.RowId`, not `TerritoryType`, `InstanceContent`, or the sheet's `Content` value. The implementation validates that the row exists, its territory matches, and its content type is a dungeon/trial/raid before dispatching on the framework thread. It does not register for a duty, alter party settings or toggle unrestricted mode. The native method returns void, so a dispatched request is not a visual verification of the opened window; unlock restrictions remain under game control. [FFXIVClientStructs source](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Agent/AgentContentsFinder.cs)

Example: `/bnav beast Slime` opens Copperbell Mines (CFC 3); `/bnav beast Karlabos` opens Sastasha (Hard) (CFC 28). `/bnav beast Cu Sith` prints the starting-quest/gourd instructions because it has no target duty or wild spawn.

## Safety and verification

Map and duty navigation skip logged-out, hidden-UI, loading and cutscene states. Combat blocking is an optional UX policy controlled by `blockInCombat`, not a Dalamud map API requirement. Pending work is consumed once instead of being replayed unexpectedly after combat or loading. Repeated events for one monster are suppressed for 350 ms; later clicks can reopen the destination. Native listeners, probes and framework callbacks are removed on disposal.

Validation performed on this machine: Release build against installed Dalamud 15.0.3.3, with zero warnings/errors. The pure C# checks cover coordinate anchors and offsets, round trips, invalid records, all 50 shipped names/numbers, source provenance, map/territory relationships, Duty Finder IDs and routing for map/duty/quest entries. Run them from this directory with:

```powershell
dotnet run --project ..\BestiaryNav.Checks\BestiaryNav.Checks.csproj -c Release
```

The Windows native row regression checks require the installed Dalamud development DLLs. They run in a separate process against allocated fixtures, without accessing the game:

```powershell
dotnet run --project ..\BestiaryNav.NativeChecks\BestiaryNav.NativeChecks.csproj -c Release
```

The user verified command-based map/flag, Duty Finder and Cu Sith guidance in-game. The native addon identity, entry node layout and event registrations were inspected directly in the running client. Live click acceptance cases and patch revalidation are documented in [NATIVE-BINDING.md](NATIVE-BINDING.md). Published capture coordinates have not been independently surveyed at every physical spawn.

API 10 is a historical target, not a binary compatibility promise. This project explicitly targets API 15. Backporting requires the matching SDK/runtime, Lumina row APIs and FFXIVClientStructs layout; older `AddonArgs.Addon`/`IGameGui` return types may be raw pointers rather than today's wrappers. Do not load an API-10-era native layout into a current client.
