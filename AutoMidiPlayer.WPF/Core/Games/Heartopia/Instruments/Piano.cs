using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class HeartopiaInstruments
    {

        public static readonly InstrumentConfig Piano2r = new(
            game: "Heartopia",
            name: "Heartopia Piano 2-Row",
            notes: new List<int> {
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
            },
            keyboardLayouts:
            [
                HeartopiaKeyboardLayouts.QWERTY_2Row
            ]
        );

        public static readonly InstrumentConfig Piano3r = new(
            game: "Heartopia",
            name: "Heartopia Piano 3-Row",
            notes: new List<int>
            {
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
            },
            keyboardLayouts:
            [
                HeartopiaKeyboardLayouts.QWERTY_3Row
            ]
        );

        public static readonly InstrumentConfig Piano22k = new(
            game: "Heartopia",
            name: "Heartopia Piano 22",
            notes: new List<int>
            {
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
                83, // B5
                84  // C6
            },
            keyboardLayouts:
            [
                HeartopiaKeyboardLayouts.QWERTY_22Key
            ]
        );

        public static readonly InstrumentConfig Piano37k = new(
            game: "Heartopia",
            name: "Heartopia Piano 37",
            notes: new List<int> {
                48, // C3
                49, // C#3
                50, // D3
                51, // D#3
                52, // E3
                53, // F3
                54, // F#3
                55, // G3
                56, // G#3
                57, // A3
                58, // A#3
                59, // B3

                60, // C4
                61, // C#4
                62, // D4
                63, // D#4
                64, // E4
                65, // F4
                66, // F#4
                67, // G4
                68, // G#4
                69, // A4
                70, // A#4
                71, // B4

                72, // C5
                73, // C#5
                74, // D5
                75, // D#5
                76, // E5
                77, // F5
                78, // F#5
                79, // G5
                80, // G#5
                81, // A5
                82, // A#5
                83, // B5
                84  // C6
            },
            keyboardLayouts:
            [
                HeartopiaKeyboardLayouts.QWERTY_37Key
            ]
        );
    }
}
