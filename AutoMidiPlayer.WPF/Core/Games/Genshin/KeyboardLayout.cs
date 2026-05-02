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
                "q", "w", "e", "r", "t", "y", "u",
                "a", "s", "d", "f", "g", "h", "j",
                "z", "x", "c", "v", "b", "n", "m",
            ]);

        public static readonly KeyboardLayoutConfig QWERTZ = new(
            name: "QWERTZ",
            keys: [
                "q", "w", "e", "r", "t", "z", "u",
                "a", "s", "d", "f", "g", "h", "j",
                "y", "x", "c", "v", "b", "n", "m",
            ]);

        public static readonly KeyboardLayoutConfig AZERTY = new(
            name: "AZERTY",
            keys: [
                "a", "z", "e", "r", "t", "y", "u",
                "q", "s", "d", "f", "g", "h", "j",
                "w", "x", "c", "v", "b", "n", ",",
            ]);

        public static readonly KeyboardLayoutConfig DVORAK = new(
            name: "DVORAK",
            keys: [
                "x", ",", "d", "o", "k", "t", "f",
                "a", ";", "h", "y", "u", "j", "c",
                "/", "b", "i", ".", "n", "l", "m",
            ]);

        public static readonly KeyboardLayoutConfig DVORAKLeft = new(
            name: "DVORAKLeft",
            keys: [
                "w", "b", "j", "y", "g", "r", "t",
                "k", "u", "f", "5", "c", "h", "8",
                "l", "x", "d", "v", "e", "n", "6",
            ]);

        public static readonly KeyboardLayoutConfig DVORAKRight = new(
            name: "DVORAKRight",
            keys: [
                "e", "m", "g", "y", "j", "o", "i",
                "f", "u", "k", "8", ".", "h", "5",
                "d", "c", "l", ",", "p", "n", "7",
            ]);

        public static readonly KeyboardLayoutConfig Colemak = new(
            name: "Colemak",
            keys: [
                "q", "w", "k", "s", "f", "o", "i",
                "a", "d", "g", "e", "t", "h", "y",
                "z", "x", "c", "v", "b", "j", "m",
            ]);
    }
}
