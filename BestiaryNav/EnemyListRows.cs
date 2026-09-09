using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BestiaryNav;

internal static unsafe class EnemyListRows
{
    // Verified on game 2026.09.01: first row is node 2, clones are 20001..20007.
    // Never index AddonEnemyList.EnemyOneComponent: it describes only row zero.
    public static bool TryGetBounds(nint address, int row, out EnemyRowBounds bounds)
    {
        bounds = default;
        if (row is < 0 or >= 8 || !NativeSnapshot.TryRead<AtkUnitBase>(address, out var addon))
            return false;
        var nodeId = row == 0 ? 2u : 20000u + (uint)row;
        if (!TryFind(addon.UldManager, nodeId, out var nodeAddress, out var node) ||
            (ushort)node.Type != 1001 || (node.NodeFlags & NodeFlags.Visible) == 0 ||
            !NativeSnapshot.TryRead<AtkComponentNode>(nodeAddress, out var componentNode) ||
            !NativeSnapshot.TryRead<AtkComponentBase>((nint)componentNode.Component, out var component) ||
            (nint)component.OwnerNode != nodeAddress ||
            !TryFind(component.UldManager, 19, out _, out var collision) || collision.Type != NodeType.Collision ||
            !TryFind(component.UldManager, 6, out _, out var name) || name.Type != NodeType.Text)
            return false;

        // Check the game's own row-index registration as well as the node ID.
        var eventAddress = (nint)collision.AtkEventManager.Event;
        for (var i = 0; eventAddress != 0 && i < 16; i++)
        {
            if (!NativeSnapshot.TryRead<AtkEvent>(eventAddress, out var registration))
                return false;
            if (registration.State.EventType == AtkEventType.MouseDown &&
                registration.Param == row && (nint)registration.Listener == address)
            {
                if (!float.IsFinite(node.ScreenX) || !float.IsFinite(name.ScreenY) ||
                    !float.IsFinite(node.Transform.M11) || node.Transform.M11 <= 0 || node.Width == 0 ||
                    !float.IsFinite(name.Transform.M22) || name.Transform.M22 <= 0 || name.Height == 0)
                    return false;
                bounds = new EnemyRowBounds(node.ScreenX, node.ScreenX + node.Width * node.Transform.M11,
                    name.ScreenY + name.Height * name.Transform.M22 / 2);
                return true;
            }
            eventAddress = (nint)registration.NextEvent;
        }
        return false;
    }

    private static bool TryFind(AtkUldManager manager, uint id, out nint address, out AtkResNode result)
    {
        address = 0;
        result = default;
        if (manager.NodeList == null || manager.NodeListCount is 0 or > 128)
            return false;
        for (var i = 0; i < manager.NodeListCount; i++)
        {
            if (!NativeSnapshot.TryRead<nint>((nint)manager.NodeList + i * sizeof(nint), out var candidate))
                return false;
            if (candidate == 0)
                continue;
            if (!NativeSnapshot.TryRead<AtkResNode>(candidate, out var node))
                return false;
            if (node.NodeId != id)
                continue;
            address = candidate;
            result = node;
            return true;
        }
        return false;
    }
}
