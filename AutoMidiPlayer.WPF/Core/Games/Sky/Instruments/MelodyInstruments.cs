using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Sky: CotL melody instruments (Melody part, 15-note diatonic).
/// Instruments with 𝄐 (fermata) can hold extended notes in-game.
/// </summary>
public static partial class SkyInstruments
{
    // ── Shared note arrays (C-major diatonic, 15 notes) ──────────────

    private static readonly List<int> NotesC3C5 = [
        48, 50, 52, 53, 55, 57, 59, // C3 D3 E3 F3 G3 A3 B3
        60, 62, 64, 65, 67, 69, 71, // C4 D4 E4 F4 G4 A4 B4
        72                          // C5
    ];

    private static readonly List<int> NotesC4C6 = [
        60, 62, 64, 65, 67, 69, 71, // C4 D4 E4 F4 G4 A4 B4
        72, 74, 76, 77, 79, 81, 83, // C5 D5 E5 F5 G5 A5 B5
        84                          // C6
    ];

    private static readonly List<int> NotesC5C7 = [
        72, 74, 76, 77, 79, 81, 83, // C5 D5 E5 F5 G5 A5 B5
        84, 86, 88, 89, 91, 93, 95, // C6 D6 E6 F6 G6 A6 B6
        96                          // C7
    ];

    // ── C3–C5 range ──────────────────────────────────────────────────

    public static readonly InstrumentConfig SkyHarp = new(
        game: "Sky",
        name: "Harp",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyFledglingHarp = new(
        game: "Sky",
        name: "Fledgling Harp",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyGuitar = new(
        game: "Sky",
        name: "Guitar",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyRhythmGuitar = new(
        game: "Sky",
        name: "Rhythm Guitar",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyUkulele = new(
        game: "Sky",
        name: "Ukulele",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyLute = new(
        game: "Sky",
        name: "Lute",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    /// <summary>𝄐 Extended notes supported</summary>
    public static readonly InstrumentConfig SkyElectricGuitar = new(
        game: "Sky",
        name: "Electric Guitar 𝄐",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyBlueElectricGuitar = new(
        game: "Sky",
        name: "Blue Electric Guitar",
        notes: NotesC3C5,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    // ── C4–C6 range ──────────────────────────────────────────────────

    public static readonly InstrumentConfig SkyPiano = new(
        game: "Sky",
        name: "Piano",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyGrandPiano = new(
        game: "Sky",
        name: "Grand Piano",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyDuetsGrandPiano = new(
        game: "Sky",
        name: "Duets Grand Piano",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyFledglingUprightPiano = new(
        game: "Sky",
        name: "Fledgling Upright Piano",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyKalimba = new(
        game: "Sky",
        name: "Kalimba",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    /// <summary>𝄐 Extended notes supported</summary>
    public static readonly InstrumentConfig SkyTriumphViolin = new(
        game: "Sky",
        name: "Triumph Violin 𝄐",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    /// <summary>𝄐 Extended notes supported</summary>
    public static readonly InstrumentConfig SkyVoiceOfAURORA = new(
        game: "Sky",
        name: "Voice of AURORA 𝄐",
        notes: NotesC4C6,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    // ── C5–C7 range ──────────────────────────────────────────────────

    public static readonly InstrumentConfig SkyXylophone = new(
        game: "Sky",
        name: "Xylophone",
        notes: NotesC5C7,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);

    public static readonly InstrumentConfig SkyWinterPiano = new(
        game: "Sky",
        name: "Winter Piano",
        notes: NotesC5C7,
        keyboardLayouts: [SkyKeyboardLayouts.QWERTY_15]);
}
