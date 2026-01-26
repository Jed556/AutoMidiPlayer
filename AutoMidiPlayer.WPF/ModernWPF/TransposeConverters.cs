using System;
using System.Globalization;
using System.Windows.Data;
using AutoMidiPlayer.Data.Entities;

namespace AutoMidiPlayer.WPF.ModernWPF;

public class TransposeToDisplayConverter : IValueConverter
{
    public static TransposeToDisplayConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Transpose transpose)
        {
            return MusicConstants.TransposeShortNames.TryGetValue(transpose, out var name)
                ? name
                : transpose.ToString();
        }
        return "-";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TransposeToTooltipConverter : IValueConverter
{
    public static TransposeToTooltipConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Transpose transpose)
        {
            return MusicConstants.TransposeTooltips.TryGetValue(transpose, out var tooltip)
                ? tooltip
                : transpose.ToString();
        }
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class KeyToNoteConverter : IValueConverter
{
    public static KeyToNoteConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int key)
        {
            return MusicConstants.GetNoteName(key);
        }
        return "C3";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
