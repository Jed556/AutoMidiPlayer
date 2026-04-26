using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Services;
using Melanchall.DryWetMidi.Interaction;
using PropertyChanged;
using Stylet;
using StyletIoC;

namespace AutoMidiPlayer.WPF.ViewModels;

public class PianoSheetViewModel : Screen, IHandle<OpenedFileChangedNotification>, IHandle<InstrumentViewModel>
{
    private static readonly Settings Settings = Settings.Default;

    private readonly MainWindowViewModel _main;
    private readonly IEventAggregator _events;
    private int _bars = 1;
    private int _beats;
    private int _shorten = 1;
    private string _result = string.Empty;

    public PianoSheetViewModel(MainWindowViewModel main)
    {
        _main = main;
        _events = _main.Ioc.Get<IEventAggregator>();
        _events.Subscribe(this);
        SongSettings.SettingsRebuildRequired += OnSongSettingsRebuildRequired;
    }

    [OnChangedMethod(nameof(Update))] public char Delimiter { get; set; } = '_';

    [OnChangedMethod(nameof(Update))]
    public KeyValuePair<string, string> SelectedLayout
    {
        get => InstrumentPage.SelectedLayout;
        set => InstrumentPage.SelectedLayout = value;
    }

    public QueueViewModel QueueView => _main.QueueView;

    public SongService SongSettings => _main.SongSettings;

    public InstrumentViewModel InstrumentPage => _main.InstrumentView;

    public string Result
    {
        get => _result;
        private set => SetAndNotify(ref _result, value);
    }

    [OnChangedMethod(nameof(Update))]
    public int Bars
    {
        get => _bars;
        set => SetAndNotify(ref _bars, Math.Max(value, 0));
    }

    [OnChangedMethod(nameof(Update))]
    public int Beats
    {
        get => _beats;
        set => SetAndNotify(ref _beats, Math.Max(value, 0));
    }

    [OnChangedMethod(nameof(Update))]
    public int Shorten
    {
        get => _shorten;
        set => SetAndNotify(ref _shorten, Math.Max(value, 1));
    }

    public void Update()
    {
        var openedFile = QueueView.OpenedFile;
        if (openedFile is null)
        {
            Result = string.Empty;
            return;
        }

        if (Bars == 0 && Beats == 0)
        {
            Result = string.Empty;
            return;
        }

        var layout = InstrumentPage.SelectedLayout.Key; // layout name (string)
        var instrument = InstrumentPage.SelectedInstrument.Key; // instrument id (string)

        // Ticks is too small so it is not included
        var split = openedFile.Split((uint)Bars, (uint)Beats, 0);

        var sb = new StringBuilder();
        foreach (var bar in split)
        {
            var notes = bar.GetNotes();
            if (notes.Count == 0)
                continue;

            var last = 0;

            foreach (var note in notes)
            {
                var id = note.NoteNumber + SongSettings.EffectiveKeyOffset;
                var transpose = SongSettings.Transpose?.Key;
                if (Settings.TransposeNotes && transpose is not null)
                    KeyboardPlayer.TransposeNote(instrument, ref id, transpose.Value);

                if (!KeyboardPlayer.TryGetKey(layout, instrument, id, out var key)) continue;

                var difference = note.Time - last;
                var dotCount = difference / Shorten;

                sb.Append(new string(Delimiter, (int)dotCount));
                sb.Append(key.ToString().Last());

                last = (int)note.Time;
            }

            sb.AppendLine();
        }

        Result = sb.ToString();
    }

    public void Handle(OpenedFileChangedNotification message) => Update();

    public void Handle(InstrumentViewModel message)
    {
        NotifyOfPropertyChange(nameof(SelectedLayout));
        Update();
    }

    private void OnSongSettingsRebuildRequired()
    {
        if (Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            Application.Current.Dispatcher.BeginInvoke(Update);
            return;
        }

        Update();
    }

    protected override void OnActivate()
    {
        Logger.LogPageVisit("Piano Sheet", source: "screen-activate");
        NotifyOfPropertyChange(nameof(SelectedLayout));
        Update();

        if (Application.Current?.Dispatcher is { } dispatcher)
            dispatcher.BeginInvoke(() => NotifyOfPropertyChange(nameof(SelectedLayout)));
    }
}
