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

    public KeyboardLayoutConfig(string name, IReadOnlyList<VirtualKeyCode> keys)
    {
        Name = name;
        KeyStrokes = keys.Select(key => new Keyboard.KeyStroke(key)).ToArray();
    }

    public KeyboardLayoutConfig(string name, IReadOnlyList<Keyboard.KeyStroke> keyStrokes)
    {
        Name = name;
        KeyStrokes = keyStrokes;
    }

    public KeyboardLayoutConfig(string name, IReadOnlyList<char> characters)
    {
        Name = name;
        KeyStrokes = characters
            .Select(character => Keyboard.TryGetKeyStrokeForCharacter(character, out var keyStroke)
                ? keyStroke
                : new Keyboard.KeyStroke(VirtualKeyCode.SPACE))
            .ToArray();
    }
}
