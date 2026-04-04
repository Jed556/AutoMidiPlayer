using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.Media;
using Windows.Media.Playback;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Core.Games;
using AutoMidiPlayer.WPF.ViewModels;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;
using WinMediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Service responsible for user-facing playback controls:
/// play/pause, next/previous, slider, listen mode, and UI state.
/// Backend engine logic lives in <see cref="PlaybackEngineService"/>.
/// </summary>
public class PlaybackControlsService : PropertyChangedBase, IHandle<PlayTimerNotification>, IHandle<ListenModeChangedNotification>
{
    #region Fields

    private static readonly TimeSpan ListenModeFocusTransitionGrace = TimeSpan.FromMilliseconds(600);

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly WinMediaPlayer? _player;

    private bool _ignoreSliderChange;
    private TimeSpan _songPosition;
    private int _savePositionCounter;

    #endregion

    #region Constructor

    public PlaybackControlsService(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;

        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

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
    }

    #endregion

    #region Properties

    private PlaybackEngineService Engine => _main.PlaybackEngine;

    public bool IsPlaying => Engine.Playback?.IsRunning ?? false;

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

            return Engine.Playback is not null
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

    public bool IsListenModeEnabled => Settings.UseSpeakers;

    public string ListenModeTooltip => IsListenModeEnabled
        ? "Listen Mode (Speakers): On"
        : "Listen Mode (Speakers): Off";

    private QueueViewModel Queue => _main.QueueView;
    private TrackViewModel TrackView => _main.TrackView;
    private SongService SongSettings => _main.SongSettings;

    private static string PauseIcon => "\xEDB4";
    private static string PlayIcon => "\xF5B0";

    private string CurrentSongLabel => Queue.OpenedFile is null
        ? "<none>"
        : $"{Queue.OpenedFile.Title} ({Queue.OpenedFile.Path})";

    private MusicDisplayProperties? Display =>
        _player?.SystemMediaTransportControls.DisplayUpdater.MusicProperties;

    private SystemMediaTransportControls? Controls =>
        _player?.SystemMediaTransportControls;

    #endregion

    #region Events

    public event EventHandler? SongPositionChanged;
    public event EventHandler? PlaybackStateChanged;

    #endregion

    #region Playback Controls

    public void SetSavedPosition(double positionSeconds)
    {
        Engine.SavedPosition = positionSeconds;
        SongPosition = positionSeconds;
    }

    public async Task PlayPause()
    {
        if (Engine.Playback is null)
            await Engine.InitializePlayback();

        var playback = Engine.Playback;
        if (playback is null)
            return;

        CrashLogger.LogStep(
            "PLAY_PAUSE_CLICK",
            $"isRunning={playback.IsRunning} | song='{CurrentSongLabel}' | position={CurrentTime:mm\\:ss}");

        try
        {
            if (playback.IsRunning)
            {
                playback.Stop();
                Queue.SaveCurrentSong(CurrentTime.TotalSeconds);
                CrashLogger.LogStep("PLAYBACK_PAUSED", $"song='{CurrentSongLabel}' | position={CurrentTime:mm\\:ss}");
            }
            else
            {
                var time = new MetricTimeSpan(CurrentTime);
                playback.PlaybackStart = time;
                playback.MoveToTime(time);

                var started = await Engine.StartPlayback(playback);
                CrashLogger.LogStep(
                    "PLAYBACK_START_REQUEST",
                    $"started={started} | song='{CurrentSongLabel}' | position={CurrentTime:mm\\:ss}");
            }
        }
        catch (ObjectDisposedException) { }
    }

    public void CloseFile()
    {
        Engine.ResetPlayback();

        TrackView.MidiTracks.Clear();
        MoveSlider(TimeSpan.Zero);

        Queue.OpenedFile = null;
        SongSettings.ClearSettings();
        _main.InstrumentView.UpdateFromCurrentSong();
    }

    /// <summary>
    /// Go to the next song.
    /// </summary>
    /// <param name="userInitiated">True if user clicked Next button, false if auto-triggered by song finish</param>
    public async Task Next(bool userInitiated = true)
    {
        CrashLogger.LogStep(
            "NEXT_CLICK",
            $"userInitiated={userInitiated} | current='{CurrentSongLabel}'");

        var next = Queue.Next(userInitiated);
        if (next is null)
        {
            var pb = Engine.Playback;
            if (pb is not null)
            {
                try { pb.PlaybackStart = null; pb.MoveToStart(); }
                catch (ObjectDisposedException) { }
            }

            MoveSlider(TimeSpan.Zero);
            UpdateButtons();
            CrashLogger.LogStep("NEXT_NO_TARGET", "Reached end of queue.");
            return;
        }

        CrashLogger.LogStep("NEXT_TARGET", $"song='{next.Title}' | path='{next.Path}'");
        await Engine.LoadFileAsync(next, autoPlay: true);
    }

    public async void Previous()
    {
        CrashLogger.LogStep("PREVIOUS_CLICK", $"current='{CurrentSongLabel}' | position={CurrentTime:mm\\:ss}");

        if (CurrentTime > TimeSpan.FromSeconds(3))
        {
            var pb = Engine.Playback;
            if (pb is not null)
            {
                try { pb.Stop(); pb.MoveToStart(); }
                catch (ObjectDisposedException) { }
            }

            MoveSlider(TimeSpan.Zero);

            pb = Engine.Playback;
            if (pb is not null)
                await Engine.StartPlayback(pb);

            CrashLogger.LogStep("PREVIOUS_RESTART_CURRENT", $"song='{CurrentSongLabel}'");
        }
        else
        {
            while (Queue.History.Count > 1)
            {
                Queue.History.Pop();
                var previous = Queue.History.Pop();

                if (Queue.GetPlaylist().Any(track => track.Song.Id == previous.Song.Id))
                {
                    CrashLogger.LogStep("PREVIOUS_TARGET", $"song='{previous.Title}' | path='{previous.Path}'");
                    await Engine.LoadFileAsync(previous, autoPlay: true);
                    return;
                }
            }

            CrashLogger.LogStep("PREVIOUS_NO_TARGET", "No previous song available in current playlist history.");
        }
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

        var pb = Engine.Playback;
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

    public void OnSongTick(object? sender, PlaybackCurrentTimeChangedEventArgs e)
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

    public void MoveSlider(TimeSpan value)
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

    #region Listen Mode

    public bool SetListenMode(bool enabled, bool pausePlaybackOnChange = true)
    {
        if (Settings.UseSpeakers == enabled)
            return false;

        CrashLogger.LogStep(
            "LISTEN_MODE_SET",
            $"enabled={enabled} | pausePlaybackOnChange={pausePlaybackOnChange} | song='{CurrentSongLabel}'");

        Settings.Modify(s => s.UseSpeakers = enabled);

        var pausedPlayback = false;
        var pb = Engine.Playback;
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
        NotifyListenModeProperties();

        CrashLogger.LogStep("LISTEN_MODE_SET_COMPLETED", $"enabled={enabled} | pausedPlayback={pausedPlayback}");
        return pausedPlayback;
    }

    public void ToggleListenMode()
    {
        var enableListenMode = !IsListenModeEnabled;
        CrashLogger.LogStep("LISTEN_MODE_TOGGLE_CLICK", $"targetEnabled={enableListenMode} | song='{CurrentSongLabel}'");

        // If user turns Listen Mode off while currently playing and no game is running,
        // pause immediately so auto-enable doesn't flip it back on mid-playback.
        if (!enableListenMode && IsPlaying)
        {
            var selectedGame = _main.SelectedGame?.Definition;
            var isGameRunning = selectedGame is not null && GameRegistry.IsGameRunning(selectedGame);

            if (!isGameRunning)
            {
                var pausedPlayback = SetListenMode(false, pausePlaybackOnChange: true);
                if (pausedPlayback)
                {
                    var selectedGameName = _main.SelectedGame?.Definition.DisplayName ?? "Selected game";
                    var gameLabel = $"{selectedGameName} is not running";
                    _main.ShowPlaybackStoppedGameNotRunningToast(gameLabel);
                }

                return;
            }
        }

        SetListenMode(enableListenMode, pausePlaybackOnChange: false);

        if (!enableListenMode && IsPlaying)
        {
            var selectedGame = _main.SelectedGame?.Definition;
            var isGameRunning = selectedGame is not null && GameRegistry.IsGameRunning(selectedGame);
            if (isGameRunning)
            {
                // Give foreground switching a brief grace period to avoid transient
                // false focus-loss pauses while Windows applies the focus change.
                Engine.SuppressFocusLossPause(ListenModeFocusTransitionGrace);
                WindowHelper.EnsureGameOnTop();
            }
        }
    }

    #endregion

    #region UI Updates

    public void NotifyPlaybackStateChanged()
    {
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyControlProperties()
    {
        NotifyOfPropertyChange(nameof(MaximumTime));
        NotifyOfPropertyChange(nameof(CanHitPlayPause));
        NotifyOfPropertyChange(nameof(CanHitNext));
        NotifyOfPropertyChange(nameof(CanHitPrevious));
        NotifyListenModeProperties();
    }

    private void NotifyListenModeProperties()
    {
        NotifyOfPropertyChange(nameof(IsListenModeEnabled));
        NotifyOfPropertyChange(nameof(ListenModeTooltip));
    }

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
                Engine.Playback is null ? MediaPlaybackStatus.Stopped :
                Engine.Playback.IsRunning ? MediaPlaybackStatus.Playing :
                MediaPlaybackStatus.Paused;

            var file = Queue.OpenedFile;
            if (file is not null)
            {
                Display.Title = file.Title;
                Display.Artist = string.IsNullOrWhiteSpace(file.Artist) ? "Unknown artist" : file.Artist;
            }

            Controls.DisplayUpdater.Update();
        }
    }

    #endregion

    #region Event Handlers

    public async void Handle(PlayTimerNotification message)
    {
        if (IsPlaying == message.ShouldPlay)
            return;

        if (message.ShouldPlay && !CanHitPlayPause)
            return;

        await PlayPause();
    }

    public void Handle(ListenModeChangedNotification message)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Handle(message));
            return;
        }

        NotifyListenModeProperties();
    }

    #endregion
}
