namespace AutoMidiPlayer.WPF.Core.Instruments;

public static partial class GenshinInstruments
{
    /// <summary>
    /// Nightwind Horn - 14 individual notes across C3-B4.
    /// It supports sustained notes in-game when Auto MIDI Player's Hold Notes option is enabled.
    /// </summary>
    public static readonly InstrumentConfig NightwindHorn = new(
        game: "Genshin Impact",
        name: "Nightwind Horn",
        notes:
        [
            60, 62, 64, 65, 67, 69, 71, // C4 D4 E4 F4 G4 A4 B4 (Q-U)
            48, 50, 52, 53, 55, 57, 59, // C3 D3 E3 F3 G3 A3 B3 (A-J)
        ],
        keyboardLayouts:
        [
            GenshinKeyboardLayouts.QWERTYTwoOctaves,
            GenshinKeyboardLayouts.QWERTZTwoOctaves,
            GenshinKeyboardLayouts.AZERTYTwoOctaves,
            GenshinKeyboardLayouts.DVORAKTwoOctaves,
            GenshinKeyboardLayouts.DVORAKLeftTwoOctaves,
            GenshinKeyboardLayouts.DVORAKRightTwoOctaves,
            GenshinKeyboardLayouts.ColemakTwoOctaves,
        ]);
}
