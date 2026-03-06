namespace AutoMidiPlayer.Data.Midi;

public class MidiInput(string deviceName)
{
    public string DeviceName { get; } = deviceName;
}
