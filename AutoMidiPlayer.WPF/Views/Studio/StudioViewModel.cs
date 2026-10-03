using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.WPF.Controls.NoSongPlaceholder;
using AutoMidiPlayer.WPF.Controls.Snackbar;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Services;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Stylet;
using StyletIoC;
using IContainer = StyletIoC.IContainer;

namespace AutoMidiPlayer.WPF.ViewModels;

public class StudioViewModel : Screen, IHandle<OpenedFileChangedNotification>, IHandle<MidiFile>
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

        KeyOptions = MusicConstants.GenerateKeyOptions();
        _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == 0);
    }

    #region Properties - Components & Delegates

    public NoSongPlaceholderComponent Placeholder { get; }
    public MainWindowViewModel Main => _main;
    public PlaybackControlsService Controls => _main.PlaybackControls;
    public SongService SongSettings => _main.SongSettings;
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
        set
        {
            if (SetAndNotify(ref _selectedKeyOption, value))
            {
                if (SelectedMarker != null && value != null && SelectedMarker.KeyOffset != value.Value)
                {
                    SelectedMarker.KeyOffset = value.Value;
                    var noteName = MusicConstants.GetNoteName(value.Value);
                    SelectedMarker.Label = $"{noteName} ({(value.Value >= 0 ? "+" : "")}{value.Value})";
                    _ = SaveMarkersToSongAsync();
                }
            }
        }
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
        if (!string.Equals(_lastActiveKeyDisplay, display, StringComparison.Ordinal))
        {
            _lastActiveKeyDisplay = display;
            NotifyOfPropertyChange(nameof(ActiveKeyAtPlayheadDisplay));
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

    public string ActiveKeyAtPlayheadDisplay
    {
        get
        {
            if (CurrentFile?.Song is null)
                return "Key: C (+0)";

            var song = CurrentFile.Song;
            var marker = song.GetKeyMarkerAtTime(CurrentTime);
            var offset = marker?.KeyOffset ?? song.Key;
            var noteName = MusicConstants.GetNoteName(offset);
            var isModulated = marker != null;

            return isModulated
                ? $"Active: {noteName} ({(offset >= 0 ? "+" : "")}{offset}) [Modulated at {marker!.TimeString}]"
                : $"Active: {noteName} ({(offset >= 0 ? "+" : "")}{offset}) [Song Default]";
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
        UpdateActiveKeyDisplay();
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
        Controls.SongPosition = time.TotalSeconds;
        Controls.OnSongPositionChanged();
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
            SnackbarService.Info("Auto-Detect", "No Key Signature events found in this MIDI file.");
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
        SnackbarService.Success("Auto-Detect Complete", $"Detected and added {detected.Count} key changes.");
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
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to persist key change markers.");
        }
    }

    #endregion
}
