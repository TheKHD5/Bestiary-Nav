using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BestiaryNav;

internal static unsafe class BestiarySelectionReader
{
    public const string AddonName = "XBMMonsterNotebook";

    // Observed in the live 2026.09.01 client with Dalamud 15.0.3.3.
    // The same 25 components are reused across pages and sort/filter settings.
    // See NATIVE-BINDING.md for evidence and the patch validation procedure.
    public static bool TryRead(AtkUnitBase* addon, int eventParam, out uint number)
    {
        number = 0;
        if (eventParam is < 4 or > 28)
            return false;
        var entry = FindNode(&addon->UldManager, (uint)(27 + eventParam - 4));
        if (entry == null || (ushort)entry->Type != 1024 || !entry->IsVisible())
            return false;
        var component = ((AtkComponentNode*)entry)->Component;
        if (component == null)
            return false;
        var collision = FindNode(&component->UldManager, 12);
        var label = FindNode(&component->UldManager, 11);
        if (collision == null || collision->Type != NodeType.Collision || !collision->IsVisible() ||
            label == null || label->Type != NodeType.Text || !label->IsVisible())
            return false;

        // Verify this is still the game's numbered-entry MouseDown registration.
        var registration = collision->AtkEventManager.Event;
        var matched = false;
        for (var i = 0; registration != null && i < 16; i++, registration = registration->NextEvent)
        {
            if (registration->State.EventType == AtkEventType.MouseDown &&
                registration->Param == eventParam && (nint)registration->Listener == (nint)addon)
            {
                matched = true;
                break;
            }
        }
        if (!matched)
            return false;
        var text = ((AtkTextNode*)label)->NodeText.AsSpan();
        if (text.Length > 96)
            return false;
        return BestiaryEntryLabel.TryParse(Encoding.UTF8.GetString(text), out number);
    }

    private static AtkResNode* FindNode(AtkUldManager* manager, uint id)
    {
        if (manager->NodeList == null || manager->NodeListCount > 512)
            return null;
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node != null && node->NodeId == id)
                return node;
        }
        return null;
    }
}
