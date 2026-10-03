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
    public event EventHandler<double>? ZoomAdjustRequested;
    public event EventHandler<TimeSpan?>? HoverTimeChanged;
    public event EventHandler<TimeSpan?>? ViewTimeChanged;

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
            new PropertyMetadata(TimeSpan.Zero, (d, e) => ((HorizontalPianoRoll)d).Canvas.CurrentTime = (TimeSpan)e.NewValue));

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
        Canvas.ScrubRequested += (s, time) => ScrubRequested?.Invoke(this, time);
        Canvas.MarkerClicked += (s, marker) => MarkerClicked?.Invoke(this, marker);
        Canvas.HoverTimeChanged += (s, time) => HoverTimeChanged?.Invoke(this, time);
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
            }
            Canvas?.RebuildNoteIndex();
            UpdateScrollBounds();
        };
    }

    public bool IsOverflowing => Canvas?.IsOverflowing ?? false;
    public PianoRollCanvas RollCanvas => Canvas;

    public void RefreshNotes()
    {
        Canvas.RebuildNoteIndex();
        UpdateScrollBounds();
    }

    private void UpdateScrollBounds()
    {
        if (Canvas == null || VerticalScrollBar == null) return;
        var totalHeight = Canvas.TotalContentHeight;
        var viewportHeight = Canvas.ActualHeight;

        if (totalHeight > viewportHeight && viewportHeight > 0)
        {
            VerticalScrollBar.Visibility = Visibility.Visible;
            VerticalScrollBar.Maximum = totalHeight - viewportHeight;
            VerticalScrollBar.ViewportSize = viewportHeight;
            VerticalScrollBar.SmallChange = Canvas.KeyHeight;
            VerticalScrollBar.LargeChange = Canvas.KeyHeight * 4;
            VerticalScrollBar.Value = Canvas.VerticalOffset;
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
}
