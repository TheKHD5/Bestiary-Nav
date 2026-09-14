using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BestiaryNav;

internal enum CaptureRunPhase { Idle, Traveling, Dismounting, Searching, Approaching, Marking, Fighting, WaitingForCapture }

// Only framework-thread values are retained: object IDs/positions, never native
// actor pointers. Rotation is controlled only for one selected entry at a time.
internal sealed class CaptureRun(Configuration config, CaptureStateReader captures,
    IReadOnlyDictionary<(uint Territory, uint NameId), uint> catalog, IObjectTable objects, ITargetManager targets,
    IClientState client, ICondition conditions, IPluginLog log, AutoCapture capture, CaptureRotationIpc rotation,
    TravelController travel, Func<TravelPlayer> travelPlayer, uint bst, Func<bool> gameUiAvailable) : IDisposable
{
    public CaptureRunPhase Phase { get; private set; }
    public bool Active => Phase != CaptureRunPhase.Idle;
    public bool UserInterrupted { get; private set; }
    public string CompactStatus => Phase switch
    {
        CaptureRunPhase.Traveling => "Capture: traveling…",
        CaptureRunPhase.Dismounting => "Capture: dismounting…",
        CaptureRunPhase.Searching => "Capture: searching…",
        CaptureRunPhase.Approaching => "Capture: approaching…",
        CaptureRunPhase.Marking => "Capture: marking…",
        CaptureRunPhase.Fighting => "Capture: fighting…",
        CaptureRunPhase.WaitingForCapture => "Capture: checking result…",
        _ => "Capture: ready",
    };
    public string Status { get; private set; } = "Select a Bestiary entry to start a capture run.";
    private readonly CaptureRetryGate retry = new();
    private readonly HashSet<ulong> defeated = [];
    private readonly SpawnSearchRoute search = new();
    private TravelPlan? area;
    private uint number;
    private ulong targetId;
    private long deadline, runDeadline, nextAction, nextVerify, nextScan;
    private bool ownsTravel;

    public void Start(uint entry, TravelPlan destination, bool travelToArea)
    {
        Stop();
        try
        {
            var player = objects.LocalPlayer;
            if (!config.CaptureRun || !capture.Compatible || player == null || player.ClassJob.RowId != bst ||
                !client.IsLoggedIn || player.IsDead || conditions[ConditionFlag.InCombat])
                throw new InvalidOperationException("Start capture runs as BST, out of combat, with compatible game data.");
            if (!captures.TryRead(out var bits)) throw new InvalidOperationException("Open Master's Bestiary once to load capture records.");
            if (!CaptureRules.IsUncaptured(bits, entry)) throw new InvalidOperationException($"Bestiary #{entry} is already captured.");
            if (!catalog.Any(p => p.Key.Territory == destination.TerritoryId && p.Value == entry))
                throw new InvalidOperationException("This entry has no verified capture target in that territory.");
            rotation.Acquire(bst);
            number = entry; area = destination; targetId = 0; defeated.Clear(); retry.Reset();
            capture.RunActive = true; capture.RunTarget = 0;
            var now = Environment.TickCount64;
            runDeadline = now + 1800000;
            nextVerify = nextScan = nextAction = 0;
            if (travelToArea)
            {
                Phase = CaptureRunPhase.Traveling;
                ownsTravel = true;
                // Get close enough to the floor for Dismount, including flying routes.
                travel.Start(destination with { ArrivalDistance = 1 }, travelPlayer(), now);
                Status = $"Traveling to Bestiary #{number}'s capture area…";
            }
            else SetPhase(CaptureRunPhase.Dismounting, now, 20000, "Preparing to capture nearby duty targets…");
        }
        catch (Exception ex) { Stop(ex.Message); }
    }

    public unsafe void Update()
    {
        if (!Active) return;
        var now = Environment.TickCount64;
        try
        {
            var player = objects.LocalPlayer;
            var loading = conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51];
            var movement = Phase == CaptureRunPhase.Traveling ? travelPlayer() : default;
            var waitingForWorld = Phase == CaptureRunPhase.Traveling && travel.AwaitingWorld && (loading || !movement.WorldReady);
            if (!config.CaptureRun || !config.MapTrackingOnClick || !config.EnableClickNavigation || !capture.Compatible ||
                (!client.IsLoggedIn && !loading) || player?.IsDead == true)
            { Stop("Capture run stopped: disabled, logged out, incapacitated, or no longer BST."); return; }
            if (now >= runDeadline) { Stop("Capture run stopped after 30 minutes. Select the entry again to retry."); return; }
            // During our teleport the player/UI and rotation can vanish briefly.
            // Let travel retain and resume its plan, before inspecting those values.
            if (waitingForWorld) { UpdateTravel(movement, now); return; }
            if (player == null || player.ClassJob.RowId != bst)
            { Stop("Capture run stopped: the player is unavailable or no longer BST."); return; }
            if (conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78] ||
                conditions[ConditionFlag.OccupiedInQuestEvent]) { Stop("Capture run stopped during an event."); return; }
            if (!loading && !gameUiAvailable()) { Stop("Capture run stopped while the game UI is unavailable."); return; }
            if (!loading && config.CancelTravelOnManualMovement && ManualMovementInput.Read())
            { Stop("Capture run canceled because you moved manually.", userInterrupted: true); return; }
            if (!loading && now >= nextVerify) { rotation.Verify(requireRotation: Phase != CaptureRunPhase.Traveling); nextVerify = now + 500; }
            var recordsReady = captures.TryRead(out var bits);
            if (recordsReady && !CaptureRules.IsUncaptured(bits, number))
            { Stop($"Bestiary #{number} captured. Capture run complete."); return; }

            if (Phase == CaptureRunPhase.Traveling)
            {
                UpdateTravel(movement, now);
                return;
            }
            if (loading || client.TerritoryType != area!.TerritoryId)
            { Stop("Capture run stopped after leaving the selected area."); return; }
            // Checking the target and disabling rotation happens each frame, even
            // though idle scans/IPC verification are throttled.
            var npc = targetId == 0 ? null : objects.OfType<IBattleNpc>().FirstOrDefault(n => n.GameObjectId == targetId);
            if (targetId != 0)
            {
                if (npc == null) { Stop("Capture target disappeared. Select the entry again when it is visible."); return; }
                if (npc.IsDead || npc.CurrentHp == 0) { capture.ObserveDefeat(npc.EntityId); OnDefeat(now); return; }
                if (targets.Target?.GameObjectId != targetId)
                {
                    // Target loss does not establish user input: the game or a
                    // dependency may clear it during death processing. End this
                    // attempt; batch mode rechecks records and retries when safe.
                    Stop("Capture attempt stopped because the main target changed or cleared.");
                    return;
                }
                if (!Matches(npc) || !npc.IsTargetable || npc.Level == 0 || npc.Level > player.Level ||
                    !CaptureRunPolicy.InArea(npc.Position, area.MapPoint, area.SearchRadius, area.TargetFloor))
                { Stop("Capture target is no longer eligible or left the selected spawn area."); return; }
            }
            if (now >= deadline) { Stop($"Capture run timed out during {Phase}. Select the entry again to retry."); return; }
            if (!recordsReady)
            {
                rotation.SetRunning(false); capture.RunTarget = 0; StopMovement();
                Status = "Waiting for capture records to finish refreshing…";
                return;
            }
            if (Phase == CaptureRunPhase.Dismounting)
            {
                if (conditions[ConditionFlag.InCombat]) { Stop("Combat started before capture preparation finished."); return; }
                if (!conditions[ConditionFlag.Mounted] && !conditions[ConditionFlag.InFlight] &&
                    !conditions[ConditionFlag.MountOrOrnamentTransition])
                { BeginSearch(now); return; }
                if (now >= nextAction && !player.IsCasting && !conditions[ConditionFlag.MountOrOrnamentTransition])
                {
                    nextAction = now + 1000;
                    var actions = ActionManager.Instance();
                    // GeneralAction sheet 23 = Dismount. The game rejects it if
                    // landing is not possible; never force a teleport or position.
                    if (actions != null && actions->GetActionStatus(ActionType.GeneralAction, 23) == 0)
                        actions->UseAction(ActionType.GeneralAction, 23);
                }
                return;
            }
            if (conditions[ConditionFlag.Mounted] || conditions[ConditionFlag.InFlight])
            { Stop("Capture run stopped because you mounted."); return; }
            if (Phase == CaptureRunPhase.WaitingForCapture)
            {
                ulong? fresh = captures.TryRead(out var records) ? records : null;
                if (!retry.CanRetry(now, fresh, number, conditions[ConditionFlag.InCombat])) return;
                BeginSearch(now);
            }
            if (Phase == CaptureRunPhase.Searching)
            {
                if (conditions[ConditionFlag.InCombat]) { Stop("Unrelated combat started while looking for a target."); return; }
                if (player.IsCasting) { StopMovement(); return; }
                if (now < nextScan || !captures.TryRead(out _)) return;
                nextScan = now + 500;
                // Do not select another beast while any loaded actor has our mark.
                if (objects.OfType<IBattleChara>().Any(n => !n.IsDead && OwnMark(n, player.EntityId))) { StopMovement(); return; }
                var candidates = objects.OfType<IBattleNpc>().Where(Matches).Select(n => new CaptureCandidate(n.GameObjectId,
                    number, n.Position, n.Level, !n.IsDead && n.CurrentHp > 0, n.IsTargetable, (n.StatusFlags & StatusFlags.InCombat) != 0)).ToArray();
                CaptureRunPolicy.RefreshDefeated(candidates, defeated);
                var closest = CaptureRunPolicy.Closest(candidates, number, player.Level, player.Position, area.MapPoint,
                    area.SearchRadius, area.TargetFloor, defeated);
                if (closest == null) { SearchArea(player.Position, now); return; }
                npc = objects.OfType<IBattleNpc>().FirstOrDefault(n => n.GameObjectId == closest.Value.Id);
                if (npc == null) return;
                StopMovement();
                targetId = npc.GameObjectId;
                targets.Target = npc;
                SetPhase(CaptureRunPhase.Approaching, now, 60000, $"Approaching #{number} {npc.Name}…");
            }
            if (npc == null) return;
            var marked = OwnMark(npc, player.EntityId);
            if (Phase == CaptureRunPhase.Approaching && !marked && (npc.StatusFlags & StatusFlags.InCombat) != 0)
            { Stop("The selected beast entered combat before Capture. Select another entry to retry."); return; }
            if (Phase == CaptureRunPhase.Approaching || Phase == CaptureRunPhase.Fighting)
            {
                var reach = MathF.Max(2, npc.HitboxRadius + player.HitboxRadius + 1.5f);
                if (Vector3.Distance(player.Position, npc.Position) > reach)
                {
                    rotation.SetRunning(false); capture.RunTarget = 0;
                    if (player.IsCasting) { StopMovement(); return; }
                    if (!ownsTravel)
                    {
                        ownsTravel = true;
                        travel.Start(new(client.TerritoryType, npc.Position, 0, 0, npc.Name.ToString(), 10,
                            ExactDestination: true, AllowMount: false, AllowFlight: false, ArrivalDistance: 1.5f), ApproachPlayer(npc), now);
                    }
                    else travel.Update(ApproachPlayer(npc), now, config.CancelTravelOnManualMovement);
                    if (!travel.Active)
                    {
                        ownsTravel = false;
                        if (!travel.Arrived) { Stop(travel.Status); return; }
                    }
                    return;
                }
                StopMovement();
                if (Phase == CaptureRunPhase.Approaching)
                    SetPhase(CaptureRunPhase.Marking, now, 30000, "Applying Capture before starting the rotation…");
            }
            if (Phase == CaptureRunPhase.Marking || Phase == CaptureRunPhase.Fighting)
            {
                capture.RunTarget = targetId;
                if (!marked)
                {
                    rotation.SetRunning(false);
                    Status = "Waiting for Interest Captured before using damage combos…";
                    return;
                }
                if (Phase == CaptureRunPhase.Marking)
                    SetPhase(CaptureRunPhase.Fighting, now, 600000, $"Fighting #{number} {npc.Name} with Rotation Solver…");
                rotation.SetRunning(true);
            }
        }
        catch (Exception ex) { log.Error(ex, "Capture run stopped after an error."); Stop(ex.Message); }
    }

    private TravelPlayer ApproachPlayer(IBattleNpc npc)
    {
        var player = travelPlayer();
        // Only the owned, marked capture fight may use short ground approaches
        // during combat. All other travel restrictions remain intact.
        if (player.BlockReason == "Travel stopped in combat." && objects.LocalPlayer is { } local &&
            targets.Target?.GameObjectId == npc.GameObjectId && OwnMark(npc, local.EntityId))
            player = player with { BlockReason = null };
        return player;
    }

    private void UpdateTravel(TravelPlayer player, long now)
    {
        travel.Update(player, now, config.CancelTravelOnManualMovement);
        Status = travel.Status;
        if (travel.Active) return;
        ownsTravel = false;
        if (!travel.Arrived) { Stop(travel.Status); return; }
        SetPhase(CaptureRunPhase.Dismounting, now, 20000, "Landing and dismounting…");
    }

    private void BeginSearch(long now)
    {
        search.Reset(area!.MapPoint, area.SearchRadius);
        SetPhase(CaptureRunPhase.Searching, now, 600000, "Looking for eligible beasts; searching the spawn area on foot…");
    }

    private void SearchArea(Vector3 player, long now)
    {
        rotation.SetRunning(false); capture.RunTarget = 0;
        if (ownsTravel)
        {
            travel.Update(travelPlayer(), now, config.CancelTravelOnManualMovement);
            if (travel.Active) return;
            ownsTravel = false;
            if (!travel.Arrived && !travel.NoRoute) { Stop(travel.Status); return; }
            search.Complete();
        }
        var point = search.Next(player);
        if (point == null)
        {
            Stop("No eligible beast found after searching this spawn circle. Widen Search radius or choose another location.");
            return;
        }
        Status = $"Searching spawn area: point {search.Visited + 1}/{search.Total}…";
        ownsTravel = true;
        travel.Start(new(area!.TerritoryId, point.Value, 0, 0, "spawn-area survey", 10, area.TargetFloor,
            AllowMount: false, AllowFlight: false, ArrivalDistance: 3,
            SearchBoundary: new(area.MapPoint, area.SearchRadius, area.TargetFloor)), travelPlayer(), now);
        if (!travel.Active && !travel.NoRoute) { ownsTravel = false; Stop(travel.Status); }
    }

    private bool Matches(IBattleNpc n) => n.BattleNpcKind == BattleNpcSubKind.Combatant &&
        catalog.TryGetValue((client.TerritoryType, n.NameId), out var entry) && entry == number;
    private static bool OwnMark(IBattleChara n, uint source) => n.StatusList.Any(s => CaptureMarkTracker.IsOwnMark(s.StatusId, s.SourceId, source));

    private void OnDefeat(long now)
    {
        rotation.SetRunning(false);
        StopMovement();
        defeated.Add(targetId);
        if (targets.Target?.GameObjectId == targetId) targets.Target = null;
        targetId = 0; capture.RunTarget = 0;
        retry.Defeated(now);
        SetPhase(CaptureRunPhase.WaitingForCapture, now, 60000, "Waiting at least 3 seconds for the capture result…");
    }

    private void SetPhase(CaptureRunPhase phase, long now, long timeout, string status)
    { Phase = phase; deadline = now + timeout; Status = status; }

    private void StopMovement()
    {
        if (ownsTravel) travel.Stop();
        ownsTravel = false;
    }

    public void Stop(string reason = "Capture run stopped.", bool userInterrupted = false)
    {
        UserInterrupted = userInterrupted;
        var wasActive = Active;
        Phase = CaptureRunPhase.Idle;
        capture.RunActive = false; capture.RunTarget = 0;
        StopMovement();
        try { rotation.Release(); }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not stop capture rotation control.");
            reason += " Rotation Solver could not be stopped; switch it off manually.";
        }
        if (targetId != 0 && targets.Target?.GameObjectId == targetId) targets.Target = null;
        targetId = 0; area = null;
        Status = reason;
        if (wasActive) log.Information(reason);
    }

    public void Dispose() { Stop(); rotation.Dispose(); }
}
