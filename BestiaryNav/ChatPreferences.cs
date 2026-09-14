using System;
using System.Collections.Generic;

namespace BestiaryNav;

public enum ChatMessageKind { Locations, QuestGuidance, TravelProgress, TravelResults, Warnings, Commands, Diagnostics }

[Serializable]
public sealed class ChatRule
{
    public bool Enabled { get; set; }
    // Empty means inherit the default channel.
    public string Channel { get; set; } = "";
}

[Serializable]
public sealed class ChatPreferences
{
    public bool PrintInChat { get; set; } = true;
    public string DefaultChannel { get; set; } = "Echo";
    public Dictionary<ChatMessageKind, ChatRule> Rules { get; set; } = CreateDefaults();

    public static readonly (string Id, string Label)[] Channels =
    [
        ("Echo", "Echo"), ("SystemMessage", "System messages"), ("SystemError", "System errors"),
        ("Say", "Say"), ("Party", "Party"), ("Alliance", "Alliance"), ("FreeCompany", "Free Company"),
        ("Ls1", "Linkshell 1"), ("Ls2", "Linkshell 2"), ("Ls3", "Linkshell 3"), ("Ls4", "Linkshell 4"),
        ("Ls5", "Linkshell 5"), ("Ls6", "Linkshell 6"), ("Ls7", "Linkshell 7"), ("Ls8", "Linkshell 8"),
        ("CrossLinkShell1", "Cross-world linkshell 1"), ("CrossLinkShell2", "Cross-world linkshell 2"),
        ("CrossLinkShell3", "Cross-world linkshell 3"), ("CrossLinkShell4", "Cross-world linkshell 4"),
        ("CrossLinkShell5", "Cross-world linkshell 5"), ("CrossLinkShell6", "Cross-world linkshell 6"),
        ("CrossLinkShell7", "Cross-world linkshell 7"), ("CrossLinkShell8", "Cross-world linkshell 8"),
    ];

    private static Dictionary<ChatMessageKind, ChatRule> CreateDefaults()
    {
        var rules = new Dictionary<ChatMessageKind, ChatRule>();
        foreach (var kind in Enum.GetValues<ChatMessageKind>())
            rules[kind] = new() { Enabled = kind is ChatMessageKind.QuestGuidance or ChatMessageKind.Warnings
                or ChatMessageKind.Commands or ChatMessageKind.Diagnostics };
        return rules;
    }

    public void Normalize()
    {
        if (!ValidChannel(DefaultChannel)) DefaultChannel = "Echo";
        Rules ??= CreateDefaults();
        foreach (var (kind, fallback) in CreateDefaults())
        {
            if (!Rules.TryGetValue(kind, out var rule) || rule == null) Rules[kind] = fallback;
            else if (!ValidChannel(rule.Channel)) rule.Channel = "";
        }
    }

    public string? Route(ChatMessageKind kind)
    {
        if (!PrintInChat || !Rules.TryGetValue(kind, out var rule) || rule is not { Enabled: true }) return null;
        return ValidChannel(rule.Channel) ? rule.Channel : ValidChannel(DefaultChannel) ? DefaultChannel : "Echo";
    }

    public static bool ValidChannel(string? channel) => Array.Exists(Channels, item => item.Id == channel);
    public static string ChannelLabel(string channel) => Array.Find(Channels, item => item.Id == channel).Label ?? "Default channel";
}
