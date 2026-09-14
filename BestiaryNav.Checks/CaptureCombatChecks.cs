using System;
using BestiaryNav;

internal static class CaptureCombatChecks
{
    public static void Run(Action<bool, string> check)
    {
        var watch = new CaptureCombatWatchdog();
        check(!watch.Check(1, 100, 0, true), "new marked target gets time for normal rotation");
        check(!watch.Check(1, 100, 7999, true), "no premature recovery");
        check(watch.Check(1, 100, 8000, true), "enabled rotation with no damage triggers recovery");
        check(!watch.Check(1, 100, 8001, true), "recovery cannot spam every frame");
        check(!watch.Check(1, 90, 15000, true), "damage acknowledges recovery progress");
        check(!watch.Check(1, 90, 22999, true), "damage restarts observation window");
        check(watch.Check(1, 90, 23000, true), "later stall is recoverable too");
        check(!watch.Check(1, 90, 31000, false), "casting or an unavailable mark blocks recovery");
        check(!watch.Check(1, 90, 38999, true), "resume from casting gets a fresh window");
        check(watch.Check(1, 90, 39000, true), "recovery resumes after cast ends");
        watch.Pause(46000);
        check(!watch.Check(1, 90, 53999, true), "approach or mark-loss pause postpones recovery");
        check(watch.Check(1, 90, 54000, true), "approach pause is bounded after returning to range");
        check(!watch.Check(1, 0, 55000, true), "defeated target cannot receive a recovery strike");
        check(!watch.Check(2, 100, 56000, true) && watch.Recoveries == 0, "second target has independent progress state");
        check(watch.Check(2, 100, 64000, true), "second-target-only stall triggers recovery");
        check(!watch.Check(0, 100, 72000, true), "missing target cancels watchdog");
        watch.Check(3, 100, 80000, true); watch.Reset();
        check(!watch.Check(3, 100, 100000, true), "stop discards overdue recovery");
    }
}
