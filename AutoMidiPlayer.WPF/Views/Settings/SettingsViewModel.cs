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
using AutoMidiPlayer.WPF.MessageBox;
using AutoMidiPlayer.WPF.Services;
using AutoMidiPlayer.WPF.Views;
using JetBrains.Annotations;
using Microsoft.Win32;
using PropertyChanged;
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using static AutoMidiPlayer.Data.Entities.Transpose;
using WpfUiApplicationTheme = Wpf.Ui.Appearance.ApplicationTheme;
using Wpf.Ui.Controls;
using AutoMidiPlayer.WPF.Controls.Snackbar;

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

    public static List<UpdateCheckFrequencyOption> UpdateCheckFrequencyOptions { get; } = new()
    {
        new("10 Minutes", 0),
        new("Hourly", 1),
        new("Daily", 2),
        new("Weekly", 3),
        new("On Launch", 4)
    };

    public static List<KeypressInputModeOption> KeypressInputModes { get; } = new()
    {
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

    public static List<MusicConstants.KeyOption> NewSongBaseKeyOptions { get; } = MusicConstants.GenerateKeyOptions();

    public static List<KeyValuePair<Transpose, string>> NewSongTransposeOptions { get; } =
        MusicConstants.TransposeNames.ToList();

    public static List<MusicConstants.SpeedOption> NewSongSpeedOptions { get; } =
        MusicConstants.GenerateSpeedOptions();

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly UpdateService _updateService;
    private AccentColorOption _selectedAccentColor = null!;
    private bool _isApplyingKeypressMode;
    private MouseStopClickOption _selectedMouseStopClickOption = null!;
    private KeypressInputModeOption _selectedKeypressInputMode = null!;
    private ThemeOption _selectedTheme = null!;
    private UpdateCheckFrequencyOption _selectedUpdateCheckFrequency = null!;

    private MusicConstants.KeyOption _selectedNewSongBaseKeyOption = null!;
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
        _updateService = ioc.Get<UpdateService>();

        // Initialize theme from settings
        _selectedTheme = Settings.AppTheme switch
        {
            0 => ThemeOptions[0], // Light
            1 => ThemeOptions[1], // Dark
            _ => ThemeOptions[2]  // System
        };
        var startupTheme = _selectedTheme.Value == WpfUiApplicationTheme.Unknown
            ? (WpfUiApplicationTheme)SystemThemeService.GetSystemTheme()
            : _selectedTheme.Value;
        SystemThemeService.ApplySystemThemeNow((ApplicationTheme)startupTheme);

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
        if (Settings.CrashLogVerbosity != CrashLogVerbosity)
            Settings.Modify(s => s.CrashLogVerbosity = CrashLogVerbosity);

        ConfigureMidiFolderWatcher();

        // Initialize update frequency from settings
        var freqValue = Settings.UpdateCheckFrequency;
        _selectedUpdateCheckFrequency = UpdateCheckFrequencyOptions.FirstOrDefault(o => o.Value == freqValue)
            ?? UpdateCheckFrequencyOptions.Last();

        _updateService.UpdateAvailable += (s, newVersion) =>
        {
            LatestVersion = newVersion;
            UpdateString = "(Update available!)";
            NotifyOfPropertyChange(() => NeedsUpdate);
        };

        _updateService.BackgroundDownloadCompleted += (s, args) =>
        {
            var (success, version, errorMsg) = args;
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ShowAutoDownloadSnackbar(success, version, errorMsg));
            }
            else
            {
                ShowAutoDownloadSnackbar(success, version, errorMsg);
            }
        };

        _updateService.ConfigureUpdateCheckTimer();
    }

    /// <summary>Observable collection of game location entries for the settings UI</summary>
    public BindableCollection<GameInfo> GameLocations { get; }

    public bool IsGameLocationsExpanded { get; set; }
    public void ToggleGameLocationsExpanded() => IsGameLocationsExpanded = !IsGameLocationsExpanded;

    public AccentColorOption SelectedAccentColor
    {
        get => _selectedAccentColor;
        set
        {
            if (SetAndNotify(ref _selectedAccentColor, value) && value is not null)
            {
                Settings.AccentColor = value.ColorHex;
                Settings.Save();
                SystemThemeService.ApplyAccentColorNow(value.ColorHex);
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
        => SystemThemeService.ApplyAccentColorNow(hexColor, scheduleDeferredRefresh);

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
                var toApply = value.Value;
                if (toApply == WpfUiApplicationTheme.Unknown)
                {
                    toApply = (WpfUiApplicationTheme)SystemThemeService.GetSystemTheme();
                }

                SystemThemeService.ApplySystemThemeNow((ApplicationTheme)toApply);

                Settings.Modify(s => s.AppTheme = value.Value switch
                {
                    WpfUiApplicationTheme.Light => 0,
                    WpfUiApplicationTheme.Dark => 1,
                    _ => -1
                });

                // Reapply accent color after theme change without forcing a second immediate theme apply.
                ApplyAccentColor(_selectedAccentColor.ColorHex, scheduleDeferredRefresh: false);
            }
        }
    }



    public bool AutoCheckUpdates
    {
        get => Settings.AutoCheckUpdates;
        set
        {
            if (Settings.AutoCheckUpdates == value) return;
            Settings.Modify(s => s.AutoCheckUpdates = value);
            NotifyOfPropertyChange();
            _updateService.ConfigureUpdateCheckTimer();
        }
    }

    public bool AutoDownloadUpdates
    {
        get => Settings.AutoDownloadUpdates;
        set
        {
            if (Settings.AutoDownloadUpdates == value) return;
            Settings.Modify(s => s.AutoDownloadUpdates = value);
            NotifyOfPropertyChange();
        }
    }

    public UpdateCheckFrequencyOption SelectedUpdateCheckFrequency
    {
        get => _selectedUpdateCheckFrequency;
        set
        {
            if (SetAndNotify(ref _selectedUpdateCheckFrequency, value))
            {
                Settings.Modify(s => s.UpdateCheckFrequency = value.Value);
                _updateService.ConfigureUpdateCheckTimer();
            }
        }
    }

    public bool DebugModeEnabled { get; set; } = Settings.DebugModeEnabled;

    public bool PageCaching { get; set; } = Settings.PageCaching;

    public bool LogPlayedNotes { get; set; } = Settings.LogPlayedNotes;

    public int CrashLogVerbosity { get; set; } =
        Math.Clamp(Settings.CrashLogVerbosity, Logger.ErrorsOnlyVerbosity, Logger.AllStepsVerbosity);

    public string CrashLogVerbosityDescription => GetCrashLogVerbosityDescription(CrashLogVerbosity);

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

    #region AutoCorrectThreshold

    /// <summary>
    /// Sorted distinct key counts across all registered instruments.
    /// Drives the slider tick positions via CustomTicks binding.
    /// </summary>
    public static int[] AutoCorrectThresholdTicks { get; } = new[] { 0 }.Concat(Core.Keyboard.GetDistinctInstrumentKeyCounts()).ToArray();

    /// <summary>
    /// Pipe-delimited labels for the slider thumb tooltip (one per tick).
    /// </summary>
    public string AutoCorrectThresholdToolTipOptions { get; } =
        string.Join("|", AutoCorrectThresholdTicks.Select(k => k == 0 ? "Off" : $"{k} keys"));

    private int _autoCorrectThresholdValue = AutoCorrectThresholdTicks
        .OrderBy(t => Math.Abs(t - Settings.AutoCorrectThreshold))
        .FirstOrDefault();

    /// <summary>
    /// Current slider value — the actual key count (not an index).
    /// Bound directly to the Slider.Value property.
    /// </summary>
    public int AutoCorrectThresholdValue
    {
        get => _autoCorrectThresholdValue;
        set
        {
            // Snap to nearest valid tick (the slider control already snaps,
            // but guard against programmatic sets with arbitrary values)
            var snapped = AutoCorrectThresholdTicks
                .OrderBy(t => Math.Abs(t - value))
                .FirstOrDefault();

            if (SetAndNotify(ref _autoCorrectThresholdValue, snapped))
            {
                NotifyOfPropertyChange(nameof(AutoCorrectThresholdDescription));
            }
        }
    }

    /// <summary>
    /// Human-readable description shown below the slider.
    /// </summary>
    public string AutoCorrectThresholdDescription => AutoCorrectThresholdValue == 0
        ? "Auto-correct is disabled."
        : $"Auto-correct pitch for instruments with ≤ {AutoCorrectThresholdValue} keys when Smart Transpose is active.";

    #endregion

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

    public bool AutoDetectBaseKey
    {
        get => Settings.AutoDetectBaseKey;
        set
        {
            if (Settings.AutoDetectBaseKey == value)
                return;

            Settings.Modify(s => s.AutoDetectBaseKey = value);
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

    public MusicConstants.KeyOption SelectedNewSongBaseKeyOption
    {
        get => _selectedNewSongBaseKeyOption;
        set
        {
            if (!SetAndNotify(ref _selectedNewSongBaseKeyOption, value) || value is null || _isSynchronizingNewSongDefaults)
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

    public void ScrollToVersionSection()
    {
        if (View is SettingsPageView view)
        {
            view.ScrollToVersionSection();
        }
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
        var baseKey = Math.Clamp(Settings.DefaultSongBaseKey, MusicConstants.MinKeyOffset, MusicConstants.MaxKeyOffset);
        var baseKeyOption = NewSongBaseKeyOptions.FirstOrDefault(option => option.Value == baseKey)
            ?? NewSongBaseKeyOptions.First();

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
            _selectedNewSongBaseKeyOption = baseKeyOption;
            NotifyOfPropertyChange(nameof(SelectedNewSongBaseKeyOption));

            SyncNewSongKeyOptions(baseKeyOption.Value, Settings.DefaultSongKey, saveSettings: false);

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
        if (Settings.DefaultSongBaseKey != baseKey
            || Settings.DefaultSongKey != _selectedNewSongKeyOption.Value
            || Settings.DefaultSongTranspose != (int)_selectedNewSongTransposeOption.Key
            || Math.Abs(Settings.DefaultSongSpeed - _selectedNewSongSpeedOption.Value) > 0.001
            || (int)Settings.DefaultSongMergeMilliseconds != sanitizedMergeMs)
        {
            Settings.Modify(s =>
            {
                s.DefaultSongBaseKey = baseKey;
                s.DefaultSongKey = _selectedNewSongKeyOption.Value;
                s.DefaultSongTranspose = (int)_selectedNewSongTransposeOption.Key;
                s.DefaultSongSpeed = _selectedNewSongSpeedOption.Value;
                s.DefaultSongMergeMilliseconds = (uint)sanitizedMergeMs;
            });

            DefaultSongMergeMilliseconds = sanitizedMergeMs;
        }
    }

    private void SyncNewSongKeyOptions(int baseKey, int preferredKey, bool saveSettings)
    {
        var keyOptions = MusicConstants.GenerateKeyOptions(baseKey);
        var clampedKey = Math.Clamp(
            preferredKey,
            MusicConstants.GetRelativeMinKeyOffset(baseKey),
            MusicConstants.GetRelativeMaxKeyOffset(baseKey));

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
            s.DefaultSongBaseKey = baseKey;
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

    private static string GetCrashLogVerbosityDescription(int verbosity)
    {
        if (!Settings.Default.DebugModeEnabled)
            return "Debug mode is off, so diagnostics are forced to All Steps.";

        return verbosity switch
        {
            <= Logger.ErrorsOnlyVerbosity => "Logs exceptions and explicit errors only.",
            >= Logger.AllStepsVerbosity => "Logs all diagnostic steps, warnings, and errors.",
            _ => "Logs warnings and errors."
        };
    }

    private QueueViewModel Queue => _main.QueueView;

    public void ToggleDebugMode()
    {
        DebugModeEnabled = !DebugModeEnabled;
    }

    public void ShowDebugMessageBoxSample()
    {
        CrashMessageBox.Show(
            new InvalidOperationException("This is a sample themed message box used for debug UI validation."),
            Logger.GetPrimaryLogPath());

        Logger.LogStep("DEBUG_MESSAGEBOX_SAMPLE", "shown");
    }

    public async Task ShowDebugDialogSample()
    {
        var result = await DialogHelper.ShowActionDialogAsync(new DialogActionRequest
        {
            Title = "Debug Dialog Sample",
            Icon = SymbolRegular.Info24,
            Body = "This is a sample dialog rendered through DialogHelper.",
            ConfirmButton = new DialogActionButton
            {
                Text = "Ok",
                Appearance = ControlAppearance.Primary
            },
            CancelButton = new DialogActionButton
            {
                Text = "Close",
                Appearance = ControlAppearance.Secondary
            }
        });

        Logger.LogStep("DEBUG_DIALOG_SAMPLE", $"result={result}");
    }

    public async Task ShowDebugSnackbarSample()
    {
        var duration = TimeSpan.FromSeconds(15);
        SnackbarService.Info("Info Toast", "This is an info toast", duration: duration);
        await Task.Delay(100);
        SnackbarService.Success("Success Toast", "This is a success toast", duration: duration);
        await Task.Delay(100);
        SnackbarService.Warning("Warning Toast", "This is a warning toast", duration: duration);
        await Task.Delay(100);
        SnackbarService.Danger("Danger Toast", "This is a danger toast", duration: duration);
    }

    public async Task CheckForUpdate()
    {
        if (IsCheckingUpdate)
            return;

        UpdateString = "Checking for updates...";
        IsCheckingUpdate = true;

        try
        {
            LatestVersion = await _updateService.CheckForUpdatesAsync(IncludeBetaUpdates, ProgramVersion) ?? new GitVersion();
            if (LatestVersion.Version > ProgramVersion)
            {
                UpdateString = "(Update available!)";
                if (AutoDownloadUpdates)
                {
                    _ = _updateService.BackgroundDownloadUpdateAsyncWrapper(LatestVersion);
                }
            }
            else
            {
                UpdateString = string.Empty;
            }
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

    private void ShowAutoDownloadSnackbar(bool success, string version, string errorMsg)
    {
        if (success)
        {
            SnackbarService.Success(
                "Update downloaded",
                $"Version {version} is ready to install.");
        }
        else
        {
            SnackbarService.Danger(
                "Auto-download failed",
                string.IsNullOrEmpty(errorMsg) ? "Failed to auto-download update." : errorMsg);
        }
    }

    public async Task ShowUpdateDialog()
    {
        if (LatestVersion == null || ProgramVersion >= LatestVersion.Version)
            return;

        await UpdateDialog.ShowAsync(LatestVersion, _updateService);
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
            await IncorrectGameLocationDialog.ShowLauncherWarningAsync();
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
        Logger.LogStep("MIDI_SCAN_BEGIN", $"folder='{folderPath}'");

        var trackCountBefore = _main.SongsView.Tracks.Count;

        IsScanningMidiFolder = true;
        try
        {
            await _main.FileService.ScanFolder(folderPath);
        }
        finally
        {
            IsScanningMidiFolder = false;
            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            var newSongCount = _main.SongsView.Tracks.Count - trackCountBefore;
            Logger.LogStep("MIDI_SCAN_END", $"folder='{folderPath}' | newSongs={newSongCount} | elapsedMs={elapsedMs:F0}");

            if (newSongCount > 0)
            {
                var label = newSongCount == 1 ? "song" : "songs";
                SnackbarService.Success("New songs found", $"{newSongCount} new {label} added to library");
            }
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

    public void ResetSongDefaults()
    {
        var defaultBaseKeyOption = NewSongBaseKeyOptions.FirstOrDefault(option => option.Value == 0)
            ?? NewSongBaseKeyOptions.First();

        var defaultTransposeOption = NewSongTransposeOptions.FirstOrDefault(option => option.Key == Ignore);
        if (defaultTransposeOption.Equals(default(KeyValuePair<Transpose, string>)))
            defaultTransposeOption = NewSongTransposeOptions.First();

        var defaultSpeedOption = NewSongSpeedOptions
            .OrderBy(option => Math.Abs(option.Value - 1.0))
            .First();

        _isSynchronizingNewSongDefaults = true;
        try
        {
            _selectedNewSongBaseKeyOption = defaultBaseKeyOption;
            NotifyOfPropertyChange(nameof(SelectedNewSongBaseKeyOption));

            SyncNewSongKeyOptions(defaultBaseKeyOption.Value, defaultBaseKeyOption.Value, saveSettings: false);

            _selectedNewSongTransposeOption = defaultTransposeOption;
            NotifyOfPropertyChange(nameof(SelectedNewSongTransposeOption));

            _selectedNewSongSpeedOption = defaultSpeedOption;
            NotifyOfPropertyChange(nameof(SelectedNewSongSpeedOption));
        }
        finally
        {
            _isSynchronizingNewSongDefaults = false;
        }

        AutoDetectBaseKey = true;
        DefaultSongArtist = string.Empty;
        DefaultSongAlbum = string.Empty;
        DefaultSongCustomBpm = 0;
        DefaultSongHoldNotes = false;
        DefaultSongMergeNotes = false;
        DefaultSongMergeMilliseconds = 100;

        Settings.Modify(s =>
        {
            s.AutoDetectBaseKey = true;
            s.DefaultSongArtist = string.Empty;
            s.DefaultSongAlbum = string.Empty;
            s.DefaultSongCustomBpm = 0;
            s.DefaultSongBaseKey = defaultBaseKeyOption.Value;
            s.DefaultSongKey = defaultBaseKeyOption.Value;
            s.DefaultSongTranspose = (int)defaultTransposeOption.Key;
            s.DefaultSongSpeed = defaultSpeedOption.Value;
            s.DefaultSongHoldNotes = false;
            s.DefaultSongMergeNotes = false;
            s.DefaultSongMergeMilliseconds = 100;
        });
    }

    public async Task ResetAppData()
    {
        var shouldReset = await ResetAppDataConfirmationDialog.ConfirmAsync();
        if (!shouldReset)
            return;

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            await UnableToResetAppDataDialog.ShowErrorAsync("Could not resolve the current executable path for restart.");
            return;
        }

        var currentProcessId = Environment.ProcessId;
        var escapedAppDataPath = AppPaths.AppDataDirectory.Replace("'", "''");
        var escapedExecutablePath = executablePath.Replace("'", "''");
        var escapedStatusFilePath = AppPaths.AppStatusFilePath.Replace("'", "''");

        var resetStatusString = $"[{DateTime.Now:HH:mm:ss}] RESET";

        var resetCommand = $"Start-Sleep -Milliseconds 400; " +
                           $"Wait-Process -Id {currentProcessId}; " +
                           $"Remove-Item -LiteralPath '{escapedAppDataPath}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                           $"New-Item -ItemType Directory -Path '{escapedAppDataPath}' -Force | Out-Null; " +
                           $"Set-Content -Path '{escapedStatusFilePath}' -Value '{resetStatusString}' -Force; " +
                           $"Start-Process -FilePath '{escapedExecutablePath}'";

        var arguments = $"-NoProfile -WindowStyle Hidden -Command \"{resetCommand}\"";

        try
        {
            StartResetHelperProcess("pwsh.exe", arguments);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            try
            {
                StartResetHelperProcess("powershell.exe", arguments);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                await UnableToResetAppDataDialog.ShowErrorAsync("Could not start PowerShell to complete reset and restart.");
                return;
            }
        }

        Application.Current.Shutdown();
    }

    /// <summary>
    /// Starts a detached helper process used to clear app data after shutdown and restart the app.
    /// </summary>
    private static void StartResetHelperProcess(string shellPath, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(arguments);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = shellPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start reset helper process.");
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
        Logger.LogStep("MIDI_AUTO_SCAN_TOGGLE", $"enabled={AutoScanMidiFolder}");
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
            Logger.Log($"Failed to configure MIDI folder watcher for '{MidiFolder}'.");
            Logger.LogException(error);
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
                Logger.Log("MIDI folder auto-scan failed.");
                Logger.LogException(error);
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
            SystemThemeService.ApplyAccentColorNow(_selectedAccentColor.ColorHex, scheduleDeferredRefresh: false);
    }

    [UsedImplicitly]
    public void SetTimeToNow() => DateTime = DateTime.Now;

    protected override void OnActivate()
    {
        Logger.LogPageVisit("Settings", source: "screen-activate");
    }

    protected override void OnDeactivate()
    {
        base.OnDeactivate();
    }



    [UsedImplicitly]
    private void OnAutoCheckUpdatesChanged()
    {
        Settings.Modify(s => s.AutoCheckUpdates = AutoCheckUpdates);
    }

    [UsedImplicitly]
    private void OnDebugModeEnabledChanged()
    {
        Settings.Modify(s => s.DebugModeEnabled = DebugModeEnabled);
        NotifyOfPropertyChange(nameof(CrashLogVerbosityDescription));
    }

    [UsedImplicitly]
    private void OnPageCachingChanged()
    {
        Settings.Modify(s => s.PageCaching = PageCaching);
    }

    [UsedImplicitly]
    private void OnLogPlayedNotesChanged()
    {
        Settings.Modify(s => s.LogPlayedNotes = LogPlayedNotes);
    }

    [UsedImplicitly]
    private void OnCrashLogVerbosityChanged()
    {
        var clampedVerbosity = Math.Clamp(
            CrashLogVerbosity,
            Logger.ErrorsOnlyVerbosity,
            Logger.AllStepsVerbosity);

        if (clampedVerbosity != CrashLogVerbosity)
        {
            CrashLogVerbosity = clampedVerbosity;
            return;
        }

        Settings.Modify(s => s.CrashLogVerbosity = clampedVerbosity);
        NotifyOfPropertyChange(nameof(CrashLogVerbosityDescription));
    }

    [UsedImplicitly]
    private void OnIncludeBetaUpdatesChanged() { }

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

    [UsedImplicitly]
    private void OnAutoCorrectThresholdValueChanged()
    {
        Settings.AutoCorrectThreshold = AutoCorrectThresholdValue;
        Settings.Save();
        _events.Publish(this);
    }

    private static KeypressInputModeOption ResolveKeypressInputMode(bool useDirectInput, bool useWindowMessage)
    {
        if (useWindowMessage)
            return GetKeypressInputModeOption(KeypressInputMode.WindowMessage);

        if (useDirectInput)
            return GetKeypressInputModeOption(KeypressInputMode.DirectInput);

        return GetKeypressInputModeOption(KeypressInputMode.DirectInput);
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
    DirectInput = 0,
    WindowMessage = 1
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

public class UpdateCheckFrequencyOption(string name, int value)
{
    public string Name { get; } = name;
    public int Value { get; } = value;

    public override string ToString() => Name;
}
