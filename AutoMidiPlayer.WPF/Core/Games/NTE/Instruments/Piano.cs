using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

public static partial class NTEInstruments
{
    public static readonly InstrumentConfig Piano21k = new(
        game: "NTE",
        name: "Piano (21 Key)",
        notes: [
            60, 62, 64, 65, 67, 69, 71, // C4 D4 E4 F4 G4 A4 B4
            48, 50, 52, 53, 55, 57, 59, // C3 D3 E3 F3 G3 A3 B3
            36, 38, 40, 41, 43, 45, 47, // C2 D2 E2 F2 G2 A2 B2
        ],
        keyboardLayouts: [NTEKeyboardLayouts.QWERTY_21Keys]
    );

    public static readonly InstrumentConfig Piano36k = new(
    game: "NTE",
    name: "Piano (36 Key)",
    notes: [
            60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, // C4 C#4 D4 D#4 E4 F4 F#4 G4 G#4 A4 A#4 B4
            48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, // C3 C#3 D3 D#3 E3 F3 F#3 G3 G#3 A3 A#3 B3
            36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, // C2 C#2 D2 D#2 E2 F2 F#2 G2 G#2 A2 A#2 B2
    ],
    keyboardLayouts: [NTEKeyboardLayouts.QWERTY_36Keys]
    );
}
