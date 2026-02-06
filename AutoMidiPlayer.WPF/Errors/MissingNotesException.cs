using System;

namespace AutoMidiPlayer.WPF.Errors;

public class MissingNotesException : Exception
{
    public MissingNotesException(string message) : base(message) { }
}
