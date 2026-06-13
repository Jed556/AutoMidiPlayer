using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AutoMidiPlayer.WPF.Core.Games;

/// <summary>
/// Static metadata for a supported game. To add a new game, create a GameDefinition
/// entry in <see cref="GameRegistry"/> and follow the steps documented there.
/// </summary>
public class GameDefinition(
    string id,
    string displayName,
    string instrumentGameName,
    string imageResourcePath,
    IReadOnlyList<string> processNames,
    Func<string> getLocation,
    Action<string> setLocation,
    Func<bool> getIsActive,
    Action<bool> setIsActive,
    IReadOnlyList<string>? windowNames = null)
{
    /// <summary>Unique identifier for this game (e.g., "GenshinImpact")</summary>
    public string Id { get; } = id;

    /// <summary>Display name shown in UI (e.g., "Genshin Impact")</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Name used in InstrumentConfig.Game to associate instruments with this game</summary>
    public string InstrumentGameName { get; } = instrumentGameName;

    /// <summary>Pack URI to the game's image resource</summary>
    public string ImageResourcePath { get; } = imageResourcePath;

    /// <summary>Process names to check for running state detection</summary>
    public IReadOnlyList<string> ProcessNames { get; } = processNames;

    /// <summary>Window names to check for running state detection (optional, matches MainWindowTitle)</summary>
    public IReadOnlyList<string> WindowNames { get; } = windowNames ?? Array.Empty<string>();

    /// <summary>Getter for the persisted location setting</summary>
    public Func<string> GetLocation { get; } = getLocation;

    /// <summary>Setter for the persisted location setting</summary>
    public Action<string> SetLocation { get; } = setLocation;

    /// <summary>Getter for the persisted active/selected state setting</summary>
    public Func<bool> GetIsActive { get; } = getIsActive;

    /// <summary>Setter for the persisted active/selected state setting</summary>
    public Action<bool> SetIsActive { get; } = setIsActive;
}

/// <summary>
/// Runtime state wrapper for a game, providing observable properties for UI binding.
/// Combines static <see cref="GameDefinition"/> metadata with live state (running/selected).
/// PropertyChanged.Fody auto-weaves INotifyPropertyChanged notifications.
/// </summary>
public class GameInfo(GameDefinition definition) : INotifyPropertyChanged
{
#pragma warning disable CS0067
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    /// <summary>Static game metadata</summary>
    public GameDefinition Definition { get; } = definition;

    /// <summary>Whether the game process is currently detected as running</summary>
    public bool IsRunning { get; set; } = false;

    /// <summary>Whether this game is currently selected as the active game</summary>
    public bool IsSelected { get; set; } = definition.GetIsActive();

    /// <summary>Status text for display ("Running" when running, "Inactive" otherwise)</summary>
    public string StatusText => IsRunning ? "Running" : "Inactive";

    /// <summary>Convenience: display name from definition</summary>
    public string DisplayName => Definition.DisplayName;

    /// <summary>Convenience: image resource path from definition</summary>
    public string ImagePath => Definition.ImageResourcePath;

    /// <summary>Persisted executable location for this game</summary>
    public string Location { get; set; } = definition.GetLocation();

    /// <summary>Fody-detected: sync location changes back to settings</summary>
    private void OnLocationChanged()
    {
        Definition.SetLocation(Location);
    }
}
