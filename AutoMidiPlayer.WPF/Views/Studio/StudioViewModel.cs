using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.WPF.Controls.NoSongPlaceholder;
using AutoMidiPlayer.WPF.Controls.PianoRoll;
using AutoMidiPlayer.WPF.Controls.Snackbar;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Services;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Stylet;
using StyletIoC;
using IContainer = StyletIoC.IContainer;

namespace AutoMidiPlayer.WPF.ViewModels;

public class StudioViewModel : Screen,
    IHandle<OpenedFileChangedNotification>,
    IHandle<MidiFile>,
    IHandle<InstrumentViewModel>,
    IHandle<SettingsPageViewModel>
{
    private readonly IContainer _ioc;
    private readonly MainWindowViewModel _main;
    private readonly IEventAggregator _events;
    private readonly PlaybackCurrentTimeWatcher _timeWatcher;

    private bool _isSidebarOpen = true;
    private string _activeSidebarTab = "Tracks"; // "Tracks" or "Settings"
    private KeyChangeMarker? _selectedMarker;
    private MusicConstants.KeyOption? _selectedKeyOption;
    private TimeSpan _currentTime = TimeSpan.Zero;
    private double _zoomLevel = 1.0;
    private double _keyHeight = 16.0;
    private bool _isRenderingHooked;
    private string _lastActiveKeyDisplay = string.Empty;
    private string _lastActiveDisplayModeText = string.Empty;
    private KeyChangeMarker? _lastActiveMarker;
    private int _lastActiveKey = int.MinValue;

    public StudioViewModel(IContainer ioc, MainWindowViewModel main, NoSongPlaceholderComponent placeholder)
    {
        _ioc = ioc;
        _main = main;
        Placeholder = placeholder;
        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);
        _timeWatcher = PlaybackCurrentTimeWatcher.Instance;

        SongSettings.SettingsRebuildRequired += HandleSongSettingsRebuildRequired;
        SongSettings.PropertyChanged += HandleSongSettingsPropertyChanged;
        _main.ActiveGamesChanged += HandleActiveGamesChanged;

        KeyOptions = MusicConstants.GenerateKeyOptions();
        _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == 0);
        _selectedDisplayMode = DisplayModes[0];
    }

    #region Properties - Components & Delegates

    public NoSongPlaceholderComponent Placeholder { get; }
    public MainWindowViewModel Main => _main;
    public PlaybackControlsService Controls => _main.PlaybackControls;
    public SongService SongSettings => _main.SongSettings;
    public string CurrentInstrumentId => _main.InstrumentView?.SelectedInstrument.Key ?? string.Empty;
    public int PitchRevision { get; private set; }

    public void NotifyPitchUpdateRequired()
    {
        if (Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            Application.Current.Dispatcher.BeginInvoke(NotifyPitchUpdateRequired);
            return;
        }

        _lastActiveMarker = null;
        _lastActiveKey = int.MinValue;
        _lastActiveKeyDisplay = string.Empty;
        _lastActiveDisplayModeText = string.Empty;
        UpdateActiveKeyDisplay();
        NotifyOfPropertyChange(nameof(CurrentInstrumentId));
        PitchRevision++;
        NotifyOfPropertyChange(nameof(PitchRevision));
    }

    public event EventHandler? SongDataReloaded;

    public BindableCollection<MidiTrack> MidiTracks => _main.TrackView.MidiTracks;

    public MidiFile? CurrentFile => _main.QueueView.OpenedFile;
    public bool HasSongOpen => CurrentFile is not null;

    public BindableCollection<KeyChangeMarker> KeyChangeMarkers { get; } = new();

    public KeyChangeMarker? SelectedMarker
    {
        get => _selectedMarker;
        set
        {
            if (SetAndNotify(ref _selectedMarker, value))
            {
                NotifyOfPropertyChange(nameof(HasSelectedMarker));
                if (value != null)
                {
                    _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == value.KeyOffset);
                    NotifyOfPropertyChange(nameof(SelectedKeyOption));
                }
            }
        }
    }

    public bool HasSelectedMarker => SelectedMarker != null;

    public List<MusicConstants.KeyOption> KeyOptions { get; }

    public MusicConstants.KeyOption? SelectedKeyOption
    {
        get => _selectedKeyOption;
        set => SetAndNotify(ref _selectedKeyOption, value);
    }

    public TimeSpan CurrentTime
    {
        get => _currentTime;
        set
        {
            if (SetAndNotify(ref _currentTime, value))
            {
                UpdateActiveKeyDisplayIfNeeded();
            }
        }
    }

    private void UpdateActiveKeyDisplay()
    {
        var display = ActiveKeyAtPlayheadDisplay;
        var modeText = ActiveDisplayModeText;
        if (!string.Equals(_lastActiveKeyDisplay, display, StringComparison.Ordinal) ||
            !string.Equals(_lastActiveDisplayModeText, modeText, StringComparison.Ordinal))
        {
            _lastActiveKeyDisplay = display;
            _lastActiveDisplayModeText = modeText;
            NotifyOfPropertyChange(nameof(ActiveKeyAtPlayheadDisplay));
            NotifyOfPropertyChange(nameof(ActiveDisplayModeText));
            NotifyOfPropertyChange(nameof(ActiveDisplayModeTooltip));
        }
    }

    private void UpdateActiveKeyDisplayIfNeeded()
    {
        if (KeyChangeMarkers.Count == 0)
        {
            // If there are no key change markers, the active key is static throughout playback
            return;
        }

        var song = CurrentFile?.Song;
        if (song is null) return;

        var marker = song.GetKeyMarkerAtTime(CurrentTime);
        var offset = marker?.KeyOffset ?? song.Key;

        if (marker != _lastActiveMarker || offset != _lastActiveKey)
        {
            _lastActiveMarker = marker;
            _lastActiveKey = offset;
            UpdateActiveKeyDisplay();
        }
    }

    public TimeSpan Duration => Controls.MaximumTime;

    public double ZoomLevel
    {
        get => _zoomLevel;
        set => SetAndNotify(ref _zoomLevel, Math.Clamp(value, 0.2, 5.0));
    }

    public double KeyHeight
    {
        get => _keyHeight;
        set => SetAndNotify(ref _keyHeight, Math.Clamp(value, 6.0, 50.0));
    }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        set
        {
            if (SetAndNotify(ref _isSidebarOpen, value))
            {
                NotifyOfPropertyChange(nameof(IsTracksDrawerActive));
                NotifyOfPropertyChange(nameof(IsSettingsDrawerActive));
                if (!IsTracksDrawerActive) StopTrackGlows();
            }
        }
    }

    public string ActiveSidebarTab
    {
        get => _activeSidebarTab;
        set
        {
            if (SetAndNotify(ref _activeSidebarTab, value))
            {
                NotifyOfPropertyChange(nameof(IsTracksTabActive));
                NotifyOfPropertyChange(nameof(IsSettingsTabActive));
                NotifyOfPropertyChange(nameof(IsTracksDrawerActive));
                NotifyOfPropertyChange(nameof(IsSettingsDrawerActive));
                NotifyOfPropertyChange(nameof(ActiveDrawerTitle));
                if (!IsTracksDrawerActive) StopTrackGlows();
            }
        }
    }

    public bool IsTracksTabActive => ActiveSidebarTab == "Tracks";
    public bool IsSettingsTabActive => ActiveSidebarTab == "Settings";

    public bool IsTracksDrawerActive => IsSidebarOpen && ActiveSidebarTab == "Tracks";
    public bool IsSettingsDrawerActive => IsSidebarOpen && ActiveSidebarTab == "Settings";
    public string ActiveDrawerTitle => ActiveSidebarTab == "Tracks" ? "Tracks" : "Song Settings";

    public List<NoteDisplayModeOption> DisplayModes { get; } =
    [
        new()
        {
            Mode = PianoRollDisplayMode.Auto,
            Name = "Auto",
            Description = "Display according to markers & transposes"
        },
        new()
        {
            Mode = PianoRollDisplayMode.Transpose,
            Name = "Transpose",
            Description = "Display transposed equivalents (Up, Smart, Down, Ignore)"
        },
        new()
        {
            Mode = PianoRollDisplayMode.SongDefault,
            Name = "Song Default",
            Description = "Original sequence (unmodified MIDI)"
        }
    ];

    private NoteDisplayModeOption _selectedDisplayMode = null!;
    public NoteDisplayModeOption SelectedDisplayMode
    {
        get => _selectedDisplayMode;
        set
        {
            if (SetAndNotify(ref _selectedDisplayMode, value))
            {
                _lastActiveDisplayModeText = ActiveDisplayModeText;
                NotifyOfPropertyChange(nameof(ActiveDisplayModeText));
                NotifyOfPropertyChange(nameof(ActiveDisplayModeTooltip));
                NotifyPitchUpdateRequired();
            }
        }
    }

    private static string FormatTimestampWithMs(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return (int)time.TotalHours > 0
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}:{time.Milliseconds:D3}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}:{time.Milliseconds:D3}";
    }

    public string ActiveDisplayModeText
    {
        get
        {
            if (CurrentFile?.Song is null)
                return $"Active: Auto ({MusicConstants.GetNoteName(0)} +0) from Song Default";

            var song = CurrentFile.Song;
            var marker = song.GetKeyMarkerAtTime(CurrentTime);

            var hasTranspose = SongSettings.IsTransposeActive
                || SongSettings.KeyOffset != 0
                || (song.Transpose != null && song.Transpose != Transpose.Ignore)
                || song.Key != 0
                || SongSettings.IsAutoCorrectActive;

            var songOffset = hasTranspose ? SongSettings.EffectiveKeyOffset : 0;
            var songNoteName = MusicConstants.GetNoteName(songOffset);
            var defaultNoteName = MusicConstants.GetNoteName(0);
            var fallbackSource = hasTranspose ? "Transpose Mode" : "Song Default";

            return SelectedDisplayMode?.Mode switch
            {
                PianoRollDisplayMode.SongDefault =>
                    $"Active: Song Default ({defaultNoteName} +0) from Song Default",

                PianoRollDisplayMode.Transpose =>
                    $"Active: Transpose ({songNoteName} {(songOffset >= 0 ? "+" : "")}{songOffset}) from {fallbackSource}",

                _ => marker != null
                    ? $"Active: Auto ({MusicConstants.GetNoteName(marker.KeyOffset)} {(marker.KeyOffset >= 0 ? "+" : "")}{marker.KeyOffset}) from Marker at {FormatTimestampWithMs(marker.Time)}"
                    : $"Active: Auto ({songNoteName} {(songOffset >= 0 ? "+" : "")}{songOffset}) from {fallbackSource}"
            };
        }
    }

    public string ActiveDisplayModeTooltip
    {
        get
        {
            if (CurrentFile?.Song is null)
                return "Note Display Mode";

            var song = CurrentFile.Song;
            var marker = song.GetKeyMarkerAtTime(CurrentTime);

            var hasTranspose = SongSettings.IsTransposeActive
                || SongSettings.KeyOffset != 0
                || (song.Transpose != null && song.Transpose != Transpose.Ignore)
                || song.Key != 0
                || SongSettings.IsAutoCorrectActive;

            var songOffset = hasTranspose ? SongSettings.EffectiveKeyOffset : 0;
            var songNoteName = MusicConstants.GetNoteName(songOffset);
            var fallbackSource = hasTranspose ? "Transpose Mode" : "Song Default";

            var offset = marker?.KeyOffset ?? songOffset;
            var noteName = MusicConstants.GetNoteName(offset);
            var isModulated = marker != null;

            return SelectedDisplayMode?.Mode switch
            {
                PianoRollDisplayMode.SongDefault =>
                    "Song Default: Displaying original sequence exactly as authored in the MIDI file.",
                PianoRollDisplayMode.Transpose =>
                    $"Transpose: Displaying notes transposed by song key offset ({songNoteName} {(songOffset >= 0 ? "+" : "")}{songOffset}) and {SongSettings.TransposeMode} mode.",
                _ =>
                    $"Auto: Displaying notes dynamically according to timeline markers and transpositions.\nActive at playhead: {noteName} ({(offset >= 0 ? "+" : "")}{offset}) [{(isModulated ? $"Marker at {FormatTimestampWithMs(marker!.Time)}" : fallbackSource)}]"
            };
        }
    }

    public string ActiveKeyAtPlayheadDisplay
    {
        get
        {
            if (CurrentFile?.Song is null)
                return "Key: C (+0)";

            var song = CurrentFile.Song;
            var marker = song.GetKeyMarkerAtTime(CurrentTime);
            var hasTranspose = SongSettings.IsTransposeActive
                || SongSettings.KeyOffset != 0
                || (song.Transpose != null && song.Transpose != Transpose.Ignore)
                || song.Key != 0
                || SongSettings.IsAutoCorrectActive;

            var fallbackSource = hasTranspose ? "Transpose Mode" : "Song Default";
            var offset = marker?.KeyOffset ?? (hasTranspose ? SongSettings.EffectiveKeyOffset : 0);
            var noteName = MusicConstants.GetNoteName(offset);
            var isModulated = marker != null;

            return isModulated
                ? $"Active: {noteName} ({(offset >= 0 ? "+" : "")}{offset}) [Modulated at {marker!.TimeString}]"
                : $"Active: {noteName} ({(offset >= 0 ? "+" : "")}{offset}) [{fallbackSource}]";
        }
    }

    public bool HoldNotes
    {
        get => CurrentFile?.Song.HoldNotes ?? false;
        set
        {
            if (CurrentFile?.Song is not null && CurrentFile.Song.HoldNotes != value)
            {
                CurrentFile.Song.HoldNotes = value;
                NotifyOfPropertyChange(nameof(HoldNotes));
                _ = SaveSongPropertyAsync();
            }
        }
    }

    public bool AutoChord
    {
        get => CurrentFile?.Song.AutoChord ?? false;
        set
        {
            if (CurrentFile?.Song is not null && CurrentFile.Song.AutoChord != value)
            {
                CurrentFile.Song.AutoChord = value;
                NotifyOfPropertyChange(nameof(AutoChord));
                _ = SaveSongPropertyAsync();
            }
        }
    }

    private async Task SaveSongPropertyAsync()
    {
        if (CurrentFile?.Song is null) return;
        try
        {
            using var db = _ioc.Get<PlayerContext>();
            db.Songs.Update(CurrentFile.Song);
            await db.SaveChangesAsync();
            await _main.PlaybackEngine.RefreshCurrentSongRealtimeAsync();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to save song setting from Studio.");
        }
    }

    #endregion

    #region Lifecycle & Smooth Clock

    protected override void OnActivate()
    {
        base.OnActivate();
        Logger.LogPageVisit("Studio", source: "screen-activate");

        _timeWatcher.CurrentTimeChanged += HandleCurrentTimeTick;
        Controls.SongPositionChanged += HandleSongPositionChanged;
        Controls.PropertyChanged += HandleControlsPropertyChanged;
        _main.QueueView.PropertyChanged += HandleQueuePropertyChanged;
        _main.PlaybackEngine.NotePlayed += HandleNotePlayed;

        ReloadSongData();

        if (Controls.IsPlaying)
        {
            StartSmoothClock();
        }
    }

    protected override void OnDeactivate()
    {
        base.OnDeactivate();
        StopSmoothClock();
        _timeWatcher.CurrentTimeChanged -= HandleCurrentTimeTick;
        Controls.SongPositionChanged -= HandleSongPositionChanged;
        Controls.PropertyChanged -= HandleControlsPropertyChanged;
        _main.QueueView.PropertyChanged -= HandleQueuePropertyChanged;
        _main.PlaybackEngine.NotePlayed -= HandleNotePlayed;
        StopTrackGlows();
    }

    private void HandleNotePlayed(object? sender, NotePlayedEventArgs e)
    {
        if (!IsTracksDrawerActive) return;

        var matchingTracks = MidiTracks.Where(t => t.IsChecked && t.IsPlayingNoteAt(e.NoteNumber, e.CurrentTimeUs)).ToList();
        foreach (var track in matchingTracks)
        {
            track.TriggerGlow();
        }
    }

    private void StopTrackGlows()
    {
        foreach (var track in MidiTracks)
        {
            track.StopGlow();
        }
    }

    public void Handle(OpenedFileChangedNotification message)
    {
        ReloadSongData();
    }

    public void Handle(MidiFile message)
    {
        ReloadSongData();
    }

    public void Handle(InstrumentViewModel message)
    {
        NotifyPitchUpdateRequired();
    }

    public void Handle(SettingsPageViewModel message)
    {
        NotifyPitchUpdateRequired();
    }

    private void HandleSongSettingsRebuildRequired()
    {
        NotifyPitchUpdateRequired();
    }

    private void HandleSongSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SongService.KeyOffset)
            or nameof(SongService.EffectiveKeyOffset)
            or nameof(SongService.Transpose)
            or nameof(SongService.TransposeMode)
            or nameof(SongService.SelectedKeyOption))
        {
            NotifyPitchUpdateRequired();
        }
    }

    private void HandleActiveGamesChanged()
    {
        NotifyPitchUpdateRequired();
    }

    protected override void OnClose()
    {
        base.OnClose();
        SongSettings.SettingsRebuildRequired -= HandleSongSettingsRebuildRequired;
        SongSettings.PropertyChanged -= HandleSongSettingsPropertyChanged;
        _main.ActiveGamesChanged -= HandleActiveGamesChanged;
    }

    private void HandleControlsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaybackControlsService.IsPlaying))
        {
            if (Controls.IsPlaying) StartSmoothClock();
            else StopSmoothClock();
        }
    }

    private void HandleQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueueViewModel.OpenedFile))
        {
            ReloadSongData();
        }
    }

    private void StartSmoothClock()
    {
        if (_isRenderingHooked) return;
        _isRenderingHooked = true;
        CompositionTarget.Rendering += OnSmoothRenderTick;
    }

    private void StopSmoothClock()
    {
        if (!_isRenderingHooked) return;
        _isRenderingHooked = false;
        CompositionTarget.Rendering -= OnSmoothRenderTick;
    }

    private void OnSmoothRenderTick(object? sender, EventArgs e)
    {
        if (!Controls.IsPlaying)
        {
            StopSmoothClock();
            return;
        }

        var playback = _main.PlaybackEngine.Playback;
        if (playback is { IsRunning: true })
        {
            try
            {
                var metric = playback.GetCurrentTime<MetricTimeSpan>();
                CurrentTime = (TimeSpan)metric;
            }
            catch
            {
                // Playback might be resetting
            }
        }
    }

    private void HandleCurrentTimeTick(object? sender, PlaybackCurrentTimeChangedEventArgs e)
    {
        if (!_isRenderingHooked)
        {
            if (Controls.IsPlaying)
            {
                StartSmoothClock();
            }
            else
            {
                foreach (var playbackTime in e.Times)
                {
                    CurrentTime = (TimeSpan)(MetricTimeSpan)playbackTime.Time;
                    break;
                }
            }
        }
    }

    private void HandleSongPositionChanged(object? sender, EventArgs e)
    {
        CurrentTime = Controls.CurrentTime;
    }

    public void ReloadSongData()
    {
        if (Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            Application.Current.Dispatcher.BeginInvoke(ReloadSongData);
            return;
        }

        CurrentFile?.EnsureMidiLoaded();
        NotifyOfPropertyChange(nameof(CurrentFile));
        NotifyOfPropertyChange(nameof(HasSongOpen));
        NotifyOfPropertyChange(nameof(Duration));

        KeyChangeMarkers.Clear();
        if (CurrentFile?.Song is not null)
        {
            foreach (var m in CurrentFile.Song.KeyChangeMarkers.OrderBy(m => m.TimeMs))
            {
                KeyChangeMarkers.Add(m);
            }
        }
        SelectedMarker = null;
        _lastActiveKeyDisplay = string.Empty;
        _lastActiveDisplayModeText = string.Empty;
        UpdateActiveKeyDisplay();
        NotifyPitchUpdateRequired();
        SongDataReloaded?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Sidebar Controls

    public void ToggleTracksDrawer()
    {
        if (IsTracksDrawerActive)
        {
            IsSidebarOpen = false;
        }
        else
        {
            ActiveSidebarTab = "Tracks";
            IsSidebarOpen = true;
        }
    }

    public void ToggleSettingsDrawer()
    {
        if (IsSettingsDrawerActive)
        {
            IsSidebarOpen = false;
        }
        else
        {
            ActiveSidebarTab = "Settings";
            IsSidebarOpen = true;
        }
    }

    public void CloseSidebar()
    {
        IsSidebarOpen = false;
    }

    public void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    public void SelectTracksTab()
    {
        ActiveSidebarTab = "Tracks";
        IsSidebarOpen = true;
    }

    public void SelectSettingsTab()
    {
        ActiveSidebarTab = "Settings";
        IsSidebarOpen = true;
    }

    #endregion

    #region Scrubber & Marker Actions

    public void ScrubTo(TimeSpan time)
    {
        Controls.Seek(time);
        CurrentTime = time;
    }

    public async Task AddOffsetAtPlayheadAsync()
    {
        if (CurrentFile?.Song is null)
            return;

        var ms = (long)CurrentTime.TotalMilliseconds;

        // Snap to nearest note start within 150ms if available
        if (CurrentFile.Midi != null)
        {
            try
            {
                var tempoMap = CurrentFile.OriginalTempoMap ?? TempoMap.Default;
                var nearestNoteMs = CurrentFile.Midi.GetNotes()
                    .Select(n => (long)n.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds)
                    .Where(t => Math.Abs(t - ms) <= 150)
                    .OrderBy(t => Math.Abs(t - ms))
                    .Cast<long?>()
                    .FirstOrDefault();

                if (nearestNoteMs.HasValue)
                {
                    ms = nearestNoteMs.Value;
                }
            }
            catch
            {
                // Fall back to exact playhead ms if note inspection fails
            }
        }

        var offset = SelectedKeyOption?.Value ?? SongSettings.KeyOffset;
        var noteName = MusicConstants.GetNoteName(offset);

        // Remove any existing marker close to this time (within 300ms)
        var existing = KeyChangeMarkers.FirstOrDefault(m => Math.Abs(m.TimeMs - ms) < 300);
        if (existing != null)
        {
            KeyChangeMarkers.Remove(existing);
        }

        var newMarker = new KeyChangeMarker
        {
            TimeMs = ms,
            KeyOffset = offset,
            Label = $"{noteName} ({(offset >= 0 ? "+" : "")}{offset})"
        };

        KeyChangeMarkers.Add(newMarker);
        var sorted = KeyChangeMarkers.OrderBy(m => m.TimeMs).ToList();
        KeyChangeMarkers.Clear();
        foreach (var m in sorted) KeyChangeMarkers.Add(m);

        SelectedMarker = newMarker;
        await SaveMarkersToSongAsync();

        SnackbarService.Success("Key Offset Added", $"Set {newMarker.Label} at {newMarker.TimeString}");
    }

    public async Task AutoDetectKeyChangesAsync()
    {
        if (CurrentFile?.Midi is null)
            return;

        var tempoMap = CurrentFile.OriginalTempoMap ?? TempoMap.Default;
        var detected = FileService.DetectKeyChangesFromMidi(CurrentFile.Midi, tempoMap);

        if (detected.Count == 0)
        {
            SnackbarService.Info("Scan Keys", "No Key Signature events found in this MIDI file.");
            return;
        }

        // Merge detected markers
        foreach (var marker in detected)
        {
            var existing = KeyChangeMarkers.FirstOrDefault(m => Math.Abs(m.TimeMs - marker.TimeMs) < 500);
            if (existing != null)
                KeyChangeMarkers.Remove(existing);

            KeyChangeMarkers.Add(marker);
        }

        var sorted = KeyChangeMarkers.OrderBy(m => m.TimeMs).ToList();
        KeyChangeMarkers.Clear();
        foreach (var m in sorted) KeyChangeMarkers.Add(m);

        await SaveMarkersToSongAsync();
        SnackbarService.Success("Scan Keys Complete", $"Detected and added {detected.Count} key changes.");
    }

    public async Task DeleteSelectedMarkerAsync()
    {
        if (SelectedMarker is null || CurrentFile?.Song is null)
            return;

        var removed = SelectedMarker;
        KeyChangeMarkers.Remove(removed);
        SelectedMarker = null;

        await SaveMarkersToSongAsync();
        SnackbarService.Info("Marker Removed", $"Removed key offset marker at {removed.TimeString}");
    }

    public void NavigatePrevMarker()
    {
        if (KeyChangeMarkers.Count == 0) return;

        var currentMs = (long)CurrentTime.TotalMilliseconds;
        var prev = KeyChangeMarkers
            .Where(m => m.TimeMs < currentMs - 200)
            .OrderByDescending(m => m.TimeMs)
            .FirstOrDefault();

        if (prev != null)
        {
            SelectedMarker = prev;
            ScrubTo(TimeSpan.FromMilliseconds(prev.TimeMs));
        }
    }

    public void NavigateNextMarker()
    {
        if (KeyChangeMarkers.Count == 0) return;

        var currentMs = (long)CurrentTime.TotalMilliseconds;
        var next = KeyChangeMarkers
            .Where(m => m.TimeMs > currentMs + 200)
            .OrderBy(m => m.TimeMs)
            .FirstOrDefault();

        if (next != null)
        {
            SelectedMarker = next;
            ScrubTo(TimeSpan.FromMilliseconds(next.TimeMs));
        }
    }

    private async Task SaveMarkersToSongAsync()
    {
        if (CurrentFile?.Song is null) return;

        try
        {
            CurrentFile.Song.KeyChangeMarkers = KeyChangeMarkers.ToList();

            using var db = _ioc.Get<PlayerContext>();
            db.Songs.Update(CurrentFile.Song);
            await db.SaveChangesAsync();

            NotifyOfPropertyChange(nameof(ActiveKeyAtPlayheadDisplay));
            NotifyPitchUpdateRequired();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to persist key change markers.");
        }
    }

    #endregion
}

public class NoteDisplayModeOption
{
    public PianoRollDisplayMode Mode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
