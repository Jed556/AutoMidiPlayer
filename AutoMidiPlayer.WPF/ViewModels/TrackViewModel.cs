using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Windows.Media;
using Windows.Media.Playback;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.ModernWPF.Errors;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Melanchall.DryWetMidi.Tools;
using ModernWpf.Controls;
using Stylet;
using StyletIoC;
using static AutoMidiPlayer.WPF.ViewModels.SettingsPageViewModel;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.ViewModels;

public class TrackViewModel : Screen,
    IHandle<MidiFile>, IHandle<MidiTrack>,
    IHandle<SettingsPageViewModel>,
    IHandle<MergeNotesNotification>,
    IHandle<PlayTimerNotification>
{
    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly MediaPlayer? _player;
    private readonly OutputDevice? _speakers;
    private readonly PlaybackCurrentTimeWatcher _timeWatcher;
    private bool _ignoreSliderChange;
    private InputDevice? _inputDevice;
    private TimeSpan _songPosition;
    private double? _savedPosition;
    private int _savePositionCounter;
    private bool _isViewActive = true; // Start true since TrackView is the initial tab

    public TrackViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _timeWatcher = PlaybackCurrentTimeWatcher.Instance;

        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

        SelectedMidiInput = MidiInputs[0];

        _timeWatcher.CurrentTimeChanged += OnSongTick;

        // SystemMediaTransportControls is only supported on Windows 10 and later
        // https://docs.microsoft.com/en-us/uwp/api/windows.media.systemmediatransportcontrols
        if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
            Environment.OSVersion.Version.Major >= 10)
        {
            _player = ioc.Get<MediaPlayer>();

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

            SettingsPage.CanUseSpeakers = false;
            Settings.UseSpeakers = false;
        }
    }

    public BindableCollection<MidiInput> MidiInputs { get; } = new()
    {
        new("None")
    };

    public BindableCollection<MidiTrack> MidiTracks { get; } = new();

    public bool CanHitNext
    {
        get
        {
            if (Queue.Loop is not QueueViewModel.LoopMode.Playlist)
                return true;

            var last = Queue.GetPlaylist().LastOrDefault();
            return Queue.OpenedFile != last;
        }
    }

    public bool CanHitPlayPause
    {
        get
        {
            var hasNotes = MidiTracks
                .Where(t => t.IsChecked)
                .Any(t => t.CanBePlayed);

            return Playback is not null
                && hasNotes
                && MaximumTime > TimeSpan.Zero;
        }
    }

    public bool CanHitPrevious => CurrentTime > TimeSpan.FromSeconds(3) || Queue.History.Count > 1;

    public double SongPosition
    {
        get => _songPosition.TotalSeconds;
        set => SetAndNotify(ref _songPosition, TimeSpan.FromSeconds(value));
    }

    public MidiInput? SelectedMidiInput { get; set; }

    public Playback? Playback { get; private set; }

    public QueueViewModel Queue => _main.QueueView;

    public bool IsPlaying => Playback?.IsRunning ?? false;

    public string PlayPauseIcon => Playback?.IsRunning ?? false ? PauseIcon : PlayIcon;

    public string PlayPauseTooltip => Playback?.IsRunning ?? false ? "Pause" : "Play";

    public TimeSpan CurrentTime => _songPosition;

    public TimeSpan MaximumTime => Queue.OpenedFile?.Duration ?? TimeSpan.Zero;

    private MusicDisplayProperties? Display =>
        _player?.SystemMediaTransportControls.DisplayUpdater.MusicProperties;

    private SettingsPageViewModel SettingsPage => _main.SettingsView;

    private static string PauseIcon => "\xEDB4";

    private static string PlayIcon => "\xF5B0";

    private SystemMediaTransportControls? Controls =>
        _player?.SystemMediaTransportControls;

    protected override void OnActivate()
    {
        CrashLogger.Log($"OnActivate called, MidiTracks.Count={MidiTracks.Count}");
        try
        {
            base.OnActivate();
            _isViewActive = true;
            CrashLogger.Log("OnActivate completed successfully");
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex);
            throw;
        }
    }

    protected override void OnDeactivate()
    {
        CrashLogger.Log($"OnDeactivate called, MidiTracks.Count={MidiTracks.Count}");
        try
        {
            base.OnDeactivate();
            _isViewActive = false;

            // Stop all glow effects when leaving the view
            foreach (var track in MidiTracks)
            {
                track.StopGlow();
            }
            CrashLogger.Log("OnDeactivate completed successfully");
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex);
            throw;
        }
    }

    public async void Handle(MergeNotesNotification message)
    {
        // Save current playback state before reinitializing
        var wasPlaying = Playback?.IsRunning ?? false;

        // Use _savedPosition so it's restored INSIDE InitializePlayback before UpdateButtons()
        _savedPosition = _songPosition.TotalSeconds;

        if (!message.Merge)
        {
            Queue.OpenedFile?.InitializeMidi();
            InitializeTracks();
        }

        await InitializePlayback();

        // Resume playback if was playing
        if (wasPlaying && Playback is not null)
        {
            Playback.Start();
        }
    }

    public async void Handle(MidiFile file)
    {
        CloseFile();
        Queue.OpenedFile = file;
        Queue.History.Push(file);

        // Re-read the MIDI file from disk to restore all tracks
        // (InitializePlayback modifies the in-memory MIDI by removing unchecked tracks)
        file.InitializeMidi();
        InitializeTracks();
        await InitializePlayback();
    }

    public async void Handle(MidiTrack track)
    {
        // Save disabled tracks state to song
        if (Queue.OpenedFile is not null)
        {
            var disabledIndices = MidiTracks
                .Where(t => !t.IsChecked)
                .Select(t => t.Index);
            Queue.OpenedFile.Song.DisabledTracks = string.Join(",", disabledIndices);

            // Save directly to database
            await using var db = _ioc.Get<LyreContext>();
            db.Songs.Update(Queue.OpenedFile.Song);
            await db.SaveChangesAsync();
        }

        // Save current playback state before reinitializing
        var wasPlaying = Playback?.IsRunning ?? false;

        // Use _savedPosition so it's restored INSIDE InitializePlayback before UpdateButtons()
        // This prevents the WPF slider from resetting when Maximum binding updates
        _savedPosition = _songPosition.TotalSeconds;

        await InitializePlayback();

        // Resume playback if was playing
        if (wasPlaying && Playback is not null)
        {
            Playback.Start();
        }
    }

    public async void Handle(PlayTimerNotification message)
    {
        if (!(Playback?.IsRunning ?? false) && CanHitPlayPause) await PlayPause();
    }

    public async void Handle(SettingsPageViewModel message)
    {
        // Save current playback state before reinitializing
        var wasPlaying = Playback?.IsRunning ?? false;

        // Use _savedPosition so it's restored INSIDE InitializePlayback before UpdateButtons()
        _savedPosition = _songPosition.TotalSeconds;

        await InitializePlayback();

        // Resume playback if was playing
        if (wasPlaying && Playback is not null)
        {
            Playback.Start();
        }
    }

    public void OpenFile()
    {
        // Note: OpenFile has been removed from QueueViewModel
        // Songs should be added via SongsView instead
        UpdateButtons();
    }

    /// <summary>
    /// Set a saved position to restore when the song starts playing
    /// </summary>
    public void SetSavedPosition(double positionSeconds)
    {
        _savedPosition = positionSeconds;
        // Update the slider to show saved position
        SongPosition = positionSeconds;
    }

    public async Task PlayPause()
    {
        if (Playback is null)
            await InitializePlayback();

        if (Playback!.IsRunning)
        {
            Playback.Stop();
            // Save position on pause
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

        MidiTracks.Clear();
        MoveSlider(TimeSpan.Zero);

        Playback = null;
        Queue.OpenedFile = null;
        SettingsPage.Transpose = null;
    }

    public async void Next()
    {
        var next = Queue.Next();
        if (next is null)
        {
            if (Playback is not null)
            {
                Playback.PlaybackStart = null;
                Playback.MoveToStart();
            }

            MoveSlider(TimeSpan.Zero);
            return;
        }

        Handle(next);

        if (Playback is not null)
            await PlayPause();
    }

    public void OnSelectedMidiInputChanged()
    {
        _inputDevice?.Dispose();

        if (SelectedMidiInput?.DeviceName is not null
            && SelectedMidiInput.DeviceName != "None")
        {
            _inputDevice = InputDevice.GetByName(SelectedMidiInput.DeviceName);

            _inputDevice!.EventReceived += OnNoteEvent;
            _inputDevice!.StartEventsListening();
        }
    }

    public void OnSongPositionChanged()
    {
        NotifyOfPropertyChange(() => CurrentTime);

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

    public void OnSongTick(object? sender, PlaybackCurrentTimeChangedEventArgs e)
    {
        foreach (var playbackTime in e.Times)
        {
            TimeSpan time = (MetricTimeSpan)playbackTime.Time;
            MoveSlider(time);

            UpdateButtons();

            // Save position every 5 seconds (assuming ~10 ticks per second)
            _savePositionCounter++;
            if (_savePositionCounter >= 50)
            {
                _savePositionCounter = 0;
                Queue.SaveCurrentSong(time.TotalSeconds);
            }
        }
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
            Queue.Previous();
    }

    public void RefreshDevices()
    {
        MidiInputs.Clear();
        MidiInputs.Add(new("None"));

        foreach (var device in InputDevice.GetAll())
        {
            MidiInputs.Add(new(device.Name));
        }

        SelectedMidiInput = MidiInputs[0];
    }

    public void UpdateButtons()
    {
        NotifyOfPropertyChange(() => CanHitNext);
        NotifyOfPropertyChange(() => CanHitPrevious);
        NotifyOfPropertyChange(() => CanHitPlayPause);

        NotifyOfPropertyChange(() => IsPlaying);
        NotifyOfPropertyChange(() => PlayPauseIcon);
        NotifyOfPropertyChange(() => PlayPauseTooltip);
        NotifyOfPropertyChange(() => MaximumTime);
        NotifyOfPropertyChange(() => CurrentTime);

        // Update window title based on playback state
        _main.UpdateTitle();

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

    private int ApplyNoteSettings(Keyboard.Instrument instrument, int noteId)
    {
        noteId -= Queue.OpenedFile?.Song.Key ?? SettingsPage.KeyOffset;
        return Settings.TransposeNotes && SettingsPage.Transpose is not null
            ? LyrePlayer.TransposeNote(instrument, ref noteId, SettingsPage.Transpose.Value.Key)
            : noteId;
    }

    private Task InitializePlayback()
    {
        Playback?.Stop();
        Playback?.Dispose();

        if (Queue.OpenedFile is null)
            return Task.CompletedTask;

        var midi = Queue.OpenedFile.Midi;

        // Use the original tempo map stored when the file was loaded
        // This preserves BPM/tempo regardless of which tracks are enabled
        var tempoMap = Queue.OpenedFile.OriginalTempoMap;

        // Get only the checked tracks for playback
        var tracksToPlay = MidiTracks
            .Where(t => t.IsChecked)
            .Select(t => t.Track)
            .ToList();

        if (Settings.MergeNotes && tracksToPlay.Count > 0)
        {
            // Create a temporary MIDI file for merging
            midi.Chunks.Clear();
            midi.Chunks.AddRange(tracksToPlay);
            midi.MergeObjects(ObjectType.Note, new()
            {
                VelocityMergingPolicy = VelocityMergingPolicy.Average,
                Tolerance = new MetricTimeSpan(0, 0, 0, (int)Settings.MergeMilliseconds)
            });
            tracksToPlay = midi.GetTrackChunks().ToList();
        }

        // Transpose setting is now handled per-song in the import dialog
        // The song's transpose setting (defaulting to Ignore) will be used

        // Use GetPlayback with track chunks and original tempo map to maintain consistent BPM
        var playback = tracksToPlay.GetPlayback(tempoMap);

        Playback = playback;
        playback.Speed = SettingsPage.Speed;
        playback.InterruptNotesOnStop = true;
        playback.Finished += (_, _) => { Next(); };
        playback.EventPlayed += OnNoteEvent;

        playback.Started += (_, _) =>
        {
            _timeWatcher.RemoveAllPlaybacks();
            _timeWatcher.AddPlayback(playback, TimeSpanType.Metric);
            _timeWatcher.Start();

            UpdateButtons();
        };

        playback.Stopped += (_, _) =>
        {
            _timeWatcher.Stop();

            UpdateButtons();
        };

        // Restore saved position if available
        if (_savedPosition.HasValue)
        {
            var time = TimeSpan.FromSeconds(_savedPosition.Value);
            playback.MoveToTime(new MetricTimeSpan(time));
            _savedPosition = null;

            // Move slider AFTER UpdateButtons to avoid WPF binding interference
            UpdateButtons();
            MoveSlider(time);
            return Task.CompletedTask;
        }

        UpdateButtons();
        return Task.CompletedTask;
    }

    private void InitializeTracks()
    {
        if (Queue.OpenedFile?.Midi is null)
            return;

        // Get disabled track indices from song
        var disabledIndices = new HashSet<int>();
        if (!string.IsNullOrEmpty(Queue.OpenedFile.Song.DisabledTracks))
        {
            foreach (var indexStr in Queue.OpenedFile.Song.DisabledTracks.Split(','))
            {
                if (int.TryParse(indexStr.Trim(), out var index))
                    disabledIndices.Add(index);
            }
        }

        MidiTracks.Clear();
        var trackChunks = Queue.OpenedFile.Midi.GetTrackChunks().ToList();
        for (var i = 0; i < trackChunks.Count; i++)
        {
            var isChecked = !disabledIndices.Contains(i);
            MidiTracks.Add(new MidiTrack(_events, trackChunks[i], i, isChecked));
        }
    }

    private void MoveSlider(TimeSpan value)
    {
        _ignoreSliderChange = true;
        SongPosition = value.TotalSeconds;
    }

    private void OnNoteEvent(object? sender, MidiEventPlayedEventArgs e)
    {
        if (e.Event is not NoteEvent noteEvent)
            return;

        PlayNote(noteEvent);
    }

    private void OnNoteEvent(object? sender, MidiEventReceivedEventArgs e)
    {
        if (e.Event is not NoteEvent noteEvent)
            return;

        PlayNote(noteEvent);
    }

    private void PlayNote(NoteEvent noteEvent)
    {
        try
        {
            var layout = SettingsPage.SelectedLayout.Key;
            var instrument = SettingsPage.SelectedInstrument.Key;
            var note = ApplyNoteSettings(instrument, noteEvent.NoteNumber);

            // Trigger glow effect on tracks containing this note (only for NoteOn and when view is active)
            if (noteEvent.EventType == MidiEventType.NoteOn && noteEvent.Velocity > 0)
            {
                var matchingTracks = MidiTracks.Where(t => t.IsChecked && t.ContainsNote(noteEvent.NoteNumber)).ToList();
                if (_isViewActive && matchingTracks.Count > 0)
                {
                    foreach (var track in matchingTracks)
                    {
                        track.TriggerGlow();
                    }
                }
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

            switch (noteEvent.EventType)
            {
                case MidiEventType.NoteOff:
                    LyrePlayer.NoteUp(note, layout, instrument);
                    break;
                case MidiEventType.NoteOn when noteEvent.Velocity <= 0:
                    return;
                case MidiEventType.NoteOn when Settings.HoldNotes:
                    LyrePlayer.NoteDown(note, layout, instrument);
                    break;
                case MidiEventType.NoteOn:
                    LyrePlayer.PlayNote(note, layout, instrument);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex);
        }
    }
}
