using System.Collections.Generic;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core.Instruments
{

    /// <summary>
    /// Genshin keyboard layouts.
    /// </summary>
    internal static class GenshinKeyboardLayouts
    {
        public static readonly KeyboardLayoutConfig QWERTY = new(
            name: "QWERTY",
            keys: [
                'z', 'x', 'c', 'v',
                'b', 'n', 'm',

                'a', 's', 'd', 'f',
                'g', 'h', 'j',

                'q', 'w', 'e', 'r',
                't', 'y', 'u'
            ]);

        public static readonly KeyboardLayoutConfig QWERTZ = new(
            name: "QWERTZ",
            keys: [
                'y', 'x', 'c', 'v',
                'b', 'n', 'm',

                'a', 's', 'd', 'f',
                'g', 'h', 'j',

                'q', 'w', 'e', 'r',
                't', 'z', 'u'
            ]);

        public static readonly KeyboardLayoutConfig AZERTY = new(
            name: "AZERTY",
            keys: [
                'w', 'x', 'c', 'v',
                'b', 'n', ',',

                'q', 's', 'd', 'f',
                'g', 'h', 'j',

                'a', 'z', 'e', 'r',
                't', 'y', 'u'
            ]);

        public static readonly KeyboardLayoutConfig DVORAK = new(
            name: "DVORAK",
            keys: [
                '/', 'b', 'i', '.',
                'n', 'l', 'm',

                'a', ';', 'h', 'y',
                'u', 'j', 'c',

                'x', ',', 'd', 'o',
                'k', 't', 'f'
            ]);

        public static readonly KeyboardLayoutConfig DVORAKLeft = new(
            name: "DVORAKLeft",
            keys: [
                'l', 'x', 'd', 'v',
                'e', 'n', '6',

                'k', 'u', 'f', '5',
                'c', 'h', '8',

                'w', 'b', 'j', 'y',
                'g', 'r', 't'
            ]);

        public static readonly KeyboardLayoutConfig DVORAKRight = new(
            name: "DVORAKRight",
            keys: [
                'd', 'c', 'l', ',',
                'p', 'n', '7',

                'f', 'u', 'k', '8',
                '.', 'h', '5',

                'e', 'm', 'g', 'y',
                'j', 'o', 'i'
            ]);

        public static readonly KeyboardLayoutConfig Colemak = new(
            name: "Colemak",
            keys: [
                'z', 'x', 'c', 'v',
                'b', 'j', 'm',

                'a', 'd', 'g', 'e',
                't', 'h', 'y',

                'q', 'w', 'k', 's',
                'f', 'o', 'i'
            ]);
    }
}
