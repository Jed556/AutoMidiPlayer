using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

public partial class InteractivePianoRollControl : UserControl
{
    public event EventHandler<TimeSpan>? ScrubRequested;
    public event EventHandler<KeyChangeMarker>? MarkerClicked;

    public static readonly DependencyProperty MidiFileProperty =
        DependencyProperty.Register(
            nameof(MidiFile),
            typeof(MidiFile),
            typeof(InteractivePianoRollControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMidiFileChanged));

    private static void OnMidiFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractivePianoRollControl ctrl)
        {
            var file = (MidiFile?)e.NewValue;
            file?.EnsureMidiLoaded();
            if (ctrl.PianoRoll != null) ctrl.PianoRoll.MidiFile = file;
            if (ctrl.Timeline != null) ctrl.Timeline.MidiFile = file;
            ctrl.RefreshNotesAndTimeline();
            ctrl.UpdateTimelineViewport();
        }
    }

    public MidiFile? MidiFile
    {
        get => (MidiFile?)GetValue(MidiFileProperty);
        set => SetValue(MidiFileProperty, value);
    }

    public static readonly DependencyProperty CurrentTimeProperty =
        DependencyProperty.Register(
            nameof(CurrentTime),
            typeof(TimeSpan),
            typeof(InteractivePianoRollControl),
            new PropertyMetadata(TimeSpan.Zero));

    public TimeSpan CurrentTime
    {
        get => (TimeSpan)GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(
            nameof(Duration),
            typeof(TimeSpan),
            typeof(InteractivePianoRollControl),
            new PropertyMetadata(TimeSpan.FromSeconds(1)));

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty KeyChangeMarkersProperty =
        DependencyProperty.Register(
            nameof(KeyChangeMarkers),
            typeof(IEnumerable<KeyChangeMarker>),
            typeof(InteractivePianoRollControl),
            new PropertyMetadata(null));

    public IEnumerable<KeyChangeMarker>? KeyChangeMarkers
    {
        get => (IEnumerable<KeyChangeMarker>?)GetValue(KeyChangeMarkersProperty);
        set => SetValue(KeyChangeMarkersProperty, value);
    }

    public static readonly DependencyProperty SelectedMarkerProperty =
        DependencyProperty.Register(
            nameof(SelectedMarker),
            typeof(KeyChangeMarker),
            typeof(InteractivePianoRollControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public KeyChangeMarker? SelectedMarker
    {
        get => (KeyChangeMarker?)GetValue(SelectedMarkerProperty);
        set => SetValue(SelectedMarkerProperty, value);
    }

    public static readonly DependencyProperty IsSyncEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSyncEnabled),
            typeof(bool),
            typeof(InteractivePianoRollControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsSyncEnabledChanged));

    private static void OnIsSyncEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractivePianoRollControl ctrl)
        {
            var isSync = (bool)e.NewValue;
            if (isSync)
            {
                ctrl.ViewTime = null;
            }
            else
            {
                ctrl.ViewTime = ctrl.CurrentTime;
            }
            ctrl.UpdateTimelineViewport();
        }
    }

    public bool IsSyncEnabled
    {
        get => (bool)GetValue(IsSyncEnabledProperty);
        set => SetValue(IsSyncEnabledProperty, value);
    }

    public static readonly DependencyProperty ViewTimeProperty =
        DependencyProperty.Register(
            nameof(ViewTime),
            typeof(TimeSpan?),
            typeof(InteractivePianoRollControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewTimeChanged));

    private static void OnViewTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractivePianoRollControl ctrl)
        {
            ctrl.UpdateTimelineViewport();
        }
    }

    public TimeSpan? ViewTime
    {
        get => (TimeSpan?)GetValue(ViewTimeProperty);
        set => SetValue(ViewTimeProperty, value);
    }

    private double _targetZoom = 1.0;
    private double _animStartZoom = 1.0;
    private long _animStartTimestamp;
    private double _animDurationMs = 220.0;
    private bool _isZoomAnimating;

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel),
            typeof(double),
            typeof(InteractivePianoRollControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomLevelChanged));

    private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InteractivePianoRollControl ctrl)
        {
            if (!ctrl._isZoomAnimating)
            {
                ctrl._targetZoom = (double)e.NewValue;
            }
            ctrl.UpdateTimelineViewport();
        }
    }

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public static readonly DependencyProperty KeyHeightProperty =
        DependencyProperty.Register(
            nameof(KeyHeight),
            typeof(double),
            typeof(InteractivePianoRollControl),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double KeyHeight
    {
        get => (double)GetValue(KeyHeightProperty);
        set => SetValue(KeyHeightProperty, Math.Clamp(value, 6.0, 50.0));
    }

    public InteractivePianoRollControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var file = MidiFile;
            file?.EnsureMidiLoaded();
            if (PianoRoll != null && PianoRoll.MidiFile != file) PianoRoll.MidiFile = file;
            if (Timeline != null && Timeline.MidiFile != file) Timeline.MidiFile = file;
            UpdateTimelineViewport();
            RefreshNotesAndTimeline();
        };
        SizeChanged += (_, _) => UpdateTimelineViewport();
        PianoRoll.SizeChanged += (_, _) => UpdateTimelineViewport();
        PreviewMouseWheel += OnPreviewMouseWheel;
        Unloaded += (_, _) => StopZoomAnimation();
    }

    public void UpdateTimelineViewport()
    {
        var canvasW = PianoRoll?.Canvas?.ActualWidth ?? 0;
        var rollWidth = Math.Max(50.0, canvasW > 64.0 ? canvasW - 64.0 : (PianoRoll != null && PianoRoll.ActualWidth > 0 ? PianoRoll.ActualWidth - 64.0 : 800.0));
        var zoom = Math.Clamp(ZoomLevel, 0.25, 5.0);
        var pixelsPerMs = 0.15 * zoom;

        var horizonMs = (long)(rollWidth / pixelsPerMs);

        Timeline.VisiblePastMs = 0L;
        Timeline.VisibleHorizonMs = horizonMs;
        Timeline.ViewTime = (!IsSyncEnabled) ? (ViewTime ?? CurrentTime) : null;
    }

    public void RefreshNotesAndTimeline()
    {
        PianoRoll.RefreshNotes();
        Timeline.RefreshMinimap();
    }

    private void OnTimelineScrubRequested(object? sender, TimeSpan time)
    {
        ScrubRequested?.Invoke(this, time);
    }

    private void OnTimelineMarkerClicked(object? sender, KeyChangeMarker marker)
    {
        SelectedMarker = marker;
        MarkerClicked?.Invoke(this, marker);
    }

    private void OnPianoRollScrubRequested(object? sender, TimeSpan time)
    {
        ScrubRequested?.Invoke(this, time);
    }

    private void OnPianoRollMarkerClicked(object? sender, KeyChangeMarker marker)
    {
        SelectedMarker = marker;
        MarkerClicked?.Invoke(this, marker);
    }

    private void OnPianoRollHoverTimeChanged(object? sender, TimeSpan? time)
    {
        if (Timeline != null)
        {
            Timeline.HoverTime = time;
        }
    }

    private void OnTimelineHoverTimeChanged(object? sender, TimeSpan? time)
    {
        if (PianoRoll != null)
        {
            PianoRoll.HoverTime = time;
        }
    }

    private void OnTimelineSyncChanged(object? sender, bool isSync)
    {
        IsSyncEnabled = isSync;
    }

    private void OnPianoRollViewTimeChanged(object? sender, TimeSpan? time)
    {
        ViewTime = time;
        UpdateTimelineViewport();
    }

    private void OnTimelineZoomResetRequested(object? sender, EventArgs e)
    {
        ResetZoom();
    }

    #region Smooth Zoom Easing

    public static double GetNextZoomLevel(double currentZoom, bool zoomIn)
    {
        const double step = 0.25;
        const double minZoom = 0.25;
        const double maxZoom = 5.0;

        if (zoomIn)
        {
            var next = (Math.Floor((currentZoom + 0.005) / step) + 1.0) * step;
            return Math.Clamp(Math.Round(next, 2), minZoom, maxZoom);
        }
        else
        {
            var prev = (Math.Ceiling((currentZoom - 0.005) / step) - 1.0) * step;
            return Math.Clamp(Math.Round(prev, 2), minZoom, maxZoom);
        }
    }

    public void StepZoom(bool zoomIn)
    {
        var baseZoom = _isZoomAnimating ? _targetZoom : ZoomLevel;
        var nextZoom = GetNextZoomLevel(baseZoom, zoomIn);
        EaseZoomTo(nextZoom);
    }

    public void ResetZoom()
    {
        EaseZoomTo(1.0);
    }

    public void EaseZoomTo(double targetZoom, double durationMs = 220.0)
    {
        targetZoom = Math.Clamp(targetZoom, 0.25, 5.0);
        if (Math.Abs(ZoomLevel - targetZoom) < 0.0001 && Math.Abs(_targetZoom - targetZoom) < 0.0001)
            return;

        _targetZoom = targetZoom;
        _animStartZoom = ZoomLevel;
        _animDurationMs = Math.Max(50.0, durationMs);
        _animStartTimestamp = Stopwatch.GetTimestamp();

        if (!_isZoomAnimating)
        {
            _isZoomAnimating = true;
            CompositionTarget.Rendering += OnZoomAnimationTick;
        }
    }

    public void EaseZoomDelta(double delta, double durationMs = 220.0)
    {
        var baseZoom = _isZoomAnimating ? _targetZoom : ZoomLevel;
        EaseZoomTo(baseZoom + delta, durationMs);
    }

    private void OnZoomAnimationTick(object? sender, EventArgs e)
    {
        if (!_isZoomAnimating)
            return;

        var elapsedSeconds = (double)(Stopwatch.GetTimestamp() - _animStartTimestamp) / Stopwatch.Frequency;
        var elapsedMs = elapsedSeconds * 1000.0;
        var t = Math.Clamp(elapsedMs / _animDurationMs, 0.0, 1.0);

        // Cubic Ease-Out curve: 1 - (1 - t)^3
        var ease = 1.0 - Math.Pow(1.0 - t, 3.0);
        var currentZoom = _animStartZoom + (_targetZoom - _animStartZoom) * ease;

        ZoomLevel = currentZoom;

        if (t >= 1.0 || Math.Abs(currentZoom - _targetZoom) < 0.0001)
        {
            ZoomLevel = _targetZoom;
            StopZoomAnimation();
        }
    }

    private void StopZoomAnimation()
    {
        if (!_isZoomAnimating) return;
        _isZoomAnimating = false;
        CompositionTarget.Rendering -= OnZoomAnimationTick;
    }

    public void StepKeyHeight(bool increase)
    {
        var delta = increase ? 2.0 : -2.0;
        KeyHeight = Math.Clamp(KeyHeight + delta, 6.0, 50.0);
    }

    public void ResetKeyHeight()
    {
        KeyHeight = 16.0;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            StepZoom(e.Delta > 0);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            StepKeyHeight(e.Delta > 0);
            e.Handled = true;
        }
        else if (PianoRoll != null && PianoRoll.IsMouseOver && PianoRoll.IsOverflowing)
        {
            // Allow PianoRoll to handle vertical scrolling when overflowing
        }
        else if (!IsSyncEnabled)
        {
            var pixelsPerMs = 0.15 * ZoomLevel;
            var shiftMs = (long)((e.Delta / 120.0) * -150.0 / pixelsPerMs);
            var current = ViewTime ?? CurrentTime;
            var totalMs = (long)Duration.TotalMilliseconds;
            var newMs = (long)current.TotalMilliseconds + shiftMs;
            if (totalMs > 0) newMs = Math.Clamp(newMs, 0, totalMs);
            else newMs = Math.Max(0, newMs);
            ViewTime = TimeSpan.FromMilliseconds(newMs);
            e.Handled = true;
        }
    }

    private void OnPianoRollZoomAdjustRequested(object? sender, double delta)
    {
        StepZoom(delta > 0);
    }

    private void OnTimelineZoomAdjustRequested(object? sender, double delta)
    {
        StepZoom(delta > 0);
    }

    #endregion
}
