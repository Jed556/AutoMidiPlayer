using System;
using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Configuration for a game instrument including notes and available keyboard layouts.
/// </summary>
public class InstrumentConfig(
    string game,
    string name,
    IList<int> notes,
    IReadOnlyList<KeyboardLayoutConfig> keyboardLayouts,
    IReadOnlyList<ChordPadConfig>? chordPads = null,
    bool? supportsPedals = null)
{
    /// <summary>
    /// Game this instrument belongs to.
    /// </summary>
    public string Game { get; } = game;

    /// <summary>
    /// Display name of the instrument
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// MIDI note numbers this instrument can play
    /// </summary>
    public IList<int> Notes { get; } = notes ?? Array.Empty<int>();

    /// <summary>
    /// Keyboard layouts available for this instrument.
    /// </summary>
    public IReadOnlyList<KeyboardLayoutConfig> KeyboardLayouts { get; } = keyboardLayouts ?? Array.Empty<KeyboardLayoutConfig>();

    /// <summary>
    /// Optional pre-composed chord pads exposed by the game instrument.
    /// These are kept separate from <see cref="Notes"/>, whose entries always map one MIDI note to one key.
    /// </summary>
    public IReadOnlyList<ChordPadConfig> ChordPads { get; } = chordPads ?? Array.Empty<ChordPadConfig>();

    /// <summary>
    /// Whether this instrument supports pedal bindings (sustain, sostenuto, una corda).
    /// Defaults to true if any keyboard layout config defines a pedal key, or can be explicitly specified.
    /// </summary>
    public bool SupportsPedals { get; } = supportsPedals
        ?? (keyboardLayouts != null && System.Linq.Enumerable.Any(keyboardLayouts, l => l.SustainKey != null || l.SostenutoKey != null || l.UnaCordaKey != null));
}
