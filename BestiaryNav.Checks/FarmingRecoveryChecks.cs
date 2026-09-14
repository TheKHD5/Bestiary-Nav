using System;
using System.Collections.Generic;
using BestiaryNav;

internal static class FarmingRecoveryChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(FarmingRecoveryPolicy.CanStartRecovery(true, 0, true), "Levelling can start while dead with auto respawn enabled");
        check(!FarmingRecoveryPolicy.CanStartRecovery(true, 0, false), "starting dead does not opt into automatic return without the setting");
        check(FarmingRecoveryPolicy.Incapacitated(false, 0), "zero HP enters recovery before the IsDead flag arrives");
        check(FarmingRecoveryPolicy.Incapacitated(true, 1), "death flag also enters recovery independently of HP");
        check(!FarmingRecoveryPolicy.CanStartRecovery(false, 100, true), "living players follow normal start checks");
        check(!FarmingRecoveryPolicy.Incapacitated(false, 100), "living player exits death handling");
        for (var failed = -1; failed < 3; failed++)
        {
            var calls = new List<int>();
            var errors = 0;
            var failure = failed;
            Action Step(int index) => () =>
            {
                calls.Add(index);
                if (failure == index) throw new InvalidOperationException("simulated death cleanup failure");
            };
            FarmingRecoveryPolicy.Cleanup(Step(0), Step(1), Step(2), _ => errors++);
            check(calls.Count == 3 && calls[0] == 0 && calls[1] == 1 && calls[2] == 2,
                $"death cleanup runs movement, rotation and target steps despite failure {failed}");
            check(errors == (failed == -1 ? 0 : 1), "cleanup reports only actual failures without aborting recovery");
        }
        var allErrors = 0;
        void Fail() => throw new InvalidOperationException("dependency unavailable");
        FarmingRecoveryPolicy.Cleanup(Fail, Fail, Fail, _ => allErrors++);
        check(allErrors == 3, "even complete cleanup failure does not throw out of revival handling");
    }
}
