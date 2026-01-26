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
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using Wpf.Ui.Mvvm.Contracts;
using static AutoMidiPlayer.Data.Entities.Transpose;

namespace AutoMidiPlayer.WPF.ViewModels;

public class SettingsPageViewModel : Screen
{
    public static readonly Dictionary<Transpose, string> TransposeNames = new()
    {
        [Up] = "Up (1 Semitone up)",
        [Ignore] = "Ignore (Skip notes)",
        [Down] = "Down (1 Semitone down)"
    };

    public static readonly Dictionary<Transpose, string> TransposeTooltips = new()
    {
        [Up] = "Transpose out-of-range notes 1 semitone up",
        [Ignore] = "Skip out-of-range notes",
        [Down] = "Transpose out-of-range notes 1 semitone down"
    };

    // Key and Speed option wrapper class for ComboBox binding
    public class KeyOption
    {
        public int Value { get; set; }
        public string Display { get; set; } = string.Empty;
    }

    public class SpeedOption
    {
        public double Value { get; set; }
        public string Display { get; set; } = string.Empty;
    }

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly IThemeService _theme;
    private readonly MainWindowViewModel _main;
    private int _keyOffset;
    private double _speed = 1.0;

    public SettingsPageViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _theme = ioc.Get<IThemeService>();
        _main = main;

        _keyOffset = Queue.OpenedFile?.Song.Key ?? 0;

        ThemeManager.Current.ApplicationTheme = Settings.AppTheme switch
        {
            0 => ApplicationTheme.Light,
            1 => ApplicationTheme.Dark,
            _ => null
        };
    }

    public bool AutoCheckUpdates { get; set; } = Settings.AutoCheckUpdates;

    public bool CanChangeTime => PlayTimerToken is null;

    public bool CanStartStopTimer => DateTime - DateTime.Now > TimeSpan.Zero;

    public bool CanUseSpeakers { get; set; } = true;

    public bool IncludeBetaUpdates { get; set; } = Settings.IncludeBetaUpdates;

    public bool IsCheckingUpdate { get; set; }

    public bool MergeNotes { get; set; } = Settings.MergeNotes;

    public string MidiFolder { get; set; } = Settings.MidiFolder;

    public bool HasMidiFolder => !string.IsNullOrEmpty(MidiFolder);

    public bool NeedsUpdate => ProgramVersion < LatestVersion.Version;

    [UsedImplicitly] public CancellationTokenSource? PlayTimerToken { get; private set; }

    public static CaptionedObject<Transition>? Transition { get; set; } =
        TransitionCollection.Transitions[Settings.SelectedTransition];

    public DateTime DateTime { get; set; } = DateTime.Now;

    [UsedImplicitly]
    public Dictionary<int, string> KeyOffsets { get; set; } = new()
    {
        [-27] = "A0",
        [-26] = "A♯0",
        [-25] = "B0",
        [-24] = "C1",
        [-23] = "C♯1",
        [-22] = "D1",
        [-21] = "D♯1",
        [-20] = "E1",
        [-19] = "F1",
        [-18] = "F♯1",
        [-17] = "G1",
        [-16] = "G♯1",
        [-15] = "A1",
        [-14] = "A♯1",
        [-13] = "B1",
        [-12] = "C2",
        [-11] = "C♯2",
        [-10] = "D2",
        [-9] = "D♯2",
        [-8] = "E2",
        [-7] = "F2",
        [-6] = "F♯2",
        [-5] = "G2",
        [-4] = "G♯2",
        [-3] = "A2",
        [-2] = "A♯2",
        [-1] = "B2",
        [0] = "C3",
        [1] = "C♯3",
        [2] = "D3",
        [3] = "D♯3",
        [4] = "E3",
        [5] = "F3",
        [6] = "F♯3",
        [7] = "G3",
        [8] = "G♯3",
        [9] = "A3",
        [10] = "A♯3",
        [11] = "B3",
        [12] = "C4 Middle C",
        [13] = "C♯4",
        [14] = "D4",
        [15] = "D♯4",
        [16] = "E4",
        [17] = "F4",
        [18] = "F♯4",
        [19] = "G4",
        [20] = "G♯4",
        [21] = "A4 Concert Pitch",
        [22] = "A♯4",
        [23] = "B4",
        [24] = "C5"
    };

    public GitVersion LatestVersion { get; set; } = new();

    public int KeyOffset
    {
        get => _keyOffset;
        set => SetAndNotify(ref _keyOffset, Math.Clamp(value, MinOffset, MaxOffset));
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
        set => SetAndNotify(ref _speed, Math.Round(Math.Clamp(value, 0.1, 4.0), 1));
    }

    public string SpeedDisplay => $"Speed: {Speed:0.0}x";

    public static string GenshinLocation
    {
        get => Settings.GenshinLocation;
        set => Settings.GenshinLocation = value;
    }

    public string Key => $"{KeyOffsets[KeyOffset]}";

    // KeyOptions for ComboBox binding
    public List<KeyOption> KeyOptions { get; } = new()
    {
        new() { Value = -27, Display = "-27 (A0)" },
        new() { Value = -26, Display = "-26 (A♯0)" },
        new() { Value = -25, Display = "-25 (B0)" },
        new() { Value = -24, Display = "-24 (C1)" },
        new() { Value = -23, Display = "-23 (C♯1)" },
        new() { Value = -22, Display = "-22 (D1)" },
        new() { Value = -21, Display = "-21 (D♯1)" },
        new() { Value = -20, Display = "-20 (E1)" },
        new() { Value = -19, Display = "-19 (F1)" },
        new() { Value = -18, Display = "-18 (F♯1)" },
        new() { Value = -17, Display = "-17 (G1)" },
        new() { Value = -16, Display = "-16 (G♯1)" },
        new() { Value = -15, Display = "-15 (A1)" },
        new() { Value = -14, Display = "-14 (A♯1)" },
        new() { Value = -13, Display = "-13 (B1)" },
        new() { Value = -12, Display = "-12 (C2)" },
        new() { Value = -11, Display = "-11 (C♯2)" },
        new() { Value = -10, Display = "-10 (D2)" },
        new() { Value = -9, Display = "-9 (D♯2)" },
        new() { Value = -8, Display = "-8 (E2)" },
        new() { Value = -7, Display = "-7 (F2)" },
        new() { Value = -6, Display = "-6 (F♯2)" },
        new() { Value = -5, Display = "-5 (G2)" },
        new() { Value = -4, Display = "-4 (G♯2)" },
        new() { Value = -3, Display = "-3 (A2)" },
        new() { Value = -2, Display = "-2 (A♯2)" },
        new() { Value = -1, Display = "-1 (B2)" },
        new() { Value = 0, Display = "0 (C3)" },
        new() { Value = 1, Display = "+1 (C♯3)" },
        new() { Value = 2, Display = "+2 (D3)" },
        new() { Value = 3, Display = "+3 (D♯3)" },
        new() { Value = 4, Display = "+4 (E3)" },
        new() { Value = 5, Display = "+5 (F3)" },
        new() { Value = 6, Display = "+6 (F♯3)" },
        new() { Value = 7, Display = "+7 (G3)" },
        new() { Value = 8, Display = "+8 (G♯3)" },
        new() { Value = 9, Display = "+9 (A3)" },
        new() { Value = 10, Display = "+10 (A♯3)" },
        new() { Value = 11, Display = "+11 (B3)" },
        new() { Value = 12, Display = "+12 (C4)" },
        new() { Value = 13, Display = "+13 (C♯4)" },
        new() { Value = 14, Display = "+14 (D4)" },
        new() { Value = 15, Display = "+15 (D♯4)" },
        new() { Value = 16, Display = "+16 (E4)" },
        new() { Value = 17, Display = "+17 (F4)" },
        new() { Value = 18, Display = "+18 (F♯4)" },
        new() { Value = 19, Display = "+19 (G4)" },
        new() { Value = 20, Display = "+20 (G♯4)" },
        new() { Value = 21, Display = "+21 (A4)" },
        new() { Value = 22, Display = "+22 (A♯4)" },
        new() { Value = 23, Display = "+23 (B4)" },
        new() { Value = 24, Display = "+24 (C5)" },
        new() { Value = 25, Display = "+25 (C♯5)" },
        new() { Value = 26, Display = "+26 (D5)" },
        new() { Value = 27, Display = "+27 (D♯5)" }
    };

    private KeyOption? _selectedKeyOption;
    public KeyOption? SelectedKeyOption
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

    // SpeedOptions for ComboBox binding
    public List<SpeedOption> SpeedOptions { get; } = new()
    {
        new() { Value = 0.1, Display = "0.1x" },
        new() { Value = 0.2, Display = "0.2x" },
        new() { Value = 0.3, Display = "0.3x" },
        new() { Value = 0.4, Display = "0.4x" },
        new() { Value = 0.5, Display = "0.5x" },
        new() { Value = 0.6, Display = "0.6x" },
        new() { Value = 0.7, Display = "0.7x" },
        new() { Value = 0.8, Display = "0.8x" },
        new() { Value = 0.9, Display = "0.9x" },
        new() { Value = 1.0, Display = "1.0x" },
        new() { Value = 1.1, Display = "1.1x" },
        new() { Value = 1.2, Display = "1.2x" },
        new() { Value = 1.3, Display = "1.3x" },
        new() { Value = 1.4, Display = "1.4x" },
        new() { Value = 1.5, Display = "1.5x" },
        new() { Value = 1.6, Display = "1.6x" },
        new() { Value = 1.7, Display = "1.7x" },
        new() { Value = 1.8, Display = "1.8x" },
        new() { Value = 1.9, Display = "1.9x" },
        new() { Value = 2.0, Display = "2.0x" },
        new() { Value = 2.5, Display = "2.5x" },
        new() { Value = 3.0, Display = "3.0x" },
        new() { Value = 3.5, Display = "3.5x" },
        new() { Value = 4.0, Display = "4.0x" }
    };

    private SpeedOption? _selectedSpeedOption;
    public SpeedOption? SelectedSpeedOption
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

    public uint MergeMilliseconds { get; set; } = Settings.MergeMilliseconds;

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
    public void OnThemeChanged()
    {
        _theme.SetTheme(ThemeManager.Current.ApplicationTheme switch
        {
            ApplicationTheme.Light => ThemeType.Light,
            ApplicationTheme.Dark => ThemeType.Dark,
            _ => _theme.GetSystemTheme()
        });

        Settings.Modify(s => s.AppTheme = (int?)ThemeManager.Current.ApplicationTheme ?? -1);
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
    }

    [UsedImplicitly]
    private void OnMergeMillisecondsChanged()
    {
        Settings.Modify(s => s.MergeMilliseconds = MergeMilliseconds);
        _events.Publish(this);
    }

    [UsedImplicitly]
    private void OnMergeNotesChanged()
    {
        Settings.Modify(s => s.MergeNotes = MergeNotes);
        _events.Publish(new MergeNotesNotification(MergeNotes));
    }

    [UsedImplicitly]
    private void OnSelectedInstrumentIndexChanged()
    {
        var instrument = (int)SelectedInstrument.Key;
        Settings.Modify(s => s.SelectedInstrument = instrument);
    }

    [UsedImplicitly]
    private void OnSelectedLayoutIndexChanged()
    {
        var layout = (int)SelectedLayout.Key;
        Settings.Modify(s => s.SelectedLayout = layout);
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
    }
}
