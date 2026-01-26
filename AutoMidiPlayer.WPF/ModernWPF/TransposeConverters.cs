using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using ModernWpf;

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

/// <summary>
/// Multi-value converter to check if a song is currently playing.
/// Values[0]: The MidiFile of the row
/// Values[1]: The currently opened MidiFile
/// Returns accent color brush if playing, otherwise transparent.
/// </summary>
public class IsPlayingToColorConverter : IMultiValueConverter
{
    public static IsPlayingToColorConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is MidiFile rowFile && values[1] is MidiFile openedFile)
        {
            if (rowFile == openedFile)
            {
                return new SolidColorBrush(ThemeManager.Current.ActualAccentColor);
            }
        }
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Multi-value converter to check if a song is currently playing.
/// Returns the accent color foreground if playing, otherwise default foreground.
/// </summary>
public class IsPlayingToForegroundConverter : IMultiValueConverter
{
    public static IsPlayingToForegroundConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is MidiFile rowFile && values[1] is MidiFile openedFile)
        {
            if (rowFile == openedFile)
            {
                return new SolidColorBrush(ThemeManager.Current.ActualAccentColor);
            }
        }
        return DependencyProperty.UnsetValue; // Use default foreground
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Multi-value converter that returns true if the row's MidiFile matches the currently playing file.
/// </summary>
public class IsPlayingToBoolConverter : IMultiValueConverter
{
    public static IsPlayingToBoolConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is MidiFile rowFile && values[1] is MidiFile openedFile)
        {
            return rowFile == openedFile;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
