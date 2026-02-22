using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AutoMidiPlayer.WPF.Core.Games;

/// <summary>
/// Static metadata for a supported game. To add a new game, create a GameDefinition
/// entry in <see cref="GameRegistry"/> and follow the steps documented there.
/// </summary>
public class GameDefinition
{
    /// <summary>Unique identifier for this game (e.g., "GenshinImpact")</summary>
    public string Id { get; }

    /// <summary>Display name shown in UI (e.g., "Genshin Impact")</summary>
    public string DisplayName { get; }

    /// <summary>Name used in InstrumentConfig.Game to associate instruments with this game</summary>
    public string InstrumentGameName { get; }

    /// <summary>Pack URI to the game's image resource</summary>
    public string ImageResourcePath { get; }

    /// <summary>Process names to check for running state detection</summary>
    public IReadOnlyList<string> ProcessNames { get; }

    /// <summary>Default executable filename (e.g., "GenshinImpact.exe")</summary>
    public string DefaultExeName { get; }

    /// <summary>List of default file system paths to search for the game executable on fresh start</summary>
    public IReadOnlyList<string> DefaultSearchPaths { get; }

    /// <summary>Getter for the persisted location setting</summary>
    public Func<string> GetLocation { get; }

    /// <summary>Setter for the persisted location setting</summary>
    public Action<string> SetLocation { get; }

    /// <summary>Getter for the persisted active/selected state setting</summary>
    public Func<bool> GetIsActive { get; }

    /// <summary>Setter for the persisted active/selected state setting</summary>
    public Action<bool> SetIsActive { get; }

    public GameDefinition(
        string id,
        string displayName,
        string instrumentGameName,
        string imageResourcePath,
        IReadOnlyList<string> processNames,
        string defaultExeName,
        IReadOnlyList<string> defaultSearchPaths,
        Func<string> getLocation,
        Action<string> setLocation,
        Func<bool> getIsActive,
        Action<bool> setIsActive)
    {
        Id = id;
        DisplayName = displayName;
        InstrumentGameName = instrumentGameName;
        ImageResourcePath = imageResourcePath;
        ProcessNames = processNames;
        DefaultExeName = defaultExeName;
        DefaultSearchPaths = defaultSearchPaths;
        GetLocation = getLocation;
        SetLocation = setLocation;
        GetIsActive = getIsActive;
        SetIsActive = setIsActive;
    }
}

/// <summary>
/// Runtime state wrapper for a game, providing observable properties for UI binding.
/// Combines static <see cref="GameDefinition"/> metadata with live state (running/selected).
/// PropertyChanged.Fody auto-weaves INotifyPropertyChanged notifications.
/// </summary>
public class GameInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Static game metadata</summary>
    public GameDefinition Definition { get; }

    /// <summary>Whether the game process is currently detected as running</summary>
    public bool IsRunning { get; set; }

    /// <summary>Whether this game is currently selected as the active game</summary>
    public bool IsSelected { get; set; }

    /// <summary>Status text for display ("Active" when running, "Inactive" otherwise)</summary>
    public string StatusText => IsRunning ? "Active" : "Inactive";

    /// <summary>Convenience: display name from definition</summary>
    public string DisplayName => Definition.DisplayName;

    /// <summary>Convenience: image resource path from definition</summary>
    public string ImagePath => Definition.ImageResourcePath;

    /// <summary>Persisted executable location for this game</summary>
    public string Location { get; set; }

    public GameInfo(GameDefinition definition)
    {
        Definition = definition;
        Location = definition.GetLocation();
        IsRunning = false;
        IsSelected = definition.GetIsActive();
    }

    /// <summary>Fody-detected: sync location changes back to settings</summary>
    private void OnLocationChanged()
    {
        Definition.SetLocation(Location);
    }
}
