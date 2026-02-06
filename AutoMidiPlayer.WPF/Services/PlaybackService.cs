using System;
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
using AutoMidiPlayer.WPF.Errors;
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
    private bool _autoPlayOnLoad;

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
        SongSettings.SpeedChanged += speed => { if (Playback is not null) Playback.Speed = speed; };
        SongSettings.SettingsRebuildRequired += async () =>
        {
            TrackView.UpdateTrackPlayableNotes();
            TrackView.NotifyNoteStatsChanged();

            var wasPlaying = Playback?.IsRunning ?? false;
            _savedPosition = _songPosition.TotalSeconds;
            await InitializePlayback();
            if (wasPlaying && Playback is not null) Playback.Start();

            // Notify song list UI to refresh
            _main.SongsView.RefreshCurrentSong();
            _main.QueueView.RefreshCurrentSong();
        };

        // SystemMediaTransportControls is only supported on Windows 10 and later
        if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
            Environment.OSVersion.Version.Major >= 10)
        {
            _player = ioc.Get<WinMediaPlayer>();

            _player!.CommandManager.NextReceived += (_, _) => Next();
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
            new ErrorContentDialog(e, closeText: "Ignore").ShowAsync();
            Settings.UseSpeakers = false;
        }
    }

    #endregion

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
    private SongSettingsService SongSettings => _main.SongSettings;

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

        if (Playback is null)
            return;

        if (Playback.IsRunning)
        {
            Playback.Stop();
            Queue.SaveCurrentSong(CurrentTime.TotalSeconds);
        }
        else
        {
            var time = new MetricTimeSpan(CurrentTime);
            Playback.PlaybackStart = time;
            Playback.MoveToTime(time);

            if (Settings.UseSpeakers)
                Playback.Start();
            else
            {
                WindowHelper.EnsureGameOnTop();
                await Task.Delay(100);

                if (WindowHelper.IsGameFocused())
                {
                    Playback.PlaybackStart = Playback.GetCurrentTime(TimeSpanType.Midi);
                    Playback.Start();
                }
            }
        }
    }

    public void CloseFile()
    {
        if (Playback != null)
        {
            _timeWatcher.RemovePlayback(Playback);
            Playback.Stop();
            Playback.Dispose();
        }

        TrackView.MidiTracks.Clear();
        MoveSlider(TimeSpan.Zero);

        Playback = null;
        Queue.OpenedFile = null;
        SongSettings.ClearSettings();
    }

    /// <summary>
    /// Go to the next song.
    /// </summary>
    /// <param name="userInitiated">True if user clicked Next button, false if auto-triggered by song finish</param>
    public void Next(bool userInitiated = true)
    {
        var next = Queue.Next(userInitiated);
        if (next is null)
        {
            if (Playback is not null)
            {
                Playback.PlaybackStart = null;
                Playback.MoveToStart();
            }

            MoveSlider(TimeSpan.Zero);
            UpdateButtons();
            return;
        }

        _autoPlayOnLoad = true;
        _events.Publish(next);
    }

    public void Previous()
    {
        if (CurrentTime > TimeSpan.FromSeconds(3))
        {
            Playback?.Stop();
            Playback?.MoveToStart();

            MoveSlider(TimeSpan.Zero);
            Playback?.Start();
        }
        else
        {
            if (Queue.History.Count > 1)
            {
                Queue.History.Pop();
                var previous = Queue.History.Pop();

                _autoPlayOnLoad = true;
                _events.Publish(previous);
            }
        }
    }

    #endregion

    #region Playback Initialization

    public Task InitializePlayback()
    {
        Playback?.Stop();
        Playback?.Dispose();

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
        playback.Speed = SongSettings.Speed;
        playback.InterruptNotesOnStop = true;
        playback.Finished += (_, _) =>
        {
            // Marshal to UI thread to avoid cross-thread issues
            // Pass false to indicate this is auto-next from song finish, not user clicking Next
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() => Next(userInitiated: false));
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

    #endregion

    #region Slider & Time

    public void OnSongPositionChanged()
    {
        if (Playback is null) Next();

        if (!_ignoreSliderChange && Playback is not null)
        {
            var isRunning = Playback.IsRunning;
            Playback.Stop();
            Playback.MoveToTime(new MetricTimeSpan(_songPosition));

            if (Settings.UseSpeakers && isRunning)
                Playback.Start();
        }

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

            if (!WindowHelper.IsGameFocused())
            {
                Playback?.Stop();
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

    private int ApplyNoteSettings(Keyboard.Instrument instrument, int noteId)
    {
        noteId -= Queue.OpenedFile?.Song.Key ?? SongSettings.KeyOffset;
        return Settings.TransposeNotes && SongSettings.Transpose is not null
            ? KeyboardPlayer.TransposeNote(instrument, ref noteId, SongSettings.Transpose.Value.Key)
            : noteId;
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

    public async void Handle(MidiFile file)
    {
        CloseFile();
        Queue.OpenedFile = file;
        Queue.History.Push(file);

        // Apply per-song settings (speed, key, transpose)
        SongSettings.ApplyPerSongSettings(file);

        // Re-read the MIDI file from disk to restore all tracks
        file.InitializeMidi();
        TrackView.InitializeTracks();
        TrackView.UpdateTrackPlayableNotes();

        await InitializePlayback();

        // Update note statistics for new file
        TrackView.NotifyNoteStatsChanged();

        // Notify UI about property changes
        NotifyOfPropertyChange(nameof(MaximumTime));
        NotifyOfPropertyChange(nameof(CanHitPlayPause));
        NotifyOfPropertyChange(nameof(CanHitNext));
        NotifyOfPropertyChange(nameof(CanHitPrevious));

        // Auto-play if requested (from Next/Previous navigation)
        if (_autoPlayOnLoad && Playback is not null)
        {
            _autoPlayOnLoad = false;
            await PlayPause();
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
        if (!IsPlaying && CanHitPlayPause)
            await PlayPause();
    }

    #endregion
}

/// <summary>
/// Event args for note played event
/// </summary>
public class NotePlayedEventArgs : EventArgs
{
    public int NoteNumber { get; }

    public NotePlayedEventArgs(int noteNumber)
    {
        NoteNumber = noteNumber;
    }
}
