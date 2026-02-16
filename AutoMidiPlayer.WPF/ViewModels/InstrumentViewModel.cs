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
using Melanchall.DryWetMidi.Multimedia;
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
    private InputDevice? _inputDevice;


    public InstrumentViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

        // Initialize selected MIDI input
        SelectedMidiInput = MidiInputs[0];

        SelectedInstrument = Keyboard.GetInstrumentAtIndex(Settings.SelectedInstrument);
        RefreshAvailableLayouts();

        var preferredLayout = Keyboard.GetLayoutAtIndex(Settings.SelectedLayout);
        SelectedLayout = AvailableLayouts.FirstOrDefault(layout => layout.Key == preferredLayout.Key);
        if (SelectedLayout.Equals(default(KeyValuePair<string, string>)) && AvailableLayouts.Count > 0)
            SelectedLayout = AvailableLayouts[0];

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


    public BindableCollection<MidiInput> MidiInputs { get; } =
    [
        new("None")
    ];

    public MidiInput? SelectedMidiInput { get; set; }

    public KeyValuePair<string, string> SelectedInstrument { get; set; }

    public KeyValuePair<string, string> SelectedLayout { get; set; }

    public BindableCollection<KeyValuePair<string, string>> AvailableLayouts { get; } = new();

    public bool MergeNotes { get; set; }

    public uint MergeMilliseconds { get; set; }

    public bool HoldNotes { get; set; }

    public bool HasSongOpen => _main.QueueView.OpenedFile != null;

    public bool CanChangeTime => PlayTimerToken is null;

    public bool CanStartStopTimer => DateTime - DateTime.Now > TimeSpan.Zero;

    [UsedImplicitly] public CancellationTokenSource? PlayTimerToken { get; private set; }

    public DateTime DateTime { get; set; } = DateTime.Now;

    public string TimerText => CanChangeTime ? "Start" : "Stop";

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

    public void OnSelectedMidiInputChanged()
    {
        _inputDevice?.Dispose();

        if (SelectedMidiInput?.DeviceName is not null
            && SelectedMidiInput.DeviceName != "None")
        {
            _inputDevice = InputDevice.GetByName(SelectedMidiInput.DeviceName);

            _inputDevice!.EventReceived += OnNoteEvent; _inputDevice!.EventReceived += OnNoteEvent;
            _inputDevice!.StartEventsListening();
        }
    }

    private void OnNoteEvent(object? sender, MidiEventReceivedEventArgs e)
    {
        if (e.Event is not Melanchall.DryWetMidi.Core.NoteOnEvent noteOn) return;
        if (noteOn.Velocity == 0) return;

        KeyboardPlayer.PlayNote(noteOn.NoteNumber, SelectedLayout.Key, SelectedInstrument.Key);
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
    public void SetTimeToNow() => DateTime = DateTime.Now;

    [UsedImplicitly]
    private void OnSelectedInstrumentChanged()
    {
        RefreshAvailableLayouts();

        if (AvailableLayouts.Count > 0)
            SelectedLayout = AvailableLayouts[0];

        var index = Keyboard.GetInstrumentIndex(SelectedInstrument.Key);
        Settings.Modify(s => s.SelectedInstrument = index);
        _events.Publish(this);
    }

    [UsedImplicitly]
    private void OnSelectedLayoutChanged()
    {
        var index = Keyboard.GetLayoutIndex(SelectedLayout.Key);
        Settings.Modify(s => s.SelectedLayout = index);
        _events.Publish(this);
    }

    private void RefreshAvailableLayouts()
    {
        AvailableLayouts.Clear();

        var layouts = Keyboard.GetLayoutNamesForInstrument(SelectedInstrument.Key);
        foreach (var layout in layouts)
            AvailableLayouts.Add(layout);

        NotifyOfPropertyChange(nameof(AvailableLayouts));
    }

    [UsedImplicitly]
    private async void OnMergeNotesChanged()
    {
        if (_isUpdatingFromSong) return;
        if (_main.QueueView is null) return;

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
        if (_main.QueueView is null) return;

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
        if (_main.QueueView is null) return;

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
