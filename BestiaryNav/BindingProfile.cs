namespace BestiaryNav;

public sealed class BindingProfile
{
    public bool Enabled { get; set; }
    public string VerifiedGameVersion { get; set; } = "";
    public string VerifiedDalamudVersion { get; set; } = "";
    public string AddonName { get; set; } = "";
    public bool BlockInCombat { get; set; } = true;

    // A local test build can target an audited pair without claiming live verification.
    // Publishing rejects these fields until acceptance is recorded and they are removed.
    public string CandidateGameVersion { get; set; } = "";
    public string CandidateDalamudVersion { get; set; } = "";
    public bool IsCandidate => !string.IsNullOrWhiteSpace(CandidateGameVersion) ||
        !string.IsNullOrWhiteSpace(CandidateDalamudVersion);
    public string? ValidationNotice => IsCandidate ? "Compatibility test build: in-game validation is pending." : null;
    public string Status => IsCandidate ? "candidate - live validation pending" : "verified";

    public string? GetIssue(string gameVersion, string dalamudVersion, string expectedAddon)
    {
        if (!Enabled) return "Bestiary integration is disabled in this build.";
        var game = IsCandidate ? CandidateGameVersion : VerifiedGameVersion;
        var dalamud = IsCandidate ? CandidateDalamudVersion : VerifiedDalamudVersion;
        if (string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(dalamud))
            return "Incomplete Bestiary compatibility profile. Update Bestiary Nav.";
        if (gameVersion != game)
            return $"Unsupported FFXIV version {gameVersion}; this build supports {game}. Update Bestiary Nav.";
        if (dalamudVersion != dalamud)
            return $"Unsupported Dalamud version {dalamudVersion}; this build supports {dalamud}. Update Bestiary Nav.";
        return AddonName != expectedAddon ? "Invalid Bestiary addon profile. Update Bestiary Nav." : null;
    }
}
