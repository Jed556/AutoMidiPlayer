using System;
using System.IO;
using System.Linq;

namespace AutoMidiPlayer.Data.Models;

/// <summary>
/// Represents a keymap layout for mapping MIDI notes to keyboard characters.
/// Used for games like Roblox that use character-based input.
/// </summary>
public class KeymapLayout
{
    /// <summary>
    /// Display name of the keymap layout
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Array of 61 characters representing the keyboard mapping for piano notes.
    /// Index 0 = lowest note, Index 60 = highest note.
    /// </summary>
    public char[] Notes { get; set; } = Array.Empty<char>();

    /// <summary>
    /// File path where this keymap was loaded from (optional)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets the keyboard character for a given MIDI note.
    /// </summary>
    /// <param name="midiNote">MIDI note number (typically 36-96 for 61-note keyboard)</param>
    /// <param name="baseNote">The MIDI note number that maps to index 0 of the Notes array (default 36 = C2)</param>
    /// <returns>The character to press, or null if note is out of range</returns>
    public char? GetKeyForNote(int midiNote, int baseNote = 36)
    {
        var index = midiNote - baseNote;
        if (index < 0 || index >= Notes.Length)
            return null;

        return Notes[index];
    }

    /// <summary>
    /// Loads a keymap layout from a .mlf file (MIRP Layout File format).
    /// </summary>
    /// <param name="filePath">Path to the .mlf file</param>
    /// <returns>A KeymapLayout instance</returns>
    /// <exception cref="InvalidDataException">Thrown if file doesn't contain exactly 61 lines or has invalid characters</exception>
    public static KeymapLayout LoadFromFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length != 61)
            throw new InvalidDataException($"Keymap file must have exactly 61 lines (found {lines.Length})");

        var notes = new char[61];
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                throw new InvalidDataException($"Line {i + 1} is empty. Each line must contain exactly one character.");
            
            if (line.Length > 1)
                throw new InvalidDataException($"Line {i + 1} contains multiple characters. Each line must contain exactly one character.");
            
            notes[i] = line[0];
        }

        return new KeymapLayout
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            Notes = notes,
            FilePath = filePath
        };
    }

    /// <summary>
    /// Saves the keymap layout to a .mlf file.
    /// </summary>
    /// <param name="filePath">Path where to save the file</param>
    public void SaveToFile(string filePath)
    {
        File.WriteAllLines(filePath, Notes.Select(c => c.ToString()));
        FilePath = filePath;
    }
}
