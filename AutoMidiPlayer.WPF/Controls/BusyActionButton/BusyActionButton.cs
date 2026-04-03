using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Controls;

public partial class BusyActionButton : UserControl
{
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy),
        typeof(bool),
        typeof(BusyActionButton),
        new PropertyMetadata(false));

    public static readonly DependencyProperty MinimumVisibleDurationProperty = DependencyProperty.Register(
        nameof(MinimumVisibleDuration),
        typeof(TimeSpan),
        typeof(BusyActionButton),
        new PropertyMetadata(TimeSpan.FromSeconds(1)));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(BusyActionButton),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(BusyActionButton),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ButtonPaddingProperty = DependencyProperty.Register(
        nameof(ButtonPadding),
        typeof(Thickness),
        typeof(BusyActionButton),
        new PropertyMetadata(new Thickness(8)));

    public static readonly DependencyProperty ButtonToolTipProperty = DependencyProperty.Register(
        nameof(ButtonToolTip),
        typeof(object),
        typeof(BusyActionButton),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IconSymbolProperty = DependencyProperty.Register(
        nameof(IconSymbol),
        typeof(SymbolRegular),
        typeof(BusyActionButton),
        new PropertyMetadata(SymbolRegular.ArrowClockwise24));

    public static readonly DependencyProperty IconFontSizeProperty = DependencyProperty.Register(
        nameof(IconFontSize),
        typeof(double),
        typeof(BusyActionButton),
        new PropertyMetadata(18d));

    public static readonly DependencyProperty IconFilledProperty = DependencyProperty.Register(
        nameof(IconFilled),
        typeof(bool),
        typeof(BusyActionButton),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IndicatorSizeProperty = DependencyProperty.Register(
        nameof(IndicatorSize),
        typeof(double),
        typeof(BusyActionButton),
        new PropertyMetadata(16d));

    public BusyActionButton()
    {
        InitializeComponent();
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public TimeSpan MinimumVisibleDuration
    {
        get => (TimeSpan)GetValue(MinimumVisibleDurationProperty);
        set => SetValue(MinimumVisibleDurationProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public Thickness ButtonPadding
    {
        get => (Thickness)GetValue(ButtonPaddingProperty);
        set => SetValue(ButtonPaddingProperty, value);
    }

    public object? ButtonToolTip
    {
        get => GetValue(ButtonToolTipProperty);
        set => SetValue(ButtonToolTipProperty, value);
    }

    public SymbolRegular IconSymbol
    {
        get => (SymbolRegular)GetValue(IconSymbolProperty);
        set => SetValue(IconSymbolProperty, value);
    }

    public double IconFontSize
    {
        get => (double)GetValue(IconFontSizeProperty);
        set => SetValue(IconFontSizeProperty, value);
    }

    public bool IconFilled
    {
        get => (bool)GetValue(IconFilledProperty);
        set => SetValue(IconFilledProperty, value);
    }

    public double IndicatorSize
    {
        get => (double)GetValue(IndicatorSizeProperty);
        set => SetValue(IndicatorSizeProperty, value);
    }
}
