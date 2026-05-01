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
}
