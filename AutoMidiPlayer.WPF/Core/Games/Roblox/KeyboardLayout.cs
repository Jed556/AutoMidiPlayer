using System;
using System.Collections.Generic;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Roblox keyboard layouts for character-based piano input.
/// Maps characters to their corresponding VirtualKeyCode, including support for Shift-modified characters.
/// </summary>
internal static class RobloxKeyboardLayouts
{
    /// <summary>
    /// Mapping of characters to their VirtualKeyCode and whether Shift is required.
    /// Used for Roblox piano games that accept character input.
    /// </summary>
    private static readonly Dictionary<char, (VirtualKeyCode key, bool requiresShift)> CharacterToKeyMap = new()
    {
        // Numbers (no shift)
        { '0', (VirtualKeyCode.VK_0, false) },
        { '1', (VirtualKeyCode.VK_1, false) },
        { '2', (VirtualKeyCode.VK_2, false) },
        { '3', (VirtualKeyCode.VK_3, false) },
        { '4', (VirtualKeyCode.VK_4, false) },
        { '5', (VirtualKeyCode.VK_5, false) },
        { '6', (VirtualKeyCode.VK_6, false) },
        { '7', (VirtualKeyCode.VK_7, false) },
        { '8', (VirtualKeyCode.VK_8, false) },
        { '9', (VirtualKeyCode.VK_9, false) },

        // Number row symbols (with shift)
        { '!', (VirtualKeyCode.VK_1, true) },
        { '@', (VirtualKeyCode.VK_2, true) },
        { '#', (VirtualKeyCode.VK_3, true) },
        { '$', (VirtualKeyCode.VK_4, true) },
        { '%', (VirtualKeyCode.VK_5, true) },
        { '^', (VirtualKeyCode.VK_6, true) },
        { '&', (VirtualKeyCode.VK_7, true) },
        { '*', (VirtualKeyCode.VK_8, true) },
        { '(', (VirtualKeyCode.VK_9, true) },
        { ')', (VirtualKeyCode.VK_0, true) },

        // Lowercase letters (no shift)
        { 'a', (VirtualKeyCode.VK_A, false) },
        { 'b', (VirtualKeyCode.VK_B, false) },
        { 'c', (VirtualKeyCode.VK_C, false) },
        { 'd', (VirtualKeyCode.VK_D, false) },
        { 'e', (VirtualKeyCode.VK_E, false) },
        { 'f', (VirtualKeyCode.VK_F, false) },
        { 'g', (VirtualKeyCode.VK_G, false) },
        { 'h', (VirtualKeyCode.VK_H, false) },
        { 'i', (VirtualKeyCode.VK_I, false) },
        { 'j', (VirtualKeyCode.VK_J, false) },
        { 'k', (VirtualKeyCode.VK_K, false) },
        { 'l', (VirtualKeyCode.VK_L, false) },
        { 'm', (VirtualKeyCode.VK_M, false) },
        { 'n', (VirtualKeyCode.VK_N, false) },
        { 'o', (VirtualKeyCode.VK_O, false) },
        { 'p', (VirtualKeyCode.VK_P, false) },
        { 'q', (VirtualKeyCode.VK_Q, false) },
        { 'r', (VirtualKeyCode.VK_R, false) },
        { 's', (VirtualKeyCode.VK_S, false) },
        { 't', (VirtualKeyCode.VK_T, false) },
        { 'u', (VirtualKeyCode.VK_U, false) },
        { 'v', (VirtualKeyCode.VK_V, false) },
        { 'w', (VirtualKeyCode.VK_W, false) },
        { 'x', (VirtualKeyCode.VK_X, false) },
        { 'y', (VirtualKeyCode.VK_Y, false) },
        { 'z', (VirtualKeyCode.VK_Z, false) },

        // Uppercase letters (with shift)
        { 'A', (VirtualKeyCode.VK_A, true) },
        { 'B', (VirtualKeyCode.VK_B, true) },
        { 'C', (VirtualKeyCode.VK_C, true) },
        { 'D', (VirtualKeyCode.VK_D, true) },
        { 'E', (VirtualKeyCode.VK_E, true) },
        { 'F', (VirtualKeyCode.VK_F, true) },
        { 'G', (VirtualKeyCode.VK_G, true) },
        { 'H', (VirtualKeyCode.VK_H, true) },
        { 'I', (VirtualKeyCode.VK_I, true) },
        { 'J', (VirtualKeyCode.VK_J, true) },
        { 'K', (VirtualKeyCode.VK_K, true) },
        { 'L', (VirtualKeyCode.VK_L, true) },
        { 'M', (VirtualKeyCode.VK_M, true) },
        { 'N', (VirtualKeyCode.VK_N, true) },
        { 'O', (VirtualKeyCode.VK_O, true) },
        { 'P', (VirtualKeyCode.VK_P, true) },
        { 'Q', (VirtualKeyCode.VK_Q, true) },
        { 'R', (VirtualKeyCode.VK_R, true) },
        { 'S', (VirtualKeyCode.VK_S, true) },
        { 'T', (VirtualKeyCode.VK_T, true) },
        { 'U', (VirtualKeyCode.VK_U, true) },
        { 'V', (VirtualKeyCode.VK_V, true) },
        { 'W', (VirtualKeyCode.VK_W, true) },
        { 'X', (VirtualKeyCode.VK_X, true) },
        { 'Y', (VirtualKeyCode.VK_Y, true) },
        { 'Z', (VirtualKeyCode.VK_Z, true) },

        // Common symbols
        { '-', (VirtualKeyCode.OEM_MINUS, false) },
        { '=', (VirtualKeyCode.OEM_PLUS, false) },
        { '[', (VirtualKeyCode.OEM_4, false) },
        { ']', (VirtualKeyCode.OEM_6, false) },
        { '\\', (VirtualKeyCode.OEM_5, false) },
        { ';', (VirtualKeyCode.OEM_1, false) },
        { '\'', (VirtualKeyCode.OEM_7, false) },
        { ',', (VirtualKeyCode.OEM_COMMA, false) },
        { '.', (VirtualKeyCode.OEM_PERIOD, false) },
        { '/', (VirtualKeyCode.OEM_2, false) },

        // Shifted symbols
        { '_', (VirtualKeyCode.OEM_MINUS, true) },
        { '+', (VirtualKeyCode.OEM_PLUS, true) },
        { '{', (VirtualKeyCode.OEM_4, true) },
        { '}', (VirtualKeyCode.OEM_6, true) },
        { '|', (VirtualKeyCode.OEM_5, true) },
        { ':', (VirtualKeyCode.OEM_1, true) },
        { '"', (VirtualKeyCode.OEM_7, true) },
        { '<', (VirtualKeyCode.OEM_COMMA, true) },
        { '>', (VirtualKeyCode.OEM_PERIOD, true) },
        { '?', (VirtualKeyCode.OEM_2, true) },

        // Space
        { ' ', (VirtualKeyCode.SPACE, false) }
    };

    /// <summary>
    /// Converts a character to its VirtualKeyCode representation.
    /// </summary>
    /// <param name="character">The character to convert</param>
    /// <param name="requiresShift">Output parameter indicating if Shift key is required</param>
    /// <returns>The VirtualKeyCode for the character, or null if not found</returns>
    public static VirtualKeyCode? GetVirtualKeyForCharacter(char character, out bool requiresShift)
    {
        if (CharacterToKeyMap.TryGetValue(character, out var mapping))
        {
            requiresShift = mapping.requiresShift;
            return mapping.key;
        }

        requiresShift = false;
        return null;
    }

    /// <summary>
    /// Default MIRP layout for Roblox Piano.
    /// Uses character-based input converted to VirtualKeyCodes.
    /// This layout dynamically converts the 61-character keymap to key presses.
    /// </summary>
    public static readonly KeyboardLayoutConfig MIRP_Default = new(
        name: "MIRP Default",
        keys: BuildKeysFromCharacters(GetDefaultMIRPCharacters())
    );

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

    /// <summary>
    /// Builds a list of VirtualKeyCodes from a character array.
    /// Note: This doesn't handle Shift properly in the current architecture,
    /// so we'll need to handle that separately in the KeyboardPlayer.
    /// </summary>
    private static List<VirtualKeyCode> BuildKeysFromCharacters(char[] characters)
    {
        var keys = new List<VirtualKeyCode>();
        foreach (var ch in characters)
        {
            var vk = GetVirtualKeyForCharacter(ch, out _);
            keys.Add(vk ?? VirtualKeyCode.SPACE); // Fallback to space if not found
        }
        return keys;
    }
}
