using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;

namespace BestiaryNav;

internal sealed class UncapturedMarkers
{
    private readonly Configuration config;
    private readonly CaptureStateReader captureState;
    private readonly Dictionary<(uint Territory, uint NameId), uint> targets;
    private readonly IReadOnlyDictionary<uint, MonsterEntry> catalog;
    private readonly IObjectTable objects;
    private readonly IClientState client;
    private readonly ICondition conditions;
    private readonly IGameGui gui;
    private readonly ModelLabelLayout.Project project;
    private readonly uint beastmasterJobId;
    private readonly Dictionary<uint, uint> nearby = [];
    private long nextScan;
    private uint scannedTerritory;
    private ulong captured;
    private bool loaded;
    private uint previewEntityId;
    private long previewUntil;
    private long lastDrawAt;
    private int updateThread;
    private int drawThread;
    private string lastDrawError = "none";
    private OverlayFrame overlay = OverlayFrame.Empty;
    private int resetRequested;
    private readonly record struct ModelFrame(uint EntityId, uint Number, Vector3 Position, float Radius, Vector4 Color);
    private readonly record struct RowFrame(EnemyRowBounds Bounds, uint Number, Vector4 Color);
    private sealed record OverlayFrame(long Timestamp, ModelFrame[] Models, RowFrame[] Rows, ModelFrame? Preview, long PreviewUntil)
    {
        public static readonly OverlayFrame Empty = new(0, [], [], null, 0);
    }
    public string Status { get; private set; } = "Enable markers to scan nearby capture targets.";

    public UncapturedMarkers(Configuration config, CaptureStateReader captureState,
        Dictionary<(uint Territory, uint NameId), uint> targets, IReadOnlyDictionary<uint, MonsterEntry> catalog, IObjectTable objects,
        IClientState client, ICondition conditions, IGameGui gui, uint beastmasterJobId)
    {
        this.config = config;
        this.captureState = captureState;
        this.targets = targets;
        this.catalog = catalog;
        this.objects = objects;
        this.client = client;
        this.conditions = conditions;
        this.gui = gui;
        project = gui.WorldToScreen;
        this.beastmasterJobId = beastmasterJobId;
    }

    private bool CanShow => config.ShowUncapturedMarkers && client.IsLoggedIn && !gui.GameUiHidden &&
        !conditions[ConditionFlag.BetweenAreas] && !conditions[ConditionFlag.BetweenAreas51] &&
        !conditions[ConditionFlag.WatchingCutscene] && !conditions[ConditionFlag.WatchingCutscene78];

    public void Reset()
    {
        // The settings window can run on a different thread from the live scan.
        Interlocked.Exchange(ref resetRequested, 1);
        Volatile.Write(ref overlay, OverlayFrame.Empty);
    }

    private void ResetScan()
    {
        nearby.Clear();
        loaded = false;
        nextScan = 0;
        previewUntil = 0;
        Volatile.Write(ref overlay, OverlayFrame.Empty);
    }

    public IEnumerable<string> Diagnose(IGameObject? selected, bool preview = true)
    {
        yield return $"Markers: enabled={config.ShowUncapturedMarkers}, canShow={CanShow}, territory={client.TerritoryType}, range={config.MarkerRange:0.#}; draw={(lastDrawAt == 0 ? "not called" : $"{Environment.TickCount64 - lastDrawAt}ms ago")}.";
        yield return $"Drawing: updateThread={updateThread}, drawThread={drawThread}, lastError={lastDrawError}.";
        var frame = Volatile.Read(ref overlay);
        yield return $"Overlay: age={(frame.Timestamp == 0 ? -1 : Environment.TickCount64 - frame.Timestamp)}ms, models={frame.Models.Length}, previewReady={frame.Preview.HasValue}.";
        var ready = captureState.TryRead(out var bits);
        yield return !captureState.IsAvailable ? captureState.UnavailableReason : ready
            ? $"Capture records ready: {BitOperations.PopCount(bits)} captured." : "Capture records not ready. Open Master's Bestiary once.";
        if (selected is not IBattleNpc npc || objects.LocalPlayer is not { } player)
        {
            yield return "Select the beast and run /bnav diagnose again.";
            yield break;
        }
        yield return $"Label job filter: BST-only={config.LabelsOnlyOnBeastmaster}, currentJob={player.ClassJob.RowId}, BST={beastmasterJobId}, allowed={IsLabelJobAllowed(player)}.";
        var matched = targets.TryGetValue((client.TerritoryType, npc.NameId), out var number);
        yield return $"Target: {npc.Name}; nameId={npc.NameId}, baseId={npc.BaseId}, kind={npc.BattleNpcKind}, dead={npc.IsDead}, targetable={npc.IsTargetable}, distance={Vector3.Distance(player.Position, npc.Position):0.#}.";
        yield return matched ? $"Matched #{number}: {(ready ? (CaptureRules.IsUncaptured(bits, number) ? "uncaptured" : "captured") : "unknown capture state")}; in scan={nearby.ContainsKey(npc.EntityId)}."
            : "No enemy/territory match in the catalog.";
        // Clipboard reports are refreshed on the framework thread without any
        // rendering calls. Explicit diagnose retains the original projection probe.
        if (preview)
        {
            var anchor = npc.Position + new Vector3(0, Math.Clamp(npc.HitboxRadius * 1.3f, 1.5f, 6f), 0);
            var onScreen = gui.WorldToScreen(anchor, out var screen);
            yield return $"Projection: onScreen={onScreen}, x={screen.X:0.#}, y={screen.Y:0.#}.";
        }
        if (!preview || !matched || !captureState.IsAvailable || !CanShow || !IsLabelJobAllowed(player) || npc.IsDead || !npc.IsTargetable ||
            npc.BattleNpcKind != BattleNpcSubKind.Combatant)
            yield break;
        previewEntityId = npc.EntityId;
        previewUntil = Environment.TickCount64 + 30000;
        yield return $"Showing Preview #{number} for 30 seconds. Capture records and nearby count are unchanged.";
    }

    public void Update()
    {
        if (Interlocked.Exchange(ref resetRequested, 0) != 0) ResetScan();
        updateThread = Environment.CurrentManagedThreadId;
        if (!CanShow || objects.LocalPlayer is not { } player)
        {
            ResetScan();
            Status = config.ShowUncapturedMarkers ? "Waiting for the game world." : "Uncaptured markers are off.";
            return;
        }
        // Read the small capture bitset every update, so capture/unload clears marks promptly.
        if (!captureState.TryRead(out var current))
        {
            ResetScan();
            Status = captureState.IsAvailable ? "Open Master's Bestiary once to load capture records." : captureState.UnavailableReason;
            return;
        }
        if (!loaded || current != captured || scannedTerritory != client.TerritoryType)
            nextScan = 0;
        captured = current;
        loaded = true;
        if (Environment.TickCount64 < nextScan)
        {
            BuildOverlay(player);
            return;
        }
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
        BuildOverlay(player);
    }

    // All object-table and native addon lookups run on Framework.Update. Publish
    // immutable values so rendering never keeps or reacquires game object wrappers.
    private void BuildOverlay(IPlayerCharacter player)
    {
        // Read the equipped job only on the framework thread. Switching away
        // clears world, enemy-list and diagnostic labels together; counts remain.
        if (!IsLabelJobAllowed(player))
        {
            previewUntil = 0;
            Volatile.Write(ref overlay, OverlayFrame.Empty);
            return;
        }
        var models = new Dictionary<uint, ModelFrame>();
        foreach (var (entityId, number) in nearby)
        {
            if (TryBuildModel(entityId, player, out var model) && model.Number == number)
                models[entityId] = model;
        }
        ModelFrame? preview = Environment.TickCount64 < previewUntil && TryBuildModel(previewEntityId, player, out var previewModel)
            ? previewModel : null;
        Volatile.Write(ref overlay, new OverlayFrame(Environment.TickCount64, [.. models.Values],
            BuildEnemyRows(models), preview, previewUntil));
    }

    private bool IsLabelJobAllowed(IPlayerCharacter player) => !config.LabelsOnlyOnBeastmaster ||
        beastmasterJobId != 0 && player.ClassJob.RowId == beastmasterJobId;

    private bool TryBuildModel(uint entityId, IPlayerCharacter player, out ModelFrame model)
    {
        model = default;
        if (objects.SearchByEntityId(entityId) is not IBattleNpc npc || npc.BattleNpcKind != BattleNpcSubKind.Combatant ||
            npc.IsDead || !npc.IsTargetable || !targets.TryGetValue((scannedTerritory, npc.NameId), out var number) ||
            !CaptureRules.IsInRange(Vector3.DistanceSquared(player.Position, npc.Position), config.MarkerRange))
            return false;
        model = new(entityId, number, npc.Position, npc.HitboxRadius, MarkerColor(npc.Level, player.Level));
        return true;
    }

    private unsafe RowFrame[] BuildEnemyRows(Dictionary<uint, ModelFrame> models)
    {
        if (!config.ShowEnemyListLabels || models.Count == 0) return [];
        var addonPtr = gui.GetAddonByName("_EnemyList");
        if (addonPtr.IsNull || !addonPtr.IsReady || !addonPtr.IsVisible)
            return [];
        if (!NativeSnapshot.TryRead<EnemyListNumberArray>((nint)EnemyListNumberArray.Instance(), out var data) ||
            data.EnemyCount is < 1 or > 8)
            return [];
        var rows = new List<RowFrame>(data.EnemyCount);
        for (var i = 0; i < data.EnemyCount; i++)
        {
            var row = data.Enemies[i];
            if (row.ActiveInList && models.TryGetValue((uint)row.EntityId, out var model) &&
                EnemyListRows.TryGetBounds(addonPtr.Address, i, out var bounds))
                rows.Add(new(bounds, model.Number, model.Color));
        }
        return [.. rows];
    }

    public void Draw()
    {
        lastDrawAt = Environment.TickCount64;
        drawThread = Environment.CurrentManagedThreadId;
        try
        {
            var frame = Volatile.Read(ref overlay);
            if (frame.Timestamp == 0 || lastDrawAt - frame.Timestamp > 1000 || gui.GameUiHidden)
                return;
            var viewport = ImGui.GetMainViewport();
            var draw = ImGui.GetForegroundDrawList(viewport);
            if (config.ShowModelLabels) foreach (var model in frame.Models)
                if (ModelLabelLayout.TryProject(model.Position, model.Radius, project, out var screen))
                    DrawLabel(draw, screen, Label(model.Number), true, model.Color);
            if (config.ShowModelLabels && lastDrawAt < frame.PreviewUntil && frame.Preview is { } preview &&
                ModelLabelLayout.TryProject(preview.Position, preview.Radius, project, out var previewScreen))
                DrawLabel(draw, previewScreen, $"Preview #{preview.Number}", true, preview.Color);
            if (config.ShowEnemyListLabels) foreach (var row in frame.Rows)
            {
                var label = Label(row.Number);
                if (EnemyLabelLayout.TryPlace(row.Bounds, ImGui.CalcTextSize(label) * config.LabelScale, viewport.Pos, viewport.Size, out var position))
                    DrawLabel(draw, position, label, false, row.Color);
            }
        }
        catch (Exception ex)
        {
            var error = $"{ex.GetType().Name}: {ex.Message}";
            if (error != lastDrawError)
            {
                lastDrawError = error;
                Plugin.Log.Error(ex, "Marker drawing failed; use /bnav diagnose for details.");
            }
        }
    }

    // Live levels refresh both overlays on every framework update.
    private Vector4 MarkerColor(byte enemyLevel, byte playerLevel) =>
        enemyLevel > playerLevel ? config.AboveLevelColor : config.EligibleColor;

    private string Label(uint number) => catalog.TryGetValue(number, out var beast) && beast.NavigationKind == "duty"
        ? $"Capture / Gourd #{number}" : $"Uncaptured #{number}";

    private void DrawLabel(ImDrawListPtr draw, Vector2 anchor, string label, bool aboveModel, Vector4 color)
    {
        if (!float.IsFinite(anchor.X) || !float.IsFinite(anchor.Y))
            return;
        var size = ImGui.CalcTextSize(label) * config.LabelScale;
        var pos = anchor;
        var tip = anchor;
        if (aboveModel)
        {
            var viewport = ImGui.GetMainViewport();
            if (!ModelLabelLayout.TryPlace(anchor, size, viewport.Pos, viewport.Size, out pos, out tip))
                return;
        }
        draw.AddRectFilled(pos - new Vector2(5, 3), pos + size + new Vector2(5, 3), 0xDD181818, 4);
        var packed = ImGui.ColorConvertFloat4ToU32(color);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * config.LabelScale, pos, packed, label);
        if (aboveModel)
            draw.AddTriangleFilled(tip, tip + new Vector2(-6, -9), tip + new Vector2(6, -9), packed);
    }
}
