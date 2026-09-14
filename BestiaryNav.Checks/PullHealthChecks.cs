using System;
using BestiaryNav;

internal static class PullHealthChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var normalThreshold in new[] { 0, 70 })
        {
            foreach (var hp in new uint[] { 1, 69, 70, 99 })
                check(PullHealthPolicy.ShouldWait(true, hp, 100, false, normalThreshold),
                    $"full-HP option blocks a new pull at {hp}% with normal threshold {normalThreshold}");
            check(!PullHealthPolicy.ShouldWait(true, 100, 100, false, normalThreshold), "full HP resumes new pulls");
            check(!PullHealthPolicy.ShouldWait(true, 101, 100, false, normalThreshold), "HP above a changed maximum does not block");
            check(!PullHealthPolicy.ShouldWait(true, 1, 100, true, normalThreshold), "existing fights and defensive pulls do not wait for regeneration");
            check(PullHealthPolicy.ShouldWait(true, 0, 0, false, normalThreshold), "unavailable max HP cannot authorize a fresh pull");
            check(!PullHealthPolicy.ShouldWait(true, 99999, 99999, false, normalThreshold), "non-round maximum resumes at exactly full HP");
            check(PullHealthPolicy.ShouldWait(true, 99998, 99999, false, normalThreshold), "one missing HP still waits without percent rounding");
        }
        check(!PullHealthPolicy.ShouldWait(false, 1, 100, false), "disabled option preserves Capture's existing behavior");
        check(PullHealthPolicy.ShouldWait(false, 69, 100, false, 70), "disabled option preserves Levelling's 70% minimum");
        check(!PullHealthPolicy.ShouldWait(false, 70, 100, false, 70), "Levelling resumes at its usual minimum when full-HP option is off");
        check(!PullHealthPolicy.ShouldWait(false, 99, 100, false, 70), "turning full-HP off releases a wait above 70%");
        check(!PullHealthPolicy.ShouldWait(false, uint.MaxValue, uint.MaxValue, false, 70), "HP threshold comparison cannot overflow");
        // Heal, take fresh damage before engagement, defend, then heal and resume.
        check(!PullHealthPolicy.ShouldWait(true, 100, 100, false), "initial healing completes");
        check(PullHealthPolicy.ShouldWait(true, 80, 100, false), "pre-pull damage restores the health gate");
        check(!PullHealthPolicy.ShouldWait(true, 80, 100, true), "an attacker interrupts healing for defense");
        check(PullHealthPolicy.ShouldWait(true, 80, 100, false), "after defense the next pull waits again");
        check(!PullHealthPolicy.ShouldWait(true, 100, 100, false), "healing after defense resumes normal pulls");
    }
}
