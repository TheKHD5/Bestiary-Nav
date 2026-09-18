using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility;

namespace BestiaryNav;

internal sealed class SettingsWindow(Configuration configuration, string? bindingIssue, Action save, Func<string> markerStatus,
    TravelController travel, Func<bool> travelAvailable, Action stopTravel, Action<bool> setAutoTravel,
    Action openCollection, Func<string> diagnostics, Action<bool> setLocationPopup, Func<string> lastNotification, Func<string> captureStatus,
    CaptureRun captureRun, CaptureAllController captureAll, Action<bool> setCaptureAll, FarmingRun farming, Action<bool> setFarming)
    : Window("Bestiary Nav###BestiaryNavSettings")
{
    private uint farmingTargetTerritory;
    private string farmingSpeciesSearch = "";
    private string patrolName = "";
    private string patrolEditStatus = "";
    public override void Draw()
    {
        using var spacing = new UiContentSpacingScope(configuration.Appearance);
        if (ImGui.Button("Collection")) openCollection();
        ImGui.SameLine();
        if (ImGui.Button("Copy diagnostics")) ImGui.SetClipboardText(diagnostics());
        Tip("Copies plugin compatibility, capture records, selected target, and travel status for troubleshooting.");
        if (!ImGui.BeginTabBar("SettingsTabs")) return;
        if (ImGui.BeginTabItem("Navigation"))
        {
            var enabled = configuration.EnableClickNavigation;
            if (ImGui.Checkbox("Enable entry click navigation", ref enabled))
            {
                configuration.EnableClickNavigation = enabled;
                save();
            }
            Tip("Left-click a Bestiary entry to open its spawn area, Duty Finder, or quest guidance. Settings save automatically.");
            var tracking = configuration.MapTrackingOnClick;
            if (ImGui.Checkbox("Location pop-up", ref tracking)) setLocationPopup(tracking);
            Tip("Open the map or Duty Finder when clicking Master's Bestiary entries. Turning this off also disables Auto Navigate, stops the current trip, and clears the search circle. Bestiary clicks then have no navigation actions. Explicit commands and collection actions remain available.");
            var radius = configuration.SpawnAreaRadius;
            ImGui.SetNextItemWidth(170);
            if (ImGui.SliderFloat("Search radius (yalms)", ref radius, 10, 200, "%.0f"))
            {
                configuration.SpawnAreaRadius = radius;
                save();
            }
            Tip("Approximate search circles around reported spawn coordinates, not verified spawn boundaries. Capture runs walk survey points within this circle when no eligible beast is visible. Increase this for large spawn areas (up to 200 yalms). Applies when you next select a beast.");
            var blockCombat = configuration.BlockInCombat;
            if (ImGui.Checkbox("Skip navigation while in combat", ref blockCombat))
            {
                configuration.BlockInCombat = blockCombat;
                save();
            }
            Tip("Skip map and duty navigation during combat. Automatic travel always stops when combat starts.");
            if (bindingIssue != null) ImGui.TextWrapped(bindingIssue);
            ImGui.Separator();
            var autoTravel = configuration.AutoTravel;
            if (ImGui.Checkbox("Auto Navigate", ref autoTravel))
                setAutoTravel(autoTravel);
            Tip("Travel to the selected spawn-circle center: teleport if needed, summon a mount, and fly when the game allows it. Underground destinations use ground routes through their entrances; land before starting one. Turning this on also enables Location pop-up. Normal teleport costs apply. Duties open in Duty Finder.\n\n" +
                (travelAvailable() ? "Lifestream and vnavmesh connected." : "Requires Lifestream and vnavmesh installed and enabled."));
            var cancelOnMovement = configuration.CancelTravelOnManualMovement;
            if (ImGui.Checkbox("Cancel travel on manual movement", ref cancelOnMovement))
            {
                configuration.CancelTravelOnManualMovement = cancelOnMovement;
                save();
            }
            Tip("Movement keys, the controller movement stick, or both mouse buttons cancel the current trip. Auto Navigate stays enabled for your next selection.");
            if (autoTravel && !travelAvailable()) ImGui.TextWrapped("Auto Navigate needs Lifestream + vnavmesh.");
            if (travel.Active)
            {
                ImGui.TextWrapped(travel.Status);
                if (ImGui.Button("Stop travel")) stopTravel();
            }
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Capture"))
        {
            var all = captureAll.Enabled;
            if (ImGui.Checkbox("Capture all available", ref all)) setCaptureAll(all);
            Tip("Capture uncaptured overworld beasts at or below your current BST level using travel, spawn-area searches, Capture, and Rotation Solver. Enables capture runs and Location pop-up. Skips duties and quests. Stops when none remain at your level.\n\nFailed entries (including a lost or cleared target) retry after 15–120 seconds while this stays on, staying on the selected entry until capture is confirmed (or it is no longer level-eligible). Other documented locations for that same entry may be tried. Waits at least 3 seconds for capture results. Enemies directly attacking you are fought before the saved capture attempt resumes. Death, missing dependencies, and job changes wait for recovery; it does not revive you or change jobs.\n\nTurn this off or use /bnav stop to cancel all retries. Manual movement (when enabled), another destination, logout, and plugin reload also stop the batch. Normal teleport costs apply. Session-only; never starts itself after a reload.");
            ImGui.TextWrapped(captureAll.Status);
            ImGui.Separator();
            var run = configuration.CaptureRun;
            if (ImGui.Checkbox("Auto capture run (Rotation Solver)", ref run))
            {
                configuration.CaptureRun = run;
                if (!run) captureRun.Stop();
                save();
            }
            Tip("BST only. Clicking an uncaptured Bestiary entry travels to its spawn area, selects the closest eligible beast, applies Capture, then starts Rotation Solver's BST rotation. Waits at least 3 seconds after defeat and rechecks capture records before trying another beast in that same area. Stops after capture, target changes, or /bnav stop. Location pop-up must be on.\n\nRequires Rotation Solver Reborn (7.5.6.8+ with its BST rotation selected), vnavmesh, and Lifestream. Start with Rotation Solver off; disable its Teaching Mode and Auto On settings. Bestiary Nav turns it on for the marked capture target or an enemy actively attacking you, and off between attempts. Incidental combat interrupts travel or patrol temporarily; the saved destination resumes once combat clears. Changing its mode yourself stops the run. Duties must be entered manually; select the entry again near its beast inside. Runs expire after 30 minutes.");
            if (run || captureRun.Active) ImGui.TextWrapped(captureRun.Status);
            Tip("If no player spell/weaponskill is recorded for 6 seconds, or the target takes no damage for 8 seconds while in range, Bestiary Nav refreshes Rotation Solver and attempts the next valid basic combo step. Capture recovery requires your mark. During defense, recovery can also act on the selected enemy actively attacking you. Copy diagnostics includes the last recovery result.");
            if ((captureRun.Active || captureAll.Enabled) && ImGui.Button("Stop capture run")) stopTravel();
            DrawHealthRecoverySetting();
            ImGui.Separator();
            var autoCapture = configuration.AutoCapture;
            if (ImGui.Checkbox("Auto Capture main target", ref autoCapture)) { configuration.AutoCapture = autoCapture; save(); }
            Tip("Only while equipped as BST and in combat with a living, uncaptured main target at or below your level. Uses Capture when the game allows it. Waits while any beast has your Interest Captured effect, then retries if the main target remains eligible. Does not switch targets or start combat.");
            var hp = configuration.AutoCaptureMaxHpPercent;
            ImGui.SetNextItemWidth(190 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderInt("Target HP at or below (%)", ref hp, 1, 100)) { configuration.AutoCaptureMaxHpPercent = hp; save(); }
            Tip("Applies to standalone Auto Capture. 100% casts as soon as eligible. Lower values wait for weaker targets and improve the capture chance. Capture runs always apply Capture before damage combos. The plugin never refreshes your active mark merely because HP has fallen.");
            ImGui.TextWrapped(captureStatus());
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Levelling (Experimental)###Farming"))
        {
            var enabled = farming.Enabled;
            if (ImGui.Checkbox("Levelling mode", ref enabled)) setFarming(enabled);
            Tip("BST only. Travel to documented overworld spawn areas with vnavmesh and Lifestream, then use Rotation Solver against the nearest eligible enemy of any type inside the patrol circle and selected level range. Captured beasts are valid targets; Capture is paused. Start with Rotation Solver off. /bnav stop cancels. Levelling does not restart after reload. Normal teleport and consumable costs apply.");
            DrawHealthRecoverySetting();
            var minimum = configuration.Farming.MinimumAbove;
            var maximum = configuration.Farming.MaximumAbove;
            ImGui.SetNextItemWidth(150);
            var changed = ImGui.SliderInt("Minimum levels above BST", ref minimum, 1, 10);
            ImGui.SetNextItemWidth(150);
            changed |= ImGui.SliderInt("Maximum levels above BST", ref maximum, 1, 10);
            if (changed)
            {
                configuration.Farming.MinimumAbove = minimum; configuration.Farming.MaximumAbove = maximum;
                configuration.Farming.Normalize();
                if (farming.Enabled) setFarming(false);
                save();
            }
            Tip("Offsets always use your current BST level. Setting both to +5 targets exactly five levels above you, including after level-ups. Partial overlaps stay eligible. Empty patrols pause for 60 seconds, then retry; route failures retry after two minutes. Other eligible selected groups can be tried during the pause. Changing the range stops the run. Duties are excluded; FATE participation is optional.");
            ImGui.BeginDisabled(configuration.Farming.SelectedPatrol.Length > 0);
            DrawFarmingGroups();
            ImGui.EndDisabled();
            DrawFarmingTargets();
            DrawFarmingPatrols();
            var goal = configuration.Farming.TargetLevel;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Target BST level (0 = no limit)", ref goal)) { configuration.Farming.TargetLevel = Math.Clamp(goal, 0, 100); save(); }
            Tip("Stops farming as soon as your BST level reaches this value, before selecting another target or destination.");
            var fates = configuration.Farming.ParticipateInFates;
            ImGui.BeginDisabled(configuration.Farming.SelectedGroups.Count > 0 || configuration.Farming.SelectedPatrol.Length > 0);
            if (ImGui.Checkbox("Participate in FATEs", ref fates)) { configuration.Farming.ParticipateInFates = fates; save(); }
            ImGui.EndDisabled();
            Tip("Available with Automatic monster selection and catalog patrols. Custom routines and selected groups keep their chosen locations and do not detour to FATEs.");
            var ignore = configuration.Farming.IgnoreNotoriousMonsters;
            if (ImGui.Checkbox("Ignore Notorious Monsters", ref ignore)) { configuration.Farming.IgnoreNotoriousMonsters = ignore; save(); }
            Tip("Skip hunt marks and enemies with boss rank when choosing farming or FATE pulls. Self-defense against an enemy already attacking you still takes priority.");
            var respawn = configuration.Farming.AutoRespawn;
            if (ImGui.Checkbox("Auto respawn and resume", ref respawn)) { configuration.Farming.AutoRespawn = respawn; save(); }
            Tip("Use the game's revive confirmation when incapacitated, wait for loading and HP recovery, then choose an area and resume. If off, farming waits for you to revive manually. Never accepts unrelated dialogs.");
            var chocobo = configuration.Farming.SummonChocobo;
            if (ImGui.Checkbox("Summon companion chocobo", ref chocobo)) { configuration.Farming.SummonChocobo = chocobo; save(); }
            Tip("Uses Gysahl Greens from your inventory when no companion is summoned. Requires the companion unlock and an allowed area. Missing supplies do not stop farming.");
            var food = configuration.Farming.UseFood;
            if (ImGui.Checkbox("Refresh food EXP buff", ref food)) { configuration.Farming.UseFood = food; save(); }
            var choices = farming.FoodChoices();
            var selectedName = "Select food from inventory";
            foreach (var choice in choices) if (choice.Id == configuration.Farming.FoodId) selectedName = choice.Name;
            if (ImGui.BeginCombo("Food", selectedName))
            {
                if (choices.Count == 0) ImGui.TextDisabled("No usable food found in your inventory bags.");
                foreach (var choice in choices)
                    if (ImGui.Selectable($"{choice.Name} ×{choice.Count}##{choice.Id}", configuration.Farming.FoodId == choice.Id))
                    { configuration.Farming.FoodId = choice.Id; save(); }
                ImGui.EndCombo();
            }
            Tip("Only uses the selected food and quality from the four inventory bags. Waits for Well Fed to expire; refreshes between fights while stationary and dismounted. Existing food buffs are preserved. No purchases or HQ substitutions.");
            ImGui.Separator();
            ImGui.TextWrapped(farming.Status);
            ImGui.TextWrapped(farming.TargetRange);
            if (configuration.Farming.SelectedPatrol.Length > 0) ImGui.TextWrapped(farming.PatrolStatus);
            ImGui.TextWrapped(farming.SuppliesStatus);
            if (farming.Enabled && ImGui.Button("Stop farming")) setFarming(false);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Labels"))
        {
            var showMarkers = configuration.ShowUncapturedMarkers;
            if (ImGui.Checkbox("Highlight uncaptured beasts", ref showMarkers))
            {
                configuration.ShowUncapturedMarkers = showMarkers;
                save();
            }
            Tip("Labels appear above beasts and beside enemy-list rows. Green: at or below your level. Red: above your level. Labels disappear after capture. Duty beasts may award a gourd after the encounter.");
            var onlyBeastmaster = configuration.LabelsOnlyOnBeastmaster;
            if (ImGui.Checkbox("Only show labels on Beastmaster (BST)", ref onlyBeastmaster))
            {
                configuration.LabelsOnlyOnBeastmaster = onlyBeastmaster;
                save();
            }
            Tip("Show beast and enemy-list labels only while your equipped job is Beastmaster. Updates automatically when you switch jobs. Collection progress and map navigation remain available on every job.");
            var models = configuration.ShowModelLabels;
            if (ImGui.Checkbox("Above beasts", ref models)) { configuration.ShowModelLabels = models; save(); }
            ImGui.SameLine();
            var rows = configuration.ShowEnemyListLabels;
            if (ImGui.Checkbox("Enemy list", ref rows)) { configuration.ShowEnemyListLabels = rows; save(); }
            var scale = configuration.LabelScale;
            ImGui.SetNextItemWidth(190);
            if (ImGui.SliderFloat("Label size", ref scale, .75f, 2, "%.2fx")) { configuration.LabelScale = scale; save(); }
            var eligible = new Vector3(configuration.EligibleColor.X, configuration.EligibleColor.Y, configuration.EligibleColor.Z);
            if (ImGui.ColorEdit3("At or below my level", ref eligible)) { configuration.EligibleColor = new(eligible, 1); save(); }
            var above = new Vector3(configuration.AboveLevelColor.X, configuration.AboveLevelColor.Y, configuration.AboveLevelColor.Z);
            if (ImGui.ColorEdit3("Above my level", ref above)) { configuration.AboveLevelColor = new(above, 1); save(); }
            var range = configuration.MarkerRange;
            ImGui.SetNextItemWidth(190);
            if (ImGui.SliderFloat("Marker range (yalms)", ref range, 10, 100, "%.0f"))
            {
                configuration.MarkerRange = range;
                save();
            }
            Tip("Maximum distance for nearby labels. Each beast name counts once, even when several of the same beast are nearby.");
            if (showMarkers) ImGui.TextWrapped(markerStatus());
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Chat"))
        {
            DrawChatSettings();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Appearance"))
        {
            DrawAppearance();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Privacy"))
        {
            ImGui.TextWrapped("Optional reports help TheKHD5 understand plugin adoption and which versions need support. Only the maintainer can view the usage statistics.");
            ImGui.Spacing();
            ImGui.TextWrapped("Sends a random installation ID and plugin version about every five minutes while the plugin is loaded. The server records receipt times. No character, account, world, location, inventory or combat data is sent.");
            ImGui.Spacing();
            var reporting = configuration.UsageReporting.Enabled;
            if (ImGui.Checkbox("Share optional usage statistics", ref reporting))
            {
                configuration.UsageReporting.Enabled = reporting;
                save();
            }
            Tip("Off by default. Turning this off stops future check-ins; a request already in flight may still arrive. Reporting never controls plugin features.");
            if (ImGui.Button("Reset reporting ID"))
            {
                configuration.UsageReporting.ResetIdentity();
                save();
            }
            Tip("Creates a new random ID locally. It is not derived from your character or hardware. Earlier records remain until they expire and may count separately.");
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawAppearance()
    {
        var appearance = configuration.Appearance;
        var enabled = appearance.Enabled;
        if (ImGui.Checkbox("Use custom theme", ref enabled)) { appearance.Enabled = enabled; save(); }
        Tip("Applies to Bestiary Nav settings, collection, and the anchored toolbar. Changes appear immediately and save automatically. Turn off to use your Dalamud style. Capture-label colors stay independent in Labels.");
        ImGui.BeginDisabled(!enabled);
        ImGui.TextDisabled("STYLE");
        foreach (var style in ThemeCatalog.Styles)
        {
            if (style != ThemeCatalog.Styles[0]) ImGui.SameLine();
            if (ImGui.RadioButton(style.ToString(), appearance.Style == style)) { appearance.Style = style; save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(ThemeCatalog.Description(style));
        }
        ImGui.Spacing();
        ImGui.TextDisabled("COLOR PALETTE");
        var paletteColumns = ImGui.GetContentRegionAvail().X < 460 * ImGuiHelpers.GlobalScale ? 1 : 2;
        if (ImGui.BeginTable("Palettes", paletteColumns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var palette in ThemeCatalog.Palettes)
            {
                ImGui.TableNextColumn();
                ImGui.PushID((int)palette);
                var selected = appearance.Palette == palette;
                var colors = ThemeCatalog.Palette(palette);
                if (ImGui.Selectable(palette.ToString(), selected, ImGuiSelectableFlags.None, new Vector2(100 * ImGuiHelpers.GlobalScale, 0)))
                { appearance.Palette = palette; save(); }
                ImGui.SameLine();
                PaletteSwatch("Background", colors.Background, palette);
                ImGui.SameLine();
                PaletteSwatch("Controls", colors.Surface, palette);
                ImGui.SameLine();
                PaletteSwatch("Accent", colors.Accent, palette);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
        var opacity = appearance.Opacity;
        ImGui.SetNextItemWidth(190 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Window opacity", ref opacity, .8f, 1, "%.2f")) { appearance.Opacity = opacity; save(); }
        Tip("Adjusts the window and toolbar backgrounds. Text and controls remain fully visible.");
        ImGui.EndDisabled();
        if (ImGui.Button("Reset appearance")) { configuration.Appearance = new(); save(); }
        Tip("Restores Modern, Midnight, and 97% opacity. Navigation, labels, and chat preferences are preserved.");
    }

    private void PaletteSwatch(string label, Vector4 color, UiPalette palette)
    {
        if (ImGui.ColorButton(label, color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop,
                new Vector2(16 * ImGuiHelpers.GlobalScale)))
        { configuration.Appearance.Palette = palette; save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{palette} · {label}");
    }

    private void DrawChatSettings()
    {
        var chat = configuration.ChatOutput;
        var enabled = chat.PrintInChat;
        if (ImGui.Checkbox("Print in chat", ref enabled)) { chat.PrintInChat = enabled; save(); }
        Tip("Turn off every Bestiary Nav chat message, including command replies and diagnostics. Logging and Copy diagnostics remain available.");
        ImGui.BeginDisabled(!enabled);
        var channel = chat.DefaultChannel;
        if (ChannelCombo("Default channel", ref channel, false)) { chat.DefaultChannel = channel; save(); }
        Tip("Messages appear only in your own chat log, even when Party or Free Company is selected. Configure which chat tabs display that channel in the game's Chat Log Settings.");
        var narrow = ImGui.GetContentRegionAvail().X < 520 * ImGuiHelpers.GlobalScale;
        if (ImGui.BeginTable("ChatTypes", narrow ? 1 : 2, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Messages", narrow ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed,
                narrow ? 0 : 280 * ImGuiHelpers.GlobalScale);
            if (!narrow) ImGui.TableSetupColumn("Channel", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            ChatRow(ChatMessageKind.Locations, "Locations and duties", "Beast name, capture target, location notes, and duty guidance when selecting an entry.");
            ChatRow(ChatMessageKind.QuestGuidance, "Quest guidance", "Acquisition instructions for beasts with no map or duty destination.");
            ChatRow(ChatMessageKind.TravelProgress, "Travel progress", "Teleporting, loading the mesh, mounting, finding a route, and starting movement. Prints only when the status changes.");
            ChatRow(ChatMessageKind.TravelResults, "Travel results and problems", "Arrival, cancellation, interrupted travel, missing dependencies, and route failures. Travel status remains available in settings diagnostics.");
            ChatRow(ChatMessageKind.Warnings, "Navigation warnings", "Map, duty selection, Bestiary availability, and compatibility errors.");
            ChatRow(ChatMessageKind.Commands, "Command replies and help", "Command usage, unknown beast names, and /bnav next results.");
            ChatRow(ChatMessageKind.Diagnostics, "Diagnostic output", "Detailed output requested with /bnav diagnose or /bnav probe. Does not enable background chat spam.");
            ImGui.EndTable();
        }
        ImGui.EndDisabled();
        if (ImGui.CollapsingHeader("Latest message")) ImGui.TextWrapped(lastNotification());
    }

    private void ChatRow(ChatMessageKind kind, string label, string tip)
    {
        var rule = configuration.ChatOutput.Rules[kind];
        ImGui.PushID((int)kind);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var enabled = rule.Enabled;
        if (ImGui.Checkbox(label, ref enabled)) { rule.Enabled = enabled; save(); }
        Tip(tip);
        if (ImGui.TableGetColumnCount() == 1) ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.BeginDisabled(!enabled);
        var channel = rule.Channel;
        if (ChannelCombo("##Channel", ref channel, true)) { rule.Channel = channel; save(); }
        ImGui.EndDisabled();
        ImGui.PopID();
    }

    private static bool ChannelCombo(string label, ref string channel, bool inherit)
    {
        ImGui.SetNextItemWidth(190 * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginCombo(label, ChatPreferences.ChannelLabel(channel))) return false;
        var changed = false;
        if (inherit && ImGui.Selectable("Default channel", channel.Length == 0)) { channel = ""; changed = true; }
        foreach (var option in ChatPreferences.Channels)
            if (ImGui.Selectable(option.Label, channel == option.Id)) { channel = option.Id; changed = true; }
        ImGui.EndCombo();
        return changed;
    }

    private void DrawFarmingGroups()
    {
        ImGui.TextUnformatted("Patrol areas / monsters");
        Tip("Expand a zone to choose patrol locations and target species together. Location checkboxes choose where to travel; species checkboxes choose what to fight. The species list shows known zone enemies whose level ranges overlap yours; actual levels, patrol bounds and FATE/NM rules still govern pulls. Species may spawn elsewhere in the zone or only during events. Changing choices stops Levelling.");
        var groups = farming.GroupChoices();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##FarmingGroup", farming.SelectedGroupLabel, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.Selectable("Automatic — choose patrol areas", configuration.Farming.SelectedGroups.Count == 0))
                SelectGroup("", false);
            foreach (var category in FarmingPolicy.Categories(groups))
            {
                var active = category.Areas.Any(a => configuration.Farming.SelectedGroups.Contains(a.Key));
                ImGui.SetNextItemOpen(active, ImGuiCond.Appearing);
                if (!ImGui.TreeNode(category.Name + "###PatrolZone" + category.TerritoryId)) continue;
                ImGui.TextDisabled("Patrol locations");
                foreach (var group in category.Areas)
                {
                    var selected = configuration.Farming.SelectedGroups.Contains(group.Key);
                    if (ImGui.Checkbox(group.PatrolLabel + "###" + group.Key, ref selected)) SelectGroup(group.Key, selected);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Catalog anchor: {group.Name}. Every allowed species in range can be targeted around this point.");
                }
                var species = farming.EligibleGroupSpecies(category.TerritoryId);
                ImGui.TextDisabled($"Monsters matching your level range ({species.Count}, zone-wide)");
                foreach (var mob in species)
                {
                    var target = configuration.Farming.AllowsTarget(category.TerritoryId, mob.NameId);
                    if (ImGui.Checkbox($"{mob.Name} · {mob.Levels}###GroupSpecies{category.TerritoryId}/{mob.NameId}", ref target))
                        EditFarmingTargets(category.TerritoryId, f => f.Set(mob.NameId, target));
                }
                if (species.Count == 0) ImGui.TextDisabled("No known species match. The full species editor remains below.");
                ImGui.TreePop();
            }
            if (groups.Count == 0) ImGui.TextDisabled("No documented groups match. Equip BST or adjust the range.");
            // Keep saved choices removable even after a level-up hides them from the matching list.
            foreach (var key in configuration.Farming.SelectedGroups.ToArray())
            {
                if (System.Linq.Enumerable.Any(groups, g => g.Key == key)) continue;
                var selected = true;
                if (ImGui.Checkbox(farming.SavedGroupLabel(key) + " (outside range / unavailable)###" + key, ref selected))
                    SelectGroup(key, selected);
            }
            ImGui.EndCombo();
        }
        if (configuration.Farming.SelectedGroups.Count > 0 &&
            !System.Linq.Enumerable.Any(groups, g => configuration.Farming.SelectedGroups.Contains(g.Key)))
            ImGui.TextWrapped("No selected group overlaps the current range. Select more groups or Automatic.");
    }

    private void SelectGroup(string key, bool selected)
    {
        if (key.Length == 0 && configuration.Farming.SelectedGroups.Count == 0) return;
        if (farming.Enabled) setFarming(false);
        if (key.Length == 0) configuration.Farming.SelectedGroups.Clear();
        else if (selected && !configuration.Farming.SelectedGroups.Contains(key)) configuration.Farming.SelectedGroups.Add(key);
        else if (!selected) configuration.Farming.SelectedGroups.Remove(key);
        configuration.Farming.SelectedGroup = "";
        save();
    }

    private void DrawFarmingPatrols()
    {
        if (!ImGui.TreeNode("Custom patrol routines")) return;
        Tip("Create a routine, walk to each location and Register checkpoint. All checkpoints must be in one overworld zone. Selected routines replace the group patrol, loop in order, and use ground paths with recorded height. At each checkpoint, fight eligible enemies within the search radius, then continue. Level range, Target/Ignore choices, healing and target-level stop still apply. FATE detours are disabled. Editing stops Levelling; enable it after recording. /bnav stop cancels.");
        var options = configuration.Farming;
        var selected = options.Patrols.FirstOrDefault(p => p.Id == options.SelectedPatrol);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##PatrolRoutine", selected?.Name ?? (options.SelectedPatrol.Length == 0 ? "Use catalog patrols" : "Saved routine unavailable")))
        {
            if (ImGui.Selectable("Use catalog patrols", options.SelectedPatrol.Length == 0))
            { StopForPatrolEdit(); options.SelectedPatrol = ""; save(); }
            foreach (var routine in options.Patrols)
                if (ImGui.Selectable(routine.Name + "##" + routine.Id, options.SelectedPatrol == routine.Id))
                { StopForPatrolEdit(); options.SelectedPatrol = routine.Id; save(); }
            ImGui.EndCombo();
        }
        ImGui.InputTextWithHint("##NewPatrolName", "Routine name…", ref patrolName, 80);
        if (ImGui.Button("Create patrol routine"))
        {
            StopForPatrolEdit();
            var routine = new FarmingPatrol { Name = string.IsNullOrWhiteSpace(patrolName) ? $"Patrol {options.Patrols.Count + 1}" : patrolName.Trim() };
            options.Patrols.Add(routine); options.SelectedPatrol = routine.Id; patrolName = "";
            patrolEditStatus = "Walk to your first location and register a checkpoint."; save();
        }
        selected = options.Patrols.FirstOrDefault(p => p.Id == options.SelectedPatrol);
        if (selected != null)
        {
            var name = selected.Name;
            if (ImGui.InputText("Routine name", ref name, 80)) { StopForPatrolEdit(); selected.Name = name; save(); }
            var radius = selected.SearchRadius;
            if (ImGui.SliderFloat("Checkpoint search radius", ref radius, 10, 100, "%.0f yalms"))
            { StopForPatrolEdit(); selected.SearchRadius = radius; save(); }
            if (ImGui.Button("Register checkpoint"))
            { StopForPatrolEdit(); patrolEditStatus = farming.RegisterCheckpoint(selected); save(); }
            ImGui.SameLine();
            if (ImGui.Button("Delete routine"))
            { StopForPatrolEdit(); options.Patrols.Remove(selected); options.SelectedPatrol = ""; patrolEditStatus = "Routine deleted."; save(); ImGui.TreePop(); return; }
            var zone = farming.TargetZones().FirstOrDefault(z => z.TerritoryId == selected.TerritoryId)?.Name ?? "Zone assigned at first checkpoint";
            ImGui.TextWrapped($"{zone} · {selected.Checkpoints.Count} checkpoints · loops in order");
            if (ImGui.BeginChild("##PatrolCheckpoints", new Vector2(0, 180 * ImGuiHelpers.GlobalScale)))
            {
                for (var i = 0; i < selected.Checkpoints.Count; i++)
                {
                    var point = selected.Checkpoints[i];
                    ImGui.PushID(i);
                    ImGui.TextUnformatted($"{i + 1}. X:{point.MapX:0.1}, Y:{point.MapY:0.1} · height {point.Y:0.1}");
                    ImGui.BeginDisabled(i == 0);
                    var up = ImGui.SmallButton("Up");
                    ImGui.EndDisabled(); ImGui.SameLine();
                    ImGui.BeginDisabled(i == selected.Checkpoints.Count - 1);
                    var down = ImGui.SmallButton("Down");
                    ImGui.EndDisabled(); ImGui.SameLine();
                    var remove = ImGui.SmallButton("Remove");
                    ImGui.PopID();
                    if (up || down || remove)
                    {
                        StopForPatrolEdit();
                        if (remove) selected.Checkpoints.RemoveAt(i);
                        else { var next = i + (up ? -1 : 1); (selected.Checkpoints[i], selected.Checkpoints[next]) = (selected.Checkpoints[next], selected.Checkpoints[i]); }
                        save(); break;
                    }
                }
            }
            ImGui.EndChild();
        }
        if (patrolEditStatus.Length > 0) ImGui.TextWrapped(patrolEditStatus);
        ImGui.TreePop();
    }

    private void StopForPatrolEdit()
    {
        if (farming.Enabled) setFarming(false);
    }

    private void DrawFarmingTargets()
    {
        if (!ImGui.TreeNode("Targets in this area")) return;
        Tip("Checked = target; unchecked = ignore. Choices save separately for each zone and apply to new Levelling pulls, including FATEs. All eligible species are targeted by default, regardless of the selected destination's monster name. Actual enemy level, patrol-circle bounds and your FATE/NM options still apply. Defense against attackers continues even for ignored species. Changing targets stops the run; enable Levelling again when finished.");
        var zones = farming.TargetZones();
        if (zones.Count == 0) { ImGui.TextDisabled("Select a patrol area or enter an overworld zone."); ImGui.TreePop(); return; }
        var zone = System.Linq.Enumerable.FirstOrDefault(zones, z => z.TerritoryId == farmingTargetTerritory) ?? zones[0];
        farmingTargetTerritory = zone.TerritoryId;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##TargetZone", zone.Name))
        {
            foreach (var choice in zones)
                if (ImGui.Selectable(choice.Name + "##" + choice.TerritoryId, choice.TerritoryId == farmingTargetTerritory))
                { farmingTargetTerritory = choice.TerritoryId; farmingSpeciesSearch = ""; }
            ImGui.EndCombo();
        }
        // Defer the newly chosen zone to the next frame, rather than editing the old one.
        if (zone.TerritoryId != farmingTargetTerritory) { ImGui.TreePop(); return; }
        if (ImGui.Button("Target all")) EditFarmingTargets(zone.TerritoryId, f => f.SetAll(true));
        ImGui.SameLine();
        if (ImGui.Button("Ignore all")) EditFarmingTargets(zone.TerritoryId, f => f.SetAll(false));
        Tip("Target all also allows newly discovered species. Ignore all excludes new species until checked. Neither button overrides your level range or other combat filters.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SpeciesSearch", "Search species…", ref farmingSpeciesSearch, 128);
        var species = farming.TargetChoices(zone.TerritoryId);
        ImGui.TextDisabled($"{species.Count} known zone species");
        Tip("Includes cataloged ordinary and special/event enemies throughout this zone, not just Bestiary beasts. Live sightings extend the list. Catalog levels are informational; enemies may be outside the patrol circle or not currently spawned. The list is not guaranteed exhaustive.");
        if (ImGui.BeginChild("##FarmingSpecies", new Vector2(0, 200 * ImGuiHelpers.GlobalScale)))
        {
            foreach (var mob in species)
            {
                if (!mob.Name.Contains(farmingSpeciesSearch, StringComparison.OrdinalIgnoreCase)) continue;
                var enabled = configuration.Farming.AllowsTarget(zone.TerritoryId, mob.NameId);
                if (ImGui.Checkbox($"{mob.Name} · {mob.Levels}##{mob.NameId}", ref enabled))
                    EditFarmingTargets(zone.TerritoryId, f => f.Set(mob.NameId, enabled));
            }
        }
        ImGui.EndChild();
        ImGui.TreePop();
    }

    private void EditFarmingTargets(uint territory, Action<FarmingTargetFilter> edit)
    {
        if (farming.Enabled) setFarming(false);
        if (!configuration.Farming.AreaTargets.TryGetValue(territory, out var filter))
            configuration.Farming.AreaTargets[territory] = filter = new();
        edit(filter);
        save();
    }

    private void DrawHealthRecoverySetting()
    {
        var fullHp = configuration.WaitForFullHpBeforeEngaging;
        if (ImGui.Checkbox("Wait for full HP before engaging", ref fullHp))
        { configuration.WaitForFullHpBeforeEngaging = fullHp; save(); }
        Tip("Shared by Capture and Levelling. Wait for 100% of your own HP before pulling another enemy. Pauses movement toward new targets while healing; resumes automatically at full HP. Existing fights and defense against attackers continue. Off keeps the usual behavior, including Levelling's 70% HP minimum.");
    }

    private static void Tip(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(i)");
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    public void Open()
    {
        Flags = ImGuiWindowFlags.HorizontalScrollbar;
        Size = new Vector2(600, 500) * ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 180) * ImGuiHelpers.GlobalScale,
            MaximumSize = new Vector2(float.MaxValue) };
        IsOpen = true;
    }
}
