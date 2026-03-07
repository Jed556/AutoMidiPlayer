using System.Collections.Generic;
using GenshinKeyboardLayouts = AutoMidiPlayer.WPF.Core.Instruments.GenshinKeyboardLayouts;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class GenshinInstruments
    {

        /// <summary>
        /// Windsong Lyre - 21 keys, diatonic scale (C3-B5)
        /// </summary>
        public static readonly InstrumentConfig WindsongLyre = new(
            game: "Genshin Impact",
            name: "Windsong Lyre",
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
