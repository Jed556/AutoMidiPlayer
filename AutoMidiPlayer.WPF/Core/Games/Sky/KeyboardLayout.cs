using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Core.Instruments;

/// <summary>
/// Sky: Children of the Light keyboard layouts.
/// Standard 15-note (3×5 grid), 8-note (2×4 grid), and 4-note (2×2 grid).
/// Key order maps bottom-row-left-to-right first, then middle, then top.
/// </summary>
internal static class SkyKeyboardLayouts
{
    /// <summary>
    /// 15-note QWERTY layout for standard pitched instruments.
    /// Grid:  Y U I O P  (top row,    notes 11-15)
    ///        H J K L ;  (middle row, notes 6-10)
    ///        N M , . /  (bottom row, notes 1-5)
    /// </summary>
    public static readonly KeyboardLayoutConfig QWERTY_15 = new(
        name: "QWERTY",
        keys: [
            'n', 'm', ',', '.', '/',
            'h', 'j', 'k', 'l', ';',
            'y', 'u', 'i', 'o', 'p'
            ]);

    /// <summary>
    /// 8-note QWERTY layout for percussion instruments.
    /// Grid:  Y U I O  (top row,    notes 5-8)
    ///        P H J K  (bottom row, notes 1-4)
    /// </summary>
    public static readonly KeyboardLayoutConfig QWERTY_8 = new(
        name: "QWERTY",
        keys: [
            'p', 'h', 'j', 'k',
            'y', 'u', 'i', 'o'
            ]);

    /// <summary>
    /// 4-note QWERTY layout (notable percussion exception).
    /// Grid:  Y U  (top row,    notes 3-4)
    ///        I O  (bottom row, notes 1-2)
    /// </summary>
    public static readonly KeyboardLayoutConfig QWERTY_4 = new(
        name: "QWERTY",
        keys: [
            'i', 'o',
            'y', 'u'
        ]);
}
