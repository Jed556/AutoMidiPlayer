using System;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.Core;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

/// <summary>
/// Display mode for notes in the Piano Roll and Timeline minimap.
/// </summary>
public enum PianoRollDisplayMode
{
    /// <summary>
    /// Displays notes dynamically following timeline key markers and transpositions.
    /// </summary>
    Auto,

    /// <summary>
    /// Displays transposed note equivalents following song key offset and song transpose mode.
    /// </summary>
    Transpose,

    /// <summary>
    /// Displays the original sequence exactly as authored in the MIDI file.
    /// </summary>
    SongDefault
}

/// <summary>
/// Calculates the rendered note pitch for the piano roll and timeline minimap based on display mode.
/// </summary>
public static class PianoRollNoteCalculator
{
    public static int CalculateDisplayPitch(
        int rawNoteNumber,
        long startMs,
        PianoRollDisplayMode mode,
        Song? song,
        string? instrumentId,
        bool isAutoCorrect)
    {
        if (song is null || mode == PianoRollDisplayMode.SongDefault)
            return rawNoteNumber;

        if (mode == PianoRollDisplayMode.Transpose)
        {
            var songKeyOffset = isAutoCorrect
                ? MusicConstants.GetEffectiveKeyOffset(song.Key, song.BaseKey)
                : song.Key;

            var noteId = rawNoteNumber + songKeyOffset;
            var songTranspose = song.Transpose ?? Transpose.Ignore;
            if (songTranspose != Transpose.Ignore && !string.IsNullOrEmpty(instrumentId))
            {
                KeyboardPlayer.TransposeNote(instrumentId, ref noteId, songTranspose);
            }

            return Math.Clamp(noteId, 0, 127);
        }

        // Auto mode: dynamic timeline key markers
        var marker = song.GetKeyMarkerAtTime(TimeSpan.FromMilliseconds(startMs));
        var activeKeyOffset = marker?.KeyOffset ?? song.Key;
        var effectiveOffset = isAutoCorrect
            ? (marker != null
                ? MusicConstants.GetEffectiveKeyOffset(activeKeyOffset, song.BaseKey)
                : MusicConstants.GetEffectiveKeyOffset(song.Key, song.BaseKey))
            : activeKeyOffset;

        var autoNoteId = rawNoteNumber + effectiveOffset;
        var activeTranspose = marker?.Transpose ?? song.Transpose ?? Transpose.Ignore;

        if (activeTranspose != Transpose.Ignore && !string.IsNullOrEmpty(instrumentId))
        {
            KeyboardPlayer.TransposeNote(instrumentId, ref autoNoteId, activeTranspose);
        }

        return Math.Clamp(autoNoteId, 0, 127);
    }
}
