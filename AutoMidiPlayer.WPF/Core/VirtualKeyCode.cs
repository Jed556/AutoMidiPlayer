// ReSharper disable InconsistentNaming
namespace AutoMidiPlayer.WPF.Core;

/// <summary>
/// Win32 virtual key codes used for keyboard input injection.
/// Values match the VK_* constants from the Windows API.
/// </summary>
public enum VirtualKeyCode : ushort
{
    // Modifier keys
    SHIFT     = 0x10,
    CONTROL   = 0x11,
    MENU      = 0x12,  // Alt

    // Whitespace
    SPACE     = 0x20,

    // Digits 0–9
    VK_0 = 0x30,
    VK_1 = 0x31,
    VK_2 = 0x32,
    VK_3 = 0x33,
    VK_4 = 0x34,
    VK_5 = 0x35,
    VK_6 = 0x36,
    VK_7 = 0x37,
    VK_8 = 0x38,
    VK_9 = 0x39,

    // Letters A–Z
    VK_A = 0x41,
    VK_B = 0x42,
    VK_C = 0x43,
    VK_D = 0x44,
    VK_E = 0x45,
    VK_F = 0x46,
    VK_G = 0x47,
    VK_H = 0x48,
    VK_I = 0x49,
    VK_J = 0x4A,
    VK_K = 0x4B,
    VK_L = 0x4C,
    VK_M = 0x4D,
    VK_N = 0x4E,
    VK_O = 0x4F,
    VK_P = 0x50,
    VK_Q = 0x51,
    VK_R = 0x52,
    VK_S = 0x53,
    VK_T = 0x54,
    VK_U = 0x55,
    VK_V = 0x56,
    VK_W = 0x57,
    VK_X = 0x58,
    VK_Y = 0x59,
    VK_Z = 0x5A,

    // OEM keys (US standard keyboard)
    OEM_1      = 0xBA,  // ;:
    OEM_PLUS   = 0xBB,  // =+
    OEM_COMMA  = 0xBC,  // ,<
    OEM_MINUS  = 0xBD,  // -_
    OEM_PERIOD = 0xBE,  // .>
    OEM_2      = 0xBF,  // /?
    OEM_4      = 0xDB,  // [{
    OEM_5      = 0xDC,  // \|
    OEM_6      = 0xDD,  // ]}
    OEM_7      = 0xDE,  // '"
}
