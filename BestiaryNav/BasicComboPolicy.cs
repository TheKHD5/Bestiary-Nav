namespace BestiaryNav;

internal static class BasicComboPolicy
{
    // Only advance an unexpired, learned combo. Never fall back to the starter
    // merely because the correct next step is temporarily on cooldown.
    public static int Next(bool active, uint last, uint starter, uint second,
        int level, int secondLevel, int thirdLevel) => !active ? 0 :
        last == starter && level >= secondLevel ? 1 :
        last == second && level >= thirdLevel ? 2 : 0;
}
