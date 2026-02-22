using System;
using System.Collections.Generic;
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

    #region Game Definitions
    /// <summary>All registered games in display order</summary>
    public static readonly IReadOnlyList<GameDefinition> AllGames =
    [
        new GameDefinition(
            id: "Genshin Impact",
            displayName: "Genshin Impact",
            instrumentGameName: "Genshin Impact",
            imageResourcePath: "pack://application:,,,/Resources/Genshin_Impact.png",
            processNames: ["GenshinImpact", "YuanShen"],
            defaultExeName: "GenshinImpact.exe",
            defaultSearchPaths:
            [
                @"C:\Program Files\Genshin Impact\Genshin Impact Game\GenshinImpact.exe",
                @"C:\Program Files\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                @"D:\Genshin Impact\Genshin Impact Game\GenshinImpact.exe",
                @"D:\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                @"E:\Genshin Impact\Genshin Impact Game\GenshinImpact.exe",
                @"E:\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                @"F:\Genshin Impact\Genshin Impact Game\GenshinImpact.exe",
                @"F:\Genshin Impact\Genshin Impact Game\YuanShen.exe",
            ],
            getLocation: () => Settings.GenshinLocation,
            setLocation: v => Settings.Modify(s => s.GenshinLocation = v),
            getIsActive: () => Settings.ActiveGenshin,
            setIsActive: v => Settings.Modify(s => s.ActiveGenshin = v)
        ),
        new GameDefinition(
            id: "Heartopia",
            displayName: "Heartopia",
            instrumentGameName: "Heartopia",
            imageResourcePath: "pack://application:,,,/Resources/Heartopia.png",
            processNames: ["xdt"],
            defaultExeName: "xdt.exe",
            defaultSearchPaths:
            [
                @"C:\Program Files (x86)\Steam\steamapps\common\Heartopia\xdt.exe",
                @"C:\Program Files\Steam\steamapps\common\Heartopia\xdt.exe",
                @"D:\Steam\steamapps\common\Heartopia\xdt.exe",
                @"D:\SteamLibrary\steamapps\common\Heartopia\xdt.exe",
                @"E:\Steam\steamapps\common\Heartopia\xdt.exe",
                @"E:\SteamLibrary\steamapps\common\Heartopia\xdt.exe",
                @"F:\Steam\steamapps\common\Heartopia\xdt.exe",
                @"F:\SteamLibrary\steamapps\common\Heartopia\xdt.exe",
                @"G:\Steam\steamapps\common\Heartopia\xdt.exe",
                @"G:\SteamLibrary\steamapps\common\Heartopia\xdt.exe",
                @"G:\GAMES\Steam\steamapps\common\Heartopia\xdt.exe",
            ],
            getLocation: () => Settings.HeartopiaLocation,
            setLocation: v => Settings.Modify(s => s.HeartopiaLocation = v),
            getIsActive: () => Settings.ActiveHeartopia,
            setIsActive: v => Settings.Modify(s => s.ActiveHeartopia = v)
        ),
        new GameDefinition(
            id: "Roblox",
            displayName: "Roblox",
            instrumentGameName: "Roblox",
            imageResourcePath: "pack://application:,,,/Resources/Roblox.png",
            processNames: ["RobloxPlayerBeta", "Roblox"],
            defaultExeName: "RobloxPlayerBeta.exe",
            defaultSearchPaths:
            [
                @"C:\Program Files (x86)\Roblox\Versions\version-unknown\RobloxPlayerBeta.exe",
                @"C:\Program Files\Roblox\Versions\version-unknown\RobloxPlayerBeta.exe",
            ],
            getLocation: () => Settings.RobloxLocation,
            setLocation: v => Settings.Modify(s => s.RobloxLocation = v),
            getIsActive: () => Settings.ActiveRoblox,
            setIsActive: v => Settings.Modify(s => s.ActiveRoblox = v)
        ),
        new GameDefinition(
            id: "Sky",
            displayName: "Sky: Children of the Light",
            instrumentGameName: "Sky",
            imageResourcePath: "pack://application:,,,/Resources/Sky.png",
            processNames: ["Sky"],
            defaultExeName: "Sky.exe",
            defaultSearchPaths:
            [
                @"C:\Program Files (x86)\Steam\steamapps\common\Sky Children of the Light\Sky.exe",
                @"C:\Program Files\Steam\steamapps\common\Sky Children of the Light\Sky.exe",
                @"D:\Steam\steamapps\common\Sky Children of the Light\Sky.exe",
                @"D:\SteamLibrary\steamapps\common\Sky Children of the Light\Sky.exe",
                @"E:\Steam\steamapps\common\Sky Children of the Light\Sky.exe",
                @"E:\SteamLibrary\steamapps\common\Sky Children of the Light\Sky.exe",
                @"F:\Steam\steamapps\common\Sky Children of the Light\Sky.exe",
                @"F:\SteamLibrary\steamapps\common\Sky Children of the Light\Sky.exe",
                @"G:\Steam\steamapps\common\Sky Children of the Light\Sky.exe",
                @"G:\SteamLibrary\steamapps\common\Sky Children of the Light\Sky.exe",
            ],
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
    /// Try to auto-detect game locations by searching default paths and registry.
    /// Returns true if at least one game was found.
    /// </summary>
    public static bool TryAutoDetectLocations()
    {
        var found = false;
        foreach (var game in AllGames)
        {
            var location = TryFindGameLocation(game);
            if (location != null)
            {
                game.SetLocation(location);
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// Try to find a specific game's executable by searching configured path,
    /// default search paths, registry hints, and relative paths.
    /// Returns the path if found, null otherwise.
    /// </summary>
    public static string? TryFindGameLocation(GameDefinition game)
    {
        // 1. Check current configured location
        var currentLocation = game.GetLocation();
        if (File.Exists(currentLocation))
            return currentLocation;

        // 2. Check registry-based Genshin launcher path
        var registryInstallPath = WindowHelper.InstallLocation;
        if (!string.IsNullOrWhiteSpace(registryInstallPath))
        {
            var registryPath = Path.Combine(registryInstallPath, "Genshin Impact Game", game.DefaultExeName);
            if (File.Exists(registryPath))
                return registryPath;
        }

        // 3. Search all known paths
        foreach (var path in GetSearchPaths(game))
        {
            if (File.Exists(path))
                return path;
        }

        // 4. Check relative to application directory
        var relativePath = Path.Combine(AppContext.BaseDirectory, game.DefaultExeName);
        if (File.Exists(relativePath))
            return relativePath;

        return null;
    }

    private static IEnumerable<string> GetSearchPaths(GameDefinition game)
    {
        foreach (var path in game.DefaultSearchPaths)
        {
            if (!string.IsNullOrWhiteSpace(path) && !path.Contains("version-unknown", StringComparison.OrdinalIgnoreCase))
                yield return path;
        }

        if (!string.Equals(game.Id, "Roblox", StringComparison.OrdinalIgnoreCase))
            yield break;

        var localRobloxVersions = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox",
            "Versions");

        foreach (var path in EnumerateRobloxVersionExecutables(localRobloxVersions))
            yield return path;

        foreach (var baseDir in new[]
                 {
                     @"C:\Program Files (x86)\Roblox\Versions",
                     @"C:\Program Files\Roblox\Versions"
                 })
        {
            foreach (var path in EnumerateRobloxVersionExecutables(baseDir))
                yield return path;
        }
    }

    private static IEnumerable<string> EnumerateRobloxVersionExecutables(string versionsDir)
    {
        if (!Directory.Exists(versionsDir))
            yield break;

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.GetDirectories(versionsDir);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in dirs)
            yield return Path.Combine(dir, "RobloxPlayerBeta.exe");
    }

    /// <summary>
    /// Check if a game process is currently running.
    /// Checks both configured location process name and fallback process names.
    /// </summary>
    public static bool IsGameRunning(GameDefinition game)
    {
        var processNames = game.ProcessNames.ToList();

        // Also check configured location process name
        var configuredPath = game.GetLocation();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configuredName = Path.GetFileNameWithoutExtension(configuredPath);
            if (!string.IsNullOrWhiteSpace(configuredName) &&
                !processNames.Contains(configuredName, StringComparer.OrdinalIgnoreCase))
            {
                processNames.Add(configuredName);
            }
        }

        return processNames.Any(processName =>
        {
            try
            {
                return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
            }
            catch
            {
                return false;
            }
        });
    }

    #endregion
}
