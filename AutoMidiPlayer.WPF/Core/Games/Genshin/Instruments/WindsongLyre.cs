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
                48, // C3
                50, // D3
                52, // E3
                53, // F3
                55, // G3
                57, // A3
                59, // B3

                60, // C4
                62, // D4
                64, // E4
                65, // F4
                67, // G4
                69, // A4
                71, // B4

                72, // C5
                74, // D5
                76, // E5
                77, // F5
                79, // G5
                81, // A5
                83  // B5
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
