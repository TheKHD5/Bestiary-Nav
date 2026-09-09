using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;

namespace BestiaryNav;

internal sealed class UncapturedMarkers
{
    private readonly Configuration config;
    private readonly CaptureStateReader captureState;
    private readonly Dictionary<(uint Territory, uint NameId), uint> targets;
    private readonly IObjectTable objects;
    private readonly IClientState client;
    private readonly ICondition conditions;
    private readonly IGameGui gui;
    private readonly Dictionary<uint, uint> nearby = [];
    private long nextScan;
    private uint scannedTerritory;
    private ulong captured;
    private bool loaded;
    public string Status { get; private set; } = "Enable markers to scan nearby capture targets.";

    public UncapturedMarkers(Configuration config, CaptureStateReader captureState,
        Dictionary<(uint Territory, uint NameId), uint> targets, IObjectTable objects,
        IClientState client, ICondition conditions, IGameGui gui)
    {
        this.config = config;
        this.captureState = captureState;
        this.targets = targets;
        this.objects = objects;
        this.client = client;
        this.conditions = conditions;
        this.gui = gui;
    }

    private bool CanShow => config.ShowUncapturedMarkers && client.IsLoggedIn && !gui.GameUiHidden &&
        !conditions[ConditionFlag.BetweenAreas] && !conditions[ConditionFlag.BetweenAreas51] &&
        !conditions[ConditionFlag.WatchingCutscene] && !conditions[ConditionFlag.WatchingCutscene78];

    public void Reset()
    {
        nearby.Clear();
        loaded = false;
        nextScan = 0;
    }

    public void Update()
    {
        if (!CanShow || objects.LocalPlayer is not { } player)
        {
            Reset();
            Status = config.ShowUncapturedMarkers ? "Waiting for the game world." : "Uncaptured markers are off.";
            return;
        }
        // Read the small capture bitset every update, so capture/unload clears marks promptly.
        if (!captureState.TryRead(out var current))
        {
            Reset();
            Status = captureState.IsAvailable ? "Waiting for capture records to load." : "Capture markers need a compatible plugin update.";
            return;
        }
        if (!loaded || current != captured || scannedTerritory != client.TerritoryType)
            nextScan = 0;
        captured = current;
        loaded = true;
        if (Environment.TickCount64 < nextScan)
            return;
        nextScan = Environment.TickCount64 + 250;
        scannedTerritory = client.TerritoryType;
        nearby.Clear();
        ulong nearbyBeasts = 0;
        foreach (var obj in objects)
        {
            if (obj is not IBattleNpc npc || npc.BattleNpcKind != BattleNpcSubKind.Combatant || npc.IsDead || !npc.IsTargetable ||
                !targets.TryGetValue((scannedTerritory, npc.NameId), out var number) ||
                !CaptureRules.IsUncaptured(captured, number) ||
                !CaptureRules.IsInRange(Vector3.DistanceSquared(player.Position, npc.Position), config.MarkerRange))
                continue;
            nearby[npc.EntityId] = number;
            // Count each Bestiary entry once, even when several actors match it.
            nearbyBeasts |= 1UL << (int)(number - 1);
        }
        var uniqueCount = BitOperations.PopCount(nearbyBeasts);
        Status = $"{BitOperations.PopCount(captured & ((1UL << 50) - 1))}/50 captured; {uniqueCount} unique nearby uncaptured {(uniqueCount == 1 ? "target" : "targets")}.";
    }

    public void Draw()
    {
        if (!CanShow || !loaded || client.TerritoryType != scannedTerritory || objects.LocalPlayer is not { } player)
            return;
        var draw = ImGui.GetBackgroundDrawList();
        foreach (var (entityId, number) in nearby)
        {
            // Reacquire instead of retaining native object pointers across frames.
            if (objects.SearchByEntityId(entityId) is not IBattleNpc npc || npc.BattleNpcKind != BattleNpcSubKind.Combatant || npc.IsDead || !npc.IsTargetable ||
                !targets.TryGetValue((scannedTerritory, npc.NameId), out var liveNumber) || number != liveNumber ||
                !CaptureRules.IsInRange(Vector3.DistanceSquared(player.Position, npc.Position), config.MarkerRange))
                continue;
            var anchor = npc.Position + new Vector3(0, Math.Clamp(npc.HitboxRadius * 1.3f, 1.5f, 6f), 0);
            if (gui.WorldToScreen(anchor, out var screen))
                DrawLabel(draw, screen, $"Uncaptured #{number}", true, MarkerColor(npc.Level, player.Level));
        }
        DrawEnemyList(draw, player.Level);
    }

    private unsafe void DrawEnemyList(ImDrawListPtr draw, byte playerLevel)
    {
        var addonPtr = gui.GetAddonByName("_EnemyList");
        if (addonPtr.IsNull || !addonPtr.IsReady || !addonPtr.IsVisible)
            return;
        if (!NativeSnapshot.TryRead<EnemyListNumberArray>((nint)EnemyListNumberArray.Instance(), out var data) ||
            data.EnemyCount is < 1 or > 8)
            return;
        for (var i = 0; i < data.EnemyCount; i++)
        {
            var row = data.Enemies[i];
            if (!row.ActiveInList || !nearby.TryGetValue((uint)row.EntityId, out var number) ||
                objects.SearchByEntityId((uint)row.EntityId) is not IBattleNpc npc ||
                npc.BattleNpcKind != BattleNpcSubKind.Combatant || npc.IsDead || !npc.IsTargetable ||
                !targets.TryGetValue((scannedTerritory, npc.NameId), out var liveNumber) || number != liveNumber)
                continue;
            var viewport = ImGui.GetMainViewport();
            if (EnemyListRows.TryGetBounds(addonPtr.Address, i, out var bounds) &&
                EnemyLabelLayout.TryPlace(bounds, ImGui.CalcTextSize("Uncaptured"), viewport.Pos, viewport.Size, out var position))
                DrawLabel(draw, position, "Uncaptured", false, MarkerColor(npc.Level, playerLevel));
        }
    }

    // ImGui packed colors are ABGR. Read live levels each draw so level sync and
    // level-ups update both overlays without rebuilding the nearby target scan.
    private static uint MarkerColor(byte enemyLevel, byte playerLevel) =>
        enemyLevel > playerLevel ? 0xFF5555FFu : 0xFF66E066u;

    private static void DrawLabel(ImDrawListPtr draw, Vector2 anchor, string label, bool aboveModel, uint color)
    {
        if (!float.IsFinite(anchor.X) || !float.IsFinite(anchor.Y))
            return;
        var size = ImGui.CalcTextSize(label);
        var pos = aboveModel ? anchor - new Vector2(size.X / 2, size.Y + 17) : anchor;
        draw.AddRectFilled(pos - new Vector2(5, 3), pos + size + new Vector2(5, 3), 0xDD181818, 4);
        draw.AddText(pos, color, label);
        if (aboveModel)
            draw.AddTriangleFilled(anchor, anchor + new Vector2(-6, -9), anchor + new Vector2(6, -9), color);
    }
}
