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
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Git;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.ModernWPF;
using AutoMidiPlayer.WPF.ModernWPF.Animation;
using AutoMidiPlayer.WPF.ModernWPF.Animation.Transitions;
using JetBrains.Annotations;
using Microsoft.Win32;
using ModernWpf;
using ModernWpf.Controls;
using PropertyChanged;
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using Wpf.Ui.Mvvm.Contracts;
using static AutoMidiPlayer.Data.Entities.Transpose;

namespace AutoMidiPlayer.WPF.ViewModels;

public class SettingsPageViewModel : Screen
{
    // Re-export from MusicConstants for backward compatibility
    public static Dictionary<Transpose, string> TransposeNames => MusicConstants.TransposeNames;
    public static Dictionary<Transpose, string> TransposeTooltips => MusicConstants.TransposeTooltips;

    // Predefined accent colors (Spotify green is first/default)
    public static List<AccentColorOption> AccentColors { get; } = new()
    {
        new("Spotify Green", "#1DB954"),
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
        new("Light", ApplicationTheme.Light),
        new("Dark", ApplicationTheme.Dark),
        new("Use system setting", null)
    };

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly IThemeService _theme;
    private readonly MainWindowViewModel _main;
    private int _keyOffset;
    private double _speed = 1.0;
    private AccentColorOption _selectedAccentColor = null!;
    private ThemeOption _selectedTheme = null!;

    public SettingsPageViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _theme = ioc.Get<IThemeService>();
        _main = main;

        _keyOffset = Queue.OpenedFile?.Song.Key ?? 0;

        // Initialize theme from settings
        _selectedTheme = Settings.AppTheme switch
        {
            0 => ThemeOptions[0], // Light
            1 => ThemeOptions[1], // Dark
            _ => ThemeOptions[2]  // System
        };
        ThemeManager.Current.ApplicationTheme = _selectedTheme.Value;

        // Initialize accent color from settings
        _selectedAccentColor = AccentColors.FirstOrDefault(c => c.ColorHex == Settings.AccentColor)
            ?? AccentColors[0]; // Default to Spotify Green
        ApplyAccentColor(_selectedAccentColor.ColorHex);

        // Initialize instrument from settings
        SelectedInstrument = Keyboard.InstrumentNames
            .FirstOrDefault(i => (int)i.Key == Settings.SelectedInstrument);

        // Initialize layout from settings
        SelectedLayout = Keyboard.LayoutNames
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
            ThemeManager.Current.AccentColor = color;

            // Also apply to Wpf.Ui accent for sidebar and other controls
            Accent.Apply(color);
        }
        catch
        {
            // Fallback to Spotify green if color parsing fails
            var fallbackColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954");
            ThemeManager.Current.AccentColor = fallbackColor;
            Accent.Apply(fallbackColor);
        }
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetAndNotify(ref _selectedTheme, value) && value is not null)
            {
                ThemeManager.Current.ApplicationTheme = value.Value;
                _theme.SetTheme(value.Value switch
                {
                    ApplicationTheme.Light => ThemeType.Light,
                    ApplicationTheme.Dark => ThemeType.Dark,
                    _ => _theme.GetSystemTheme()
                });
                Settings.Modify(s => s.AppTheme = (int?)value.Value ?? -1);

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

    public string MidiFolder { get; set; } = Settings.MidiFolder;

    public bool HasMidiFolder => !string.IsNullOrEmpty(MidiFolder);

    public bool NeedsUpdate => ProgramVersion < LatestVersion.Version;

    [UsedImplicitly] public CancellationTokenSource? PlayTimerToken { get; private set; }

    public static CaptionedObject<Transition>? Transition { get; set; } =
        TransitionCollection.Transitions[Settings.SelectedTransition];

    public DateTime DateTime { get; set; } = DateTime.Now;

    [UsedImplicitly]
    public Dictionary<int, string> KeyOffsets => MusicConstants.KeyOffsets;

    public GitVersion LatestVersion { get; set; } = new();

    public int KeyOffset
    {
        get => _keyOffset;
        set
        {
            if (SetAndNotify(ref _keyOffset, Math.Clamp(value, MinOffset, MaxOffset)))
            {
                // Update the selected key option to match
                _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == _keyOffset);
                NotifyOfPropertyChange(nameof(SelectedKeyOption));
            }
        }
    }

    public int MaxOffset => KeyOffsets.Keys.Max();

    public int MinOffset => KeyOffsets.Keys.Min();

    public KeyValuePair<Keyboard.Instrument, string> SelectedInstrument { get; set; }

    public KeyValuePair<Keyboard.Layout, string> SelectedLayout { get; set; }

    public KeyValuePair<Transpose, string>? Transpose { get; set; }

    public static List<MidiSpeed> MidiSpeeds { get; } = new()
    {
        new("0.25x", 0.25),
        new("0.5x", 0.5),
        new("0.75x", 0.75),
        new("Normal", 1),
        new("1.25x", 1.25),
        new("1.5x", 1.5),
        new("1.75x", 1.75),
        new("2x", 2)
    };

    public MidiSpeed SelectedSpeed { get; set; } = MidiSpeeds[Settings.SelectedSpeed];

    public double Speed
    {
        get => _speed;
        set
        {
            if (SetAndNotify(ref _speed, Math.Round(Math.Clamp(value, 0.1, 4.0), 1)))
            {
                // Update the selected speed option to match
                _selectedSpeedOption = SpeedOptions.FirstOrDefault(s => Math.Abs(s.Value - _speed) < 0.01)
                    ?? SpeedOptions.First(s => s.Value == 1.0);
                NotifyOfPropertyChange(nameof(SelectedSpeedOption));
            }
        }
    }

    public string SpeedDisplay => $"Speed: {Speed:0.0}x";

    public static string GenshinLocation
    {
        get => Settings.GenshinLocation;
        set => Settings.GenshinLocation = value;
    }

    /// <summary>
    /// Path where application data (database, logs, etc.) is stored
    /// </summary>
    public static string DataLocation => AppPaths.AppDataDirectory;

    public string Key => $"{KeyOffsets[KeyOffset]}";

    // KeyOptions for ComboBox binding - generated from MusicConstants
    public List<MusicConstants.KeyOption> KeyOptions { get; } = MusicConstants.GenerateKeyOptions();

    private MusicConstants.KeyOption? _selectedKeyOption;
    public MusicConstants.KeyOption? SelectedKeyOption
    {
        get => _selectedKeyOption ??= KeyOptions.FirstOrDefault(k => k.Value == KeyOffset);
        set
        {
            if (value != null && SetAndNotify(ref _selectedKeyOption, value))
            {
                KeyOffset = value.Value;
            }
        }
    }

    // SpeedOptions for ComboBox binding - generated from MusicConstants
    public List<MusicConstants.SpeedOption> SpeedOptions { get; } = MusicConstants.GenerateSpeedOptions();

    private MusicConstants.SpeedOption? _selectedSpeedOption;
    public MusicConstants.SpeedOption? SelectedSpeedOption
    {
        get => _selectedSpeedOption ??= SpeedOptions.FirstOrDefault(s => Math.Abs(s.Value - Speed) < 0.01) ?? SpeedOptions.First(s => s.Value == 1.0);
        set
        {
            if (value != null && SetAndNotify(ref _selectedSpeedOption, value))
            {
                Speed = value.Value;
            }
        }
    }

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

    // Key offset controls
    public void IncreaseKey() => KeyOffset++;
    public void DecreaseKey() => KeyOffset--;

    // Speed controls
    public void IncreaseSpeed() => Speed = Math.Round(Speed + 0.1, 1);
    public void DecreaseSpeed() => Speed = Math.Round(Speed - 0.1, 1);

    public async Task LocationMissing()
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = "Could not find Game's Location, please find GenshinImpact.exe, YuanShen.exe, or xdt.exe (Heartopia)",

            PrimaryButtonText = "Find Manually...",
            SecondaryButtonText = "Ignore (Notes might not play)",
            CloseButtonText = "Exit"
        };

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
        _theme.SetTheme(ThemeManager.Current.ApplicationTheme switch
        {
            ApplicationTheme.Light => ThemeType.Light,
            ApplicationTheme.Dark => ThemeType.Dark,
            _ => _theme.GetSystemTheme()
        });

        Settings.Modify(s => s.AppTheme = (int?)ThemeManager.Current.ApplicationTheme ?? -1);

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
            var dialog = new ContentDialog
            {
                Title = "Incorrect Location",
                Content = "launcher.exe is not the game, please find GenshinImpact.exe, YuanShen.exe, or xdt.exe (Heartopia)",

                CloseButtonText = "Ok"
            };

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
    private async void OnKeyOffsetChanged()
    {
        if (Queue.OpenedFile is null)
            return;

        await using var db = _ioc.Get<LyreContext>();

        Queue.OpenedFile.Song.Key = KeyOffset;
        db.Update(Queue.OpenedFile.Song);

        await db.SaveChangesAsync();

        // Notify UI to refresh
        _main.SongsView.RefreshCurrentSong();
        _main.QueueView.RefreshCurrentSong();
    }

    [UsedImplicitly]
    private async void OnSpeedChanged()
    {
        _events.Publish(this);

        if (Queue.OpenedFile is null)
            return;

        await using var db = _ioc.Get<LyreContext>();

        Queue.OpenedFile.Song.Speed = Speed;
        db.Update(Queue.OpenedFile.Song);

        await db.SaveChangesAsync();

        // Notify UI to refresh
        _main.SongsView.RefreshCurrentSong();
        _main.QueueView.RefreshCurrentSong();
    }

    [UsedImplicitly]
    private void OnUseDirectInputChanged()
    {
        Settings.UseDirectInput = UseDirectInput;
        Settings.Save();
        LyrePlayer.UseDirectInput = UseDirectInput;
    }

    [UsedImplicitly]
    private void OnSelectedSpeedChanged() => _events.Publish(this);

    [UsedImplicitly]
    private async void OnTransposeChanged()
    {
        if (Queue.OpenedFile is null)
            return;

        await using var db = _ioc.Get<LyreContext>();

        Queue.OpenedFile.Song.Transpose = Transpose?.Key;
        db.Update(Queue.OpenedFile.Song);

        await db.SaveChangesAsync();

        // Notify UI to refresh
        _main.SongsView.RefreshCurrentSong();
        _main.QueueView.RefreshCurrentSong();

        // Notify TrackViewModel to update statistics
        _events.Publish(this);
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
    public ApplicationTheme? Value { get; }

    public ThemeOption(string name, ApplicationTheme? value)
    {
        Name = name;
        Value = value;
    }

    public override string ToString() => Name;
}
