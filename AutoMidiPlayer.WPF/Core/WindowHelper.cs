using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AutoMidiPlayer.Data.Properties;
using Microsoft.Win32;

namespace AutoMidiPlayer.WPF.Core;

public static class WindowHelper
{
    public static string? InstallLocation => Registry.LocalMachine
        .OpenSubKey(@"SOFTWARE\launcher", false)
        ?.GetValue("InstPath") as string;

    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    private static string ActiveGameProcessName
    {
        get
        {
            var instrument = Keyboard.GetInstrumentAtIndex(Settings.Default.SelectedInstrument).Key;
            var location = instrument.Contains("Heartopia", StringComparison.OrdinalIgnoreCase)
                ? Settings.Default.HeartopiaLocation
                : instrument.Contains("Roblox", StringComparison.OrdinalIgnoreCase)
                    ? Settings.Default.RobloxLocation
                    : Settings.Default.GenshinLocation;

            return Path.GetFileNameWithoutExtension(location);
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
        var process = Process.GetProcessesByName(processName);
        return process.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)?.MainWindowHandle;
    }

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);
}
