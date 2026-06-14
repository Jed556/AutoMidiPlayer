using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Controls.Snackbar;

/// <summary>
/// An individual snackbar notification card with icon, text, progress bar, close button, and action buttons.
/// </summary>
public partial class SnackbarItem : UserControl
{
    private DispatcherTimer? _autoCloseTimer;
    private Storyboard? _progressStoryboard;
    private bool _isClosing;

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(SnackbarSeverity), typeof(SnackbarItem),
        new PropertyMetadata(SnackbarSeverity.Info, OnSeverityChanged));

    public static readonly DependencyProperty IconSymbolProperty = DependencyProperty.Register(
        nameof(IconSymbol), typeof(SymbolRegular?), typeof(SnackbarItem),
        new PropertyMetadata(null, OnIconPropertyChanged));

    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
        nameof(IconSource), typeof(ImageSource), typeof(SnackbarItem),
        new PropertyMetadata(null, OnIconPropertyChanged));

    public static readonly DependencyProperty PrimaryTextProperty = DependencyProperty.Register(
        nameof(PrimaryText), typeof(string), typeof(SnackbarItem),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryTextProperty = DependencyProperty.Register(
        nameof(SecondaryText), typeof(string), typeof(SnackbarItem),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan), typeof(SnackbarItem),
        new PropertyMetadata(TimeSpan.FromSeconds(5)));

    public static readonly DependencyProperty ShowProgressBarProperty = DependencyProperty.Register(
        nameof(ShowProgressBar), typeof(bool), typeof(SnackbarItem),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowCloseButtonProperty = DependencyProperty.Register(
        nameof(ShowCloseButton), typeof(bool), typeof(SnackbarItem),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ActionButtonsProperty = DependencyProperty.Register(
        nameof(ActionButtons), typeof(IList<SnackbarActionButton>), typeof(SnackbarItem),
        new PropertyMetadata(null));

    public SnackbarSeverity Severity
    {
        get => (SnackbarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public SymbolRegular? IconSymbol
    {
        get => (SymbolRegular?)GetValue(IconSymbolProperty);
        set => SetValue(IconSymbolProperty, value);
    }

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public string PrimaryText
    {
        get => (string)GetValue(PrimaryTextProperty);
        set => SetValue(PrimaryTextProperty, value);
    }

    public string SecondaryText
    {
        get => (string)GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public bool ShowProgressBar
    {
        get => (bool)GetValue(ShowProgressBarProperty);
        set => SetValue(ShowProgressBarProperty, value);
    }

    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    public IList<SnackbarActionButton>? ActionButtons
    {
        get => (IList<SnackbarActionButton>?)GetValue(ActionButtonsProperty);
        set => SetValue(ActionButtonsProperty, value);
    }

    // Resolved icon/color for binding from XAML.
    public SymbolRegular ResolvedIcon => IconSymbol ?? SeverityToIcon(Severity);
    public Brush ResolvedIconRingBrush => IconSource != null
        ? (Brush)FindResource("SystemAccentColorPrimaryBrush")
        : SeverityToRingBrush(Severity);
    public Visibility SymbolIconVisibility => IconSource == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImageIconVisibility => IconSource != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SecondaryTextVisibility => string.IsNullOrEmpty(SecondaryText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ActionButtonsVisibility => ActionButtons is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProgressBarVisibility => ShowProgressBar ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CloseButtonVisibility => ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Raised when the snackbar finishes its close animation and should be removed from the container.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised when the snackbar begins its close animation and should be removed from the logical stack.</summary>
    public event EventHandler? Closing;

    public SnackbarItem()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlayEntranceAnimation();
        StartAutoCloseTimer();
        StartProgressBarAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoCloseTimer();
        _progressStoryboard?.Stop();
        _progressStoryboard = null;
    }

    /// <summary>
    /// Initiates the close sequence: fade-out animation followed by raising <see cref="Closed"/>.
    /// Safe to call multiple times — only the first call takes effect.
    /// </summary>
    public void Close()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        StopAutoCloseTimer();
        _progressStoryboard?.Stop();

        Closing?.Invoke(this, EventArgs.Empty);
        PlayExitAnimation(() => Closed?.Invoke(this, EventArgs.Empty));
    }

    public void OnCloseButtonClick(object sender, RoutedEventArgs e) => Close();

    // --- Auto-close timer ---

    private void StartAutoCloseTimer()
    {
        if (Duration <= TimeSpan.Zero)
            return;

        _autoCloseTimer = new DispatcherTimer { Interval = Duration };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();
    }

    private void StopAutoCloseTimer()
    {
        if (_autoCloseTimer is null)
            return;

        _autoCloseTimer.Stop();
        _autoCloseTimer = null;
    }

    // --- Progress bar animation ---

    private void StartProgressBarAnimation()
    {
        if (!ShowProgressBar || Duration <= TimeSpan.Zero)
            return;

        var animation = new DoubleAnimation(1.0, 0.0, new Duration(Duration))
        {
            FillBehavior = FillBehavior.HoldEnd
        };

        ProgressBarScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
    }

    // --- Entrance/exit animations ---

    private void PlayEntranceAnimation()
    {
        Opacity = 0;
        EntranceScale.ScaleX = 0.85;
        EntranceScale.ScaleY = 0.85;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        fadeIn.Completed += (_, _) => Opacity = 1;

        var scaleIn = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        scaleIn.Completed += (_, _) =>
        {
            EntranceScale.ScaleX = 1;
            EntranceScale.ScaleY = 1;
        };

        BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn, HandoffBehavior.SnapshotAndReplace);
    }

    private void PlayExitAnimation(Action onComplete)
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        fadeOut.Completed += (_, _) =>
        {
            Opacity = 0;
            onComplete();
        };

        var scaleOut = new DoubleAnimation(EntranceScale.ScaleX, 0.85, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        scaleOut.Completed += (_, _) =>
        {
            EntranceScale.ScaleX = 0.85;
            EntranceScale.ScaleY = 0.85;
        };

        BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut, HandoffBehavior.SnapshotAndReplace);
    }

    // --- Severity → icon/color mapping ---

    private static SymbolRegular SeverityToIcon(SnackbarSeverity severity) => severity switch
    {
        SnackbarSeverity.Success => SymbolRegular.CheckmarkCircle24,
        SnackbarSeverity.Warning => SymbolRegular.Warning24,
        SnackbarSeverity.Danger => SymbolRegular.ErrorCircle24,
        _ => SymbolRegular.Info24
    };

    private Brush SeverityToRingBrush(SnackbarSeverity severity)
    {
        return severity switch
        {
            SnackbarSeverity.Success => new SolidColorBrush(Color.FromRgb(108, 203, 95)),   // green
            SnackbarSeverity.Warning => new SolidColorBrush(Color.FromRgb(252, 185, 65)),   // amber
            SnackbarSeverity.Danger => new SolidColorBrush(Color.FromRgb(232, 80, 80)),     // red
            _ => (Brush)FindResource("SystemAccentColorPrimaryBrush")                        // accent blue
        };
    }

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SnackbarItem item)
            return;

        item.NotifyVisualPropertiesChanged();
    }

    private static void OnIconPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SnackbarItem item)
            return;

        item.NotifyVisualPropertiesChanged();
    }

    private void NotifyVisualPropertiesChanged()
    {
        // These are CLR-computed properties bound in XAML via {Binding}.
        // Force the UI to re-read them after the backing dependency property changes.
        var dp = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            SeverityProperty, typeof(SnackbarItem));

        // Brute-force: re-set DataContext to trigger all bindings.
        // This is safe because DataContext == this and nothing external binds to it.
        DataContext = null;
        DataContext = this;
    }
}
