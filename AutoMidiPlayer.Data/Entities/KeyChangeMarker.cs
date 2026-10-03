using System;
using System.Text.Json.Serialization;

namespace AutoMidiPlayer.Data.Entities;

/// <summary>
/// Represents a timed key offset change (modulation point) within a song.
/// </summary>
public class KeyChangeMarker
{
    /// <summary>
    /// Timestamp in milliseconds from the start of the song.
    /// </summary>
    public long TimeMs { get; set; }

    /// <summary>
    /// The relative key offset for this section (relative to Song.BaseKey).
    /// </summary>
    public int KeyOffset { get; set; }

    /// <summary>
    /// Optional section-specific transpose mode override.
    /// If null, inherits the song's global Transpose mode.
    /// </summary>
    public Transpose? Transpose { get; set; }

    /// <summary>
    /// Optional section title or label (e.g., "Verse 1", "Chorus (+1)", "Key Change").
    /// </summary>
    public string? Label { get; set; }

    [JsonIgnore]
    public TimeSpan Time => TimeSpan.FromMilliseconds(Math.Max(0, TimeMs));

    [JsonIgnore]
    public string TimeString => Time.ToString(@"mm\:ss\.fff");

    [JsonIgnore]
    public string NoteDisplay => MusicConstants.GetNoteName(KeyOffset);

    public override string ToString() => $"{TimeString} - {NoteDisplay} ({KeyOffset:+0;-0;0})";
}
