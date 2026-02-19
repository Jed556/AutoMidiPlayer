using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoMidiPlayer.Data.Models;

namespace AutoMidiPlayer.Data.Services;

/// <summary>
/// Service for managing keymap layouts used in character-based piano games like Roblox.
/// </summary>
public class KeymapService
{
    /// <summary>
    /// Gets the default MIRP (MIDI Input to Roblox Piano) keymap layout.
    /// This is the standard 61-note layout used in Roblox piano games.
    /// </summary>
    public static KeymapLayout GetDefaultRobloxLayout()
    {
        // Default MIRP layout from GreatCorn/MIRP project
        // Maps 61 notes to keyboard keys in the standard Roblox piano layout
        var notes = new[]
        {
            '1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7', '8', '*', '9', '(', '0',
            'q', 'Q', 'w', 'W', 'e', 'E', 'r', 't', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P',
            'a', 's', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k', 'l', 'L',
            'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n', 'm'
        };

        return new KeymapLayout
        {
            Name = "Roblox Default (MIRP)",
            Notes = notes
        };
    }

    /// <summary>
    /// Loads a keymap from a .mlf file.
    /// </summary>
    /// <param name="filePath">Path to the .mlf file</param>
    /// <returns>The loaded keymap layout</returns>
    public static KeymapLayout LoadKeymapFromFile(string filePath)
    {
        return KeymapLayout.LoadFromFile(filePath);
    }

    /// <summary>
    /// Validates that a keymap has exactly 61 valid keyboard characters.
    /// </summary>
    /// <param name="layout">The keymap layout to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool ValidateKeymap(KeymapLayout layout)
    {
        if (layout.Notes.Length != 61)
            return false;

        // Check that all characters are valid keyboard characters
        var validChars = GetValidKeymapCharacters();
        return layout.Notes.All(c => validChars.Contains(c));
    }

    /// <summary>
    /// Gets the set of valid characters that can be used in a keymap.
    /// </summary>
    public static HashSet<char> GetValidKeymapCharacters()
    {
        return new HashSet<char>
        {
            // Numbers
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
            // Number row symbols (with Shift)
            '!', '@', '#', '$', '%', '^', '&', '*', '(', ')',
            // Letters (lowercase)
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
            'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
            // Letters (uppercase - require Shift)
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
            'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
            // Common symbols
            '-', '=', '[', ']', '\\', ';', '\'', ',', '.', '/',
            '_', '+', '{', '}', '|', ':', '"', '<', '>', '?',
            // Space
            ' '
        };
    }

    /// <summary>
    /// Saves the default MIRP layout to a file.
    /// </summary>
    /// <param name="filePath">Path where to save the file</param>
    public static void SaveDefaultLayoutToFile(string filePath)
    {
        var defaultLayout = GetDefaultRobloxLayout();
        defaultLayout.SaveToFile(filePath);
    }
}
