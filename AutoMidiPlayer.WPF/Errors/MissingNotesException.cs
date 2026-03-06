using System;

namespace AutoMidiPlayer.WPF.Errors;

public class MissingNotesException(string message) : Exception(message)
{
}
