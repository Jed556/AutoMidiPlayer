using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoMidiPlayer.WPF.Converters;

public class DateTimeToTimeSpanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DateTime dateTime ? dateTime.TimeOfDay : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan timeSpan)
            return DateTime.Today.Add(timeSpan);

        return DateTime.Now;
    }
}
