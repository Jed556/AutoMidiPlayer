using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

public partial class HorizontalPianoRoll : UserControl
{
    public event EventHandler<TimeSpan>? ScrubRequested;
    public event EventHandler<KeyChangeMarker>? MarkerClicked;
    public event EventHandler<KeyChangeMarker>? MarkerDeleteRequested;
    public event EventHandler<KeyChangeMarker>? MarkerEditRequested;
    public event EventHandler<double>? ZoomAdjustRequested;
    public event EventHandler<TimeSpan?>? HoverTimeChanged;
    public event EventHandler<TimeSpan?>? ViewTimeChanged;

    private readonly System.Windows.Threading.DispatcherTimer _markerHideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private bool _isMarkerHovered;
    private bool _isPopupHovered;
    private KeyChangeMarker? _activeHoverMarker;

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(
            nameof(DisplayMode),
            typeof(PianoRollDisplayMode),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(PianoRollDisplayMode.Auto, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.DisplayMode = (PianoRollDisplayMode)e.NewValue;
                }
            }));

    public PianoRollDisplayMode DisplayMode
    {
        get => (PianoRollDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public static readonly DependencyProperty InstrumentIdProperty =
        DependencyProperty.Register(
            nameof(InstrumentId),
            typeof(string),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.InstrumentId = (string?)e.NewValue;
                }
            }));

    public string? InstrumentId
    {
        get => (string?)GetValue(InstrumentIdProperty);
        set => SetValue(InstrumentIdProperty, value);
    }

    public static readonly DependencyProperty PitchRevisionProperty =
        DependencyProperty.Register(
            nameof(PitchRevision),
            typeof(int),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(0, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.PitchRevision = (int)e.NewValue;
                }
            }));

    public int PitchRevision
    {
        get => (int)GetValue(PitchRevisionProperty);
        set => SetValue(PitchRevisionProperty, value);
    }

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(false, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll)
                {
                    if (roll.Canvas != null)
                    {
                        roll.Canvas.IsPlaying = (bool)e.NewValue;
                    }
                    if ((bool)e.NewValue)
                    {
                        roll.HideMarkerPopupImmediate();
                    }
                }
            }));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public static readonly DependencyProperty IsSyncEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSyncEnabled),
            typeof(bool),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(true, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.IsSyncEnabled = (bool)e.NewValue;
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
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.ViewTime = (TimeSpan?)e.NewValue;
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
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.HoverTime = (TimeSpan?)e.NewValue;
                }
            }));

    public TimeSpan? HoverTime
    {
        get => (TimeSpan?)GetValue(HoverTimeProperty);
        set => SetValue(HoverTimeProperty, value);
    }
    public static readonly DependencyProperty MidiTracksProperty =
        DependencyProperty.Register(
            nameof(MidiTracks),
            typeof(IEnumerable<MidiTrack>),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.MidiTracks = (IEnumerable<MidiTrack>?)e.NewValue;
                }
            }));

    public IEnumerable<MidiTrack>? MidiTracks
    {
        get => (IEnumerable<MidiTrack>?)GetValue(MidiTracksProperty);
        set => SetValue(MidiTracksProperty, value);
    }

    public static readonly DependencyProperty MidiFileProperty =
        DependencyProperty.Register(
            nameof(MidiFile),
            typeof(MidiFile),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.MidiFile = (MidiFile?)e.NewValue;
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
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(TimeSpan.Zero, (d, e) =>
            {
                var roll = (HorizontalPianoRoll)d;
                if (roll.Canvas != null)
                {
                    roll.Canvas.CurrentTime = (TimeSpan)e.NewValue;
                    roll.UpdateMarkerPopupPositionDuringPlayback();
                }
            }));

    public TimeSpan CurrentTime
    {
        get => (TimeSpan)GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }

    public static readonly DependencyProperty KeyChangeMarkersProperty =
        DependencyProperty.Register(
            nameof(KeyChangeMarkers),
            typeof(IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>),
            typeof(HorizontalPianoRoll),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.KeyChangeMarkers = (IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>?)e.NewValue;
                }
            }));

    public IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>? KeyChangeMarkers
    {
        get => (IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>?)GetValue(KeyChangeMarkersProperty);
        set => SetValue(KeyChangeMarkersProperty, value);
    }

    public static readonly DependencyProperty SelectedMarkerProperty =
        DependencyProperty.Register(
            nameof(SelectedMarker),
            typeof(KeyChangeMarker),
            typeof(HorizontalPianoRoll),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedMarkerChanged));

    private static void OnSelectedMarkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var roll = (HorizontalPianoRoll)d;
        var selected = (KeyChangeMarker?)e.NewValue;
        if (roll.MarkerPopup?.IsOpen == true && roll._activeHoverMarker != null)
        {
            roll.MarkerPopupContent.IsMarkerSelected = (roll._activeHoverMarker == selected);
        }
    }

    public KeyChangeMarker? SelectedMarker
    {
        get => (KeyChangeMarker?)GetValue(SelectedMarkerProperty);
        set => SetValue(SelectedMarkerProperty, value);
    }

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel),
            typeof(double),
            typeof(HorizontalPianoRoll),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((HorizontalPianoRoll)d).Canvas.ZoomLevel = (double)e.NewValue));

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public static readonly DependencyProperty KeyHeightProperty =
        DependencyProperty.Register(
            nameof(KeyHeight),
            typeof(double),
            typeof(HorizontalPianoRoll),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) =>
            {
                if (d is HorizontalPianoRoll roll && roll.Canvas != null)
                {
                    roll.Canvas.KeyHeight = (double)e.NewValue;
                }
            }));

    public double KeyHeight
    {
        get => (double)GetValue(KeyHeightProperty);
        set => SetValue(KeyHeightProperty, value);
    }

    public event EventHandler<double>? KeyHeightAdjustRequested;

    public HorizontalPianoRoll()
    {
        InitializeComponent();
        _markerHideTimer.Tick += OnMarkerHideTimerTick;
        Canvas.ScrubRequested += (s, time) => ScrubRequested?.Invoke(this, time);
        Canvas.MarkerClicked += (s, marker) => MarkerClicked?.Invoke(this, marker);
        Canvas.MarkerHoverChanged += OnCanvasMarkerHoverChanged;
        Canvas.HoverTimeChanged += (s, time) => HoverTimeChanged?.Invoke(this, time);
        Canvas.HoverPositionChanged += (s, e) =>
        {
            HoverOverlay?.SetHover(e.HoverX, e.HoverMs, Canvas.KeyboardWidth, Canvas.TotalDurationMs);
        };
        Canvas.ViewTimeChanged += (s, time) =>
        {
            ViewTime = time;
            ViewTimeChanged?.Invoke(this, time);
        };
        Canvas.ScrollBoundsChanged += (s, e) => UpdateScrollBounds();
        Canvas.VerticalOffsetChanged += (s, offset) =>
        {
            if (VerticalScrollBar != null && Math.Abs(VerticalScrollBar.Value - offset) > 0.01)
            {
                VerticalScrollBar.Value = offset;
            }
        };

        PreviewMouseWheel += OnPreviewMouseWheel;
        SizeChanged += (_, _) => UpdateScrollBounds();
        Loaded += (_, _) =>
        {
            if (Canvas != null)
            {
                if (Canvas.MidiFile != MidiFile)
                    Canvas.MidiFile = MidiFile;
                if (Canvas.KeyChangeMarkers != KeyChangeMarkers)
                    Canvas.KeyChangeMarkers = KeyChangeMarkers;
                Canvas.IsSyncEnabled = IsSyncEnabled;
                Canvas.ViewTime = ViewTime;
                Canvas.KeyHeight = KeyHeight;
                Canvas.InstrumentId = InstrumentId;
                Canvas.PitchRevision = PitchRevision;
                Canvas.CurrentTime = CurrentTime;
                Canvas.InvalidateVisual();
            }
            if (Canvas?.HasNotes != true)
            {
                Canvas?.RebuildNoteIndex();
            }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, UpdateScrollBounds);
        };
    }

    public bool IsOverflowing => Canvas?.IsOverflowing ?? false;
    public PianoRollCanvas RollCanvas => Canvas;
    public int PitchCount => Canvas?.PitchCount ?? 49;

    public void RefreshNotes()
    {
        Canvas.RebuildNoteIndex();
        UpdateScrollBounds();
    }

    public void FitKeyHeightToViewport()
    {
        if (Canvas == null) return;
        var pitchCount = Canvas.PitchCount;
        var viewportHeight = Canvas.ActualHeight;

        if (pitchCount <= 0 || viewportHeight <= 0)
            return;

        var target = Math.Clamp(viewportHeight / pitchCount, 6.0, 50.0);
        KeyHeight = target;
        Canvas.KeyHeight = target;
        UpdateScrollBounds();
    }

    private void UpdateScrollBounds()
    {
        if (Canvas == null || VerticalScrollBar == null) return;
        var totalHeight = Canvas.TotalContentHeight;
        var viewportHeight = Canvas.ActualHeight;

        if (viewportHeight <= 0)
        {
            // Layout arrangement not yet complete; do not destroy scroll offset
            return;
        }

        if (totalHeight > viewportHeight + 1.0)
        {
            VerticalScrollBar.Visibility = Visibility.Visible;
            VerticalScrollBar.Maximum = totalHeight - viewportHeight;
            VerticalScrollBar.ViewportSize = viewportHeight;
            VerticalScrollBar.SmallChange = Canvas.KeyHeight;
            VerticalScrollBar.LargeChange = Canvas.KeyHeight * 4;
            VerticalScrollBar.Value = Math.Clamp(Canvas.VerticalOffset, 0, VerticalScrollBar.Maximum);
        }
        else
        {
            VerticalScrollBar.Visibility = Visibility.Collapsed;
            VerticalScrollBar.Value = 0;
            Canvas.VerticalOffset = 0;
        }
    }

    private void OnVerticalScrollBarScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        if (Canvas != null)
        {
            Canvas.VerticalOffset = e.NewValue;
        }
    }

    public void AdjustKeyHeight(double delta)
    {
        var target = Math.Clamp(KeyHeight + delta, 6.0, 50.0);
        KeyHeight = target;
        KeyHeightAdjustRequested?.Invoke(this, delta);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Horizontal time zoom (Ctrl+Wheel)
            var delta = e.Delta > 0 ? 1.0 : -1.0;
            if (ZoomAdjustRequested != null)
            {
                ZoomAdjustRequested.Invoke(this, delta);
            }
            else
            {
                var next = delta > 0 ? ZoomLevel + 0.25 : ZoomLevel - 0.25;
                ZoomLevel = Math.Clamp(next, 0.25, 5.0);
            }
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Vertical key height zoom (Alt+Wheel or Shift+Wheel)
            var delta = e.Delta > 0 ? 2.0 : -2.0;
            AdjustKeyHeight(delta);
            e.Handled = true;
        }
        else
        {
            // Regular wheel: scroll piano roll vertically through notes (FL Studio style)
            if (Canvas != null && Canvas.TotalContentHeight > Canvas.ActualHeight && Canvas.ActualHeight > 0)
            {
                var scrollDelta = e.Delta > 0 ? -Canvas.KeyHeight * 2 : Canvas.KeyHeight * 2;
                Canvas.VerticalOffset = Math.Clamp(Canvas.VerticalOffset + scrollDelta, 0, Canvas.MaxVerticalOffset);
                if (VerticalScrollBar != null)
                {
                    VerticalScrollBar.Value = Canvas.VerticalOffset;
                }
                e.Handled = true;
            }
        }
    }

    private void OnCanvasMarkerHoverChanged(object? sender, (KeyChangeMarker Marker, double MarkerX, Point MousePos)? e)
    {
        if (e.HasValue)
        {
            ShowMarkerPopup(e.Value.Marker, e.Value.MarkerX, e.Value.MousePos.Y);
        }
        else
        {
            if (_isMarkerHovered)
            {
                _isMarkerHovered = false;
                if (!_isPopupHovered)
                {
                    StartMarkerHideTimer();
                }
            }
        }
    }

    private void ShowMarkerPopup(KeyChangeMarker marker, double markerX, double mouseY)
    {
        if (IsPlaying || (Canvas != null && Canvas.IsPlaybackActive))
            return;

        _isMarkerHovered = true;
        _markerHideTimer.Stop();

        if (MarkerPopup == null || MarkerPopupContent == null || Canvas == null)
            return;

        if (_activeHoverMarker != marker)
        {
            _activeHoverMarker = marker;
            MarkerPopupContent.SetMarker(marker, isAbove: false, isSelected: marker == SelectedMarker);
        }
        else
        {
            MarkerPopupContent.IsMarkerSelected = (marker == SelectedMarker);
        }

        const double popupWidth = 84.0;
        var clampedX = Math.Clamp(markerX - (popupWidth / 2.0), Canvas.KeyboardWidth, Math.Max(Canvas.KeyboardWidth, Canvas.ActualWidth - popupWidth));
        var anchorY = mouseY <= 50 ? 22.0 : Math.Min(mouseY + 4, Math.Max(22.0, Canvas.ActualHeight - 70));

        MarkerPopup.HorizontalOffset = clampedX;
        MarkerPopup.VerticalOffset = anchorY;

        if (!MarkerPopup.IsOpen)
        {
            MarkerPopup.IsOpen = true;
            MarkerPopupContent.PlayEntranceAnimation();
        }
    }

    public void UpdateMarkerPopupPositionDuringPlayback()
    {
        if (MarkerPopup?.IsOpen == true && (IsPlaying || (Canvas != null && Canvas.IsPlaybackActive)))
        {
            HideMarkerPopupImmediate();
            return;
        }

        if (MarkerPopup?.IsOpen != true || _activeHoverMarker == null || _isPopupHovered || Canvas == null)
            return;

        var pixelsPerMs = 0.15 * ZoomLevel;
        var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
            ? (long)ViewTime.Value.TotalMilliseconds
            : (long)CurrentTime.TotalMilliseconds;

        var markerX = Canvas.KeyboardWidth + (_activeHoverMarker.TimeMs - effectiveViewTimeMs) * pixelsPerMs;
        if (markerX < Canvas.KeyboardWidth || markerX > Canvas.ActualWidth)
        {
            HideMarkerPopupImmediate();
        }
        else
        {
            var popupWidth = MarkerPopupContent.ActualWidth > 0 ? MarkerPopupContent.ActualWidth : 80;
            var clampedX = Math.Clamp(markerX - (popupWidth / 2), Canvas.KeyboardWidth, Math.Max(Canvas.KeyboardWidth, Canvas.ActualWidth - popupWidth));
            MarkerPopup.HorizontalOffset = clampedX;
        }
    }

    private void StartMarkerHideTimer()
    {
        _markerHideTimer.Stop();
        _markerHideTimer.Start();
    }

    private void OnMarkerHideTimerTick(object? sender, EventArgs e)
    {
        _markerHideTimer.Stop();
        if (!_isMarkerHovered && !_isPopupHovered && MarkerPopup.IsOpen)
        {
            MarkerPopupContent.PlayExitAnimation(() =>
            {
                MarkerPopup.IsOpen = false;
                _activeHoverMarker = null;
            });
        }
    }

    private void HideMarkerPopupImmediate()
    {
        _markerHideTimer.Stop();
        _isMarkerHovered = false;
        _isPopupHovered = false;
        _activeHoverMarker = null;
        if (MarkerPopup != null)
        {
            MarkerPopup.IsOpen = false;
        }
    }

    private void OnMarkerPopupMouseEnter(object sender, MouseEventArgs e)
    {
        _isPopupHovered = true;
        _markerHideTimer.Stop();
    }

    private void OnMarkerPopupMouseLeave(object sender, MouseEventArgs e)
    {
        _isPopupHovered = false;
        if (!_isMarkerHovered)
        {
            StartMarkerHideTimer();
        }
    }

    private void OnMarkerPopupDeleteRequested(object? sender, KeyChangeMarker marker)
    {
        HideMarkerPopupImmediate();
        MarkerDeleteRequested?.Invoke(this, marker);
    }

    private void OnMarkerPopupEditRequested(object? sender, KeyChangeMarker marker)
    {
        MarkerEditRequested?.Invoke(this, marker);
    }
}
