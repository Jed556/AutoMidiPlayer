using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.WPF.Core;

namespace AutoMidiPlayer.WPF.Converters;

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
                return new SolidColorBrush(AccentColorHelper.GetAccentColor());
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
                return new SolidColorBrush(AccentColorHelper.GetAccentColor());
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

/// <summary>
/// Multi-value converter that returns true if the row's MidiFile matches the currently playing file
/// AND playback is actually running.
/// Values: [0] = MidiFile (row), [1] = MidiFile (opened), [2] = bool (IsPlaying)
/// </summary>
public class IsActivelyPlayingConverter : IMultiValueConverter
{
    public static IsActivelyPlayingConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 &&
            values[0] is MidiFile rowFile &&
            values[1] is MidiFile openedFile &&
            values[2] is bool isPlaying)
        {
            return rowFile == openedFile && isPlaying;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Multi-value converter that returns the appropriate play/pause glyph character.
/// Returns pause glyph if this row's file is playing, play glyph otherwise.
/// Values: [0] = MidiFile (row), [1] = MidiFile (opened), [2] = bool (IsPlaying)
/// </summary>
public class PlayPauseGlyphConverter : IMultiValueConverter
{
    public static PlayPauseGlyphConverter Instance { get; } = new();

    private const char PlayGlyph = '\uF5B0';
    private const char PauseGlyph = '\uF8AE';

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 &&
            values[0] is MidiFile rowFile &&
            values[1] is MidiFile openedFile &&
            values[2] is bool isPlaying)
        {
            // If this file is currently playing, show pause icon
            if (rowFile == openedFile && isPlaying)
                return PauseGlyph;
        }
        return PlayGlyph;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Multi-value converter that returns the appropriate play/pause Geometry.
/// Returns pause geometry if this row's file is playing, play geometry otherwise.
/// Values: [0] = MidiFile (row), [1] = MidiFile (opened), [2] = bool (IsPlaying)
/// </summary>
public class PlayPauseGeometryConverter : IMultiValueConverter
{
    public static PlayPauseGeometryConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool showPause = false;

        if (values.Length >= 3 &&
            values[0] is MidiFile rowFile &&
            values[1] is MidiFile openedFile &&
            values[2] is bool isPlaying)
        {
            // If this file is currently playing, show pause icon
            showPause = rowFile == openedFile && isPlaying;
        }

        var resourceKey = showPause ? "PauseIconGeometry" : "PlayIconGeometry";
        return Application.Current.FindResource(resourceKey) as Geometry ?? Geometry.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter to get the 1-based display index of an item in a list.
/// Values[0]: The MidiFile of the row
/// Values[1]: The ItemsSource collection
/// Returns the 1-based index of the item in the collection.
/// </summary>
public class ItemIndexConverter : IMultiValueConverter
{
    public static ItemIndexConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is MidiFile item &&
            values[1] is System.Collections.IEnumerable collection)
        {
            int index = 1;
            foreach (var obj in collection)
            {
                if (ReferenceEquals(obj, item))
                {
                    return index.ToString();
                }
                index++;
            }
        }
        return "?";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
