using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Services;

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
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Genshin_Impact.png",
            processNames: ["GenshinImpact", "YuanShen"],
            getLocation: () => Settings.GenshinLocation,
            setLocation: v => Settings.Modify(s => s.GenshinLocation = v),
            getIsActive: () => Settings.ActiveGenshin,
            setIsActive: v => Settings.Modify(s => s.ActiveGenshin = v)
        ),
        new GameDefinition(
            id: "Wuthering Waves",
            displayName: "Wuthering Waves",
            instrumentGameName: "Wuthering Waves",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Wuthering_Waves.png",
            processNames: ["Client-Win64-Shipping", "Wuthering Waves"],
            // windowNames: ["Wuthering Waves"],
            getLocation: () => Settings.WutheringWavesLocation,
            setLocation: v => Settings.Modify(s => s.WutheringWavesLocation = v),
            getIsActive: () => Settings.ActiveWutheringWaves,
            setIsActive: v => Settings.Modify(s => s.ActiveWutheringWaves = v)
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
            id: "WWM",
            displayName: "Where Winds Meet",
            instrumentGameName: "Where Winds Meet",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/WWM.png",
            processNames: ["WhereWindsMeet", "Where Winds Meet", "yysls", "yysls_client", "wwm"],
            getLocation: () => Settings.WWMLocation,
            setLocation: v => Settings.Modify(s => s.WWMLocation = v),
            getIsActive: () => Settings.ActiveWWM,
            setIsActive: v => Settings.Modify(s => s.ActiveWWM = v)
        ),
        new GameDefinition(
            id: "BPSR",
            displayName: "Blue Protocol: Star Resonance",
            instrumentGameName: "BPSR",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/BPSR.png",
            processNames: ["BPSR"],
            getLocation: () => Settings.BPSRLocation,
            setLocation: v => Settings.Modify(s => s.BPSRLocation = v),
            getIsActive: () => Settings.ActiveBPSR,
            setIsActive: v => Settings.Modify(s => s.ActiveBPSR = v)
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
        ),
        new GameDefinition(
            id: "Roblox",
            displayName: "Roblox",
            instrumentGameName: "Roblox",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Roblox.png",
            processNames: ["Roblox Game Client"],
            windowNames: ["Roblox"],
            getLocation: () => Settings.RobloxLocation,
            setLocation: v => Settings.Modify(s => s.RobloxLocation = v),
            getIsActive: () => Settings.ActiveRoblox,
            setIsActive: v => Settings.Modify(s => s.ActiveRoblox = v)
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
            id: "Core Keeper",
            displayName: "Core Keeper",
            instrumentGameName: "Core Keeper",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/Core_Keeper.png",
            processNames: ["CoreKeeper"],
            getLocation: () => Settings.CoreKeeperLocation,
            setLocation: v => Settings.Modify(s => s.CoreKeeperLocation = v),
            getIsActive: () => Settings.ActiveCoreKeeper,
            setIsActive: v => Settings.Modify(s => s.ActiveCoreKeeper = v)
        ),
        new GameDefinition(
            id: "HPMA",
            displayName: "Harry Potter: Magic Awakened",
            instrumentGameName: "HPMA",
            imageResourcePath: "pack://application:,,,/Resources/Images/Games/HPMA.png",
            processNames: ["launcher"],
            windowNames: ["Harry Potter: Magic Awakened"],
            getLocation: () => Settings.HPMALocation,
            setLocation: v => Settings.Modify(s => s.HPMALocation = v),
            getIsActive: () => Settings.ActiveHPMA,
            setIsActive: v => Settings.Modify(s => s.ActiveHPMA = v)
        ),
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

    #endregion

    #region Process Snapshot & Running Check

    private static readonly object _snapshotLock = new();
    private static HashSet<string>? _cachedRunningProcesses;
    private static long _lastSnapshotTimestampMs;
    private const long SnapshotCacheTtlMs = 1500;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        private const int MaxPath = 260;
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint SnapshotProcess = 0x00000002;

    private static HashSet<string> GetRunningProcessesSnapshot()
    {
        var now = Stopwatch.GetTimestamp();
        var nowMs = (long)(now * 1000.0 / Stopwatch.Frequency);
        var ttl = (GarbageManService.IsPlaybackActive?.Invoke() == true) ? 5000 : 3000;

        lock (_snapshotLock)
        {
            if (_cachedRunningProcesses is not null && (nowMs - _lastSnapshotTimestampMs) < ttl)
            {
                return _cachedRunningProcesses;
            }

            var snapshot = QueryRunningProcesses();
            _cachedRunningProcesses = snapshot;
            _lastSnapshotTimestampMs = nowMs;
            return snapshot;
        }
    }

    private static HashSet<string> QueryRunningProcesses()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var hSnapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
            if (hSnapshot != IntPtr.Zero && hSnapshot != InvalidHandleValue)
            {
                try
                {
                    var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                    if (Process32First(hSnapshot, ref entry))
                    {
                        do
                        {
                            var name = entry.szExeFile;
                            if (!string.IsNullOrEmpty(name))
                            {
                                result.Add(name);
                                var withoutExt = Path.GetFileNameWithoutExtension(name);
                                if (!string.IsNullOrEmpty(withoutExt))
                                    result.Add(withoutExt);
                            }
                        } while (Process32Next(hSnapshot, ref entry));
                    }
                }
                finally
                {
                    CloseHandle(hSnapshot);
                }

                return result;
            }
        }
        catch
        {
            // Ignore native error, fallback below
        }

        // Fallback using Process.GetProcesses() if Toolhelp fails
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var p in processes)
                {
                    try
                    {
                        var name = p.ProcessName;
                        if (!string.IsNullOrEmpty(name))
                        {
                            result.Add(name);
                        }
                    }
                    catch
                    {
                        // Ignore individual process access errors
                    }
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // Ignore - returns whatever was collected or empty set
        }

        return result;
    }

    /// <summary>
    /// Check if a game process is currently running.
    /// Checks both configured location process name and fallback process names.
    /// Results are cached briefly via a unified system process snapshot to avoid expensive per-note process enumeration.
    /// </summary>
    public static bool IsGameRunning(GameDefinition game)
    {
        try
        {
            var runningProcesses = GetRunningProcessesSnapshot();
            return IsGameRunningCore(game, runningProcesses);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGameRunningCore(GameDefinition game, HashSet<string> runningProcesses)
    {
        var matched = false;
        foreach (var name in game.ProcessNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && runningProcesses.Contains(name))
            {
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            var configuredPath = game.GetLocation();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var configuredName = Path.GetFileNameWithoutExtension(configuredPath);
                if (!string.IsNullOrWhiteSpace(configuredName) && runningProcesses.Contains(configuredName))
                    matched = true;
            }
        }

        if (!matched)
            return false;

        // If the game definition does not require window title matching, we are done
        if (game.WindowNames.Count == 0)
            return true;

        // For games with specific WindowNames (e.g. Roblox, HPMA launcher), verify window title
        var allNames = new List<string>(game.ProcessNames);
        var confPath = game.GetLocation();
        if (!string.IsNullOrWhiteSpace(confPath))
        {
            var confName = Path.GetFileNameWithoutExtension(confPath);
            if (!string.IsNullOrWhiteSpace(confName))
                allNames.Add(confName);
        }

        return CheckGameWindowMatches(game, allNames);
    }

    private static bool CheckGameWindowMatches(GameDefinition game, IEnumerable<string> processNames)
    {
        try
        {
            foreach (var processName in processNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                try
                {
                    foreach (var process in processes)
                    {
                        try
                        {
                            var title = process.MainWindowTitle;
                            if (game.WindowNames.Any(w => string.Equals(w, title, StringComparison.OrdinalIgnoreCase)))
                                return true;
                        }
                        catch
                        {
                            // Ignore access errors on individual process properties
                        }
                    }
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    #endregion
}
