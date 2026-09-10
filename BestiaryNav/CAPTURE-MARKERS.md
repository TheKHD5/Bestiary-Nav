# Uncaptured beast markers

The nearby target count reports unique Bestiary entries: seven Lost Lambs count as one uncaptured target. Individual eligible enemies still receive model and enemy-list labels.

From 0.4.5, enemy-list labels align vertically to text node 6 (the enemy name), beside the scaled row's right edge. The placement includes the ImGui viewport origin for windowed play, falls back to the left if the right side has insufficient room, and clamps the complete label background inside the viewport. The row lookup remains a guarded, read-only snapshot. The user's combat test reached both enemy-list rows without the earlier crash but exposed the off-screen positioning corrected here; the user confirmed the corrected placement in 0.4.6. The native regression suite now has 25 passing checks, including scaled widths, both horizontal edges, viewport offsets, vertical clipping and invalid geometry.

After login, capture records may remain unloaded until Master's Bestiary has been opened once. From 0.5.5, bare `/bnav` opens only Master's Bestiary, regardless of whether records have loaded, via `UIModule.ExecuteMainCommand(100)`. Row 100 was verified in the installed MainCommand sheet. The request runs once on the framework thread, checks the version profile, navigation conditions and command unlock, and does not toggle an already visible Bestiary closed. `/bnav config` only opens settings. No automatic retry persists across combat, loading or logout; run `/bnav` again when the UI is available. Marker counts update when the game's records arrive, not merely when the open request is sent.

Enable **Highlight uncaptured beasts** in Bestiary Nav settings. The default range is 50 yalms, adjustable from 10 to 100. Labels are drawn above nearby target models and beside their existing enemy-list rows. From 0.4.6, both use green for enemies at or below the player's current level and red for enemies above it. Colors reflect the live level comparison on each draw, including level changes; they describe the level requirement, not Capture's other conditions or gourd acquisition rules. Higher-level uncaptured enemies remain included in the unique nearby count. The overlay accepts no input and does not place shared party target markers, change targets, attack, or add unengaged enemies to the game's enemy list.

The feature reads capture state while the Bestiary is closed. Captured enemies, pets, dead/untargetable actors, out-of-range actors and objects outside the verified habitat/duty are excluded. Unknown or unloaded capture state produces no markers. Logging out, loading a zone, entering a cutscene, hiding the UI, turning off the setting or unloading the plugin clears the overlay. Combat does not hide markers; the separate combat setting governs map/Duty Finder navigation.

## Capture-state evidence

For the 0.5.8 compatibility update on 2026-09-10, the existing capture signature
matched exactly once in the installed executable, at static VA `141956D10`.
Its call at +20 still resolves to getter `141975A00`, which loads the singleton
at `142AFB670` and returns. The registration handler still requires state 3 at
+0x14; `1419760A0` still tests bit `(petId - 1)` for IDs 1 through 56.
The new upstream XBMManager definition in ClientStructs `694dbbf` independently
agrees with the bitset at +0, count at +0x10, and received state at +0x14.
Capture targets and game-version data did not change. No capture-state offsets or
signatures were changed by this update. The user confirmed nearby uncaptured
markers work again after re-enabling the local 0.5.8 build. A new capture and
enemy-list combat test were not separately reported for this update.

Verified against the installed game `2026.09.01.0000.0000` and Dalamud `15.0.3.3`, on 2026-09-09. The read-only investigation followed the game's item-registration check for Cu Sith Gourd, Item 49805. Its ItemAction is 2915, action 50454, and Data[0] is bestiary number 1. The gourd action's registration handler obtains a capture-state singleton, checks its state at +0x14 equals 3, and tests bit `(number - 1)` in its first seven bytes. The native function bounds the number to 1–56.

The inspected capture snapshot was `0x300102AFBD3`, with count 18 at +0x10 and load state 3 at +0x14. The set bits matched the captured numbers visible in both Bestiary pages: 1, 2, 5, 7, 8, 9, 10, 12, 13, 14, 15, 16, 18, 20, 22, 29, 41 and 42. Automated checks cover that independent snapshot and the first/last number boundaries.

`CaptureStateReader` resolves a unique signature in the gourd handler, follows its read-only singleton getter and validates the getter's instruction shape. It performs no native calls, detours or writes. Runtime reads require the exact version gate, a non-null singleton, loaded state 3 and a count matching the bitset. The bitset is read on framework updates; nearby objects are scanned at most four times per second. A changed capture bitset forces an immediate rescan. No collection data is persisted or shared between characters.

Recheck the handler, singleton fields, bit numbering and load-state meaning before updating the version gate. A structural check cannot make an incompatible native layout safe.

## Enemy identity and coverage

`capture-targets.json` contains BNpcName row IDs exported from the installed English game sheet for all 49 non-quest acquisition targets in `locations.json`. Runtime matching uses the enemy's numeric NameId plus the catalog's territory/duty, so it does not depend on the client's display language. The actor must also be a combatant battle NPC, not a pet or buddy. Alternate habitats or differently named capturable variants outside the sourced catalog are not claimed as covered.

Duty targets are acquisition targets: the label can point to the encounter that awards a gourd, rather than an enemy that accepts direct Capture. Cu Sith has no enemy target.

The enemy-list overlay uses the installed typed `EnemyListNumberArray` entity IDs and the addon's UldManager row nodes. Row zero is node 2; rows 1–7 are nodes 20001–20007 (type 1001). Each component has collision node 19 with a MouseDown event whose listener is the addon and whose parameter is the row index. `EnemyListRows` validates that registration and the component's owner before using the row's screen position, following HUD movement and scaling.

Version 0.4.0 crashed in `UncapturedMarkers.DrawEnemyList` during the live combat test. It incorrectly indexed `AddonEnemyList.EnemyOneComponent` as an array of row buttons. The live layout shows that the pointer leads to one row's descriptor: its first field is the button, but subsequent fields are other nodes belonging to that same row. Separate descriptors appear at successive addon fields. Version 0.4.1 temporarily disabled this path. Version 0.4.2 removes that lookup and uses the verified UldManager nodes instead. Enemy-list snapshots use `ReadProcessMemory` on the current process; unreadable pointers, oversized lists, unexpected types/owners, hidden nodes and mismatched events produce no label. Event traversal is bounded, and no native pointer is retained between frames.

Model labels use `IGameGui.WorldToScreen`; they are screen-space overlays anchored in the world, not a change to a model's material. The projection does not provide terrain occlusion.

## Live acceptance checks

The user confirmed the gold `Uncaptured #3` model labels on nearby Lost Lambs with 0.4.0 enabled. The 0.4.2 Release build completed without warnings or errors; 428 automated checks passed, including collection bit ordering, catalog identity, territory restrictions and range boundaries. Another 17 checks exercise the actual native row reader in an isolated process with allocated fixtures: all eight rows in reverse node-list order, unreadable pointers, hidden rows, incorrect owners/events, cyclic events and excessive counts. These checks do not substitute for live rendering validation. Subsequent live testing of 0.4.6 confirmed model labels, enemy-list alignment, level colors, and capture updates: after capturing a Lost Lamb, its labels disappeared and the unique nearby count updated. The current native regression suite has 25 checks.

- With an uncaptured Lost Lamb nearby, enable markers and verify `Uncaptured #3` above the animal.
- Engage a target manually and verify its existing enemy-list row receives the label, aligned with the row.
- Disable markers and verify both labels disappear immediately; reload and verify the saved preference.
- Capture the beast and verify the tag disappears when the collection record updates.
- Verify captured beasts and pets are excluded, and range changes/zone transitions remove stale labels.

Sources for typed rendering APIs: [IGameGui](https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/IGameGui/), [AddonEnemyList](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/AddonEnemyList.cs), [EnemyListNumberArray](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Arrays/EnemyListNumberArray.cs). Capture field semantics above come from the local native investigation, not these upstream type definitions.
