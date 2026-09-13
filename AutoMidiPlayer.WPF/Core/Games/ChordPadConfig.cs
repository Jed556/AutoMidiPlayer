using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// A game-provided key that plays a complete chord rather than one note.
/// </summary>
public sealed class ChordPadConfig
{
    public ChordPadConfig(string name, IEnumerable<int> pitchClasses, int keyIndex)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A chord pad name is required.", nameof(name));
        if (keyIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(keyIndex));

        var normalizedPitchClasses = pitchClasses
            .Select(pitchClass => ((pitchClass % 12) + 12) % 12)
            .ToHashSet();

        if (normalizedPitchClasses.Count == 0)
            throw new ArgumentException("A chord pad must contain at least one pitch class.", nameof(pitchClasses));

        Name = name;
        PitchClasses = normalizedPitchClasses;
        KeyIndex = keyIndex;
    }

    public string Name { get; }

    /// <summary>
    /// Distinct pitch classes in the chord. Matching is inversion- and octave-independent.
    /// </summary>
    public IReadOnlySet<int> PitchClasses { get; }

    /// <summary>
    /// Index into <see cref="KeyboardLayoutConfig.ChordKeyStrokes"/>.
    /// </summary>
    public int KeyIndex { get; }
}
