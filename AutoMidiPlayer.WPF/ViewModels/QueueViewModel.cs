using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.ModernWPF.Errors;
using Melanchall.DryWetMidi.Core;
using ModernWpf;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.ViewModels;

public class QueueViewModel : Screen
{
    public enum LoopMode
    {
        Once,
        Track,
        Playlist,
        All
    }

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;

    public QueueViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _main = main;

        // Load saved queue settings
        Shuffle = Settings.QueueShuffle;
        Loop = (LoopMode)Settings.QueueLoopMode;
    }

    public BindableCollection<MidiFile> FilteredTracks => string.IsNullOrWhiteSpace(FilterText)
        ? Tracks
        : new(Tracks.Where(t => t.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase)));

    public BindableCollection<MidiFile> Tracks { get; } = new();

    public bool Shuffle { get; set; }

    public IEnumerable<string> TrackTitles => Tracks.Select(t => t.Title);

    public LoopMode Loop { get; set; } = LoopMode.All;

    public MidiFile? OpenedFile { get; set; }

    public MidiFile? SelectedFile { get; set; }

    public SolidColorBrush ShuffleStateColor => Shuffle
        ? new(ThemeManager.Current.ActualAccentColor)
        : Brushes.Gray;

    public Stack<MidiFile> History { get; } = new();

    public string LoopStateString =>
        Loop switch
        {
            LoopMode.Once => "\xF5E7",
            LoopMode.Track => "\xE8ED",
            LoopMode.Playlist => "\xEBE7",
            LoopMode.All => "\xE8EE",
            _ => string.Empty
        };

    public string? FilterText { get; set; }

    private BindableCollection<MidiFile> ShuffledTracks { get; set; } = new();

    public BindableCollection<MidiFile> GetPlaylist() => Shuffle ? ShuffledTracks : Tracks;

    public MidiFile? Next()
    {
        var playlist = GetPlaylist().ToList();
        if (OpenedFile is null) return playlist.FirstOrDefault();

        switch (Loop)
        {
            case LoopMode.Once:
                return null;
            case LoopMode.Track:
                return OpenedFile;
        }

        var next = playlist.IndexOf(OpenedFile) + 1;
        if (Loop is LoopMode.All)
            next %= playlist.Count;

        return playlist.ElementAtOrDefault(next);
    }

    public void AddFiles(IEnumerable<MidiFile> files)
    {
        foreach (var file in files)
        {
            if (!Tracks.Contains(file))
                Tracks.Add(file);
        }

        ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));
        OnQueueModified();

        var next = Next();
        if (OpenedFile is null && Tracks.Count > 0 && next is not null)
            _events.Publish(Next());
    }

    public void AddFile(MidiFile file)
    {
        if (!Tracks.Contains(file))
        {
            Tracks.Add(file);
            ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));
            OnQueueModified();
        }
    }

    public void PlayFromQueue(MidiFile? file)
    {
        if (file is not null)
        {
            _events.Publish(file);
        }
    }

    public void ClearQueue()
    {
        Tracks.Clear();
        FilteredTracks.Clear();
        History.Clear();

        OpenedFile = null;
        SelectedFile = null;
        SaveQueue();
    }

    public void RemoveTrack()
    {
        if (SelectedFile is not null)
        {
            OpenedFile = OpenedFile == SelectedFile ? null : OpenedFile;
            Tracks.Remove(SelectedFile);
            OnQueueModified();
        }
    }

    public void MoveUp()
    {
        if (SelectedFile is null) return;

        var index = Tracks.IndexOf(SelectedFile);
        if (index > 0)
        {
            Tracks.Move(index, index - 1);
            OnQueueModified();
        }
    }

    public void MoveDown()
    {
        if (SelectedFile is null) return;

        var index = Tracks.IndexOf(SelectedFile);
        if (index < Tracks.Count - 1)
        {
            Tracks.Move(index, index + 1);
            OnQueueModified();
        }
    }

    public void OnFileChanged(object sender, EventArgs e)
    {
        if (SelectedFile is not null)
            _events.Publish(SelectedFile);
    }

    public void OnOpenedFileChanged()
    {
        if (OpenedFile is null) return;

        var transpose = SettingsPageViewModel.TransposeNames
            .FirstOrDefault(e => e.Key == OpenedFile.Song.Transpose);

        if (OpenedFile.Song.Transpose is not null)
            _main.SettingsView.Transpose = transpose;
        _main.SettingsView.KeyOffset = OpenedFile.Song.Key;
        _main.SettingsView.Speed = OpenedFile.Song.Speed ?? 1.0;
    }

    public void Previous()
    {
        History.Pop();
        _events.Publish(History.Pop());
    }

    public void ToggleLoop()
    {
        var loopState = (int)Loop;
        var loopStates = Enum.GetValues(typeof(LoopMode)).Length;

        var newState = (loopState + 1) % loopStates;
        Loop = (LoopMode)newState;

        // Save to settings
        Settings.QueueLoopMode = (int)Loop;
        Settings.Save();
    }

    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;

        if (Shuffle)
            ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));

        // Save to settings
        Settings.QueueShuffle = Shuffle;
        Settings.Save();

        RefreshQueue();
    }

    /// <summary>
    /// Save queue song IDs to settings
    /// </summary>
    public void SaveQueue()
    {
        var songIds = Tracks.Select(t => t.Song.Id.ToString());
        Settings.QueueSongIds = string.Join(",", songIds);
        Settings.Save();
    }

    /// <summary>
    /// Restore queue from saved song IDs
    /// </summary>
    public void RestoreQueue(IEnumerable<MidiFile> availableTracks)
    {
        if (string.IsNullOrEmpty(Settings.QueueSongIds)) return;

        var savedIds = Settings.QueueSongIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        var trackDict = availableTracks.ToDictionary(t => t.Song.Id);

        foreach (var id in savedIds)
        {
            if (trackDict.TryGetValue(id, out var track) && !Tracks.Contains(track))
            {
                Tracks.Add(track);
            }
        }

        if (Shuffle)
            ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));

        RefreshQueue();
    }

    /// <summary>
    /// Called when queue is modified to auto-save
    /// </summary>
    public void OnQueueModified()
    {
        SaveQueue();
        RefreshQueue();
    }

    private void RefreshQueue()
    {
        var playlist = GetPlaylist();
        foreach (var file in playlist)
        {
            file.Position = playlist.IndexOf(file);
        }
    }
}
