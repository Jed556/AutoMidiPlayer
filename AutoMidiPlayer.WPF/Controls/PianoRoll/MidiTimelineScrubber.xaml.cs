using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

public partial class MidiTimelineScrubber : UserControl
{
    private bool _isDraggingScrubber;

    public event EventHandler<TimeSpan>? ScrubRequested;
    public event EventHandler<KeyChangeMarker>? MarkerClicked;
    public event EventHandler<double>? ZoomAdjustRequested;
    public event EventHandler? ZoomResetRequested;
    public event EventHandler<TimeSpan?>? HoverTimeChanged;
    public event EventHandler<bool>? SyncChanged;

    public static readonly DependencyProperty IsSyncEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSyncEnabled),
            typeof(bool),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(true, (d, e) =>
            {
                if (d is MidiTimelineScrubber scrubber)
                {
                    scrubber.UpdateSyncButtonState((bool)e.NewValue);
                }
            }));

    public bool IsSyncEnabled
    {
        get => (bool)GetValue(IsSyncEnabledProperty);
        set => SetValue(IsSyncEnabledProperty, value);
    }

    public static readonly DependencyProperty ViewTimeProperty =
        DependencyProperty.Register(
            nameof(ViewTime),
            typeof(TimeSpan?),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is MidiTimelineScrubber scrubber && scrubber.Canvas != null)
                {
                    scrubber.Canvas.ViewTime = (TimeSpan?)e.NewValue;
                }
            }));

    public TimeSpan? ViewTime
    {
        get => (TimeSpan?)GetValue(ViewTimeProperty);
        set => SetValue(ViewTimeProperty, value);
    }

    public static readonly DependencyProperty HoverTimeProperty =
        DependencyProperty.Register(
            nameof(HoverTime),
            typeof(TimeSpan?),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is MidiTimelineScrubber scrubber && scrubber.Canvas != null)
                {
                    scrubber.Canvas.HoverTime = (TimeSpan?)e.NewValue;
                }
            }));

    public TimeSpan? HoverTime
    {
        get => (TimeSpan?)GetValue(HoverTimeProperty);
        set => SetValue(HoverTimeProperty, value);
    }

    public static readonly DependencyProperty MidiFileProperty =
        DependencyProperty.Register(
            nameof(MidiFile),
            typeof(MidiFile),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is MidiTimelineScrubber scrubber && scrubber.Canvas != null)
                {
                    scrubber.Canvas.MidiFile = (MidiFile?)e.NewValue;
                }
            }));

    public MidiFile? MidiFile
    {
        get => (MidiFile?)GetValue(MidiFileProperty);
        set => SetValue(MidiFileProperty, value);
    }

    public static readonly DependencyProperty CurrentTimeProperty =
        DependencyProperty.Register(
            nameof(CurrentTime),
            typeof(TimeSpan),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(TimeSpan.Zero, (d, e) => ((MidiTimelineScrubber)d).Canvas.CurrentTime = (TimeSpan)e.NewValue));

    public TimeSpan CurrentTime
    {
        get => (TimeSpan)GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(
            nameof(Duration),
            typeof(TimeSpan),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(TimeSpan.FromSeconds(1), (d, e) => ((MidiTimelineScrubber)d).Canvas.Duration = (TimeSpan)e.NewValue));

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty KeyChangeMarkersProperty =
        DependencyProperty.Register(
            nameof(KeyChangeMarkers),
            typeof(IEnumerable<KeyChangeMarker>),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(null, (d, e) => ((MidiTimelineScrubber)d).Canvas.KeyChangeMarkers = (IEnumerable<KeyChangeMarker>)e.NewValue));

    public IEnumerable<KeyChangeMarker>? KeyChangeMarkers
    {
        get => (IEnumerable<KeyChangeMarker>?)GetValue(KeyChangeMarkersProperty);
        set => SetValue(KeyChangeMarkersProperty, value);
    }

    public static readonly DependencyProperty SelectedMarkerProperty =
        DependencyProperty.Register(
            nameof(SelectedMarker),
            typeof(KeyChangeMarker),
            typeof(MidiTimelineScrubber),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((MidiTimelineScrubber)d).Canvas.SelectedMarker = (KeyChangeMarker)e.NewValue));

    public KeyChangeMarker? SelectedMarker
    {
        get => (KeyChangeMarker?)GetValue(SelectedMarkerProperty);
        set => SetValue(SelectedMarkerProperty, value);
    }

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel),
            typeof(double),
            typeof(MidiTimelineScrubber),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomLevelChanged));

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public static readonly DependencyProperty VisiblePastMsProperty =
        DependencyProperty.Register(
            nameof(VisiblePastMs),
            typeof(long),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(0L, (d, e) => ((MidiTimelineScrubber)d).Canvas.VisiblePastMs = (long)e.NewValue));

    public long VisiblePastMs
    {
        get => (long)GetValue(VisiblePastMsProperty);
        set => SetValue(VisiblePastMsProperty, value);
    }

    public static readonly DependencyProperty VisibleHorizonMsProperty =
        DependencyProperty.Register(
            nameof(VisibleHorizonMs),
            typeof(long),
            typeof(MidiTimelineScrubber),
            new PropertyMetadata(6000L, (d, e) => ((MidiTimelineScrubber)d).Canvas.VisibleHorizonMs = (long)e.NewValue));

    public long VisibleHorizonMs
    {
        get => (long)GetValue(VisibleHorizonMsProperty);
        set => SetValue(VisibleHorizonMsProperty, value);
    }

    public static readonly DependencyProperty KeyHeightProperty =
        DependencyProperty.Register(
            nameof(KeyHeight),
            typeof(double),
            typeof(MidiTimelineScrubber),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnKeyHeightChanged));

    public double KeyHeight
    {
        get => (double)GetValue(KeyHeightProperty);
        set => SetValue(KeyHeightProperty, value);
    }

    public event EventHandler<double>? KeyHeightAdjustRequested;
    public event EventHandler? KeyHeightResetRequested;

    private static void OnKeyHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MidiTimelineScrubber scrubber)
        {
            var val = (double)e.NewValue;
            scrubber.UpdateKeyHeightText(val);
        }
    }

    private void UpdateKeyHeightText(double height)
    {
        if (KeyHeightText != null)
        {
            KeyHeightText.Text = $"{Math.Round(height)}px";
        }
    }

    private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MidiTimelineScrubber scrubber)
        {
            var val = (double)e.NewValue;
            scrubber.ZoomText.Text = $"{Math.Round(val * 100)}%";
        }
    }

    public MidiTimelineScrubber()
    {
        InitializeComponent();
        ZoomText.Text = "100%";
        UpdateKeyHeightText(KeyHeight);
        Loaded += (_, _) =>
        {
            UpdateSyncButtonState(IsSyncEnabled);
            UpdateKeyHeightText(KeyHeight);
            if (Canvas != null && Canvas.MidiFile != MidiFile)
            {
                Canvas.MidiFile = MidiFile;
            }
            Canvas?.RebuildMinimap();
        };
    }

    public void RefreshMinimap()
    {
        Canvas.RebuildMinimap();
    }

    private void OnScrubberMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(Canvas);

        // Check if user clicked on a marker
        var clickedMarker = Canvas.HitTestMarker(pos);
        if (clickedMarker != null)
        {
            SelectedMarker = clickedMarker;
            MarkerClicked?.Invoke(this, clickedMarker);
            // Also seek to the marker's time
            ScrubRequested?.Invoke(this, TimeSpan.FromMilliseconds(clickedMarker.TimeMs));
            return;
        }

        // Otherwise start scrubbing
        _isDraggingScrubber = true;
        ScrubberArea.CaptureMouse();
        SeekToPosition(pos.X);
    }

    private void OnScrubberMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Canvas);
        if (_isDraggingScrubber && ScrubberArea.IsMouseCaptured)
        {
            SeekToPosition(pos.X);
        }

        if (Canvas != null && Canvas.ActualWidth > 0 && pos.X >= 0 && pos.X <= Canvas.ActualWidth)
        {
            var width = Canvas.ActualWidth;
            var ratio = Math.Clamp(pos.X / width, 0.0, 1.0);
            var totalMs = Canvas.TotalDurationMs;
            var targetMs = ratio * totalMs;
            var time = TimeSpan.FromMilliseconds(targetMs);
            Canvas.HoverTime = time;
            HoverTimeChanged?.Invoke(this, time);
        }
        else
        {
            if (Canvas != null) Canvas.HoverTime = null;
            HoverTimeChanged?.Invoke(this, null);
        }
    }

    private void OnScrubberMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingScrubber)
        {
            if (Canvas != null) Canvas.HoverTime = null;
            HoverTimeChanged?.Invoke(this, null);
        }
    }

    private void OnScrubberMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingScrubber)
        {
            _isDraggingScrubber = false;
            ScrubberArea.ReleaseMouseCapture();

            if (Canvas != null)
            {
                var pos = e.GetPosition(Canvas);
                if (pos.X < 0 || pos.X > Canvas.ActualWidth || pos.Y < 0 || pos.Y > Canvas.ActualHeight)
                {
                    Canvas.HoverTime = null;
                    HoverTimeChanged?.Invoke(this, null);
                }
            }
        }
    }

    private void SeekToPosition(double mouseX)
    {
        var width = Canvas.ActualWidth;
        if (width <= 0) return;

        var ratio = Math.Clamp(mouseX / width, 0.0, 1.0);
        var totalMs = Canvas != null ? Canvas.TotalDurationMs : (long)Duration.TotalMilliseconds;
        var targetMs = ratio * totalMs;
        ScrubRequested?.Invoke(this, TimeSpan.FromMilliseconds(targetMs));
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e)
    {
        if (ZoomAdjustRequested != null)
        {
            ZoomAdjustRequested.Invoke(this, 0.25);
        }
        else
        {
            ZoomLevel = Math.Clamp(ZoomLevel + 0.25, 0.2, 5.0);
        }
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e)
    {
        if (ZoomAdjustRequested != null)
        {
            ZoomAdjustRequested.Invoke(this, -0.25);
        }
        else
        {
            ZoomLevel = Math.Clamp(ZoomLevel - 0.25, 0.2, 5.0);
        }
    }

    private void OnZoomTextClick(object sender, MouseButtonEventArgs e)
    {
        ZoomResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSyncButtonClick(object sender, RoutedEventArgs e)
    {
        IsSyncEnabled = !IsSyncEnabled;
        UpdateSyncButtonState(IsSyncEnabled);
        SyncChanged?.Invoke(this, IsSyncEnabled);
    }

    public void UpdateSyncButtonState(bool isSync)
    {
        if (SyncButton != null)
        {
            SyncButton.Appearance = isSync ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            SyncButton.ToolTip = isSync
                ? "Follow Playhead: Locked (Click to unlock view)"
                : "Follow Playhead: Unlocked (Click to lock to playhead)";
        }
        if (SyncIcon != null)
        {
            SyncIcon.Symbol = isSync ? Wpf.Ui.Controls.SymbolRegular.LockClosed16 : Wpf.Ui.Controls.SymbolRegular.LockOpen16;
        }
    }

    private void OnKeyHeightDownClick(object sender, RoutedEventArgs e)
    {
        if (KeyHeightAdjustRequested != null)
        {
            KeyHeightAdjustRequested.Invoke(this, -2.0);
        }
        else
        {
            KeyHeight = Math.Clamp(KeyHeight - 2.0, 6.0, 50.0);
        }
    }

    private void OnKeyHeightUpClick(object sender, RoutedEventArgs e)
    {
        if (KeyHeightAdjustRequested != null)
        {
            KeyHeightAdjustRequested.Invoke(this, 2.0);
        }
        else
        {
            KeyHeight = Math.Clamp(KeyHeight + 2.0, 6.0, 50.0);
        }
    }

    private void OnKeyHeightTextClick(object sender, MouseButtonEventArgs e)
    {
        if (KeyHeightResetRequested != null)
        {
            KeyHeightResetRequested.Invoke(this, EventArgs.Empty);
        }
        else
        {
            KeyHeight = 16.0;
        }
    }
}
