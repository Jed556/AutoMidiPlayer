using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class HeartopiaInstruments
    {
        public static readonly InstrumentConfig Conga2r = new(
            game: "Heartopia",
            name: "Conga (2 Row)",
            notes: [
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
                83, // B5
                84  // C6
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_2Row
            ]
        );

        public static readonly InstrumentConfig Conga3r = new(
            game: "Heartopia",
            name: "Conga (3 Row)",
            notes: [
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
                83, // B5
                84  // C6
            ],
            keyboardLayouts: [
                HeartopiaKeyboardLayouts.QWERTY_3Row
            ]
        );
    }
}
