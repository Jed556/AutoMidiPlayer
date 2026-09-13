namespace AutoMidiPlayer.WPF.Core.Instruments;

public static partial class GenshinInstruments
{
    /// <summary>
    /// Djem Djem Drum - eight percussion pads mapped from C4 to C5.
    /// </summary>
    public static readonly InstrumentConfig DjemDjemDrum = new(
        game: "Genshin Impact",
        name: "Djem Djem Drum",
        notes: [60, 62, 64, 65, 67, 69, 71, 72],
        keyboardLayouts: [GenshinKeyboardLayouts.QWERTYDrums]);
}
