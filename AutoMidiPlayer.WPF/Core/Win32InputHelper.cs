using System;
using System.Runtime.InteropServices;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core;

/// <summary>
/// Shared Win32 keyboard input helper for direct game input.
/// Provides low-level SendInput functionality used by both KeyboardPlayer and RobloxKeyboardPlayer.
/// </summary>
internal static class Win32InputHelper
{
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
        // Padding to match MOUSEINPUT size (INPUT structure uses union in Win32, requires matching sizes)
        // These fields ensure proper memory layout for the INPUT union structure
        private readonly uint padding1;
        private readonly uint padding2;
    }

    /// <summary>
    /// Sends a key event using Win32 SendInput API.
    /// </summary>
    /// <param name="key">The virtual key code to send</param>
    /// <param name="keyUp">True for key up event, false for key down event</param>
    public static void SendKeyDirect(VirtualKeyCode key, bool keyUp)
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
