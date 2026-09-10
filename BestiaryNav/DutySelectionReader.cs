using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BestiaryNav;

internal static unsafe class DutySelectionReader
{
    // The checkbox callback takes a ONE-based ContentList index, not the visible
    // tree row, duty ID, or highlighted row. See DUTY-SELECTION.md.
    public static bool TryFindEntry(AgentContentsFinder* pointer, uint dutyId, out uint index)
    {
        index = 0;
        if (dutyId == 0 || !NativeSnapshot.TryRead<AgentContentsFinder>((nint)pointer, out var agent)) return false;
        var count = agent.ContentList.LongCount;
        if (count is < 1 or > 2048) return false;
        for (var i = 0; i < count; i++)
        {
            if (!NativeSnapshot.TryRead<nint>((nint)(agent.ContentList.First + i), out var address) ||
                !NativeSnapshot.TryRead<Contents>(address, out var entry)) return false;
            if (entry.Id.ContentType != ContentsType.Regular || entry.Id.Id != dutyId) continue;
            if (index != 0) return false; // Reject an ambiguous list.
            index = (uint)i + 1;
        }
        return index != 0;
    }

    public static bool TryReadSelection(AgentContentsFinder* pointer, uint dutyId, out int count, out bool targetOnly)
    {
        count = 0;
        targetOnly = false;
        if (!NativeSnapshot.TryRead<AgentContentsFinder>((nint)pointer, out var agent)) return false;
        var length = agent.SelectedContent.LongCount;
        if (length is < 0 or > 5) return false;
        count = (int)length;
        if (count != 1) return true;
        if (!NativeSnapshot.TryRead<ContentsId>((nint)agent.SelectedContent.First, out var selected)) return false;
        targetOnly = selected.ContentType == ContentsType.Regular && selected.Id == dutyId;
        return true;
    }
}
