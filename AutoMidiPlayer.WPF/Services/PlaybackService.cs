using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.Media;
using Windows.Media.Playback;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Core.Games;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.ViewModels;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Melanchall.DryWetMidi.Tools;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;
using WinMediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Service responsible for all MIDI playback operations.
/// Handles play/pause, navigation, time tracking, and note playing.
/// </summary>
public class PlaybackService : PropertyChangedBase, IHandle<MidiFile>, IHandle<MidiTrack>,
    IHandle<SettingsPageViewModel>, IHandle<InstrumentViewModel>,
    IHandle<MergeNotesNotification>, IHandle<PlayTimerNotification>
{
    #region Fields

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly WinMediaPlayer? _player;
    private readonly OutputDevice? _speakers;
    private readonly PlaybackCurrentTimeWatcher _timeWatcher;

    private bool _ignoreSliderChange;
    private TimeSpan _songPosition;
    private double? _savedPosition;
    private int _savePositionCounter;
    private int _loadEpoch;

    #endregion

    #region Constructor

    public PlaybackService(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _timeWatcher = PlaybackCurrentTimeWatcher.Instance;

        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

        _timeWatcher.CurrentTimeChanged += OnSongTick;

        // Subscribe to song settings changes
        SongSettings.SpeedChanged += _ => ApplyEffectivePlaybackSpeed();
        SongSettings.SettingsRebuildRequired += OnSongSettingsRebuildRequired;

        // SystemMediaTransportControls is only supported on Windows 10 and later
        if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
            Environment.OSVersion.Version.Major >= 10)
        {
            _player = ioc.Get<WinMediaPlayer>();

            _player!.CommandManager.NextReceived += async (_, _) => await Next();
            _player!.CommandManager.PreviousReceived += (_, _) => Previous();

            _player!.CommandManager.PlayReceived += async (_, _) => await PlayPause();
            _player!.CommandManager.PauseReceived += async (_, _) => await PlayPause();
        }

        try
        {
            _speakers = OutputDevice.GetByName("Microsoft GS Wavetable Synth");
        }
        catch (ArgumentException e)
        {
            CrashLogger.Log("Failed to initialize Microsoft GS Wavetable Synth.");
            CrashLogger.LogException(e);
            _ = ShowAudioInitializationErrorAsync(e);
            SetListenMode(false, pausePlaybackOnChange: false);
        }
    }

    #endregion

    private void OnSongSettingsRebuildRequired()
    {
        _ = HandleSongSettingsRebuildRequiredAsync();
    }

    /// <summary>
    /// Rebuilds playback in-place for the currently opened song while preserving position and play state.
    /// Useful when song properties are edited from dialogs and should apply immediately.
    /// </summary>
    public Task RefreshCurrentSongRealtimeAsync() => HandleSongSettingsRebuildRequiredAsync();

    private async Task HandleSongSettingsRebuildRequiredAsync()
    {
        try
        {
            TrackView.UpdateTrackPlayableNotes();
            TrackView.NotifyNoteStatsChanged();

            var wasPlaying = Playback?.IsRunning ?? false;
            _savedPosition = _songPosition.TotalSeconds;
            await InitializePlayback();
            if (wasPlaying && Playback is not null)
                Playback.Start();

            // Notify song list UI to refresh
            _main.SongsView.RefreshCurrentSong();
            _main.QueueView.RefreshCurrentSong();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Unhandled exception while rebuilding playback after song settings change.");
            CrashLogger.LogException(ex);
        }
    }

    private static async Task ShowAudioInitializationErrorAsync(Exception e)
    {
        var message = $"Audio output device initialization failed.\n\nError:\n{e.Message}";

        try
        {
            var dialog = DialogHelper.CreateDialog();
            dialog.Title = "Audio device unavailable";
            dialog.Content = message;
            dialog.CloseButtonText = "Ignore";

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            CrashLogger.Log("DialogHost was not ready while showing audio initialization error. Falling back to MessageBox.");
            System.Windows.MessageBox.Show(
                message,
                "Audio device unavailable",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch (Exception dialogError)
        {
            CrashLogger.Log("Failed to display audio initialization error dialog.");
            CrashLogger.LogException(dialogError);
            System.Windows.MessageBox.Show(
                message,
                "Audio device unavailable",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    #region Properties

    public Playback? Playback { get; private set; }

    public bool IsPlaying => Playback?.IsRunning ?? false;

    public double SongPosition
    {
        get => _songPosition.TotalSeconds;
        set
        {
            _songPosition = TimeSpan.FromSeconds(value);
            NotifyOfPropertyChange(nameof(SongPosition));
            NotifyOfPropertyChange(nameof(CurrentTime));
            SongPositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TimeSpan CurrentTime => _songPosition;

    public TimeSpan MaximumTime => Queue.OpenedFile?.Duration ?? TimeSpan.Zero;

    public bool CanHitNext
    {
        get
        {
            // In Off mode, can't go next if at the last song
            if (Queue.Loop is QueueViewModel.LoopMode.Off)
            {
                var last = Queue.GetPlaylist().LastOrDefault();
                return Queue.OpenedFile != last;
            }
            return true;
        }
    }

    public bool CanHitPlayPause
    {
        get
        {
            var hasNotes = TrackView.MidiTracks
                .Where(t => t.IsChecked)
                .Any(t => t.CanBePlayed);

            return Playback is not null
                && hasNotes
                && MaximumTime > TimeSpan.Zero;
        }
    }

    public bool CanHitPrevious => CurrentTime > TimeSpan.FromSeconds(3) || Queue.History.Count > 1;

    public string PlayPauseIcon => IsPlaying ? PauseIcon : PlayIcon;

    public string PlayPauseSvgSource => IsPlaying ? "/Icons/Controls/Pause.svg" : "/Icons/Controls/Play.svg";

    public Geometry PlayPauseGeometry => IsPlaying
        ? (Geometry)Application.Current.FindResource("PauseIconGeometry")
        : (Geometry)Application.Current.FindResource("PlayIconGeometry");

    public string PlayPauseTooltip => IsPlaying ? "Pause" : "Play";

    private QueueViewModel Queue => _main.QueueView;
    private TrackViewModel TrackView => _main.TrackView;
    private SettingsPageViewModel SettingsPage => _main.SettingsView;
    private InstrumentViewModel InstrumentPage => _main.InstrumentView;
    private SongService SongSettings => _main.SongSettings;

    private static string PauseIcon => "\xEDB4";
    private static string PlayIcon => "\xF5B0";

    private MusicDisplayProperties? Display =>
        _player?.SystemMediaTransportControls.DisplayUpdater.MusicProperties;

    private SystemMediaTransportControls? Controls =>
        _player?.SystemMediaTransportControls;

    #endregion

    #region Events

    public event EventHandler? SongPositionChanged;
    public event EventHandler? PlaybackStateChanged;
    public event EventHandler<NotePlayedEventArgs>? NotePlayed;

    #endregion

    #region Playback Controls

    public void SetSavedPosition(double positionSeconds)
    {
        _savedPosition = positionSeconds;
        SongPosition = positionSeconds;
    }

    public async Task PlayPause()
    {
        if (Playback is null)
            await InitializePlayback();

        var playback = Playback;
        if (playback is null)
            return;

        try
        {
            if (playback.IsRunning)
            {
                playback.Stop();
                Queue.SaveCurrentSong(CurrentTime.TotalSeconds);
            }
            else
            {
                var time = new MetricTimeSpan(CurrentTime);
                playback.PlaybackStart = time;
                playback.MoveToTime(time);

                await StartPlayback(playback);
            }
        }
        catch (ObjectDisposedException) { }
    }

    public void CloseFile()
    {
        var old = Playback;
        Playback = null;

        if (old != null)
        {
            try { _timeWatcher.RemovePlayback(old); } catch (ObjectDisposedException) { }
            try { old.Stop(); } catch (ObjectDisposedException) { }
            try { old.Dispose(); } catch (ObjectDisposedException) { }
        }

        TrackView.MidiTracks.Clear();
        MoveSlider(TimeSpan.Zero);

        Queue.OpenedFile = null;
        SongSettings.ClearSettings();
    }

    /// <summary>
    /// Go to the next song.
    /// </summary>
    /// <param name="userInitiated">True if user clicked Next button, false if auto-triggered by song finish</param>
    public async Task Next(bool userInitiated = true)
    {
        var next = Queue.Next(userInitiated);
        if (next is null)
        {
            var pb = Playback;
            if (pb is not null)
            {
                try { pb.PlaybackStart = null; pb.MoveToStart(); }
                catch (ObjectDisposedException) { }
            }

            MoveSlider(TimeSpan.Zero);
            UpdateButtons();
            return;
        }

        await LoadFileAsync(next, autoPlay: true);
    }

    public async void Previous()
    {
        if (CurrentTime > TimeSpan.FromSeconds(3))
        {
            var pb = Playback;
            if (pb is not null)
            {
                try { pb.Stop(); pb.MoveToStart(); }
                catch (ObjectDisposedException) { }
            }

            MoveSlider(TimeSpan.Zero);

            pb = Playback;
            if (pb is not null)
                await StartPlayback(pb);
        }
        else
        {
            while (Queue.History.Count > 1)
            {
                Queue.History.Pop();
                var previous = Queue.History.Pop();

                if (Queue.GetPlaylist().Any(track => track.Song.Id == previous.Song.Id))
                {
                    await LoadFileAsync(previous, autoPlay: true);
                    return;
                }
            }
        }
    }

    #endregion

    #region Playback Initialization

    public Task InitializePlayback()
    {
        var old = Playback;
        Playback = null;
        if (old != null)
        {
            try { old.Stop(); } catch (ObjectDisposedException) { }
            try { old.Dispose(); } catch (ObjectDisposedException) { }
        }

        if (Queue.OpenedFile is null)
        {
            UpdateButtons();
            return Task.CompletedTask;
        }

        var midi = Queue.OpenedFile.Midi;
        var tempoMap = Queue.OpenedFile.OriginalTempoMap;

        var tracksToPlay = TrackView.MidiTracks
            .Where(t => t.IsChecked)
            .Select(t => t.Track)
            .ToList();

        var useMergeNotes = Queue.OpenedFile.Song.MergeNotes ?? false;
        var mergeMilliseconds = Queue.OpenedFile.Song.MergeMilliseconds ?? 100;

        if (useMergeNotes && tracksToPlay.Count > 0)
        {
            midi.Chunks.Clear();
            midi.Chunks.AddRange(tracksToPlay);
            midi.MergeObjects(ObjectType.Note, new()
            {
                VelocityMergingPolicy = VelocityMergingPolicy.Average,
                Tolerance = new MetricTimeSpan(0, 0, 0, (int)mergeMilliseconds)
            });
            tracksToPlay = midi.GetTrackChunks().ToList();
        }

        if (tracksToPlay.Count == 0)
        {
            Playback = null;
            UpdateButtons();
            return Task.CompletedTask;
        }

        var playback = tracksToPlay.GetPlayback(tempoMap);

        Playback = playback;
        ApplyEffectivePlaybackSpeed();
        playback.InterruptNotesOnStop = true;
        playback.Finished += (_, _) =>
        {
            // Marshal to UI thread to avoid cross-thread issues
            // Only auto-next if this playback is still the current one
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(async () =>
            {
                if (Playback == playback)
                    await Next(userInitiated: false);
            });
        };
        playback.EventPlayed += OnNoteEvent;

        playback.Started += (_, _) =>
        {
            _timeWatcher.RemoveAllPlaybacks();
            _timeWatcher.AddPlayback(playback, TimeSpanType.Metric);
            _timeWatcher.Start();
            UpdateButtons();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        };

        playback.Stopped += (_, _) =>
        {
            _timeWatcher.Stop();
            UpdateButtons();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        };

        if (_savedPosition.HasValue)
        {
            var time = TimeSpan.FromSeconds(_savedPosition.Value);
            try
            {
                playback.MoveToTime(new MetricTimeSpan(time));
            }
            catch (InvalidOperationException)
            {
                // Enumeration already finished - playback has no events
            }
            _savedPosition = null;

            UpdateButtons();
            MoveSlider(time);
            return Task.CompletedTask;
        }

        UpdateButtons();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies the combined per-song playback speed:
    /// base speed option multiplied by custom BPM ratio (if configured).
    /// </summary>
    private void ApplyEffectivePlaybackSpeed()
    {
        if (Playback is null)
            return;

        var speed = SongSettings.Speed;
        var file = Queue.OpenedFile;

        if (file?.Song.Bpm is double customBpm && customBpm > 0)
        {
            var nativeBpm = file.GetNativeBpm();
            if (nativeBpm > 0)
                speed *= customBpm / nativeBpm;
        }

        Playback.Speed = speed;
    }

    #endregion

    #region Slider & Time

    public void OnSongPositionChanged()
    {
        if (_ignoreSliderChange)
        {
            _ignoreSliderChange = false;
            return;
        }

        var pb = Playback;
        if (pb is null)
            return;

        try
        {
            var isRunning = pb.IsRunning;
            pb.Stop();
            pb.MoveToTime(new MetricTimeSpan(_songPosition));
            if (Settings.UseSpeakers && isRunning)
                pb.Start();
        }
        catch (ObjectDisposedException) { }

        _ignoreSliderChange = false;
    }

    private void OnSongTick(object? sender, PlaybackCurrentTimeChangedEventArgs e)
    {
        foreach (var playbackTime in e.Times)
        {
            TimeSpan time = (MetricTimeSpan)playbackTime.Time;
            MoveSlider(time);
            UpdateButtons();

            _savePositionCounter++;
            if (_savePositionCounter >= 50)
            {
                _savePositionCounter = 0;
                Queue.SaveCurrentSong(time.TotalSeconds);
            }
        }
    }

    private void MoveSlider(TimeSpan value)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => MoveSlider(value));
            return;
        }

        _ignoreSliderChange = true;
        SongPosition = value.TotalSeconds;
    }

    #endregion

    #region Note Playing

    private void OnNoteEvent(object? sender, MidiEventPlayedEventArgs e)
    {
        if (e.Event is not NoteEvent noteEvent)
            return;

        PlayNote(noteEvent);
    }

    private void PlayNote(NoteEvent noteEvent)
    {
        try
        {
            var layout = InstrumentPage.SelectedLayout.Key;
            var instrument = InstrumentPage.SelectedInstrument.Key;
            var note = ApplyNoteSettings(instrument, noteEvent.NoteNumber);
            var selectedGame = _main.SelectedGame?.Definition;
            var isGameRunning = selectedGame is not null && GameRegistry.IsGameRunning(selectedGame);

            // Notify listeners about note being played (for track glow effects)
            if (noteEvent.EventType == MidiEventType.NoteOn && noteEvent.Velocity > 0)
            {
                NotePlayed?.Invoke(this, new NotePlayedEventArgs(noteEvent.NoteNumber));
            }

            if (Settings.UseSpeakers)
            {
                noteEvent.NoteNumber = new((byte)note);
                _speakers?.SendEvent(noteEvent);
                return;
            }

            if (!isGameRunning)
            {
                if (!HandleGameNotRunning())
                    return;

                noteEvent.NoteNumber = new((byte)note);
                _speakers?.SendEvent(noteEvent);
                return;
            }

            if (!WindowHelper.IsGameFocused())
            {
                HandleGameFocusLoss();
                return;
            }

            var useHoldNotes = Queue.OpenedFile?.Song.HoldNotes ?? false;

            switch (noteEvent.EventType)
            {
                case MidiEventType.NoteOff:
                    KeyboardPlayer.NoteUp(note, layout, instrument);
                    break;
                case MidiEventType.NoteOn when noteEvent.Velocity <= 0:
                    return;
                case MidiEventType.NoteOn when useHoldNotes:
                    KeyboardPlayer.NoteDown(note, layout, instrument);
                    break;
                case MidiEventType.NoteOn:
                    KeyboardPlayer.PlayNote(note, layout, instrument);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex);
        }
    }

    private int ApplyNoteSettings(string instrumentId, int noteId)
    {
        noteId -= Queue.OpenedFile?.Song.Key ?? SongSettings.KeyOffset;
        return Settings.TransposeNotes && SongSettings.Transpose is not null
            ? KeyboardPlayer.TransposeNote(instrumentId, ref noteId, SongSettings.Transpose.Value.Key)
            : noteId;
    }

    private bool HandleGameNotRunning()
    {
        var shouldAutoEnableListenMode = Settings.AutoEnableListenMode;

        if (shouldAutoEnableListenMode && !Settings.UseSpeakers)
        {
            var pausedPlayback = SetListenMode(true, pausePlaybackOnChange: true);
            if (pausedPlayback)
                return false;
        }

        var selectedGameName = _main.SelectedGame?.Definition.DisplayName ?? "Selected game";
        var gameLabel = $"{selectedGameName} is not running";

        var listenModeEnabled = Settings.UseSpeakers;
        _main.ShowGameInactiveToast(gameLabel, listenModeEnabled);

        return listenModeEnabled;
    }

    private void HandleGameFocusLoss()
    {
        var pb = Playback;
        if (pb is not null)
        {
            try
            {
                if (pb.IsRunning)
                {
                    pb.Stop();
                    Queue.SaveCurrentSong(CurrentTime.TotalSeconds);
                    UpdateButtons();
                }
            }
            catch (ObjectDisposedException) { }
        }

        var selectedGameName = _main.SelectedGame?.Definition.DisplayName ?? "Selected game";
        _main.ShowGameFocusLossToast(selectedGameName);
    }

    private async Task<bool> StartPlayback(Playback playback)
    {
        var selectedGame = _main.SelectedGame?.Definition;
        var isGameRunning = selectedGame is not null && GameRegistry.IsGameRunning(selectedGame);

        try
        {
            if (Settings.UseSpeakers)
            {
                playback.PlaybackStart = playback.GetCurrentTime(TimeSpanType.Midi);
                playback.Start();
                return true;
            }

            if (!isGameRunning)
            {
                if (!HandleGameNotRunning())
                    return false;

                playback.PlaybackStart = playback.GetCurrentTime(TimeSpanType.Midi);
                playback.Start();
                return true;
            }

            WindowHelper.EnsureGameOnTop();
            await Task.Delay(120);

            // After delay, verify this playback is still current
            if (Playback != playback)
                return false;

            if (WindowHelper.IsGameFocused())
            {
                playback.PlaybackStart = playback.GetCurrentTime(TimeSpanType.Midi);
                playback.Start();
                return true;
            }
        }
        catch (ObjectDisposedException) { }

        return false;
    }

    public bool SetListenMode(bool enabled, bool pausePlaybackOnChange = true)
    {
        if (Settings.UseSpeakers == enabled)
            return false;

        Settings.Modify(s => s.UseSpeakers = enabled);

        var pausedPlayback = false;
        var pb = Playback;
        if (pausePlaybackOnChange && pb is not null)
        {
            try
            {
                if (pb.IsRunning)
                {
                    pb.Stop();
                    Queue.SaveCurrentSong(CurrentTime.TotalSeconds);
                    pausedPlayback = true;
                    UpdateButtons();
                }
            }
            catch (ObjectDisposedException) { }
        }

        _events.Publish(new ListenModeChangedNotification(enabled));
        return pausedPlayback;
    }

    #endregion

    #region UI Updates

    public void UpdateButtons()
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateButtons);
            return;
        }

        _main.UpdateTitle();

        // Notify UI of property changes
        NotifyOfPropertyChange(nameof(IsPlaying));
        NotifyOfPropertyChange(nameof(PlayPauseIcon));
        NotifyOfPropertyChange(nameof(PlayPauseSvgSource));
        NotifyOfPropertyChange(nameof(PlayPauseGeometry));
        NotifyOfPropertyChange(nameof(PlayPauseTooltip));
        NotifyOfPropertyChange(nameof(CanHitPlayPause));
        NotifyOfPropertyChange(nameof(CanHitNext));
        NotifyOfPropertyChange(nameof(CanHitPrevious));

        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);

        if (Controls is not null && Display is not null)
        {
            Controls.IsPlayEnabled = CanHitPlayPause;
            Controls.IsPauseEnabled = CanHitPlayPause;
            Controls.IsNextEnabled = CanHitNext;
            Controls.IsPreviousEnabled = CanHitPrevious;

            Controls.PlaybackStatus =
                Queue.OpenedFile is null ? MediaPlaybackStatus.Closed :
                Playback is null ? MediaPlaybackStatus.Stopped :
                Playback.IsRunning ? MediaPlaybackStatus.Playing :
                MediaPlaybackStatus.Paused;

            var file = Queue.OpenedFile;
            if (file is not null)
            {
                var position = $"{file.Position}/{Queue.GetPlaylist().Count}";
                Display.Title = file.Title;
                Display.Artist = $"Playing {position} {CurrentTime:mm\\:ss}";
            }

            Controls.DisplayUpdater.Update();
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Loads a MIDI file, initializes playback, and optionally auto-plays.
    /// Awaitable — callers that need to wait for loading should use this directly.
    /// </summary>
    public async Task LoadFileAsync(MidiFile file, bool autoPlay = false)
    {
        // Ignore duplicate reloads for the currently opened file.
        // This can be triggered by selection-change events while the same song is already loaded.
        if (Queue.OpenedFile == file && Playback is not null)
        {
            if (autoPlay && !Playback.IsRunning)
            {
                var playback = Playback;
                if (playback is not null)
                {
                    try
                    {
                        playback.Stop();
                        playback.PlaybackStart = null;
                        playback.MoveToStart();
                        MoveSlider(TimeSpan.Zero);
                        await StartPlayback(playback);
                    }
                    catch (ObjectDisposedException) { }
                }
            }
            return;
        }

        var epoch = ++_loadEpoch;

        CloseFile();
        Queue.OpenedFile = file;
        Queue.History.Push(file);

        SongSettings.ApplyPerSongSettings(file);

        try
        {
            file.InitializeMidi();
        }
        catch (FileNotFoundException)
        {
            await _main.FileService.HandleMissingSongFileAsync(file);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            await _main.FileService.HandleMissingSongFileAsync(file);
            return;
        }

        TrackView.InitializeTracks();
        TrackView.UpdateTrackPlayableNotes();

        await InitializePlayback();

        TrackView.NotifyNoteStatsChanged();

        NotifyOfPropertyChange(nameof(MaximumTime));
        NotifyOfPropertyChange(nameof(CanHitPlayPause));
        NotifyOfPropertyChange(nameof(CanHitNext));
        NotifyOfPropertyChange(nameof(CanHitPrevious));

        _main.SongsView.RefreshCurrentSong();
        _main.QueueView.RefreshCurrentSong();

        // Only auto-play if this is still the most recent load request
        if (autoPlay && epoch == _loadEpoch && Playback is not null)
            await PlayPause();
    }

    /// <summary>
    /// Event aggregator handler — fire-and-forget entry point for MidiFile publish.
    /// No auto-play; callers that need auto-play should use LoadFileAsync directly.
    /// </summary>
    public async void Handle(MidiFile file)
    {
        try
        {
            await LoadFileAsync(file);
        }
        catch (Exception e)
        {
            CrashLogger.Log("Unhandled playback file-load exception.");
            CrashLogger.LogException(e);
        }
    }

    public async void Handle(MidiTrack track)
    {
        // Save disabled tracks state to song
        if (Queue.OpenedFile is not null)
        {
            var disabledIndices = TrackView.MidiTracks
                .Where(t => !t.IsChecked)
                .Select(t => t.Index);
            Queue.OpenedFile.Song.DisabledTracks = string.Join(",", disabledIndices);

            await using var db = _ioc.Get<LyreContext>();
            db.Songs.Update(Queue.OpenedFile.Song);
            await db.SaveChangesAsync();
        }

        // Update note statistics
        TrackView.NotifyNoteStatsChanged();

        var wasPlaying = Playback?.IsRunning ?? false;
        _savedPosition = _songPosition.TotalSeconds;

        await InitializePlayback();

        if (wasPlaying && Playback is not null)
            Playback.Start();
    }

    public async void Handle(MergeNotesNotification message)
    {
        var wasPlaying = Playback?.IsRunning ?? false;
        _savedPosition = _songPosition.TotalSeconds;

        if (!message.Merge)
        {
            Queue.OpenedFile?.InitializeMidi();
            TrackView.InitializeTracks();
        }

        await InitializePlayback();

        if (wasPlaying && Playback is not null)
            Playback.Start();
    }

    public async void Handle(SettingsPageViewModel message)
    {
        TrackView.UpdateTrackPlayableNotes();
        TrackView.NotifyNoteStatsChanged();

        var wasPlaying = Playback?.IsRunning ?? false;
        _savedPosition = _songPosition.TotalSeconds;

        await InitializePlayback();

        if (wasPlaying && Playback is not null)
            Playback.Start();
    }

    public void Handle(InstrumentViewModel message)
    {
        if (_main.InstrumentView is null) return;

        TrackView.UpdateTrackPlayableNotes();
        TrackView.NotifyNoteStatsChanged();
    }

    public async void Handle(PlayTimerNotification message)
    {
        if (IsPlaying == message.ShouldPlay)
            return;

        if (message.ShouldPlay && !CanHitPlayPause)
            return;

        await PlayPause();
    }

    #endregion
}

/// <summary>
/// Event args for note played event
/// </summary>
public class NotePlayedEventArgs(int noteNumber) : EventArgs
{
    public int NoteNumber { get; } = noteNumber;
}
