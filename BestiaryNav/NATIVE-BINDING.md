# Master's Bestiary click binding

Discovery date: 2026-09-09. Game repository version: `2026.09.01.0000.0000`. Installed Dalamud version: `15.0.3.3`. The plugin compiles against the installed FFXIVClientStructs through Dalamud.NET.Sdk 15.

## Observed layout

The English window titled **Master's Bestiary** is `XBMMonsterNotebook`; the adjacent details window is `XBMMonsterBookDetail`. A bounded read-only inspection of the running client's addon/node lists showed:

| Item | Observed value |
| --- | --- |
| Main addon AtkValues count | 284 |
| Entry component node IDs | 27 through 51 |
| Entry node type | 1024 |
| Number label within each component | Node 11, Text |
| Click area within each component | Node 12, Collision |
| Event registered on that area | MouseDown (3), listener = main addon |
| Event parameters | 4 through 28, corresponding to the 25 grid slots |
| Other entry events | InputReceived (12), MouseOver (6), MouseOut (7) |

On page 1, component 27's label was `No. 1` and its MouseDown parameter was 4. Component 31 had `No. 5` and parameter 8; component 43 had `No. 17` and parameter 20; component 51 had `No. 25` and parameter 28. Labels remained available for uncaught question-mark entries. These are node IDs within ULD managers, not memory offsets.

The runtime reader uses those labels, not an assumed selected-ID field. Mouse data uses the typed `AtkEventData.MouseData.ButtonId` field. Only button 0 is accepted. The adapter validates the current collision node's original event registration before reading the number. The matching node types and label format are also checked on every click.

No process-memory inspection utility is shipped in the plugin. Runtime reads use installed FFXIVClientStructs types on the framework thread. The diagnostic inspection used only read access to the local game process.

## Live results for 0.3.0

- The user confirmed that clicking No. 2 opened the map flag and No. 17 opened Copperbell Mines in Duty Finder.
- Plugin logs confirmed numbers 1, 2, 7, 11 and 14 from page-one clicks, including repeat activation of No. 7. The game's details panel continued responding.
- After switching to page two, clicking the uncaught No. 48 produced event parameter 26 and resolved to number 48 (Karlabos). The Duty Finder visibly opened Sastasha (Hard)'s details; no queue was registered. Changing pages itself did not navigate.
- Release build: zero warnings/errors. All 368 automated checks passed.

Sort/filter changes and non-left-button behavior remain additional manual regression cases below; the reader uses each control's live label and filters the mouse button explicitly.

## Revalidation after patches

### Dalamud 15.0.3.4 compatibility (2026-09-10)

The game remains `2026.09.01.0000.0000`. Reviewed the upstream Dalamud
[`15.0.3.3...15.0.3.4`](https://github.com/goatcorp/Dalamud/compare/15.0.3.3...15.0.3.4)
changes and ClientStructs
[`21898bf...694dbbf`](https://github.com/aers/FFXIVClientStructs/compare/21898bf815f0e56e02b7dc08f0a3e24822c759d0...694dbbf6c0bda544d18a8e2a7431e799c3662c16).
The consumed Bestiary Atk node/event layouts and menu command interfaces are unchanged.
The installed MainCommand sheet still names row 100 Master's Bestiary. Rebuilt 0.5.8
against the current installed libraries; 467 functional checks and 25 native row checks pass.
The current development and runtime ClientStructs DLLs have the same SHA-256:
`70E7DE516890AE10ED128A4B191EA6DF726CE70860C43478B88364D308BD9A2D`.
The profile now permits exactly Dalamud 15.0.3.4; other versions remain disabled.
The user re-enabled the local 0.5.8 build and confirmed `/bnav`, entry navigation,
and nearby uncaptured markers work again. The full sort/filter, page-two, and
combat acceptance matrix above was not repeated for this compatibility update.

Do not update version strings without checking the node and event semantics. Native pointer validation is structural and cannot make an incompatible FFXIVClientStructs layout safe.

Live acceptance cases:

- Load while the Bestiary is already open; click a captured entry such as No. 2 and verify its flag.
- Click an uncaught entry such as No. 17 and verify Copperbell Mines opens without queue registration.
- Change to page 2 and click No. 48; verify Sastasha (Hard), not the monster previously occupying that grid slot.
- Change sort/filter settings and confirm destinations follow displayed numbers; changing the controls alone must not navigate.
- Hover and right-click must not open a map or Duty Finder.
- Click the same entry again after closing the destination; it must reopen.
- Close/reopen the Bestiary and reload the plugin; no duplicate callbacks or stale requests.
- Cu Sith prints quest guidance; combat/loading/cutscene restrictions still apply to map and duty actions.

Automated checks cover all 50 number labels, malformed/out-of-range labels, the destination catalog, map coordinate transforms and navigation routing. Real native event delivery and visible game behavior require the live cases above.

## Upstream references

- [Addon lifecycle event definitions](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/Addon/Lifecycle/AddonEvent.cs)
- [AtkEvent and event types](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Component/GUI/AtkEvent.cs)
- [AtkEventData and mouse button field](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Component/GUI/AtkEventData.cs)
- [AtkUldManager and node types](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Component/GUI/AtkUldManager.cs)
