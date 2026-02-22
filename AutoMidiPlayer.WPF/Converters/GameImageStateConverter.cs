using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoMidiPlayer.WPF.Converters;

public class GameImageStateConverter : IMultiValueConverter
{
    private static readonly ConcurrentDictionary<string, BitmapSource> ColorCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, BitmapSource> GrayCache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return Binding.DoNothing;

        var sourcePath = values[0] as string;
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Binding.DoNothing;

        var isActive = values[1] is bool active && active;

        var key = sourcePath.Trim();
        return isActive
            ? GetColorImage(key)
            : GetGrayImage(key);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static BitmapSource GetColorImage(string sourcePath)
    {
        return ColorCache.GetOrAdd(sourcePath, path =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        });
    }

    private static BitmapSource GetGrayImage(string sourcePath)
    {
        return GrayCache.GetOrAdd(sourcePath, path =>
        {
            var color = GetColorImage(path);
            var gray = new FormatConvertedBitmap();
            gray.BeginInit();
            gray.Source = color;
            gray.DestinationFormat = PixelFormats.Gray8;
            gray.EndInit();
            gray.Freeze();
            return gray;
        });
    }
}
