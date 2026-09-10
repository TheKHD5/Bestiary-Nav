using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

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
    [PluginService] internal static IAetheryteList Aetherytes { get; private set; } = null!;

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
    private readonly CaptureStateReader captureState;
    private readonly TravelIpc travelIpc;
    private readonly TravelController travel;
    private readonly TravelPlanBuilder travelPlans;
    private bool pendingAutoTravel;
    private string lastTravelStatus = "";
    private bool pendingBestiaryOpen;
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
        var captureTargets = ReadResource<CaptureTargetDatabase>("capture-targets.json");
        captureState = new CaptureStateReader(SigScanner, Log, bindingIssue ??
            (captureTargets.GameVersion != gameVersion ? "Capture target data does not support this FFXIV version. Update Bestiary Nav." : null));
        Log.Information($"Capture-state binding available: {captureState.IsAvailable}.");
        markers = new UncapturedMarkers(configuration, captureState, captureTargets.BuildIndex(monsters),
            Objects, ClientState, Condition, GameGui);
        travelIpc = new TravelIpc(PluginInterface);
        travel = new TravelController(travelIpc);
        travelPlans = new TravelPlanBuilder(Data, Aetherytes);
        settingsWindow = new SettingsWindow(configuration, bindingIssue, SaveConfiguration, () => markers.Status,
            travel, () => travelIpc.Available, StopTravel, SetAutoTravel);
        quickToggle = new BestiaryQuickToggle(configuration, GameGui, ClientState, Condition, bindingActive, SetAutoTravel);
        windowSystem.AddWindow(settingsWindow);
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
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

    private void OpenUi()
    {
        if (!disposed)
            settingsWindow.Open();
    }

    private void SaveConfiguration()
    {
        markers.Reset();
        if (!configuration.EnableClickNavigation)
            pendingRequest = null;
        PluginInterface.SavePluginConfig(configuration);
    }

    private void SetAutoTravel(bool enabled)
    {
        configuration.AutoTravel = enabled;
        if (enabled) configuration.EnableClickNavigation = true;
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
        QueueBeast(number);
    }

    private bool QueueBeast(uint bestiaryNumber, bool autoTravel = false)
    {
        // Clear an earlier request even if the new selection has no known location.
        pendingRequest = null;
        pendingAutoTravel = false;
        if (!monsters.TryGetValue(bestiaryNumber, out var monster))
        {
            Log.Debug($"Unknown Bestiary number {bestiaryNumber}.");
            return false;
        }
        // Curated order: first location is preferred. A future chooser can expose alternatives.
        pendingRequest = NavigationRequest.ForBeast(monster);
        pendingAutoTravel = autoTravel;
        return true;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed)
            return;
        markers.Update();
        if (travel.Active)
        {
            travel.Update(GetTravelPlayer(), Environment.TickCount64, configuration.CancelTravelOnManualMovement);
            if (travel.Status != lastTravelStatus)
            {
                Log.Information($"Auto travel: {travel.Status}");
                lastTravelStatus = travel.Status;
                if (!travel.Active) Chat.Print($"[Bestiary Nav] {travel.Status}");
            }
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
        if (travel.Active) travel.Stop("Stopped to handle the newly selected destination.");
        // No native pointer or event args survive the click callback. Process the latest
        // request on the framework thread, after leaving native ReceiveEvent dispatch.
        try
        {
            if (request.Notice.Length != 0)
                Chat.Print($"[Bestiary Nav] {request.Notice}");
            string reason;
            if (request.Duty is { } duty)
            {
                if (!TryOpenDutyFinder(duty, out reason))
                    Chat.PrintError($"[Bestiary Nav] {reason}");
                else if (startTravel)
                    travel.Stop("Duty Finder opened. Enter and navigate the duty manually.");
            }
            else if (request.Location is { } point)
            {
                if (!TryOpenMapAndFlag(point, out reason))
                    Chat.PrintError($"[Bestiary Nav] {reason}");
                else if (startTravel)
                {
                    if (!bindingActive)
                        travel.Stop("Auto travel requires a compatible game and Dalamud version.");
                    else
                    {
                        var plan = travelPlans.Build(point);
                        Log.Information($"Auto travel requested: territory={plan.TerritoryId}, point={plan.MapPoint}, aetheryte={plan.AetheryteId}.");
                        travel.Start(plan, GetTravelPlayer(), Environment.TickCount64);
                    }
                    Chat.Print($"[Bestiary Nav] {travel.Status}");
                }
            }
            else if (startTravel) travel.Stop("Quest guidance shown. There is no automatic route for this entry.");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not navigate to beast.");
            Chat.PrintError("[Bestiary Nav] Could not navigate; see /xllog.");
        }
    }

    private void OnLogout(int type, int code)
    {
        pendingBestiaryOpen = false;
        StopTravel();
        markers.Reset();
    }

    private TravelPlayer GetTravelPlayer()
    {
        var player = Objects.LocalPlayer;
        var loading = Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51];
        string? blocked = Condition[ConditionFlag.InCombat] ? "Travel stopped in combat." :
            player?.IsDead == true ? "Travel stopped because the player is incapacitated." :
            Condition[ConditionFlag.WatchingCutscene] || Condition[ConditionFlag.WatchingCutscene78] ||
            Condition[ConditionFlag.OccupiedInQuestEvent] ? "Travel stopped during an event or cutscene." :
            !loading && (player == null || GameGui.GameUiHidden) ? "Travel stopped while the game UI is unavailable." : null;
        var manualMovement = false;
        if (configuration.CancelTravelOnManualMovement && ClientState.IsLoggedIn && !loading && blocked == null)
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
            loading, player?.IsCasting == true, blocked, manualMovement);
    }

    private void StopTravel()
    {
        pendingRequest = null;
        pendingAutoTravel = false;
        travel.Stop();
    }

    private unsafe void OpenBestiary()
    {
        if (!bindingActive)
        {
            Chat.PrintError($"[Bestiary Nav] {bindingIssue}");
            return;
        }
        if (!CanNavigate(out var reason))
        {
            Chat.Print($"[Bestiary Nav] Cannot open Master's Bestiary right now. {reason}");
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
            Chat.PrintError("[Bestiary Nav] Master's Bestiary is not available or has not been unlocked.");
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
        // This is a UX preference, not a technical requirement of OpenMapWithMapLink.
        if (configuration.BlockInCombat && Condition[ConditionFlag.InCombat])
        {
            reason = "Navigation skipped in combat.";
            return false;
        }
        return true;
    }

    private unsafe bool TryOpenDutyFinder(DutyDestination destination, out string reason)
    {
        if (!CanNavigate(out reason))
            return false;
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
        // Argument is ContentFinderCondition.RowId, NOT a territory, InstanceContent
        // or ContentFinderCondition.Content ID. This opens the duty details only.
        // No queue registration, party changes, or unrestricted-party settings are sent.
        agent->OpenRegularDuty(destination.ContentFinderConditionId, false);
        // Native method returns void: this means the open request was dispatched,
        // not that the player owns/unlocked the duty or the UI was visually verified.
        return true;
    }

    private bool TryOpenMapAndFlag(MapLocation point, out string reason)
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

        // X/Y are floats in displayed map units. The FLOAT overload performs the scale
        // and offset conversion (and its normal 0.05 display-rounding adjustment).
        // Do NOT cast to int: the int overload instead expects raw world units * 1000.
        var payload = new MapLinkPayload(point.TerritoryTypeId, point.MapId, point.X, point.Y);
        if (GameGui.OpenMapWithMapLink(payload))
            return true; // This operation opens the map AND sets the active <flag>.
        reason = "The game declined the map link.";
        return false;
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
        if (words.Length >= 2 && words[0] is "beast" or "go")
        {
            var key = string.Join(' ', words.Skip(1));
            var number = uint.TryParse(key, out var parsed) ? parsed :
                monsters.Values.FirstOrDefault(m => m.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase))?.BestiaryNumber ?? 0;
            if (!QueueBeast(number, words[0] == "go"))
                Chat.PrintError("[Bestiary Nav] Unknown beast name or Bestiary number.");
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
            Chat.Print($"[Bestiary Nav] Probing {probeAddon}; click entries, inspect /xllog. Use /bnav stop to finish.");
            return;
        }
        if (words.Length == 1 && words[0] == "stop")
        {
            StopTravel();
            StopProbe();
            return;
        }
        Chat.Print($"[Bestiary Nav] {Command} beast <number or name> | go <number or name> | map <territory> <map> <x> <y> | probe <addonName> | stop");
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
        travel.Dispose();
        pendingBestiaryOpen = false;
        pendingRequest = null;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
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
