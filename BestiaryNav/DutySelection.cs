using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BestiaryNav;

internal sealed class DutySelection(IGameGui gui, Func<bool> canEdit, Action<string> report)
{
    private uint target;
    private long deadline;
    private int phase;
    private nint openedAddon;

    public void Start(uint dutyId)
    {
        target = dutyId;
        deadline = Environment.TickCount64 + 5000;
        phase = 0;
        openedAddon = 0;
    }

    public void Cancel() { target = 0; }

    // Called only from Framework.Update. Each mutation runs once, on separate
    // updates, with fresh pointers and selection verification before continuing.
    public unsafe void Update()
    {
        if (target == 0) return;
        try
        {
            if (!canEdit()) { Fail("Duty selection stopped because Duty Finder cannot be changed right now."); return; }
            if (Environment.TickCount64 >= deadline) { Fail("Duty Finder did not finish loading the target duty. Select it manually."); return; }
            var live = gui.GetAddonByName("ContentsFinder");
            if (live.IsNull || !live.IsReady || !live.IsVisible)
            {
                if (openedAddon != 0) Cancel();
                return;
            }
            if (openedAddon != 0 && live.Address != openedAddon) { Cancel(); return; }
            openedAddon = live.Address;
            var agent = AgentContentsFinder.Instance();
            if (!NativeSnapshot.TryRead<AgentContentsFinder>((nint)agent, out var state)) return;
            if (state.SelectedDuty.ContentType != ContentsType.Regular || state.SelectedDuty.Id != target)
            {
                // OpenRegularDuty can defer applying its highlighted duty until
                // the next native update. Never edit the previous duty meanwhile.
                if (phase == 0) return;
                Fail("Duty Finder selection changed; automatic selection stopped.");
                return;
            }
            if (!DutySelectionReader.TryFindEntry(agent, target, out var index)) return;
            if (!DutySelectionReader.TryReadSelection(agent, target, out var count, out var targetOnly))
            { Fail("Could not read Duty Finder selections safely."); return; }
            if (targetOnly) { Cancel(); return; }
            var addon = (AtkUnitBase*)live.Address;
            if (phase == 0)
            {
                // Native Clear Selection, never Join (callback 5).
                AtkValue clear = new() { Type = AtkValueType.Int, Int = 13 };
                phase = 1;
                addon->FireCallback(1, &clear);
            }
            else if (phase == 1)
            {
                if (count != 0) { Fail("Duty selections changed or could not be cleared. Select the duty manually."); return; }
                AtkValue* values = stackalloc AtkValue[2];
                values[0] = new() { Type = AtkValueType.Int, Int = 3 };
                values[1] = new() { Type = AtkValueType.UInt, UInt = index };
                phase = 2;
                addon->FireCallback(2, values);
            }
            else Fail("The target duty was not checked. Select it manually in Duty Finder.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Duty selection failed.");
            Fail("Could not select the target duty. Select it manually in Duty Finder.");
        }
    }

    private void Fail(string message) { Cancel(); report(message); }
}
