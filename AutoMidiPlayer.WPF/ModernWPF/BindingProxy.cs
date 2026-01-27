using System.Windows;

namespace AutoMidiPlayer.WPF.ModernWPF;

/// <summary>
/// A proxy class that allows binding to the DataContext from elements
/// that are not part of the visual tree (like ContextMenus in NotifyIcon).
/// </summary>
public class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy),
            new PropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore()
    {
        return new BindingProxy();
    }
}
