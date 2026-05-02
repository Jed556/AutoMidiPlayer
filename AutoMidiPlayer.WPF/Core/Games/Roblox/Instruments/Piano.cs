using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class RobloxInstruments
    {
        /// <summary>
        /// Roblox Piano - 61 keys, full chromatic scale (C2-C7)
        /// </summary>
        public static readonly InstrumentConfig Piano61k = new(
            game: "Roblox",
            name: "Piano (61 Key)",
            notes: [
                36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, // C2 C#2 D2 D#2 E2 F2 F#2 G2 G#2 A2 A#2 B2
                48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, // C3 C#3 D3 D#3 E3 F3 F#3 G3 G#3 A3 A#3 B3
                60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, // C4 C#4 D4 D#4 E4 F4 F#4 G4 G#4 A4 A#4 B4
                72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, // C5 C#5 D5 D#5 E5 F5 F#5 G5 G#5 A5 A#5 B5
                84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, // C6 C#6 D6 D#6 E6 F6 F#6 G6 G#6 A6 A#6 B6
                96,                                             // C7
            ],
            keyboardLayouts: [
                RobloxKeyboardLayouts.QWERTY_61Key
            ]
        );

        /// <summary>
        /// Roblox Piano - 88 keys, full chromatic scale (C2-C7)
        /// </summary>
        public static readonly InstrumentConfig Piano = new(
            game: "Roblox",
            name: "Piano (88 Key)",
            notes: [
                21, 22, 23,                                             // A0 A#0 B0
                24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35,         // C1 C#1 D1 D#1 E1 F1 F#1 G1 G#1 A1 A#1 B1
                36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47,         // C2 C#2 D2 D#2 E2 F2 F#2 G2 G#2 A2 A#2 B2
                48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59,         // C3 C#3 D3 D#3 E3 F3 F#3 G3 G#3 A3 A#3 B3
                60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71,         // C4 C#4 D4 D#4 E4 F4 F#4 G4 G#4 A4 A#4 B4
                72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83,         // C5 C#5 D5 D#5 E5 F5 F#5 G5 G#5 A5 A#5 B5
                84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95,         // C6 C#6 D6 D#6 E6 F6 F#6 G6 G#6 A6 A#6 B6
                96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, // C7 C#7 D7 D#7 E7 F7 F#7 G7 G#7 A7 A#7 B7
                108                                                     // C8
            ],
            keyboardLayouts: [
                RobloxKeyboardLayouts.QWERTY_88Key
            ]
        );
    }
}
