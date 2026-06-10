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
using AutoMidiPlayer.WPF.Helpers;

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

    public PianoSheetViewModel(MainWindowViewModel main, Controls.NoSongPlaceholder.NoSongPlaceholderComponent placeholder)
    {
        _main = main;
        _events = _main.Ioc.Get<IEventAggregator>();
        _events.Subscribe(this);
        SongSettings.SettingsRebuildRequired += OnSongSettingsRebuildRequired;
        
        Placeholder = placeholder;
        Placeholder.DisplayMode = Controls.NoSongPlaceholder.PlaceholderDisplayMode.TextAndIcon;
        Placeholder.Icon = Wpf.Ui.Controls.SymbolRegular.ImmersiveReader16;
    }

    public Controls.NoSongPlaceholder.NoSongPlaceholderComponent Placeholder { get; }

    public bool HasSongOpen => QueueView.OpenedFile is not null;

    private string _delimiter = "_";

    public string Delimiter
    {
        get => _delimiter;
        set
        {
            if (string.IsNullOrEmpty(value)) return;

            var newDelimiter = value.Last().ToString();
            if (_delimiter != newDelimiter)
            {
                _delimiter = newDelimiter;
                NotifyOfPropertyChange();
                Update();
            }
            else if (value != _delimiter)
            {
                NotifyOfPropertyChange();
            }
        }
    }

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

    public bool IsDelimiterWarningVisible { get; private set; }
    public string DelimiterWarningText { get; private set; } = string.Empty;

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
        NotifyOfPropertyChange(nameof(HasSongOpen));
        
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

        var hasWarning = false;
        var warningText = string.Empty;

        if (Keyboard.TryGetKeyStrokeForCharacter(Delimiter[0], out var stroke))
        {
            var layoutKeys = Keyboard.GetLayout(layout, instrument);
            var index = layoutKeys.ToList().FindIndex(k => k.Key == stroke.Key);
            if (index >= 0)
            {
                var notes = Keyboard.GetNotes(instrument);
                if (index < notes.Count)
                {
                    var noteId = notes[index];
                    var noteName = Melanchall.DryWetMidi.MusicTheory.Note.Get((Melanchall.DryWetMidi.Common.SevenBitNumber)noteId).ToString();
                    hasWarning = true;
                    warningText = $"Used as note {noteName}";
                }
            }
        }

        IsDelimiterWarningVisible = hasWarning;
        DelimiterWarningText = warningText;
        NotifyOfPropertyChange(nameof(IsDelimiterWarningVisible));
        NotifyOfPropertyChange(nameof(DelimiterWarningText));

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

                if (!KeyboardPlayer.TryGetKeyStroke(layout, instrument, id, out var keyStroke)) continue;

                var difference = note.Time - last;
                var dotCount = difference / Shorten;

                sb.Append(new string(Delimiter[0], (int)dotCount));
                sb.Append(Keyboard.KeyStrokeToDisplayString(keyStroke));

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

    public async void ShowLegend()
    {
        var layout = InstrumentPage.SelectedLayout.Key;
        var instrument = InstrumentPage.SelectedInstrument.Key;

        var notes = Keyboard.GetNotes(instrument);
        var keys = Keyboard.GetLayout(layout, instrument);

        var sb = new StringBuilder();
        sb.AppendLine($"Delimiter: {Delimiter}");
        sb.AppendLine("^: Ctrl modifier");
        sb.AppendLine("Uppercase: Shift modifier");
        sb.AppendLine();
        sb.Append("Keystrokes:");

        var layoutList = keys.ToList();
        int count = Math.Min(notes.Count, layoutList.Count);
        
        int numLanes = 4;
        if (count <= 24) numLanes = 2;
        else if (count <= 36) numLanes = 3;

        int rows = (int)Math.Ceiling((double)count / numLanes);
        if (rows == 0) rows = 1;

        var grid = new System.Windows.Controls.Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };

        for (int lane = 0; lane < numLanes; lane++)
        {
            if (lane > 0)
            {
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(56) });
            }

            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        }

        for (int i = 0; i < rows; i++)
        {
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        }

        for (int lane = 0; lane < numLanes; lane++)
        {
            int dividerCol = lane * 4 + 1;
            
            var line = new System.Windows.Shapes.Rectangle
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "DividerStrokeColorDefaultBrush");
            System.Windows.Controls.Grid.SetRowSpan(line, rows);
            System.Windows.Controls.Grid.SetColumn(line, dividerCol);
            grid.Children.Add(line);
        }

        for (int r = 0; r < rows; r++)
        {
            for (int lane = 0; lane < numLanes; lane++)
            {
                int index = r + lane * rows;
                if (index < count)
                {
                    var noteId = notes[index];
                    var keyStroke = layoutList[index];
                    var noteName = Melanchall.DryWetMidi.MusicTheory.Note.Get((Melanchall.DryWetMidi.Common.SevenBitNumber)noteId).ToString();
                    
                    int keyCol = lane * 4;
                    int noteCol = lane * 4 + 2;

                    var keyTb = new System.Windows.Controls.TextBlock { Text = Keyboard.KeyStrokeToDisplayString(keyStroke), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 4) };
                    System.Windows.Controls.Grid.SetRow(keyTb, r);
                    System.Windows.Controls.Grid.SetColumn(keyTb, keyCol);
                    grid.Children.Add(keyTb);

                    var noteTb = new System.Windows.Controls.TextBlock { Text = noteName, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
                    System.Windows.Controls.Grid.SetRow(noteTb, r);
                    System.Windows.Controls.Grid.SetColumn(noteTb, noteCol);
                    grid.Children.Add(noteTb);
                }
            }
        }

        var request = new DialogActionRequest
        {
            Title = "Legend",
            Body = sb.ToString(),
            Content = grid,
            Icon = Wpf.Ui.Controls.SymbolRegular.Info20,
            ConfirmButton = null,
            CancelButton = new DialogActionButton { Text = "Close", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary },
        };

        await DialogHelper.ShowActionDialogAsync(request);
    }

    public void ResetDefaults()
    {
        Delimiter = "_";
        Bars = 1;
        Beats = 1;
        Shorten = 1;
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
