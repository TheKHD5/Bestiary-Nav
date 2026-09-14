namespace BestiaryNav;

// Damage alone is insufficient: auto-attacks, pets and other players can hide a
// stalled rotation. Track the player's confirmed spell/weaponskill records too.
// Time spent casting, approaching, or waiting for our Capture mark is excluded.
internal sealed class CaptureCombatWatchdog
{
    public const long Delay = 8000;
    public const long SkillDelay = 6000;
    private ulong target;
    private uint previousHp;
    private long nextRecovery;
    private long lastSkill, nextSkillRecovery;
    public int Recoveries { get; private set; }
    public string Reason { get; private set; } = "No recovery needed.";

    public bool Check(ulong id, uint hp, long now, bool ready, long skillStamp)
    {
        if (id == 0 || hp == 0) { Reset(); return false; }
        if (target != id)
        {
            target = id; previousHp = hp; nextRecovery = now + Delay;
            lastSkill = skillStamp; nextSkillRecovery = now + SkillDelay; Recoveries = 0;
        }
        if (!ready) { Pause(now); lastSkill = skillStamp; }
        if (hp < previousHp) nextRecovery = now + Delay;
        // Off clears RSR's records; an empty history is not a used skill.
        if (skillStamp != 0 && skillStamp != lastSkill) nextSkillRecovery = now + SkillDelay;
        lastSkill = skillStamp;
        previousHp = hp;
        if (!ready || (now < nextRecovery && now < nextSkillRecovery)) return false;
        Reason = now >= nextSkillRecovery ? "No player spell/weaponskill for 6s" : "No damage for 8s";
        Pause(now); Recoveries++;
        return true;
    }

    public void Pause(long now) { nextRecovery = now + Delay; nextSkillRecovery = now + SkillDelay; }
    public void Reset()
    {
        target = 0; previousHp = 0; nextRecovery = lastSkill = nextSkillRecovery = 0;
        Recoveries = 0; Reason = "No recovery needed.";
    }
}
