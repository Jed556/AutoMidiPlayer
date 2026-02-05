using System.Windows;
using System.Windows.Media;

namespace AutoMidiPlayer.WPF.Core;

public static class AccentColorHelper
{
    public static Color GetAccentColor()
    {
        if (Application.Current?.Resources["SystemAccentColor"] is Color color)
            return color;

        if (Application.Current?.Resources["SystemAccentColorBrush"] is SolidColorBrush brush)
            return brush.Color;

        return Colors.DodgerBlue;
    }
}
