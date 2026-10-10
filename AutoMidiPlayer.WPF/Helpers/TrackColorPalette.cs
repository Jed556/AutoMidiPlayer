using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.WPF.Core;

namespace AutoMidiPlayer.WPF.Helpers;

public static class TrackColorPalette
{
    public static readonly Color[] Colors =
    [
        Color.FromRgb(0, 180, 216),    // 0: Cyan
        Color.FromRgb(6, 214, 160),    // 1: Emerald
        Color.FromRgb(255, 183, 3),    // 2: Amber
        Color.FromRgb(251, 133, 0),    // 3: Coral Orange
        Color.FromRgb(131, 56, 236),   // 4: Purple
        Color.FromRgb(255, 0, 110),    // 5: Pink
        Color.FromRgb(58, 134, 255),   // 6: Blue
        Color.FromRgb(112, 224, 0),    // 7: Lime
        Color.FromRgb(247, 37, 133),   // 8: Magenta
        Color.FromRgb(76, 201, 240),   // 9: Sky Blue
        Color.FromRgb(254, 228, 64),   // 10: Bright Yellow
        Color.FromRgb(243, 114, 44)    // 11: Tangerine
    ];

    private static readonly SolidColorBrush[] Brushes;
    private static readonly SolidColorBrush[] TextBrushes;

    static TrackColorPalette()
    {
        Brushes = new SolidColorBrush[Colors.Length];
        TextBrushes = new SolidColorBrush[Colors.Length];

        for (var i = 0; i < Colors.Length; i++)
        {
            var b = new SolidColorBrush(Colors[i]);
            b.Freeze();
            Brushes[i] = b;

            var c = Colors[i];
            var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            var tb = new SolidColorBrush(lum > 0.5 ? Color.FromRgb(18, 18, 20) : System.Windows.Media.Colors.White);
            tb.Freeze();
            TextBrushes[i] = tb;
        }
    }

    public static Color GetColor(int trackIndex, int trackCount = 0)
    {
        if (trackCount == 1)
            return AccentColorHelper.GetAccentColor();

        return Colors[Math.Abs(trackIndex) % Colors.Length];
    }

    public static SolidColorBrush GetBrush(int trackIndex, int trackCount = 0)
    {
        if (trackCount == 1)
        {
            var brush = new SolidColorBrush(AccentColorHelper.GetAccentColor());
            brush.Freeze();
            return brush;
        }

        return Brushes[Math.Abs(trackIndex) % Brushes.Length];
    }

    public static SolidColorBrush GetTextBrush(int trackIndex, int trackCount = 0)
    {
        if (trackCount == 1)
        {
            var c = AccentColorHelper.GetAccentColor();
            var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            var tb = new SolidColorBrush(lum > 0.5 ? Color.FromRgb(18, 18, 20) : System.Windows.Media.Colors.White);
            tb.Freeze();
            return tb;
        }

        return TextBrushes[Math.Abs(trackIndex) % TextBrushes.Length];
    }

    public static SolidColorBrush[] AllBrushes => Brushes;
}

public class TrackBrushConverter : IValueConverter
{
    public static readonly TrackBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MidiTrack track)
        {
            return TrackColorPalette.GetBrush(track.Index, track.TotalTracks);
        }
        if (value is int trackIndex)
        {
            return TrackColorPalette.GetBrush(trackIndex);
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class TrackColorConverter : IValueConverter
{
    public static readonly TrackColorConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MidiTrack track)
        {
            return TrackColorPalette.GetColor(track.Index, track.TotalTracks);
        }
        if (value is int trackIndex)
        {
            return TrackColorPalette.GetColor(trackIndex);
        }
        return System.Windows.Media.Colors.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class TrackTextBrushConverter : IValueConverter
{
    public static readonly TrackTextBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MidiTrack track)
        {
            return TrackColorPalette.GetTextBrush(track.Index, track.TotalTracks);
        }
        if (value is int trackIndex)
        {
            return TrackColorPalette.GetTextBrush(trackIndex);
        }
        return System.Windows.Media.Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
