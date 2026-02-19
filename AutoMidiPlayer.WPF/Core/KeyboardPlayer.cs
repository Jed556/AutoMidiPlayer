using System;
using System.Collections.Generic;
using System.Linq;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.Core.Instruments;
using WindowsInput;
using WindowsInput.Native;
using static AutoMidiPlayer.WPF.Core.Keyboard;

namespace AutoMidiPlayer.WPF.Core;

public static class KeyboardPlayer
{
    private static readonly IInputSimulator Input = new InputSimulator();

    // Use direct SendInput for games that don't respond to InputSimulator
    public static bool UseDirectInput { get; set; } = true;

    public static int TransposeNote(
        string instrumentId, ref int noteId,
        Transpose direction = Transpose.Ignore)
    {
        if (direction is Transpose.Ignore) return noteId;
        var notes = Keyboard.GetNotes(instrumentId);
        while (true)
        {
            if (notes.Contains(noteId))
                return noteId;

            if (noteId < notes.First())
                noteId += 12;
            else if (noteId > notes.Last())
                noteId -= 12;
            else
            {
                return direction switch
                {
                    Transpose.Up => ++noteId,
                    Transpose.Down => --noteId,
                    _ => noteId
                };
            }
        }
    }

    public static void NoteDown(int noteId, string layoutName, string instrumentId)
    {
        // Check if this is a Roblox instrument (character-based input)
        if (IsRobloxInstrument(instrumentId))
        {
            RobloxKeyboardPlayer.NoteDown(noteId);
        }
        else
        {
            InteractNote(noteId, layoutName, instrumentId, Input.Keyboard.KeyDown);
        }
    }

    public static void NoteUp(int noteId, string layoutName, string instrumentId)
    {
        // Check if this is a Roblox instrument (character-based input)
        if (IsRobloxInstrument(instrumentId))
        {
            RobloxKeyboardPlayer.NoteUp(noteId);
        }
        else
        {
            InteractNote(noteId, layoutName, instrumentId, Input.Keyboard.KeyUp);
        }
    }

    public static void PlayNote(int noteId, string layoutName, string instrumentId)
    {
        // Check if this is a Roblox instrument (character-based input)
        if (IsRobloxInstrument(instrumentId))
        {
            RobloxKeyboardPlayer.PlayNote(noteId);
        }
        else
        {
            InteractNote(noteId, layoutName, instrumentId, Input.Keyboard.KeyPress);
        }
    }

    /// <summary>
    /// Checks if the instrument is a Roblox instrument that requires character-based input.
    /// </summary>
    private static bool IsRobloxInstrument(string instrumentId)
    {
        return instrumentId.StartsWith("Roblox", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetKey(string layoutName, string instrumentId, int noteId, out VirtualKeyCode key)
    {
        var keys = Keyboard.GetLayout(layoutName, instrumentId);
        var notes = Keyboard.GetNotes(instrumentId);
        return TryGetKey(keys, notes, noteId, out key);
    }

    private static bool TryGetKey(
        this IEnumerable<VirtualKeyCode> keys, IList<int> notes,
        int noteId, out VirtualKeyCode key)
    {
        var keyIndex = notes.IndexOf(noteId);
        key = keys.ElementAtOrDefault(keyIndex);

        return keyIndex != -1;
    }

    private static void InteractNote(
        int noteId, string layoutName, string instrumentId,
        Func<VirtualKeyCode, IKeyboardSimulator> action)
    {
        if (TryGetKey(layoutName, instrumentId, noteId, out var key))
        {
            if (UseDirectInput)
            {
                // Use direct Win32 SendInput with scan codes for better game compatibility
                Win32InputHelper.SendKeyDirect(key, action == Input.Keyboard.KeyUp);
            }
            else
            {
                action.Invoke(key);
            }
        }
    }
}
