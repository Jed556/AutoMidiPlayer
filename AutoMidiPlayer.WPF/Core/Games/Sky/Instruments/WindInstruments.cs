using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Sky: CotL wind instruments (Wind part, 15-note diatonic).
/// Instruments with 𝄐 (fermata) can hold extended notes in-game.
/// </summary>
public static partial class SkyInstruments
{
    // ── C2–C4 range (Wind) ───────────────────────────────────────────

    private static readonly List<int> NotesC2C4 = [
        36, 38, 40, 41, 43, // C2 D2 E2 F2 G2
        45, 47, 48, 50, 52, // A2 B2 C3 D3 E3
        53, 55, 57, 59, 60, // C4 F3 G3 A3 B3
    ];

    public static readonly InstrumentConfig SkyHorn = new(
        game: "Sky",
        name: "Horn",
        notes: NotesC2C4,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    // ── C3–C5 range (Wind) ───────────────────────────────────────────

    /// <summary>𝄐 Extended notes supported</summary>
    public static readonly InstrumentConfig SkyTriumphSaxophone = new(
        game: "Sky",
        name: "Triumph Saxophone 𝄐",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    // ── C4–C6 range (Wind) ───────────────────────────────────────────

    public static readonly InstrumentConfig SkyFlute = new(
        game: "Sky",
        name: "Flute",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyPanflute = new(
        game: "Sky",
        name: "Panflute",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyBugle = new(
        game: "Sky",
        name: "Bugle",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyMantaOcarina = new(
        game: "Sky",
        name: "Manta Ocarina",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    /// <summary>𝄐 Extended notes supported</summary>
    public static readonly InstrumentConfig SkyHarmonica = new(
        game: "Sky",
        name: "Harmonica 𝄐",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyVesselFlute = new(
        game: "Sky",
        name: "Vessel Flute",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);
}
