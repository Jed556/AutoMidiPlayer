using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Stylet;

namespace AutoMidiPlayer.Data.Midi;

public class MidiTrack : INotifyPropertyChanged
{
    private readonly IEventAggregator _events;
    private bool _isChecked;
    private bool _isActive;
    private int _playableNotes;
    private DispatcherTimer? _glowTimer;
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    public int ProgramId { get; private set; }
    public string AvgPitchDisplay { get; private set; } = string.Empty;
    public string TimeRangeDisplay { get; private set; } = string.Empty;
    public string PitchRangeDisplay { get; private set; } = string.Empty;
    public double TimeStartRatio { get; private set; }
    public double TimeDurationRatio { get; private set; }
    public double TimeEndRatio { get; private set; }
    public double PitchStartRatio { get; private set; }
    public double PitchRangeRatio { get; private set; }
    public double PitchEndRatio { get; private set; }
    
    private int _displayTrackNumber;
    public int DisplayTrackNumber
    {
        get => _displayTrackNumber;
        set
        {
            if (_displayTrackNumber != value)
            {
                _displayTrackNumber = value;
                OnPropertyChanged();
            }
        }
    }

    private HashSet<int>? _noteNumbers; // Cached note numbers for fast lookup
    private Dictionary<int, List<(long StartUs, long EndUs)>>? _noteTimingsUs;
    private const int GlowDurationMs = 150; // How long the glow stays on

    // Black keys (sharps/flats) in MIDI note numbers mod 12: C#, D#, F#, G#, A#
    private static readonly HashSet<int> BlackKeys = new() { 1, 3, 6, 8, 10 };

    // General MIDI Instrument Names
    private static readonly string[] GeneralMidiInstruments =
    {
        "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavi",
        "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
        "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
        "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)", "Electric Guitar (clean)", "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar harmonics",
        "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
        "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
        "String Ensemble 1", "String Ensemble 2", "SynthStrings 1", "SynthStrings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
        "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "SynthBrass 1", "SynthBrass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
        "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)", "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
        "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
        "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)", "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
        "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bag pipe", "Fiddle", "Shanai",
        "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
        "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot"
    };

    public MidiTrack(IEventAggregator events, TrackChunk track, int index, Melanchall.DryWetMidi.Core.MidiFile file, bool isChecked = true)
    {
        _events = events;
        _isChecked = isChecked;

        Track = track;
        Index = index;
        TrackName = track.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;
        InstrumentName = track.Events.OfType<InstrumentNameEvent>().FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(InstrumentName))
        {
            var programChange = track.Events.OfType<ProgramChangeEvent>().FirstOrDefault();
            if (programChange != null)
            {
                if (programChange.Channel == 9)
                {
                    InstrumentName = "Standard Drum Kit";
                }
                else
                {
                    int progNum = programChange.ProgramNumber;
                    ProgramId = progNum;
                    if (progNum >= 0 && progNum < GeneralMidiInstruments.Length)
                    {
                        InstrumentName = GeneralMidiInstruments[progNum];
                    }
                }
            }
            else
            {
                var firstNote = Track.GetNotes().FirstOrDefault();
                if (firstNote != null)
                {
                    if (firstNote.Channel == 9)
                    {
                        InstrumentName = "Standard Drum Kit";
                    }
                    else if (file != null)
                    {
                        var globalProgramChange = file.GetTrackChunks()
                            .SelectMany(c => c.Events)
                            .OfType<ProgramChangeEvent>()
                            .FirstOrDefault(p => p.Channel == firstNote.Channel);

                        if (globalProgramChange != null)
                        {
                            int progNum = globalProgramChange.ProgramNumber;
                            ProgramId = progNum;
                            if (progNum >= 0 && progNum < GeneralMidiInstruments.Length)
                            {
                                InstrumentName = GeneralMidiInstruments[progNum];
                            }
                        }
                    }
                }
            }
        }

        // Calculate statistics
        CalculateStatistics(file);
    }

    private void CalculateStatistics(Melanchall.DryWetMidi.Core.MidiFile file)
    {
        var notes = Track.GetNotes().ToList();
        NotesCount = notes.Count;
        // Cache note numbers for fast lookup during playback
        _noteNumbers = notes.Select(n => (int)n.NoteNumber).ToHashSet();

        // Precalculate note timings in microseconds for accurate UI glow
        var tempoMap = file?.GetTempoMap() ?? TempoMap.Default;
        _noteTimingsUs = new Dictionary<int, List<(long StartUs, long EndUs)>>();
        foreach (var note in notes)
        {
            var pitch = (int)note.NoteNumber;
            if (!_noteTimingsUs.TryGetValue(pitch, out var timings))
            {
                timings = new List<(long, long)>();
                _noteTimingsUs[pitch] = timings;
            }
            
            var startUs = note.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds;
            var endUs = Melanchall.DryWetMidi.Interaction.TimeConverter.ConvertTo<MetricTimeSpan>(note.Time + note.Length, tempoMap).TotalMicroseconds;
            timings.Add((startUs, endUs));
        }

        if (NotesCount == 0)
        {
            BlackKeyRatio = 0;
            AverageDurationMs = 0;
            FrequentNotesRatio = 0;
            return;
        }

        // Black key ratio
        var blackKeyCount = notes.Count(n => BlackKeys.Contains(n.NoteNumber % 12));
        BlackKeyRatio = (double)blackKeyCount / NotesCount * 100;

        // Average duration in milliseconds
        var totalDuration = notes.Sum(n => n.LengthAs<MetricTimeSpan>(TempoMap.Default).TotalMicroseconds / 1000.0);
        AverageDurationMs = totalDuration / NotesCount;

        // Frequent notes ratio - notes that appear more than average
        var noteGroups = notes.GroupBy(n => n.NoteNumber).ToList();
        var averageOccurrence = (double)NotesCount / noteGroups.Count;
        var frequentNotes = noteGroups.Where(g => g.Count() > averageOccurrence).Sum(g => g.Count());
        FrequentNotesRatio = (double)frequentNotes / NotesCount * 100;

        // Expanded Accordion Statistics
        var firstNote = notes.OrderBy(n => n.Time).FirstOrDefault();
        var lastNote = notes.OrderByDescending(n => n.Time + n.Length).FirstOrDefault();
        
        if (firstNote != null && lastNote != null)
        {
            var startTime = firstNote.TimeAs<MetricTimeSpan>(tempoMap);
            var endTime = Melanchall.DryWetMidi.Interaction.TimeConverter.ConvertTo<MetricTimeSpan>(lastNote.Time + lastNote.Length, tempoMap);
            
            TimeRangeDisplay = $"{startTime.Minutes:D2}:{startTime.Seconds:D2} - {endTime.Minutes:D2}:{endTime.Seconds:D2}";
            
            if (file != null)
            {
                var totalTime = file.GetDuration<MetricTimeSpan>();
                if (totalTime.TotalMicroseconds > 0)
                {
                    TimeStartRatio = (double)startTime.TotalMicroseconds / totalTime.TotalMicroseconds;
                    TimeDurationRatio = (double)(endTime.TotalMicroseconds - startTime.TotalMicroseconds) / totalTime.TotalMicroseconds;
                    TimeEndRatio = Math.Max(0, 1.0 - TimeStartRatio - TimeDurationRatio);
                }
            }
        }

        // Pitch calculations
        var minNote = notes.Min(n => n.NoteNumber);
        var maxNote = notes.Max(n => n.NoteNumber);
        var avgNoteNumber = (int)Math.Round(notes.Average(n => n.NoteNumber));
        
        var minNoteObj = Melanchall.DryWetMidi.MusicTheory.Note.Get((Melanchall.DryWetMidi.Common.SevenBitNumber)minNote);
        PitchRangeDisplay = $"{MusicConstants.FormatNoteName(minNote)} - {MusicConstants.FormatNoteName(maxNote)}";
        AvgPitchDisplay = $"{MusicConstants.FormatNoteName((int)avgNoteNumber)}";
        
        // Pitch ratio mapped to 0-127
        PitchStartRatio = minNote / 127.0;
        PitchRangeRatio = Math.Max(0.01, (maxNote - minNote) / 127.0); // min 1% width
        PitchEndRatio = Math.Max(0, 1.0 - PitchStartRatio - PitchRangeRatio);
    }

    public bool CanBePlayed => Track.Events.Count(e => e is NoteEvent) > 0;

    public int Index { get; }

    public int NotesCount { get; private set; }

    public double BlackKeyRatio { get; private set; }

    public double AverageDurationMs { get; private set; }

    public double FrequentNotesRatio { get; private set; }

    public int PlayableNotes
    {
        get => _playableNotes;
        private set
        {
            if (_playableNotes != value)
            {
                _playableNotes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayableNotesDisplay));
            }
        }
    }

    // Formatted display strings
    public string NotesCountDisplay => $"{NotesCount:N0}";
    public string PlayableNotesDisplay => $"{PlayableNotes:N0}";
    public string BlackKeyRatioDisplay => $"{BlackKeyRatio:F1}%";
    public string AverageDurationDisplay => $"{AverageDurationMs:F0}ms";
    public string FrequentNotesRatioDisplay => $"{FrequentNotesRatio:F1}%";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            _isChecked = value;
            _events.Publish(this);
        }
    }

    public string? TrackName { get; }
    public string? InstrumentName { get; }

    public TrackChunk Track { get; }

    /// <summary>
    /// Gets or sets whether this track is currently playing a note (for glow effect)
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (_isActive != value)
            {
                _isActive = value;
                try
                {
                    OnPropertyChanged();
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex);
                }
            }
        }
    }

    /// <summary>
    /// Triggers the glow effect for this track
    /// </summary>
    public void TriggerGlow()
    {
        try
        {
            // Must run on UI thread
            if (Application.Current?.Dispatcher is null)
                return;

            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(TriggerGlow);
                return;
            }

            // Stop existing timer if any
            _glowTimer?.Stop();

            // Set active
            IsActive = true;

            // Create timer to turn off glow (DispatcherTimer runs on UI thread)
            _glowTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GlowDurationMs) };
            _glowTimer.Tick += GlowTimer_Tick;
            _glowTimer.Start();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    private void GlowTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            _glowTimer?.Stop();
            if (_glowTimer is not null)
                _glowTimer.Tick -= GlowTimer_Tick;
            IsActive = false;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    /// <summary>
    /// Stops the glow effect immediately (used when view is deactivated)
    /// </summary>
    public void StopGlow()
    {
        try
        {
            _glowTimer?.Stop();
            if (_glowTimer is not null)
                _glowTimer.Tick -= GlowTimer_Tick;
            _glowTimer = null;
            _isActive = false; // Set directly to avoid PropertyChanged when cleaning up
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    /// <summary>
    /// Updates the playable notes count based on available notes and transpose settings
    /// </summary>
    /// <param name="availableNotes">Set of note IDs that can be played</param>
    /// <param name="keyOffset">Key offset to apply</param>
    /// <param name="transposeFunc">Optional function to transpose notes</param>
    public void UpdatePlayableNotes(HashSet<int> availableNotes, int keyOffset, Func<int, int>? transposeFunc = null)
    {
        var notes = Track.GetNotes();
        var playable = notes.Count(note =>
        {
            var noteId = note.NoteNumber + keyOffset;
            if (transposeFunc is not null)
            {
                noteId = transposeFunc(noteId);
            }
            return availableNotes.Contains(noteId);
        });
        PlayableNotes = playable;
    }

    /// <summary>
    /// Checks if this track contains the given note number (O(1) lookup)
    /// </summary>
    public bool ContainsNote(int noteNumber) =>
        _noteNumbers?.Contains(noteNumber) ?? false;

    /// <summary>
    /// Checks if the track is actively playing a specific note at the given time (with 100ms tolerance)
    /// </summary>
    public bool IsPlayingNoteAt(int noteNumber, long currentUs)
    {
        if (_noteTimingsUs != null && _noteTimingsUs.TryGetValue(noteNumber, out var timings))
        {
            // Add a small 100ms tolerance window (100,000 microseconds) for playback sync imperfections
            foreach (var (startUs, endUs) in timings)
            {
                if (currentUs >= startUs - 100000 && currentUs <= endUs + 100000)
                    return true;
            }
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
