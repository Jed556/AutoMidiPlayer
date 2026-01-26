using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.ModernWPF;

public class TransposeToDisplayConverter : IValueConverter
{
    public static TransposeToDisplayConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Transpose transpose)
        {
            // Return short name for table display
            return transpose switch
            {
                Transpose.Up => "Up",
                Transpose.Ignore => "Ignore",
                Transpose.Down => "Down",
                _ => transpose.ToString()
            };
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
            return SettingsPageViewModel.TransposeTooltips.TryGetValue(transpose, out var tooltip)
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

    private static readonly Dictionary<int, string> KeyOffsets = new()
    {
        [-27] = "A0",
        [-26] = "A♯0",
        [-25] = "B0",
        [-24] = "C1",
        [-23] = "C♯1",
        [-22] = "D1",
        [-21] = "D♯1",
        [-20] = "E1",
        [-19] = "F1",
        [-18] = "F♯1",
        [-17] = "G1",
        [-16] = "G♯1",
        [-15] = "A1",
        [-14] = "A♯1",
        [-13] = "B1",
        [-12] = "C2",
        [-11] = "C♯2",
        [-10] = "D2",
        [-9] = "D♯2",
        [-8] = "E2",
        [-7] = "F2",
        [-6] = "F♯2",
        [-5] = "G2",
        [-4] = "G♯2",
        [-3] = "A2",
        [-2] = "A♯2",
        [-1] = "B2",
        [0] = "C3",
        [1] = "C♯3",
        [2] = "D3",
        [3] = "D♯3",
        [4] = "E3",
        [5] = "F3",
        [6] = "F♯3",
        [7] = "G3",
        [8] = "G♯3",
        [9] = "A3",
        [10] = "A♯3",
        [11] = "B3",
        [12] = "C4",
        [13] = "C♯4",
        [14] = "D4",
        [15] = "D♯4",
        [16] = "E4",
        [17] = "F4",
        [18] = "F♯4",
        [19] = "G4",
        [20] = "G♯4",
        [21] = "A4",
        [22] = "A♯4",
        [23] = "B4",
        [24] = "C5",
        [25] = "C♯5",
        [26] = "D5",
        [27] = "D♯5"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int key)
        {
            return KeyOffsets.TryGetValue(key, out var note) ? note : key.ToString();
        }
        return "C3";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
