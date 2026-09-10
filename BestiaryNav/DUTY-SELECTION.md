# Duty Finder selection

When **Location pop-up** is off, native Bestiary clicks do not open or modify Duty Finder, whether queued or not. Explicit commands and collection actions remain available.

Outside a queue, Bestiary duty requests open the target's details, wait for the native list, send **Clear Selection**, then check the target duty on a later framework update. The result must be exactly one regular duty matching its ContentFinderCondition ID. A target already selected alone needs no toggle. Selection callbacks never join or withdraw from a queue or change party settings.

While already queued, requests only call OpenRegularDuty to highlight the target. They do not schedule selection callbacks or clear checked duties. Queue conditions are checked both before and after opening details and again on every pending selection update; starting a queue aborts any pending clear/select operation. No attempt is made to restore or modify queued selections after that abort.

## Verified native behavior

For game `2026.09.01.0000.0000` and Dalamud `15.0.3.4`, examined the installed executable alongside [the pinned ClientStructs definitions](https://github.com/aers/FFXIVClientStructs/tree/694dbbf6c0bda544d18a8e2a7431e799c3662c16):

- AddonContentsFinder vtable: `0x1422342C8`, resolved from a unique constructor signature. ReceiveEvent is `0x141266280`.
- Checkbox dispatch helper `0x1412681E0` sends `Int(3), UInt(contentListIndex)`.
- AgentContentsFinder ReceiveEvent `0x140F0C5E0`, event-kind 0, callback 3 (`0x140F0C9F1`) reads **one-based** ContentList indexing and toggles SelectedContent. This is distinct from the visible tree's row index and the duty's ID.
- Callback 13 (`0x140F0CC35`) clears SelectedContent and updates the native list/details flags. Callback 5 is Join and is never emitted by this plugin.
- AtkUnitBase.FireCallback signature has one match, resolving to `0x140675160`.

The code locates the target by `ContentsType.Regular` and ContentFinderCondition ID, including for trials and raids. Reads are bounded and guarded through NativeSnapshot. Native vectors are not edited directly. No addon or list pointers survive between updates.

## Guards and checks

- Exact compatibility gate; logged-in, visible game UI; combat preference; no loading/cutscene or pending duty queue.
- Duty/territory validation and `IUnlockState.IsInstanceContentUnlocked` before clearing any selection. Local game sheets verified all 12 catalog duty entries' InstanceContent-to-ContentFinderCondition mapping, including Hydra and raid IDs.
- Visible, ready Duty Finder, expected highlighted duty, unique target in its list, unchanged addon identity, and at most five selections.
- Separate clear/select/verify updates, each mutation at most once. Five-second timeout; a closed window, changed selection, logout, stop command, or newer destination cancels the operation.
- Native fixture tests cover one-based indexing, same-ID roulettes, missing/duplicate targets, empty/single/multiple selections, invalid list sizes, null agents, and unreadable pointers.

**Pending live verification:** start with multiple checked duties, choose a dungeon beast, confirm only its yellow checkbox remains checked, and confirm there is no queue registration. Repeat with an empty selection and the target already selected. Test a locked duty and a changed/closed window as well. While queued, confirm clicking a beast highlights its duty while the original queue and checked duties remain unchanged.
