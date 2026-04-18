using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AutoMidiPlayer.WPF.Controls;

[ContentProperty(nameof(ChildContent))]
public partial class Section : UserControl
{
    public static readonly DependencyProperty ChildContentProperty =
        DependencyProperty.Register(nameof(ChildContent), typeof(object), typeof(Section),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Section),
            new PropertyMetadata(string.Empty, OnHeaderVisualPropertyChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(Section),
            new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(Section),
            new PropertyMetadata(null, OnHeaderVisualPropertyChanged));

    public static readonly DependencyProperty TitleFontSizeProperty =
        DependencyProperty.Register(nameof(TitleFontSize), typeof(double), typeof(Section),
            new PropertyMetadata(20d));

    public static readonly DependencyProperty HasHeaderProperty =
        DependencyProperty.Register(nameof(HasHeader), typeof(bool), typeof(Section),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasDescriptionProperty =
        DependencyProperty.Register(nameof(HasDescription), typeof(bool), typeof(Section),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasHeaderContentProperty =
        DependencyProperty.Register(nameof(HasHeaderContent), typeof(bool), typeof(Section),
            new PropertyMetadata(false));

    public Section()
    {
        InitializeComponent();
        UpdateHeaderState();
        UpdateDescriptionState();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? ChildContent
    {
        get => GetValue(ChildContentProperty);
        set => SetValue(ChildContentProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    public bool HasHeader
    {
        get => (bool)GetValue(HasHeaderProperty);
        private set => SetValue(HasHeaderProperty, value);
    }

    public bool HasDescription
    {
        get => (bool)GetValue(HasDescriptionProperty);
        private set => SetValue(HasDescriptionProperty, value);
    }

    public bool HasHeaderContent
    {
        get => (bool)GetValue(HasHeaderContentProperty);
        private set => SetValue(HasHeaderContentProperty, value);
    }

    private static void OnHeaderVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is Section section)
        {
            section.UpdateHeaderState();
        }
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is Section section)
        {
            section.UpdateDescriptionState();
        }
    }

    private void UpdateHeaderState()
    {
        HasHeaderContent = HeaderContent is not null;
        HasHeader = HasHeaderContent || !string.IsNullOrWhiteSpace(Title);
    }

    private void UpdateDescriptionState()
    {
        HasDescription = !string.IsNullOrWhiteSpace(Description);
    }
}
