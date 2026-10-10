using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AutoMidiPlayer.WPF.Core.Games;
using Microsoft.Win32;

namespace AutoMidiPlayer.WPF.Core;

public static class WindowHelper
{
    public static string? InstallLocation => Registry.LocalMachine
        .OpenSubKey(@"SOFTWARE\launcher", false)
        ?.GetValue("InstPath") as string;

    private static string[]? _cachedActiveGameProcessNames;
    private static long _lastProcessNamesRefreshTimestampMs;
    private const long ProcessNamesCacheTtlMs = 3000;

    private static IntPtr _lastForegroundWindow = IntPtr.Zero;
    private static uint _lastForegroundProcessId = 0;
    private static bool _lastFocusVerdict = false;
    private static long _lastFocusCheckTimestampMs = 0;
    private const long FocusCheckTtlMs = 120;

    public static void InvalidateProcessNamesCache()
    {
        _cachedActiveGameProcessNames = null;
        _lastForegroundWindow = IntPtr.Zero;
        _lastForegroundProcessId = 0;
        _lastFocusVerdict = false;
        _lastFocusCheckTimestampMs = 0;
    }

    private static string[] ActiveGameProcessNames
    {
        get
        {
            var now = Stopwatch.GetTimestamp();
            var nowMs = (long)(now * 1000.0 / Stopwatch.Frequency);

            if (_cachedActiveGameProcessNames != null && (nowMs - _lastProcessNamesRefreshTimestampMs) < ProcessNamesCacheTtlMs)
            {
                return _cachedActiveGameProcessNames;
            }

            var activeGame = GameRegistry.AllGames.FirstOrDefault(game => game.GetIsActive());

            if (activeGame is null)
            {
                _cachedActiveGameProcessNames = [];
                _lastProcessNamesRefreshTimestampMs = nowMs;
                return _cachedActiveGameProcessNames;
            }

            var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var configuredProcessName = Path.GetFileNameWithoutExtension(activeGame.GetLocation());
            if (!string.IsNullOrWhiteSpace(configuredProcessName))
                processNames.Add(configuredProcessName);

            foreach (var processName in activeGame.ProcessNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                processNames.Add(processName);

            var result = processNames.ToArray();
            _cachedActiveGameProcessNames = result;
            _lastProcessNamesRefreshTimestampMs = nowMs;
            return result;
        }
    }

    public static bool IsGameFocused()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        var now = Stopwatch.GetTimestamp();
        var nowMs = (long)(now * 1000.0 / Stopwatch.Frequency);

        // Fast path: If foreground window is the exact same handle and recently checked, return cached result immediately
        if (foregroundWindow == _lastForegroundWindow && (nowMs - _lastFocusCheckTimestampMs) < FocusCheckTtlMs)
        {
            return _lastFocusVerdict;
        }

        var processNames = ActiveGameProcessNames;
        if (processNames.Length == 0)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0)
            return false;

        bool isFocused;
        // If window handle and process ID haven't changed, retain the verified verdict without re-inspecting process
        if (foregroundWindow == _lastForegroundWindow && processId == _lastForegroundProcessId)
        {
            isFocused = _lastFocusVerdict;
        }
        else
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                isFocused = processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                isFocused = false;
            }
        }

        _lastForegroundWindow = foregroundWindow;
        _lastForegroundProcessId = processId;
        _lastFocusVerdict = isFocused;
        _lastFocusCheckTimestampMs = nowMs;

        return isFocused;
    }

    public static void EnsureGameOnTop()
    {
        var gameWindow = FindWindowByProcessNames(ActiveGameProcessNames);
        if (gameWindow is null) return;

        SwitchToThisWindow((IntPtr)gameWindow, true);
    }

    /// <summary>
    /// Returns the main window handle of the currently active game process,
    /// or null if the game is not running. Used by the Window Message input path.
    /// </summary>
    public static IntPtr? GetActiveGameWindowHandle()
        => FindWindowByProcessNames(ActiveGameProcessNames);

    [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static IntPtr? FindWindowByProcessNames(IEnumerable<string> processNames)
    {
        var names = processNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length == 0)
            return null;

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero)
        {
            GetWindowThreadProcessId(foregroundWindow, out var processId);
            if (processId != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    if (names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                        return foregroundWindow;
                }
                catch
                {
                    // Ignore and continue with process enumeration fallback.
                }
            }
        }

        foreach (var processName in names)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                try
                {
                    var handle = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)?.MainWindowHandle;
                    if (handle.HasValue && handle.Value != IntPtr.Zero)
                        return handle;
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
                // Ignore process access errors and continue.
            }
        }

        return null;
    }

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);
}
