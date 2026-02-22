namespace AutoMidiPlayer.Data.Notification;

public sealed class ListenModeChangedNotification
{
    public ListenModeChangedNotification(bool enabled)
    {
        Enabled = enabled;
    }

    public bool Enabled { get; }
}
