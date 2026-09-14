using System;
using BestiaryNav;

internal static class CaptureCombatChecks
{
    public static void Run(Action<bool, string> check)
    {
        var watch = new CaptureCombatWatchdog();
        check(!watch.Check(1, 100, 0, true, 10), "new target gets time for normal rotation");
        check(!watch.Check(1, 90, 3000, true, 10), "auto-attack damage does not trigger early recovery");
        check(!watch.Check(1, 80, 5999, true, 10), "six-second skill grace period");
        check(watch.Check(1, 70, 6000, true, 10), "pet or auto-attack damage cannot conceal missing weaponskills");
        check(watch.Reason == "No player spell/weaponskill for 6s", "reports the actual stalled-skill check");
        check(!watch.Check(1, 60, 6001, true, 10), "recovery is not repeated every frame");
        check(!watch.Check(1, 60, 9000, true, 20), "confirmed skill acknowledges progress");
        check(!watch.Check(1, 50, 14999, true, 20), "new skill restarts its observation window");
        check(watch.Check(1, 40, 15000, true, 20), "later stall is recoverable despite more background damage");
        check(!watch.Check(1, 40, 21000, false, 20), "casting or an unavailable mark blocks recovery");
        check(!watch.Check(1, 40, 26999, true, 20), "resume after casting gets a fresh window");
        check(watch.Check(1, 40, 27000, true, 20), "recovery resumes after cast ends");
        watch.Pause(30000);
        check(!watch.Check(1, 40, 35999, true, 20), "approach or mark-loss pause postpones recovery");
        check(watch.Check(1, 40, 36000, true, 20), "approach pause is bounded after returning to range");
        check(!watch.Check(1, 0, 37000, true, 20), "defeated target cannot receive a recovery strike");
        check(!watch.Check(2, 100, 38000, true, 20) && watch.Recoveries == 0, "second target has independent progress state");
        check(watch.Check(2, 90, 44000, true, 20), "second-target-only stall triggers recovery");
        check(!watch.Check(0, 100, 50000, true, 20), "missing target cancels watchdog");
        watch.Check(3, 100, 51000, true, 20); watch.Reset();
        check(!watch.Check(3, 100, 100000, true, 20), "stop discards overdue recovery");

        watch.Reset(); watch.Check(1, 100, 0, true, 0);
        for (var now = 2000; now <= 20000; now += 2000)
            check(!watch.Check(1, (uint)(100 - now / 1000), now, true, now), "repeated same skill with a new use timestamp keeps combat healthy");
        watch.Reset(); watch.Check(1, 100, 0, true, 10);
        watch.Check(1, 100, 4000, true, 20);
        check(watch.Check(1, 100, 8000, true, 30) && watch.Reason == "No damage for 8s", "no-damage recovery remains active even with skill records");
        watch.Reset(); watch.Check(1, 100, 0, true, 10);
        watch.Check(1, 90, 3000, true, 0);
        check(watch.Check(1, 80, 6000, true, 0), "cleared RSR history does not count as using a skill");
        watch.Reset(); watch.Check(1, 100, 0, false, 10);
        watch.Check(1, 90, 6000, false, 20);
        check(!watch.Check(1, 80, 11999, true, 20), "skills seen while paused do not cause an immediate recovery");

        check(BasicComboPolicy.Next(false, 1, 1, 2, 50, 4, 26) == 0, "expired combo starts at Smash Axe");
        check(BasicComboPolicy.Next(true, 1, 1, 2, 50, 4, 26) == 1, "recovery continues from Smash Axe to Axeblade Bite");
        check(BasicComboPolicy.Next(true, 2, 1, 2, 50, 4, 26) == 2, "recovery continues from Axeblade Bite to Shieldsplitter");
        check(BasicComboPolicy.Next(true, 3, 1, 2, 50, 4, 26) == 0, "completed combo restarts normally");
        check(BasicComboPolicy.Next(true, 1, 1, 2, 3, 4, 26) == 0, "unlearned second step is excluded");
        check(BasicComboPolicy.Next(true, 2, 1, 2, 25, 4, 26) == 0, "unlearned finisher is excluded");
        check(BasicComboPolicy.Next(true, 999, 1, 2, 50, 4, 26) == 0, "unrelated action cannot advance the basic combo");
    }
}
