using System;
using System.Collections.Generic;
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
        [Transpose.Ignore] = "Ignore",
        [Transpose.Down] = "Down"
    };

    /// <summary>
    /// Transpose mode tooltips/descriptions
    /// </summary>
    public static readonly Dictionary<Transpose, string> TransposeTooltips = new()
    {
        [Transpose.Up] = "Transpose out-of-range notes 1 semitone up",
        [Transpose.Ignore] = "Skip out-of-range notes",
        [Transpose.Down] = "Transpose out-of-range notes 1 semitone down"
    };

    /// <summary>
    /// Short display names for transpose (for table columns)
    /// </summary>
    public static readonly Dictionary<Transpose, string> TransposeShortNames = new()
    {
        [Transpose.Up] = "Up",
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
        var prefix = keyOffset > 0 ? "+" : "";
        var suffix = keyOffset == 0 && includeDefault ? " (Default)" : "";
        return $"{prefix}{keyOffset} ({note}{suffix})";
    }

    /// <summary>
    /// Generate key options for ComboBox binding
    /// </summary>
    public static List<KeyOption> GenerateKeyOptions() =>
        KeyOffsets.OrderBy(k => k.Key)
            .Select(k => new KeyOption { Value = k.Key, Display = FormatKeyDisplay(k.Key) })
            .ToList();

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
        speeds.AddRange(new[] { 2.5, 3.0, 3.5, 4.0 });

        return speeds.Select(s => new SpeedOption { Value = s, Display = $"{s:0.0}x" }).ToList();
    }

    /// <summary>
    /// Key option for ComboBox binding
    /// </summary>
    public class KeyOption
    {
        public int Value { get; set; }
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
