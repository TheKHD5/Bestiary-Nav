using System;
using BestiaryNav;

internal static class AutoCaptureChecks
{
    public static void Run(Action<bool, string> check)
    {
        var valid = new CaptureSnapshot(true, true, true, true, true, true, true, true, true,
            30, 29, 100, 100, 100, false, true, 123);
        check(AutoCapturePolicy.Eligible(valid), "eligible BST combat target can be captured at configured HP");
        var rejected = new[] {
            valid with { Enabled = false }, valid with { Compatible = false }, valid with { IsBeastmaster = false },
            valid with { PlayerReady = false }, valid with { InCombat = false }, valid with { TargetInCombat = false },
            valid with { KnownUncaptured = false }, valid with { TargetAlive = false }, valid with { Targetable = false },
            valid with { PlayerLevel = 0 }, valid with { TargetLevel = 0 }, valid with { TargetLevel = 31 },
            valid with { CurrentHp = 0 }, valid with { MaxHp = 0 }, valid with { CurrentHp = 101 },
            valid with { MaximumHpPercent = 0 }, valid with { MaximumHpPercent = 101 }, valid with { MaximumHpPercent = 25 },
            valid with { HasCaptureInProgress = true }, valid with { ActionReady = false },
            valid with { TargetId = 0 }, valid with { TargetId = 0xE0000000 },
        };
        for (var i = 0; i < rejected.Length; i++) check(!AutoCapturePolicy.Eligible(rejected[i]), $"auto capture guard {i}");
        check(AutoCapturePolicy.Eligible(valid with { TargetLevel = 30 }), "equal-level beasts are eligible");
        check(AutoCapturePolicy.Eligible(valid with { CurrentHp = 25, MaximumHpPercent = 25 }), "HP threshold is inclusive");
        check(AutoCapturePolicy.Eligible(valid with { CurrentHp = uint.MaxValue, MaxHp = uint.MaxValue }), "large boss HP avoids integer overflow");
        var policy = new AutoCapturePolicy();
        var calls = 0; ulong castTarget = 0;
        bool Cast(ulong target) { calls++; castTarget = target; return true; }
        check(policy.TryCapture(valid, 0, Cast) && calls == 1 && castTarget == 123, "capture uses precisely the supplied main target");
        check(!policy.TryCapture(valid, 250, Cast) && calls == 1, "server acknowledgement delay does not cause a duplicate");
        check(!policy.TryCapture(valid with { TargetId = 456 }, 2500, Cast), "switching targets cannot bypass acknowledgement delay");
        check(!policy.TryCapture(valid with { HasCaptureInProgress = true }, 6000, Cast), "active mark blocks recast after cooldown");
        check(policy.TryCapture(valid, 120000, Cast) && calls == 2, "mark expiration allows a new cast on the same living target");
        check(!policy.TryCapture(valid with { TargetAlive = false }, 240000, Cast), "dead target is never recaptured");
        check(!policy.TryCapture(valid with { KnownUncaptured = false }, 240000, Cast), "newly recorded beast stops automatic capture");
        policy.Reset();
        check(!policy.TryCapture(valid, 0, _ => { calls++; return false; }), "native rejection is not treated as a successful cast");
        var before = calls;
        check(!policy.TryCapture(valid, 500, Cast) && calls == before, "rejected calls are throttled");
        check(policy.TryCapture(valid, 1000, Cast), "rejected call can retry after bounded backoff");
        check(CaptureMarkTracker.IsOwnMark(4626, 99, 99) && !CaptureMarkTracker.IsOwnMark(4626, 88, 99) &&
            !CaptureMarkTracker.IsOwnMark(4624, 99, 99), "capture status checks its ID and player ownership");
        var marks = new CaptureMarkTracker();
        marks.BeginScan();
        check(marks.HasActiveMark(true, 0), "existing player capture buff protects an unseen target on enable");
        marks.Observe(123, true, 120, 0);
        check(marks.HasActiveMark(true, 0), "main-target mark blocks capture");
        marks.BeginScan(); marks.Observe(123, true, null, 1000); marks.Observe(456, true, 119, 1000);
        check(marks.HasActiveMark(true, 1000), "another marked actor blocks recast even after the main target's mark ends");
        marks.BeginScan(); marks.Observe(123, true, null, 2000); marks.Observe(456, true, null, 2000);
        check(!marks.HasActiveMark(true, 2000), "visible mark removal allows retry despite a lingering player buff");
        marks.BeginScan(); marks.Observe(456, true, 5, 3000); marks.HasActiveMark(true, 3000);
        marks.BeginScan();
        check(marks.HasActiveMark(true, 4000), "marked actor leaving the object table keeps its remaining lifetime");
        check(!marks.HasActiveMark(false, 8000), "unseen mark lease expires instead of blocking forever");
        marks.BeginScan(); marks.Observe(123, true, 120, 9000); marks.HasActiveMark(true, 9000);
        marks.BeginScan(); marks.Observe(123, false, 119, 10000);
        check(!marks.HasActiveMark(true, 10000), "dead marked actor no longer occupies capture");
        marks.Reset(); marks.BeginScan();
        check(!marks.HasActiveMark(false, 0), "logout and territory reset clear capture tracking");
    }
}
