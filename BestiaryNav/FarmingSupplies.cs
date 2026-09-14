using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Inventory;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace BestiaryNav;

internal sealed record FarmingFood(uint Id, string Name, uint Count);

internal sealed class FarmingSupplies(IDataManager data, IGameInventory inventory, IBuddyList buddies)
{
    private readonly Dictionary<uint, string> foods = data.GetExcelSheet<Item>(ClientLanguage.English)
        // ItemUICategory 46 is Meal (singular). Use the stable sheet ID rather
        // than a UI label: a label mismatch otherwise hides every inventory food.
        .Where(i => i.ItemUICategory.RowId == 46 && i.ItemAction.RowId != 0)
        .ToDictionary(i => i.RowId, i => i.Name.ExtractText());
    private readonly uint greens = data.GetExcelSheet<Item>(ClientLanguage.English)
        .FirstOrDefault(i => i.Name.ExtractText() == "Gysahl Greens").RowId;
    private readonly uint fed = data.GetExcelSheet<Lumina.Excel.Sheets.Status>(ClientLanguage.English)
        .FirstOrDefault(s => s.Name.ExtractText() == "Well Fed").RowId;
    private long nextUse, settleUntil;
    public string Status { get; private set; } = "Optional supplies are off.";
    // Resolve only the local player's live companion, not arbitrary friendly
    // NPCs, mount names, or another player's chocobo. Use the full object ID.
    public ulong CompanionId => buddies.CompanionBuddy is { CurrentHP: > 0 } buddy &&
        buddy.GameObject is IBattleChara { IsDead: false, CurrentHp: > 0 } companion ? companion.GameObjectId : 0;
    private List<GameInventoryItem> Items()
    {
        List<GameInventoryItem> result = [];
        // Bag1..4 are the four normal inventory pages; no retainers or saddlebags.
        foreach (var bag in new[] { GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4 })
            foreach (var item in inventory.GetInventoryItems(bag))
                if (!item.IsEmpty && item.Quantity > 0) result.Add(item);
        return result;
    }
    public IReadOnlyList<FarmingFood> FoodChoices() => Items().Where(i => foods.ContainsKey(i.BaseItemId) && !i.IsCollectable)
        .GroupBy(i => i.ItemId).Select(g => new FarmingFood(g.Key, foods[g.First().BaseItemId] + (g.First().IsHq ? " (HQ)" : ""), (uint)g.Sum(i => i.Quantity)))
        .OrderBy(i => i.Name).ToArray();

    // Called only stationary, dismounted, out of combat by the farming controller.
    // true means wait for the attempted item action; missing stock never stalls farming.
    public unsafe bool Prepare(FarmingOptions options, IPlayerCharacter player, long now)
    {
        if (now < settleUntil) return true;
        if (now < nextUse) return false;
        nextUse = now + 5000;
        var items = Items();
        uint use = 0;
        Status = "Supplies ready.";
        if (options.UseFood && fed != 0 && !player.StatusList.Any(s => s.StatusId == fed && s.RemainingTime > 0))
        {
            if (options.FoodId != 0 && foods.ContainsKey(options.FoodId % 1000000) && items.Any(i => i.ItemId == options.FoodId)) use = options.FoodId;
            else Status = "Selected food is unavailable; farming continues without it.";
        }
        if (use == 0 && options.SummonChocobo && buddies.CompanionBuddy == null)
        {
            if (greens != 0 && items.Any(i => i.ItemId == greens)) use = greens;
            else Status = "No Gysahl Greens in your bags; farming continues without a companion.";
        }
        if (use == 0) return false;
        var actions = ActionManager.Instance();
        if (actions == null || player.IsCasting || actions->ActionQueued || actions->AnimationLock > 0) return true;
        var result = actions->GetActionStatus(ActionType.Item, use, player.GameObjectId);
        if (result != 0) { Status = $"Supply item unavailable (game status {result}); retrying later."; nextUse = now + 30000; return false; }
        var accepted = actions->UseAction(ActionType.Item, use, player.GameObjectId, 0xFFFF);
        Status = accepted ? "Using farming supply…" : "Supply use rejected; retrying later.";
        nextUse = now + (accepted ? 5000 : 30000);
        if (accepted) settleUntil = now + 3500;
        return accepted;
    }
}
