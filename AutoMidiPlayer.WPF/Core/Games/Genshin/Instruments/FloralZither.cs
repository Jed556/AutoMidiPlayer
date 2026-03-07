using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class GenshinInstruments
    {

        /// <summary>
        /// Floral Zither - 21 keys, diatonic scale (same as Windsong Lyre)
        /// </summary>
        public static readonly InstrumentConfig FloralZither = new(
            game: "Genshin Impact",
            name: "Floral Zither",
            notes: [
                72, 74, 76, 77, 79, 81, 83, // C5 D5 E5 F5 G5 A5 B5
                60, 62, 64, 65, 67, 69, 71, // C4 D4 E4 F4 G4 A4 B4
                48, 50, 52, 53, 55, 57, 59, // C3 D3 E3 F3 G3 A3 B3
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
