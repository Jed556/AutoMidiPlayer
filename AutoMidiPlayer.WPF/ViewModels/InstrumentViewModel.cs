using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using JetBrains.Annotations;
using Stylet;
using StyletIoC;

namespace AutoMidiPlayer.WPF.ViewModels;

public class InstrumentViewModel : Screen
{
    private static readonly Settings Settings = Settings.Default;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;

    public InstrumentViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _main = main;
        _events = ioc.Get<IEventAggregator>();

        // Initialize instrument from settings
        SelectedInstrument = Keyboard.InstrumentNames
            .FirstOrDefault(i => (int)i.Key == Settings.SelectedInstrument);

        // Initialize layout from settings
        SelectedLayout = Keyboard.LayoutNames
            .FirstOrDefault(l => (int)l.Key == Settings.SelectedLayout);

        // Initialize MergeNotes from settings
        MergeNotes = Settings.MergeNotes;
        MergeMilliseconds = Settings.MergeMilliseconds;
    }

    public KeyValuePair<Keyboard.Instrument, string> SelectedInstrument { get; set; }

    public KeyValuePair<Keyboard.Layout, string> SelectedLayout { get; set; }

    public bool MergeNotes { get; set; }

    public uint MergeMilliseconds { get; set; }

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
    private void OnMergeNotesChanged()
    {
        Settings.Modify(s => s.MergeNotes = MergeNotes);
        _events.Publish(new MergeNotesNotification(MergeNotes));
    }

    [UsedImplicitly]
    private void OnMergeMillisecondsChanged()
    {
        Settings.Modify(s => s.MergeMilliseconds = MergeMilliseconds);
        _events.Publish(this);
    }
}
