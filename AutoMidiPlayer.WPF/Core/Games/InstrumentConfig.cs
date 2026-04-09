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
    IReadOnlyList<KeyboardLayoutConfig> keyboardLayouts)
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
}
