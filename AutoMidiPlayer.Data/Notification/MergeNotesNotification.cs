using AutoMidiPlayer.Data.Midi;

namespace AutoMidiPlayer.Data.Notification;

public class MergeNotesNotification(bool merge)
{
    public bool Merge { get; } = merge;
}

public class TrackNotification(MidiTrack track, bool enabled)
{
    public bool Enabled { get; } = enabled;

    public MidiTrack Track { get; } = track;
}
