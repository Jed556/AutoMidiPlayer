using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using JetBrains.Annotations;
using Stylet;
using StyletIoC;

namespace AutoMidiPlayer.WPF.ViewModels;

public class InstrumentViewModel : Screen, IHandle<MidiFile>
{
    private static readonly Settings Settings = Settings.Default;
    private readonly IEventAggregator _events;
    private readonly IContainer _ioc;
    private readonly MainWindowViewModel _main;
    private bool _isUpdatingFromSong;

    public InstrumentViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

        // Initialize instrument from settings
        SelectedInstrument = Keyboard.InstrumentNames
            .FirstOrDefault(i => (int)i.Key == Settings.SelectedInstrument);

        // Initialize layout from settings
        SelectedLayout = Keyboard.LayoutNames
            .FirstOrDefault(l => (int)l.Key == Settings.SelectedLayout);

        // Initialize note settings to defaults (will be updated when song is loaded)
        MergeNotes = false;
        MergeMilliseconds = 100;
        HoldNotes = false;
    }

    protected override void OnActivate()
    {
        base.OnActivate();
        UpdateFromCurrentSong();
        NotifyOfPropertyChange(nameof(HasSongOpen));
    }

    /// <summary>
    /// Handle when a new song is opened - update UI to reflect song's settings
    /// </summary>
    public void Handle(MidiFile message)
    {
        UpdateFromCurrentSong();
        NotifyOfPropertyChange(nameof(HasSongOpen));
    }

    /// <summary>
    /// Updates the UI to reflect the current song's settings
    /// </summary>
    public void UpdateFromCurrentSong()
    {
        var song = _main.QueueView.OpenedFile?.Song;
        if (song == null)
        {
            // No song open - use defaults
            _isUpdatingFromSong = true;
            MergeNotes = false;
            MergeMilliseconds = 100;
            HoldNotes = false;
            _isUpdatingFromSong = false;
            return;
        }

        _isUpdatingFromSong = true;
        MergeNotes = song.MergeNotes ?? false;
        MergeMilliseconds = song.MergeMilliseconds ?? 100;
        HoldNotes = song.HoldNotes ?? false;
        _isUpdatingFromSong = false;
    }

    public KeyValuePair<Keyboard.Instrument, string> SelectedInstrument { get; set; }

    public KeyValuePair<Keyboard.Layout, string> SelectedLayout { get; set; }

    public bool MergeNotes { get; set; }

    public uint MergeMilliseconds { get; set; }

    public bool HoldNotes { get; set; }

    public bool HasSongOpen => _main.QueueView.OpenedFile != null;

    public bool CanChangeTime => PlayTimerToken is null;

    public bool CanStartStopTimer => DateTime - DateTime.Now > TimeSpan.Zero;

    [UsedImplicitly] public CancellationTokenSource? PlayTimerToken { get; private set; }

    public DateTime DateTime { get; set; } = DateTime.Now;

    public string TimerText => CanChangeTime ? "Start" : "Stop";

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
    public void SetTimeToNow() => DateTime = DateTime.Now;

    [UsedImplicitly]
    private void OnSelectedInstrumentChanged()
    {
        var instrument = (int)SelectedInstrument.Key;
        Settings.Modify(s => s.SelectedInstrument = instrument);
        _events.Publish(this);
    }

    [UsedImplicitly]
    private void OnSelectedLayoutChanged()
    {
        var layout = (int)SelectedLayout.Key;
        Settings.Modify(s => s.SelectedLayout = layout);
        _events.Publish(this);
    }

    [UsedImplicitly]
    private async void OnMergeNotesChanged()
    {
        if (_isUpdatingFromSong) return;

        var song = _main.QueueView.OpenedFile?.Song;
        if (song != null)
        {
            song.MergeNotes = MergeNotes;
            await SaveCurrentSong();
        }
        _events.Publish(new MergeNotesNotification(MergeNotes));
    }

    [UsedImplicitly]
    private async void OnMergeMillisecondsChanged()
    {
        if (_isUpdatingFromSong) return;

        var song = _main.QueueView.OpenedFile?.Song;
        if (song != null)
        {
            song.MergeMilliseconds = MergeMilliseconds;
            await SaveCurrentSong();
        }
        _events.Publish(this);
    }

    [UsedImplicitly]
    private async void OnHoldNotesChanged()
    {
        if (_isUpdatingFromSong) return;

        var song = _main.QueueView.OpenedFile?.Song;
        if (song != null)
        {
            song.HoldNotes = HoldNotes;
            await SaveCurrentSong();
        }
    }

    private async Task SaveCurrentSong()
    {
        var song = _main.QueueView.OpenedFile?.Song;
        if (song == null) return;

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Update(song);
        await db.SaveChangesAsync();
    }
}
