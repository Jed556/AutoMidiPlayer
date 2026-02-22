using System.Collections.Generic;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core.Instruments
{

    /// <summary>
    /// Heartopia keyboard layouts.
    /// </summary>
    internal static class HeartopiaKeyboardLayouts
    {
        public static readonly KeyboardLayoutConfig QWERTY_2Row = new(
            name: "QWERTY",
            keys: [
                'a', 's', 'd', 'f','g', 'h', 'j',
                'q', 'w', 'e', 'r','t', 'y', 'u', 'i'
            ]);

        public static readonly KeyboardLayoutConfig QWERTY_3Row = new(
            name: "QWERTY",
            keys: [
                'y', 'u', 'i', 'o', 'p',
                'h', 'j', 'k', 'l', ';',
                'n', 'm', ',', '.', '/'
            ]);

        public static readonly KeyboardLayoutConfig QWERTY_22Key = new(
            name: "QWERTY",
            keys: [
                'z', 'x', 'c', 'v',
                'b', 'n', 'm',

                'a', 's', 'd', 'f',
                'g', 'h', 'j',

                'q', 'w', 'e', 'r',
                't', 'y', 'u', 'i'
            ]);

        public static readonly KeyboardLayoutConfig QWERTY_37Key = new(
            name: "QWERTY",
            keys: [
                ',', 'l', '.', ';',
                '/', 'o', '0', 'p',
                '-', '[', '+', ']',

                'z', 's', 'x', 'd',
                'c', 'v', 'g', 'b',
                'h', 'n', 'j', 'm',

                'q', '2', 'w', '3',
                'e', 'r', '5', 't',
                '6', 'y', '7', 'u',
                'i'
            ]);
    }
}
