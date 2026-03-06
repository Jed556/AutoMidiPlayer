namespace AutoMidiPlayer.Data.Notification;

public sealed class ListenModeChangedNotification(bool enabled)
{
    public bool Enabled { get; } = enabled;
}
