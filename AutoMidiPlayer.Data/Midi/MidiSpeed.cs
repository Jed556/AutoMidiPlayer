namespace AutoMidiPlayer.Data.Midi;

public class MidiSpeed(string speedName, double speed)
{
    public double Speed { get; } = speed;

    public string SpeedName { get; } = speedName;
}
