using System.Windows;

namespace AutoMidiPlayer.WPF.Controls;

public static class IconButtonProperties
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsActive",
            typeof(bool),
            typeof(IconButtonProperties),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsActive(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsActiveProperty);
    }

    public static void SetIsActive(DependencyObject obj, bool value)
    {
        obj.SetValue(IsActiveProperty, value);
    }
}
