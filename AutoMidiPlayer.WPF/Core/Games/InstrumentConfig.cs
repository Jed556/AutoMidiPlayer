using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Configuration for a game instrument including notes and available keyboard layouts.
/// </summary>
public class InstrumentConfig
{
    /// <summary>
    /// Game this instrument belongs to.
    /// </summary>
    public string Game { get; }

    /// <summary>
    /// Display name of the instrument
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// MIDI note numbers this instrument can play
    /// </summary>
    public IList<int> Notes { get; }

    /// <summary>
    /// Keyboard layouts available for this instrument.
    /// </summary>
    public IReadOnlyList<KeyboardLayoutConfig> KeyboardLayouts { get; }



    public InstrumentConfig(
        string game,
        string name,
        IList<int> notes,
        IReadOnlyList<KeyboardLayoutConfig> keyboardLayouts)
    {
        Game = game;
        Name = name;
        Notes = notes;
        KeyboardLayouts = keyboardLayouts;
    }
}
