using System.Collections.Generic;
using System.Linq;
using AutoMidiPlayer.WPF.Core;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core.Instruments;

public class KeyboardLayoutConfig
{
    public string Name { get; }

    public IReadOnlyList<Keyboard.KeyStroke> KeyStrokes { get; }

    public IReadOnlyList<VirtualKeyCode> Keys => KeyStrokes.Select(k => k.Key).ToArray();

    public KeyboardLayoutConfig(string name, IReadOnlyList<VirtualKeyCode> keyCodes)
    {
        Name = name;
        KeyStrokes = keyCodes.Select(key => new Keyboard.KeyStroke(key)).ToArray();
    }

    public KeyboardLayoutConfig(string name, IReadOnlyList<Keyboard.KeyStroke> keyStrokes)
    {
        Name = name;
        KeyStrokes = keyStrokes;
    }

    /// <summary>
    /// Initialize a keyboard layout from a collection of string elements.
    /// 
    /// Each element can be:
    ///   - Single character: "a", "b", "1", "!" → uses existing character-to-keystroke map
    ///   - Ctrl prefix: "^a", "^b", "^1" → Ctrl+key modifier
    ///   - Ctrl+Shift: "^A", "^B", "^C" → Ctrl+Shift+key modifiers
    ///   - Special: "^" alone → Shift+6 (the caret character itself, not a Ctrl modifier)
    /// 
    /// Examples:
    ///   new KeyboardLayoutConfig("Standard", ["n", "m", ",", ".", "/", "h", "j", "k", "l", ";", "y", "u", "i", "o", "p"])
    ///   new KeyboardLayoutConfig("Extended", ["^n", "^m", "^,", "^.", "^/", "^h", "^j", "^k", "^l", "^;", "^y", "^u", "^i", "^o", "^p"])
    ///   new KeyboardLayoutConfig("Mixed", ["n", "^n", "m", "^m", ...])
    /// </summary>
    public KeyboardLayoutConfig(string name, IReadOnlyList<string> keys)
    {
        Name = name;
        KeyStrokes = Keyboard.ParseLayoutKeys(keys);
    }
}
