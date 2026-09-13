namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Where Winds Meet keyboard layouts.
/// </summary>
internal static class WWMKeyboardLayouts
{
    public static readonly KeyboardLayoutConfig QWERTY = new(
        name: "QWERTY",
        keys: [
            "q", "Q", "w", "^e", "e", "r", "R", "t", "T", "y", "^u", "u",
            "a", "A", "s", "^d", "d", "f", "F", "g", "G", "h", "^j", "j",
            "z", "Z", "x", "^c", "c", "v", "V", "b", "B", "n", "^m", "m"
        ]);
}
