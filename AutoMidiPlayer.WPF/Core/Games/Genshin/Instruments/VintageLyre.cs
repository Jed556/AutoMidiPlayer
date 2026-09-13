using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class GenshinInstruments
    {

        /// <summary>
        /// Vintage Lyre - 21 keys from C3 to B5.
        /// The low and middle rows are C Dorian (E-flat and B-flat), while the
        /// high row is C Phrygian (D-flat, E-flat, A-flat, and B-flat).
        /// </summary>
        public static readonly InstrumentConfig VintageLyre = new(
            game: "Genshin Impact",
            name: "Vintage Lyre",
            notes: [
                72, 73, 75, 77, 79, 80, 82, // C5 Db5 Eb5 F5 G5 Ab5 Bb5
                60, 62, 63, 65, 67, 69, 70, // C4 D4 Eb4 F4 G4 A4 Bb4
                48, 50, 51, 53, 55, 57, 58, // C3 D3 Eb3 F3 G3 A3 Bb3
            ],
            keyboardLayouts: [
                GenshinKeyboardLayouts.QWERTY,
                GenshinKeyboardLayouts.QWERTZ,
                GenshinKeyboardLayouts.AZERTY,
                GenshinKeyboardLayouts.DVORAK,
                GenshinKeyboardLayouts.DVORAKLeft,
                GenshinKeyboardLayouts.DVORAKRight,
                GenshinKeyboardLayouts.Colemak
            ]
        );
    }
}
