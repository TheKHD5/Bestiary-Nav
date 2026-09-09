using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;

namespace BestiaryNav;

internal sealed unsafe class CaptureStateReader
{
    private readonly nint statePointerAddress;
    public bool IsAvailable => statePointerAddress != 0;

    public CaptureStateReader(ISigScanner scanner, IPluginLog log, bool versionVerified)
    {
        if (!versionVerified)
            return;
        try
        {
            // Verified 2026.09.01: the gourd registration check reads ItemAction.Data[0],
            // obtains the capture-state singleton, checks Loaded == 3 and tests bit (id-1).
            // Resolve its getter instead of embedding an ASLR-dependent address.
            var check = scanner.ScanText("40 53 48 83 EC 20 41 0F B7 58 02 E8 ?? ?? ?? ?? 84 C0 74 ?? E8 ?? ?? ?? ?? 83 78 14 03");
            var call = check + 20;
            if (Marshal.ReadByte(call) != 0xE8)
                return;
            var getter = call + 5 + Marshal.ReadInt32(call + 1);
            if (Marshal.ReadByte(getter) != 0x48 || Marshal.ReadByte(getter + 1) != 0x8B ||
                Marshal.ReadByte(getter + 2) != 0x05 || Marshal.ReadByte(getter + 7) != 0xC3)
                return;
            statePointerAddress = getter + 7 + Marshal.ReadInt32(getter + 3);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Capture-state binding is unavailable; uncaptured markers are disabled.");
        }
    }

    public bool TryRead(out ulong captured)
    {
        captured = 0;
        if (statePointerAddress == 0)
            return false;
        var state = *(byte**)statePointerAddress;
        if (state == null || *(int*)(state + 0x14) != 3)
            return false;
        var bits = *(ulong*)state & 0x00FFFFFFFFFFFFFFUL;
        // The client currently has room for 56 species. A count mismatch means the
        // collection is being refreshed or the layout no longer matches this profile.
        if (*(uint*)(state + 0x10) != BitOperations.PopCount(bits))
            return false;
        captured = bits;
        return true;
    }
}
