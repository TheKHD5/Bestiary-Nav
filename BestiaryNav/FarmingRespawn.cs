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
    public unsafe void TryReturn(long now)
    {
        if (now < nextAttempt) return;
        nextAttempt = now + 5000;
        var agent = AgentRevive.Instance();
        var live = gui.GetAddonByName("SelectYesno");
        if (!NativeSnapshot.TryRead<AgentRevive>((nint)agent, out var agentState) || !agent->IsAgentActive() ||
            !NativeSnapshot.TryRead<Revive>((nint)agentState.Revive, out var reviveState) ||
            reviveState.State != ReviveState.Revivable || live.IsNull || !live.IsReady || !live.IsVisible) return;
        var addon = (AddonSelectYesno*)live.Address;
        // Only this agent's prompt. Never accept an arbitrary SelectYesno dialog
        // merely because the player is dead.
        if (!FarmingReviveReader.TryEvent(live.Address, agent->GetAddonId(), out var eventAddress)) return;
        var evt = (AtkEvent*)eventAddress;
        // Use the button's registered event, following ECommons' ClickAddonButton
        // pattern; no guessed callback index, prompt text, or node index.
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
    }
}
