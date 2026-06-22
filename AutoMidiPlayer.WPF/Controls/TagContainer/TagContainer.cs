using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoMidiPlayer.WPF.Controls;

public partial class TagContainer : UserControl
{
    public static readonly DependencyProperty TagsProperty =
        DependencyProperty.Register("Tags", typeof(IEnumerable<string>), typeof(TagContainer),
            new PropertyMetadata(null, OnTagsChanged));

    public IEnumerable<string> Tags
    {
        get => (IEnumerable<string>)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public static readonly DependencyProperty VisibleTagsProperty =
        DependencyProperty.Register("VisibleTags", typeof(IEnumerable<string>), typeof(TagContainer), new PropertyMetadata(null));

    public IEnumerable<string> VisibleTags
    {
        get => (IEnumerable<string>)GetValue(VisibleTagsProperty);
        private set => SetValue(VisibleTagsProperty, value);
    }

    public static readonly DependencyProperty OverflowTagsProperty =
        DependencyProperty.Register("OverflowTags", typeof(IEnumerable<string>), typeof(TagContainer), new PropertyMetadata(null));

    public IEnumerable<string> OverflowTags
    {
        get => (IEnumerable<string>)GetValue(OverflowTagsProperty);
        private set => SetValue(OverflowTagsProperty, value);
    }

    public static readonly DependencyProperty OverflowCountProperty =
        DependencyProperty.Register("OverflowCount", typeof(int), typeof(TagContainer), new PropertyMetadata(0));

    public int OverflowCount
    {
        get => (int)GetValue(OverflowCountProperty);
        private set => SetValue(OverflowCountProperty, value);
    }

    public static readonly DependencyProperty HasOverflowProperty =
        DependencyProperty.Register("HasOverflow", typeof(bool), typeof(TagContainer), new PropertyMetadata(false));

    public bool HasOverflow
    {
        get => (bool)GetValue(HasOverflowProperty);
        private set => SetValue(HasOverflowProperty, value);
    }

    public TagContainer()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
    }

    private static void OnTagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TagContainer)d).Recalculate();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Recalculate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Recalculate();
    }

    private void Recalculate()
    {
        if (Tags == null || !Tags.Any())
        {
            VisibleTags = Array.Empty<string>();
            OverflowTags = Array.Empty<string>();
            OverflowCount = 0;
            HasOverflow = false;
            return;
        }

        var tagsList = Tags.ToList();
        double availableWidth = ActualWidth;
        
        if (availableWidth <= 0 || !IsLoaded)
        {
            // Fallback before layout
            VisibleTags = tagsList;
            OverflowTags = Array.Empty<string>();
            OverflowCount = 0;
            HasOverflow = false;
            return;
        }

        Typeface typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        double CalculateTextWidth(string text)
        {
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                10.5,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return formattedText.Width + 18 + 6; // Padding (9+9) + Margin.Right (6)
        }

        double totalWidth = 0;
        int visibleCount = 0;

        foreach (var tag in tagsList)
        {
            totalWidth += CalculateTextWidth(tag);
        }

        if (totalWidth <= availableWidth)
        {
            VisibleTags = tagsList;
            OverflowTags = Array.Empty<string>();
            OverflowCount = 0;
            HasOverflow = false;
            return;
        }

        double currentWidth = 0;
        for (int i = 0; i < tagsList.Count; i++)
        {
            string tag = tagsList[i];
            double tagWidth = CalculateTextWidth(tag);
            int remaining = tagsList.Count - (i + 1);
            double indicatorWidth = CalculateTextWidth($"+{remaining + 1}"); 

            if (currentWidth + tagWidth + indicatorWidth <= availableWidth)
            {
                currentWidth += tagWidth;
                visibleCount++;
            }
            else
            {
                break;
            }
        }

        if (visibleCount == 0 && tagsList.Count > 0)
        {
            visibleCount = 1; 
        }

        VisibleTags = tagsList.Take(visibleCount).ToList();
        OverflowTags = tagsList.Skip(visibleCount).ToList();
        OverflowCount = OverflowTags.Count();
        HasOverflow = OverflowCount > 0;
    }
}
