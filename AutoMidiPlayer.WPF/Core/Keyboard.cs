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
    public static IEnumerable<VirtualKeyCode> GetLayout(string? layoutName, string? instrumentId)
    {
        var config = GetInstrumentConfig(instrumentId);

        if (config.KeyboardLayouts.Count == 0)
            return _layoutRegistry.Values.FirstOrDefault()?.Keys ?? Array.Empty<VirtualKeyCode>();

        var match = config.KeyboardLayouts
            .FirstOrDefault(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));

        return (match ?? config.KeyboardLayouts[0]).Keys;
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
