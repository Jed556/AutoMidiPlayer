using System;
using System.IO;
using System.Security.Cryptography;

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
        FileHash = ComputeFileHash(path);
    }

    public Guid Id { get; set; }

    public int Key { get; set; }

    public string Path { get; set; } = null!;

    /// SHA-256 hash of the MIDI file content for duplicate detection.
    public string? FileHash { get; set; }

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Album { get; set; }

    public DateTime? DateAdded { get; set; }

    public Transpose? Transpose { get; set; } = Entities.Transpose.Ignore;

    /// Playback speed (0.1 to 4.0).
    public double? Speed { get; set; }

    /// Custom BPM override. If null, uses MIDI file's native BPM.
    public double? Bpm { get; set; }

    /// Comma-separated list of disabled track indices (0-based).
    public string? DisabledTracks { get; set; }

    /// Per-song merge notes setting. If null, uses global setting.
    public bool? MergeNotes { get; set; }

    /// Per-song merge milliseconds setting. If null, uses global setting.
    public uint? MergeMilliseconds { get; set; }

    /// Per-song hold notes setting. If null, uses global setting.
    public bool? HoldNotes { get; set; }

    /// <summary>
    /// Computes SHA-256 hash of a file's content.
    /// </summary>
    /// <param name="filePath">Path to the file to hash.</param>
    /// <returns>Hex string of the SHA-256 hash, or null if file doesn't exist.</returns>
    public static string? ComputeFileHash(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexString(hashBytes);
        }
        catch
        {
            return null;
        }
    }
}
