using System;
using System.Collections.Generic;
using BestiaryNav;

internal static class RotationSolverChecks
{
    public static void Run(Action<bool, string> check)
    {
        var off = new RotationSolverState(false, false, false, false, false, false, true, false, false);
        var combat = off with { Active = true, Manual = true, Henched = true };
        var auto = off with { Active = true };
        var manual = off with { Active = true, Manual = true };
        void Reject(Action action, string name)
        {
            try { action(); check(false, name); }
            catch (InvalidOperationException) { check(true, name); }
        }
        var backend = new FakeSolver(off);
        var control = new RotationSolverControl(backend);
        control.Acquire(); control.Verify();
        check(backend.Changes.Count == 0 && backend.State.IsOff, "acquire leaves RSR off while traveling and marking");
        control.SetRunning(true);
        check(backend.Changes.Count == 1 && backend.Changes[0] == RotationSolverMode.Capture && backend.State.IsCaptureMode,
            "capture combat selects external-control manual targeting, never Auto");
        control.SetRunning(true);
        check(backend.Changes.Count == 1, "steady combat does not spam mode changes");
        control.SetRunning(false);
        check(backend.State.IsOff, "rotation switches off after defeat or mark loss");
        var gate = new CaptureRetryGate(); gate.Defeated(1000);
        for (var now = 1000; now < 4000; now += 1000)
        {
            if (gate.CanRetry(now, 0, 3, false)) control.SetRunning(true);
            check(backend.State.IsOff, $"RSR stays off throughout three-second result pause at {now}");
        }
        check(!gate.CanRetry(4000, 1UL << 2, 3, false), "late capture prevents RSR from restarting");
        control.SetRunning(true); control.Release();
        check(backend.State.IsOff, "cancel/unload returns our active rotation to Off");
        var changes = backend.Changes.Count; control.Release();
        check(backend.Changes.Count == changes, "duplicate cleanup is inert");
        Reject(() => control.SetRunning(true), "released controller cannot restart actions");

        foreach (var other in new[] { auto, manual, combat, off with { TargetOnly = true }, off with { AutoDuty = true }, off with { Pvp = true } })
        {
            backend = new(other); control = new(backend);
            Reject(control.Acquire, "capture never takes over an already active mode");
            control.Release(); check(backend.Changes.Count == 0, "rejected start leaves prior mode untouched");
        }
        foreach (var unsupported in new[] { off with { BstRotation = false }, off with { Teaching = true }, off with { AutomaticActivation = true } })
        {
            backend = new(unsupported); control = new(backend);
            Reject(control.Acquire, "unsupported rotation/teaching/auto-on settings block capture runs");
            check(backend.Changes.Count == 0, "readiness failure makes no configuration or mode changes");
        }
        foreach (var userMode in new[] { auto, manual, off })
        {
            backend = new(off); control = new(backend); control.Acquire(); control.SetRunning(true);
            backend.State = userMode; changes = backend.Changes.Count;
            Reject(control.Verify, "manual takeover cancels capture control");
            control.Release();
            check(backend.Changes.Count == changes && backend.State == userMode, "stop preserves the user's replacement mode");
        }
        backend = new(off); control = new(backend); control.Acquire(); backend.State = auto;
        Reject(() => control.SetRunning(true), "auto activation while waiting cannot be overwritten at next pull");
        control.Release(); check(backend.Changes.Count == 0, "interrupted wait does not send Off to another controller");

        backend = new(off); control = new(backend); control.Acquire(); control.SetRunning(true);
        backend.State = combat with { BstRotation = false };
        Reject(control.Verify, "changing away from BST interrupts rotation");
        control.Release(); check(backend.State.IsOff, "job change still stops the mode we started");

        backend = new(off) { Available = false }; control = new(backend);
        Reject(control.Acquire, "disabled dependency prevents start");
        backend.Available = true; control.Acquire(); control.SetRunning(true); backend.Available = false;
        Reject(control.Verify, "dependency loss interrupts a capture run");
        changes = backend.Changes.Count; control.Release();
        check(backend.Changes.Count == changes, "no IPC mutation against an unloaded dependency");

        backend = new(off); control = new(backend); control.Acquire();
        backend.State = off with { BstRotation = false };
        control.Verify(requireRotation: false);
        check(backend.Changes.Count == 0, "rotation can reload while teleporting without canceling the trip or starting combat");
        Reject(() => control.SetRunning(true), "combat still waits for BST rotation after travel");
        backend.State = off; control.SetRunning(true);
        check(backend.State.IsCaptureMode, "BST rotation resumes normally after zone load");
        control.Release();

        backend = new(off); control = new(backend); control.Acquire();
        backend.ThrowAfterChange = true;
        Reject(() => control.SetRunning(true), "IPC exceptions surface instead of pretending success");
        backend.ThrowAfterChange = false; control.Release();
        check(backend.State.IsOff, "partially applied activation is stopped during cleanup");

        backend = new(off); control = new(backend); control.Acquire(); control.SetRunning(true);
        backend.IgnoreNextChange = true;
        Reject(() => control.SetRunning(false), "rejected pause is detected through readback");
        control.Release(); check(backend.State.IsOff, "cleanup retries Off when pause did not take effect");

        backend = new(off); control = new(backend); control.Acquire(); control.SetRunning(true);
        var openerCalls = 0;
        control.Restart(() => { check(backend.State.IsOff, "solver paused while the recovery opener is issued"); openerCalls++; });
        check(openerCalls == 1 && backend.State.IsCaptureMode && backend.Changes.Count == 3,
            "stalled rotation refreshes through Off and resumes existing-target mode");
        backend.State = auto;
        Reject(() => control.Restart(() => openerCalls++), "recovery cannot overwrite a user's changed mode");
        check(openerCalls == 1 && backend.State == auto, "manual takeover blocks recovery strike as well as mode changes");
        control.Release();
        backend = new(off); control = new(backend); control.Acquire();
        Reject(() => control.Restart(() => openerCalls++), "no recovery during travel or capture-result pause");
        control.SetRunning(true);
        Reject(() => control.Restart(() => throw new InvalidOperationException("opener unavailable")), "recovery callback failure does not silently resume");
        check(backend.State.IsOff, "failed opener callback leaves solver paused");
        control.Release();
        backend = new(off); control = new(backend); control.Acquire(); control.SetRunning(true);
        backend.State = off; changes = backend.Changes.Count;
        control.Release();
        check(backend.State.IsOff && backend.Changes.Count == changes,
            "death release tolerates RSR already turning itself off without combat verification");
        control.Acquire(); control.SetRunning(true); backend.State = auto;
        changes = backend.Changes.Count; control.Release();
        check(backend.State == auto && backend.Changes.Count == changes,
            "death release never overwrites another controller's replacement mode");
    }

    private sealed class FakeSolver(RotationSolverState initial) : IRotationSolverBackend
    {
        public bool Available { get; set; } = true;
        public RotationSolverState State = initial;
        public List<RotationSolverMode> Changes { get; } = [];
        public bool ThrowAfterChange, IgnoreNextChange;
        public RotationSolverState Read() => State;
        public void ChangeMode(RotationSolverMode mode)
        {
            Changes.Add(mode);
            if (IgnoreNextChange) { IgnoreNextChange = false; return; }
            State = State with { Active = mode == RotationSolverMode.Capture, Manual = mode == RotationSolverMode.Capture,
                Henched = mode == RotationSolverMode.Capture, TargetOnly = false, AutoDuty = false, Pvp = false };
            if (ThrowAfterChange) throw new InvalidOperationException("test callback failed after applying mode");
        }
    }
}
