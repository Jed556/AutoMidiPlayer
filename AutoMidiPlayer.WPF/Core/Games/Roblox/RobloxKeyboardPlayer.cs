using System;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Helper class for playing notes on Roblox piano using character-based input.
/// Handles Shift key requirements for uppercase letters and symbols.
/// </summary>
public static class RobloxKeyboardPlayer
{
    // Small delay between key press and release for proper game registration
    private const int KEY_PRESS_DELAY_MS = 10;

    // Cached default MIRP character sequence to avoid repeated allocations
    private static readonly char[] DefaultMIRPCharacters = new[]
    {
        '1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7', '8', '*', '9', '(', '0',
        'q', 'Q', 'w', 'W', 'e', 'E', 'r', 't', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P',
        'a', 's', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k', 'l', 'L',
        'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n', 'm'
    };

    /// <summary>
    /// Plays a note on Roblox piano by converting MIDI note to character and simulating key press.
    /// </summary>
    /// <param name="midiNote">MIDI note number</param>
    /// <param name="baseNote">Base MIDI note for the keyboard (default 36 = C2)</param>
    public static void PlayNote(int midiNote, int baseNote = 36)
    {
        var character = GetCharacterForNote(midiNote, baseNote);
        if (character.HasValue)
        {
            PressCharacter(character.Value);
        }
    }

    /// <summary>
    /// Presses down a note (key down only).
    /// </summary>
    public static void NoteDown(int midiNote, int baseNote = 36)
    {
        var character = GetCharacterForNote(midiNote, baseNote);
        if (character.HasValue)
        {
            PressCharacterDown(character.Value);
        }
    }

    /// <summary>
    /// Releases a note (key up only).
    /// </summary>
    public static void NoteUp(int midiNote, int baseNote = 36)
    {
        var character = GetCharacterForNote(midiNote, baseNote);
        if (character.HasValue)
        {
            PressCharacterUp(character.Value);
        }
    }

    /// <summary>
    /// Gets the character for a MIDI note from the default MIRP layout.
    /// </summary>
    private static char? GetCharacterForNote(int midiNote, int baseNote)
    {
        var index = midiNote - baseNote;
        
        if (index < 0 || index >= DefaultMIRPCharacters.Length)
            return null;

        return DefaultMIRPCharacters[index];
    }

    /// <summary>
    /// Presses a character (full press and release).
    /// </summary>
    private static void PressCharacter(char character)
    {
        var vk = RobloxKeyboardLayouts.GetVirtualKeyForCharacter(character, out var requiresShift);
        if (vk == null) return;

        if (requiresShift)
        {
            Win32InputHelper.SendKeyDirect(VirtualKeyCode.SHIFT, false);
            Win32InputHelper.SendKeyDirect(vk.Value, false);
            System.Threading.Thread.Sleep(KEY_PRESS_DELAY_MS);
            Win32InputHelper.SendKeyDirect(vk.Value, true);
            Win32InputHelper.SendKeyDirect(VirtualKeyCode.SHIFT, true);
        }
        else
        {
            Win32InputHelper.SendKeyDirect(vk.Value, false);
            System.Threading.Thread.Sleep(KEY_PRESS_DELAY_MS);
            Win32InputHelper.SendKeyDirect(vk.Value, true);
        }
    }

    /// <summary>
    /// Presses down a character (key down only).
    /// In hold-notes mode, releases Shift immediately after key down to avoid interfering with other notes.
    /// </summary>
    private static void PressCharacterDown(char character)
    {
        var vk = RobloxKeyboardLayouts.GetVirtualKeyForCharacter(character, out var requiresShift);
        if (vk == null) return;

        if (requiresShift)
        {
            Win32InputHelper.SendKeyDirect(VirtualKeyCode.SHIFT, false);
            Win32InputHelper.SendKeyDirect(vk.Value, false);
            // Release Shift immediately to avoid affecting overlapping notes
            Win32InputHelper.SendKeyDirect(VirtualKeyCode.SHIFT, true);
        }
        else
        {
            Win32InputHelper.SendKeyDirect(vk.Value, false);
        }
    }

    /// <summary>
    /// Releases a character (key up only).
    /// </summary>
    private static void PressCharacterUp(char character)
    {
        var vk = RobloxKeyboardLayouts.GetVirtualKeyForCharacter(character, out _);
        if (vk == null) return;

        Win32InputHelper.SendKeyDirect(vk.Value, true);
        // Note: Shift is already released in PressCharacterDown, so we don't release it here
    }
}
