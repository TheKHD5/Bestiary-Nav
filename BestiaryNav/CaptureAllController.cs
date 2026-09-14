using System;
using System.Collections.Generic;
using System.Linq;

namespace BestiaryNav;

// Session-only orchestration. Each attempt uses the existing single-entry runner;
// no target selection, native actions, or rotation ownership live here.
internal sealed class CaptureAllController
{
    private readonly Dictionary<uint, (int Failures, long ReadyAt)> retries = [];
    private long settledAt;
    private long nextAttempt;
    public bool Enabled { get; private set; }
    public uint Current { get; private set; }
    public uint Selected { get; private set; }
    public string Status { get; private set; } = "Capture all is off.";
    public int Failures(uint number) => retries.GetValueOrDefault(number).Failures;

    public void Start()
    {
        retries.Clear(); Current = Selected = 0; settledAt = nextAttempt = 0;
        Enabled = true; Status = "Preparing to capture eligible overworld beasts…";
    }

    public void Stop(string reason = "Capture all stopped.")
    {
        Enabled = false; Current = Selected = 0; Status = reason;
    }

    public uint? Update(long now, CollectionSnapshot state, IEnumerable<MonsterEntry> beasts,
        IReadOnlyDictionary<uint, BeastAcquisition> metadata, bool busy, string? waitReason, string attemptStatus)
    {
        if (!Enabled) return null;
        if (busy) { settledAt = 0; Status = attemptStatus; return null; }
        if (Current != 0)
        {
            // Allow the capture pop-up and the cached collection snapshot to settle,
            // including when Start failed synchronously before the runner became active.
            if (settledAt == 0) settledAt = now + CaptureRetryGate.MinimumDelay;
            if (now < settledAt || !state.Ready)
            { Status = "Waiting for the capture result…"; return null; }
            if (CaptureRules.IsUncaptured(state.Captured, Current))
            {
                var count = Math.Min(Failures(Current), int.MaxValue - 1) + 1;
                var delay = 15000L << Math.Min(count - 1, 3);
                retries[Current] = (count, now + delay);
            }
            else retries.Remove(Current);
            Current = 0; settledAt = 0;
        }
        if (waitReason != null) { Status = waitReason; return null; }
        if (!state.Ready || state.Level == 0 || state.Territory == 0)
        { Status = "Waiting for your level and Bestiary capture records…"; return null; }
        var eligible = beasts.Where(b => b.NavigationKind == "map" && b.Locations.Count != 0 &&
            CaptureRules.IsUncaptured(state.Captured, b.BestiaryNumber) &&
            metadata.TryGetValue(b.BestiaryNumber, out var info) && info.MinimumLevel <= state.Level).ToArray();
        if (eligible.Length == 0)
        {
            Stop("Complete: no uncaptured overworld beasts available at your current level.");
            return null;
        }
        if (now < nextAttempt) { Status = "Waiting before the next capture attempt…"; return null; }
        // Keep the selected entry through failed attempts. A cooldown must not
        // send the player to another beast while this one remains uncaptured.
        var selected = eligible.FirstOrDefault(b => b.BestiaryNumber == Selected);
        if (selected == null) Selected = 0; // captured, or no longer level-eligible
        var choices = selected == null ? eligible : new[] { selected };
        var next = CollectionPlanner.Recommend(choices.Where(b => retries.GetValueOrDefault(b.BestiaryNumber).ReadyAt <= now), metadata, state);
        if (next == null)
        {
            var seconds = Math.Max(1, (choices.Min(b => retries.GetValueOrDefault(b.BestiaryNumber).ReadyAt) - now + 999) / 1000);
            Status = $"Retrying #{Selected} in {seconds}s. Last attempt: {attemptStatus}";
            return null;
        }
        Current = Selected = next.BestiaryNumber;
        nextAttempt = now + CaptureRetryGate.MinimumDelay;
        Status = $"Starting #{Current} {next.DisplayName}…";
        return Current;
    }
}
