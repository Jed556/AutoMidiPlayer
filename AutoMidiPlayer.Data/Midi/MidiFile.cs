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

    public string Path => Song.Path;

    public string Title => Song.Title ?? GetFileNameWithoutExtension(Path);

    public string? Author => Song.Author;

    public TimeSpan Duration => Midi.GetDuration<MetricTimeSpan>();

    public IEnumerable<Melanchall.DryWetMidi.Core.MidiFile> Split(uint bars, uint beats, uint ticks) =>
        Midi.SplitByGrid(new SteppedGrid(new BarBeatTicksTimeSpan(bars, beats, ticks)));

    public void InitializeMidi() => Midi = Melanchall.DryWetMidi.Core.MidiFile.Read(Path, _settings);
}
