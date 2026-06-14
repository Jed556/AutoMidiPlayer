using System;
using System.Windows;
using System.Windows.Controls;

namespace AutoMidiPlayer.WPF.Controls;

public partial class PedalStatusPopup : UserControl
{
    public static readonly DependencyProperty IsSoftActiveProperty =
        DependencyProperty.Register(
            nameof(IsSoftActive),
            typeof(bool),
            typeof(PedalStatusPopup),
            new PropertyMetadata(false));

    public bool IsSoftActive
    {
        get => (bool)GetValue(IsSoftActiveProperty);
        set => SetValue(IsSoftActiveProperty, value);
    }

    public static readonly DependencyProperty IsSostenutoActiveProperty =
        DependencyProperty.Register(
            nameof(IsSostenutoActive),
            typeof(bool),
            typeof(PedalStatusPopup),
            new PropertyMetadata(false));

    public bool IsSostenutoActive
    {
        get => (bool)GetValue(IsSostenutoActiveProperty);
        set => SetValue(IsSostenutoActiveProperty, value);
    }

    public static readonly DependencyProperty IsSustainActiveProperty =
        DependencyProperty.Register(
            nameof(IsSustainActive),
            typeof(bool),
            typeof(PedalStatusPopup),
            new PropertyMetadata(false));

    public bool IsSustainActive
    {
        get => (bool)GetValue(IsSustainActiveProperty);
        set => SetValue(IsSustainActiveProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(PedalStatusPopup),
            new PropertyMetadata(false));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public PedalStatusPopup()
    {
        InitializeComponent();
        Opacity = 0; // Start hidden
    }

    public void PlayEntranceAnimation()
    {
        Opacity = 0;
        EntranceScale.ScaleX = 0.85;
        EntranceScale.ScaleY = 0.85;
        EntranceTranslate.Y = 8;

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        fadeIn.Completed += (_, _) => Opacity = 1;

        var scaleIn = new System.Windows.Media.Animation.DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        scaleIn.Completed += (_, _) =>
        {
            EntranceScale.ScaleX = 1;
            EntranceScale.ScaleY = 1;
        };

        var translateIn = new System.Windows.Media.Animation.DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        translateIn.Completed += (_, _) => EntranceTranslate.Y = 0;

        BeginAnimation(OpacityProperty, fadeIn, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleIn, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleIn, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        EntranceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, translateIn, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    public void PlayExitAnimation(Action onComplete)
    {
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        fadeOut.Completed += (_, _) =>
        {
            Opacity = 0;
            onComplete?.Invoke();
        };

        var scaleOut = new System.Windows.Media.Animation.DoubleAnimation(EntranceScale.ScaleX, 0.85, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        scaleOut.Completed += (_, _) =>
        {
            EntranceScale.ScaleX = 0.85;
            EntranceScale.ScaleY = 0.85;
        };

        var translateOut = new System.Windows.Media.Animation.DoubleAnimation(EntranceTranslate.Y, 8, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        translateOut.Completed += (_, _) => EntranceTranslate.Y = 8;

        BeginAnimation(OpacityProperty, fadeOut, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleOut, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleOut, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        EntranceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, translateOut, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }
}
