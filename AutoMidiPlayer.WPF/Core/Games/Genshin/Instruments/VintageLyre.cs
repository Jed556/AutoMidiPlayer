using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class GenshinInstruments
    {

        /// <summary>
        /// Vintage Lyre - 21 keys, Dorian mode scale
        /// </summary>
        public static readonly InstrumentConfig VintageLyre = new(
            game: "Genshin Impact",
            name: "Vintage Lyre",
            notes: new List<int>
            {
                48, // C3
                50, // D3
                51, // Eb3
                53, // F3
                55, // G3
                57, // A3
                58, // Bb3

                60, // C4
                62, // D4
                63, // Eb4
                65, // F4
                67, // G4
                69, // A4
                70, // Bb4

                72, // C5
                74, // Db5
                76, // Eb5
                77, // F5
                79, // G5
                80, // Ab5
                82  // Bb5
            },
            keyboardLayouts:
            [
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
