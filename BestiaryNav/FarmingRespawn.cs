using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BestiaryNav;

internal sealed class FarmingRespawn(IGameGui gui)
{
    private long nextAttempt;
    public string Status { get; private set; } = "No revival requested.";
    public void Reset() { nextAttempt = 0; Status = "Waiting for the Return prompt…"; }
    public unsafe void TryReturn(long now)
    {
        if (now < nextAttempt) return;
        nextAttempt = now + 5000;
        var agent = AgentRevive.Instance();
        var live = gui.GetAddonByName("SelectYesno");
        if (!NativeSnapshot.TryRead<AgentRevive>((nint)agent, out var agentState) || !agent->IsAgentActive())
        { Status = "Waiting for the game's revival agent."; return; }
        if (!NativeSnapshot.TryRead<Revive>((nint)agentState.Revive, out var reviveState))
        { Status = "Waiting for readable revival state."; return; }
        if (reviveState.State != ReviveState.Revivable)
        { Status = $"Waiting for revival readiness ({reviveState.State})."; return; }
        if (live.IsNull || !live.IsReady || !live.IsVisible)
        { Status = "Waiting for the Return confirmation to appear."; return; }
        var addon = (AddonSelectYesno*)live.Address;
        // Only this agent's prompt. Never accept an arbitrary SelectYesno dialog
        // merely because the player is dead.
        var owner = agent->GetAddonId();
        if (!FarmingReviveReader.TryEvent(live.Address, owner, out var eventAddress))
        {
            var dialogId = NativeSnapshot.TryRead<AddonSelectYesno>(live.Address, out var dialog) ? dialog.Id : 0;
            Status = $"Return confirmation not accepted: owner={owner}, dialog={dialogId}; prompt or enabled button could not be verified.";
            return;
        }
        var evt = (AtkEvent*)eventAddress;
        // Use the button's registered event, following ECommons' ClickAddonButton
        // pattern; no guessed callback index, prompt text, or node index.
        var eventType = evt->State.EventType;
        var parameter = (int)evt->Param;
        addon->ReceiveEvent(eventType, parameter, evt);
        Status = $"Return confirmation sent ({eventType}, {parameter}); waiting for revival and zone loading…";
    }
}
