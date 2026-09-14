using System;
using System.Text.Json;
using BestiaryNav;

internal static class ChatChecks
{
    public static void Run(Action<bool, string> check)
    {
        var chat = new ChatPreferences();
        check(chat.Route(ChatMessageKind.Locations) == null && chat.Route(ChatMessageKind.TravelProgress) == null
            && chat.Route(ChatMessageKind.TravelResults) == null, "quiet default suppresses routine navigation and travel output");
        check(chat.Route(ChatMessageKind.Warnings) == "Echo" && chat.Route(ChatMessageKind.QuestGuidance) == "Echo",
            "default retains actionable guidance");
        foreach (var kind in Enum.GetValues<ChatMessageKind>()) chat.Rules[kind].Enabled = true;
        chat.DefaultChannel = "Party";
        chat.Rules[ChatMessageKind.Warnings].Channel = "SystemError";
        check(chat.Route(ChatMessageKind.Warnings) == "SystemError" && chat.Route(ChatMessageKind.Commands) == "Party",
            "category channel overrides inherited channel independently");
        chat.Rules[ChatMessageKind.Commands].Enabled = false;
        check(chat.Route(ChatMessageKind.Commands) == null && chat.Route(ChatMessageKind.Diagnostics) == "Party",
            "muting one category leaves other output enabled");
        chat.PrintInChat = false;
        foreach (var kind in Enum.GetValues<ChatMessageKind>())
            check(chat.Route(kind) == null, "master mute covers " + kind);
        var restored = JsonSerializer.Deserialize<ChatPreferences>(JsonSerializer.Serialize(chat))!;
        restored.Normalize();
        check(!restored.PrintInChat && restored.DefaultChannel == "Party" && !restored.Rules[ChatMessageKind.Commands].Enabled,
            "saved mute, channel, and category preferences survive reload");
        restored.PrintInChat = true;
        check(restored.Route(ChatMessageKind.Warnings) == "SystemError", "unmuting restores category override");
        restored.DefaultChannel = "Invalid";
        restored.Rules[ChatMessageKind.Warnings].Channel = "Invalid";
        restored.Rules[ChatMessageKind.Diagnostics] = null!;
        restored.Rules.Remove(ChatMessageKind.Locations);
        restored.Normalize();
        check(restored.Route(ChatMessageKind.Warnings) == "Echo", "invalid channels recover to local Echo");
        check(restored.Route(ChatMessageKind.Diagnostics) == "Echo" && restored.Route(ChatMessageKind.Locations) == null,
            "missing or null rules get category defaults");
        restored.Rules = null!;
        restored.Normalize();
        check(restored.Rules.Count == Enum.GetValues<ChatMessageKind>().Length, "null saved rule collection recovers");
        check(restored.Route((ChatMessageKind)999) == null, "unknown message category is not printed");
        foreach (var channel in ChatPreferences.Channels)
        {
            restored.DefaultChannel = channel.Id;
            check(restored.Route(ChatMessageKind.Warnings) == channel.Id, "channel preference routes " + channel.Id);
        }
    }
}
