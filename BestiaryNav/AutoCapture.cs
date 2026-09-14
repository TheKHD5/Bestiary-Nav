using System;
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace BestiaryNav;

internal sealed class AutoCapture
{
    // Verified against the supported local game sheets, then checked by name at
    // startup in English regardless of the client's display language.
    private const uint CaptureAction = 44880, InterestCaptured = 4626, CapturingInterest = 4624;
    private readonly Configuration config;
    private readonly CaptureStateReader captures;
    private readonly IReadOnlyDictionary<(uint Territory, uint NameId), uint> catalog;
    private readonly IObjectTable objects;
    private readonly ITargetManager targets;
    private readonly IClientState client;
    private readonly ICondition conditions;
    private readonly IPluginLog log;
    private readonly uint bst;
    private readonly bool compatible;
    private readonly bool recoveryStrikeCompatible;
    private readonly AutoCapturePolicy policy = new();
    private readonly CaptureMarkTracker marks = new();
    private long nextScan;
    private uint territory, playerId;
    public string Status { get; private set; } = "Auto Capture is off.";
    public string RecoveryStatus { get; private set; } = "No recovery strike requested.";
    public bool Compatible => compatible;
    public bool RunActive { get; set; }
    public ulong RunTarget { get; set; }
    public ulong DefenseTarget { get; set; }
    public void ObserveDefeat(uint entityId) => marks.Observe(entityId, false, null, Environment.TickCount64);

    public AutoCapture(Configuration config, CaptureStateReader captures,
        IReadOnlyDictionary<(uint Territory, uint NameId), uint> catalog, IObjectTable objects,
        ITargetManager targets, IClientState client, ICondition conditions, IPluginLog log,
        IDataManager data, uint bst, bool bindingActive)
    {
        this.config = config; this.captures = captures; this.catalog = catalog; this.objects = objects;
        this.targets = targets; this.client = client; this.conditions = conditions; this.log = log; this.bst = bst;
        var action = data.GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English).GetRowOrDefault(CaptureAction);
        var debuff = data.GetExcelSheet<Lumina.Excel.Sheets.Status>(ClientLanguage.English).GetRowOrDefault(InterestCaptured);
        var buff = data.GetExcelSheet<Lumina.Excel.Sheets.Status>(ClientLanguage.English).GetRowOrDefault(CapturingInterest);
        compatible = bindingActive && bst != 0 && action is { IsPlayerAction: true } && action.Value.Name.ExtractText() == "Capture" &&
            debuff?.Name.ExtractText() == "Interest Captured" && buff?.Name.ExtractText() == "Capturing Interest";
        var strike = data.GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English).GetRowOrDefault(44879);
        recoveryStrikeCompatible = compatible && strike is { IsPlayerAction: true, CastType: 1 } &&
            strike.Value.Name.ExtractText() == "Smash Axe";
    }

    // A single, game-validated opener can establish combat if the solver has no
    // usable next action. Capture recovery requires our mark; defense is limited
    // to the run's owned attacker actively targeting the player.
    public bool TryRecoveryStrike(ulong target) => TryStrike(target, false);
    public bool TryDefenseStrike(ulong target) => TryStrike(target, true);
    private unsafe bool TryStrike(ulong target, bool defense)
    {
        RecoveryStatus = "Recovery strike blocked: target, capture mark, or player readiness changed.";
        if (!recoveryStrikeCompatible || !config.CaptureRun || !config.MapTrackingOnClick || !RunActive ||
            target == 0 || (defense ? DefenseTarget : RunTarget) != target ||
            objects.LocalPlayer is not { } player || targets.Target is not IBattleNpc npc ||
            npc.GameObjectId != target || player.ClassJob.RowId != bst || player.IsDead || player.IsCasting ||
            !client.IsLoggedIn || conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51] ||
            conditions[ConditionFlag.Mounted] || conditions[ConditionFlag.InFlight] ||
            conditions[ConditionFlag.MountOrOrnamentTransition] ||
            conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78] ||
            conditions[ConditionFlag.OccupiedInQuestEvent] || npc.IsDead || npc.CurrentHp == 0 || !npc.IsTargetable ||
            npc.BattleNpcKind != BattleNpcSubKind.Combatant) return false;
        if (defense)
        {
            if (!conditions[ConditionFlag.InCombat] || !CaptureDefensePolicy.AttackingPlayer(
                new(npc.GameObjectId, npc.TargetObjectId, npc.Position, true, true,
                    (npc.StatusFlags & StatusFlags.InCombat) != 0), player.GameObjectId)) return false;
        }
        else
        {
            if (npc.Level == 0 || npc.Level > player.Level ||
                !catalog.TryGetValue((client.TerritoryType, npc.NameId), out var entry) ||
                !captures.TryRead(out var records) || !CaptureRules.IsUncaptured(records, entry)) return false;
            var marked = false;
            foreach (var status in npc.StatusList)
                if (CaptureMarkTracker.IsOwnMark(status.StatusId, status.SourceId, player.EntityId)) marked = true;
            if (!marked) return false;
        }
        var actions = ActionManager.Instance();
        if (actions == null || actions->ActionQueued || actions->AnimationLock > 0 || targets.Target?.GameObjectId != target)
        { RecoveryStatus = "Recovery strike waiting for the current action lock or queue."; return false; }
        var actionStatus = actions->GetActionStatus(ActionType.Action, 44879, target);
        if (actionStatus != 0)
        { RecoveryStatus = $"Smash Axe unavailable (game action status {actionStatus})."; return false; }
        var used = actions->UseAction(ActionType.Action, 44879, target);
        RecoveryStatus = used ? "Smash Axe opener accepted." : "The game rejected the Smash Axe opener.";
        return used;
    }

    public void Reset()
    {
        policy.Reset(); marks.Reset();
        nextScan = 0; territory = playerId = 0;
    }

    public unsafe void Update()
    {
        if (!config.AutoCapture && !RunActive) { Reset(); Status = "Auto Capture is off."; return; }
        if (!compatible) { Reset(); Status = "Auto Capture needs compatible game and action data."; return; }
        if (!client.IsLoggedIn || objects.LocalPlayer is not { } player ||
            conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51])
        { Reset(); Status = "Waiting for the game world."; return; }
        if (territory != client.TerritoryType || playerId != player.EntityId)
        { Reset(); territory = client.TerritoryType; playerId = player.EntityId; }
        var now = Environment.TickCount64;
        if (now < nextScan) return;
        nextScan = now + 250;
        if (player.ClassJob.RowId != bst) { Status = "Equip Beastmaster to use Auto Capture."; return; }
        try
        {
            // Scan all loaded battle actors, not just the selected/visible label
            // targets. Keep unexpired marks remembered if their actor disappears.
            marks.BeginScan();
            foreach (var obj in objects)
            {
                if (obj is not IBattleChara actor) continue;
                float? remaining = null;
                foreach (var status in actor.StatusList)
                {
                    if (CaptureMarkTracker.IsOwnMark(status.StatusId, status.SourceId, player.EntityId))
                        remaining = status.RemainingTime;
                }
                marks.Observe(actor.EntityId, !actor.IsDead, remaining, now);
            }
            var hasPlayerBuff = false;
            foreach (var status in player.StatusList)
                if (status.StatusId == CapturingInterest) hasPlayerBuff = true;
            // On enable/reload, a player buff can indicate a marked actor outside
            // the object table. Wait rather than replace that unseen capture.
            var captureBusy = marks.HasActiveMark(hasPlayerBuff, now);
            if (RunActive && RunTarget == 0) return;
            if (captureBusy) { Status = "Waiting for your existing Interest Captured effect to end."; return; }
            if (!captures.TryRead(out var bits)) { Status = "Open Master's Bestiary once to load capture records."; return; }
            if (targets.Target is not IBattleNpc npc || npc.BattleNpcKind != BattleNpcSubKind.Combatant)
            { Status = "Select an uncaptured beast as your main target."; return; }
            if (RunActive && npc.GameObjectId != RunTarget) return;
            var uncaptured = catalog.TryGetValue((client.TerritoryType, npc.NameId), out var number) && CaptureRules.IsUncaptured(bits, number);
            var ready = !player.IsDead && !player.IsCasting && !conditions[ConditionFlag.Mounted] && !conditions[ConditionFlag.InFlight] &&
                !conditions[ConditionFlag.MountOrOrnamentTransition] && !conditions[ConditionFlag.WatchingCutscene] &&
                !conditions[ConditionFlag.WatchingCutscene78] && !conditions[ConditionFlag.OccupiedInQuestEvent];
            var state = new CaptureSnapshot(config.AutoCapture || RunActive, compatible, player.ClassJob.RowId == bst, ready,
                RunActive || conditions[ConditionFlag.InCombat], RunActive || (npc.StatusFlags & StatusFlags.InCombat) != 0, uncaptured,
                !npc.IsDead, npc.IsTargetable, player.Level, npc.Level, npc.CurrentHp, npc.MaxHp, RunActive ? 100 : config.AutoCaptureMaxHpPercent,
                captureBusy, true, npc.GameObjectId);
            if (!AutoCapturePolicy.Eligible(state)) { Status = "Waiting for an eligible main target in combat."; return; }
            var actions = ActionManager.Instance();
            if (actions == null || actions->ActionQueued || actions->AnimationLock > 0 ||
                actions->GetActionStatus(ActionType.Action, CaptureAction, npc.GameObjectId) != 0)
            { Status = "Waiting for Capture to be usable (range, cooldown, or action lock)."; return; }
            var used = policy.TryCapture(state, now, target =>
            {
                // Never change targets, send a chat command, or queue a capture
                // against an actor that is no longer the player's main target.
                if ((!config.AutoCapture && !RunActive) || targets.Target?.GameObjectId != target ||
                    (RunActive && RunTarget != target)) return false;
                return actions->UseAction(ActionType.Action, CaptureAction, target);
            });
            if (used)
            {
                Status = $"Capture used on #{number} {npc.Name}. Waiting for Interest Captured.";
                log.Information(Status);
            }
        }
        catch (Exception ex)
        {
            nextScan = now + 5000;
            Status = "Auto Capture paused after an error; see Copy diagnostics or /xllog.";
            log.Error(ex, "Auto Capture update failed.");
        }
    }
}
