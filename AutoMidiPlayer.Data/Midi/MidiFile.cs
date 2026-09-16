using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi.Extensions;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;
using Stylet;
using static System.IO.Path;

namespace AutoMidiPlayer.Data.Midi;

public class MidiFile : Screen
{
    private readonly ReadingSettings? _settings;
    private int _position;
    /// <summary>
    /// Creates tolerant default reading settings that snap out-of-range parameters (0-127)
    /// to limits (e.g. byte 134 in NoteOn clamped to 127) and ignore non-fatal chunk mismatches.
    /// </summary>
    public static ReadingSettings CreateDefaultReadingSettings() => new()
    {
        TextEncoding = System.Text.Encoding.UTF8,
        InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.SnapToLimits,
        InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
        InvalidSystemCommonEventParameterValuePolicy = InvalidSystemCommonEventParameterValuePolicy.SnapToLimits,
        InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
        NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
        UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
        UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
        MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
        NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
        UnknownFileFormatPolicy = UnknownFileFormatPolicy.Ignore,
        ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
        UnknownChannelEventPolicy = UnknownChannelEventPolicy.SkipStatusByte,
    };

    public MidiFile(Song song, ReadingSettings? settings = null)
    {
        _settings = settings ?? CreateDefaultReadingSettings();
        _settings.TextEncoding ??= System.Text.Encoding.UTF8;

        Song = song;

        if (song.CachedDurationMs.HasValue && song.CachedNativeBpm.HasValue)
        {
            // Metadata already cached from a previous parse — skip expensive disk I/O.
            // The full MIDI will be parsed on-demand when the song is opened for playback.
            Logger.LogMidiParser($"MIDI_LOAD_SKIPPED (cached) path='{Path}'");
        }
        else
        {
            // First-time load or cache missing — parse now and cache metadata
            InitializeMidi();
            CacheMetadata();
        }
    }

    /// <summary>
    /// Whether the MIDI file contained formatting errors that were automatically repaired
    /// using tolerant reading policies (e.g. SnapToLimits).
    /// </summary>
    public bool HasRecoveredErrors { get; private set; }

    /// <summary>
    /// The message of the recovered formatting error, if any.
    /// </summary>
    public string? RecoveryReason { get; private set; }

    public Song Song { get; }

    public int Position
    {
        get => _position + 1;
        set => SetAndNotify(ref _position, value);
    }

    /// <summary>
    /// The parsed DryWetMidi file object. Null when the MIDI file has not been parsed yet
    /// (lazy-loaded songs with cached metadata skip parsing until playback).
    /// Callers that need the full MIDI tree should use <see cref="EnsureMidiLoaded"/>
    /// or rely on <see cref="PlaybackEngineService"/> which calls <see cref="InitializeMidi"/>
    /// before accessing this property.
    /// </summary>
    public Melanchall.DryWetMidi.Core.MidiFile? Midi { get; private set; }

    /// <summary>
    /// Whether the full MIDI file has been parsed into memory.
    /// </summary>
    public bool IsMidiLoaded => Midi is not null;

    /// <summary>
    /// The original tempo map from the MIDI file, preserved regardless of track changes.
    /// </summary>
    public TempoMap? OriginalTempoMap { get; private set; }

    public string Path => Song.Path;

    public string Title => Song.Title ?? GetFileNameWithoutExtension(Path);

    public string? Artist => Song.Artist;

    public TimeSpan Duration
    {
        get
        {
            // Use cached value if MIDI hasn't been parsed yet
            if (!IsMidiLoaded && Song.CachedDurationMs.HasValue)
                return TimeSpan.FromMilliseconds(Song.CachedDurationMs.Value);

            if (Midi is null)
                return TimeSpan.Zero;

            try
            {
                return Midi.GetDuration<MetricTimeSpan>();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Handle corrupted MIDI files gracefully
                return TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// Gets the BPM from the MIDI file's tempo map. Returns the tempo at the start of the file.
    /// Falls back to cached value if MIDI is not loaded.
    /// </summary>
    public double GetNativeBpm()
    {
        // Use cached value if MIDI hasn't been parsed yet
        if (!IsMidiLoaded && Song.CachedNativeBpm.HasValue)
            return Song.CachedNativeBpm.Value;

        if (Midi is null)
            return 120.0; // Default BPM fallback

        var tempoMap = Midi.GetTempoMap();
        var tempo = tempoMap.GetTempoAtTime(new MetricTimeSpan(0));
        return tempo.BeatsPerMinute;
    }

    /// <summary>
    /// Gets the effective BPM - uses song's custom BPM if set, otherwise uses native MIDI BPM.
    /// </summary>
    public double EffectiveBpm => Song.Bpm ?? GetNativeBpm();

    public IEnumerable<Melanchall.DryWetMidi.Core.MidiFile> Split(uint bars, uint beats, uint ticks) =>
        Midi?.SplitByGrid(new SteppedGrid(new BarBeatTicksTimeSpan(bars, beats, ticks)))
        ?? Enumerable.Empty<Melanchall.DryWetMidi.Core.MidiFile>();

    /// <summary>
    /// Ensures the full MIDI file is parsed into memory. No-op if already loaded.
    /// Call this before accessing <see cref="Midi"/> directly.
    /// </summary>
    public void EnsureMidiLoaded()
    {
        if (!IsMidiLoaded)
            InitializeMidi();
    }

    public void InitializeMidi()
    {
        var sw = Stopwatch.StartNew();
        Logger.LogMidiParser($"MIDI_LOAD_BEGIN path='{Path}'");

        try
        {
            // First attempt: read strictly to verify if the MIDI file has any format errors
            var strictSettings = new ReadingSettings { TextEncoding = System.Text.Encoding.UTF8 };
            Midi = Melanchall.DryWetMidi.Core.MidiFile.Read(Path, strictSettings);
            HasRecoveredErrors = false;
            RecoveryReason = null;
        }
        catch (MidiException ex)
        {
            // File contains non-standard or malformed MIDI events (e.g. parameter > 127 in NoteOn).
            // Fall back to tolerant reading settings (SnapToLimits) to recover automatically.
            Logger.LogMidiParser($"MIDI_LOAD_STRICT_FAILED path='{Path}' | error='{ex.Message}'. Retrying with tolerant settings.");
            Midi = Melanchall.DryWetMidi.Core.MidiFile.Read(Path, _settings);
            HasRecoveredErrors = true;
            RecoveryReason = ex.Message;
        }

        Midi.RemoveMalformedSysExEvents();
        // Store the original tempo map so it's preserved even when tracks are modified
        OriginalTempoMap = Midi.GetTempoMap();

        sw.Stop();
        var trackCount = Midi.GetTrackChunks().Count();
        var nativeBpm = GetNativeBpm();
        Logger.LogMidiParser(
            $"MIDI_LOAD_END path='{Path}' | tracks={trackCount} | bpm={nativeBpm:0.###} | recoveredErrors={HasRecoveredErrors} | elapsedMs={sw.Elapsed.TotalMilliseconds:0}");

        // Update cached metadata whenever we parse
        CacheMetadata();
    }

    /// <summary>
    /// Writes the current Duration and NativeBpm to the Song entity's cache properties.
    /// The caller is responsible for persisting the Song to the database.
    /// </summary>
    public void CacheMetadata()
    {
        if (Midi is null) return;

        try
        {
            var duration = Midi.GetDuration<MetricTimeSpan>();
            Song.CachedDurationMs = (long)((TimeSpan)duration).TotalMilliseconds;
        }
        catch (ArgumentOutOfRangeException)
        {
            Song.CachedDurationMs = 0;
        }

        try
        {
            var tempoMap = Midi.GetTempoMap();
            var tempo = tempoMap.GetTempoAtTime(new MetricTimeSpan(0));
            Song.CachedNativeBpm = tempo.BeatsPerMinute;
        }
        catch
        {
            Song.CachedNativeBpm = 120.0;
        }
    }
}
