namespace AutoMidiPlayer.Data.Notification;

public class PlayTimerNotification(bool shouldPlay = true)
{
	public bool ShouldPlay { get; } = shouldPlay;
}
