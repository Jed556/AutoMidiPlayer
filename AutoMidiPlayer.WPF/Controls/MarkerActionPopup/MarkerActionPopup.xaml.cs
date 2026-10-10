using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AutoMidiPlayer.Data.Entities;

namespace AutoMidiPlayer.WPF.Controls;

public partial class MarkerActionPopup : UserControl
{
    private double _translateYStart = 8;

    public KeyChangeMarker? CurrentMarker { get; private set; }

    public event EventHandler<KeyChangeMarker>? DeleteRequested;
    public event EventHandler<KeyChangeMarker>? EditRequested;

    public MarkerActionPopup()
    {
        InitializeComponent();
        Opacity = 0;
    }

    public bool IsMarkerSelected
    {
        get => EditButton.ColorMode == ButtonColorMode.Accent;
        set
        {
            EditButton.ColorMode = value ? ButtonColorMode.Accent : ButtonColorMode.Default;
            EditButton.ToolTip = value ? "Deselect marker" : "Edit marker";
        }
    }

    public void SetMarker(KeyChangeMarker marker, bool isAbove, bool isSelected = false)
    {
        CurrentMarker = marker;
        MarkerTitleText.Text = !string.IsNullOrWhiteSpace(marker.Label) ? marker.Label : marker.NoteDisplay;
        MarkerTimeText.Text = marker.TimeString;
        IsMarkerSelected = isSelected;

        if (isAbove)
        {
            RenderTransformOrigin = new Point(0.5, 1.0);
            OuterBorder.Padding = new Thickness(0, 0, 0, 6);
            _translateYStart = 8;
        }
        else
        {
            RenderTransformOrigin = new Point(0.5, 0.0);
            OuterBorder.Padding = new Thickness(0, 6, 0, 0);
            _translateYStart = -8;
        }
    }

    public void PlayEntranceAnimation()
    {
        Opacity = 0;
        EntranceScale.ScaleX = 0.85;
        EntranceScale.ScaleY = 0.85;
        EntranceTranslate.Y = _translateYStart;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        fadeIn.Completed += (_, _) => Opacity = 1;

        var scaleIn = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        scaleIn.Completed += (_, _) =>
        {
            EntranceScale.ScaleX = 1;
            EntranceScale.ScaleY = 1;
        };

        var translateIn = new DoubleAnimation(_translateYStart, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        translateIn.Completed += (_, _) => EntranceTranslate.Y = 0;

        BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleIn, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleIn, HandoffBehavior.SnapshotAndReplace);
        EntranceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, translateIn, HandoffBehavior.SnapshotAndReplace);
    }

    public void PlayExitAnimation(Action onComplete)
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        fadeOut.Completed += (_, _) =>
        {
            Opacity = 0;
            onComplete?.Invoke();
        };

        var scaleOut = new DoubleAnimation(EntranceScale.ScaleX, 0.85, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };

        var translateOut = new DoubleAnimation(EntranceTranslate.Y, _translateYStart, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };

        BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleOut, HandoffBehavior.SnapshotAndReplace);
        EntranceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleOut, HandoffBehavior.SnapshotAndReplace);
        EntranceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, translateOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (CurrentMarker != null)
        {
            DeleteRequested?.Invoke(this, CurrentMarker);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (CurrentMarker != null)
        {
            IsMarkerSelected = !IsMarkerSelected;
            EditRequested?.Invoke(this, CurrentMarker);
        }
    }
}
