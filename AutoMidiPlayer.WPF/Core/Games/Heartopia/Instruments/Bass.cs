using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments
{
    public static partial class HeartopiaInstruments
    {

        public static readonly InstrumentConfig WoodenBass2r = new(
            game: "Heartopia",
            name: "Wooden Bass (2 Row)",
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

        public static readonly InstrumentConfig WoodenBass3r = new(
            game: "Heartopia",
            name: "Wooden Bass (3 Row)",
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

        // additional 15-key instruments copied from bass configuration
        public static readonly InstrumentConfig BambooXiao2r = new(
            game: "Heartopia",
            name: "Bamboo Xiao (2 Row)",
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

        public static readonly InstrumentConfig BambooXiao3r = new(
            game: "Heartopia",
            name: "Bamboo Xiao (3 Row)",
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

        // all other instruments reuse the same notes and layouts as BambooXiao
        public static readonly InstrumentConfig Bagpipe2r = new(
            game: "Heartopia",
            name: "Bagpipe (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Bagpipe3r = new(
            game: "Heartopia",
            name: "Bagpipe (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Cajon2r = new(
            game: "Heartopia",
            name: "Cajon (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Cajon3r = new(
            game: "Heartopia",
            name: "Cajon (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Cello2r = new(
            game: "Heartopia",
            name: "Cello (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Cello3r = new(
            game: "Heartopia",
            name: "Cello (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Concertina2r = new(
            game: "Heartopia",
            name: "Concertina (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Concertina3r = new(
            game: "Heartopia",
            name: "Concertina (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Conga2r = new(
            game: "Heartopia",
            name: "Conga (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Conga3r = new(
            game: "Heartopia",
            name: "Conga (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Lute2r = new(
            game: "Heartopia",
            name: "Lute (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Lute3r = new(
            game: "Heartopia",
            name: "Lute (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Mbira2r = new(
            game: "Heartopia",
            name: "Mbira (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Mbira3r = new(
            game: "Heartopia",
            name: "Mbira (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Recorder2r = new(
            game: "Heartopia",
            name: "Recorder (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Recorder3r = new(
            game: "Heartopia",
            name: "Recorder (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

        public static readonly InstrumentConfig Violin2r = new(
            game: "Heartopia",
            name: "Violin (2 Row)",
            notes: BambooXiao2r.Notes,
            keyboardLayouts: BambooXiao2r.KeyboardLayouts
        );
        public static readonly InstrumentConfig Violin3r = new(
            game: "Heartopia",
            name: "Violin (3 Row)",
            notes: BambooXiao3r.Notes,
            keyboardLayouts: BambooXiao3r.KeyboardLayouts
        );

    }
}
