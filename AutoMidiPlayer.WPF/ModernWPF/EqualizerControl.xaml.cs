using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AutoMidiPlayer.WPF.ModernWPF;

public partial class EqualizerControl : UserControl
{
    private Storyboard? _animation;
    private bool _isAnimating;

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(EqualizerControl),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public EqualizerControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CreateAnimation();
        if (IsPlaying && IsVisible)
        {
            StartAnimation();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAnimation();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && IsPlaying)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EqualizerControl control)
        {
            if ((bool)e.NewValue && control.IsVisible)
            {
                control.StartAnimation();
            }
            else
            {
                control.StopAnimation();
            }
        }
    }

    private void CreateAnimation()
    {
        if (_animation != null) return;

        _animation = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        // Bar 1 animation
        var anim1 = new DoubleAnimation
        {
            From = 4,
            To = 14,
            Duration = TimeSpan.FromMilliseconds(300),
            AutoReverse = true
        };
        Storyboard.SetTarget(anim1, Bar1);
        Storyboard.SetTargetProperty(anim1, new PropertyPath(HeightProperty));
        _animation.Children.Add(anim1);

        // Bar 2 animation
        var anim2 = new DoubleAnimation
        {
            From = 10,
            To = 4,
            Duration = TimeSpan.FromMilliseconds(250),
            AutoReverse = true,
            BeginTime = TimeSpan.FromMilliseconds(100)
        };
        Storyboard.SetTarget(anim2, Bar2);
        Storyboard.SetTargetProperty(anim2, new PropertyPath(HeightProperty));
        _animation.Children.Add(anim2);

        // Bar 3 animation
        var anim3 = new DoubleAnimation
        {
            From = 6,
            To = 16,
            Duration = TimeSpan.FromMilliseconds(350),
            AutoReverse = true,
            BeginTime = TimeSpan.FromMilliseconds(50)
        };
        Storyboard.SetTarget(anim3, Bar3);
        Storyboard.SetTargetProperty(anim3, new PropertyPath(HeightProperty));
        _animation.Children.Add(anim3);

        // Bar 4 animation
        var anim4 = new DoubleAnimation
        {
            From = 12,
            To = 6,
            Duration = TimeSpan.FromMilliseconds(280),
            AutoReverse = true,
            BeginTime = TimeSpan.FromMilliseconds(150)
        };
        Storyboard.SetTarget(anim4, Bar4);
        Storyboard.SetTargetProperty(anim4, new PropertyPath(HeightProperty));
        _animation.Children.Add(anim4);
    }

    private void StartAnimation()
    {
        if (_isAnimating || _animation == null) return;

        try
        {
            _animation.Begin(BarsGrid, true);
            _isAnimating = true;
        }
        catch
        {
            // Animation might fail during layout changes
        }
    }

    private void StopAnimation()
    {
        if (!_isAnimating || _animation == null) return;

        try
        {
            _animation.Stop(BarsGrid);
            _isAnimating = false;

            // Reset bar heights
            Bar1.Height = 4;
            Bar2.Height = 8;
            Bar3.Height = 6;
            Bar4.Height = 10;
        }
        catch
        {
            // Animation might fail during layout changes
        }
    }
}
