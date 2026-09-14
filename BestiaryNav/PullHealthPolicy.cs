namespace BestiaryNav;

internal static class PullHealthPolicy
{
    public static bool ShouldWait(bool fullHp, uint current, uint maximum, bool engaged, int usualMinimumPercent = 0)
    {
        // Never make the player wait for out-of-combat regeneration while fighting.
        if (engaged || (!fullHp && usualMinimumPercent == 0)) return false;
        if (maximum == 0) return true;
        return fullHp ? current < maximum : (ulong)current * 100 < (ulong)maximum * (uint)usualMinimumPercent;
    }
}
