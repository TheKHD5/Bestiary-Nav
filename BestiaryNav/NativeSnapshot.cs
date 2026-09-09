using System.Runtime.InteropServices;

namespace BestiaryNav;

// Copy native data before examining it. An unreadable pointer returns false rather
// than causing an AccessViolationException (which cannot be caught reliably).
internal static unsafe class NativeSnapshot
{
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int ReadProcessMemory(nint process, nint address, void* buffer, nuint size, out nuint read);

    public static bool TryRead<T>(nint address, out T value) where T : unmanaged
    {
        value = default;
        if (address < 0x10000)
            return false;
        T copy = default;
        if (ReadProcessMemory(-1, address, &copy, (nuint)sizeof(T), out var read) == 0 || read != (nuint)sizeof(T))
            return false;
        value = copy;
        return true;
    }
}
