using System;
using System.Runtime.InteropServices;
using WindowsInput;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Helper class for playing notes on Roblox piano using character-based input.
/// Handles Shift key requirements for uppercase letters and symbols.
/// </summary>
public static class RobloxKeyboardPlayer
{
    private static readonly IInputSimulator Input = new InputSimulator();

    // Small delay between key press and release for proper game registration
    private const int KEY_PRESS_DELAY_MS = 10;

    // Win32 API for direct keyboard input (more compatible with games)
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
        // Padding to match MOUSEINPUT size (INPUT structure uses union in Win32, requires matching sizes)
        // These fields ensure proper memory layout for the INPUT union structure
        private readonly uint padding1;
        private readonly uint padding2;
    }

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
        var characters = GetDefaultMIRPCharacters();
        
        if (index < 0 || index >= characters.Length)
            return null;

        return characters[index];
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
            SendKeyDirect(VirtualKeyCode.SHIFT, false);
            SendKeyDirect(vk.Value, false);
            System.Threading.Thread.Sleep(KEY_PRESS_DELAY_MS);
            SendKeyDirect(vk.Value, true);
            SendKeyDirect(VirtualKeyCode.SHIFT, true);
        }
        else
        {
            SendKeyDirect(vk.Value, false);
            System.Threading.Thread.Sleep(KEY_PRESS_DELAY_MS);
            SendKeyDirect(vk.Value, true);
        }
    }

    /// <summary>
    /// Presses down a character (key down only).
    /// </summary>
    private static void PressCharacterDown(char character)
    {
        var vk = RobloxKeyboardLayouts.GetVirtualKeyForCharacter(character, out var requiresShift);
        if (vk == null) return;

        if (requiresShift)
        {
            SendKeyDirect(VirtualKeyCode.SHIFT, false);
        }
        SendKeyDirect(vk.Value, false);
    }

    /// <summary>
    /// Releases a character (key up only).
    /// </summary>
    private static void PressCharacterUp(char character)
    {
        var vk = RobloxKeyboardLayouts.GetVirtualKeyForCharacter(character, out var requiresShift);
        if (vk == null) return;

        SendKeyDirect(vk.Value, true);
        if (requiresShift)
        {
            SendKeyDirect(VirtualKeyCode.SHIFT, true);
        }
    }

    /// <summary>
    /// Sends a key using Win32 SendInput API.
    /// </summary>
    private static void SendKeyDirect(VirtualKeyCode key, bool keyUp)
    {
        uint scanCode = MapVirtualKey((uint)key, MAPVK_VK_TO_VSC);

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = (ushort)key,
                wScan = (ushort)scanCode,
                dwFlags = (keyUp ? KEYEVENTF_KEYUP : KEYEVENTF_KEYDOWN) | KEYEVENTF_SCANCODE,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        _ = SendInput(1, [input], Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>
    /// Gets the default MIRP character sequence (61 characters).
    /// </summary>
    private static char[] GetDefaultMIRPCharacters()
    {
        return new[]
        {
            '1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7', '8', '*', '9', '(', '0',
            'q', 'Q', 'w', 'W', 'e', 'E', 'r', 't', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P',
            'a', 's', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k', 'l', 'L',
            'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n', 'm'
        };
    }
}
