using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace BestiaryNav;

internal enum FarmingPhase { Idle, Choosing, Traveling, Preparing, Searching, Fighting, Recovering }

internal sealed class FarmingRun(Configuration config, FarmingDatabase database, IObjectTable objects,
    ITargetManager targets, IClientState client, ICondition conditions, IPluginLog log,
    CaptureRotationIpc rotation, TravelController travel, TravelPlanBuilder plans, Func<TravelPlayer> travelPlayer,
    Func<bool> compatible, Func<bool> uiAvailable, uint bst, FarmingSupplies supplies, IDataManager data,
    IFateTable fates, FarmingRespawn respawn) : IDisposable
{
    public bool Enabled => Phase != FarmingPhase.Idle;
    public FarmingPhase Phase { get; private set; }
    public string Status { get; private set; } = "Farming is off.";
    public string SuppliesStatus => supplies.Status;
    public IReadOnlyList<FarmingFood> FoodChoices() => supplies.FoodChoices();
    private FarmingSelection? selection;
    private TravelPlan? area;
    private readonly SpawnSearchRoute search = new();
    private readonly CaptureCombatWatchdog watchdog = new();
    private readonly Dictionary<FarmingArea, long> unavailable = [];
    private ulong target;
    private bool ownsTravel, targetSelected, engaged, defending;
    private long nextScan, nextVerify, nextAction, waitUntil, targetDeadline, areaDeadline;
    private int reached;
    private ushort selectedFate;
    private readonly Dictionary<ushort, long> completedFates = [];
    private readonly HashSet<uint> notoriousBases = data.GetExcelSheet<NotoriousMonster>().Where(n => n.BNpcBase.RowId != 0).Select(n => n.BNpcBase.RowId).ToHashSet();
    private readonly bool strikeValid = data.GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English)
        .GetRowOrDefault(44879) is { IsPlayerAction: true, CastType: 1 } strike && strike.Name.ExtractText() == "Smash Axe";

    public void Start()
    {
        Stop();
        try
        {
            config.Farming.Normalize();
            if (!compatible() || objects.LocalPlayer is not { IsDead: false } p || p.ClassJob.RowId != bst ||
                !client.IsLoggedIn || conditions[ConditionFlag.InCombat] || !uiAvailable())
                throw new InvalidOperationException("Start farming as BST, out of combat, with compatible game data.");
            rotation.Acquire(bst);
            if (FarmingPolicy.ReachedGoal(p.Level, config.Farming)) { Stop("Target BST level already reached."); return; }
            unavailable.Clear(); selection = null; area = null;
            selectedFate = 0; completedFates.Clear();
            nextScan = nextVerify = nextAction = waitUntil = 0;
            Phase = FarmingPhase.Choosing; Status = "Choosing a farming area…";
        }
        catch (Exception ex) { Stop(ex.Message); }
    }

    public unsafe void Update()
    {
        if (!Enabled) return;
        var now = Environment.TickCount64;
        try
        {
            var state = travelPlayer();
            if (!compatible()) { Stop("Farming stopped: incompatible game data."); return; }
            if (config.CancelTravelOnManualMovement && state.ManualMovement) { Stop("Farming canceled by manual movement."); return; }
            if (Phase == FarmingPhase.Recovering && (state.Loading || !state.WorldReady))
            { Status = "Returning after incapacitation; waiting for the world…"; return; }
            if (Phase == FarmingPhase.Traveling && travel.AwaitingWorld && (state.Loading || !state.WorldReady))
            { UpdateTravel(state, now); return; }
            var player = objects.LocalPlayer;
            if (!client.IsLoggedIn || player == null || player.ClassJob.RowId != bst)
            { Stop("Farming stopped: logged out or no longer BST."); return; }
            if (FarmingPolicy.ReachedGoal(player.Level, config.Farming)) { Stop($"Target BST level {config.Farming.TargetLevel} reached. Farming complete."); return; }
            if (state.Loading || !uiAvailable() || conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78] ||
                conditions[ConditionFlag.OccupiedInQuestEvent] || conditions[ConditionFlag.BoundByDuty] ||
                conditions[ConditionFlag.BoundByDuty56] || conditions[ConditionFlag.BoundByDuty95])
            { Stop("Farming stopped during an event, duty, or unexpected area transition."); return; }
            if (player.IsDead)
            {
                if (Phase != FarmingPhase.Recovering)
                {
                    EndTarget(); rotation.Release(); Phase = FarmingPhase.Recovering;
                    if (selection != null) unavailable[selection.Area] = now + 120000;
                    selection = null; area = null; selectedFate = 0;
                }
                Status = config.Farming.AutoRespawn ? "Incapacitated: returning, then resuming farming…" : "Waiting for revival; farming remains enabled.";
                if (config.Farming.AutoRespawn) respawn.TryReturn(now);
                return;
            }
            if (Phase == FarmingPhase.Recovering)
            {
                if (player.CurrentHp < player.MaxHp * 0.7f || conditions[ConditionFlag.InCombat]) { Status = "Recovering after revival…"; return; }
                rotation.Acquire(bst); Phase = FarmingPhase.Choosing; waitUntil = now + 3000; return;
            }
            if (now >= nextVerify) { rotation.Verify(Phase != FarmingPhase.Traveling); nextVerify = now + 500; }
            var combat = conditions[ConditionFlag.InCombat];
            var actors = objects.OfType<IBattleNpc>().Where(n => n.BattleNpcKind == BattleNpcSubKind.Combatant).ToArray();
            var npc = actors.FirstOrDefault(n => n.GameObjectId == target);
            if (target != 0 && (npc == null || npc.IsDead || npc.CurrentHp == 0))
            {
                EndTarget(); waitUntil = now + 3000; Phase = FarmingPhase.Preparing;
            }
            if (target == 0 && combat)
            {
                var id = CaptureDefensePolicy.Select(actors.Select(n => new CaptureAggressor(n.GameObjectId, n.TargetObjectId,
                    n.Position, !n.IsDead && n.CurrentHp > 0, n.IsTargetable, (n.StatusFlags & StatusFlags.InCombat) != 0)),
                    player.GameObjectId, player.Position);
                npc = actors.FirstOrDefault(n => n.GameObjectId == id);
                if (npc == null) { rotation.SetRunning(false); StopMovement(); Status = "Waiting for an attacker to become visible…"; return; }
                Pick(npc, true, now);
            }
            if (target != 0 && npc != null)
            {
                Fight(npc, player, now);
                return;
            }
            if (now < waitUntil) return;
            if (selectedFate != 0 && (!config.Farming.ParticipateInFates ||
                !fates.Any(f => f.FateId == selectedFate && f.State == FateState.Running && f.TimeRemaining > 20)))
            {
                completedFates[selectedFate] = now + 120000;
                StopMovement(); selectedFate = 0; selection = null; area = null; Phase = FarmingPhase.Choosing;
            }
            if (selection != null && selection.Complete(player.Level))
            {
                if (selectedFate != 0) { completedFates[selectedFate] = now + 120000; selectedFate = 0; }
                rotation.SetRunning(false); StopMovement(); selection = null; area = null; Phase = FarmingPhase.Choosing;
                Status = "You reached this area's highest target level; choosing a stronger area…";
            }
            if (selection == null) { if (!ChooseFate(player.Level, player.Position, now)) Choose(player.Level, now); return; }
            if (Phase is FarmingPhase.Searching or FarmingPhase.Preparing && selectedFate == 0 && ChooseFate(player.Level, player.Position, now)) return;
            if (Phase == FarmingPhase.Traveling) { UpdateTravel(state, now); return; }
            if (client.TerritoryType != area!.TerritoryId) { BeginTravel(now); return; }
            if (conditions[ConditionFlag.Mounted] || conditions[ConditionFlag.InFlight] || conditions[ConditionFlag.MountOrOrnamentTransition])
            {
                StopMovement(); rotation.SetRunning(false);
                if (now >= nextAction && !player.IsCasting && !conditions[ConditionFlag.MountOrOrnamentTransition])
                {
                    nextAction = now + 1000;
                    var actions = ActionManager.Instance();
                    if (actions != null && actions->GetActionStatus(ActionType.GeneralAction, 23) == 0) actions->UseAction(ActionType.GeneralAction, 23);
                }
                Status = "Landing and dismounting for farming…"; return;
            }
            if (player.IsCasting) { StopMovement(); return; }
            if (player.CurrentHp < player.MaxHp * 0.7f) { StopMovement(); Status = "Recovering HP before the next pull…"; return; }
            if (Phase == FarmingPhase.Preparing)
            {
                StopMovement(); rotation.SetRunning(false);
                if (supplies.Prepare(config.Farming, player, now)) { Status = supplies.Status; return; }
                // An interrupted journey may have ended well outside the destination.
                if (!CaptureRunPolicy.InArea(player.Position, area.MapPoint, area.SearchRadius, area.TargetFloor)) { BeginTravel(now); return; }
                search.Reset(area.MapPoint, area.SearchRadius); reached = 0;
                Phase = FarmingPhase.Searching; areaDeadline = now + 300000;
            }
            if (now < nextScan) return;
            nextScan = now + 500;
            npc = actors.Where(n => Eligible(n, player.Level) && (selectedFate != 0 || (n.StatusFlags & StatusFlags.InCombat) == 0))
                .OrderBy(n => Vector3.DistanceSquared(n.Position, player.Position)).FirstOrDefault();
            if (npc != null)
            {
                StopMovement();
                if (supplies.Prepare(config.Farming, player, now)) { Status = supplies.Status; return; }
                Pick(npc, false, now); return;
            }
            if (now >= areaDeadline) { FailArea(now, "No eligible targets found after five minutes."); return; }
            if (ownsTravel)
            {
                travel.Update(state, now, config.CancelTravelOnManualMovement);
                if (travel.Active) return;
                ownsTravel = false;
                if (!travel.Arrived && !travel.NoRoute) { Stop(travel.Status); return; }
                if (travel.Arrived) reached++;
                search.Complete();
            }
            var point = search.Next(player.Position);
            if (point == null)
            {
                if (reached == 0) { FailArea(now, "No reachable farming patrol points."); return; }
                search.Reset(area.MapPoint, area.SearchRadius); reached = 0; waitUntil = now + 3000; return;
            }
            ownsTravel = true;
            travel.Start(new(area.TerritoryId, point.Value, 0, 0, "farming patrol", 10, area.TargetFloor,
                AllowMount: false, AllowFlight: false, ArrivalDistance: 3,
                SearchBoundary: new(area.MapPoint, area.SearchRadius, area.TargetFloor)), state, now);
            Status = $"Farming {selection.Area.Name} (Lv. {selection.Minimum}–{selection.Maximum}): patrolling {search.Visited + 1}/{search.Total}…";
        }
        catch (Exception ex) { log.Error(ex, "Farming stopped after an error."); Stop(ex.Message); }
    }

    private uint FateId(IBattleNpc npc) => NativeSnapshot.TryRead<FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject>(npc.Address, out var actor) ? actor.FateId : uint.MaxValue;
    private bool Notorious(IBattleNpc npc) => notoriousBases.Contains(npc.BaseId) || data.GetExcelSheet<BNpcBase>().GetRowOrDefault(npc.BaseId)?.Rank is 2 or 6;
    private bool Eligible(IBattleNpc npc, byte level) => selection != null && area != null && npc.IsTargetable &&
        !npc.IsDead && npc.CurrentHp > 0 && FarmingPolicy.MayPull(Notorious(npc), FateId(npc), selectedFate, config.Farming) &&
        (selectedFate != 0 ? FateId(npc) == selectedFate : selection.Area.NameIds.Contains(npc.NameId)) && selection.Eligible(npc.Level, level) &&
        CaptureRunPolicy.InArea(npc.Position, area.MapPoint, area.SearchRadius, area.TargetFloor);

    private bool ChooseFate(byte level, Vector3 position, long now)
    {
        if (!config.Farming.ParticipateInFates || conditions[ConditionFlag.InCombat]) return false;
        var fate = fates.Where(f => f.State == FateState.Running && f.TimeRemaining > 60 &&
                (f.IconId == 60721 || (f.IconId == 60722 && !config.Farming.IgnoreNotoriousMonsters)) &&
                f.Level >= level + config.Farming.MinimumAbove && f.Level <= level + config.Farming.MaximumAbove &&
                completedFates.GetValueOrDefault(f.FateId) <= now)
            .OrderBy(f => Vector3.DistanceSquared(f.Position, position)).FirstOrDefault();
        if (fate == null) return false;
        StopMovement(); selectedFate = fate.FateId;
        selection = new(new FarmingArea { Name = fate.Name.ToString(), MinimumLevel = fate.Level, MaximumLevel = fate.Level },
            level + config.Farming.MinimumAbove, Math.Min(fate.Level, level + config.Farming.MaximumAbove));
        area = new(client.TerritoryType, fate.Position, 0, 0, $"FATE: {fate.Name}", Math.Clamp(fate.Radius, 10, 200));
        BeginTravel(now); return true;
    }

    private void Choose(int level, long now)
    {
        selection = FarmingPolicy.Select(database.Areas, level, config.Farming, client.TerritoryType, a =>
        {
            if (a.NameIds.Count == 0 || unavailable.GetValueOrDefault(a) > now) return false;
            try { var p = plans.Build(a.Location, config.SpawnAreaRadius); return p.TerritoryId == client.TerritoryType || p.AetheryteId != 0; }
            catch { return false; }
        });
        if (selection == null)
        {
            if (unavailable.Values.Any(t => t > now)) { waitUntil = now + 10000; Status = "Waiting to retry farming areas…"; return; }
            Stop($"No reachable documented farming area for BST level {level} +{config.Farming.MinimumAbove}–{config.Farming.MaximumAbove}. Adjust the range or unlock an aetheryte."); return;
        }
        area = plans.Build(selection.Area.Location, config.SpawnAreaRadius);
        BeginTravel(now);
    }

    private void BeginTravel(long now)
    {
        rotation.SetRunning(false); StopMovement(); ownsTravel = true; Phase = FarmingPhase.Traveling;
        travel.Start(area! with { ArrivalDistance = 1, AllowAreaFallback = true }, travelPlayer(), now);
        Status = $"Traveling to {selection!.Area.Name}, Lv. {selection.Minimum}–{selection.Maximum}: {area!.Name}";
    }
    private void UpdateTravel(TravelPlayer state, long now)
    {
        travel.Update(state, now, config.CancelTravelOnManualMovement);
        if (travel.Active) { Status = travel.Status; return; }
        ownsTravel = false;
        if (!travel.Arrived) { FailArea(now, travel.Status); return; }
        Phase = FarmingPhase.Preparing;
    }
    private void FailArea(long now, string reason)
    {
        EndTarget();
        if (selectedFate != 0) { completedFates[selectedFate] = now + 120000; selectedFate = 0; }
        if (selection != null) unavailable[selection.Area] = now + 120000;
        selection = null; area = null; Phase = FarmingPhase.Choosing; waitUntil = now + 3000;
        Status = reason + " Trying another farming area…";
    }
    private void Pick(IBattleNpc npc, bool defense, long now)
    {
        rotation.SetRunning(false); StopMovement(); watchdog.Reset();
        target = npc.GameObjectId; targetSelected = engaged = false; defending = defense;
        Phase = FarmingPhase.Fighting; targetDeadline = now + 180000;
        nextAction = 0; Status = $"{(defense ? "Defending against" : "Approaching")} {npc.Name}…";
    }
    private unsafe void Fight(IBattleNpc npc, IPlayerCharacter player, long now)
    {
        if (now >= targetDeadline) { Stop("Farming fight timed out; take over or restart farming."); return; }
        if (!engaged && !defending && !Eligible(npc, player.Level))
        { EndTarget(); Phase = FarmingPhase.Preparing; return; }
        if (!npc.IsTargetable) { rotation.SetRunning(false); StopMovement(); return; }
        var inCombat = (npc.StatusFlags & StatusFlags.InCombat) != 0;
        if (!engaged && inCombat && npc.TargetObjectId != player.GameObjectId && !defending && selectedFate == 0)
        { EndTarget(); Phase = FarmingPhase.Preparing; return; }
        if (targetSelected && targets.Target != null && targets.Target.GameObjectId != target)
        { Stop("Farming stopped because you changed the main target."); return; }
        if (conditions[ConditionFlag.Mounted] || conditions[ConditionFlag.InFlight] || conditions[ConditionFlag.MountOrOrnamentTransition])
        {
            rotation.SetRunning(false); StopMovement();
            if (now >= nextAction && !player.IsCasting)
            {
                nextAction = now + 1000;
                var a = ActionManager.Instance();
                if (a != null && a->GetActionStatus(ActionType.GeneralAction, 23) == 0) a->UseAction(ActionType.GeneralAction, 23);
            }
            Status = "Dismounting to fight…"; return;
        }
        if (Vector3.Distance(player.Position, npc.Position) > MathF.Max(2, player.HitboxRadius + npc.HitboxRadius + 1.5f))
        {
            rotation.SetRunning(false); watchdog.Pause(now);
            if (player.IsCasting) { StopMovement(); return; }
            var state = travelPlayer();
            if (state.BlockReason == "Travel stopped in combat." && (engaged || npc.TargetObjectId == player.GameObjectId)) state = state with { BlockReason = null };
            if (!ownsTravel)
            {
                if (now < nextAction) return;
                ownsTravel = true;
                travel.Start(new(client.TerritoryType, npc.Position, 0, 0, "farming target", 10,
                    ExactDestination: true, AllowMount: false, AllowFlight: false, ArrivalDistance: 1.5f), state, now);
            }
            else travel.Update(state, now, config.CancelTravelOnManualMovement);
            if (!travel.Active) { ownsTravel = false; nextAction = now + 1000; }
            return;
        }
        StopMovement();
        if (targets.Target?.GameObjectId != target)
        {
            rotation.SetRunning(false);
            if (now >= nextAction) { targets.Target = npc; targetSelected = true; nextAction = now + 1000; }
            return;
        }
        targetSelected = true;
        rotation.Verify();
        // Explicit single-target farming opener; capture marks are not needed.
        if (!engaged && !player.IsCasting) engaged = TryStrike(npc, player);
        rotation.SetRunning(true);
        if (inCombat && npc.TargetObjectId == player.GameObjectId) engaged = true;
        if (watchdog.Check(target, npc.CurrentHp, now, !player.IsCasting)) rotation.Restart(() => TryStrike(npc, player));
        Status = $"{(defending ? "Defending against" : "Farming")} {npc.Name}, Lv. {npc.Level} with Rotation Solver…";
    }
    private unsafe bool TryStrike(IBattleNpc npc, IPlayerCharacter player)
    {
        if (!Enabled || !compatible() || !strikeValid || targets.Target?.GameObjectId != target || npc.GameObjectId != target ||
            npc.IsDead || !npc.IsTargetable || player.IsDead || player.IsCasting || player.ClassJob.RowId != bst ||
            (!defending && !engaged && !Eligible(npc, player.Level))) return false;
        var a = ActionManager.Instance();
        return a != null && !a->ActionQueued && a->AnimationLock <= 0 && a->GetActionStatus(ActionType.Action, 44879, target) == 0 &&
            a->UseAction(ActionType.Action, 44879, target);
    }
    private void StopMovement() { if (ownsTravel) travel.Stop(); ownsTravel = false; }
    private void EndTarget()
    {
        rotation.SetRunning(false); StopMovement(); watchdog.Reset();
        if (target != 0 && targets.Target?.GameObjectId == target) targets.Target = null;
        target = 0; targetSelected = engaged = defending = false;
    }
    public void Stop(string reason = "Farming stopped.")
    {
        Phase = FarmingPhase.Idle; StopMovement();
        try { rotation.Release(); } catch (Exception ex) { log.Warning(ex, "Could not release farming rotation."); }
        if (target != 0 && targets.Target?.GameObjectId == target) targets.Target = null;
        target = 0; selection = null; area = null; Status = reason;
    }
    public void Dispose() { Stop(); rotation.Dispose(); }
}
