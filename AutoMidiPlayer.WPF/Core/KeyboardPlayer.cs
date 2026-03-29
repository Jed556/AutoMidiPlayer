using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data.Entities;
using WindowsInput;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core;

public static class KeyboardPlayer
{
    private static readonly IInputSimulator Input = new InputSimulator();
    private static readonly int[][] SmartModes =
    [
        [0, 2, 4, 5, 7, 9, 11], // ionian (major)
        [0, 2, 3, 5, 7, 9, 10], // dorian
        [0, 1, 3, 5, 7, 8, 10], // phrygian
        [0, 2, 4, 6, 7, 9, 11], // lydian
        [0, 2, 4, 5, 7, 9, 10], // mixolydian
        [0, 2, 3, 5, 7, 8, 10], // aeolian (minor)
        [0, 1, 3, 5, 6, 8, 10]  // locrian
    ];

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

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

    /// <summary>
    /// Delay (in milliseconds) inserted between key-down and key-up when performing
    /// <see cref="KeyAction.Press"/> in InputSimulator, direct input, and window-message paths.
    /// A non-zero value can improve compatibility with games that require a
    /// slightly longer held press. Delay is handled asynchronously so playback timing
    /// is not blocked. Default is 50.
    /// </summary>
    public static int DirectInputPressDelayMs { get; set; } = 50;

    /// <summary>
    /// When using <see cref="UseDirectInput"/>, controls whether the key-up event is sent for
    /// <see cref="KeyAction.Press"/>. Default is true.
    /// </summary>
    public static bool EnableKeyUp { get; set; } = true;

    /// <summary>
    /// Send input via PostMessage to the game window instead of global SendInput.
    /// Use this for games (e.g. Sky: CotL) that detect and block injected input by
    /// checking the LLKHF_INJECTED flag in a low-level keyboard hook.
    /// PostMessage bypasses global hooks entirely.
    /// </summary>
    public static bool UseWindowMessage { get; set; } = false;

    public static int TransposeNote(
        string instrumentId, ref int noteId,
        Transpose direction = Transpose.Ignore)
    {
        if (direction is Transpose.Ignore) return noteId;
        var notes = Keyboard.GetNotes(instrumentId);
        if (notes.Count == 0)
            return noteId;
        var noteSet = new HashSet<int>(notes);

        if (direction is Transpose.Smart)
        {
            noteId = SmartTransposeNote(notes, noteSet, noteId);
            return noteId;
        }

        var minNote = notes[0];
        var maxNote = notes[0];
        for (var i = 1; i < notes.Count; i++)
        {
            if (notes[i] < minNote) minNote = notes[i];
            if (notes[i] > maxNote) maxNote = notes[i];
        }

        while (true)
        {
            if (noteSet.Contains(noteId))
                return noteId;

            if (noteId < minNote)
                noteId += 12;
            else if (noteId > maxNote)
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

    private static int SmartTransposeNote(IList<int> notes, HashSet<int> noteSet, int originalNote)
    {
        if (noteSet.Contains(originalNote))
            return originalNote;

        var (tonic, modeIndex) = DetectBestScale(notes, originalNote);
        var scalePitchClasses = BuildScalePitchClassSet(tonic, modeIndex);

        var scaleCandidates = notes.Where(note => scalePitchClasses.Contains(Mod12(note))).ToList();
        var candidates = scaleCandidates.Count > 0 ? scaleCandidates : notes.ToList();

        var best = candidates[0];
        var bestScore = double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            var originalDistance = Math.Abs(candidate - originalNote);
            var octaveDistance = Math.Abs((candidate / 12) - (originalNote / 12));
            var score = originalDistance + (octaveDistance * 0.15);

            if (score < bestScore || (Math.Abs(score - bestScore) < 0.000001 && originalDistance < Math.Abs(best - originalNote)))
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static (int tonic, int modeIndex) DetectBestScale(IList<int> noteNumbers, int centerNote)
    {
        if (noteNumbers.Count == 0)
            return (0, 0);

        var histogram = new double[12];
        foreach (var noteNumber in noteNumbers)
        {
            var pitchClass = Mod12(noteNumber);
            var weight = 1.0 / (1.0 + Math.Abs(noteNumber - centerNote));
            histogram[pitchClass] += weight;
        }

        var bestScore = double.NegativeInfinity;
        var bestTonic = 0;
        var bestMode = 0;

        for (var tonic = 0; tonic < 12; tonic++)
        {
            for (var modeIndex = 0; modeIndex < SmartModes.Length; modeIndex++)
            {
                var score = 0.0;
                foreach (var interval in SmartModes[modeIndex])
                {
                    score += histogram[(tonic + interval) % 12];
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTonic = tonic;
                    bestMode = modeIndex;
                }
            }
        }

        return (bestTonic, bestMode);
    }

    private static HashSet<int> BuildScalePitchClassSet(int tonic, int modeIndex)
    {
        var set = new HashSet<int>();
        foreach (var interval in SmartModes[modeIndex])
            set.Add((tonic + interval) % 12);
        return set;
    }

    private static int Mod12(int note) => ((note % 12) + 12) % 12;

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

        if (UseWindowMessage)
        {
            var hWnd = WindowHelper.GetActiveGameWindowHandle();
            if (hWnd.HasValue && hWnd.Value != IntPtr.Zero)
            {
                SendKeyStrokeWindow(keyStroke, hWnd.Value, action);
                return;
            }
            // Fall through to normal input if window not found
        }

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
                for (int i = modifiers.Length - 1; i >= 0; i--)
                    Input.Keyboard.KeyUp(modifiers[i]);
                break;

            case KeyAction.Up:
                Input.Keyboard.KeyUp(keyStroke.Key);
                break;

            case KeyAction.Press:
                foreach (var modifier in modifiers)
                    Input.Keyboard.KeyDown(modifier);
                Input.Keyboard.KeyDown(keyStroke.Key);

                if (EnableKeyUp)
                {
                    ScheduleDelayedAction(() =>
                    {
                        Input.Keyboard.KeyUp(keyStroke.Key);
                        for (int i = modifiers.Length - 1; i >= 0; i--)
                            Input.Keyboard.KeyUp(modifiers[i]);
                    });
                }
                else
                {
                    for (int i = modifiers.Length - 1; i >= 0; i--)
                        Input.Keyboard.KeyUp(modifiers[i]);
                }
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
                for (int i = modifiers.Length - 1; i >= 0; i--)
                    SendKeyDirect(modifiers[i], true);
                break;

            case KeyAction.Up:
                SendKeyDirect(keyStroke.Key, true);
                break;

            case KeyAction.Press:
                foreach (var modifier in modifiers)
                    SendKeyDirect(modifier, false);
                SendKeyDirect(keyStroke.Key, false);

                if (EnableKeyUp)
                {
                    ScheduleDelayedAction(() =>
                    {
                        SendKeyDirect(keyStroke.Key, true);
                        for (int i = modifiers.Length - 1; i >= 0; i--)
                            SendKeyDirect(modifiers[i], true);
                    });
                }
                else
                {
                    for (int i = modifiers.Length - 1; i >= 0; i--)
                        SendKeyDirect(modifiers[i], true);
                }
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

    private static void SendKeyStrokeWindow(Keyboard.KeyStroke keyStroke, IntPtr hWnd, KeyAction action)
    {
        // PostMessage bypasses low-level keyboard hooks and the LLKHF_INJECTED flag.
        // lParam encoding for WM_KEYDOWN: repeat=1 | (scanCode << 16)
        // lParam encoding for WM_KEYUP:  repeat=1 | (scanCode << 16) | (0xC0 << 24) [prev-state + transition bits]
        var vk = (uint)keyStroke.Key;
        var scanCode = MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        var lParamDown = (IntPtr)(1 | ((int)scanCode << 16));
        var lParamUp = (IntPtr)(1 | ((int)scanCode << 16) | unchecked((int)0xC0000000));

        switch (action)
        {
            case KeyAction.Down:
                PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, lParamDown);
                break;

            case KeyAction.Up:
                PostMessage(hWnd, WM_KEYUP, (IntPtr)vk, lParamUp);
                break;

            case KeyAction.Press:
                PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, lParamDown);
                if (EnableKeyUp)
                {
                    ScheduleDelayedAction(() =>
                    {
                        PostMessage(hWnd, WM_KEYUP, (IntPtr)vk, lParamUp);
                    });
                }
                break;
        }
    }

    private static void ScheduleDelayedAction(Action action)
    {
        var delayMs = Math.Max(0, DirectInputPressDelayMs);

        if (delayMs == 0)
        {
            action();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                action();
            }
            catch
            {
                // Best-effort delayed key-up dispatch.
            }
        });
    }
}
