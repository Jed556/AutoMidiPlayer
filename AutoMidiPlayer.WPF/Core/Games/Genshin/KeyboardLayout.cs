using System.Linq;

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

        /// <summary>
        /// The two-row, eight-pad layout used by Genshin's percussion instruments.
        /// </summary>
        public static readonly KeyboardLayoutConfig QWERTYDrums = new(
            name: "QWERTY (Drums)",
            keys: [
                "q", "w", "e", "r",
                "a", "s", "d", "f",
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

        // The Nightwind Horn uses the top two rows of the standard 21-key grid.
        public static readonly KeyboardLayoutConfig QWERTYTwoOctaves = CreateTwoOctaveLayout(QWERTY);
        public static readonly KeyboardLayoutConfig QWERTZTwoOctaves = CreateTwoOctaveLayout(QWERTZ);
        public static readonly KeyboardLayoutConfig AZERTYTwoOctaves = CreateTwoOctaveLayout(AZERTY);
        public static readonly KeyboardLayoutConfig DVORAKTwoOctaves = CreateTwoOctaveLayout(DVORAK);
        public static readonly KeyboardLayoutConfig DVORAKLeftTwoOctaves = CreateTwoOctaveLayout(DVORAKLeft);
        public static readonly KeyboardLayoutConfig DVORAKRightTwoOctaves = CreateTwoOctaveLayout(DVORAKRight);
        public static readonly KeyboardLayoutConfig ColemakTwoOctaves = CreateTwoOctaveLayout(Colemak);

        // Lingering Euphonia's individual notes are in the middle and low rows. Its Q-U row
        // triggers preset chords, so it is kept as a separate chord-pad mapping.
        public static readonly KeyboardLayoutConfig QWERTYMelody = CreateMelodyLayout(QWERTY);
        public static readonly KeyboardLayoutConfig QWERTZMelody = CreateMelodyLayout(QWERTZ);
        public static readonly KeyboardLayoutConfig AZERTYMelody = CreateMelodyLayout(AZERTY);
        public static readonly KeyboardLayoutConfig DVORAKMelody = CreateMelodyLayout(DVORAK);
        public static readonly KeyboardLayoutConfig DVORAKLeftMelody = CreateMelodyLayout(DVORAKLeft);
        public static readonly KeyboardLayoutConfig DVORAKRightMelody = CreateMelodyLayout(DVORAKRight);
        public static readonly KeyboardLayoutConfig ColemakMelody = CreateMelodyLayout(Colemak);

        private static KeyboardLayoutConfig CreateTwoOctaveLayout(KeyboardLayoutConfig layout) => new(
            name: $"{layout.Name} (Two Octaves)",
            keyStrokes: layout.KeyStrokes.Take(14).ToArray());

        private static KeyboardLayoutConfig CreateMelodyLayout(KeyboardLayoutConfig layout) => new(
            name: $"{layout.Name} (Melody)",
            keyStrokes: layout.KeyStrokes.Skip(7).ToArray(),
            chordKeyStrokes: layout.KeyStrokes.Take(7).ToArray());
    }
}
