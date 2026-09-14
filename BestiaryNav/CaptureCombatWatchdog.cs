namespace BestiaryNav;

// Recovery is based on observable damage, not just the dependency's enabled flag.
// Time spent casting, approaching, or waiting for our Capture mark is excluded.
internal sealed class CaptureCombatWatchdog
{
    public const long Delay = 8000;
    private ulong target;
    private uint previousHp;
    private long nextRecovery;
    public int Recoveries { get; private set; }

    public bool Check(ulong id, uint hp, long now, bool ready)
    {
        if (id == 0 || hp == 0) { Reset(); return false; }
        if (target != id)
        { target = id; previousHp = hp; nextRecovery = now + Delay; Recoveries = 0; }
        if (!ready || hp < previousHp) nextRecovery = now + Delay;
        previousHp = hp;
        if (!ready || now < nextRecovery) return false;
        nextRecovery = now + Delay; Recoveries++;
        return true;
    }

    public void Pause(long now) => nextRecovery = now + Delay;
    public void Reset() { target = 0; previousHp = 0; nextRecovery = 0; Recoveries = 0; }
}
