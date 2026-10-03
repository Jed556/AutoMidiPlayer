using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

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

    /// Detected or assigned base key center for this song.
    /// If null, <see cref="Key"/> is treated as an absolute offset from C3 (legacy behavior).
    public int? BaseKey { get; set; }

    public int Key { get; set; }

    public string Path { get; set; } = null!;

    /// SHA-256 hash of the MIDI file content for duplicate detection.
    public string? FileHash { get; set; }

    public string? Title { get; set; }

    public string? Artist { get; set; }

    public string? Album { get; set; }

    public DateTime? DateAdded { get; set; }

    public Transpose? Transpose { get; set; } = Entities.Transpose.Ignore;

    /// Playback speed (0.1 to 4.0).
    public double? Speed { get; set; }

    /// Custom BPM override. If null, uses MIDI file's native BPM.
    public double? Bpm { get; set; }

    /// Cached duration in milliseconds from last MIDI parse.
    /// Avoids re-parsing the MIDI file just to display duration in the song list.
    public long? CachedDurationMs { get; set; }

    /// Cached native BPM from MIDI tempo map.
    /// Avoids re-parsing the MIDI file just to display BPM in the song list.
    public double? CachedNativeBpm { get; set; }

    /// Comma-separated list of disabled track indices (0-based).
    public string? DisabledTracks { get; set; }

    /// Per-song merge notes setting. If null, uses global setting.
    public bool? MergeNotes { get; set; }

    /// Per-song merge milliseconds setting. If null, uses global setting.
    public uint? MergeMilliseconds { get; set; }

    /// Per-song hold notes setting. If null, uses global setting.
    public bool? HoldNotes { get; set; }

    /// <summary>
    /// Whether simultaneous MIDI notes should be matched to an instrument's chords.
    /// </summary>
    public bool? AutoChord { get; set; }

    /// <summary>
    /// Maximum onset distance, in milliseconds, for notes to be detected as one chord.
    /// </summary>
    public uint? ChordDetectionMilliseconds { get; set; }

    private string? _keyChanges;
    private List<KeyChangeMarker>? _cachedKeyChangeMarkers;

    /// <summary>
    /// JSON-serialized list of timed key modulation markers.
    /// </summary>
    public string? KeyChanges
    {
        get => _keyChanges;
        set
        {
            _keyChanges = value;
            _cachedKeyChangeMarkers = null;
        }
    }

    [NotMapped]
    public List<KeyChangeMarker> KeyChangeMarkers
    {
        get
        {
            if (_cachedKeyChangeMarkers != null) return _cachedKeyChangeMarkers;
            if (string.IsNullOrWhiteSpace(KeyChanges)) return _cachedKeyChangeMarkers = new();
            try { return _cachedKeyChangeMarkers = JsonSerializer.Deserialize<List<KeyChangeMarker>>(KeyChanges) ?? new(); }
            catch { return _cachedKeyChangeMarkers = new(); }
        }
        set
        {
            _cachedKeyChangeMarkers = value;
            KeyChanges = value is { Count: > 0 } ? JsonSerializer.Serialize(value) : null;
        }
    }

    public KeyChangeMarker? GetKeyMarkerAtTime(TimeSpan time, long lookaheadMs = 35)
    {
        var markers = KeyChangeMarkers;
        if (markers.Count == 0) return null;
        var ms = (long)time.TotalMilliseconds + lookaheadMs;
        return markers.Where(m => m.TimeMs <= ms).MaxBy(m => m.TimeMs);
    }

    public int GetEffectiveKeyOffsetAtTime(TimeSpan time)
    {
        var marker = GetKeyMarkerAtTime(time);
        var offset = marker?.KeyOffset ?? Key;
        return MusicConstants.GetEffectiveKeyOffset(offset, BaseKey);
    }

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
