using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AutoMidiPlayer.Data.Properties;

namespace AutoMidiPlayer.WPF.Core.Games;

/// <summary>
/// Central registry of all supported games. To add a new game:
/// <list type="number">
///   <item>Add a <see cref="GameDefinition"/> entry to <see cref="AllGames"/> below</item>
///   <item>Create instrument configs in Core/Games/{GameName}/Instruments/</item>
///   <item>Create keyboard layouts in Core/Games/{GameName}/KeyboardLayout.cs</item>
///   <item>Add location + active settings to Settings.settings and Settings.Designer.cs</item>
///   <item>Add a game image to Resources/{GameName}.png</item>
/// </list>
/// </summary>
public static class GameRegistry
{
    private static readonly Settings Settings = Settings.Default;

    // Cache IsGameRunning results to avoid per-note process enumeration
    private static readonly ConcurrentDictionary<string, (bool result, long timestamp)> _gameRunningCache = new();
    private const long GameRunningCacheTtlMs = 500;

    #region Game Definitions
    /// <summary>All registered games in display order</summary>
    public static readonly IReadOnlyList<GameDefinition> AllGames =
    [
        new GameDefinition(
            id: "Genshin Impact",
            displayName: "Genshin Impact",
            instrumentGameName: "Genshin Impact",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Genshin_Impact.png",
            processNames: ["GenshinImpact", "YuanShen"],
            getLocation: () => Settings.GenshinLocation,
            setLocation: v => Settings.Modify(s => s.GenshinLocation = v),
            getIsActive: () => Settings.ActiveGenshin,
            setIsActive: v => Settings.Modify(s => s.ActiveGenshin = v)
        ),
        new GameDefinition(
            id: "NTE",
            displayName: "Neverness to Everness",
            instrumentGameName: "Neverness to Everness",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/NTE.png",
            processNames: ["HTGame"],
            getLocation: () => Settings.NTELocation,
            setLocation: v => Settings.Modify(s => s.NTELocation = v),
            getIsActive: () => Settings.ActiveNTE,
            setIsActive: v => Settings.Modify(s => s.ActiveNTE = v)
        ),
        new GameDefinition(
            id: "Heartopia",
            displayName: "Heartopia",
            instrumentGameName: "Heartopia",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Heartopia.png",
            processNames: ["xdt"],
            getLocation: () => Settings.HeartopiaLocation,
            setLocation: v => Settings.Modify(s => s.HeartopiaLocation = v),
            getIsActive: () => Settings.ActiveHeartopia,
            setIsActive: v => Settings.Modify(s => s.ActiveHeartopia = v)
        ),
        new GameDefinition(
            id: "Roblox",
            displayName: "Roblox",
            instrumentGameName: "Roblox",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Roblox.png",
            processNames: ["RobloxPlayerBeta", "Roblox"],
            getLocation: () => Settings.RobloxLocation,
            setLocation: v => Settings.Modify(s => s.RobloxLocation = v),
            getIsActive: () => Settings.ActiveRoblox,
            setIsActive: v => Settings.Modify(s => s.ActiveRoblox = v)
        ),
        new GameDefinition(
            id: "Sky",
            displayName: "Sky: Children of the Light",
            instrumentGameName: "Sky",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Sky.png",
            processNames: ["Sky"],
            getLocation: () => Settings.SkyLocation,
            setLocation: v => Settings.Modify(s => s.SkyLocation = v),
            getIsActive: () => Settings.ActiveSky,
            setIsActive: v => Settings.Modify(s => s.ActiveSky = v)
        )
    ];

    #endregion


    #region Helper functions

    /// <summary>Get a game definition by its unique ID</summary>
    public static GameDefinition? GetById(string id) =>
        AllGames.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Get a game definition by its display name</summary>
    public static GameDefinition? GetByName(string displayName) =>
        AllGames.FirstOrDefault(g => string.Equals(g.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Get a game definition by its instrument game name (matches InstrumentConfig.Game)</summary>
    public static GameDefinition? GetByInstrumentGameName(string gameName) =>
        AllGames.FirstOrDefault(g => string.Equals(g.InstrumentGameName, gameName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Check if a game process is currently running.
    /// Checks both configured location process name and fallback process names.
    /// Results are cached briefly to avoid expensive per-note process enumeration.
    /// </summary>
    public static bool IsGameRunning(GameDefinition game)
    {
        var now = Stopwatch.GetTimestamp();
        var nowMs = (long)(now * 1000.0 / Stopwatch.Frequency);

        if (_gameRunningCache.TryGetValue(game.Id, out var cached) &&
            (nowMs - cached.timestamp) < GameRunningCacheTtlMs)
        {
            return cached.result;
        }

        var result = IsGameRunningCore(game);
        _gameRunningCache[game.Id] = (result, nowMs);
        return result;
    }

    private static bool IsGameRunningCore(GameDefinition game)
    {
        var processNames = new HashSet<string>(game.ProcessNames, StringComparer.OrdinalIgnoreCase);

        // Also check configured location process name
        var configuredPath = game.GetLocation();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configuredName = Path.GetFileNameWithoutExtension(configuredPath);
            if (!string.IsNullOrWhiteSpace(configuredName))
                processNames.Add(configuredName);
        }

        if (processNames.Count == 0)
            return false;

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (processNames.Contains(process.ProcessName))
                        return true;
                }
            }

            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    #endregion
}

