using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class HeartopiaInstruments
    {
        public static readonly InstrumentConfig Recorder2r = new(
            game: "Heartopia",
            name: "Recorder (2 Row)",
            notes: [
                72, 74, 76, 77, 79, 81, 83, 84, // C5 D5 E5 F5 G5 A5 B5 C6
                60, 62, 64, 65, 67, 69, 71      // C4 D4 E4 F4 G4 A4 B4
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_2Row
            ]
        );

        public static readonly InstrumentConfig Recorder3r = new(
            game: "Heartopia",
            name: "Recorder (3 Row)",
            notes: [
                60, 62, 64, 65, 67, // C4 D4 E4 F4 G4
                69, 71, 72, 74, 76, // A4 B4 C5 D5 E5
                77, 79, 81, 83, 84  // F5 G5 A5 B5 C6
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_3Row
            ]
        );
    }
}
