using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Core.Games;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.ViewModels;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Melanchall.DryWetMidi.Tools;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Backend engine for MIDI playback: initialization, note playing, file loading, and event handling.
/// User-facing controls (play/pause, slider, etc.) live in <see cref="PlaybackControlsService"/>.
/// </summary>
public class PlaybackEngineService : PropertyChangedBase, IHandle<MidiFile>, IHandle<MidiTrack>,
    IHandle<SettingsPageViewModel>, IHandle<InstrumentViewModel>,
    IHandle<MergeNotesNotification>
{
    #region Fields

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly OutputDevice? _speakers;
    private readonly PlaybackCurrentTimeWatcher _timeWatcher;

    private int _loadEpoch;
    private DateTime _suppressFocusLossUntilUtc = DateTime.MinValue;
    private DateTime _playbackStartedAtUtc = DateTime.MinValue;
    private long _scheduledEventTicks;
    private bool _loggedSongContextForNotes;
    private readonly Dictionary<int, (int SourceNote, int OutputNote, string KeyName, int Velocity, long StartMs)> _activeNotes = new();

    #endregion

    #region Constructor

    public PlaybackEngineService(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _timeWatcher = PlaybackCurrentTimeWatcher.Instance;

        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

        _timeWatcher.CurrentTimeChanged += (s, e) => Controls?.OnSongTick(s, e);

        // Subscribe to song settings changes
        SongSettings.SpeedChanged += _ => ApplyEffectivePlaybackSpeed();
        SongSettings.SettingsRebuildRequired += OnSongSettingsRebuildRequired;

        try
        {
            _speakers = OutputDevice.GetByName("Microsoft GS Wavetable Synth");
        }
        catch (ArgumentException e)
        {
            Logger.Log("Failed to initialize Microsoft GS Wavetable Synth.");
            Logger.LogException(e);
            _ = AudioDeviceUnavailableDialog.ShowInitializationErrorAsync(e);
            Settings.Modify(s => s.UseSpeakers = false);
            _events.Publish(new ListenModeChangedNotification(false));
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
            SavedPosition = Controls.SongPosition;
            await InitializePlayback();
            if (wasPlaying && Playback is not null)
                Playback.Start();

            // Notify song list UI to refresh
            _main.SongsView.RefreshCurrentSong();
            _main.QueueView.RefreshCurrentSong();
        }
        catch (Exception ex)
        {
            Logger.Log("Unhandled exception while rebuilding playback after song settings change.");
            Logger.LogException(ex);
        }
    }

    #region Properties

    private static Task InitializeMidiFileAsync(MidiFile file) =>
        Task.Run(file.InitializeMidi);

    public Playback? Playback { get; private set; }

    /// <summary>
    /// Saved playback position (in seconds) for restoring after playback rebuild.
    /// Set by controls or event handlers; consumed and cleared by InitializePlayback.
    /// </summary>
    public double? SavedPosition { get; set; }

    private PlaybackControlsService Controls => _main.PlaybackControls;
    private QueueViewModel Queue => _main.QueueView;
    private TrackViewModel TrackView => _main.TrackView;
    private InstrumentViewModel InstrumentPage => _main.InstrumentView;
    private SongService SongSettings => _main.SongSettings;
    private string CurrentSongLabel => Queue.OpenedFile is null
        ? "<none>"
        : $"{Queue.OpenedFile.Title} ({Queue.OpenedFile.Path})";
    private bool ShouldLogPlayedNotes => Settings.DebugModeEnabled && Settings.LogPlayedNotes;

    #endregion

    #region Events

    public event EventHandler<NotePlayedEventArgs>? NotePlayed;

    #endregion

    public void SuppressFocusLossPause(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var suppressUntilUtc = DateTime.UtcNow.Add(duration);
        if (suppressUntilUtc > _suppressFocusLossUntilUtc)
            _suppressFocusLossUntilUtc = suppressUntilUtc;
    }

    #region Playback Initialization

    /// <summary>
    /// Disposes and nulls the current Playback object, removing it from the time watcher.
    /// Used by PlaybackControlsService.CloseFile.
    /// </summary>
    public void ResetPlayback()
    {
        var old = Playback;
        Playback = null;

        if (old != null)
        {
            try { _timeWatcher.RemovePlayback(old); } catch (ObjectDisposedException) { }
            try { old.Stop(); } catch (ObjectDisposedException) { }
            try { old.Dispose(); } catch (ObjectDisposedException) { }
        }
    }

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
            Controls.UpdateButtons();
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
            Controls.UpdateButtons();
            return Task.CompletedTask;
        }

        var playback = tracksToPlay.GetPlayback(tempoMap);

        Playback = playback;
        ApplyEffectivePlaybackSpeed();
        playback.InterruptNotesOnStop = true;
        _scheduledEventTicks = 0;
        _activeNotes.Clear();
        _loggedSongContextForNotes = false;
        playback.Finished += (_, _) =>
        {
            // Marshal to UI thread to avoid cross-thread issues
            // Only auto-next if this playback is still the current one
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(async () =>
            {
                if (Playback == playback)
                    await Controls.Next(userInitiated: false);
            });
        };
        playback.EventPlayed += OnNoteEvent;

        playback.Started += (_, _) =>
        {
            _playbackStartedAtUtc = DateTime.UtcNow;
            _scheduledEventTicks = 0;
            _activeNotes.Clear();
            _loggedSongContextForNotes = false;

            _timeWatcher.RemoveAllPlaybacks();
            _timeWatcher.AddPlayback(playback, TimeSpanType.Metric);
            _timeWatcher.Start();
            Controls.UpdateButtons();
            Controls.NotifyPlaybackStateChanged();

            Logger.LogPlayback($"PLAYBACK_STARTED song='{CurrentSongLabel}'");
            LogPerformanceSnapshot("playback-start");
        };

        playback.Stopped += (_, _) =>
        {
            _timeWatcher.Stop();
            Controls.UpdateButtons();
            Controls.NotifyPlaybackStateChanged();

            Logger.LogPlayback($"PLAYBACK_STOPPED song='{CurrentSongLabel}' | position={Controls.CurrentTime:mm\\:ss}");
            LogPerformanceSnapshot("playback-stop");
        };

        if (SavedPosition.HasValue)
        {
            var time = TimeSpan.FromSeconds(SavedPosition.Value);
            try
            {
                playback.MoveToTime(new MetricTimeSpan(time));
            }
            catch (InvalidOperationException)
            {
                // Enumeration already finished - playback has no events
            }
            SavedPosition = null;

            Controls.UpdateButtons();
            Controls.MoveSlider(time);
            return Task.CompletedTask;
        }

        Controls.UpdateButtons();
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

    #region Note Playing

    private void OnNoteEvent(object? sender, MidiEventPlayedEventArgs e)
    {
        _scheduledEventTicks += (long)e.Event.DeltaTime;

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
            var sourceNote = (int)noteEvent.NoteNumber;
            var isNoteOn = noteEvent.EventType == MidiEventType.NoteOn && noteEvent.Velocity > 0;
            var noteForKeyboard = ApplyNoteSettings(instrument, noteEvent.NoteNumber);
            var noteForListen = ApplyListenModeSettings(sourceNote);
            var hasMappedKey = KeyboardPlayer.TryGetKey(layout, instrument, noteForKeyboard, out var mappedKey);
            var transposeMode = Settings.TransposeNotes && SongSettings.Transpose is not null
                ? SongSettings.Transpose.Value.Key
                : (Transpose?)null;

            if (ShouldLogPlayedNotes && isNoteOn)
                LogSchedulerSample(sourceNote);

            // Check listen mode BEFORE expensive IsGameRunning process lookup
            if (Settings.UseSpeakers)
            {
                if (ShouldSkipListenNote(instrument, noteForListen, transposeMode))
                    return;

                if (isNoteOn)
                    NotePlayed?.Invoke(this, new NotePlayedEventArgs(sourceNote));

                if (ShouldLogPlayedNotes)
                    LogNoteInputOutput("speakers", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                _speakers?.SendEvent(CreateOutputNoteEvent(noteEvent, noteForListen));
                return;
            }

            var selectedGame = _main.SelectedGame?.Definition;
            var isGameRunning = selectedGame is not null && GameRegistry.IsGameRunning(selectedGame);

            if (!isGameRunning)
            {
                if (!HandleGameNotRunning(isPlaybackStartAttempt: false))
                    return;

                if (ShouldSkipListenNote(instrument, noteForListen, transposeMode))
                    return;

                if (isNoteOn)
                    NotePlayed?.Invoke(this, new NotePlayedEventArgs(sourceNote));

                if (ShouldLogPlayedNotes)
                    LogNoteInputOutput("auto-listen", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                _speakers?.SendEvent(CreateOutputNoteEvent(noteEvent, noteForListen));
                return;
            }

            if (!WindowHelper.IsGameFocused())
            {
                if (DateTime.UtcNow <= _suppressFocusLossUntilUtc)
                    return;

                HandleGameFocusLoss();
                return;
            }

            var useHoldNotes = Queue.OpenedFile?.Song.HoldNotes ?? false;

            switch (noteEvent.EventType)
            {
                case MidiEventType.NoteOff:
                    if (ShouldLogPlayedNotes)
                        LogNoteInputOutput("game", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                    KeyboardPlayer.NoteUp(noteForKeyboard, layout, instrument);
                    break;
                case MidiEventType.NoteOn when noteEvent.Velocity <= 0:
                    if (ShouldLogPlayedNotes)
                        LogNoteInputOutput("game", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                    return;
                case MidiEventType.NoteOn:
                    if (!hasMappedKey)
                        return;

                    NotePlayed?.Invoke(this, new NotePlayedEventArgs(sourceNote));

                    if (ShouldLogPlayedNotes)
                        LogNoteInputOutput("game", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                    if (useHoldNotes)
                        KeyboardPlayer.NoteDown(noteForKeyboard, layout, instrument);
                    else
                        KeyboardPlayer.PlayNote(noteForKeyboard, layout, instrument);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    private int ApplyNoteSettings(string instrumentId, int noteId)
    {
        noteId += SongSettings.GetEffectiveKeyOffset(Queue.OpenedFile?.Song);
        return Settings.TransposeNotes && SongSettings.Transpose is not null
            ? KeyboardPlayer.TransposeNote(instrumentId, ref noteId, SongSettings.Transpose.Value.Key)
            : noteId;
    }

    private int ApplyListenModeSettings(int noteId)
    {
        // Listen mode treats BaseKey as metadata root and only applies the
        // user-selected relative song offset (+/- around 0).
        var relativeOffset = Queue.OpenedFile?.Song.BaseKey is not null
            ? SongSettings.KeyOffset
            : 0;

        return Math.Clamp(noteId + relativeOffset, 0, 127);
    }

    private static bool ShouldSkipListenNote(string instrumentId, int note, Transpose? transposeMode)
    {
        if (transposeMode is not Transpose.Ignore)
            return false;

        if (Settings.PlayUnplayableOnIgnore)
            return false;

        return !Keyboard.GetNotes(instrumentId).Contains(note);
    }

    private static NoteEvent CreateOutputNoteEvent(NoteEvent source, int note)
    {
        var outputNote = new SevenBitNumber((byte)Math.Clamp(note, 0, 127));

        return source switch
        {
            NoteOnEvent noteOn => new NoteOnEvent(outputNote, noteOn.Velocity)
            {
                Channel = noteOn.Channel
            },
            NoteOffEvent noteOff => new NoteOffEvent(outputNote, noteOff.Velocity)
            {
                Channel = noteOff.Channel
            },
            _ => source
        };
    }

    private bool HandleGameNotRunning(bool isPlaybackStartAttempt)
    {
        Logger.LogStep(
            "GAME_NOT_RUNNING_DETECTED",
            $"song='{CurrentSongLabel}' | playbackStartAttempt={isPlaybackStartAttempt} | autoEnableListenMode={Settings.AutoEnableListenMode}");

        var shouldAutoEnableListenMode = Settings.AutoEnableListenMode;

        if (shouldAutoEnableListenMode && !Settings.UseSpeakers)
        {
            // Do not auto-enable mid-playback. User intent (manual off) should stick
            // until they explicitly press Play again.
            if (!isPlaybackStartAttempt)
            {
                PausePlaybackForGameNotRunning();
                return false;
            }

            var pausedPlayback = Controls.SetListenMode(true, pausePlaybackOnChange: true);
            if (pausedPlayback)
                return false;

            Logger.LogStep("LISTEN_MODE_AUTO_ENABLED", $"song='{CurrentSongLabel}'");
        }

        var selectedGameName = _main.SelectedGame?.Definition.DisplayName ?? "Selected game";
        var gameLabel = $"{selectedGameName} is not running";

        var listenModeEnabled = Settings.UseSpeakers;
        _main.ShowGameInactiveToast(gameLabel, listenModeEnabled);

        Logger.LogStep("GAME_NOT_RUNNING_TOAST", $"song='{CurrentSongLabel}' | listenModeEnabled={listenModeEnabled}");

        return listenModeEnabled;
    }

    private void PausePlaybackForGameNotRunning()
    {
        var pb = Playback;
        if (pb is not null)
        {
            try
            {
                if (pb.IsRunning)
                {
                    pb.Stop();
                    Queue.SaveCurrentSong(Controls.CurrentTime.TotalSeconds);
                    Controls.UpdateButtons();
                }
            }
            catch (ObjectDisposedException) { }
        }

        var selectedGameName = _main.SelectedGame?.Definition.DisplayName ?? "Selected game";
        var gameLabel = $"{selectedGameName} is not running";
        _main.ShowPlaybackStoppedGameNotRunningToast(gameLabel);
    }

    private void HandleGameFocusLoss()
    {
        Logger.LogStep("GAME_FOCUS_LOST", $"song='{CurrentSongLabel}'");

        var pb = Playback;
        if (pb is not null)
        {
            try
            {
                if (pb.IsRunning)
                {
                    pb.Stop();
                    Queue.SaveCurrentSong(Controls.CurrentTime.TotalSeconds);
                    Controls.UpdateButtons();
                }
            }
            catch (ObjectDisposedException) { }
        }

        var selectedGameName = _main.SelectedGame?.Definition.DisplayName ?? "Selected game";
        _main.ShowGameFocusLossToast(selectedGameName);
    }

    public async Task<bool> StartPlayback(Playback playback)
    {
        var selectedGame = _main.SelectedGame?.Definition;
        var isGameRunning = selectedGame is not null && GameRegistry.IsGameRunning(selectedGame);

        Logger.LogStep(
            "PLAYBACK_ENGINE_START_ATTEMPT",
            $"song='{CurrentSongLabel}' | useSpeakers={Settings.UseSpeakers} | gameRunning={isGameRunning}");

        try
        {
            if (Settings.UseSpeakers)
            {
                playback.PlaybackStart = playback.GetCurrentTime(TimeSpanType.Midi);
                playback.Start();
                Logger.LogStep("PLAYBACK_ENGINE_STARTED", $"song='{CurrentSongLabel}' | mode=speakers");
                Logger.LogPlayback($"PLAYBACK_START song='{CurrentSongLabel}' | mode=speakers");
                return true;
            }

            if (!isGameRunning)
            {
                if (!HandleGameNotRunning(isPlaybackStartAttempt: true))
                    return false;

                playback.PlaybackStart = playback.GetCurrentTime(TimeSpanType.Midi);
                playback.Start();
                Logger.LogPlayback($"PLAYBACK_START song='{CurrentSongLabel}' | mode=auto-listen");
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
                Logger.LogStep("PLAYBACK_ENGINE_STARTED", $"song='{CurrentSongLabel}' | mode=game-focused");
                Logger.LogPlayback($"PLAYBACK_START song='{CurrentSongLabel}' | mode=game-focused");
                return true;
            }
        }
        catch (ObjectDisposedException) { }

        Logger.LogStep("PLAYBACK_ENGINE_START_ABORTED", $"song='{CurrentSongLabel}'");
        return false;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Loads a MIDI file, initializes playback, and optionally auto-plays.
    /// Awaitable — callers that need to wait for loading should use this directly.
    /// </summary>
    public async Task LoadFileAsync(MidiFile file, bool autoPlay = false)
    {
        _loggedSongContextForNotes = false;
        _scheduledEventTicks = 0;
        _activeNotes.Clear();

        Logger.LogStep(
            "PLAYBACK_LOAD_REQUEST",
            $"title='{file.Title}' | path='{file.Path}' | autoPlay={autoPlay}");

        Logger.LogPlayback(
            $"PLAYBACK_LOAD_REQUEST title='{file.Title}' | path='{file.Path}' | autoPlay={autoPlay}");

        // Ignore duplicate reloads for the currently opened file.
        // This can be triggered by selection-change events while the same song is already loaded.
        if (Queue.OpenedFile == file && Playback is not null)
        {
            Logger.LogStep("PLAYBACK_LOAD_SKIPPED_DUPLICATE", $"title='{file.Title}' | autoPlay={autoPlay}");
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
                        Controls.MoveSlider(TimeSpan.Zero);
                        await StartPlayback(playback);
                    }
                    catch (ObjectDisposedException) { }
                }
            }
            return;
        }

        var epoch = ++_loadEpoch;

        Controls.CloseFile(notifyOpenedFileChanged: false);
        Queue.OpenedFile = file;
        Queue.History.Push(file);

        SongSettings.ApplyPerSongSettings(file);
        _main.InstrumentView.UpdateFromCurrentSong();

        try
        {
            await InitializeMidiFileAsync(file);
        }
        catch (FileNotFoundException)
        {
            Logger.LogStep("PLAYBACK_LOAD_MISSING_FILE", $"path='{file.Path}'");
            await _main.FileService.HandleMissingSongFileAsync(file);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            Logger.LogStep("PLAYBACK_LOAD_MISSING_DIRECTORY", $"path='{file.Path}'");
            await _main.FileService.HandleMissingSongFileAsync(file);
            return;
        }

        // Abandon stale load work if a newer request won while initialization was running.
        if (epoch != _loadEpoch || !ReferenceEquals(Queue.OpenedFile, file))
        {
            Logger.LogStep("PLAYBACK_LOAD_STALE_IGNORED", $"title='{file.Title}' | epoch={epoch} | currentEpoch={_loadEpoch}");
            return;
        }

        TrackView.InitializeTracks();
        TrackView.UpdateTrackPlayableNotes();

        await InitializePlayback();

        TrackView.NotifyNoteStatsChanged();

        Controls.NotifyControlProperties();

        _main.SongsView.RefreshCurrentSong();
        _main.QueueView.RefreshCurrentSong();

        Logger.LogStep(
            "PLAYBACK_LOAD_COMPLETED",
            $"title='{file.Title}' | path='{file.Path}' | tracks={TrackView.MidiTracks.Count} | autoPlay={autoPlay}");

        Logger.LogPlayback(
            $"PLAYBACK_LOAD_COMPLETED title='{file.Title}' | path='{file.Path}' | tracks={TrackView.MidiTracks.Count} | autoPlay={autoPlay}");

        _events.Publish(new OpenedFileChangedNotification(file));

        // Only auto-play if this is still the most recent load request
        if (autoPlay && epoch == _loadEpoch && Playback is not null)
        {
            Logger.LogStep("PLAYBACK_LOAD_AUTOPLAY", $"title='{file.Title}'");
            await Controls.PlayPause();
        }
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
            Logger.Log("Unhandled playback file-load exception.");
            Logger.LogException(e);
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

            await using var db = _ioc.Get<PlayerContext>();
            db.Songs.Update(Queue.OpenedFile.Song);
            await db.SaveChangesAsync();
        }

        // Update note statistics
        TrackView.NotifyNoteStatsChanged();

        var wasPlaying = Playback?.IsRunning ?? false;
        SavedPosition = Controls.SongPosition;

        await InitializePlayback();

        if (wasPlaying && Playback is not null)
            Playback.Start();
    }

    public async void Handle(MergeNotesNotification message)
    {
        var wasPlaying = Playback?.IsRunning ?? false;
        SavedPosition = Controls.SongPosition;

        if (!message.Merge && Queue.OpenedFile is MidiFile openedFile)
        {
            await InitializeMidiFileAsync(openedFile);

            // Ignore stale merge notifications after a song switch.
            if (!ReferenceEquals(Queue.OpenedFile, openedFile))
                return;

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
        SavedPosition = Controls.SongPosition;

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

    private static string FormatNoteName(int noteNumber)
    {
        var names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var normalized = Math.Clamp(noteNumber, 0, 127);
        var pitch = names[normalized % 12];
        var octave = (normalized / 12) - 1;
        return $"{pitch}{octave}";
    }

    private long GetPlaybackElapsedMs()
    {
        if (_playbackStartedAtUtc == DateTime.MinValue)
            return 0;

        var elapsed = DateTime.UtcNow - _playbackStartedAtUtc;
        return Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
    }

    private void EnsureNoteSongContextLogged()
    {
        if (_loggedSongContextForNotes)
            return;

        _loggedSongContextForNotes = true;

        var opened = Queue.OpenedFile;
        var songTitle = opened?.Title ?? "<none>";
        var songPath = opened?.Path ?? "<none>";
        var transpose = SongSettings.Transpose?.Key.ToString() ?? "Ignore";
        var keyOffset = SongSettings.GetEffectiveKeyOffset(opened?.Song);
        var layout = InstrumentPage.SelectedLayout.Key;
        var instrument = InstrumentPage.SelectedInstrument.Key;

        var header =
            $"SONG title='{songTitle}' | path='{songPath}' | instrument={instrument} | layout={layout} | keyOffset={keyOffset} | transpose={transpose}";

        Logger.LogInputOutput(header);
        Logger.LogScheduler(header);
    }

    private void LogSchedulerSample(int sourceNote)
    {
        EnsureNoteSongContextLogged();

        var tempoMap = Queue.OpenedFile?.OriginalTempoMap;
        if (tempoMap is null)
            return;

        var scheduledMetric = TimeConverter.ConvertTo<MetricTimeSpan>(_scheduledEventTicks, tempoMap);
        var scheduledMs = (long)Math.Round(scheduledMetric.TotalMicroseconds / 1000.0);
        var actualMs = GetPlaybackElapsedMs();
        var drift = actualMs - scheduledMs;

        Logger.LogScheduler(
            $"[{actualMs}ms] Note {FormatNoteName(sourceNote)} scheduled={scheduledMs}ms actual={actualMs}ms drift={(drift >= 0 ? "+" : string.Empty)}{drift}ms");
    }

    private void LogNoteInputOutput(string mode, NoteEvent noteEvent, int sourceNote, int outputNote, bool hasMappedKey, WindowsInput.Native.VirtualKeyCode mappedKey)
    {
        EnsureNoteSongContextLogged();

        var keyName = hasMappedKey ? mappedKey.ToString() : "<unmapped>";
        var source = FormatNoteName(sourceNote);
        var output = FormatNoteName(outputNote);
        var eventType = noteEvent.EventType == MidiEventType.NoteOn && noteEvent.Velocity > 0
            ? "NoteOn"
            : "NoteOff";

        if (eventType == "NoteOn")
        {
            _activeNotes[outputNote] = (sourceNote, outputNote, keyName, noteEvent.Velocity, GetPlaybackElapsedMs());
            Logger.LogInputOutput($"{eventType} {source} -> {output} Key={keyName} Vel={noteEvent.Velocity} mode={mode}");

            if (hasMappedKey)
            {
                Logger.LogMapping($"MAP {source} -> {output} key={keyName} instrument={InstrumentPage.SelectedInstrument.Key} layout={InstrumentPage.SelectedLayout.Key}");
            }
            else
            {
                Logger.LogMapping($"MAP_MISS {source} -> {output} instrument={InstrumentPage.SelectedInstrument.Key} layout={InstrumentPage.SelectedLayout.Key}");
            }

            return;
        }

        if (_activeNotes.TryGetValue(outputNote, out var active))
        {
            _activeNotes.Remove(outputNote);
            var pressLength = Math.Max(0, GetPlaybackElapsedMs() - active.StartMs);
            Logger.LogInputOutput($"[{pressLength}ms] {FormatNoteName(active.SourceNote)} -> {FormatNoteName(active.OutputNote)} Key={active.KeyName} Vel={active.Velocity} mode={mode}");
            return;
        }

        Logger.LogInputOutput($"{eventType} {source} -> {output} Key={keyName} Vel={noteEvent.Velocity} mode={mode}");
    }

    private static void LogPerformanceSnapshot(string reason)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var workingSetMb = process.WorkingSet64 / (1024d * 1024d);
            var privateMb = process.PrivateMemorySize64 / (1024d * 1024d);
            Logger.LogPerformance($"PERF reason={reason} | wsMB={workingSetMb:0.0} | privateMB={privateMb:0.0} | threads={process.Threads.Count}");
        }
        catch
        {
            // Best effort only.
        }
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
