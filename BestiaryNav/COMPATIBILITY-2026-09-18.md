# September 2026 compatibility candidate

Status: **public release authorized, with remaining in-game checks documented**. A local installation
test was performed, then the previous installed files were restored at the user's
request. The owner then explicitly directed publication through the normal public
release channel. Worktree: `compat-15.0.3.5`, based on `84d9436` (0.6.13). Release: 0.6.14.

## Stable update and root cause

The [XIVLauncher stable distribution feed](https://kamori.goats.dev/Dalamud/Release/VersionInfo)
reported `assemblyVersion=15.0.3.5`, `supportedGameVer=2026.09.15.0000.0000`,
`track=release`, `hidden=false`, and changelog date `2026-09-17T15:52:18`.
The installed runtime's `version.json` and game's `ffxivgame.ver` agree.

The published plugin permits exactly game `2026.09.01.0000.0000` and Dalamud
`15.0.3.4`, so its compatibility guards disable native features.
The previous scheduled check missed the update because it relied on the stale
GitHub Releases listing. The 24-hour automation now checks the launcher feed's
assembly/game pair first and records this pair as awaiting live acceptance.

## Upstream review

- [Dalamud 15.0.3.4 to 15.0.3.5](https://github.com/goatcorp/Dalamud/compare/15.0.3.4...15.0.3.5):
  version update, XBMPet unlock-state APIs and tests, and the ClientStructs submodule;
  no AddonLifecycle changes in this comparison.
- [ClientStructs 694dbbf to b53cdf3](https://github.com/aers/FFXIVClientStructs/compare/694dbbf6c0bda544d18a8e2a7431e799c3662c16...b53cdf38b532a2ffdb711f13d9501db046bf7d0d):
  the Utf8String constructor signature changed. String construction is consumed
  transitively by native map operations; this candidate rebuilds against the new libraries.
- Gamepad, keyboard and mouse data types moved into separate files. Their existing
  names, sizes and stick/held-button fields used by movement cancellation are unchanged.
- New typed Bestiary addon declarations and XBMNoteModule functions were added.
  These do not establish that live node IDs or event delivery are unchanged.
- UIModuleHelpers grew; icon/drag-drop definitions, input-device wrappers and other
  members changed. The directly consumed AtkUnitBase/node/event, ActionManager,
  AgentMap, AgentContentsFinder, UIInputData, framework, GameObject, enemy-list and
  revive definitions have no edits in this comparison. A source review does not
  replace testing native behavior against the new executable.

Runtime and development FFXIVClientStructs DLL hashes match:
`2DC5B513647CC9897041D4BCFEB1E2C15EC087E6E0D04570D72F73B7614C39A5`.
The development Dalamud assembly reports `15.0.3.5`.

## Read-only executable and data checks

The existing gourd registration signature has exactly one match in the current
game executable at preferred VA `0x141956E70`. Its call at `+20` resolves to
`0x141975B60`, an eight-byte `mov rax,[rip+disp32]; ret` getter for singleton slot
`0x142AFB6A0`. The species check at `0x141976200` still requires loaded state 3 at
offset `0x14`, bounds-checks `species-1` to 0..55, and tests the corresponding bit
in the first seven bytes. Addresses are audit evidence, not hardcoded bindings.
The reader retains its loaded-state and count/popcount consistency checks.
The count and UI event behavior still require a live check.

The updated local game sheets confirm:

- All 49 capture targets and all 66 exact English BNpcName ID mappings match.
- MainCommand 100 is Master's Bestiary; ClassJob 43 is BST.
- Capture action 44880, Interest Captured status 4626, Capturing Interest status 4624,
  and the three basic recovery combo links match.
- All 37 stored map/territory pairs and all 12 duty/territory pairs match.

The capture-target database's game version records this data audit. Spawn
coordinates and level ranges were not newly surveyed in-game.

## Candidate behavior and checks

`binding.json` retains the last verified pair and separately specifies the
candidate pair. Runtime guards permit only that exact pair in this build, reject
incomplete profiles, and show `updated bindings - additional live checks pending`
in diagnostics. The settings Navigation tab and plugin log disclose that status.
The release script normally rejects a pending profile; `-AllowPendingLiveValidation`
permits an explicitly authorized public release without changing verification claims.

- Release build: zero warnings/errors against the new installed libraries.
- Functional checks: 1,735 passed, including 13 compatibility-profile cases.
- Native allocated-fixture checks: 44 passed. These do not call the live game.

## Remaining live checks

The user supplied diagnostics from 0.6.14.0 on game 2026.09.15.0000.0000 and
Dalamud 15.0.3.5. These confirm the candidate loads, reports capture records ready
(41 captured), connects travel dependencies, and reports no drawing error. No
model labels were present in that snapshot. This does not confirm Bestiary opening,
entry navigation, visible labels, travel, or combat behavior. Those checks below
remain unconfirmed. The user then requested restoration of the previous files;
the installed DLL and dependency file were restored to 0.6.13.0 and their hashes
matched the pre-test backup. Installer metadata and user settings were preserved.

Install only after the user authorizes a local replacement and disables the plugin.
No computer-use is authorized. Keep automated combat/travel off for initial checks.

1. Enable the candidate and run `/bnav`: Master's Bestiary should open. Verify
   capture records load and their count matches the player's actual Bestiary.
2. Click an outdoor entry: its spawn circle should open on the right map. Click
   a duty entry: the correct duty should highlight/select without starting a queue.
3. Change page/filter and verify destinations follow displayed numbers. Right-clicks
   and page controls must not navigate. Close/reopen and retry.
4. Near an uncaptured target, verify model/enemy-list labels and capture state.
5. Check Auto Navigate and manual-movement cancellation. Capture/levelling combat,
   supplies and revival remain additional native behavior checks if used.

Record actual results, not an assumed full acceptance matrix. After remaining
checks pass, promote the candidate versions to the verified fields and remove both
candidate fields in a subsequent release. The owner authorized public publication
of 0.6.14 before these checks were completed; it does not claim full verification.
