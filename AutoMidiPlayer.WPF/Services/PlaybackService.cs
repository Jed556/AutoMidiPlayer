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
using AutoMidiPlayer.WPF.Core.Instruments;
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

    /// <summary>
    /// The shared synth output device ("Microsoft GS Wavetable Synth"). Exposed so the
    /// Online preview player can render through the SAME device — the synth allows only one
    /// open handle, so a separate device would fail with MIDIERR_ALLOCATED.
    /// </summary>
    public OutputDevice? PreviewSynthDevice => _speakers;

    private int _loadEpoch;
    private DateTime _suppressFocusLossUntilUtc = DateTime.MinValue;
    private DateTime _playbackStartedAtUtc = DateTime.MinValue;
    private long _scheduledEventTicks;
    private bool _loggedSongContextForNotes;
    private bool _pedalStateNeedsResync;
    private readonly Dictionary<int, (int SourceNote, int OutputNote, string KeyName, int Velocity, long StartMs)> _activeNotes = new();
    private readonly Dictionary<(int Channel, int NoteNumber), int> _activeSpeakerNotes = new();
    private readonly HashSet<(int Note, string Layout, string Instrument)> _activeGameNotes = new();
    private readonly Dictionary<ChordPadEventKey, DetectedChordPad> _chordPadsByEvent = new();
    private readonly List<DetectedChordPad> _detectedChordPads = new();

    private sealed class DetectedChordPad(ChordPadConfig pad)
    {
        public ChordPadConfig Pad { get; } = pad;

        public bool WasTriggered { get; set; }
    }

    private readonly record struct ChordPadEventKey(
        long Time,
        MidiEventType EventType,
        int NoteNumber,
        int Channel);

    private class PedalState
    {
        public string Name { get; }
        public int CcNumber { get; }
        public HashSet<FourBitNumber> ChannelsDown { get; } = new();
        public bool IsCurrentlyHeldInGame { get; set; }

        public PedalState(string name, int ccNumber)
        {
            Name = name;
            CcNumber = ccNumber;
        }

        public void Clear()
        {
            ChannelsDown.Clear();
            // Do not clear IsCurrentlyHeldInGame here, as it tracks physical state!
        }
    }

    private readonly PedalState _sustain = new("Sustain", 64);
    private readonly PedalState _sostenuto = new("Sostenuto", 66);
    private readonly PedalState _unaCorda = new("Una Corda", 67);
    private PedalState[] AllPedals => new[] { _sustain, _sostenuto, _unaCorda };

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
            _ = AudioDeviceUnavailableView.ShowInitializationErrorAsync(e);
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
        ReleaseSustainIfActive();
        SilenceSpeakers();
        ClearChordPadDetection();

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
        if (midi is null || tempoMap is null)
        {
            ClearChordPadDetection();
            Controls.UpdateButtons();
            return Task.CompletedTask;
        }

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
            ClearChordPadDetection();
            Playback = null;
            Controls.UpdateButtons();
            return Task.CompletedTask;
        }

        BuildChordPadEventMap(tracksToPlay, tempoMap);

        var playback = tracksToPlay.GetPlayback(tempoMap);

        Playback = playback;
        ApplyEffectivePlaybackSpeed();
        playback.InterruptNotesOnStop = true;
        _scheduledEventTicks = 0;
        _activeNotes.Clear();
        _activeSpeakerNotes.Clear();
        _activeGameNotes.Clear();
        foreach (var pedal in AllPedals) pedal.Clear();
        _loggedSongContextForNotes = false;
        playback.Finished += (_, _) =>
        {
            // DryWetMidi does NOT fire the Stopped event when playback finishes
            // naturally — only Finished is raised. Stop the time watcher here so
            // it doesn't keep overriding the slider position after the song ends.
            _timeWatcher.Stop();
            ReleaseSustainIfActive();
            SilenceSpeakers();

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
            _activeSpeakerNotes.Clear();
            _activeGameNotes.Clear();
            _scheduledEventTicks = TimeConverter.ConvertFrom(
                playback.GetCurrentTime<MetricTimeSpan>(),
                playback.TempoMap);
            foreach (var chordPad in _detectedChordPads)
                chordPad.WasTriggered = false;
            foreach (var pedal in AllPedals) pedal.Clear();
            _loggedSongContextForNotes = false;

            var midi = Queue.OpenedFile?.Midi;
            var tempoMap = Queue.OpenedFile?.OriginalTempoMap;
            if (midi != null && tempoMap != null)
            {
                try
                {
                    var currentTime = new MetricTimeSpan(Controls.CurrentTime);
                    var ccEvents = midi.GetTimedEvents()
                        .Where(e => e.TimeAs<MetricTimeSpan>(tempoMap) <= currentTime)
                        .Select(e => e.Event)
                        .OfType<ControlChangeEvent>()
                        .Where(e => e.ControlNumber == 64 || e.ControlNumber == 66 || e.ControlNumber == 67);

                    foreach (var ccEvent in ccEvents)
                    {
                        var pedal = AllPedals.FirstOrDefault(p => p.CcNumber == ccEvent.ControlNumber);
                        if (pedal != null)
                        {
                            if (ccEvent.ControlValue >= 64)
                                pedal.ChannelsDown.Add(ccEvent.Channel);
                            else
                                pedal.ChannelsDown.Remove(ccEvent.Channel);
                        }
                    }

                    // Flag that we need a resync. This will be processed by ReconcilePedalStates
                    // on the very first note event that occurs while the game is focused!
                    // This reliably clears any stuck keys caused by the user alt-tabbing while holding Space.
                    _pedalStateNeedsResync = true;

                    ReconcilePedalStates();
                    SyncPedalStatesToUI();
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Failed to reconstruct pedal state during playback start/seek.");
                }
            }

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
            ReleaseSustainIfActive();
            SilenceSpeakers();
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

        ReconcilePedalStates();
        SyncPedalStatesToUI();

        if (e.Event is ControlChangeEvent ccEvent && (ccEvent.ControlNumber == 64 || ccEvent.ControlNumber == 66 || ccEvent.ControlNumber == 67))
        {
            HandlePedalEvent(ccEvent);
            return;
        }

        switch (e.Event)
        {
            case NoteEvent noteEvent:
                PlayNote(noteEvent);
                break;
        }
    }

    private void HandlePedalEvent(ControlChangeEvent ccEvent)
    {
        var pedal = AllPedals.FirstOrDefault(p => p.CcNumber == ccEvent.ControlNumber);
        if (pedal == null) return;

        var layout = InstrumentPage.SelectedLayout.Key;
        var instrument = InstrumentPage.SelectedInstrument.Key;
        var layoutConfig = Keyboard.GetLayoutConfig(layout, instrument);
        if (layoutConfig == null) return;

        var isPedalDown = ccEvent.ControlValue >= 64;

        // Track CC state unconditionally so UI pedal indicators always work
        if (isPedalDown)
            pedal.ChannelsDown.Add(ccEvent.Channel);
        else
            pedal.ChannelsDown.Remove(ccEvent.Channel);

        // Forward CC to speakers when in listen mode or fallback
        if (Settings.UseSpeakers)
        {
            _speakers?.SendEvent(ccEvent);

            if (ShouldLogPlayedNotes)
                Logger.LogInputOutput($"{(isPedalDown ? $"{pedal.Name.ToUpper()}_DOWN" : $"{pedal.Name.ToUpper()}_UP")} mode=speakers");
            
            SyncPedalStatesToUI();
            return;
        }

        var selectedGame = _main.SelectedGame?.Definition;
        var isGameRunning = selectedGame is not null && GameRegistry.IsGameRunning(selectedGame);

        if (!isGameRunning)
        {
            // Auto-listen fallback: forward to speakers if available
            if (Settings.AutoEnableListenMode)
                _speakers?.SendEvent(ccEvent);
            
            SyncPedalStatesToUI();
            return;
        }

        VirtualKeyCode? pedalKey = pedal.CcNumber switch
        {
            64 => layoutConfig.SustainKey,
            66 => layoutConfig.SostenutoKey,
            67 => layoutConfig.UnaCordaKey,
            _ => null
        };

        if (pedalKey is null)
        {
            SyncPedalStatesToUI();
            return;
        }

        ReconcilePedalStates();
        SyncPedalStatesToUI();
    }

    private void ReconcilePedalStates()
    {
        var layout = InstrumentPage.SelectedLayout.Key;
        var instrument = InstrumentPage.SelectedInstrument.Key;
        var layoutConfig = Keyboard.GetLayoutConfig(layout, instrument);
        if (layoutConfig == null || Settings.UseSpeakers)
            return;

        var isFocused = KeyboardPlayer.UseWindowMessage || WindowHelper.IsGameFocused();
        if (!isFocused)
            return;

        if (_pedalStateNeedsResync)
        {
            foreach (var pedal in AllPedals)
            {
                VirtualKeyCode? pKey = pedal.CcNumber switch
                {
                    64 => layoutConfig.SustainKey,
                    66 => layoutConfig.SostenutoKey,
                    67 => layoutConfig.UnaCordaKey,
                    _ => null
                };
                if (pKey != null)
                {
                    var shouldBeDown = pedal.ChannelsDown.Count > 0;
                    pedal.IsCurrentlyHeldInGame = !shouldBeDown;
                }
            }
            _pedalStateNeedsResync = false;
        }

        foreach (var pedal in AllPedals)
        {
            VirtualKeyCode? pedalKey = pedal.CcNumber switch
            {
                64 => layoutConfig.SustainKey,
                66 => layoutConfig.SostenutoKey,
                67 => layoutConfig.UnaCordaKey,
                _ => null
            };

            if (pedalKey is null) continue;

            var shouldBeDown = InstrumentPage.EnableSustainForInstrument && pedal.ChannelsDown.Count > 0;
            if (shouldBeDown == pedal.IsCurrentlyHeldInGame)
                continue;

            if (shouldBeDown && !pedal.IsCurrentlyHeldInGame)
            {
                pedal.IsCurrentlyHeldInGame = true;
                KeyboardPlayer.PedalDown(pedalKey.Value);
                if (ShouldLogPlayedNotes)
                    Logger.LogInputOutput($"{pedal.Name.ToUpper()}_DOWN (reconcile) key={pedalKey.Value}");
            }
            else if (!shouldBeDown && pedal.IsCurrentlyHeldInGame)
            {
                pedal.IsCurrentlyHeldInGame = false;
                KeyboardPlayer.PedalUp(pedalKey.Value);
                if (ShouldLogPlayedNotes)
                    Logger.LogInputOutput($"{pedal.Name.ToUpper()}_UP (reconcile) key={pedalKey.Value}");
            }
        }
    }

    private void SyncPedalStatesToUI()
    {
        if (Controls == null) return;
        Controls.IsSustainHeld = _sustain.IsCurrentlyHeldInGame || _sustain.ChannelsDown.Count > 0;
        Controls.IsSostenutoHeld = _sostenuto.IsCurrentlyHeldInGame || _sostenuto.ChannelsDown.Count > 0;
        Controls.IsUnaCordaHeld = _unaCorda.IsCurrentlyHeldInGame || _unaCorda.ChannelsDown.Count > 0;
    }

    private void ReleaseSustainIfActive()
    {
        var layout = InstrumentPage.SelectedLayout.Key;
        var instrument = InstrumentPage.SelectedInstrument.Key;
        var layoutConfig = Keyboard.GetLayoutConfig(layout, instrument);

        foreach (var pedal in AllPedals)
        {
            pedal.ChannelsDown.Clear();

            if (layoutConfig != null)
            {
                VirtualKeyCode? pedalKey = pedal.CcNumber switch
                {
                    64 => layoutConfig.SustainKey,
                    66 => layoutConfig.SostenutoKey,
                    67 => layoutConfig.UnaCordaKey,
                    _ => null
                };
                
                if (pedalKey is not null && pedal.IsCurrentlyHeldInGame)
                {
                    if (KeyboardPlayer.UseWindowMessage || WindowHelper.IsGameFocused())
                    {
                        KeyboardPlayer.PedalUp(pedalKey.Value);
                    }
                }
            }

            pedal.IsCurrentlyHeldInGame = false;
        }
        SyncPedalStatesToUI();

        foreach (var activeNote in _activeGameNotes)
        {
            KeyboardPlayer.NoteUp(activeNote.Note, activeNote.Layout, activeNote.Instrument);
        }
        _activeGameNotes.Clear();
    }

    private void SilenceSpeakers()
    {
        if (_speakers is null)
            return;

        foreach (var activeNote in _activeSpeakerNotes)
        {
            var (channel, noteNumber) = activeNote.Key;
            var noteOff = new NoteOffEvent(new SevenBitNumber((byte)noteNumber), new SevenBitNumber(0))
            {
                Channel = new FourBitNumber((byte)channel)
            };
            _speakers.SendEvent(noteOff);
        }
        _activeSpeakerNotes.Clear();

        for (byte i = 0; i < 16; i++)
        {
            var channel = new FourBitNumber(i);
            _speakers.SendEvent(new ControlChangeEvent(new SevenBitNumber(123), new SevenBitNumber(0)) { Channel = channel });
            _speakers.SendEvent(new ControlChangeEvent(new SevenBitNumber(64), new SevenBitNumber(0)) { Channel = channel });
            _speakers.SendEvent(new ControlChangeEvent(new SevenBitNumber(66), new SevenBitNumber(0)) { Channel = channel });
            _speakers.SendEvent(new ControlChangeEvent(new SevenBitNumber(67), new SevenBitNumber(0)) { Channel = channel });
        }
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
            var noteForListen = noteForKeyboard; // Listen mode plays the same note as keyboard output
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
                    NotePlayed?.Invoke(this, new NotePlayedEventArgs(sourceNote, Controls.CurrentTime.Ticks / 10));

                if (ShouldLogPlayedNotes)
                    LogNoteInputOutput("speakers", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                SendToSpeakers(noteEvent, noteForListen);
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
                    NotePlayed?.Invoke(this, new NotePlayedEventArgs(sourceNote, Controls.CurrentTime.Ticks / 10));

                if (ShouldLogPlayedNotes)
                    LogNoteInputOutput("auto-listen", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                SendToSpeakers(noteEvent, noteForListen);
                return;
            }

            if (!WindowHelper.IsGameFocused())
            {
                if (DateTime.UtcNow <= _suppressFocusLossUntilUtc)
                    return;

                HandleGameFocusLoss();
                return;
            }

            if (TryPlayChordPad(noteEvent, sourceNote, layout, instrument, noteForKeyboard))
                return;

            var useHoldNotes = Queue.OpenedFile?.Song.HoldNotes ?? false;

            switch (noteEvent.EventType)
            {
                case MidiEventType.NoteOff:
                    if (ShouldLogPlayedNotes)
                        LogNoteInputOutput("game", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                    _activeGameNotes.Remove((noteForKeyboard, layout, instrument));
                    KeyboardPlayer.NoteUp(noteForKeyboard, layout, instrument);
                    break;
                case MidiEventType.NoteOn when noteEvent.Velocity <= 0:
                    if (ShouldLogPlayedNotes)
                        LogNoteInputOutput("game", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                    _activeGameNotes.Remove((noteForKeyboard, layout, instrument));
                    // Also fire NoteUp here since velocity 0 is a NoteOff equivalent
                    KeyboardPlayer.NoteUp(noteForKeyboard, layout, instrument);
                    return;
                case MidiEventType.NoteOn:
                    if (!hasMappedKey)
                        return;

                    NotePlayed?.Invoke(this, new NotePlayedEventArgs(sourceNote, Controls.CurrentTime.Ticks / 10));

                    if (ShouldLogPlayedNotes)
                        LogNoteInputOutput("game", noteEvent, sourceNote, noteForKeyboard, hasMappedKey, mappedKey);

                    if (useHoldNotes)
                    {
                        _activeGameNotes.Add((noteForKeyboard, layout, instrument));
                        KeyboardPlayer.NoteDown(noteForKeyboard, layout, instrument);
                    }
                    else
                    {
                        KeyboardPlayer.PlayNote(noteForKeyboard, layout, instrument);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    private bool TryPlayChordPad(
        NoteEvent noteEvent,
        int sourceNote,
        string layout,
        string instrument,
        int noteForKeyboard)
    {
        var eventKey = new ChordPadEventKey(
            _scheduledEventTicks,
            noteEvent.EventType,
            (int)noteEvent.NoteNumber,
            (int)noteEvent.Channel);
        if (!_chordPadsByEvent.TryGetValue(eventKey, out var detectedChordPad))
            return false;

        var isNoteOn = noteEvent.EventType == MidiEventType.NoteOn && noteEvent.Velocity > 0;
        if (!isNoteOn)
            return true;

        // Keep the track view's glow state representative of the source MIDI even though the
        // output is a single game chord-pad key.
        NotePlayed?.Invoke(this, new NotePlayedEventArgs(sourceNote, Controls.CurrentTime.Ticks / 10));

        if (detectedChordPad.WasTriggered)
            return true;

        detectedChordPad.WasTriggered = true;
        KeyboardPlayer.PlayChordPad(detectedChordPad.Pad.KeyIndex, layout, instrument);

        if (ShouldLogPlayedNotes)
            Logger.LogInputOutput(
                $"CHORD_PAD name={detectedChordPad.Pad.Name} source={sourceNote} output={noteForKeyboard} keyIndex={detectedChordPad.Pad.KeyIndex}");

        return true;
    }

    private void BuildChordPadEventMap(IEnumerable<TrackChunk> trackChunks, TempoMap tempoMap)
    {
        ClearChordPadDetection();

        var song = Queue?.OpenedFile?.Song;
        var instrument = InstrumentPage.SelectedInstrument.Key;
        if (song?.DetectChordPads != true || string.IsNullOrWhiteSpace(instrument))
            return;

        var instrumentConfig = Keyboard.GetInstrumentConfig(instrument);
        if (instrumentConfig.ChordPads.Count == 0)
            return;

        var toleranceMilliseconds = Math.Min(song.ChordDetectionMilliseconds ?? 30, 1000u);
        var chordDetectionSettings = new ChordDetectionSettings
        {
            // Genshin's currently supported pads are triads or a dominant seventh. A lower
            // value would incorrectly turn two-note harmony into a pad press.
            NotesMinCount = 3,
            NotesTolerance = TimeConverter.ConvertFrom(
                new MetricTimeSpan(0, 0, 0, (int)toleranceMilliseconds),
                tempoMap)
        };

        // GetChords(IEnumerable<TrackChunk>) processes every track independently. Build a temporary,
        // time-ordered note stream so a C/E/G chord split over three MIDI tracks still reaches the
        // game's single C chord pad. Event keys below use the original absolute times, so cloning
        // this stream does not affect playback or event matching.
        var chordTrack = CreateChordDetectionTrack(trackChunks);
        foreach (var chord in chordTrack.GetChords(chordDetectionSettings))
        {
            var notes = chord.Notes.ToArray();
            var pitchClasses = notes
                .Select(note => Mod12(ApplyNoteSettings(instrument, (int)note.NoteNumber)))
                .ToHashSet();

            var chordPad = instrumentConfig.ChordPads
                .FirstOrDefault(pad => pad.PitchClasses.SetEquals(pitchClasses));
            if (chordPad is null)
                continue;

            var detectedChordPad = new DetectedChordPad(chordPad);
            var wasMapped = false;

            foreach (var note in notes)
            {
                var timedNoteOnEvent = note.GetTimedNoteOnEvent();
                if (timedNoteOnEvent?.Event is NoteEvent noteOn)
                {
                    _chordPadsByEvent[CreateChordPadEventKey(timedNoteOnEvent.Time, noteOn)] = detectedChordPad;
                    wasMapped = true;
                }

                var timedNoteOffEvent = note.GetTimedNoteOffEvent();
                if (timedNoteOffEvent?.Event is NoteEvent noteOff)
                    _chordPadsByEvent[CreateChordPadEventKey(timedNoteOffEvent.Time, noteOff)] = detectedChordPad;
            }

            if (wasMapped)
                _detectedChordPads.Add(detectedChordPad);
        }
    }

    private void ClearChordPadDetection()
    {
        _chordPadsByEvent.Clear();
        _detectedChordPads.Clear();
    }

    private static int Mod12(int note) => ((note % 12) + 12) % 12;

    private static TrackChunk CreateChordDetectionTrack(IEnumerable<TrackChunk> trackChunks)
    {
        var chordTrack = new TrackChunk();
        var previousTime = 0L;

        foreach (var timedEvent in trackChunks
                     .SelectMany(track => track.GetTimedEvents())
                     .Where(timedEvent => timedEvent.Event is NoteEvent)
                     .OrderBy(timedEvent => timedEvent.Time))
        {
            var eventCopy = timedEvent.Event.Clone();
            eventCopy.DeltaTime = timedEvent.Time - previousTime;
            chordTrack.Events.Add(eventCopy);
            previousTime = timedEvent.Time;
        }

        return chordTrack;
    }

    private static ChordPadEventKey CreateChordPadEventKey(long time, NoteEvent noteEvent) => new(
        time,
        noteEvent.EventType,
        (int)noteEvent.NoteNumber,
        (int)noteEvent.Channel);

    private int ApplyNoteSettings(string instrumentId, int noteId)
    {
        var instrumentKeyCount = Keyboard.GetNotes(instrumentId).Count;
        var threshold = Settings.AutoCorrectThreshold;

        if (threshold > 0 && instrumentKeyCount <= threshold)
        {
            // Auto-correct: apply full base key + relative offset
            noteId += SongSettings.GetEffectiveKeyOffset(Queue.OpenedFile?.Song);
        }
        else
        {
            // Wide-range instrument: only apply the relative user offset (no base key shift)
            noteId += SongSettings.KeyOffset;
        }

        return Settings.TransposeNotes && SongSettings.Transpose is not null
            ? KeyboardPlayer.TransposeNote(instrumentId, ref noteId, SongSettings.Transpose.Value.Key)
            : noteId;
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

    private void SendToSpeakers(NoteEvent source, int noteForListen)
    {
        if (_speakers is null)
            return;

        var outputEvent = CreateOutputNoteEvent(source, noteForListen);
        
        if (outputEvent is NoteOnEvent noteOn && noteOn.Velocity > 0)
        {
            var key = (noteOn.Channel, noteOn.NoteNumber);
            _activeSpeakerNotes[key] = _activeSpeakerNotes.GetValueOrDefault(key) + 1;
            _speakers.SendEvent(outputEvent);
        }
        else if (outputEvent is NoteOffEvent noteOff)
        {
            var key = (noteOff.Channel, noteOff.NoteNumber);
            if (_activeSpeakerNotes.TryGetValue(key, out var count))
            {
                if (count > 1)
                {
                    _activeSpeakerNotes[key] = count - 1;
                }
                else
                {
                    _activeSpeakerNotes.Remove(key);
                    _speakers.SendEvent(outputEvent);
                }
            }
            else
            {
                // Fallback: send it anyway if not tracked
                _speakers.SendEvent(outputEvent);
            }
        }
        else if (outputEvent is NoteOnEvent noteOnZero && noteOnZero.Velocity == 0)
        {
            var key = (noteOnZero.Channel, noteOnZero.NoteNumber);
            if (_activeSpeakerNotes.TryGetValue(key, out var count))
            {
                if (count > 1)
                {
                    _activeSpeakerNotes[key] = count - 1;
                }
                else
                {
                    _activeSpeakerNotes.Remove(key);
                    _speakers.SendEvent(outputEvent);
                }
            }
            else
            {
                _speakers.SendEvent(outputEvent);
            }
        }
        else
        {
            _speakers.SendEvent(outputEvent);
        }
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

        var listenModeEnabled = Settings.UseSpeakers;
        _main.ShowGameInactiveToast(listenModeEnabled);

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
                    _main.ShowPlaybackStoppedGameNotRunningToast();
                }
            }
            catch (ObjectDisposedException) { }
        }
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
                    _main.ShowGameFocusLossToast();
                }
            }
            catch (ObjectDisposedException) { }
        }
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
        _activeSpeakerNotes.Clear();
        foreach (var pedal in AllPedals) pedal.Clear();

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

        // Threshold change may affect whether auto-correction is active
        SongSettings.UpdateAutoCorrectState();

        ReconcilePedalStates();
        SyncPedalStatesToUI();

        var wasPlaying = Playback?.IsRunning ?? false;
        SavedPosition = Controls.SongPosition;

        await InitializePlayback();

        if (wasPlaying && Playback is not null)
            Playback.Start();
    }

    public void Handle(InstrumentViewModel message)
    {
        if (_main.InstrumentView is null || _main.QueueView is null) return;

        TrackView.UpdateTrackPlayableNotes();
        TrackView.NotifyNoteStatsChanged();

        // Instrument change may affect whether auto-correction is active
        // (depends on instrument key count vs. threshold)
        SongSettings.UpdateAutoCorrectState();

        var midi = Queue?.OpenedFile?.Midi;
        var tempoMap = Queue?.OpenedFile?.OriginalTempoMap;
        if (midi is not null && tempoMap is not null)
            BuildChordPadEventMap(midi.GetTrackChunks(), tempoMap);
        else
            ClearChordPadDetection();
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
            $"[{actualMs}ms] Note {MusicConstants.FormatNoteName(sourceNote)} scheduled={scheduledMs}ms actual={actualMs}ms drift={(drift >= 0 ? "+" : string.Empty)}{drift}ms");
    }

    private void LogNoteInputOutput(string mode, NoteEvent noteEvent, int sourceNote, int outputNote, bool hasMappedKey, VirtualKeyCode mappedKey)
    {
        EnsureNoteSongContextLogged();

        var keyName = hasMappedKey ? mappedKey.ToString() : "<unmapped>";
        var source = MusicConstants.FormatNoteName(sourceNote);
        var output = MusicConstants.FormatNoteName(outputNote);
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
            Logger.LogInputOutput($"[{pressLength}ms] {MusicConstants.FormatNoteName(active.SourceNote)} -> {MusicConstants.FormatNoteName(active.OutputNote)} Key={active.KeyName} Vel={active.Velocity} mode={mode}");
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
public class NotePlayedEventArgs(int noteNumber, long currentTimeUs) : EventArgs
{
    public int NoteNumber { get; } = noteNumber;
    public long CurrentTimeUs { get; } = currentTimeUs;
}
