using AutoMidiPlayer.Data.Midi;

namespace AutoMidiPlayer.Data.Notification;

public sealed class OpenedFileChangedNotification(MidiFile? file)
{
    public MidiFile? File { get; } = file;
}
