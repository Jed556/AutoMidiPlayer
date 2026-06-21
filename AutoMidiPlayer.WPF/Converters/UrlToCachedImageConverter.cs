using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace AutoMidiPlayer.WPF.Converters;

/// <summary>
/// Converts a remote thumbnail URL (string) into a shared <see cref="BitmapImage"/>.
/// Each distinct URL is downloaded and decoded once — at a small size, since thumbnails are
/// only ever shown in 50–64px circles — and the resulting image is cached in memory. This keeps
/// scrolling and paging the results list from re-downloading or re-decoding thumbnails, and
/// avoids holding full-resolution bitmaps in memory for tiny on-screen avatars.
/// </summary>
public sealed class UrlToCachedImageConverter : IValueConverter
{
    // 128px wide is ample for a 50–64px circle even on high-DPI displays.
    private const int DecodeWidth = 128;

    private static readonly ConcurrentDictionary<string, BitmapImage> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;

        return Cache.GetOrAdd(uri.AbsoluteUri, key =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(key, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;           // decode immediately, then release the source stream
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = DecodeWidth;                   // never keep the full-res image for a tiny circle
            bitmap.EndInit();

            // Remote URLs download asynchronously and can't be frozen until that finishes; the
            // cached instance still updates itself when the download completes. Local/cached
            // sources are ready immediately, so freeze those to drop thread affinity and copies.
            if (!bitmap.IsDownloading)
                bitmap.Freeze();

            return bitmap;
        });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
