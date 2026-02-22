using System;
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

    private static string ActiveGameProcessName
    {
        get
        {
            var activeGame = GameRegistry.AllGames.FirstOrDefault(game => game.GetIsActive());

            if (activeGame is null)
                return string.Empty;

            var configuredProcessName = Path.GetFileNameWithoutExtension(activeGame.GetLocation());
            if (!string.IsNullOrWhiteSpace(configuredProcessName))
                return configuredProcessName;

            return activeGame.ProcessNames.FirstOrDefault() ?? string.Empty;
        }
    }

    public static bool IsGameFocused()
    {
        var gameWindow = FindWindowByProcessName(ActiveGameProcessName);
        return gameWindow != null &&
            IsWindowFocused((IntPtr)gameWindow);
    }

    public static void EnsureGameOnTop()
    {
        var gameWindow = FindWindowByProcessName(ActiveGameProcessName);
        if (gameWindow is null) return;

        SwitchToThisWindow((IntPtr)gameWindow, true);
    }

    private static bool IsWindowFocused(IntPtr windowPtr)
    {
        var hWnd = GetForegroundWindow();
        return hWnd.Equals(windowPtr);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    private static IntPtr? FindWindowByProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        var process = Process.GetProcessesByName(processName);
        return process.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)?.MainWindowHandle;
    }

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);
}
