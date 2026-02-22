using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Sky: CotL bass instruments (Bass part, 15-note diatonic).
/// Instruments with 𝄐 (fermata) can hold extended notes in-game.
/// </summary>
public static partial class SkyInstruments
{
    // ── C1–C3 range ──────────────────────────────────────────────────

    private static readonly List<int> NotesC1C3 = new()
    {
        24, 26, 28, 29, 31, 33, 35, // C1 D1 E1 F1 G1 A1 B1
        36, 38, 40, 41, 43, 45, 47, // C2 D2 E2 F2 G2 A2 B2
        48                           // C3
    };

    public static readonly InstrumentConfig SkyContrabass = new(
        game: "Sky",
        name: "Contrabass",
        notes: NotesC1C3,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_15 });

    // ── C2–C4 range (Bass) ───────────────────────────────────────────

    /// <summary>𝄐 Extended notes supported</summary>
    public static readonly InstrumentConfig SkyCello = new(
        game: "Sky",
        name: "Cello 𝄐",
        notes: NotesC2C4!,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_15 });

    public static readonly InstrumentConfig SkyDuetsCello = new(
        game: "Sky",
        name: "Duets Cello",
        notes: NotesC2C4!,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_15 });
}
