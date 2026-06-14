using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace AutoMidiPlayer.WPF.Controls;

public partial class CopyableTextBox : UserControl
{
    private const double HoverOpacity = 0.64;
    private const double ActiveOpacity = 0.8;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(CopyableTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty DelimiterProperty = DependencyProperty.Register(
        nameof(Delimiter),
        typeof(string),
        typeof(CopyableTextBox),
        new PropertyMetadata("_", OnDelimiterChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(CopyableTextBox),
        new PropertyMetadata(true));

    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(
        nameof(TextWrapping),
        typeof(TextWrapping),
        typeof(CopyableTextBox),
        new PropertyMetadata(System.Windows.TextWrapping.NoWrap, OnTextWrappingChanged));

    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(CopyableTextBox),
        new PropertyMetadata(ScrollBarVisibility.Auto));

    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(HorizontalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(CopyableTextBox),
        new PropertyMetadata(ScrollBarVisibility.Auto));

    public static readonly DependencyProperty TextBoxMaxHeightProperty = DependencyProperty.Register(
        nameof(TextBoxMaxHeight),
        typeof(double),
        typeof(CopyableTextBox),
        new PropertyMetadata(double.PositiveInfinity));

    public static readonly DependencyProperty CopyToolTipProperty = DependencyProperty.Register(
        nameof(CopyToolTip),
        typeof(object),
        typeof(CopyableTextBox),
        new PropertyMetadata("Copy to clipboard"));

    public CopyableTextBox()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (MainRichTextBox is null || CopyButton is null)
            return;

        bool isScrollbarVisible = MainRichTextBox.VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                                 (MainRichTextBox.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                                  MainRichTextBox.ExtentHeight > MainRichTextBox.ViewportHeight);

        // WPF UI scrollbars are thinner than the standard system scrollbars (10px width)
        double targetRightMargin = isScrollbarVisible ? 16 : 4;

        if (Math.Abs(CopyButton.Margin.Right - targetRightMargin) > 0.1)
        {
            CopyButton.Margin = new Thickness(0, 4, targetRightMargin, 0);
        }
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Delimiter
    {
        get => (string)GetValue(DelimiterProperty);
        set => SetValue(DelimiterProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    public double TextBoxMaxHeight
    {
        get => (double)GetValue(TextBoxMaxHeightProperty);
        set => SetValue(TextBoxMaxHeightProperty, value);
    }

    public object? CopyToolTip
    {
        get => GetValue(CopyToolTipProperty);
        set => SetValue(CopyToolTipProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CopyableTextBox control)
        {
            control.UpdateDocument();
        }
    }

    private static void OnDelimiterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CopyableTextBox control)
        {
            control.UpdateDocument();
        }
    }

    private static void OnTextWrappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CopyableTextBox control)
        {
            control.UpdateDocument();
        }
    }

    private void UpdateDocument()
    {
        if (MainRichTextBox == null) return;

        var text = Text ?? string.Empty;
        var delimiterStr = Delimiter;

        var doc = new FlowDocument();
        
        if (TextWrapping == TextWrapping.NoWrap)
            doc.PageWidth = 10000;
        else
            doc.PageWidth = double.NaN;

        doc.PagePadding = new Thickness(0);
        var paragraph = new Paragraph { Margin = new Thickness(0) };

        if (!string.IsNullOrEmpty(text))
        {
            if (!string.IsNullOrEmpty(delimiterStr))
            {
                var delimiter = delimiterStr[0];
                var currentRun = new StringBuilder();
                bool isDelimiterRun = false;

                for (int i = 0; i < text.Length; i++)
                {
                    bool isDelimiter = text[i] == delimiter;

                    if (i == 0)
                    {
                        isDelimiterRun = isDelimiter;
                    }
                    else if (isDelimiter != isDelimiterRun)
                    {
                        var run = new Run(currentRun.ToString());
                        if (isDelimiterRun)
                        {
                            run.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorDisabledBrush");
                        }
                        paragraph.Inlines.Add(run);

                        currentRun.Clear();
                        isDelimiterRun = isDelimiter;
                    }

                    currentRun.Append(text[i]);
                }

                if (currentRun.Length > 0)
                {
                    var run = new Run(currentRun.ToString());
                    if (isDelimiterRun)
                    {
                        run.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorDisabledBrush");
                    }
                    paragraph.Inlines.Add(run);
                }
            }
            else
            {
                paragraph.Inlines.Add(new Run(text));
            }
        }

        doc.Blocks.Add(paragraph);
        MainRichTextBox.Document = doc;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(Text ?? string.Empty);
        e.Handled = true;
    }

    private void OnRootGridMouseEnter(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(HoverOpacity);
    }

    private void OnRootGridMouseLeave(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(0);
    }

    private void OnCopyButtonMouseEnter(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(ActiveOpacity);
    }

    private void OnCopyButtonMouseLeave(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(RootGrid.IsMouseOver ? HoverOpacity : 0);
    }

    private void AnimateCopyButtonOpacity(double targetOpacity)
    {
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        CopyButton.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
