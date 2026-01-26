using System.ComponentModel;

namespace AutoMidiPlayer.Data.Entities;

public enum Transpose
{
    [Description("Ignore missing notes")] Ignore,
    [Description("Transpose up")] Up,
    [Description("Transpose down")] Down
}
