using System.Windows;

namespace AutoMidiPlayer.WPF.Animation.Transitions;

/// <summary>
///     Specifies that animations are suppressed during navigation.
/// </summary>
public sealed class SuppressTransition : Transition
{
    protected override Animation? GetEnterAnimation(FrameworkElement element, bool movingBackwards) => null;

    protected override Animation? GetExitAnimation(FrameworkElement element, bool movingBackwards) => null;
}
