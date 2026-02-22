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

    private static string[] ActiveGameProcessNames
    {
        get
        {
            var activeGame = GameRegistry.AllGames.FirstOrDefault(game => game.GetIsActive());

            if (activeGame is null)
                return [];

            var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var configuredProcessName = Path.GetFileNameWithoutExtension(activeGame.GetLocation());
            if (!string.IsNullOrWhiteSpace(configuredProcessName))
                processNames.Add(configuredProcessName);

            foreach (var processName in activeGame.ProcessNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                processNames.Add(processName);

            return [.. processNames];
        }
    }

    public static bool IsGameFocused()
    {
        var processNames = ActiveGameProcessNames;
        if (processNames.Length == 0)
            return false;

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0)
            return false;

        try
        {
            var process = Process.GetProcessById((int)processId);
            return processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
                    var process = Process.GetProcessById((int)processId);
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
                var process = Process.GetProcessesByName(processName);
                var handle = process.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)?.MainWindowHandle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                    return handle;
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
