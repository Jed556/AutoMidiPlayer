using System;
using System.IO;
using AutoMidiPlayer.Data;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>
/// A self-contained MIDI preview player with transport controls (play / pause / stop /
/// seek). It renders audio through the Windows "Microsoft GS Wavetable Synth" on its OWN
/// output device — completely independent of the app's main playback engine, queue,
/// opened file and Listen Mode setting. Lets the Discover page preview a track before the
/// user decides to download / add it.
/// </summary>
public sealed class MidiShowPreviewPlayer : IDisposable
{
    private readonly object _lock = new();
    private Playback? _playback;

    /// <summary>Raised when playback finishes on its own (reaches the end).</summary>
    public event Action? Finished;

    public TimeSpan Duration { get; private set; }

    public bool IsLoaded
    {
        get { lock (_lock) return _playback is not null; }
    }

    public bool IsPlaying
    {
        get { lock (_lock) return _playback?.IsRunning ?? false; }
    }

    public TimeSpan CurrentTime
    {
        get
        {
            lock (_lock)
            {
                if (_playback is null)
                    return TimeSpan.Zero;
                try
                {
                    var t = _playback.GetCurrentTime<MetricTimeSpan>();
                    return TimeSpan.FromTicks(t.TotalMicroseconds * 10);
                }
                catch { return TimeSpan.Zero; }
            }
        }
    }

    /// <summary>
    /// Loads the given MIDI bytes and starts playing from the beginning, rendering through
    /// the SHARED synth device (owned by the main player). The device is never disposed here.
    /// </summary>
    public void Play(byte[] data, OutputDevice output)
    {
        Stop();

        var lenient = new ReadingSettings
        {
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
            ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read
        };

        using var stream = new MemoryStream(data);
        var midi = MidiFile.Read(stream, lenient);

        lock (_lock)
        {
            _playback = midi.GetPlayback(output);
            _playback.InterruptNotesOnStop = true; // avoid stuck notes on the shared synth
            try
            {
                var dur = _playback.GetDuration<MetricTimeSpan>();
                Duration = TimeSpan.FromTicks(dur.TotalMicroseconds * 10);
            }
            catch { Duration = TimeSpan.Zero; }

            _playback.Finished += OnFinished;
            _playback.Start();
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            try { _playback?.Stop(); } catch { /* ignore */ }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            try { _playback?.Start(); } catch { /* ignore */ }
        }
    }

    public void TogglePlayPause()
    {
        lock (_lock)
        {
            if (_playback is null) return;
            try
            {
                if (_playback.IsRunning) _playback.Stop();
                else _playback.Start();
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>Seeks to the given position (clamped to [0, Duration]).</summary>
    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && position > Duration) position = Duration;

        lock (_lock)
        {
            try { _playback?.MoveToTime(new MetricTimeSpan(position.Ticks / 10)); } catch { /* ignore */ }
        }
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        Stop();
        Finished?.Invoke();
    }

    /// <summary>Stops playback. The shared synth device is owned elsewhere and not disposed.</summary>
    public void Stop()
    {
        Playback? playback;
        lock (_lock)
        {
            playback = _playback;
            _playback = null;
        }

        if (playback is not null)
        {
            try { playback.Finished -= OnFinished; playback.Stop(); } catch { /* ignore */ }
            try { playback.Dispose(); } catch { /* ignore */ }
        }
    }

    public void Dispose() => Stop();
}
