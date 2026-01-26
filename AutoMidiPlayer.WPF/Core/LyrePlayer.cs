using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AutoMidiPlayer.Data.Entities;
using WindowsInput;
using WindowsInput.Native;
using static AutoMidiPlayer.WPF.Core.Keyboard;

namespace AutoMidiPlayer.WPF.Core;

public static class LyrePlayer
{
    private static readonly IInputSimulator Input = new InputSimulator();

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

    // Correct struct layout for 64-bit Windows
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
        // Padding to match MOUSEINPUT size (needed for union behavior)
        private readonly uint pad1;
        private readonly uint pad2;
    }

    // Use direct SendInput for games that don't respond to InputSimulator
    public static bool UseDirectInput { get; set; } = true;

    public static int TransposeNote(
        Instrument instrument, ref int noteId,
        Transpose direction = Transpose.Ignore)
    {
        if (direction is Transpose.Ignore) return noteId;
        var notes = GetNotes(instrument);
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

    public static void NoteDown(int noteId, Layout layout, Instrument instrument)
        => InteractNote(noteId, layout, instrument, Input.Keyboard.KeyDown);

    public static void NoteUp(int noteId, Layout layout, Instrument instrument)
        => InteractNote(noteId, layout, instrument, Input.Keyboard.KeyUp);

    public static void PlayNote(int noteId, Layout layout, Instrument instrument)
        => InteractNote(noteId, layout, instrument, Input.Keyboard.KeyPress);

    public static bool TryGetKey(Layout layout, Instrument instrument, int noteId, out VirtualKeyCode key)
    {
        var keys = GetLayout(layout, instrument);
        var notes = GetNotes(instrument);
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
        int noteId, Layout layout, Instrument instrument,
        Func<VirtualKeyCode, IKeyboardSimulator> action)
    {
        if (TryGetKey(layout, instrument, noteId, out var key))
        {
            if (UseDirectInput)
            {
                // Use direct Win32 SendInput with scan codes for better game compatibility
                SendKeyDirect(key, action == Input.Keyboard.KeyUp);
            }
            else
            {
                action.Invoke(key);
            }
        }
    }

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

        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }
}
