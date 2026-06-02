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

    public static readonly DependencyProperty IsGhostProperty =
        DependencyProperty.RegisterAttached(
            "IsGhost",
            typeof(bool),
            typeof(IconButtonProperties),
            new FrameworkPropertyMetadata(true));

    public static bool GetIsGhost(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsGhostProperty);
    }

    public static void SetIsGhost(DependencyObject obj, bool value)
    {
        obj.SetValue(IsGhostProperty, value);
    }

    public static readonly DependencyProperty UseAccentColorProperty =
        DependencyProperty.RegisterAttached(
            "UseAccentColor",
            typeof(bool),
            typeof(IconButtonProperties),
            new FrameworkPropertyMetadata(false));

    public static bool GetUseAccentColor(DependencyObject obj)
    {
        return (bool)obj.GetValue(UseAccentColorProperty);
    }

    public static void SetUseAccentColor(DependencyObject obj, bool value)
    {
        obj.SetValue(UseAccentColorProperty, value);
    }

    public static readonly DependencyProperty IsDangerProperty =
        DependencyProperty.RegisterAttached(
            "IsDanger",
            typeof(bool),
            typeof(IconButtonProperties),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsDanger(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsDangerProperty);
    }

    public static void SetIsDanger(DependencyObject obj, bool value)
    {
        obj.SetValue(IsDangerProperty, value);
    }
}
