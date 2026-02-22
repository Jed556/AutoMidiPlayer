using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AutoMidiPlayer.Data.Entities;
using WindowsInput;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core;

public static class KeyboardPlayer
{
    private static readonly IInputSimulator Input = new InputSimulator();

    private enum KeyAction
    {
        Down,
        Up,
        Press
    }

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
        => InteractNote(noteId, layoutName, instrumentId, KeyAction.Down);

    public static void NoteUp(int noteId, string layoutName, string instrumentId)
        => InteractNote(noteId, layoutName, instrumentId, KeyAction.Up);

    public static void PlayNote(int noteId, string layoutName, string instrumentId)
        => InteractNote(noteId, layoutName, instrumentId, KeyAction.Press);

    public static bool TryGetKey(string layoutName, string instrumentId, int noteId, out VirtualKeyCode key)
    {
        var keyStrokes = Keyboard.GetLayout(layoutName, instrumentId);
        var notes = Keyboard.GetNotes(instrumentId);
        var found = TryGetKeyStroke(keyStrokes, notes, noteId, out var keyStroke);
        key = keyStroke.Key;
        return found;
    }

    private static bool TryGetKeyStroke(
        string layoutName, string instrumentId, int noteId, out Keyboard.KeyStroke keyStroke)
    {
        var keyStrokes = Keyboard.GetLayout(layoutName, instrumentId);
        var notes = Keyboard.GetNotes(instrumentId);
        return TryGetKeyStroke(keyStrokes, notes, noteId, out keyStroke);
    }

    private static bool TryGetKeyStroke(
        this IEnumerable<Keyboard.KeyStroke> keyStrokes, IList<int> notes,
        int noteId, out Keyboard.KeyStroke keyStroke)
    {
        var keyIndex = notes.IndexOf(noteId);
        keyStroke = keyStrokes.ElementAtOrDefault(keyIndex);
        return keyIndex != -1;
    }

    private static void InteractNote(
        int noteId, string layoutName, string instrumentId, KeyAction action)
    {
        if (!TryGetKeyStroke(layoutName, instrumentId, noteId, out var keyStroke))
            return;

        if (UseDirectInput)
        {
            SendKeyStrokeDirect(keyStroke, action);
        }
        else
        {
            SendKeyStrokeSimulated(keyStroke, action);
        }
    }

    private static void SendKeyStrokeSimulated(Keyboard.KeyStroke keyStroke, KeyAction action)
    {
        var modifiers = GetModifierKeys(keyStroke.Modifiers).ToArray();

        switch (action)
        {
            case KeyAction.Down:
                foreach (var modifier in modifiers)
                    Input.Keyboard.KeyDown(modifier);
                Input.Keyboard.KeyDown(keyStroke.Key);
                foreach (var modifier in modifiers.Reverse())
                    Input.Keyboard.KeyUp(modifier);
                break;

            case KeyAction.Up:
                Input.Keyboard.KeyUp(keyStroke.Key);
                break;

            case KeyAction.Press:
                foreach (var modifier in modifiers)
                    Input.Keyboard.KeyDown(modifier);
                Input.Keyboard.KeyPress(keyStroke.Key);
                foreach (var modifier in modifiers.Reverse())
                    Input.Keyboard.KeyUp(modifier);
                break;
        }
    }

    private static void SendKeyStrokeDirect(Keyboard.KeyStroke keyStroke, KeyAction action)
    {
        var modifiers = GetModifierKeys(keyStroke.Modifiers).ToArray();

        switch (action)
        {
            case KeyAction.Down:
                foreach (var modifier in modifiers)
                    SendKeyDirect(modifier, false);
                SendKeyDirect(keyStroke.Key, false);
                foreach (var modifier in modifiers.Reverse())
                    SendKeyDirect(modifier, true);
                break;

            case KeyAction.Up:
                SendKeyDirect(keyStroke.Key, true);
                break;

            case KeyAction.Press:
                foreach (var modifier in modifiers)
                    SendKeyDirect(modifier, false);
                SendKeyDirect(keyStroke.Key, false);
                SendKeyDirect(keyStroke.Key, true);
                foreach (var modifier in modifiers.Reverse())
                    SendKeyDirect(modifier, true);
                break;
        }
    }

    private static IEnumerable<VirtualKeyCode> GetModifierKeys(Keyboard.KeyModifiers modifiers)
    {
        if ((modifiers & Keyboard.KeyModifiers.Ctrl) != 0)
            yield return VirtualKeyCode.CONTROL;

        if ((modifiers & Keyboard.KeyModifiers.Alt) != 0)
            yield return VirtualKeyCode.MENU;

        if ((modifiers & Keyboard.KeyModifiers.Shift) != 0)
            yield return VirtualKeyCode.SHIFT;
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

        _ = SendInput(1, [input], Marshal.SizeOf(typeof(INPUT)));
    }
}
