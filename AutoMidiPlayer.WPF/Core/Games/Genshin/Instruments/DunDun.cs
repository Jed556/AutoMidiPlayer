namespace AutoMidiPlayer.WPF.Core.Instruments;

public static partial class GenshinInstruments
{
    /// <summary>
    /// Dun Dun - eight percussion pads mapped from C4 to C5.
    /// </summary>
    public static readonly InstrumentConfig DunDun = new(
        game: "Genshin Impact",
        name: "Dun Dun",
        notes: [60, 62, 64, 65, 67, 69, 71, 72],
        keyboardLayouts: [GenshinKeyboardLayouts.QWERTYDrums]);
}
