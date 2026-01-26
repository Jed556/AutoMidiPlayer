using System;

namespace AutoMidiPlayer.Data.Entities;

public class Song
{
    protected Song() { }

    public Song(string path, int key)
    {
        Key = key;
        Path = path;
        Transpose = Entities.Transpose.Ignore; // Default to Ignore
        DateAdded = DateTime.Now;
    }

    public Guid Id { get; set; }

    public int Key { get; set; }

    public string Path { get; set; } = null!;

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Album { get; set; }

    public DateTime? DateAdded { get; set; }

    public Transpose? Transpose { get; set; } = Entities.Transpose.Ignore;

    /// Playback speed (0.1 to 4.0).
    public double? Speed { get; set; }

    /// Comma-separated list of disabled track indices (0-based).
    public string? DisabledTracks { get; set; }
}
