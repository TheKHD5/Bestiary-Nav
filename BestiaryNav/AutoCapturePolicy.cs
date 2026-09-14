using System;
using System.Collections.Generic;

namespace BestiaryNav;

internal readonly record struct CaptureSnapshot(bool Enabled, bool Compatible, bool IsBeastmaster, bool PlayerReady,
    bool InCombat, bool TargetInCombat, bool KnownUncaptured, bool TargetAlive, bool Targetable,
    byte PlayerLevel, byte TargetLevel, uint CurrentHp, uint MaxHp, int MaximumHpPercent,
    bool HasCaptureInProgress, bool ActionReady, ulong TargetId);

internal sealed class AutoCapturePolicy
{
    private long nextAttempt;

    public static bool Eligible(CaptureSnapshot s) => s.Enabled && s.Compatible && s.IsBeastmaster && s.PlayerReady &&
        s.InCombat && s.TargetInCombat && s.KnownUncaptured && s.TargetAlive && s.Targetable &&
        s.PlayerLevel > 0 && s.TargetLevel > 0 && s.TargetLevel <= s.PlayerLevel &&
        s.MaxHp > 0 && s.CurrentHp > 0 && s.CurrentHp <= s.MaxHp && s.MaximumHpPercent is >= 1 and <= 100 &&
        (double)s.CurrentHp * 100 <= (double)s.MaxHp * s.MaximumHpPercent &&
        !s.HasCaptureInProgress && s.ActionReady && s.TargetId != 0 && s.TargetId != 0xE0000000;

    public bool TryCapture(CaptureSnapshot state, long now, Func<ulong, bool> cast)
    {
        if (now < nextAttempt || !Eligible(state)) return false;
        // Reserve before calling native code. Target switches do not bypass the
        // grace period while the server applies the effect to the previous target.
        nextAttempt = now + 1000;
        if (!cast(state.TargetId)) return false;
        nextAttempt = now + 5000;
        return true;
    }

    public void Reset() => nextAttempt = 0;
}

internal sealed class CaptureMarkTracker
{
    private readonly Dictionary<uint, long> markedUntil = [];
    private readonly HashSet<uint> observed = [];
    private bool anyOwnedMark, hasObservedMark;
    public static bool IsOwnMark(uint statusId, uint sourceId, uint playerId) =>
        statusId == 4626 && playerId != 0 && sourceId == playerId;
    public void BeginScan() { observed.Clear(); anyOwnedMark = false; }
    public void Observe(uint entityId, bool alive, float? remaining, long now)
    {
        observed.Add(entityId);
        if (!alive || remaining == null) { markedUntil.Remove(entityId); return; }
        anyOwnedMark = hasObservedMark = true;
        var duration = float.IsFinite(remaining.Value) ? Math.Clamp(remaining.Value, 1, 120) : 120;
        markedUntil[entityId] = now + (long)(duration * 1000);
    }
    public bool HasActiveMark(bool playerBuff, long now)
    {
        var unseenMark = false;
        foreach (var mark in markedUntil)
        {
            // Dictionary removal during enumeration is supported on .NET 10.
            if (mark.Value <= now) markedUntil.Remove(mark.Key);
            else if (!observed.Contains(mark.Key)) unseenMark = true;
        }
        if (!playerBuff && !anyOwnedMark && !unseenMark) hasObservedMark = false;
        return anyOwnedMark || unseenMark || (playerBuff && !hasObservedMark);
    }
    public void Reset() { markedUntil.Clear(); observed.Clear(); anyOwnedMark = hasObservedMark = false; }
}
