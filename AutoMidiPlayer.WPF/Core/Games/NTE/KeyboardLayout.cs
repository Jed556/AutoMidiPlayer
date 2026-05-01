namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// NTE keyboard layouts.
/// </summary>
internal static class NTEKeyboardLayouts
{
    public static readonly KeyboardLayoutConfig QWERTY_21Keys = new(
        name: "QWERTY",
        keys: [
            'q', 'w', 'e', 'r', 't', 'y', 'u',
            'a', 's', 'd', 'f', 'g', 'h', 'j',
            'z', 'x', 'c', 'v', 'b', 'n', 'm',
        ]);
}
