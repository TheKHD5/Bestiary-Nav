using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Numerics;
using System.Text.Json;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace BestiaryNav;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IUnlockState Unlocks { get; private set; } = null!;
    [PluginService] internal static IAetheryteList Aetherytes { get; private set; } = null!;
    [PluginService] internal static IGameInventory Inventory { get; private set; } = null!;
    [PluginService] internal static IBuddyList Buddies { get; private set; } = null!;
    [PluginService] internal static IFateTable Fates { get; private set; } = null!;

    private const string Command = "/bnav";
    private readonly BindingProfile binding;
    private readonly Dictionary<uint, MonsterEntry> monsters;
    private readonly bool bindingActive;
    private readonly string? bindingIssue;
    private readonly Configuration configuration;
    private readonly WindowSystem windowSystem = new("BestiaryNav");
    private readonly SettingsWindow settingsWindow;
    private readonly BestiaryQuickToggle quickToggle;
    private readonly UncapturedMarkers markers;
    private readonly AutoCapture autoCapture;
    private readonly CaptureRun captureRun;
    private readonly CaptureAllController captureAll = new();
    private readonly FarmingRun farming;
    private readonly uint beastmasterJob;
    private readonly CaptureStateReader captureState;
    private readonly TravelIpc travelIpc;
    private readonly TravelController travel;
    private readonly TravelPlanBuilder travelPlans;
    private readonly SpawnAreaMap spawnAreas;
    private readonly DutySelection dutySelection;
    private readonly Dictionary<uint, BeastAcquisition> acquisition;
    private readonly CollectionWindow collectionWindow;
    private volatile CollectionSnapshot collectionSnapshot = CollectionSnapshot.Empty;
    private long nextCollectionUpdate;
    private long nextCaptureAllUpdate;
    private volatile string diagnosticReport = "Diagnostics will be ready after the next game update.";
    private string actualGameVersion = "";
    private string actualDalamudVersion = "";
    private bool pendingAutoTravel;
    private string lastTravelStatus = "";
    private string lastNotification = "No messages yet.";
    private bool pendingBestiaryOpen;
    private bool pendingClearSpawnAreas;
    private NavigationRequest? pendingRequest;
    private uint lastBestiaryNumber;
    private long lastSelectionAt;
    private string? probeAddon;
    private long lastProbeAt;
    private bool disposed;

    public Plugin()
    {
        binding = ReadResource<BindingProfile>("binding.json");
        configuration = PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration { BlockInCombat = binding.BlockInCombat };
        monsters = ReadResource<LocationDatabase>("locations.json").BuildIndex();
        var gameVersion = Data.GameData.Repositories.TryGetValue("ffxiv", out var repository)
            ? repository.Version?.Trim() ?? "" : "";
        var dalamudVersion = typeof(IDalamudPlugin).Assembly.GetName().Version?.ToString() ?? "";
        actualGameVersion = gameVersion;
        actualDalamudVersion = dalamudVersion;
        acquisition = ReadResource<CollectionMetadata>("collection.json").BuildIndex(monsters);

        bindingIssue = !binding.Enabled ? "Bestiary integration is disabled in this build." :
            gameVersion != binding.VerifiedGameVersion
                ? $"Unsupported FFXIV version {gameVersion}; this build supports {binding.VerifiedGameVersion}. Update Bestiary Nav." :
            dalamudVersion != binding.VerifiedDalamudVersion
                ? $"Unsupported Dalamud version {dalamudVersion}; this build supports {binding.VerifiedDalamudVersion}. Update Bestiary Nav." :
            binding.AddonName != BestiarySelectionReader.AddonName ? "Invalid Bestiary addon profile. Update Bestiary Nav." : null;
        bindingActive = bindingIssue == null;
        Log.Information($"Bestiary binding active: {bindingActive}; game: {gameVersion}; Dalamud: {dalamudVersion}.");
        if (bindingIssue != null) Log.Warning(bindingIssue);
        configuration.MarkerRange = float.IsFinite(configuration.MarkerRange)
            ? Math.Clamp(configuration.MarkerRange, 10, 100) : 50;
        configuration.SpawnAreaRadius = float.IsFinite(configuration.SpawnAreaRadius) ? Math.Clamp(configuration.SpawnAreaRadius, 10, 200) : 60;
        configuration.LabelScale = float.IsFinite(configuration.LabelScale) ? Math.Clamp(configuration.LabelScale, 0.75f, 2f) : 1;
        configuration.EligibleColor = ValidColor(configuration.EligibleColor, new(.4f, .88f, .4f, 1));
        configuration.AboveLevelColor = ValidColor(configuration.AboveLevelColor, new(1, .33f, .33f, 1));
        configuration.Favorites ??= [];
        configuration.ChatOutput ??= new();
        configuration.ChatOutput.Normalize();
        configuration.Appearance ??= new();
        configuration.Appearance.Normalize();
        configuration.Farming ??= new();
        configuration.Farming.Normalize();
        configuration.AutoCaptureMaxHpPercent = Math.Clamp(configuration.AutoCaptureMaxHpPercent, 1, 100);
        if (!configuration.MapTrackingOnClick) configuration.AutoTravel = false;
        configuration.Favorites.RemoveWhere(n => !monsters.ContainsKey(n));
        var captureTargets = ReadResource<CaptureTargetDatabase>("capture-targets.json");
        captureState = new CaptureStateReader(SigScanner, Log, bindingIssue ??
            (captureTargets.GameVersion != gameVersion ? "Capture target data does not support this FFXIV version. Update Bestiary Nav." : null));
        Log.Information($"Capture-state binding available: {captureState.IsAvailable}.");
        // Resolve from the English sheet once, then compare stable row IDs across
        // all client languages. No guessed job ID or native offset is needed.
        var beastmasterJobId = Data.GetExcelSheet<ClassJob>(Dalamud.Game.ClientLanguage.English)
            .FirstOrDefault(row => row.Abbreviation.ExtractText() == "BST").RowId;
        beastmasterJob = beastmasterJobId;
        markers = new UncapturedMarkers(configuration, captureState, captureTargets.BuildIndex(monsters), monsters,
            Objects, ClientState, Condition, GameGui, beastmasterJobId);
        autoCapture = new AutoCapture(configuration, captureState, captureTargets.BuildIndex(monsters), Objects,
            Targets, ClientState, Condition, Log, Data, beastmasterJobId, bindingActive);
        travelIpc = new TravelIpc(PluginInterface, TryMountForTravel);
        travel = new TravelController(travelIpc);
        travelPlans = new TravelPlanBuilder(Data, Aetherytes);
        captureRun = new CaptureRun(configuration, captureState, captureTargets.BuildIndex(monsters), Objects, Targets,
            ClientState, Condition, Log, autoCapture, new CaptureRotationIpc(PluginInterface), travel, GetTravelPlayer, beastmasterJobId,
            () => !GameGui.GameUiHidden);
        var farmData = ReadResource<FarmingDatabase>("farming-areas.json");
        foreach (var farmArea in farmData.Areas)
        {
            if (farmArea.Location.TerritoryTypeId == 0)
            {
                var territory = Data.GetExcelSheet<TerritoryType>(Dalamud.Game.ClientLanguage.English)
                    .FirstOrDefault(t => t.PlaceName.Value.Name.ExtractText() == farmArea.Location.Area && t.ContentFinderCondition.RowId == 0 && t.Map.RowId != 0);
                farmArea.Location.TerritoryTypeId = territory.RowId;
                farmArea.Location.MapId = territory.Map.RowId;
            }
        }
        farming = new FarmingRun(configuration, farmData, Objects, Targets, ClientState, Condition, Log,
            new CaptureRotationIpc(PluginInterface), travel, travelPlans, GetTravelPlayer, () => bindingActive,
            () => !GameGui.GameUiHidden, beastmasterJobId, new FarmingSupplies(Data, Inventory, Buddies), Data, Fates, new FarmingRespawn(GameGui),
            ReadResource<FarmingTargetDatabase>("farming-targets.json"));
        spawnAreas = new SpawnAreaMap(Data, bindingActive);
        dutySelection = new DutySelection(GameGui, CanEditDutySelection, message => PrintMessage(ChatMessageKind.Warnings, message));
        collectionWindow = new CollectionWindow(monsters, acquisition, configuration, () => collectionSnapshot,
            n => QueueBeast(n), SaveConfiguration, () => pendingBestiaryOpen = true);
        windowSystem.AddWindow(collectionWindow);
        settingsWindow = new SettingsWindow(configuration, bindingIssue, SaveConfiguration, () => markers.Status,
            travel, () => travelIpc.Available, StopTravel, SetAutoTravel, collectionWindow.Open, () => diagnosticReport, SetLocationPopup,
            () => lastNotification, () => autoCapture.Status, captureRun, captureAll, SetCaptureAll, farming, SetFarming);
        quickToggle = new BestiaryQuickToggle(configuration, GameGui, ClientState, Condition, bindingActive, SetAutoTravel, OpenUi,
            collectionWindow.Open, travel, StopTravel, SetLocationPopup, captureRun,
            () => CollectionPlanner.Recommend(monsters.Values, acquisition, collectionSnapshot),
            n => QueueBeast(n, autoTravel: true, fromBestiaryClick: true), captureAll, SetCaptureAll, farming, SetFarming);
        windowSystem.AddWindow(settingsWindow);
        PluginInterface.UiBuilder.Draw += DrawWindows;
        PluginInterface.UiBuilder.Draw += markers.Draw;
        PluginInterface.UiBuilder.Draw += quickToggle.Draw;
        ClientState.Logout += OnLogout;
        PluginInterface.UiBuilder.OpenConfigUi += OpenUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenUi;
        Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Master's Bestiary. Use /bnav config for settings or /bnav stop to cancel travel.",
        });
        Framework.Update += OnFrameworkUpdate;
        if (bindingActive)
        {
            AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, binding.AddonName, OnSelection);
            AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, binding.AddonName, OnFinalize);
        }
    }

    private static Vector4 ValidColor(Vector4 color, Vector4 fallback) =>
        float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z)
            ? new(Vector3.Clamp(new(color.X, color.Y, color.Z), Vector3.Zero, Vector3.One), 1) : fallback;

    private void OpenUi()
    {
        if (!disposed)
            settingsWindow.Open();
    }

    private void SaveConfiguration()
    {
        markers.Reset();
        if (captureAll.Enabled && (!configuration.CaptureRun || !configuration.MapTrackingOnClick || !configuration.EnableClickNavigation))
            StopTravel();
        if (!configuration.EnableClickNavigation)
            pendingRequest = null;
        PluginInterface.SavePluginConfig(configuration);
    }

    private void DrawWindows()
    {
        using var titleBar = new UiTitleBarLayoutScope();
        using var theme = new UiThemeScope(configuration.Appearance, titleBar: true);
        windowSystem.Draw();
    }

    private void PrintMessage(ChatMessageKind kind, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lastNotification = message;
        Log.Information($"{kind}: {message}");
        var channel = configuration.ChatOutput.Route(kind);
        if (channel == null) return;
        // Print only to the local chat log; never send a chat command or network message.
        Chat.Print(new XivChatEntry
        {
            Type = Enum.TryParse<XivChatType>(channel, out var type) ? type : XivChatType.Echo,
            Message = new SeStringBuilder().AddText($"[Bestiary Nav] {message}").Build(),
            Silent = true,
        });
    }

    private void ReportTravelStatus()
    {
        if (travel.Status == lastTravelStatus) return;
        lastTravelStatus = travel.Status;
        PrintMessage(travel.Active ? ChatMessageKind.TravelProgress : ChatMessageKind.TravelResults, travel.Status);
    }

    private void SetLocationPopup(bool enabled)
    {
        configuration.MapTrackingOnClick = enabled;
        if (enabled) configuration.EnableClickNavigation = true;
        else
        {
            pendingClearSpawnAreas = true;
            configuration.AutoTravel = false;
            StopTravel();
        }
        SaveConfiguration();
    }

    private void SetAutoTravel(bool enabled)
    {
        configuration.AutoTravel = enabled;
        if (enabled)
        {
            configuration.EnableClickNavigation = true;
            configuration.MapTrackingOnClick = true;
        }
        if (!enabled) StopTravel();
        SaveConfiguration();
    }

    private static T ReadResource<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"BestiaryNav.{name}")
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Empty resource {name}.");
    }

    private unsafe void OnSelection(AddonEvent _, AddonArgs args)
    {
        if (disposed || !bindingActive || !configuration.EnableClickNavigation || args is not AddonReceiveEventArgs receive ||
            (int)receive.AtkEventType != (int)AtkEventType.MouseDown || receive.AtkEventData == 0)
            return;

        // Observe the game's existing left-button selection after its original handler.
        // Right-click, hover, page changes and controller focus never trigger navigation.
        if (((AtkEventData*)receive.AtkEventData)->MouseData.ButtonId != 0)
            return;
        var live = GameGui.GetAddonByName(binding.AddonName);
        if (live.IsNull || live.Address != args.Addon.Address || !live.IsReady || !live.IsVisible)
            return;

        if (!BestiarySelectionReader.TryRead((AtkUnitBase*)live.Address, receive.EventParam, out var number))
            return;
        var now = Environment.TickCount64;
        if (number == lastBestiaryNumber && now - lastSelectionAt < 350)
            return;
        lastBestiaryNumber = number;
        lastSelectionAt = now;
        Log.Information($"Bestiary left-click: eventParam={receive.EventParam}, number={number}.");
        QueueBeast(number, fromBestiaryClick: true);
    }

    private bool QueueBeast(uint bestiaryNumber, bool autoTravel = false, bool fromBestiaryClick = false, bool fromCaptureAll = false)
    {
        if (farming.Enabled) farming.Stop("Farming stopped for your selected Bestiary entry.");
        // Clear an earlier request even if the new selection has no known location.
        pendingRequest = null;
        pendingAutoTravel = false;
        if (!monsters.TryGetValue(bestiaryNumber, out var monster))
        {
            Log.Debug($"Unknown Bestiary number {bestiaryNumber}.");
            return false;
        }
        // Prefer the current territory, then the closest reported location on this map.
        pendingRequest = NavigationRequest.ForBeast(monster) with { BestiaryNumber = bestiaryNumber, FromBestiaryClick = fromBestiaryClick, FromCaptureAll = fromCaptureAll };
        if (monster.NavigationKind == "map" && CollectionPlanner.PreferredLocation(monster, collectionSnapshot) is { } preferred)
        {
            // Try other documented locations on subsequent batch attempts.
            var failures = fromCaptureAll ? captureAll.Failures(bestiaryNumber) : 0;
            pendingRequest = pendingRequest with { Location = failures == 0 ? preferred : monster.Locations[(failures - 1) % monster.Locations.Count] };
        }
        pendingAutoTravel = autoTravel;
        return true;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed)
            return;
        markers.Update();
        if (captureAll.Enabled && pendingRequest is { FromCaptureAll: false })
            captureAll.Stop("Capture all stopped for your selected destination.");
        if (captureAll.Enabled && configuration.CancelTravelOnManualMovement && GetTravelPlayer().ManualMovement)
            StopTravel("Capture all canceled because you moved manually.");
        if (pendingRequest != null && captureRun.Active) captureRun.Stop("Stopped for the newly selected destination.");
        if (pendingRequest != null && farming.Enabled) farming.Stop("Stopped for the newly selected destination.");
        captureRun.Update();
        farming.Update();
        autoCapture.Suspended = farming.Enabled;
        autoCapture.Update();
        UpdateCollection();
        UpdateCaptureAll();
        if (pendingRequest != null) dutySelection.Cancel();
        else dutySelection.Update();
        if (pendingClearSpawnAreas)
        {
            pendingClearSpawnAreas = false;
            spawnAreas.ClearOwned();
        }
        if (travel.Active && !captureRun.Active && !farming.Enabled)
        {
            travel.Update(GetTravelPlayer(), Environment.TickCount64, configuration.CancelTravelOnManualMovement);
            ReportTravelStatus();
        }
        if (pendingBestiaryOpen)
        {
            pendingBestiaryOpen = false;
            OpenBestiary();
        }
        if (disposed || pendingRequest is not { } request)
            return;
        pendingRequest = null;
        var startTravel = pendingAutoTravel || configuration.AutoTravel;
        pendingAutoTravel = false;
        // With Location pop-up disabled, Bestiary browsing has no navigation
        // side effects, including automatic travel and duty selection changes.
        if (request.FromBestiaryClick && !configuration.MapTrackingOnClick) return;
        captureRun.Stop("Stopped to handle the newly selected entry.");
        var startCaptureRun = request.FromBestiaryClick && configuration.CaptureRun && request.BestiaryNumber != 0;
        dutySelection.Cancel();
        if (travel.Active) travel.Stop("Stopped to handle the newly selected destination.");
        if (startTravel) lastTravelStatus = "";
        // No native pointer or event args survive the click callback. Process the latest
        // request on the framework thread, after leaving native ReceiveEvent dispatch.
        try
        {
            if (request.Notice.Length != 0)
                PrintMessage(request.Location == null && request.Duty == null ? ChatMessageKind.QuestGuidance : ChatMessageKind.Locations, request.Notice);
            string reason;
            if (request.Duty is { } duty)
            {
                spawnAreas.ClearOwned();
                if (startCaptureRun && ClientState.TerritoryType == duty.TerritoryTypeId && Objects.LocalPlayer is { } dutyPlayer)
                {
                    captureRun.Start(request.BestiaryNumber, new(duty.TerritoryTypeId, dutyPlayer.Position, 0, 0,
                        duty.Name, configuration.SpawnAreaRadius), false);
                    return;
                }
                if (!TryOpenDutyFinder(duty, out reason))
                    PrintMessage(ChatMessageKind.Warnings, reason);
                else if (startTravel || startCaptureRun)
                {
                    travel.Stop("Duty Finder opened. Enter and navigate the duty manually.");
                    if (startCaptureRun) captureRun.Stop("Enter the duty manually, then select the entry near its capture target.");
                    ReportTravelStatus();
                }
            }
            else if (request.Location is { } point)
            {
                if (!TryOpenSpawnArea(request, point, out reason))
                    PrintMessage(ChatMessageKind.Warnings, reason);
                else if (startTravel || startCaptureRun)
                {
                    if (!bindingActive)
                        travel.Stop("Auto travel requires a compatible game and Dalamud version.");
                    else
                    {
                        var plan = travelPlans.Build(point, configuration.SpawnAreaRadius);
                        Log.Information($"Auto travel requested: territory={plan.TerritoryId}, point={plan.MapPoint}, aetheryte={plan.AetheryteId}.");
                        if (startCaptureRun)
                        {
                            // Resume an interrupted local attempt where we are,
                            // instead of mounting and returning to the circle center.
                            var alreadyInArea = request.FromCaptureAll && Objects.LocalPlayer is { } nearbyPlayer &&
                                ClientState.TerritoryType == plan.TerritoryId &&
                                !Condition[ConditionFlag.Mounted] && !Condition[ConditionFlag.InFlight] &&
                                CaptureRunPolicy.InArea(nearbyPlayer.Position, plan.MapPoint, plan.SearchRadius, plan.TargetFloor);
                            captureRun.Start(request.BestiaryNumber, plan, !alreadyInArea);
                        }
                        else travel.Start(plan, GetTravelPlayer(), Environment.TickCount64);
                    }
                    ReportTravelStatus();
                }
            }
            else
            {
                spawnAreas.ClearOwned();
                if (startTravel || startCaptureRun)
                {
                    travel.Stop("Quest guidance shown. There is no automatic route for this entry.");
                    if (startCaptureRun) captureRun.Stop("This entry uses quest guidance and has no automatic capture route.");
                    ReportTravelStatus();
                }
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not navigate to beast.");
            PrintMessage(ChatMessageKind.Warnings, "Could not navigate; see /xllog.");
        }
    }

    private void OnLogout(int type, int code)
    {
        autoCapture.Reset();
        collectionSnapshot = CollectionSnapshot.Empty;
        nextCollectionUpdate = 0;
        pendingBestiaryOpen = false;
        StopTravel();
        markers.Reset();
    }

    private void UpdateCollection()
    {
        if (Environment.TickCount64 < nextCollectionUpdate) return;
        nextCollectionUpdate = Environment.TickCount64 + 500;
        var player = Objects.LocalPlayer;
        ulong bits = 0;
        var ready = ClientState.IsLoggedIn && player != null && captureState.TryRead(out bits);
        var mapPosition = Vector2.Zero;
        var map = Data.GetExcelSheet<Map>().GetRowOrDefault(ClientState.MapId);
        if (player != null && map is { } row && row.SizeFactor > 0 &&
            float.IsFinite(player.Position.X) && float.IsFinite(player.Position.Z))
            mapPosition = new(MapCoordinates.WorldToMap(player.Position.X, row.SizeFactor, row.OffsetX),
                MapCoordinates.WorldToMap(player.Position.Z, row.SizeFactor, row.OffsetY));
        var issue = !captureState.IsAvailable ? captureState.UnavailableReason : !ClientState.IsLoggedIn || player == null
            ? "Log in to see collection progress." : "Open Master's Bestiary once to load capture records.";
        collectionSnapshot = new(ready, bits, player?.Level ?? 0, ClientState.TerritoryType, ClientState.MapId,
            mapPosition, ready ? "Capture records ready." : issue);
        var report = new StringBuilder();
        report.AppendLine($"Bestiary Nav {typeof(Plugin).Assembly.GetName().Version}");
        report.AppendLine($"FFXIV: {actualGameVersion}; Dalamud: {actualDalamudVersion}");
        report.AppendLine($"Compatibility: {bindingIssue ?? "verified"}");
        report.AppendLine($"Player: job={player?.ClassJob.RowId}; level={player?.Level}; HP={player?.CurrentHp}/{player?.MaxHp}; dead={player?.IsDead}; combat={Condition[ConditionFlag.InCombat]}");
        report.AppendLine($"Wait for full HP before engaging: {configuration.WaitForFullHpBeforeEngaging}");
        report.AppendLine($"Travel dependencies: {(travelIpc.Available ? "connected" : "unavailable")}; phase: {travel.Phase}");
        report.AppendLine($"Travel: {travel.Status}");
        report.AppendLine($"Auto Capture: enabled={configuration.AutoCapture}; HP limit={configuration.AutoCaptureMaxHpPercent}%; {autoCapture.Status}");
        report.AppendLine($"Capture run: enabled={configuration.CaptureRun}; phase={captureRun.Phase}; {captureRun.Status}");
        report.AppendLine($"Farming: enabled={farming.Enabled}; phase={farming.Phase}; {farming.Status}");
        report.AppendLine($"Levelling target range: {farming.TargetRange}");
        report.AppendLine($"Levelling groups: {farming.SelectedGroupDetails}");
        report.AppendLine($"Levelling targets: {farming.TargetFilterStatus}");
        report.AppendLine($"Levelling patrol: {farming.PatrolStatus}");
        report.AppendLine($"Levelling search: {farming.LastSearchResult}");
        report.AppendLine($"Levelling companion: objectId={farming.CompanionId}");
        report.AppendLine($"Farming supplies: {farming.SuppliesStatus}");
        report.AppendLine($"Levelling last stop: {farming.LastStopReason}");
        report.AppendLine($"Levelling revival: auto={configuration.Farming.AutoRespawn}; {farming.RespawnStatus}");
        report.AppendLine($"Levelling recovery: {farming.LastRecovery}");
        report.AppendLine($"Levelling rotation: {farming.RotationStatus}");
        report.AppendLine($"Capture before last stop: {captureRun.LastActiveStatus}");
        report.AppendLine($"Capture recovery: {captureRun.LastRecovery}");
        report.AppendLine($"Capture rotation: {captureRun.RotationStatus}");
        report.AppendLine($"Capture all: enabled={captureAll.Enabled}; entry={captureAll.Current}; selected={captureAll.Selected}; {captureAll.Status}");
        report.AppendLine($"Spawn-area radius: {configuration.SpawnAreaRadius:0} yalms");
        foreach (var line in markers.Diagnose(Targets.Target, false)) report.AppendLine(line);
        diagnosticReport = report.ToString();
    }

    private unsafe bool TryMountForTravel()
    {
        if (!bindingActive || !ClientState.IsLoggedIn || Condition[ConditionFlag.InCombat] ||
            Condition[ConditionFlag.Mounted] || Condition[ConditionFlag.MountOrOrnamentTransition]) return false;
        var actions = ActionManager.Instance();
        // GeneralAction 9 is Mount Roulette; unavailable actions fall back to ground travel.
        return actions != null && actions->GetActionStatus(ActionType.GeneralAction, 9) == 0 &&
            actions->UseAction(ActionType.GeneralAction, 9);
    }

    private TravelPlayer GetTravelPlayer()
    {
        var player = Objects.LocalPlayer;
        var loading = Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51];
        var worldReady = player != null && player.ClassJob.RowId != 0 && ClientState.TerritoryType != 0 && !GameGui.GameUiHidden;
        string? blocked = Condition[ConditionFlag.InCombat] ? "Travel stopped in combat." :
            player?.IsDead == true ? "Travel stopped because the player is incapacitated." :
            Condition[ConditionFlag.WatchingCutscene] || Condition[ConditionFlag.WatchingCutscene78] ||
            Condition[ConditionFlag.OccupiedInQuestEvent] ? "Travel stopped during an event or cutscene." : null;
        var manualMovement = false;
        if (configuration.CancelTravelOnManualMovement && ClientState.IsLoggedIn && worldReady && !loading && blocked == null)
        {
            if (!bindingActive)
                blocked = "Manual-movement cancellation is unavailable for this game version. Update Bestiary Nav or turn that setting off.";
            else
            {
                try { manualMovement = ManualMovementInput.Read(); }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not read manual movement input.");
                    blocked = "Travel stopped because movement input could not be checked.";
                }
            }
        }
        return new(ClientState.TerritoryType, player?.Position ?? Vector3.Zero, ClientState.IsLoggedIn,
            loading, player?.IsCasting == true, blocked, manualMovement,
            Condition[ConditionFlag.Mounted],
            bindingActive && ClientState.IsLoggedIn && worldReady && !loading && blocked == null && Condition[ConditionFlag.Mounted] &&
                Control.GetFlightAllowedStatus() == 0,
            Condition[ConditionFlag.InFlight], Condition[ConditionFlag.MountOrOrnamentTransition], worldReady);
    }

    private void SetFarming(bool enabled)
    {
        if (!enabled) { farming.Stop(); return; }
        StopTravel("Stopped previous automation to start farming.");
        farming.Start();
    }

    private void SetCaptureAll(bool enabled)
    {
        if (enabled && farming.Enabled) farming.Stop("Farming stopped to start Capture all.");
        StopTravel();
        if (!enabled) return;
        configuration.CaptureRun = true;
        configuration.EnableClickNavigation = true;
        configuration.MapTrackingOnClick = true;
        SaveConfiguration();
        captureAll.Start();
        nextCaptureAllUpdate = 0;
        if (!collectionSnapshot.Ready) pendingBestiaryOpen = true;
    }

    private void UpdateCaptureAll()
    {
        if (!captureAll.Enabled) return;
        if (captureRun.UserInterrupted) { StopTravel(captureRun.Status); return; }
        var now = Environment.TickCount64;
        if (now < nextCaptureAllUpdate) return;
        nextCaptureAllUpdate = now + 500;
        if (!captureRun.Active && !travel.Active && pendingRequest == null && Condition[ConditionFlag.InCombat])
            captureRun.TryDefendWhileWaiting(now, captureAll.Selected);
        var player = Objects.LocalPlayer;
        var movement = GetTravelPlayer();
        string? wait = !ClientState.IsLoggedIn || movement.Loading || !movement.WorldReady ? "Waiting for the game world…" :
            player == null || beastmasterJob == 0 || player.ClassJob.RowId != beastmasterJob ? "Equip BST to continue Capture all." :
            player.IsDead ? "Waiting until you are alive again…" :
            Condition[ConditionFlag.BoundByDuty] || Condition[ConditionFlag.BoundByDuty56] || Condition[ConditionFlag.BoundByDuty95] ? "Leave the duty to continue overworld captures." :
            movement.BlockReason != null || player.IsCasting ? "Waiting until travel is available…" :
            !bindingActive || !autoCapture.Compatible ? "Waiting for compatible Bestiary bindings…" :
            !travelIpc.Available ? "Waiting for Lifestream and vnavmesh…" : null;
        var next = captureAll.Update(now, collectionSnapshot, monsters.Values, acquisition,
            captureRun.Active || travel.Active || pendingRequest != null, wait, captureRun.Status);
        if (next is { } number) QueueBeast(number, autoTravel: true, fromBestiaryClick: true, fromCaptureAll: true);
    }

    private void StopTravel() => StopTravel("Stopped by a control, command, settings change, or logout.");

    private void StopTravel(string reason)
    {
        farming.Stop(reason);
        captureAll.Stop(reason);
        captureRun.Stop(reason);
        var wasActive = travel.Active;
        dutySelection.Cancel();
        pendingRequest = null;
        pendingAutoTravel = false;
        travel.Stop();
        if (wasActive) ReportTravelStatus();
    }

    private unsafe void OpenBestiary()
    {
        if (!bindingActive)
        {
            PrintMessage(ChatMessageKind.Warnings, bindingIssue ?? "Bestiary integration unavailable.");
            return;
        }
        if (!CanNavigate(out var reason))
        {
            PrintMessage(ChatMessageKind.Warnings, $"Cannot open Master's Bestiary right now. {reason}");
            return;
        }
        var addon = GameGui.GetAddonByName(BestiarySelectionReader.AddonName);
        if (!addon.IsNull && addon.IsVisible)
            return; // The menu command toggles this window; do not close it.

        // MainCommand row 100 is Master's Bestiary in the verified game sheet.
        const uint bestiaryCommand = 100;
        var ui = UIModule.Instance();
        if (ui == null || Data.GetExcelSheet<MainCommand>().GetRowOrDefault(bestiaryCommand) == null ||
            !ui->IsMainCommandUnlocked(bestiaryCommand))
        {
            PrintMessage(ChatMessageKind.Warnings, "Master's Bestiary is not available or has not been unlocked.");
            return;
        }
        ui->ExecuteMainCommand(bestiaryCommand);
        Log.Information("Opened Master's Bestiary from /bnav.");
    }

    private bool CanNavigate(out string reason)
    {
        reason = "";
        if (!ClientState.IsLoggedIn || GameGui.GameUiHidden ||
            Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51] ||
            Condition[ConditionFlag.WatchingCutscene] || Condition[ConditionFlag.WatchingCutscene78])
        {
            reason = "Navigation skipped while logged out, loading, in a cutscene, or with the UI hidden.";
            return false;
        }
        // Combat blocking is a user preference for manual map and duty navigation.
        if (configuration.BlockInCombat && Condition[ConditionFlag.InCombat])
        {
            reason = "Navigation skipped in combat.";
            return false;
        }
        return true;
    }

    private bool IsDutyQueueActive() => Condition[ConditionFlag.InDutyQueue] ||
        Condition[ConditionFlag.WaitingForDuty] || Condition[ConditionFlag.WaitingForDutyFinder];

    private bool CanEditDutySelection() => bindingActive && CanNavigate(out _) && !IsDutyQueueActive();

    private unsafe bool TryOpenDutyFinder(DutyDestination destination, out string reason)
    {
        if (!CanNavigate(out reason))
            return false;
        if (!bindingActive)
        {
            reason = bindingIssue ?? "Duty navigation needs a compatible plugin update.";
            return false;
        }
        var row = Data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(destination.ContentFinderConditionId);
        if (destination.ContentFinderConditionId == 0 || row == null ||
            row.Value.TerritoryType.RowId != destination.TerritoryTypeId ||
            row.Value.ContentType.RowId is not (2 or 4 or 5))
        {
            reason = "Unknown or mismatched Duty Finder destination.";
            return false;
        }
        var agent = AgentContentsFinder.Instance();
        if (agent == null)
        {
            reason = "Duty Finder is unavailable.";
            return false;
        }
        // Opening details only highlights the duty. When queued, stop here:
        // never schedule a clear, checkbox toggle, join, or queue withdrawal.
        var queued = IsDutyQueueActive();
        agent->OpenRegularDuty(destination.ContentFinderConditionId, false);
        if (queued || IsDutyQueueActive()) return true;
        var instance = Data.GetExcelSheet<InstanceContent>().GetRowOrDefault(row.Value.Content.RowId);
        if (instance == null || !Unlocks.IsInstanceContentUnlocked(instance.Value))
        {
            reason = "Duty details opened, but this duty is not unlocked. Existing selections were kept.";
            return false;
        }
        dutySelection.Start(destination.ContentFinderConditionId);
        return true;
    }

    private bool TryOpenSpawnArea(NavigationRequest request, MapLocation point, out string reason)
    {
        if (!TryValidateMapLocation(point, out reason)) return false;
        var beast = monsters.GetValueOrDefault(request.BestiaryNumber);
        return spawnAreas.Open(beast == null ? [point] : [point, .. beast.Locations.Where(p => !ReferenceEquals(p, point))], beast?.DisplayName ?? "Capture target",
            configuration.SpawnAreaRadius, out reason);
    }

    private bool TryValidateMapLocation(MapLocation point, out string reason)
    {
        if (!CanNavigate(out reason))
            return false;

        var territory = Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(point.TerritoryTypeId);
        var map = Data.GetExcelSheet<Map>().GetRowOrDefault(point.MapId);
        if (point.TerritoryTypeId == 0 || point.MapId == 0 || territory is null || map is null ||
            map.Value.TerritoryType.RowId != point.TerritoryTypeId || map.Value.SizeFactor == 0)
        {
            reason = "Unknown territory/map, mismatched map territory, or invalid map scale.";
            return false;
        }
        // Check Map.TerritoryType, NOT TerritoryType.Map: the latter is only a default
        // map and would incorrectly reject secondary floors.
        if (!MapCoordinates.IsOnMap(point.X, map.Value.SizeFactor) ||
            !MapCoordinates.IsOnMap(point.Y, map.Value.SizeFactor))
        {
            reason = "Coordinates are not finite or fall outside this map's coordinate extent.";
            return false;
        }

        return true;
    }

    private void OnFinalize(AddonEvent _, AddonArgs args)
    {
        pendingRequest = null;
        lastBestiaryNumber = 0;
    }

    private void OnCommand(string command, string arguments)
    {
        var words = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            pendingBestiaryOpen = true;
            return;
        }
        if (words.Length == 1 && words[0] == "config")
        {
            OpenUi();
            return;
        }
        if (words.Length == 1 && words[0] == "collection") { collectionWindow.Open(); return; }
        if (words.Length == 1 && words[0] == "next")
        {
            var next = CollectionPlanner.Recommend(monsters.Values, acquisition, collectionSnapshot);
            if (next == null) PrintMessage(ChatMessageKind.Commands, collectionSnapshot.Ready ? "No remaining target at your current level." : collectionSnapshot.Status);
            else QueueBeast(next.BestiaryNumber);
            return;
        }
        if (words.Length == 1 && words[0] == "diagnose")
        {
            foreach (var line in markers.Diagnose(Targets.Target))
            {
                PrintMessage(ChatMessageKind.Diagnostics, line);
            }
            return;
        }
        if (words.Length >= 2 && words[0] is "beast" or "go")
        {
            var key = string.Join(' ', words.Skip(1));
            var number = uint.TryParse(key, out var parsed) ? parsed :
                monsters.Values.FirstOrDefault(m => m.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase))?.BestiaryNumber ?? 0;
            if (!QueueBeast(number, words[0] == "go"))
                PrintMessage(ChatMessageKind.Commands, "Unknown beast name or Bestiary number.");
            return;
        }
        if (words.Length == 5 && words[0] == "map" &&
            uint.TryParse(words[1], out var territoryId) && uint.TryParse(words[2], out var mapId) &&
            float.TryParse(words[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            float.TryParse(words[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            pendingRequest = new NavigationRequest(new MapLocation { TerritoryTypeId = territoryId, MapId = mapId, X = x, Y = y }, null, "");
            pendingAutoTravel = false;
            return;
        }
        if (words.Length == 2 && words[0] == "probe")
        {
            StopProbe();
            probeAddon = words[1];
            AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, probeAddon, OnProbe);
            PrintMessage(ChatMessageKind.Diagnostics, $"Probing {probeAddon}; click entries, inspect /xllog. Use /bnav stop to finish.");
            return;
        }
        if (words.Length == 1 && words[0] == "stop")
        {
            StopTravel();
            StopProbe();
            return;
        }
        PrintMessage(ChatMessageKind.Commands, $"{Command} beast <number or name> | go <number or name> | map <territory> <map> <x> <y> | probe <addonName> | stop");
    }

    private unsafe void OnProbe(AddonEvent _, AddonArgs args)
    {
        if (disposed || probeAddon == null || args is not AddonReceiveEventArgs receive)
            return;
        var now = Environment.TickCount64;
        if (now - lastProbeAt < 250)
            return; // Deliberately sampled; not a complete event trace.
        lastProbeAt = now;
        var live = GameGui.GetAddonByName(probeAddon);
        if (live.IsNull || live.Address != args.Addon.Address || !live.IsReady || !live.IsVisible)
            return;
        var addon = (AtkUnitBase*)live.Address;
        Log.Information($"Probe {probeAddon}: type={(int)receive.AtkEventType} ({receive.AtkEventType}), param={receive.EventParam}, AtkValuesCount={addon->AtkValuesCount}");
        if (addon->AtkValues == null)
            return;
        // Do not dereference strings, union pointers, AtkEventData, or guessed offsets.
        for (var i = 0; i < Math.Min((int)addon->AtkValuesCount, 32); i++)
        {
            var value = addon->AtkValues[i];
            if (value.Type == AtkValueType.UInt)
                Log.Information($"  AtkValues[{i}] UInt={value.UInt}");
            else if (value.Type == AtkValueType.Int)
                Log.Information($"  AtkValues[{i}] Int={value.Int}");
        }
    }

    private void StopProbe()
    {
        if (probeAddon == null)
            return;
        AddonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, probeAddon, OnProbe);
        probeAddon = null;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        farming.Dispose();
        captureRun.Dispose();
        dutySelection.Cancel();
        spawnAreas.ClearOwned();
        travel.Dispose();
        pendingBestiaryOpen = false;
        pendingRequest = null;
        PluginInterface.UiBuilder.Draw -= DrawWindows;
        PluginInterface.UiBuilder.Draw -= markers.Draw;
        PluginInterface.UiBuilder.Draw -= quickToggle.Draw;
        ClientState.Logout -= OnLogout;
        markers.Reset();
        PluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenUi;
        windowSystem.RemoveAllWindows();
        StopProbe();
        if (bindingActive)
        {
            AddonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, binding.AddonName, OnSelection);
            AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, binding.AddonName, OnFinalize);
        }
        Framework.Update -= OnFrameworkUpdate;
        Commands.RemoveHandler(Command);
    }
}

public sealed class BindingProfile
{
    public bool Enabled { get; set; }
    public string VerifiedGameVersion { get; set; } = "";
    public string VerifiedDalamudVersion { get; set; } = "";
    public string AddonName { get; set; } = "";
    public bool BlockInCombat { get; set; } = true;
}
