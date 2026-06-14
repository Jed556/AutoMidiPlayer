using System.Windows.Input;

namespace AutoMidiPlayer.WPF.Controls.Snackbar;

/// <summary>
/// Describes a custom action button rendered below the secondary text in a snackbar item.
/// </summary>
public sealed class SnackbarActionButton
{
    public string Text { get; init; } = string.Empty;

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public ButtonVariant Variant { get; init; } = ButtonVariant.Ghost;

    public ButtonColorMode ColorMode { get; init; } = ButtonColorMode.Accent;
}
