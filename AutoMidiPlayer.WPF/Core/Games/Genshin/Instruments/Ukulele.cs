namespace AutoMidiPlayer.WPF.Core.Instruments;

public static partial class GenshinInstruments
{
    /// <summary>
    /// Ukulele - 14 individual notes across C3-B4.
    /// The Q-U row triggers the preset C, Dm, Em, F, G, Am, and G7 chords in-game,
    /// so it cannot be mapped to individual MIDI pitches.
    /// </summary>
    public static readonly InstrumentConfig Ukulele = new(
        game: "Genshin Impact",
        name: "Ukulele",
        notes:
        [
            60, 62, 64, 65, 67, 69, 71, // C4 D4 E4 F4 G4 A4 B4
            48, 50, 52, 53, 55, 57, 59, // C3 D3 E3 F3 G3 A3 B3
        ],
        keyboardLayouts:
        [
            GenshinKeyboardLayouts.QWERTYMelody,
            GenshinKeyboardLayouts.QWERTZMelody,
            GenshinKeyboardLayouts.AZERTYMelody,
            GenshinKeyboardLayouts.DVORAKMelody,
            GenshinKeyboardLayouts.DVORAKLeftMelody,
            GenshinKeyboardLayouts.DVORAKRightMelody,
            GenshinKeyboardLayouts.ColemakMelody,
        ],
        chordPads:
        [
            new("C",  [0, 4, 7],     keyIndex: 0),
            new("Dm", [2, 5, 9],     keyIndex: 1),
            new("Em", [4, 7, 11],    keyIndex: 2),
            new("F",  [0, 5, 9],     keyIndex: 3),
            new("G",  [2, 7, 11],    keyIndex: 4),
            new("Am", [0, 4, 9],     keyIndex: 5),
            new("G7", [2, 5, 7, 11], keyIndex: 6),
        ]);
}
