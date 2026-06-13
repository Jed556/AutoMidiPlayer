using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    /// <summary>
    /// Harry Potter: Magic Awakened keyboard layouts.
    /// </summary>
    internal static class HPMAKeyboardLayouts
    {
        public static readonly KeyboardLayoutConfig QWERTY_14Key = new(
            name: "QWERTY",
            keys: [
                ",", ".", "/", "q", "w", "e", "r",
                "z", "x", "c", "v", "b", "n", "m",
            ]);

        public static readonly KeyboardLayoutConfig QWERTY_36Key = new(
            name: "QWERTY",
            keys: [
                "t", "6", "y", "7", "u", "i", "9", "o", "0", "p", "-", "[",
                ",", "l", ".", ";", "/", "q", "2", "w", "3", "e", "4", "r",
                "z", "s", "x", "d", "c", "v", "g", "b", "h", "n", "j", "m",
            ]);
    }
}
