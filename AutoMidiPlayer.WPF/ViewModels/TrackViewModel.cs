using System;
using System.Collections.Generic;
using System.Linq;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Services;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.ViewModels;

/// <summary>
/// ViewModel responsible for track list management and display.
/// Playback controls are handled by PlaybackService.
/// </summary>
public class TrackViewModel : Screen
{
    #region Fields

    private static readonly Settings Settings = Settings.Default;
    private readonly MainWindowViewModel _main;
    private bool _isViewActive = true;

    #endregion

    #region Constructor

    public TrackViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _main = main;

        // Subscribe to note played events from PlaybackService
        main.Playback.NotePlayed += OnNotePlayed;
    }

    #endregion

    #region Properties - Track List

    public BindableCollection<MidiTrack> MidiTracks { get; } = new();

    #endregion

    #region Properties - Delegate to PlaybackService

    public PlaybackService Playback => _main.Playback;

    public QueueViewModel Queue => _main.QueueView;

    #endregion

    #region Properties - Note Statistics

    /// <summary>
    /// Total number of notes across all enabled tracks
    /// </summary>
    public int TotalNotes => MidiTracks.Where(t => t.IsChecked).Sum(t => t.NotesCount);

    /// <summary>
    /// Number of notes that are playable with current instrument settings
    /// </summary>
    public int AccessibleNotes
    {
        get
        {
            var instrument = InstrumentPage.SelectedInstrument.Key;
            var keyOffset = SongSettings.KeyOffset;
            var transpose = SongSettings.Transpose?.Key;
            var availableNotes = Keyboard.GetNotes(instrument);

            return MidiTracks
                .Where(t => t.IsChecked)
                .SelectMany(t => t.Track.GetNotes())
                .Count(note =>
                {
                    var noteId = note.NoteNumber - keyOffset;

                    if (Settings.TransposeNotes && transpose is not null)
                    {
                        var transposed = KeyboardPlayer.TransposeNote(instrument, ref noteId, transpose.Value);
                        return availableNotes.Contains(transposed);
                    }

                    return availableNotes.Contains(noteId);
                });
        }
    }

    /// <summary>
    /// Display string showing accessible notes vs total notes
    /// </summary>
    public string NotesStatsDisplay => TotalNotes > 0
        ? $"{AccessibleNotes:N0} / {TotalNotes:N0} notes playable ({(double)AccessibleNotes / TotalNotes * 100:F1}%)"
        : "No notes";

    #endregion

    #region Properties - Private Helpers

    private SongSettingsService SongSettings => _main.SongSettings;

    private InstrumentViewModel InstrumentPage => _main.InstrumentView;

    #endregion

    #region View Lifecycle

    protected override void OnActivate()
    {
        CrashLogger.Log($"OnActivate called, MidiTracks.Count={MidiTracks.Count}");
        try
        {
            base.OnActivate();
            _isViewActive = true;
            CrashLogger.Log("OnActivate completed successfully");
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex);
            throw;
        }
    }

    protected override void OnDeactivate()
    {
        CrashLogger.Log($"OnDeactivate called, MidiTracks.Count={MidiTracks.Count}");
        try
        {
            base.OnDeactivate();
            _isViewActive = false;

            foreach (var track in MidiTracks)
            {
                track.StopGlow();
            }
            CrashLogger.Log("OnDeactivate completed successfully");
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex);
            throw;
        }
    }

    #endregion

    #region Track Management

    /// <summary>
    /// Initialize tracks from the currently opened file
    /// </summary>
    public void InitializeTracks()
    {
        if (Queue.OpenedFile?.Midi is null)
            return;

        var disabledIndices = new HashSet<int>();
        if (!string.IsNullOrEmpty(Queue.OpenedFile.Song.DisabledTracks))
        {
            foreach (var indexStr in Queue.OpenedFile.Song.DisabledTracks.Split(','))
            {
                if (int.TryParse(indexStr.Trim(), out var index))
                    disabledIndices.Add(index);
            }
        }

        MidiTracks.Clear();
        var trackChunks = Queue.OpenedFile.Midi.GetTrackChunks().ToList();
        var events = _main.Ioc.Get<IEventAggregator>();
        for (var i = 0; i < trackChunks.Count; i++)
        {
            var isChecked = !disabledIndices.Contains(i);
            MidiTracks.Add(new MidiTrack(events, trackChunks[i], i, isChecked));
        }
    }

    /// <summary>
    /// Updates playable notes count for all tracks based on current settings
    /// </summary>
    public void UpdateTrackPlayableNotes()
    {
        var instrument = InstrumentPage.SelectedInstrument.Key;
        var keyOffset = SongSettings.KeyOffset;
        var transpose = SongSettings.Transpose?.Key;
        var availableNotes = Keyboard.GetNotes(instrument).ToHashSet();

        Func<int, int>? transposeFunc = null;
        if (Settings.TransposeNotes && transpose is not null)
        {
            transposeFunc = noteId =>
            {
                var id = noteId;
                return KeyboardPlayer.TransposeNote(instrument, ref id, transpose.Value);
            };
        }

        foreach (var track in MidiTracks)
        {
            track.UpdatePlayableNotes(availableNotes, keyOffset, transposeFunc);
        }
    }

    /// <summary>
    /// Notify UI that note statistics have changed
    /// </summary>
    public void NotifyNoteStatsChanged()
    {
        NotifyOfPropertyChange(() => TotalNotes);
        NotifyOfPropertyChange(() => AccessibleNotes);
        NotifyOfPropertyChange(() => NotesStatsDisplay);
    }

    public void OpenFile()
    {
        Playback.UpdateButtons();
    }

    #endregion

    #region Note Glow Effects

    private void OnNotePlayed(object? sender, NotePlayedEventArgs e)
    {
        if (!_isViewActive) return;

        var matchingTracks = MidiTracks.Where(t => t.IsChecked && t.ContainsNote(e.NoteNumber)).ToList();
        foreach (var track in matchingTracks)
        {
            track.TriggerGlow();
        }
    }

    #endregion
}
