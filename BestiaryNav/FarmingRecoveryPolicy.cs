using System;

namespace BestiaryNav;

internal static class FarmingRecoveryPolicy
{
    public static bool Incapacitated(bool dead, uint hp) => dead || hp == 0;
    public static bool CanStartRecovery(bool dead, uint hp, bool autoRespawn) =>
        Incapacitated(dead, hp) && autoRespawn;

    // Independent cleanup steps: a lost solver mode must not prevent stopping
    // owned movement, clearing the target, or reaching the Return prompt.
    public static void Cleanup(Action stopMovement, Action releaseRotation, Action clearTarget, Action<Exception> report)
    {
        foreach (var step in new[] { stopMovement, releaseRotation, clearTarget })
            try { step(); } catch (Exception ex) { report(ex); }
    }
}
