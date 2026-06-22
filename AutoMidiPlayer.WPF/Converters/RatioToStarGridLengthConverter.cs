using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoMidiPlayer.WPF.Converters;

public class RatioToStarGridLengthConverter : IValueConverter
{
    public static RatioToStarGridLengthConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0)
                ratio = 0;
            return new GridLength(ratio, GridUnitType.Star);
        }

        return new GridLength(0, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
