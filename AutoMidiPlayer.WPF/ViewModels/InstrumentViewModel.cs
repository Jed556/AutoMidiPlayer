using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    private bool _suppressSelectionHandlers;
    private InputDevice? _inputDevice;
    private readonly Dictionary<string, string> _selectedInstrumentByGame;


    public InstrumentViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);
        _selectedInstrumentByGame = LoadSelectedInstrumentsByGame();

        _main.ActiveGamesChanged += HandleActiveGamesChanged;

        // Initialize selected MIDI input
        SelectedMidiInput = MidiInputs[0];

        _suppressSelectionHandlers = true;
        try
        {
            RefreshAvailableInstruments();
            var initialInstrument = GetPreferredInstrumentForActiveGame();
            if (initialInstrument.Equals(default(KeyValuePair<string, string>)) && AvailableInstruments.Count > 0)
                initialInstrument = AvailableInstruments[0];

            SelectedInstrument = initialInstrument;

            RefreshAvailableLayouts();
            var layout = ResolvePreferredLayout(null);
            SelectedLayout = layout;

            if (!layout.Equals(default(KeyValuePair<string, string>)))
            {
                Settings.Modify(s =>
                {
                    s.SelectedLayout = Keyboard.GetLayoutIndex(layout.Key);
                    s.SelectedLayoutName = layout.Key;
                });
            }
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }

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

    public BindableCollection<KeyValuePair<string, string>> AvailableInstruments { get; } = new();

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
        if (_suppressSelectionHandlers)
            return;

        if (SelectedInstrument.Equals(default(KeyValuePair<string, string>)))
            return;

        // Remember current layout name before refreshing
        var previousLayoutName = SelectedLayout.Key;

        // Suppress handlers during the layout refresh cycle to prevent WPF
        // ComboBox binding resets from interfering with our final selection.
        _suppressSelectionHandlers = true;
        try
        {
            RefreshAvailableLayouts();
            var layout = ResolvePreferredLayout(previousLayoutName);
            SelectedLayout = layout;

            if (!layout.Equals(default(KeyValuePair<string, string>)))
            {
                Settings.Modify(s =>
                {
                    s.SelectedLayout = Keyboard.GetLayoutIndex(layout.Key);
                    s.SelectedLayoutName = layout.Key;
                });
            }
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }

        // Deferred notify so WPF re-reads the final SelectedLayout after collection changes settle
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            () => NotifyOfPropertyChange(nameof(SelectedLayout)));

        var index = AvailableInstruments.ToList().FindIndex(i =>
            string.Equals(i.Key, SelectedInstrument.Key, StringComparison.OrdinalIgnoreCase));
        Settings.Modify(s => s.SelectedInstrument = index);
        SaveSelectedInstrumentForActiveGame();
        _events.Publish(this);
    }

    [UsedImplicitly]
    private void OnSelectedLayoutChanged()
    {
        if (_suppressSelectionHandlers)
            return;

        if (SelectedLayout.Equals(default(KeyValuePair<string, string>)))
            return;

        var index = Keyboard.GetLayoutIndex(SelectedLayout.Key);
        Settings.Modify(s =>
        {
            s.SelectedLayout = index;
            s.SelectedLayoutName = SelectedLayout.Key;
        });
        _events.Publish(this);
    }

    private void RefreshAvailableLayouts()
    {
        AvailableLayouts.Clear();

        if (SelectedInstrument.Equals(default(KeyValuePair<string, string>)))
        {
            NotifyOfPropertyChange(nameof(AvailableLayouts));
            return;
        }

        var layouts = Keyboard.GetLayoutNamesForInstrument(SelectedInstrument.Key);
        foreach (var layout in layouts)
            AvailableLayouts.Add(layout);

        NotifyOfPropertyChange(nameof(AvailableLayouts));
    }

    private void RefreshAvailableInstruments()
    {
        AvailableInstruments.Clear();

        var instruments = Keyboard.GetInstrumentNamesForGames(_main.ActiveGameNames);
        foreach (var instrument in instruments)
            AvailableInstruments.Add(instrument);

        NotifyOfPropertyChange(nameof(AvailableInstruments));
    }

    private void HandleActiveGamesChanged()
    {
        var currentLayoutName = SelectedLayout.Key;

        // Suppress all selection change handlers for the entire batch update.
        // When we Clear() the collections, WPF's ComboBox binding resets SelectedItem
        // to default, which would trigger On*Changed handlers and cause races.
        _suppressSelectionHandlers = true;

        try
        {
            RefreshAvailableInstruments();

            if (AvailableInstruments.Count == 0)
            {
                SelectedInstrument = default;
                AvailableLayouts.Clear();
                SelectedLayout = default;
                NotifyOfPropertyChange(nameof(AvailableLayouts));
                return;
            }

            var preferred = GetPreferredInstrumentForActiveGame();
            SelectedInstrument = preferred.Equals(default(KeyValuePair<string, string>))
                ? AvailableInstruments[0]
                : preferred;

            RefreshAvailableLayouts();
            var layout = ResolvePreferredLayout(currentLayoutName);
            SelectedLayout = layout;

            // Persist selections
            var instrIndex = AvailableInstruments.ToList().FindIndex(i =>
                string.Equals(i.Key, SelectedInstrument.Key, StringComparison.OrdinalIgnoreCase));
            Settings.Modify(s => s.SelectedInstrument = instrIndex);
            SaveSelectedInstrumentForActiveGame();

            if (!layout.Equals(default(KeyValuePair<string, string>)))
            {
                Settings.Modify(s =>
                {
                    s.SelectedLayout = Keyboard.GetLayoutIndex(layout.Key);
                    s.SelectedLayoutName = layout.Key;
                });
            }
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }

        // Schedule property-change notifications AFTER WPF has finished processing
        // the collection changes so the ComboBox re-reads our final values.
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            () =>
            {
                NotifyOfPropertyChange(nameof(SelectedInstrument));
                NotifyOfPropertyChange(nameof(SelectedLayout));
            });

        _events.Publish(this);
    }

    private KeyValuePair<string, string> GetPreferredInstrumentForActiveGame()
    {
        var gameId = GetActiveGameId();
        if (!string.IsNullOrWhiteSpace(gameId)
            && _selectedInstrumentByGame.TryGetValue(gameId, out var preferredInstrumentId)
            && !string.IsNullOrWhiteSpace(preferredInstrumentId))
        {
            var bySaved = AvailableInstruments.FirstOrDefault(instrument =>
                string.Equals(instrument.Key, preferredInstrumentId, StringComparison.OrdinalIgnoreCase));
            if (!bySaved.Equals(default(KeyValuePair<string, string>)))
                return bySaved;
        }

        var fromSettings = AvailableInstruments.ElementAtOrDefault(Settings.SelectedInstrument);
        if (!fromSettings.Equals(default(KeyValuePair<string, string>)))
            return fromSettings;

        return default;
    }

    private string? GetActiveGameId() => _main.SelectedGame?.Definition.Id;

    private void SaveSelectedInstrumentForActiveGame()
    {
        var gameId = GetActiveGameId();
        if (string.IsNullOrWhiteSpace(gameId)
            || SelectedInstrument.Equals(default(KeyValuePair<string, string>))
            || string.IsNullOrWhiteSpace(SelectedInstrument.Key))
            return;

        _selectedInstrumentByGame[gameId] = SelectedInstrument.Key;
        var json = JsonSerializer.Serialize(_selectedInstrumentByGame);
        Settings.Modify(s => s.SelectedInstrumentByGame = json);
    }

    private static Dictionary<string, string> LoadSelectedInstrumentsByGame()
    {
        try
        {
            var json = Settings.SelectedInstrumentByGame;
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private KeyValuePair<string, string> ResolvePreferredLayout(string? preferredLayoutName)
    {
        if (AvailableLayouts.Count == 0)
            return default;

        if (!string.IsNullOrWhiteSpace(preferredLayoutName))
        {
            var byPreferredName = AvailableLayouts.FirstOrDefault(l =>
                string.Equals(l.Key, preferredLayoutName, StringComparison.OrdinalIgnoreCase));

            if (!byPreferredName.Equals(default(KeyValuePair<string, string>)))
                return byPreferredName;
        }

        var savedName = Settings.SelectedLayoutName;
        if (!string.IsNullOrWhiteSpace(savedName))
        {
            var bySavedName = AvailableLayouts.FirstOrDefault(l =>
                string.Equals(l.Key, savedName, StringComparison.OrdinalIgnoreCase));

            if (!bySavedName.Equals(default(KeyValuePair<string, string>)))
                return bySavedName;
        }

        var savedLayout = Keyboard.GetLayoutAtIndex(Settings.SelectedLayout);
        if (!savedLayout.Equals(default(KeyValuePair<string, string>)))
        {
            var bySavedIndex = AvailableLayouts.FirstOrDefault(l =>
                string.Equals(l.Key, savedLayout.Key, StringComparison.OrdinalIgnoreCase));

            if (!bySavedIndex.Equals(default(KeyValuePair<string, string>)))
                return bySavedIndex;
        }

        return AvailableLayouts[0];
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
