using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AutoMidiPlayer.Data;

namespace AutoMidiPlayer.WPF.Helpers;

public static class TextBlockLinkHelper
{
    public static readonly DependencyProperty TextWithLinksProperty =
        DependencyProperty.RegisterAttached("TextWithLinks", typeof(string), typeof(TextBlockLinkHelper),
            new PropertyMetadata(null, OnTextWithLinksChanged));

    public static string GetTextWithLinks(DependencyObject obj) => (string)obj.GetValue(TextWithLinksProperty);
    public static void SetTextWithLinks(DependencyObject obj, string value) => obj.SetValue(TextWithLinksProperty, value);

    private static readonly Regex _urlRegex = new(
        @"(https?://[^\s\)>]*[^\s\)>.,;])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void OnTextWithLinksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        textBlock.Inlines.Clear();
        var text = e.NewValue as string;
        if (string.IsNullOrEmpty(text)) return;

        var lastPos = 0;
        foreach (Match match in _urlRegex.Matches(text))
        {
            if (match.Index > lastPos)
            {
                textBlock.Inlines.Add(new Run(text.Substring(lastPos, match.Index - lastPos)));
            }

            var url = match.Value;
            var hyperlinkButton = new AutoMidiPlayer.WPF.Controls.Hyperlink
            {
                NavigateUri = new Uri(url),
                Content = new TextBlock 
                { 
                    Text = url,
                    FontFamily = textBlock.FontFamily,
                    FontSize = textBlock.FontSize,
                    FontWeight = textBlock.FontWeight,
                    FontStyle = textBlock.FontStyle
                },
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                MinHeight = 0,
                MinWidth = 0
            };
            hyperlinkButton.SetResourceReference(Control.ForegroundProperty, "SystemAccentColorPrimaryBrush");
            
            var inlineContainer = new InlineUIContainer(hyperlinkButton) 
            { 
                BaselineAlignment = BaselineAlignment.Center 
            };
            
            textBlock.Inlines.Add(inlineContainer);

            lastPos = match.Index + match.Length;
        }

        if (lastPos < text.Length)
        {
            textBlock.Inlines.Add(new Run(text.Substring(lastPos)));
        }
    }

    private static void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink hyperlink && hyperlink.NavigateUri != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(hyperlink.NavigateUri.ToString())
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
    }
}
