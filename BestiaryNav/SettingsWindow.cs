using System;
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
            var changed = ImGui.SliderInt("Minimum levels above BST", ref minimum, 1, 5);
            ImGui.SetNextItemWidth(150);
            changed |= ImGui.SliderInt("Maximum levels above BST", ref maximum, 1, 5);
            if (changed)
            {
                configuration.Farming.MinimumAbove = minimum; configuration.Farming.MaximumAbove = maximum;
                configuration.Farming.Normalize();
                if (farming.Enabled) setFarming(false);
                save();
            }
            Tip("Offsets always use your current BST level. Setting both to +5 targets exactly five levels above you, including after level-ups. Relocate when the area no longer supports that range. A full patrol with no eligible enemies skips that area for this range until you restart. Changing the range stops the run. Duties are excluded; FATE participation is optional.");
            var goal = configuration.Farming.TargetLevel;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Target BST level (0 = no limit)", ref goal)) { configuration.Farming.TargetLevel = Math.Clamp(goal, 0, 100); save(); }
            Tip("Stops farming as soon as your BST level reaches this value, before selecting another target or destination.");
            var fates = configuration.Farming.ParticipateInFates;
            if (ImGui.Checkbox("Participate in FATEs", ref fates)) { configuration.Farming.ParticipateInFates = fates; save(); }
            Tip("Detour to active combat FATEs in the current territory within the configured level range, fight their enemies, then resume area selection. Does not start NPC conversations or hand in items.");
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
