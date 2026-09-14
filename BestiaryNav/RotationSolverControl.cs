using System;

namespace BestiaryNav;

// Verified against RotationSolver.Basic.Data.StateCommandType in 7.5.6.8.
// Henched performs the rotation against the existing main target; Auto retargets.
internal enum RotationSolverMode : byte { Off = 0, Capture = 5 }
internal readonly record struct RotationSolverState(bool Active, bool Manual, bool Henched, bool TargetOnly,
    bool AutoDuty, bool Pvp, bool BstRotation, bool Teaching, bool AutomaticActivation)
{
    public bool IsOff => !Active && !Manual && !Henched && !TargetOnly && !AutoDuty && !Pvp;
    public bool IsCaptureMode => Active && Manual && Henched && !TargetOnly && !AutoDuty && !Pvp;
}

internal interface IRotationSolverBackend
{
    bool Available { get; }
    RotationSolverState Read();
    void ChangeMode(RotationSolverMode mode);
}

// RSR has no lease API. Start only from Off, track the mode we requested, and
// never reassert control or restore an active mode after the user takes over.
internal sealed class RotationSolverControl(IRotationSolverBackend backend)
{
    private bool owned, running;

    public void Acquire()
    {
        if (owned) throw new InvalidOperationException("A capture rotation is already controlled.");
        var state = Read();
        Ready(state);
        if (!state.IsOff) throw new InvalidOperationException("Turn Rotation Solver off before starting a capture run.");
        owned = true; running = false;
    }

    public void Verify() => Verify(true);

    public void Verify(bool requireRotation)
    {
        if (!owned) throw new InvalidOperationException("Rotation Solver capture control was lost.");
        var state = Read();
        if (running ? !state.IsCaptureMode : !state.IsOff)
        {
            owned = false; running = false;
            throw new InvalidOperationException("Rotation Solver's mode changed. Capture run stopped without overriding the new mode.");
        }
        Ready(state, requireRotation);
    }

    public void SetRunning(bool value)
    {
        if (value == running) return;
        Verify();
        // Record intent before IPC so cleanup can stop a change that took effect
        // even if the dependency throws while finishing its callback.
        running = value;
        backend.ChangeMode(value ? RotationSolverMode.Capture : RotationSolverMode.Off);
        var changed = Read();
        if (value ? !changed.IsCaptureMode : !changed.IsOff)
            throw new InvalidOperationException("Rotation Solver did not apply the requested mode. Capture run stopped.");
        Ready(changed);
    }

    public void Release()
    {
        var stop = owned;
        owned = running = false;
        if (stop && backend.Available && backend.Read().IsCaptureMode)
            backend.ChangeMode(RotationSolverMode.Off);
    }

    public void Restart(Action opener)
    {
        // Never reclaim a mode that the user or another plugin changed.
        Verify();
        if (!running) throw new InvalidOperationException("Capture rotation is not running.");
        SetRunning(false);
        Verify();
        opener();
        SetRunning(true);
    }

    private RotationSolverState Read()
    {
        if (!backend.Available) throw new InvalidOperationException("Rotation Solver is disabled or its IPC is unavailable.");
        return backend.Read();
    }

    private static void Ready(RotationSolverState state, bool requireRotation = true)
    {
        if (requireRotation && !state.BstRotation) throw new InvalidOperationException("Select the BST Reborn rotation in Rotation Solver (7.5.6.8 or later).");
        if (state.Teaching) throw new InvalidOperationException("Turn off Rotation Solver's Teaching Mode to allow capture-run combos.");
        if (state.AutomaticActivation)
            throw new InvalidOperationException("Turn off Rotation Solver's Auto On settings so it stays paused between capture attempts.");
    }
}
