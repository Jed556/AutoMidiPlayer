using System;
using System.Collections.Generic;
using AutoMidiPlayer.Data.Entities;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;
using Stylet;
using static System.IO.Path;

namespace AutoMidiPlayer.Data.Midi;

public class MidiFile : Screen
{
    private readonly ReadingSettings? _settings;
    private int _position;

    public MidiFile(Song song, ReadingSettings? settings = null)
    {
        _settings = settings;

        Song = song;
        InitializeMidi();
    }

    public Song Song { get; }

    public int Position
    {
        get => _position + 1;
        set => SetAndNotify(ref _position, value);
    }

    public Melanchall.DryWetMidi.Core.MidiFile Midi { get; private set; } = null!;

    /// <summary>
    /// The original tempo map from the MIDI file, preserved regardless of track changes.
    /// </summary>
    public TempoMap OriginalTempoMap { get; private set; } = null!;

    public string Path => Song.Path;

    public string Title => Song.Title ?? GetFileNameWithoutExtension(Path);

    public string? Author => Song.Author;

    public TimeSpan Duration => Midi.GetDuration<MetricTimeSpan>();

    /// <summary>
    /// Gets the BPM from the MIDI file's tempo map. Returns the tempo at the start of the file.
    /// </summary>
    public double GetNativeBpm()
    {
        var tempoMap = Midi.GetTempoMap();
        var tempo = tempoMap.GetTempoAtTime(new MetricTimeSpan(0));
        return tempo.BeatsPerMinute;
    }

    /// <summary>
    /// Gets the effective BPM - uses song's custom BPM if set, otherwise uses native MIDI BPM.
    /// </summary>
    public double EffectiveBpm => Song.Bpm ?? GetNativeBpm();

    public IEnumerable<Melanchall.DryWetMidi.Core.MidiFile> Split(uint bars, uint beats, uint ticks) =>
        Midi.SplitByGrid(new SteppedGrid(new BarBeatTicksTimeSpan(bars, beats, ticks)));

    public void InitializeMidi()
    {
        Midi = Melanchall.DryWetMidi.Core.MidiFile.Read(Path, _settings);
        // Store the original tempo map so it's preserved even when tracks are modified
        OriginalTempoMap = Midi.GetTempoMap();
    }
}
