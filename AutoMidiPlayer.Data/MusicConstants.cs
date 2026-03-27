using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoMidiPlayer.Data.Entities;

namespace AutoMidiPlayer.Data;

/// <summary>
/// Centralized music constants for key offsets, transpose modes, and speed options.
/// This keeps the codebase DRY by providing a single source of truth.
/// </summary>
public static class MusicConstants
{
    /// <summary>
    /// Key offset to note name mapping (-27 to +27 semitones from C3)
    /// </summary>
    public static readonly Dictionary<int, string> KeyOffsets = new()
    {
        [-27] = "A0",
        [-26] = "A♯0",
        [-25] = "B0",
        [-24] = "C1",
        [-23] = "C♯1",
        [-22] = "D1",
        [-21] = "D♯1",
        [-20] = "E1",
        [-19] = "F1",
        [-18] = "F♯1",
        [-17] = "G1",
        [-16] = "G♯1",
        [-15] = "A1",
        [-14] = "A♯1",
        [-13] = "B1",
        [-12] = "C2",
        [-11] = "C♯2",
        [-10] = "D2",
        [-9] = "D♯2",
        [-8] = "E2",
        [-7] = "F2",
        [-6] = "F♯2",
        [-5] = "G2",
        [-4] = "G♯2",
        [-3] = "A2",
        [-2] = "A♯2",
        [-1] = "B2",
        [0] = "C3",
        [1] = "C♯3",
        [2] = "D3",
        [3] = "D♯3",
        [4] = "E3",
        [5] = "F3",
        [6] = "F♯3",
        [7] = "G3",
        [8] = "G♯3",
        [9] = "A3",
        [10] = "A♯3",
        [11] = "B3",
        [12] = "C4",
        [13] = "C♯4",
        [14] = "D4",
        [15] = "D♯4",
        [16] = "E4",
        [17] = "F4",
        [18] = "F♯4",
        [19] = "G4",
        [20] = "G♯4",
        [21] = "A4",
        [22] = "A♯4",
        [23] = "B4",
        [24] = "C5",
        [25] = "C♯5",
        [26] = "D5",
        [27] = "D♯5"
    };

    /// <summary>
    /// Transpose mode display names (for dropdown menus)
    /// </summary>
    public static readonly Dictionary<Transpose, string> TransposeNames = new()
    {
        [Transpose.Up] = "Up",
        [Transpose.Smart] = "Smart",
        [Transpose.Ignore] = "Ignore",
        [Transpose.Down] = "Down"
    };

    /// <summary>
    /// Transpose mode tooltips/descriptions
    /// </summary>
    public static readonly Dictionary<Transpose, string> TransposeTooltips = new()
    {
        [Transpose.Up] = "Transpose out-of-range notes 1 semitone up",
        [Transpose.Smart] = "Quantize out-of-range notes to the closest playable note using detected scale",
        [Transpose.Ignore] = "Skip out-of-range notes",
        [Transpose.Down] = "Transpose out-of-range notes 1 semitone down"
    };

    /// <summary>
    /// Short display names for transpose (for table columns)
    /// </summary>
    public static readonly Dictionary<Transpose, string> TransposeShortNames = new()
    {
        [Transpose.Up] = "Up",
        [Transpose.Smart] = "Smart",
        [Transpose.Ignore] = "Ignore",
        [Transpose.Down] = "Down"
    };

    public static int MinKeyOffset => KeyOffsets.Keys.Min();
    public static int MaxKeyOffset => KeyOffsets.Keys.Max();

    /// <summary>
    /// Get note name for a key offset
    /// </summary>
    public static string GetNoteName(int keyOffset) =>
        KeyOffsets.TryGetValue(keyOffset, out var note) ? note : "C3";

    /// <summary>
    /// Format key offset for display (e.g., "+5 (F3)" or "-3 (A2)")
    /// </summary>
    public static string FormatKeyDisplay(int keyOffset, bool includeDefault = false)
    {
        var note = GetNoteName(keyOffset);
        return FormatAlignedKeyDisplay(keyOffset, note);
    }

    /// <summary>
    /// Resolve the absolute key offset used for note conversion.
    /// For songs with a default key center, KeyOffset is relative to that center.
    /// </summary>
    public static int GetEffectiveKeyOffset(int keyOffset, int? defaultKeyOffset)
    {
        if (defaultKeyOffset is null)
            return Math.Clamp(keyOffset, MinKeyOffset, MaxKeyOffset);

        return Math.Clamp(defaultKeyOffset.Value + keyOffset, MinKeyOffset, MaxKeyOffset);
    }

    public static int GetRelativeMinKeyOffset(int? defaultKeyOffset) =>
        defaultKeyOffset is null ? MinKeyOffset : MinKeyOffset - defaultKeyOffset.Value;

    public static int GetRelativeMaxKeyOffset(int? defaultKeyOffset) =>
        defaultKeyOffset is null ? MaxKeyOffset : MaxKeyOffset - defaultKeyOffset.Value;

    /// <summary>
    /// Format relative key offset around a song-specific default key center.
    /// </summary>
    public static string FormatRelativeKeyDisplay(int relativeKeyOffset, int defaultKeyOffset, bool includeDefault = false)
    {
        var effectiveKeyOffset = GetEffectiveKeyOffset(relativeKeyOffset, defaultKeyOffset);
        var note = GetNoteName(effectiveKeyOffset);
        return FormatAlignedKeyDisplay(relativeKeyOffset, note);
    }

    private static string FormatSignedKeyOffset(int keyOffset) =>
        keyOffset.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    private static string FormatAlignedKeyDisplay(int keyOffset, string noteName) =>
        $"{FormatSignedKeyOffset(keyOffset),3} {noteName}";

    /// <summary>
    /// Generate key options for ComboBox binding
    /// </summary>
    public static List<KeyOption> GenerateKeyOptions(int? defaultKeyOffset = null)
    {
        var min = GetRelativeMinKeyOffset(defaultKeyOffset);
        var max = GetRelativeMaxKeyOffset(defaultKeyOffset);

        return Enumerable.Range(min, max - min + 1)
            .Select(offset =>
            {
                var effectiveKeyOffset = GetEffectiveKeyOffset(offset, defaultKeyOffset);
                var noteDisplay = GetNoteName(effectiveKeyOffset);

                return new KeyOption
                {
                    Value = offset,
                    OffsetDisplay = FormatSignedKeyOffset(offset),
                    NoteDisplay = noteDisplay,
                    Display = FormatAlignedKeyDisplay(offset, noteDisplay)
                };
            })
            .ToList();
    }

    /// <summary>
    /// Generate speed options for ComboBox binding
    /// </summary>
    public static List<SpeedOption> GenerateSpeedOptions()
    {
        var speeds = new List<double>();

        // 0.1 to 2.0 in 0.1 increments
        for (var s = 0.1; s <= 2.0; s = Math.Round(s + 0.1, 1))
            speeds.Add(s);

        // 2.5, 3.0, 3.5, 4.0
        speeds.AddRange([2.5, 3.0, 3.5, 4.0]);

        return speeds.Select(s => new SpeedOption { Value = s, Display = $"{s:0.0}x" }).ToList();
    }

    /// <summary>
    /// Key option for ComboBox binding
    /// </summary>
    public class KeyOption
    {
        public int Value { get; set; }
        public string OffsetDisplay { get; set; } = "0";
        public string NoteDisplay { get; set; } = "C3";
        public string Display { get; set; } = string.Empty;
    }

    /// <summary>
    /// Speed option for ComboBox binding
    /// </summary>
    public class SpeedOption
    {
        public double Value { get; set; }
        public string Display { get; set; } = string.Empty;
    }
}
