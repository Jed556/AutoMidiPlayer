using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class HeartopiaInstruments
    {

        public static readonly InstrumentConfig Piano2r = new(
            game: "Heartopia",
            name: "Piano (2 Row)",
            notes: [
                72, 74, 76, 77, 79, 81, 83, 84, // C5 D5 E5 F5 G5 A5 B5 C6
                60, 62, 64, 65, 67, 69, 71      // C4 D4 E4 F4 G4 A4 B4
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_2Row
            ]
        );

        public static readonly InstrumentConfig Piano3r = new(
            game: "Heartopia",
            name: "Piano (3 Row)",
            notes: [
                60, 62, 64, 65, 67, // C4 D4 E4 F4 G4
                69, 71, 72, 74, 76, // A4 B4 C5 D5 E5
                77, 79, 81, 83, 84  // F5 G5 A5 B5 C6
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_3Row
            ]
        );

        public static readonly InstrumentConfig Piano22k = new(
            game: "Heartopia",
            name: "Piano (22 Key)",
            notes: [
                72, 74, 76, 77, 79, 81, 83, 84, // C5 D5 E5 F5 G5 A5 B5 C6
                60, 62, 64, 65, 67, 69, 71,     // C4 D4 E4 F4 G4 A4 B4
                48, 50, 52, 53, 55, 57, 59,     // C3 D3 E3 F3 G3 A3 B3
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_22Key
            ]
        );

        public static readonly InstrumentConfig Piano37k = new(
            game: "Heartopia",
            name: "Piano (37 Key)",
            notes: [
                72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, // C5 C#5 D5 D#5 E5 F5 F#5 G5 G#5 A5 A#5 B5 C6
                60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71,     // C4 C#4 D4 D#4 E4 F4 F#4 G4 G#4 A4 A#4 B4
                48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59,     // C3 C#3 D3 D#3 E3 F3 F#3 G3 G#3 A3 A#3 B3
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_37Key
            ]
        );
    }
}
