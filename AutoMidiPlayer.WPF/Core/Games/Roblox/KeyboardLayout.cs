using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

internal static class RobloxKeyboardLayouts
{

    public static readonly KeyboardLayoutConfig MirpDefault = new(
        name: "QWERTY",
        keys: [
        '1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7', '8', '*', '9', '(', '0',
        'q', 'Q', 'w', 'W', 'e', 'E', 'r', 't', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P',
        'a', 's', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k', 'l', 'L',
        'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n', 'm'
    ]);
}
