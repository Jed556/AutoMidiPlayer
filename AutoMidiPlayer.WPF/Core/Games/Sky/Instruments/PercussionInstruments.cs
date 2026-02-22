using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Sky: CotL percussion instruments (Percussion part, 8-note 2×4 grid).
/// <list type="bullet">
///   <item>Drums (Drum, Prophecy Drum, Fortune Drum) have unpitched sounds.</item>
///   <item>Bells (Small Bell, Large Bell) play C4 D4 G4 A4 C5 D5 G5 A5.</item>
///   <item>Handpans (Sanctuary Handpan, Triumph Handpan) play D3 A3 C4 D4 F4 G4 A4 C5.</item>
///   <item>Cymbals – pitch TBA; mapped as unpitched like drums.</item>
/// </list>
/// </summary>
public static partial class SkyInstruments
{
    // ── Unpitched drums — mapped to C4-C5 diatonic for MIDI compatibility ──

    private static readonly List<int> NotesDrumUnpitched = new()
    {
        60, 62, 64, 65, 67, 69, 71, 72 // C4 D4 E4 F4 G4 A4 B4 C5
    };

    public static readonly InstrumentConfig SkyDrum = new(
        game: "Sky",
        name: "Drum",
        notes: NotesDrumUnpitched,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });

    public static readonly InstrumentConfig SkyProphecyDrum = new(
        game: "Sky",
        name: "Prophecy Drum",
        notes: NotesDrumUnpitched,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });

    public static readonly InstrumentConfig SkyFortuneDrum = new(
        game: "Sky",
        name: "Fortune Drum",
        notes: NotesDrumUnpitched,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });

    public static readonly InstrumentConfig SkyCymbals = new(
        game: "Sky",
        name: "Cymbals",
        notes: NotesDrumUnpitched,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });

    // ── Bells — 8 pitched notes ──────────────────────────────────────

    private static readonly List<int> NotesBell = new()
    {
        60, 62, 67, 69, // C4 D4 G4 A4
        72, 74, 79, 81  // C5 D5 G5 A5
    };

    public static readonly InstrumentConfig SkySmallBell = new(
        game: "Sky",
        name: "Small Bell",
        notes: NotesBell,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });

    public static readonly InstrumentConfig SkyLargeBell = new(
        game: "Sky",
        name: "Large Bell",
        notes: NotesBell,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });

    // ── Handpans — 8 pitched notes ───────────────────────────────────

    private static readonly List<int> NotesHandpan = new()
    {
        50, 57, 60, 62, // D3 A3 C4 D4
        65, 67, 69, 72  // F4 G4 A4 C5
    };

    public static readonly InstrumentConfig SkySanctuaryHandpan = new(
        game: "Sky",
        name: "Sanctuary Handpan",
        notes: NotesHandpan,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });

    public static readonly InstrumentConfig SkyTriumphHandpan = new(
        game: "Sky",
        name: "Triumph Handpan",
        notes: NotesHandpan,
        keyboardLayouts: new[] { SkyKeyboardLayouts.QWERTY_8 });
}
