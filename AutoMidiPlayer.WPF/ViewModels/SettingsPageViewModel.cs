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
using AutoMidiPlayer.WPF.Core.Games;
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

    public static List<KeypressInputModeOption> KeypressInputModes { get; } = new()
    {
        new(
            "Input Simulator",
            "Uses InputSimulator to inject keyboard events globally. Best for standard desktop apps, but some games may ignore it.",
            mode: KeypressInputMode.InputSimulator),
        new(
            "Direct Input (SendInput)",
            "Uses Win32 SendInput for global low-level key injection. This is usually the most reliable option for games.",
            mode: KeypressInputMode.DirectInput),
        new(
            "Window Message (PostMessage)",
            "Sends WM_KEYDOWN/WM_KEYUP directly to the active game window. Use this for games that block injected global input.",
            mode: KeypressInputMode.WindowMessage)
    };

    public static List<MouseStopClickOption> MouseStopClickOptions { get; } = new()
    {
        new("Off", MouseStopClickMode.Off),
        new("Left Click", MouseStopClickMode.LeftClick),
        new("Right Click", MouseStopClickMode.RightClick),
        new("Middle Click", MouseStopClickMode.MiddleClick)
    };

    public static List<MusicConstants.KeyOption> NewSongDefaultKeyOptions { get; } = MusicConstants.GenerateKeyOptions();

    public static List<KeyValuePair<Transpose, string>> NewSongTransposeOptions { get; } =
        MusicConstants.TransposeNames.ToList();

    public static List<MusicConstants.SpeedOption> NewSongSpeedOptions { get; } =
        MusicConstants.GenerateSpeedOptions();

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly GlobalHotkeyService _hotkeyService;
    private AccentColorOption _selectedAccentColor = null!;
    private bool _isApplyingKeypressMode;
    private MouseStopClickOption _selectedMouseStopClickOption = null!;
    private KeypressInputModeOption _selectedKeypressInputMode = null!;
    private ThemeOption _selectedTheme = null!;
    private MusicConstants.KeyOption _selectedNewSongDefaultKeyOption = null!;
    private MusicConstants.KeyOption _selectedNewSongKeyOption = null!;
    private KeyValuePair<Transpose, string> _selectedNewSongTransposeOption;
    private MusicConstants.SpeedOption _selectedNewSongSpeedOption = null!;
    private bool _isSynchronizingNewSongDefaults;
    private FileSystemWatcher? _midiFolderWatcher;
    private FileSystemWatcher? _midiFolderParentWatcher;
    private CancellationTokenSource? _midiFolderScanDebounceToken;
    private static readonly TimeSpan MidiFolderWatchDebounceDelay = TimeSpan.FromMilliseconds(750);

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
        // Avoid deferred theme-refresh work during startup initialization.
        ApplyAccentColor(_selectedAccentColor.ColorHex, scheduleDeferredRefresh: false);

        SelectedInstrument = Core.Keyboard.GetInstrumentAtIndex(Settings.SelectedInstrument);
        SelectedLayout = Core.Keyboard.GetLayoutAtIndex(Settings.SelectedLayout);

        // Initialize game locations from registry (shared with MainWindowViewModel's Games list)
        GameLocations = new BindableCollection<GameInfo>(
            GameRegistry.AllGames.Select(g => new GameInfo(g)));

        // Apply current keyboard input settings on startup.
        KeyboardPlayer.UseDirectInput = UseDirectInput;
        KeyboardPlayer.UseWindowMessage = UseWindowMessage;
        KeyboardPlayer.KeyboardPressDelayMs = Math.Clamp(KeyboardPressDelayMs, 0, 1000);
        KeyboardPlayer.EnableKeyUp = EnableKeyUp;

        _selectedKeypressInputMode = ResolveKeypressInputMode(UseDirectInput, UseWindowMessage);
        _selectedMouseStopClickOption = ResolveMouseStopClickOption(Settings.MouseStopClickMode);
        InitializeNewSongDefaults();
        ApplySmoothScrollingResource();

        ConfigureMidiFolderWatcher();
    }

    /// <summary>Observable collection of game location entries for the settings UI</summary>
    public BindableCollection<GameInfo> GameLocations { get; }

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

    public bool SmoothScrollingEnabled
    {
        get => GetSmoothScrollingEnabled();
        set
        {
            if (GetSmoothScrollingEnabled() == value)
                return;

            SetSmoothScrollingEnabled(value);
            ApplySmoothScrollingResource();
            NotifyOfPropertyChange();
        }
    }

    private void ApplyAccentColor(string hexColor, bool scheduleDeferredRefresh = true)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            ApplyColorToAllSystems(color, scheduleDeferredRefresh);
        }
        catch
        {
            // Fallback to Green if color parsing fails
            var fallbackColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954");
            ApplyColorToAllSystems(fallbackColor, scheduleDeferredRefresh);
        }
    }

    private void ApplyColorToAllSystems(System.Windows.Media.Color color, bool scheduleDeferredRefresh)
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

        if (scheduleDeferredRefresh)
        {
            // Delayed refresh keeps accent color stable after async theme updates.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Action(() =>
                {
                    // Re-apply custom WPF-UI resources after theme system has finished
                    SetOrUpdateResource("SystemAccentColorPrimary", color);
                    SetOrUpdateResource("SystemAccentColorPrimaryBrush", accentBrush);
                    SetOrUpdateResource("SystemAccentColorSecondaryBrush", light1Brush);
                    SetOrUpdateResource("SystemAccentColorTertiaryBrush", light2Brush);

                    // Force full theme re-apply so NavigationView, drawers, and all views refresh
                    ApplicationThemeManager.Apply(currentTheme, WindowBackdropType.Mica, false);

                    // Re-apply accent after theme refresh to ensure our custom colors persist
                    ApplicationAccentColorManager.Apply(color, currentTheme, true);
                    SetOrUpdateResource("SystemAccentColorPrimary", color);
                    SetOrUpdateResource("SystemAccentColorPrimaryBrush", accentBrush);
                    SetOrUpdateResource("SystemAccentColorSecondaryBrush", light1Brush);
                    SetOrUpdateResource("SystemAccentColorTertiaryBrush", light2Brush);
                }));
        }

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

    private static bool GetSmoothScrollingEnabled()
    {
        return Settings.SmoothScrollingEnabled;
    }

    private static void SetSmoothScrollingEnabled(bool value)
    {
        Settings.Modify(s => s.SmoothScrollingEnabled = value);
    }

    private static void ApplySmoothScrollingResource()
    {
        var app = Application.Current;
        if (app is null)
            return;

        var isEnabled = GetSmoothScrollingEnabled();
        if (app.Resources.Contains("SmoothScrollingEnabled"))
            app.Resources["SmoothScrollingEnabled"] = isEnabled;
        else
            app.Resources.Add("SmoothScrollingEnabled", isEnabled);
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

    public bool IncludeBetaUpdates { get; set; } = Settings.IncludeBetaUpdates;

    public bool IsCheckingUpdate { get; set; }

    public bool IsScanningMidiFolder { get; set; }

    public bool AutoScanMidiFolder { get; set; } = Settings.AutoScanMidiFolder;

    public bool UseDirectInput { get; set; } = Settings.UseDirectInput;

    public bool UseWindowMessage { get; set; } = Settings.UseWindowMessage;

    public KeypressInputModeOption SelectedKeypressInputMode
    {
        get => _selectedKeypressInputMode;
        set
        {
            if (SetAndNotify(ref _selectedKeypressInputMode, value) && value is not null)
            {
                _isApplyingKeypressMode = true;
                try
                {
                    ApplyKeypressMode(value.Mode);
                }
                finally
                {
                    _isApplyingKeypressMode = false;
                }

                SyncSelectedKeypressInputMode();
            }
        }
    }

    public string KeypressInputDescription => SelectedKeypressInputMode?.Description ?? string.Empty;

    public MouseStopClickOption SelectedMouseStopClickOption
    {
        get => _selectedMouseStopClickOption;
        set
        {
            if (SetAndNotify(ref _selectedMouseStopClickOption, value) && value is not null)
            {
                Settings.Modify(s => s.MouseStopClickMode = (int)value.Mode);
                _hotkeyService.RefreshMouseStopClickMode();
            }
        }
    }

    public int KeyboardPressDelayMs { get; set; } = Settings.KeyboardPressDelayMs;

    public bool EnableKeyUp { get; set; } = Settings.EnableKeyUp;

    public bool AutoEnableListenMode
    {
        get => Settings.AutoEnableListenMode;
        set
        {
            if (Settings.AutoEnableListenMode == value)
                return;

            Settings.Modify(s => s.AutoEnableListenMode = value);
            NotifyOfPropertyChange();
        }
    }

    public bool AutoDetectDefaultKey
    {
        get => Settings.AutoDetectDefaultKey;
        set
        {
            if (Settings.AutoDetectDefaultKey == value)
                return;

            Settings.Modify(s => s.AutoDetectDefaultKey = value);
            NotifyOfPropertyChange();
        }
    }

    public string DefaultSongArtist { get; set; } = Settings.DefaultSongArtist;

    public string DefaultSongAlbum { get; set; } = Settings.DefaultSongAlbum;

    public double DefaultSongCustomBpm { get; set; } = Settings.DefaultSongCustomBpm;

    public bool DefaultSongMergeNotes { get; set; } = Settings.DefaultSongMergeNotes;

    public int DefaultSongMergeMilliseconds { get; set; } = (int)Math.Clamp((int)Settings.DefaultSongMergeMilliseconds, 1, 1000);

    public bool DefaultSongHoldNotes { get; set; } = Settings.DefaultSongHoldNotes;

    public bool CanEditDefaultSongMergeMilliseconds => DefaultSongMergeNotes;

    public List<MusicConstants.KeyOption> NewSongKeyOptions { get; private set; } = new();

    public MusicConstants.KeyOption SelectedNewSongDefaultKeyOption
    {
        get => _selectedNewSongDefaultKeyOption;
        set
        {
            if (!SetAndNotify(ref _selectedNewSongDefaultKeyOption, value) || value is null || _isSynchronizingNewSongDefaults)
                return;

            _isSynchronizingNewSongDefaults = true;
            try
            {
                var preferredKey = _selectedNewSongKeyOption?.Value ?? Settings.DefaultSongKey;
                SyncNewSongKeyOptions(value.Value, preferredKey, saveSettings: true);
            }
            finally
            {
                _isSynchronizingNewSongDefaults = false;
            }
        }
    }

    public MusicConstants.KeyOption SelectedNewSongKeyOption
    {
        get => _selectedNewSongKeyOption;
        set
        {
            if (!SetAndNotify(ref _selectedNewSongKeyOption, value) || value is null || _isSynchronizingNewSongDefaults)
                return;

            Settings.Modify(s => s.DefaultSongKey = value.Value);
        }
    }

    public KeyValuePair<Transpose, string> SelectedNewSongTransposeOption
    {
        get => _selectedNewSongTransposeOption;
        set
        {
            if (SetAndNotify(ref _selectedNewSongTransposeOption, value) && !_isSynchronizingNewSongDefaults)
                Settings.Modify(s => s.DefaultSongTranspose = (int)value.Key);
        }
    }

    public MusicConstants.SpeedOption SelectedNewSongSpeedOption
    {
        get => _selectedNewSongSpeedOption;
        set
        {
            if (!SetAndNotify(ref _selectedNewSongSpeedOption, value) || value is null || _isSynchronizingNewSongDefaults)
                return;

            Settings.Modify(s => s.DefaultSongSpeed = value.Value);
        }
    }

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

    public bool IsMidiFolderMissing => HasMidiFolder && !Directory.Exists(MidiFolder);

    public bool HasAccessibleMidiFolder => HasMidiFolder && !IsMidiFolderMissing;

    public bool ShowMidiFolderManualRefresh => HasAccessibleMidiFolder && !AutoScanMidiFolder;

    public bool NeedsUpdate => ProgramVersion < LatestVersion.Version;

    [UsedImplicitly] public CancellationTokenSource? PlayTimerToken { get; private set; }

    public static CaptionedObject<Transition>? Transition { get; set; } =
        TransitionCollection.Transitions[Settings.SelectedTransition];

    public DateTime DateTime { get; set; } = DateTime.Now;

    public GitVersion LatestVersion { get; set; } = new();

    public KeyValuePair<string, string> SelectedInstrument { get; set; }

    public KeyValuePair<string, string> SelectedLayout { get; set; }

    private void InitializeNewSongDefaults()
    {
        var defaultKey = Math.Clamp(Settings.DefaultSongDefaultKey, MusicConstants.MinKeyOffset, MusicConstants.MaxKeyOffset);
        var defaultKeyOption = NewSongDefaultKeyOptions.FirstOrDefault(option => option.Value == defaultKey)
            ?? NewSongDefaultKeyOptions.First();

        var transpose = Enum.IsDefined(typeof(Transpose), Settings.DefaultSongTranspose)
            ? (Transpose)Settings.DefaultSongTranspose
            : Ignore;

        var transposeOption = NewSongTransposeOptions.FirstOrDefault(option => option.Key == transpose);
        if (transposeOption.Equals(default(KeyValuePair<Transpose, string>)))
            transposeOption = NewSongTransposeOptions.First(option => option.Key == Ignore);

        var requestedSpeed = Settings.DefaultSongSpeed <= 0
            ? 1.0
            : Math.Clamp(Settings.DefaultSongSpeed, 0.1, 4.0);

        var speedOption = NewSongSpeedOptions
            .OrderBy(option => Math.Abs(option.Value - requestedSpeed))
            .First();

        _isSynchronizingNewSongDefaults = true;
        try
        {
            _selectedNewSongDefaultKeyOption = defaultKeyOption;
            NotifyOfPropertyChange(nameof(SelectedNewSongDefaultKeyOption));

            SyncNewSongKeyOptions(defaultKeyOption.Value, Settings.DefaultSongKey, saveSettings: false);

            _selectedNewSongTransposeOption = transposeOption;
            NotifyOfPropertyChange(nameof(SelectedNewSongTransposeOption));

            _selectedNewSongSpeedOption = speedOption;
            NotifyOfPropertyChange(nameof(SelectedNewSongSpeedOption));
        }
        finally
        {
            _isSynchronizingNewSongDefaults = false;
        }

        var sanitizedMergeMs = Math.Clamp((int)Settings.DefaultSongMergeMilliseconds, 1, 1000);
        if (Settings.DefaultSongDefaultKey != defaultKey
            || Settings.DefaultSongKey != _selectedNewSongKeyOption.Value
            || Settings.DefaultSongTranspose != (int)_selectedNewSongTransposeOption.Key
            || Math.Abs(Settings.DefaultSongSpeed - _selectedNewSongSpeedOption.Value) > 0.001
            || (int)Settings.DefaultSongMergeMilliseconds != sanitizedMergeMs)
        {
            Settings.Modify(s =>
            {
                s.DefaultSongDefaultKey = defaultKey;
                s.DefaultSongKey = _selectedNewSongKeyOption.Value;
                s.DefaultSongTranspose = (int)_selectedNewSongTransposeOption.Key;
                s.DefaultSongSpeed = _selectedNewSongSpeedOption.Value;
                s.DefaultSongMergeMilliseconds = (uint)sanitizedMergeMs;
            });

            DefaultSongMergeMilliseconds = sanitizedMergeMs;
        }
    }

    private void SyncNewSongKeyOptions(int defaultKey, int preferredKey, bool saveSettings)
    {
        var keyOptions = MusicConstants.GenerateKeyOptions(defaultKey);
        var clampedKey = Math.Clamp(
            preferredKey,
            MusicConstants.GetRelativeMinKeyOffset(defaultKey),
            MusicConstants.GetRelativeMaxKeyOffset(defaultKey));

        var selectedKeyOption = keyOptions.FirstOrDefault(option => option.Value == clampedKey)
            ?? keyOptions.First();

        NewSongKeyOptions = keyOptions;
        NotifyOfPropertyChange(nameof(NewSongKeyOptions));

        _selectedNewSongKeyOption = selectedKeyOption;
        NotifyOfPropertyChange(nameof(SelectedNewSongKeyOption));

        if (!saveSettings)
            return;

        Settings.Modify(s =>
        {
            s.DefaultSongDefaultKey = defaultKey;
            s.DefaultSongKey = selectedKeyOption.Value;
        });
    }

    /// <summary>
    /// Path where application data (database, logs, etc.) is stored
    /// </summary>
    public static string DataLocation => AppPaths.AppDataDirectory;

    public string TimerText => CanChangeTime ? "Start" : "Stop";

    [UsedImplicitly] public string UpdateString { get; set; } = string.Empty;

    public static Version ProgramVersion => Assembly.GetExecutingAssembly().GetName().Version!;

    public static string ProgramVersionDisplay => GetVersionDisplay(ProgramVersion);

    private static string GetVersionDisplay(Version version)
    {
        if (version == null)
            return "unknown";

        if (version.Revision == 0 && version.Build >= 0)
            return $"{version.Major}.{version.Minor}.{version.Build}";

        if (version.Build < 0)
            return $"{version.Major}.{version.Minor}";

        return version.ToString();
    }

    private QueueViewModel Queue => _main.QueueView;

    public async Task<bool> TryGetLocationAsync()
    {
        var foundAny = false;

        foreach (var gameInfo in GameLocations)
        {
            var location = GameRegistry.TryFindGameLocation(gameInfo.Definition);
            if (location != null)
            {
                gameInfo.Location = location;
                foundAny = true;
            }
        }

        return await Task.FromResult(foundAny);
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
        var missingGames = GameLocations
            .Where(g => !File.Exists(g.Location))
            .Select(g => g.DisplayName)
            .ToList();

        if (missingGames.Count == 0) return;

        var gameList = string.Join(", ", missingGames);
        var message = $"Could not find game executable locations for: {gameList}. You can set game paths in Settings.";
        var dialog = DialogHelper.CreateDialog();
        dialog.Title = "Error";
        dialog.Content = message;
        dialog.PrimaryButtonText = "Go to Settings";
        dialog.CloseButtonText = "Ignore";

        ContentDialogResult result;

        try
        {
            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                result = await dialog.ShowAsync();
            }
            else
            {
                CrashLogger.Log("DialogHost was not ready while showing missing game location dialog. Falling back to MessageBox.");
                var fallbackResult = System.Windows.MessageBox.Show(
                    message,
                    "Error",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Warning);

                if (fallbackResult == System.Windows.MessageBoxResult.OK)
                    _main.NavigateToSettings();

                return;
            }
        }
        catch (Exception dialogError)
        {
            CrashLogger.Log("Failed to display missing game location dialog.");
            CrashLogger.LogException(dialogError);

            var fallbackResult = System.Windows.MessageBox.Show(
                message,
                "Error",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning);

            if (fallbackResult == System.Windows.MessageBoxResult.OK)
                _main.NavigateToSettings();

            return;
        }

        if (result == ContentDialogResult.Primary)
        {
            _main.NavigateToSettings();
        }
    }

    /// <summary>
    /// Browse for a game executable location. Called from settings view via Stylet action.
    /// </summary>
    [PublicAPI]
    public async Task BrowseGameLocation(GameInfo game)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Executable|*.exe|All files (*.*)|*.*",
            Title = $"Find {game.DisplayName} executable"
        };

        var success = openFileDialog.ShowDialog() == true;
        if (!success) return;

        var fileName = openFileDialog.FileName;
        if (Path.GetFileName(fileName).Equals("launcher.exe", StringComparison.OrdinalIgnoreCase))
        {
            var dialog = DialogHelper.CreateDialog();
            dialog.Title = "Incorrect Location";
            dialog.Content = "launcher.exe is not the game executable. Please select the actual game executable.";
            dialog.CloseButtonText = "Ok";
            await dialog.ShowAsync();
            return;
        }

        game.Location = fileName;
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
            Settings.Modify(settings => settings.MidiFolder = MidiFolder);

            // Auto-scan the folder
            await ScanMidiFolder();
        }
    }

    public async Task ScanMidiFolder()
    {
        if (IsScanningMidiFolder)
            return;

        if (string.IsNullOrWhiteSpace(MidiFolder))
            return;

        var folderPath = MidiFolder;
        var startedAt = DateTime.UtcNow;
        CrashLogger.LogStep("MIDI_SCAN_BEGIN", $"folder='{folderPath}'");

        IsScanningMidiFolder = true;
        try
        {
            await _main.FileService.ScanFolder(folderPath);
        }
        finally
        {
            IsScanningMidiFolder = false;
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            CrashLogger.LogStep("MIDI_SCAN_END", $"folder='{folderPath}' | elapsedMs={elapsedMs:F0}");
        }
    }

    public void ClearMidiFolder()
    {
        MidiFolder = string.Empty;
        Settings.Modify(settings => settings.MidiFolder = string.Empty);
    }

    public void OpenMidiFolder()
    {
        if (string.IsNullOrWhiteSpace(MidiFolder) || !Directory.Exists(MidiFolder))
            return;

        System.Diagnostics.Process.Start("explorer.exe", MidiFolder);
    }

    public void OpenDataFolder()
    {
        AppPaths.EnsureDirectoryExists();
        System.Diagnostics.Process.Start("explorer.exe", AppPaths.AppDataDirectory);
    }

    [UsedImplicitly]
    private void OnMidiFolderChanged()
    {
        NotifyMidiFolderStateChanged();
        ConfigureMidiFolderWatcher();
    }

    [UsedImplicitly]
    private void OnAutoScanMidiFolderChanged()
    {
        Settings.Modify(settings => settings.AutoScanMidiFolder = AutoScanMidiFolder);
        CrashLogger.LogStep("MIDI_AUTO_SCAN_TOGGLE", $"enabled={AutoScanMidiFolder}");
        NotifyOfPropertyChange(nameof(ShowMidiFolderManualRefresh));

        ConfigureMidiFolderWatcher();

        if (AutoScanMidiFolder)
            _ = ScanMidiFolder();
    }

    private void NotifyMidiFolderStateChanged()
    {
        NotifyOfPropertyChange(nameof(HasMidiFolder));
        NotifyOfPropertyChange(nameof(IsMidiFolderMissing));
        NotifyOfPropertyChange(nameof(HasAccessibleMidiFolder));
        NotifyOfPropertyChange(nameof(ShowMidiFolderManualRefresh));
    }

    private void ConfigureMidiFolderWatcher()
    {
        DisposeMidiFolderWatcher();
        NotifyMidiFolderStateChanged();

        if (!AutoScanMidiFolder)
            return;

        if (string.IsNullOrWhiteSpace(MidiFolder))
            return;

        try
        {
            var normalizedMidiFolder = NormalizeMidiFolderPath(MidiFolder);
            var parentFolder = string.IsNullOrWhiteSpace(normalizedMidiFolder)
                ? null
                : Path.GetDirectoryName(normalizedMidiFolder);

            if (!string.IsNullOrWhiteSpace(parentFolder) && Directory.Exists(parentFolder))
            {
                _midiFolderParentWatcher = new FileSystemWatcher(parentFolder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.DirectoryName
                                   | NotifyFilters.FileName
                                   | NotifyFilters.CreationTime
                                   | NotifyFilters.LastWrite,
                    Filter = "*.*",
                    EnableRaisingEvents = true
                };

                _midiFolderParentWatcher.Created += HandleMidiFolderParentWatcherChanged;
                _midiFolderParentWatcher.Deleted += HandleMidiFolderParentWatcherChanged;
                _midiFolderParentWatcher.Renamed += HandleMidiFolderParentWatcherRenamed;
            }

            if (!Directory.Exists(MidiFolder))
                return;

            _midiFolderWatcher = new FileSystemWatcher(MidiFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.CreationTime
                               | NotifyFilters.LastWrite,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            _midiFolderWatcher.Created += HandleMidiFolderWatcherChanged;
            _midiFolderWatcher.Deleted += HandleMidiFolderWatcherChanged;
            _midiFolderWatcher.Renamed += HandleMidiFolderWatcherRenamed;
        }
        catch (Exception error)
        {
            CrashLogger.Log($"Failed to configure MIDI folder watcher for '{MidiFolder}'.");
            CrashLogger.LogException(error);
            DisposeMidiFolderWatcher();
        }
    }

    private void DisposeMidiFolderWatcher()
    {
        if (_midiFolderWatcher is not null)
        {
            _midiFolderWatcher.EnableRaisingEvents = false;
            _midiFolderWatcher.Created -= HandleMidiFolderWatcherChanged;
            _midiFolderWatcher.Deleted -= HandleMidiFolderWatcherChanged;
            _midiFolderWatcher.Renamed -= HandleMidiFolderWatcherRenamed;
            _midiFolderWatcher.Dispose();
            _midiFolderWatcher = null;
        }

        if (_midiFolderParentWatcher is not null)
        {
            _midiFolderParentWatcher.EnableRaisingEvents = false;
            _midiFolderParentWatcher.Created -= HandleMidiFolderParentWatcherChanged;
            _midiFolderParentWatcher.Deleted -= HandleMidiFolderParentWatcherChanged;
            _midiFolderParentWatcher.Renamed -= HandleMidiFolderParentWatcherRenamed;
            _midiFolderParentWatcher.Dispose();
            _midiFolderParentWatcher = null;
        }

        _midiFolderScanDebounceToken?.Cancel();
        _midiFolderScanDebounceToken?.Dispose();
        _midiFolderScanDebounceToken = null;
    }

    private void HandleMidiFolderWatcherChanged(object? sender, FileSystemEventArgs e)
    {
        if (!ShouldAutoScanFromWatcherEvent(e.FullPath))
            return;

        QueueAutoScanFromWatcher();
    }

    private void HandleMidiFolderParentWatcherChanged(object? sender, FileSystemEventArgs e)
    {
        if (!IsWatchedMidiFolderPath(e.FullPath))
            return;

        ConfigureMidiFolderWatcher();
        NotifyMidiFolderStateChanged();
        QueueAutoScanFromWatcher();
    }

    private void HandleMidiFolderParentWatcherRenamed(object? sender, RenamedEventArgs e)
    {
        if (!IsWatchedMidiFolderPath(e.FullPath) && !IsWatchedMidiFolderPath(e.OldFullPath))
            return;

        ConfigureMidiFolderWatcher();
        NotifyMidiFolderStateChanged();
        QueueAutoScanFromWatcher();
    }

    private void HandleMidiFolderWatcherRenamed(object? sender, RenamedEventArgs e)
    {
        if (!ShouldAutoScanFromWatcherEvent(e.FullPath) && !ShouldAutoScanFromWatcherEvent(e.OldFullPath))
            return;

        QueueAutoScanFromWatcher();
    }

    private static bool ShouldAutoScanFromWatcherEvent(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        if (AutoImportExclusionStore.IsMidiFilePath(fullPath))
            return true;

        // For directory delete/rename events the path may no longer exist.
        return !Path.HasExtension(fullPath) || Directory.Exists(fullPath);
    }

    private bool IsWatchedMidiFolderPath(string? fullPath)
    {
        var normalizedPath = NormalizeMidiFolderPath(fullPath);
        var normalizedMidiFolder = NormalizeMidiFolderPath(MidiFolder);

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedMidiFolder))
            return false;

        return string.Equals(normalizedPath, normalizedMidiFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeMidiFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim();

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private void QueueAutoScanFromWatcher()
    {
        if (!AutoScanMidiFolder)
            return;

        _midiFolderScanDebounceToken?.Cancel();
        _midiFolderScanDebounceToken?.Dispose();
        _midiFolderScanDebounceToken = new CancellationTokenSource();
        var token = _midiFolderScanDebounceToken.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(MidiFolderWatchDebounceDelay, token);
                if (token.IsCancellationRequested)
                    return;

                var app = Application.Current;
                if (app?.Dispatcher is null)
                    return;

                var scanTask = await app.Dispatcher.InvokeAsync(() => ScanMidiFolder());
                await scanTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when a newer file event supersedes this scan.
            }
            catch (Exception error)
            {
                CrashLogger.Log("MIDI folder auto-scan failed.");
                CrashLogger.LogException(error);
            }
        });
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

        var appTheme = currentTheme switch
        {
            WpfUiApplicationTheme.Light => 0,
            WpfUiApplicationTheme.Dark => 1,
            _ => -1
        };

        var changed = Settings.AppTheme != appTheme;
        if (changed)
        {
            Settings.Modify(s => s.AppTheme = appTheme);
        }

        // Only re-apply accent when theme state actually changes.
        if (changed)
            ApplyAccentColor(_selectedAccentColor.ColorHex);
    }

    [UsedImplicitly]
    public void SetTimeToNow() => DateTime = DateTime.Now;

    protected override void OnActivate()
    {
        CrashLogger.LogPageVisit("Settings", source: "screen-activate");

        if (AutoCheckUpdates)
            _ = CheckForUpdate();
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
    private void OnDefaultSongArtistChanged()
    {
        Settings.Modify(s => s.DefaultSongArtist = string.IsNullOrWhiteSpace(DefaultSongArtist)
            ? string.Empty
            : DefaultSongArtist.Trim());
    }

    [UsedImplicitly]
    private void OnDefaultSongAlbumChanged()
    {
        Settings.Modify(s => s.DefaultSongAlbum = string.IsNullOrWhiteSpace(DefaultSongAlbum)
            ? string.Empty
            : DefaultSongAlbum.Trim());
    }

    [UsedImplicitly]
    private void OnDefaultSongCustomBpmChanged()
    {
        var clampedBpm = double.IsNaN(DefaultSongCustomBpm)
            ? 0
            : Math.Clamp(DefaultSongCustomBpm, 0, 999);

        if (Math.Abs(clampedBpm - DefaultSongCustomBpm) > 0.001)
        {
            DefaultSongCustomBpm = clampedBpm;
            return;
        }

        Settings.Modify(s => s.DefaultSongCustomBpm = clampedBpm);
    }

    [UsedImplicitly]
    private void OnDefaultSongMergeNotesChanged()
    {
        Settings.Modify(s => s.DefaultSongMergeNotes = DefaultSongMergeNotes);
        NotifyOfPropertyChange(nameof(CanEditDefaultSongMergeMilliseconds));
    }

    [UsedImplicitly]
    private void OnDefaultSongMergeMillisecondsChanged()
    {
        var clampedMergeMs = Math.Clamp(DefaultSongMergeMilliseconds, 1, 1000);
        if (clampedMergeMs != DefaultSongMergeMilliseconds)
        {
            DefaultSongMergeMilliseconds = clampedMergeMs;
            return;
        }

        Settings.Modify(s => s.DefaultSongMergeMilliseconds = (uint)clampedMergeMs);
    }

    [UsedImplicitly]
    private void OnDefaultSongHoldNotesChanged() =>
        Settings.Modify(s => s.DefaultSongHoldNotes = DefaultSongHoldNotes);

    [UsedImplicitly]
    private void OnUseDirectInputChanged()
    {
        Settings.UseDirectInput = UseDirectInput;
        Settings.Save();
        KeyboardPlayer.UseDirectInput = UseDirectInput;

        if (!_isApplyingKeypressMode)
            SyncSelectedKeypressInputMode();
    }

    [UsedImplicitly]
    private void OnUseWindowMessageChanged()
    {
        Settings.UseWindowMessage = UseWindowMessage;
        Settings.Save();
        KeyboardPlayer.UseWindowMessage = UseWindowMessage;

        if (!_isApplyingKeypressMode)
            SyncSelectedKeypressInputMode();
    }

    [UsedImplicitly]
    private void OnKeyboardPressDelayMsChanged()
    {
        var clampedDelay = Math.Clamp(KeyboardPressDelayMs, 0, 1000);
        if (clampedDelay != KeyboardPressDelayMs)
            KeyboardPressDelayMs = clampedDelay;

        Settings.KeyboardPressDelayMs = clampedDelay;
        Settings.Save();
        KeyboardPlayer.KeyboardPressDelayMs = clampedDelay;
    }

    [UsedImplicitly]
    private void OnEnableKeyUpChanged()
    {
        Settings.EnableKeyUp = EnableKeyUp;
        Settings.Save();
        KeyboardPlayer.EnableKeyUp = EnableKeyUp;
    }

    private static KeypressInputModeOption ResolveKeypressInputMode(bool useDirectInput, bool useWindowMessage)
    {
        if (useWindowMessage)
            return GetKeypressInputModeOption(KeypressInputMode.WindowMessage);

        if (useDirectInput)
            return GetKeypressInputModeOption(KeypressInputMode.DirectInput);

        return GetKeypressInputModeOption(KeypressInputMode.InputSimulator);
    }

    private static KeypressInputModeOption GetKeypressInputModeOption(KeypressInputMode mode)
    {
        return KeypressInputModes.First(option => option.Mode == mode);
    }

    private void ApplyKeypressMode(KeypressInputMode mode)
    {
        switch (mode)
        {
            case KeypressInputMode.WindowMessage:
                UseDirectInput = false;
                UseWindowMessage = true;
                break;

            case KeypressInputMode.DirectInput:
                UseDirectInput = true;
                UseWindowMessage = false;
                break;

            default:
                UseDirectInput = false;
                UseWindowMessage = false;
                break;
        }
    }

    private void SyncSelectedKeypressInputMode()
    {
        var resolvedMode = ResolveKeypressInputMode(UseDirectInput, UseWindowMessage);
        if (!ReferenceEquals(_selectedKeypressInputMode, resolvedMode))
        {
            _selectedKeypressInputMode = resolvedMode;
            NotifyOfPropertyChange(nameof(SelectedKeypressInputMode));
        }

        NotifyOfPropertyChange(nameof(KeypressInputDescription));
    }

    private static MouseStopClickOption ResolveMouseStopClickOption(int modeValue)
    {
        var mode = Enum.IsDefined(typeof(MouseStopClickMode), modeValue)
            ? (MouseStopClickMode)modeValue
            : MouseStopClickMode.Off;

        return MouseStopClickOptions.First(option => option.Mode == mode);
    }
}

public class AccentColorOption(string name, string colorHex)
{
    public string Name { get; } = name;
    public string ColorHex { get; } = colorHex;
    public System.Windows.Media.SolidColorBrush ColorBrush { get; } = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));

    public override string ToString() => Name;
}

public class ThemeOption(string name, WpfUiApplicationTheme value)
{
    public string Name { get; } = name;
    public WpfUiApplicationTheme Value { get; } = value;

    public override string ToString() => Name;
}

public enum KeypressInputMode
{
    InputSimulator = 0,
    DirectInput = 1,
    WindowMessage = 2
}

public class KeypressInputModeOption(
    string name,
    string description,
    KeypressInputMode mode)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public KeypressInputMode Mode { get; } = mode;

    public override string ToString() => Name;
}

public class MouseStopClickOption(string name, MouseStopClickMode mode)
{
    public string Name { get; } = name;
    public MouseStopClickMode Mode { get; } = mode;

    public override string ToString() => Name;
}
