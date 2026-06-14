using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace AutoMidiPlayer.WPF.Controls.Snackbar;

/// <summary>
/// Multiplies the input value by a percentage factor specified in <see cref="IValueConverter.Convert"/>'s parameter.
/// Usage: <c>Converter={snackbar:PercentageConverter}, ConverterParameter=0.75</c>
/// </summary>
[System.Windows.Localizability(System.Windows.LocalizationCategory.NeverLocalize)]
public sealed class PercentageConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double inputValue || double.IsNaN(inputValue))
            return 0d;

        var factor = 1.0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            factor = parsed;
        else if (parameter is double d)
            factor = d;

        return inputValue * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
