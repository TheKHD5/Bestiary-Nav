using System;
using System.Globalization;

namespace BestiaryNav;

public static class BestiaryEntryLabel
{
    // Read the public number from the live label, never from a cached grid position.
    // The prefix can be localized; digits elsewhere in it indicate an unexpected format.
    public static bool TryParse(ReadOnlySpan<char> label, out uint number)
    {
        number = 0;
        label = label.Trim();
        if (label.IsEmpty || label.Length > 32)
            return false;
        var start = label.Length;
        while (start > 0 && label[start - 1] is >= '0' and <= '9')
            start--;
        if (start == label.Length)
            return false;
        foreach (var c in label[..start])
            if (char.IsDigit(c) || char.IsControl(c) || c is '-' or '+')
                return false;
        return uint.TryParse(label[start..], NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && number is >= 1 and <= 50;
    }
}
