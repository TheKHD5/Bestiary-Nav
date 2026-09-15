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
    public string LastRecovery { get; private set; } = "No combat recovery needed.";
    public string RotationStatus => rotation.CombatStatus;
    public string RespawnStatus => respawn.Status;
    public ulong CompanionId => companionId;
    public string LastStopReason { get; private set; } = "No previous Levelling stop.";
    public string TargetRange => selection == null ? $"BST +{config.Farming.MinimumAbove}–{config.Farming.MaximumAbove}; no area selected" :
        $"Lv. {selection.Minimum}–{selection.Maximum}; {area?.Name}; catalog levels {selection.Area.MinimumLevel}–{selection.Area.MaximumLevel}";
    public IReadOnlyList<FarmingFood> FoodChoices() => supplies.FoodChoices();
    public IReadOnlyList<FarmingArea> GroupChoices() => FarmingPolicy.Choices(database.Areas,
        objects.LocalPlayer?.ClassJob.RowId == bst ? objects.LocalPlayer.Level : 0, config.Farming).ToArray();
    public string SelectedGroupLabel => config.Farming.SelectedGroups.Count switch
    {
        0 => "Automatic — all eligible enemies",
        1 => SavedGroupLabel(config.Farming.SelectedGroups[0]),
        _ => $"{config.Farming.SelectedGroups.Count} groups selected",
    };
    public string SelectedGroupDetails => config.Farming.SelectedGroups.Count == 0 ? SelectedGroupLabel :
        string.Join("; ", config.Farming.SelectedGroups.Select(SavedGroupLabel));
    public string SavedGroupLabel(string key) => database.Areas.FirstOrDefault(a => a.Key == key)?.Label ?? "Saved group unavailable";
    public string LastSearchResult { get; private set; } = "No completed patrol yet.";
    private readonly Dictionary<uint, string> enemyNames = BuildEnemyNames(data, database);
    private static Dictionary<uint, string> BuildEnemyNames(IDataManager data, FarmingDatabase database)
    {
        var names = database.Areas.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return data.GetExcelSheet<BNpcName>(ClientLanguage.English)
            .Where(n => names.Contains(n.Singular.ExtractText())).ToDictionary(n => n.RowId, n => n.Singular.ExtractText());
    }
    private FarmingSelection? selection;
    private TravelPlan? area;
    private readonly SpawnSearchRoute search = new();
    private readonly CaptureCombatWatchdog watchdog = new();
    private readonly Dictionary<FarmingArea, long> unavailable = [];
    private readonly FarmingEmptyAreas emptyRanges = new();
    private ulong target;
    private ulong companionId;
    private bool ownsTravel, targetSelected, engaged, defending;
    private long nextScan, nextVerify, nextAction, waitUntil, targetDeadline, areaDeadline;
    private long lastUpdate;
    private int reached;
    private ushort selectedFate;
    private readonly Dictionary<ushort, long> completedFates = [];
    private readonly HashSet<uint> notoriousBases = data.GetExcelSheet<NotoriousMonster>().Where(n => n.BNpcBase.RowId != 0).Select(n => n.BNpcBase.RowId).ToHashSet();
    private readonly BstBasicCombo recoveryCombo = new(data);

    public void Start()
    {
        Stop();
        try
        {
            config.Farming.Normalize();
            if (!compatible()) throw new InvalidOperationException("Levelling needs compatible game data.");
            if (!client.IsLoggedIn || objects.LocalPlayer is not { } p)
                throw new InvalidOperationException("Log in and wait for the player to load before starting Levelling.");
            if (p.ClassJob.RowId != bst) throw new InvalidOperationException("Equip BST before starting Levelling.");
            var recovering = FarmingRecoveryPolicy.CanStartRecovery(p.IsDead, p.CurrentHp, config.Farming.AutoRespawn);
            if (FarmingRecoveryPolicy.Incapacitated(p.IsDead, p.CurrentHp) && !recovering)
                throw new InvalidOperationException("Enable Auto respawn and resume, or return manually before starting Levelling.");
            if (!recovering && conditions[ConditionFlag.InCombat])
                throw new InvalidOperationException("Leave combat before starting Levelling.");
            if (!recovering && !uiAvailable()) throw new InvalidOperationException("Show the game UI before starting Levelling.");
            if (!recovering && config.Farming.SelectedGroups.Count > 0)
            {
                var groups = database.Areas.Where(a => FarmingPolicy.MatchesGroup(a, config.Farming) && FarmingPolicy.InRange(a, p.Level, config.Farming)).ToArray();
                if (groups.Length == 0)
                    throw new InvalidOperationException("No selected monster group overlaps the current level range. Select more groups or Automatic.");
                if (!groups.Any(g => enemyNames.Values.Contains(g.Name, StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The selected monsters' game identities could not be verified. Choose other groups or Automatic.");
            }
            LastRecovery = "No combat recovery needed.";
            if (!recovering && FarmingPolicy.ReachedGoal(p.Level, config.Farming)) { Stop("Target BST level already reached."); return; }
            unavailable.Clear(); emptyRanges.Clear(); selection = null; area = null;
            LastSearchResult = "No completed patrol yet.";
            selectedFate = 0; completedFates.Clear();
            nextScan = nextVerify = nextAction = waitUntil = 0;
            lastUpdate = Environment.TickCount64;
            respawn.Reset();
            if (recovering) { BeginRecovery(lastUpdate); return; }
            rotation.Acquire(bst);
            Phase = FarmingPhase.Choosing; Status = "Choosing a farming area…";
        }
        catch (Exception ex) { Stop(ex.Message); }
    }

    public unsafe void Update()
    {
        if (!Enabled) return;
        var now = Environment.TickCount64;
        var elapsed = Math.Max(0, now - lastUpdate);
        lastUpdate = now;
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
            // Death precedes ordinary rotation/event guards. The game's death
            // prompt and RSR's automatic shutdown are expected recovery states.
            if (FarmingRecoveryPolicy.Incapacitated(player.IsDead, player.CurrentHp))
            {
                if (Phase != FarmingPhase.Recovering) BeginRecovery(now);
                if (state.Loading || !uiAvailable() || conditions[ConditionFlag.BoundByDuty] ||
                    conditions[ConditionFlag.BoundByDuty56] || conditions[ConditionFlag.BoundByDuty95])
                { Status = "Waiting for the overworld Return prompt…"; return; }
                if (config.Farming.AutoRespawn)
                {
                    try { respawn.TryReturn(now); Status = respawn.Status; }
                    catch (Exception ex) { Status = $"Revival is waiting: {ex.Message}"; }
                }
                else Status = "Waiting for manual revival; Levelling remains enabled.";
                return;
            }
            if (Phase == FarmingPhase.Recovering)
            {
                if (state.Loading || !state.WorldReady || !uiAvailable() ||
                    conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78] ||
                    conditions[ConditionFlag.OccupiedInQuestEvent] || conditions[ConditionFlag.BoundByDuty] ||
                    conditions[ConditionFlag.BoundByDuty56] || conditions[ConditionFlag.BoundByDuty95] ||
                    PullHealthPolicy.ShouldWait(config.WaitForFullHpBeforeEngaging, player.CurrentHp, player.MaxHp, false, 70) ||
                    conditions[ConditionFlag.InCombat])
                { Status = "Recovering after revival…"; return; }
                if (now < waitUntil) return;
                try { rotation.Acquire(bst); }
                catch (Exception ex) { waitUntil = now + 5000; Status = $"Revived; waiting to resume: {ex.Message}"; return; }
                Phase = FarmingPhase.Choosing; waitUntil = now + 3000;
                Status = "Revived; choosing a Levelling area…"; return;
            }
            if (FarmingPolicy.ReachedGoal(player.Level, config.Farming)) { Stop($"Target BST level {config.Farming.TargetLevel} reached. Farming complete."); return; }
            if (state.Loading || !uiAvailable() || conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78] ||
                conditions[ConditionFlag.OccupiedInQuestEvent] || conditions[ConditionFlag.BoundByDuty] ||
                conditions[ConditionFlag.BoundByDuty56] || conditions[ConditionFlag.BoundByDuty95])
            { Stop("Farming stopped during an event, duty, or unexpected area transition."); return; }
            if (now >= nextVerify) { rotation.Verify(Phase != FarmingPhase.Traveling); nextVerify = now + 500; }
            var combat = conditions[ConditionFlag.InCombat];
            var actors = objects.OfType<IBattleNpc>().Where(n => n.BattleNpcKind == BattleNpcSubKind.Combatant).ToArray();
            companionId = supplies.CompanionId;
            if (selection != null && (selection.Minimum != player.Level + config.Farming.MinimumAbove || selection.Maximum != player.Level + config.Farming.MaximumAbove))
            {
                selection = selection.AtLevel(player.Level, config.Farming);
                if (area != null)
                {
                    search.Reset(area.MapPoint, area.SearchRadius); reached = 0;
                    areaDeadline = now + 300000;
                }
            }
            var npc = actors.FirstOrDefault(n => n.GameObjectId == target);
            if (target != 0 && (npc == null || npc.IsDead || npc.CurrentHp == 0))
            {
                EndTarget(); waitUntil = now + 3000; Phase = FarmingPhase.Preparing;
            }
            if (target != 0 && targetSelected && targets.Target != null && targets.Target.GameObjectId != target)
            { Stop("Farming stopped because you changed the main target."); return; }
            // Companion aggro can precede the player's combat flag. Interrupt
            // a new pull, patrol or HP wait, but finish an already engaged fight.
            if (target == 0 || (!engaged && !defending))
            {
                var id = CaptureDefensePolicy.Select(actors.Select(n => new CaptureAggressor(n.GameObjectId, n.TargetObjectId,
                    n.Position, !n.IsDead && n.CurrentHp > 0, n.IsTargetable, (n.StatusFlags & StatusFlags.InCombat) != 0)),
                    player.GameObjectId, player.Position, preferred: target, companion: companionId);
                var attacker = actors.FirstOrDefault(n => n.GameObjectId == id);
                if (attacker != null)
                {
                    if (target != 0) EndTarget();
                    npc = attacker;
                    Pick(npc, true, now);
                }
                else if (target == 0 && combat)
                { rotation.SetRunning(false); StopMovement(); Status = "Waiting for an attacker to become visible…"; return; }
            }
            if (target != 0 && npc != null)
            {
                if (PullHealthPolicy.ShouldWait(config.WaitForFullHpBeforeEngaging, player.CurrentHp, player.MaxHp,
                    engaged || defending || combat))
                {
                    rotation.SetRunning(false); StopMovement(); watchdog.Pause(now);
                    targetDeadline += elapsed; areaDeadline += elapsed;
                    Status = $"Waiting for full HP before the next pull ({player.CurrentHp:N0}/{player.MaxHp:N0})…";
                    return;
                }
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
            // Look at all eligible species before checking whether the area is
            // outleveled, including on the frame after a level-up.
            if (selection != null && area != null && client.TerritoryType == area.TerritoryId)
                foreach (var nearby in actors.Where(n => Eligible(n, player.Level)))
                    selection = selection.Observe(nearby.Level, player.Level);
            if (selection != null && !selection.SupportsRange)
            {
                if (selectedFate != 0) { completedFates[selectedFate] = now + 120000; selectedFate = 0; }
                rotation.SetRunning(false); StopMovement(); selection = null; area = null; Phase = FarmingPhase.Choosing;
                Status = $"This area does not support Lv. {player.Level + config.Farming.MinimumAbove}–{player.Level + config.Farming.MaximumAbove}; choosing another area…";
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
            if (PullHealthPolicy.ShouldWait(config.WaitForFullHpBeforeEngaging, player.CurrentHp, player.MaxHp, combat, 70))
            {
                rotation.SetRunning(false); StopMovement(); watchdog.Pause(now); areaDeadline += elapsed;
                Status = config.WaitForFullHpBeforeEngaging ?
                    $"Waiting for full HP before the next pull ({player.CurrentHp:N0}/{player.MaxHp:N0})…" : "Recovering HP before the next pull…";
                return;
            }
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
            if (now >= areaDeadline) { RejectEmptyRange(now, "No eligible targets found after five minutes."); return; }
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
                RejectEmptyRange(now, $"Full patrol found no Lv. {selection.Minimum}–{selection.Maximum} enemies."); return;
            }
            ownsTravel = true;
            travel.Start(new(area.TerritoryId, point.Value, 0, 0, "farming patrol", 10, area.TargetFloor,
                AllowMount: false, AllowFlight: false, ArrivalDistance: 3,
                SearchBoundary: new(area.MapPoint, area.SearchRadius, area.TargetFloor)), state, now);
            Status = $"Levelling (Lv. {selection.Minimum}–{selection.Maximum}): patrolling {search.Visited + 1}/{search.Total} for eligible enemies…";
        }
        catch (Exception ex) { log.Error(ex, "Farming stopped after an error."); Stop(ex.Message); }
    }

    private uint FateId(IBattleNpc npc) => NativeSnapshot.TryRead<FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject>(npc.Address, out var actor) ? actor.FateId : uint.MaxValue;
    private bool Notorious(IBattleNpc npc) => notoriousBases.Contains(npc.BaseId) || data.GetExcelSheet<BNpcBase>().GetRowOrDefault(npc.BaseId)?.Rank is 2 or 6;
    private bool Eligible(IBattleNpc npc, byte level) => selection != null && area != null && npc.IsTargetable &&
        !npc.IsDead && npc.CurrentHp > 0 && FarmingPolicy.MayPull(Notorious(npc), FateId(npc), selectedFate, config.Farming) &&
        (selectedFate == 0 || FateId(npc) == selectedFate) && selection.Eligible(npc.Level, level) &&
        FarmingPolicy.MatchesEnemy(selection.Area, config.Farming, enemyNames.GetValueOrDefault(npc.NameId), database.Areas) &&
        CaptureRunPolicy.InArea(npc.Position, area.MapPoint, area.SearchRadius, area.TargetFloor);

    private bool ChooseFate(byte level, Vector3 position, long now)
    {
        if (!config.Farming.ParticipateInFates || config.Farming.SelectedGroups.Count > 0 || conditions[ConditionFlag.InCombat]) return false;
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
            if (unavailable.GetValueOrDefault(a) > now || emptyRanges.Contains(a, level, config.Farming, now)) return false;
            if (config.Farming.SelectedGroups.Count > 0 && !enemyNames.Values.Contains(a.Name, StringComparer.OrdinalIgnoreCase)) return false;
            try { var p = plans.Build(a.Location, config.SpawnAreaRadius); return p.TerritoryId == client.TerritoryType || p.AetheryteId != 0; }
            catch { return false; }
        });
        if (selection == null)
        {
            var retry = database.Areas.Where(a => FarmingPolicy.MatchesGroup(a, config.Farming) && FarmingPolicy.InRange(a, level, config.Farming))
                .Select(a => Math.Max(unavailable.GetValueOrDefault(a), emptyRanges.RetryAt(a, level, config.Farming)))
                .Where(t => t > now).DefaultIfEmpty(0).Min();
            if (retry > now)
            {
                waitUntil = Math.Min(retry, now + 1000);
                Status = $"Levelling remains on: retrying eligible groups in {(retry - now + 999) / 1000}s. {LastSearchResult}";
                return;
            }
            if (config.Farming.SelectedGroups.Count > 0)
            { Stop("No selected group supports the current range with a verified identity and accessible destination. Select more groups or Automatic."); return; }
            Stop($"No documented accessible area supports Lv. {level + config.Farming.MinimumAbove}–{level + config.Farming.MaximumAbove}. Adjust the range."); return;
        }
        area = plans.Build(selection.Area.Location, config.SpawnAreaRadius);
        BeginTravel(now);
    }

    private void BeginTravel(long now)
    {
        rotation.SetRunning(false); StopMovement();
        // A fresh sweep can start where we are. Do not route back to a possibly
        // unmapped center when we are already inside the correct spawn circle.
        if (objects.LocalPlayer is { } player && client.TerritoryType == area!.TerritoryId &&
            CaptureRunPolicy.InArea(player.Position, area.MapPoint, area.SearchRadius, area.TargetFloor))
        {
            Phase = FarmingPhase.Preparing;
            Status = "Inside the spawn area; preparing a new patrol…";
            return;
        }
        ownsTravel = true; Phase = FarmingPhase.Traveling;
        travel.Start(area! with { ArrivalDistance = 1, AllowAreaFallback = true }, travelPlayer(), now);
        Status = $"Traveling to a Lv. {selection!.Minimum}–{selection.Maximum} levelling area: {area!.Name}";
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
        LastSearchResult = $"{area?.Name}: {reason}";
        EndTarget();
        if (selectedFate != 0) { completedFates[selectedFate] = now + 120000; selectedFate = 0; }
        if (selection != null) unavailable[selection.Area] = now + 120000;
        selection = null; area = null; Phase = FarmingPhase.Choosing; waitUntil = now + 3000;
        Status = reason + " Trying another eligible group, or retrying this route in two minutes…";
    }
    private void RejectEmptyRange(long now, string reason)
    {
        LastSearchResult = $"{area?.Name}: {reason}";
        if (selection != null && selectedFate == 0)
        {
            emptyRanges.Reject(selection, now);
            unavailable.Remove(selection.Area);
        }
        EndTarget();
        if (selectedFate != 0) completedFates[selectedFate] = now + 120000;
        selectedFate = 0; selection = null; area = null;
        Phase = FarmingPhase.Choosing; waitUntil = now + 3000;
        Status = reason + " Trying another eligible group, or restarting this patrol in 60s…";
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
        if (defending && !engaged && !AttackingUs(npc, player.GameObjectId))
        { EndTarget(); Phase = FarmingPhase.Preparing; return; }
        if (!engaged && !defending && !Eligible(npc, player.Level))
        { EndTarget(); Phase = FarmingPhase.Preparing; return; }
        if (!npc.IsTargetable) { rotation.SetRunning(false); StopMovement(); return; }
        var inCombat = (npc.StatusFlags & StatusFlags.InCombat) != 0;
        if (!engaged && inCombat && !AttackingUs(npc, player.GameObjectId) && !defending && selectedFate == 0)
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
            if (state.BlockReason == "Travel stopped in combat." && (engaged || AttackingUs(npc, player.GameObjectId))) state = state with { BlockReason = null };
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
        if (inCombat && AttackingUs(npc, player.GameObjectId)) engaged = true;
        if (watchdog.Check(target, npc.CurrentHp, now, !player.IsCasting, rotation.LastSkillStamp))
        {
            rotation.Restart(() => TryStrike(npc, player));
            LastRecovery = $"{watchdog.Reason} on {npc.Name}; refreshed Rotation Solver (attempt {watchdog.Recoveries}). {recoveryCombo.Status}";
            log.Information(LastRecovery);
        }
        Status = $"{(defending ? "Defending against" : "Farming")} {npc.Name}, Lv. {npc.Level} with Rotation Solver…";
    }
    private bool AttackingUs(IBattleNpc npc, ulong player) => CaptureDefensePolicy.AttackingPlayerOrCompanion(
        new(npc.GameObjectId, npc.TargetObjectId, npc.Position, !npc.IsDead && npc.CurrentHp > 0,
            npc.IsTargetable, (npc.StatusFlags & StatusFlags.InCombat) != 0), player, companionId);

    private unsafe bool TryStrike(IBattleNpc npc, IPlayerCharacter player)
    {
        if (!Enabled || !compatible() || targets.Target?.GameObjectId != target || npc.GameObjectId != target ||
            npc.IsDead || !npc.IsTargetable || player.IsDead || player.IsCasting || player.ClassJob.RowId != bst ||
            (!defending && !engaged && !Eligible(npc, player.Level)) ||
            (defending && !engaged && !AttackingUs(npc, player.GameObjectId))) return false;
        return recoveryCombo.TryUse(target, player.Level);
    }
    private void StopMovement() { if (ownsTravel) travel.Stop(); ownsTravel = false; }
    private void BeginRecovery(long now)
    {
        Phase = FarmingPhase.Recovering;
        if (selection != null) unavailable[selection.Area] = now + 120000;
        selection = null; area = null; selectedFate = 0; waitUntil = 0;
        watchdog.Reset(); respawn.Reset();
        FarmingRecoveryPolicy.Cleanup(StopMovement, rotation.Release, () =>
        {
            if (target != 0 && targets.Target?.GameObjectId == target) targets.Target = null;
        }, ex => log.Warning(ex, "Levelling death cleanup failed; revival will still be attempted."));
        target = 0; targetSelected = engaged = defending = ownsTravel = false;
        Status = "Incapacitated; waiting to return and resume Levelling…";
    }
    private void EndTarget()
    {
        rotation.SetRunning(false); StopMovement(); watchdog.Reset();
        if (target != 0 && targets.Target?.GameObjectId == target) targets.Target = null;
        target = 0; targetSelected = engaged = defending = false;
    }
    public void Stop(string reason = "Farming stopped.")
    {
        if (Enabled) LastStopReason = reason;
        Phase = FarmingPhase.Idle; StopMovement();
        try { rotation.Release(); } catch (Exception ex) { log.Warning(ex, "Could not release farming rotation."); }
        if (target != 0 && targets.Target?.GameObjectId == target) targets.Target = null;
        target = companionId = 0; selection = null; area = null; Status = reason;
    }
    public void Dispose() { Stop(); rotation.Dispose(); }
}
