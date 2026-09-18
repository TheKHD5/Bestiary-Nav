using System;
using BestiaryNav;

internal static class BindingChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string addon = "XBMMonsterNotebook", game = "2026.09.15.0000.0000", dalamud = "15.0.3.5";
        var profile = new BindingProfile { Enabled = true, AddonName = addon,
            VerifiedGameVersion = "2026.09.01.0000.0000", VerifiedDalamudVersion = "15.0.3.4" };
        check(profile.GetIssue(game, dalamud, addon) != null, "old verified profile rejects a new game");
        check(profile.GetIssue(profile.VerifiedGameVersion, dalamud, addon) != null, "old profile rejects a new Dalamud");
        check(profile.GetIssue(profile.VerifiedGameVersion, profile.VerifiedDalamudVersion, addon) == null, "verified exact pair works");
        check(!profile.IsCandidate && profile.Status == "verified" && profile.ValidationNotice == null, "verified profile preserves status");
        profile.CandidateGameVersion = game;
        check(profile.GetIssue(game, dalamud, addon) != null, "partial candidate cannot fall back to verified Dalamud");
        profile.CandidateDalamudVersion = dalamud;
        check(profile.GetIssue(game, dalamud, addon) == null, "audited candidate permits only its exact pair");
        check(profile.IsCandidate && profile.Status != "verified" && profile.ValidationNotice != null, "candidate never claims live verification");
        check(profile.GetIssue(profile.VerifiedGameVersion, profile.VerifiedDalamudVersion, addon) != null, "candidate cannot silently reuse old pair");
        check(profile.GetIssue(game, "15.0.3.6", addon) != null, "candidate cannot admit future Dalamud");
        check(profile.GetIssue("2026.09.16.0000.0000", dalamud, addon) != null, "candidate cannot admit future game");
        profile.AddonName = "wrong";
        check(profile.GetIssue(game, dalamud, addon) != null, "candidate still validates addon");
        profile.AddonName = addon;
        profile.Enabled = false;
        check(profile.GetIssue(game, dalamud, addon) != null, "disabled candidate cannot activate");
        check(new BindingProfile { Enabled = true }.GetIssue("", "", addon) != null, "empty profile cannot activate");
    }
}
