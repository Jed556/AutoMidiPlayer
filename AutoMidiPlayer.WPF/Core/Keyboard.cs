using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using AutoMidiPlayer.WPF.Core.Instruments;
using WindowsInput.Native;

namespace AutoMidiPlayer.WPF.Core;

/// <summary>
/// Central keyboard configuration containing instrument and layout definitions.
/// Game-specific instrument configurations are discovered dynamically from the Games folder.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "StringLiteralTypo")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
public static class Keyboard
{
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Shift = 1,
        Ctrl = 2,
        Alt = 4
    }

    public readonly record struct KeyStroke(VirtualKeyCode Key, KeyModifiers Modifiers = KeyModifiers.None);

    private static readonly Dictionary<char, KeyStroke> CharacterToKeyStroke = BuildCharacterToKeyStrokeMap();

    private static readonly InstrumentConfig EmptyInstrument = new(
        game: "System",
        name: "Empty",
        notes: new List<int>(),
        keyboardLayouts: Array.Empty<KeyboardLayoutConfig>());

    #region Display Names

    /// <summary>
    /// Instrument display names discovered dynamically from game files.
    /// Instrument id is the instrument Name string.
    /// </summary>
    private static readonly Dictionary<string, InstrumentConfig> _instrumentRegistry = BuildInstrumentRegistry();

    private static readonly Dictionary<string, KeyboardLayoutConfig> _layoutRegistry = BuildLayoutRegistry();

    public static readonly IReadOnlyDictionary<string, string> InstrumentNames =
        _instrumentRegistry.ToDictionary(kv => kv.Key, kv => kv.Value.Name);

    /// <summary>
    /// Layout display names discovered dynamically from game KeyboardLayout files.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LayoutNames =
        _layoutRegistry.ToDictionary(kv => kv.Key, kv => kv.Value.Name);

    private static Dictionary<string, InstrumentConfig> BuildInstrumentRegistry()
    {
        var dict = new Dictionary<string, InstrumentConfig>(StringComparer.OrdinalIgnoreCase);

        var fields = typeof(Keyboard).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "AutoMidiPlayer.WPF.Core.Instruments")
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(InstrumentConfig));

        foreach (var field in fields)
        {
            if (field.GetValue(null) is not InstrumentConfig config)
                continue;

            if (string.IsNullOrWhiteSpace(config.Name))
                continue;

            dict[config.Name] = config;
        }

        return dict
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, KeyboardLayoutConfig> BuildLayoutRegistry()
    {
        var dict = new Dictionary<string, KeyboardLayoutConfig>(StringComparer.OrdinalIgnoreCase);

        var fields = typeof(Keyboard).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "AutoMidiPlayer.WPF.Core.Instruments")
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(KeyboardLayoutConfig));

        foreach (var field in fields)
        {
            if (field.GetValue(null) is not KeyboardLayoutConfig layout)
                continue;

            if (string.IsNullOrWhiteSpace(layout.Name))
                continue;

            dict[layout.Name] = layout;
        }

        return dict
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static KeyValuePair<string, string> GetInstrumentAtIndex(int index)
    {
        var list = InstrumentNames.ToList();
        if (list.Count == 0)
            return default;

        return index >= 0 && index < list.Count ? list[index] : list[0];
    }

    public static KeyValuePair<string, string> GetLayoutAtIndex(int index)
    {
        var list = LayoutNames.ToList();
        if (list.Count == 0)
            return default;

        return index >= 0 && index < list.Count ? list[index] : list[0];
    }

    public static int GetInstrumentIndex(string instrumentId)
    {
        var list = InstrumentNames.Keys.ToList();
        var idx = list.FindIndex(id => string.Equals(id, instrumentId, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : 0;
    }

    public static int GetLayoutIndex(string layoutName)
    {
        var list = LayoutNames.Keys.ToList();
        var idx = list.FindIndex(name => string.Equals(name, layoutName, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : 0;
    }

    public static IReadOnlyDictionary<string, string> GetInstrumentNamesForGames(IEnumerable<string> activeGames)
    {
        var games = activeGames
            .Where(game => !string.IsNullOrWhiteSpace(game))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (games.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return _instrumentRegistry
            .Where(kv => games.Contains(kv.Value.Game))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, string> GetLayoutNamesForInstrument(string instrumentId)
    {
        var config = GetInstrumentConfig(instrumentId);

        var layouts = config.KeyboardLayouts
            .GroupBy(layout => layout.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(layout => layout.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(layout => layout.Name, layout => layout.Name, StringComparer.OrdinalIgnoreCase);

        return layouts;
    }

    #endregion

    // Keyboard layout tables live in game-specific layout files (see Core/Games/*/KeyboardLayout.cs)
    // and are discovered dynamically.

    private static Dictionary<char, KeyStroke> BuildCharacterToKeyStrokeMap()
    {
        var shift = KeyModifiers.Shift;
        return new Dictionary<char, KeyStroke>
        {
            { '0', new KeyStroke(VirtualKeyCode.VK_0) },
            { '1', new KeyStroke(VirtualKeyCode.VK_1) },
            { '2', new KeyStroke(VirtualKeyCode.VK_2) },
            { '3', new KeyStroke(VirtualKeyCode.VK_3) },
            { '4', new KeyStroke(VirtualKeyCode.VK_4) },
            { '5', new KeyStroke(VirtualKeyCode.VK_5) },
            { '6', new KeyStroke(VirtualKeyCode.VK_6) },
            { '7', new KeyStroke(VirtualKeyCode.VK_7) },
            { '8', new KeyStroke(VirtualKeyCode.VK_8) },
            { '9', new KeyStroke(VirtualKeyCode.VK_9) },

            { '!', new KeyStroke(VirtualKeyCode.VK_1, shift) },
            { '@', new KeyStroke(VirtualKeyCode.VK_2, shift) },
            { '#', new KeyStroke(VirtualKeyCode.VK_3, shift) },
            { '$', new KeyStroke(VirtualKeyCode.VK_4, shift) },
            { '%', new KeyStroke(VirtualKeyCode.VK_5, shift) },
            { '^', new KeyStroke(VirtualKeyCode.VK_6, shift) },
            { '&', new KeyStroke(VirtualKeyCode.VK_7, shift) },
            { '*', new KeyStroke(VirtualKeyCode.VK_8, shift) },
            { '(', new KeyStroke(VirtualKeyCode.VK_9, shift) },
            { ')', new KeyStroke(VirtualKeyCode.VK_0, shift) },

            { 'a', new KeyStroke(VirtualKeyCode.VK_A) },
            { 'b', new KeyStroke(VirtualKeyCode.VK_B) },
            { 'c', new KeyStroke(VirtualKeyCode.VK_C) },
            { 'd', new KeyStroke(VirtualKeyCode.VK_D) },
            { 'e', new KeyStroke(VirtualKeyCode.VK_E) },
            { 'f', new KeyStroke(VirtualKeyCode.VK_F) },
            { 'g', new KeyStroke(VirtualKeyCode.VK_G) },
            { 'h', new KeyStroke(VirtualKeyCode.VK_H) },
            { 'i', new KeyStroke(VirtualKeyCode.VK_I) },
            { 'j', new KeyStroke(VirtualKeyCode.VK_J) },
            { 'k', new KeyStroke(VirtualKeyCode.VK_K) },
            { 'l', new KeyStroke(VirtualKeyCode.VK_L) },
            { 'm', new KeyStroke(VirtualKeyCode.VK_M) },
            { 'n', new KeyStroke(VirtualKeyCode.VK_N) },
            { 'o', new KeyStroke(VirtualKeyCode.VK_O) },
            { 'p', new KeyStroke(VirtualKeyCode.VK_P) },
            { 'q', new KeyStroke(VirtualKeyCode.VK_Q) },
            { 'r', new KeyStroke(VirtualKeyCode.VK_R) },
            { 's', new KeyStroke(VirtualKeyCode.VK_S) },
            { 't', new KeyStroke(VirtualKeyCode.VK_T) },
            { 'u', new KeyStroke(VirtualKeyCode.VK_U) },
            { 'v', new KeyStroke(VirtualKeyCode.VK_V) },
            { 'w', new KeyStroke(VirtualKeyCode.VK_W) },
            { 'x', new KeyStroke(VirtualKeyCode.VK_X) },
            { 'y', new KeyStroke(VirtualKeyCode.VK_Y) },
            { 'z', new KeyStroke(VirtualKeyCode.VK_Z) },

            { 'A', new KeyStroke(VirtualKeyCode.VK_A, shift) },
            { 'B', new KeyStroke(VirtualKeyCode.VK_B, shift) },
            { 'C', new KeyStroke(VirtualKeyCode.VK_C, shift) },
            { 'D', new KeyStroke(VirtualKeyCode.VK_D, shift) },
            { 'E', new KeyStroke(VirtualKeyCode.VK_E, shift) },
            { 'F', new KeyStroke(VirtualKeyCode.VK_F, shift) },
            { 'G', new KeyStroke(VirtualKeyCode.VK_G, shift) },
            { 'H', new KeyStroke(VirtualKeyCode.VK_H, shift) },
            { 'I', new KeyStroke(VirtualKeyCode.VK_I, shift) },
            { 'J', new KeyStroke(VirtualKeyCode.VK_J, shift) },
            { 'K', new KeyStroke(VirtualKeyCode.VK_K, shift) },
            { 'L', new KeyStroke(VirtualKeyCode.VK_L, shift) },
            { 'M', new KeyStroke(VirtualKeyCode.VK_M, shift) },
            { 'N', new KeyStroke(VirtualKeyCode.VK_N, shift) },
            { 'O', new KeyStroke(VirtualKeyCode.VK_O, shift) },
            { 'P', new KeyStroke(VirtualKeyCode.VK_P, shift) },
            { 'Q', new KeyStroke(VirtualKeyCode.VK_Q, shift) },
            { 'R', new KeyStroke(VirtualKeyCode.VK_R, shift) },
            { 'S', new KeyStroke(VirtualKeyCode.VK_S, shift) },
            { 'T', new KeyStroke(VirtualKeyCode.VK_T, shift) },
            { 'U', new KeyStroke(VirtualKeyCode.VK_U, shift) },
            { 'V', new KeyStroke(VirtualKeyCode.VK_V, shift) },
            { 'W', new KeyStroke(VirtualKeyCode.VK_W, shift) },
            { 'X', new KeyStroke(VirtualKeyCode.VK_X, shift) },
            { 'Y', new KeyStroke(VirtualKeyCode.VK_Y, shift) },
            { 'Z', new KeyStroke(VirtualKeyCode.VK_Z, shift) },

            { '-', new KeyStroke(VirtualKeyCode.OEM_MINUS) },
            { '=', new KeyStroke(VirtualKeyCode.OEM_PLUS) },
            { '[', new KeyStroke(VirtualKeyCode.OEM_4) },
            { ']', new KeyStroke(VirtualKeyCode.OEM_6) },
            { '\\', new KeyStroke(VirtualKeyCode.OEM_5) },
            { ';', new KeyStroke(VirtualKeyCode.OEM_1) },
            { '\'', new KeyStroke(VirtualKeyCode.OEM_7) },
            { ',', new KeyStroke(VirtualKeyCode.OEM_COMMA) },
            { '.', new KeyStroke(VirtualKeyCode.OEM_PERIOD) },
            { '/', new KeyStroke(VirtualKeyCode.OEM_2) },

            { '_', new KeyStroke(VirtualKeyCode.OEM_MINUS, shift) },
            { '+', new KeyStroke(VirtualKeyCode.OEM_PLUS, shift) },
            { '{', new KeyStroke(VirtualKeyCode.OEM_4, shift) },
            { '}', new KeyStroke(VirtualKeyCode.OEM_6, shift) },
            { '|', new KeyStroke(VirtualKeyCode.OEM_5, shift) },
            { ':', new KeyStroke(VirtualKeyCode.OEM_1, shift) },
            { '"', new KeyStroke(VirtualKeyCode.OEM_7, shift) },
            { '<', new KeyStroke(VirtualKeyCode.OEM_COMMA, shift) },
            { '>', new KeyStroke(VirtualKeyCode.OEM_PERIOD, shift) },
            { '?', new KeyStroke(VirtualKeyCode.OEM_2, shift) },

            { ' ', new KeyStroke(VirtualKeyCode.SPACE) }
        };
    }

    public static bool TryGetKeyStrokeForCharacter(char character, out KeyStroke keyStroke)
        => CharacterToKeyStroke.TryGetValue(character, out keyStroke);

    #region Helper Methods

    /// <summary>
    /// Get the instrument configuration for the specified instrument
    /// </summary>
    public static InstrumentConfig GetInstrumentConfig(string? instrumentId)
    {
        if (!string.IsNullOrWhiteSpace(instrumentId)
            && _instrumentRegistry.TryGetValue(instrumentId, out var cfg))
            return cfg;

        // fallback: return first discovered instrument if requested id not found
        return _instrumentRegistry.Values.FirstOrDefault() ?? EmptyInstrument;
    }

    /// <summary>
    /// Get the key layout for the specified keyboard layout and instrument
    /// </summary>
    public static IReadOnlyList<KeyStroke> GetLayout(string? layoutName, string? instrumentId)
    {
        var config = GetInstrumentConfig(instrumentId);

        if (config.KeyboardLayouts.Count == 0)
            return _layoutRegistry.Values.FirstOrDefault()?.KeyStrokes ?? Array.Empty<KeyStroke>();

        var match = config.KeyboardLayouts
            .FirstOrDefault(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));

        return (match ?? config.KeyboardLayouts[0]).KeyStrokes;
    }

    /// <summary>
    /// Get the MIDI notes for the specified instrument id
    /// </summary>
    public static IList<int> GetNotes(string? instrumentId)
    {
        var config = GetInstrumentConfig(instrumentId);
        return config.Notes;
    }

    #endregion
}
