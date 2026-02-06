using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Git;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Animation;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.Animation.Transitions;
using AutoMidiPlayer.WPF.Services;
using JetBrains.Annotations;
using Microsoft.Win32;
using PropertyChanged;
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using static AutoMidiPlayer.Data.Entities.Transpose;
using WpfUiApplicationTheme = Wpf.Ui.Appearance.ApplicationTheme;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.ViewModels;

public class SettingsPageViewModel : Screen
{
    // Re-export from MusicConstants for backward compatibility
    public static Dictionary<Transpose, string> TransposeNames => MusicConstants.TransposeNames;
    public static Dictionary<Transpose, string> TransposeTooltips => MusicConstants.TransposeTooltips;

    // Predefined accent colors (Green is first/default)
    public static List<AccentColorOption> AccentColors { get; } = new()
    {
        new("Green", "#1DB954"),
        new("Blue", "#0078D4"),
        new("Purple", "#8B5CF6"),
        new("Red", "#EF4444"),
        new("Orange", "#F97316"),
        new("Pink", "#EC4899"),
        new("Teal", "#14B8A6"),
        new("Yellow", "#EAB308"),
        new("Indigo", "#6366F1"),
        new("Cyan", "#06B6D4")
    };

    // Theme options for dropdown
    public static List<ThemeOption> ThemeOptions { get; } = new()
    {
        new("Light", WpfUiApplicationTheme.Light),
        new("Dark", WpfUiApplicationTheme.Dark),
        new("Use system setting", WpfUiApplicationTheme.Unknown)
    };

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly GlobalHotkeyService _hotkeyService;
    private AccentColorOption _selectedAccentColor = null!;
    private ThemeOption _selectedTheme = null!;

    public SettingsPageViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _main = main;

        // Initialize global hotkey service
        _hotkeyService = ioc.Get<GlobalHotkeyService>();

        // Initialize theme from settings
        _selectedTheme = Settings.AppTheme switch
        {
            0 => ThemeOptions[0], // Light
            1 => ThemeOptions[1], // Dark
            _ => ThemeOptions[2]  // System
        };
        ApplicationThemeManager.Apply(_selectedTheme.Value, WindowBackdropType.Mica, false);

        // Initialize accent color from settings
        _selectedAccentColor = AccentColors.FirstOrDefault(c => c.ColorHex == Settings.AccentColor)
            ?? AccentColors[0]; // Default to Green
        ApplyAccentColor(_selectedAccentColor.ColorHex);

        // Initialize instrument from settings
        SelectedInstrument = Core.Keyboard.InstrumentNames
            .FirstOrDefault(i => (int)i.Key == Settings.SelectedInstrument);

        // Initialize layout from settings
        SelectedLayout = Core.Keyboard.LayoutNames
            .FirstOrDefault(l => (int)l.Key == Settings.SelectedLayout);
    }

    public AccentColorOption SelectedAccentColor
    {
        get => _selectedAccentColor;
        set
        {
            if (SetAndNotify(ref _selectedAccentColor, value) && value is not null)
            {
                Settings.AccentColor = value.ColorHex;
                Settings.Save();
                ApplyAccentColor(value.ColorHex);
            }
        }
    }

    private void ApplyAccentColor(string hexColor)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            ApplyColorToAllSystems(color);
        }
        catch
        {
            // Fallback to Green if color parsing fails
            var fallbackColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954");
            ApplyColorToAllSystems(fallbackColor);
        }
    }

    private void ApplyColorToAllSystems(System.Windows.Media.Color color)
    {
        // Create brushes from color
        var accentBrush = new System.Windows.Media.SolidColorBrush(color);
        accentBrush.Freeze();

        // Also set SystemAccentColorLight1/2/3 and Dark1/2/3 for more controls
        var accentLight1 = AdjustColorBrightness(color, 0.15f);
        var accentLight2 = AdjustColorBrightness(color, 0.30f);
        var accentLight3 = AdjustColorBrightness(color, 0.45f);
        var accentDark1 = AdjustColorBrightness(color, -0.15f);
        var accentDark2 = AdjustColorBrightness(color, -0.30f);
        var accentDark3 = AdjustColorBrightness(color, -0.45f);

        // Set all SystemAccentColor resources
        SetOrUpdateResource("SystemAccentColor", color);
        SetOrUpdateResource("SystemAccentColorLight1", accentLight1);
        SetOrUpdateResource("SystemAccentColorLight2", accentLight2);
        SetOrUpdateResource("SystemAccentColorLight3", accentLight3);
        SetOrUpdateResource("SystemAccentColorDark1", accentDark1);
        SetOrUpdateResource("SystemAccentColorDark2", accentDark2);
        SetOrUpdateResource("SystemAccentColorDark3", accentDark3);

        // Set accent brushes that controls bind to
        var light1Brush = new System.Windows.Media.SolidColorBrush(accentLight1);
        var light2Brush = new System.Windows.Media.SolidColorBrush(accentLight2);
        var light3Brush = new System.Windows.Media.SolidColorBrush(accentLight3);
        var dark1Brush = new System.Windows.Media.SolidColorBrush(accentDark1);
        var dark2Brush = new System.Windows.Media.SolidColorBrush(accentDark2);
        var dark3Brush = new System.Windows.Media.SolidColorBrush(accentDark3);
        light1Brush.Freeze();
        light2Brush.Freeze();
        light3Brush.Freeze();
        dark1Brush.Freeze();
        dark2Brush.Freeze();
        dark3Brush.Freeze();

        SetOrUpdateResource("SystemAccentColorBrush", accentBrush);
        SetOrUpdateResource("SystemAccentColorLight1Brush", light1Brush);
        SetOrUpdateResource("SystemAccentColorLight2Brush", light2Brush);
        SetOrUpdateResource("SystemAccentColorLight3Brush", light3Brush);
        SetOrUpdateResource("SystemAccentColorDark1Brush", dark1Brush);
        SetOrUpdateResource("SystemAccentColorDark2Brush", dark2Brush);
        SetOrUpdateResource("SystemAccentColorDark3Brush", dark3Brush);

        // Set WPF-UI specific accent color resources (Primary, Secondary, Tertiary)
        SetOrUpdateResource("SystemAccentColorPrimary", color);
        SetOrUpdateResource("SystemAccentColorSecondary", accentLight1);
        SetOrUpdateResource("SystemAccentColorTertiary", accentLight2);
        SetOrUpdateResource("SystemAccentColorPrimaryBrush", accentBrush);
        SetOrUpdateResource("SystemAccentColorSecondaryBrush", light1Brush);
        SetOrUpdateResource("SystemAccentColorTertiaryBrush", light2Brush);

        // Apply to WPF-UI theme system with proper order
        var currentTheme = ApplicationThemeManager.GetAppTheme();

        // Apply accent color with updateResources=true so WPF-UI controls update immediately
        ApplicationAccentColorManager.Apply(color, currentTheme, true);

        // Re-apply our custom resources since WPF-UI may have modified them
        SetOrUpdateResource("SystemAccentColorPrimary", color);
        SetOrUpdateResource("SystemAccentColorPrimaryBrush", accentBrush);
        SetOrUpdateResource("SystemAccentColorSecondaryBrush", light1Brush);
        SetOrUpdateResource("SystemAccentColorTertiaryBrush", light2Brush);

        // Then force a delayed refresh to ensure accent sticks after any async theme updates
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new System.Action(() =>
            {
                // Re-apply custom WPF-UI resources after theme system has finished
                SetOrUpdateResource("SystemAccentColorPrimary", color);
                SetOrUpdateResource("SystemAccentColorPrimaryBrush", accentBrush);
                SetOrUpdateResource("SystemAccentColorSecondaryBrush", light1Brush);
                SetOrUpdateResource("SystemAccentColorTertiaryBrush", light2Brush);
            }));

        // Notify other components that accent color changed
        _events.Publish(new AccentColorChangedNotification());
    }

    private static void SetOrUpdateResource(string key, object value)
    {
        if (Application.Current.Resources.Contains(key))
            Application.Current.Resources[key] = value;
        else
            Application.Current.Resources.Add(key, value);
    }

    private static System.Windows.Media.Color AdjustColorBrightness(System.Windows.Media.Color color, float factor)
    {
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;

        if (factor > 0)
        {
            // Lighten
            r = r + (1 - r) * factor;
            g = g + (1 - g) * factor;
            b = b + (1 - b) * factor;
        }
        else
        {
            // Darken
            r = r * (1 + factor);
            g = g * (1 + factor);
            b = b * (1 + factor);
        }

        return System.Windows.Media.Color.FromArgb(
            color.A,
            (byte)Math.Clamp(r * 255, 0, 255),
            (byte)Math.Clamp(g * 255, 0, 255),
            (byte)Math.Clamp(b * 255, 0, 255));
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetAndNotify(ref _selectedTheme, value) && value is not null)
            {
                ApplicationThemeManager.Apply(value.Value, WindowBackdropType.Mica, false);
                Settings.Modify(s => s.AppTheme = value.Value switch
                {
                    WpfUiApplicationTheme.Light => 0,
                    WpfUiApplicationTheme.Dark => 1,
                    _ => -1
                });

                // Reapply accent color after theme change
                ApplyAccentColor(_selectedAccentColor.ColorHex);
            }
        }
    }

    public bool AutoCheckUpdates { get; set; } = Settings.AutoCheckUpdates;

    public bool CanChangeTime => PlayTimerToken is null;

    public bool CanStartStopTimer => DateTime - DateTime.Now > TimeSpan.Zero;

    public bool CanUseSpeakers { get; set; } = true;

    public bool IncludeBetaUpdates { get; set; } = Settings.IncludeBetaUpdates;

    public bool IsCheckingUpdate { get; set; }

    public bool UseDirectInput { get; set; } = Settings.UseDirectInput;

    // Hotkey properties - delegating to GlobalHotkeyService
    public bool HotkeysEnabled
    {
        get => _hotkeyService.IsEnabled;
        set
        {
            _hotkeyService.IsEnabled = value;
            Settings.Modify(s => s.HotkeysEnabled = value);
            NotifyOfPropertyChange();
        }
    }

    public HotkeyBinding PlayPauseHotkey => _hotkeyService.PlayPauseHotkey;
    public HotkeyBinding NextHotkey => _hotkeyService.NextHotkey;
    public HotkeyBinding PreviousHotkey => _hotkeyService.PreviousHotkey;
    public HotkeyBinding SpeedUpHotkey => _hotkeyService.SpeedUpHotkey;
    public HotkeyBinding SpeedDownHotkey => _hotkeyService.SpeedDownHotkey;
    public HotkeyBinding PanicHotkey => _hotkeyService.PanicHotkey;

    public void UpdateHotkey(string name, Key key, ModifierKeys modifiers)
    {
        _hotkeyService.UpdateHotkey(name, key, modifiers);
        NotifyHotkeyChanged(name);
    }

    public void ClearHotkey(string name)
    {
        _hotkeyService.ClearHotkey(name);
        NotifyHotkeyChanged(name);
    }

    public void SuspendHotkeys()
    {
        _hotkeyService.SuspendHotkeys();
    }

    public void ResumeHotkeys()
    {
        _hotkeyService.ResumeHotkeys();
    }

    public void ResetHotkeys()
    {
        _hotkeyService.ResetToDefaults();
        NotifyOfPropertyChange(nameof(PlayPauseHotkey));
        NotifyOfPropertyChange(nameof(NextHotkey));
        NotifyOfPropertyChange(nameof(PreviousHotkey));
        NotifyOfPropertyChange(nameof(SpeedUpHotkey));
        NotifyOfPropertyChange(nameof(SpeedDownHotkey));
        NotifyOfPropertyChange(nameof(PanicHotkey));
    }

    private void NotifyHotkeyChanged(string name)
    {
        switch (name)
        {
            case "PlayPause": NotifyOfPropertyChange(nameof(PlayPauseHotkey)); break;
            case "Next": NotifyOfPropertyChange(nameof(NextHotkey)); break;
            case "Previous": NotifyOfPropertyChange(nameof(PreviousHotkey)); break;
            case "SpeedUp": NotifyOfPropertyChange(nameof(SpeedUpHotkey)); break;
            case "SpeedDown": NotifyOfPropertyChange(nameof(SpeedDownHotkey)); break;
            case "Panic": NotifyOfPropertyChange(nameof(PanicHotkey)); break;
        }
    }

    public string MidiFolder { get; set; } = Settings.MidiFolder;

    public bool HasMidiFolder => !string.IsNullOrEmpty(MidiFolder);

    public bool NeedsUpdate => ProgramVersion < LatestVersion.Version;

    [UsedImplicitly] public CancellationTokenSource? PlayTimerToken { get; private set; }

    public static CaptionedObject<Transition>? Transition { get; set; } =
        TransitionCollection.Transitions[Settings.SelectedTransition];

    public DateTime DateTime { get; set; } = DateTime.Now;

    public GitVersion LatestVersion { get; set; } = new();

    public KeyValuePair<Core.Keyboard.Instrument, string> SelectedInstrument { get; set; }

    public KeyValuePair<Core.Keyboard.Layout, string> SelectedLayout { get; set; }

    public static string GenshinLocation
    {
        get => Settings.GenshinLocation;
        set => Settings.GenshinLocation = value;
    }

    /// <summary>
    /// Path where application data (database, logs, etc.) is stored
    /// </summary>
    public static string DataLocation => AppPaths.AppDataDirectory;

    public string TimerText => CanChangeTime ? "Start" : "Stop";

    [UsedImplicitly] public string UpdateString { get; set; } = string.Empty;

    public static Version ProgramVersion => Assembly.GetExecutingAssembly().GetName().Version!;

    private QueueViewModel Queue => _main.QueueView;

    public async Task<bool> TryGetLocationAsync()
    {
        var locations = new[]
        {
            // User set location
            Settings.GenshinLocation,

            // Default Genshin Impact install locations
            @"C:\Program Files\Genshin Impact\Genshin Impact Game\GenshinImpact.exe",
            @"C:\Program Files\Genshin Impact\Genshin Impact Game\YuanShen.exe",

            // Custom Genshin Impact install location
            Path.Combine(WindowHelper.InstallLocation ?? string.Empty, @"Genshin Impact Game\GenshinImpact.exe"),
            Path.Combine(WindowHelper.InstallLocation ?? string.Empty, @"Genshin Impact Game\YuanShen.exe"),

            // Relative location (Genshin)
            AppContext.BaseDirectory + "GenshinImpact.exe",
            AppContext.BaseDirectory + "YuanShen.exe",

            // Common Steam Heartopia locations
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

            // Relative location (Heartopia)
            AppContext.BaseDirectory + "xdt.exe"
        };

        foreach (var location in locations)
        {
            if (await TrySetLocationAsync(location))
                return true;
        }

        return false;
    }

    public async Task CheckForUpdate()
    {
        if (IsCheckingUpdate)
            return;

        UpdateString = "Checking for updates...";
        IsCheckingUpdate = true;

        try
        {
            LatestVersion = await GetLatestVersion() ?? new GitVersion();
            UpdateString = LatestVersion.Version > ProgramVersion
                ? "(Update available!)"
                : string.Empty;
        }
        catch (Exception)
        {
            UpdateString = "Failed to check updates";
        }
        finally
        {
            IsCheckingUpdate = false;
            NotifyOfPropertyChange(() => NeedsUpdate);
        }
    }

    public async Task LocationMissing()
    {
        var dialog = DialogHelper.CreateDialog();
        dialog.Title = "Error";
        dialog.Content = "Could not find Game's Location, please find GenshinImpact.exe, YuanShen.exe, or xdt.exe (Heartopia)";
        dialog.PrimaryButtonText = "Find Manually...";
        dialog.SecondaryButtonText = "Ignore (Notes might not play)";
        dialog.CloseButtonText = "Exit";

        var result = await dialog.ShowAsync();

        switch (result)
        {
            case ContentDialogResult.None:
                RequestClose();
                break;
            case ContentDialogResult.Primary:
                await SetLocation();
                break;
            case ContentDialogResult.Secondary:
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(result), result, $"Invalid {nameof(ContentDialogResult)}");
        }
    }

    [PublicAPI]
    public async Task SetLocation()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Executable|*.exe|All files (*.*)|*.*",
            InitialDirectory = WindowHelper.InstallLocation is null
                ? @"C:\Program Files\Genshin Impact\Genshin Impact Game\"
                : Path.Combine(WindowHelper.InstallLocation, "Genshin Impact Game")
        };

        var success = openFileDialog.ShowDialog() == true;
        var set = await TrySetLocationAsync(openFileDialog.FileName);

        if (!(success && set)) await LocationMissing();
    }

    public async Task BrowseMidiFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select MIDI folder to auto-scan",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            MidiFolder = dialog.FolderName;
            Settings.MidiFolder = MidiFolder;
            Settings.Save();

            // Auto-scan the folder
            await ScanMidiFolder();
        }
    }

    public async Task ScanMidiFolder()
    {
        if (string.IsNullOrEmpty(MidiFolder) || !Directory.Exists(MidiFolder))
            return;

        await _main.SongsView.ScanFolder(MidiFolder);
    }

    public void ClearMidiFolder()
    {
        MidiFolder = string.Empty;
        Settings.MidiFolder = string.Empty;
        Settings.Save();
    }

    public void OpenDataFolder()
    {
        AppPaths.EnsureDirectoryExists();
        System.Diagnostics.Process.Start("explorer.exe", AppPaths.AppDataDirectory);
    }

    [UsedImplicitly]
    public async Task StartStopTimer()
    {
        if (PlayTimerToken is not null)
        {
            PlayTimerToken.Cancel();
            return;
        }

        PlayTimerToken = new();

        var start = DateTime - DateTime.Now;
        await Task.Delay(start, PlayTimerToken.Token)
            .ContinueWith(_ => { });

        if (!PlayTimerToken.IsCancellationRequested)
            _events.Publish(new PlayTimerNotification());

        PlayTimerToken = null;
    }

    [UsedImplicitly]
    [SuppressPropertyChangedWarnings]
    public void OnThemeChanged()
    {
        var currentTheme = ApplicationThemeManager.GetAppTheme();

        var matchingTheme = ThemeOptions.FirstOrDefault(option => option.Value == currentTheme) ?? ThemeOptions[2];
        if (_selectedTheme != matchingTheme)
        {
            _selectedTheme = matchingTheme;
            NotifyOfPropertyChange(() => SelectedTheme);
        }

        Settings.Modify(s => s.AppTheme = currentTheme switch
        {
            WpfUiApplicationTheme.Light => 0,
            WpfUiApplicationTheme.Dark => 1,
            _ => -1
        });

        // Reapply accent color after theme change
        ApplyAccentColor(_selectedAccentColor.ColorHex);
    }

    [UsedImplicitly]
    public void SetTimeToNow() => DateTime = DateTime.Now;

    protected override void OnActivate()
    {
        if (AutoCheckUpdates)
            _ = CheckForUpdate();
    }

    private async Task<bool> TrySetLocationAsync(string? location)
    {
        if (!File.Exists(location)) return false;
        if (Path.GetFileName(location).Equals("launcher.exe", StringComparison.OrdinalIgnoreCase))
        {
            var dialog = DialogHelper.CreateDialog();
            dialog.Title = "Incorrect Location";
            dialog.Content = "launcher.exe is not the game, please find GenshinImpact.exe, YuanShen.exe, or xdt.exe (Heartopia)";
            dialog.CloseButtonText = "Ok";

            await dialog.ShowAsync();
            return false;
        }

        Settings.GenshinLocation = location;
        NotifyOfPropertyChange(() => Settings.GenshinLocation);

        return true;
    }

    private async Task<GitVersion?> GetLatestVersion()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/Jed556/AutoMidiPlayer/releases");

        var productInfo = new ProductInfoHeaderValue("AutoMidiPlayer", ProgramVersion.ToString());
        request.Headers.UserAgent.Add(productInfo);

        var response = await client.SendAsync(request);
        var versions = await response.Content.ReadFromJsonAsync<List<GitVersion>>();

        return versions?
            .OrderByDescending(v => v.Version)
            .FirstOrDefault(v => (!v.Draft && !v.Prerelease) || IncludeBetaUpdates);
    }

    [UsedImplicitly]
    private void OnAutoCheckUpdatesChanged()
    {
        if (AutoCheckUpdates)
            _ = CheckForUpdate();

        Settings.Modify(s => s.AutoCheckUpdates = AutoCheckUpdates);
    }

    [UsedImplicitly]
    private void OnIncludeBetaUpdatesChanged() => _ = CheckForUpdate();

    [UsedImplicitly]
    private void OnUseDirectInputChanged()
    {
        Settings.UseDirectInput = UseDirectInput;
        Settings.Save();
        KeyboardPlayer.UseDirectInput = UseDirectInput;
    }
}

public class AccentColorOption
{
    public string Name { get; }
    public string ColorHex { get; }
    public System.Windows.Media.SolidColorBrush ColorBrush { get; }

    public AccentColorOption(string name, string colorHex)
    {
        Name = name;
        ColorHex = colorHex;
        ColorBrush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
    }

    public override string ToString() => Name;
}

public class ThemeOption
{
    public string Name { get; }
    public WpfUiApplicationTheme Value { get; }

    public ThemeOption(string name, WpfUiApplicationTheme value)
    {
        Name = name;
        Value = value;
    }

    public override string ToString() => Name;
}
