using System;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using GameAction = Lumina.Excel.Sheets.Action;

namespace BestiaryNav;

// A bounded recovery action, not a second rotation. Callers retain ownership,
// capture-mark, target, range, job and world-readiness checks before invoking it.
internal sealed class BstBasicCombo
{
    private readonly GameAction[] steps;
    public bool Compatible { get; }
    public string Status { get; private set; } = "No basic combo recovery requested.";

    public BstBasicCombo(IDataManager data)
    {
        var names = new[] { "Smash Axe", "Axeblade Bite", "Shieldsplitter" };
        var matches = data.GetExcelSheet<GameAction>(ClientLanguage.English)
            .Where(a => a.IsPlayerAction && a.CastType == 1 && a.ActionCategory.RowId == 3 &&
                names.Contains(a.Name.ExtractText())).ToArray();
        if (names.Any(n => matches.Count(a => a.Name.ExtractText() == n) != 1)) { steps = []; return; }
        steps = names.Select(n => matches.Single(a => a.Name.ExtractText() == n)).ToArray();
        Compatible = steps[0].RowId == 44879 && steps[1].ActionCombo.RowId == steps[0].RowId &&
            steps[2].ActionCombo.RowId == steps[1].RowId;
    }

    public unsafe bool TryUse(ulong target, int level)
    {
        if (!Compatible) { Status = "Basic combo data is incompatible."; return false; }
        var manager = ActionManager.Instance();
        if (manager == null || manager->ActionQueued || manager->AnimationLock > 0)
        { Status = "Basic combo waiting for the action lock or queue."; return false; }
        var step = BasicComboPolicy.Next(manager->Combo.Timer > 0, manager->Combo.Action,
            steps[0].RowId, steps[1].RowId, level, steps[1].ClassJobLevel, steps[2].ClassJobLevel);
        var action = steps[step];
        var name = action.Name.ExtractText();
        var status = manager->GetActionStatus(ActionType.Action, action.RowId, target);
        if (status != 0) { Status = $"{name} unavailable (game action status {status})."; return false; }
        var used = manager->UseAction(ActionType.Action, action.RowId, target);
        Status = used ? $"{name} recovery accepted." : $"The game rejected {name}.";
        return used;
    }
}
