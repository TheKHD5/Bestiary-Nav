using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BestiaryNav;

internal static unsafe class FarmingReviveReader
{
    public static bool TryEvent(nint dialogAddress, uint reviveAddonId, out nint eventAddress)
    {
        eventAddress = 0;
        if (reviveAddonId == 0 || !NativeSnapshot.TryRead<AddonSelectYesno>(dialogAddress, out var dialog) || dialog.Id != reviveAddonId) return false;
        return TryButtonEvent(dialog.YesButton, out eventAddress) ||
            TryButtonEvent((AtkComponentButton*)dialog.AtkComponentHoldButton278, out eventAddress);
    }
    private static bool TryButtonEvent(AtkComponentButton* pointer, out nint eventAddress)
    {
        eventAddress = 0;
        if (!NativeSnapshot.TryRead<AtkComponentButton>((nint)pointer, out var button) ||
            !NativeSnapshot.TryRead<AtkComponentNode>((nint)button.OwnerNode, out var node) ||
            (node.NodeFlags & (NodeFlags.Enabled | NodeFlags.Visible)) != (NodeFlags.Enabled | NodeFlags.Visible) ||
            !NativeSnapshot.TryRead<AtkEvent>((nint)node.AtkEventManager.Event, out _)) return false;
        eventAddress = (nint)node.AtkEventManager.Event;
        return true;
    }
}
