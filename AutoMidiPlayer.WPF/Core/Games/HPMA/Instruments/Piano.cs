using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class HPMAInstruments
    {
        public static readonly InstrumentConfig Piano14k = new(
            game: "HPMA",
            name: "Piano",
            notes: [
                60, 62, 64, 65, 67, 69, 71, // C4 D4 E4 F4 G4 A4 B4
                48, 50, 52, 53, 55, 57, 59  // C3 D3 E3 F3 G3 A3 B3
            ],
            keyboardLayouts: [
                HPMAKeyboardLayouts.QWERTY_14Key
            ]
        );

        public static readonly InstrumentConfig Piano36k = new(
            game: "HPMA",
            name: "Piano (Professional)",
            notes: [
                72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, // C5 C#5 D5 D#5 E5 F5 F#5 G5 G#5 A5 A#5 B5
                60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, // C4 C#4 D4 D#4 E4 F4 F#4 G4 G#4 A4 A#4 B4
                48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59  // C3 C#3 D3 D#3 E3 F3 F#3 G3 G#3 A3 A#3 B3
            ],
            keyboardLayouts: [
                HPMAKeyboardLayouts.QWERTY_36Key
            ]
        );
    }
}
