using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Collections.Specialized;
using System.ComponentModel;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Helpers;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

public class TimelineCanvas : FrameworkElement
{
    public readonly record struct MinimapNote(int RawPitch, int Pitch, long StartMs, long LengthMs, int TrackIndex);

    private static readonly SolidColorBrush TrackBackgroundBrush;
    private static readonly SolidColorBrush[] TrackBrushes;
    private static readonly SolidColorBrush[] DisabledTrackBrushes;
    private static readonly SolidColorBrush PlayheadBrush;
    private static readonly Pen PlayheadPen;
    private static readonly SolidColorBrush MarkerPinBrush;
    private static readonly Pen MarkerPen;
    private static readonly SolidColorBrush RulerTextBrush;
    private static readonly Typeface RulerTypeface;
    private static readonly Typeface MarkerTypeface;
    private static readonly SolidColorBrush ViewportFillBrush;
    private static readonly Pen ViewportBorderPen;
    private static readonly SolidColorBrush ViewportGripBrush;
    private static readonly StreamGeometry PlayheadHandleGeometry;
    private static readonly Pen HoverLinePen;

    static TimelineCanvas()
    {
        TrackBackgroundBrush = CreateFrozenBrush(Color.FromArgb(40, 0, 0, 0));
        TrackBrushes = TrackColorPalette.AllBrushes;
        DisabledTrackBrushes = new SolidColorBrush[TrackBrushes.Length];
        for (var i = 0; i < TrackBrushes.Length; i++)
        {
            var c = TrackBrushes[i].Color;
            DisabledTrackBrushes[i] = CreateFrozenBrush(Color.FromArgb((byte)(c.A * 0.25), c.R, c.G, c.B));
        }
        PlayheadBrush = CreateFrozenBrush(Color.FromRgb(255, 255, 255));
        PlayheadPen = new Pen(CreateFrozenBrush(Color.FromRgb(255, 255, 255)), 2);
        PlayheadPen.Freeze();

        HoverLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(120, 255, 255, 255)), 1.2);
        HoverLinePen.Freeze();

        MarkerPinBrush = CreateFrozenBrush(Color.FromRgb(255, 183, 3));      // Amber
        MarkerPen = new Pen(MarkerPinBrush, 1.5);
        MarkerPen.Freeze();

        RulerTextBrush = CreateFrozenBrush(Color.FromArgb(140, 255, 255, 255));
        RulerTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        MarkerTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        ViewportFillBrush = CreateFrozenBrush(Color.FromArgb(32, 255, 255, 255));
        ViewportBorderPen = new Pen(CreateFrozenBrush(Color.FromArgb(200, 255, 255, 255)), 1.5);
        ViewportBorderPen.Freeze();
        ViewportGripBrush = CreateFrozenBrush(Color.FromArgb(240, 255, 255, 255));

        PlayheadHandleGeometry = new StreamGeometry();
        using (var ctx = PlayheadHandleGeometry.Open())
        {
            ctx.BeginFigure(new Point(-5, 0), true, true);
            ctx.LineTo(new Point(5, 0), true, false);
            ctx.LineTo(new Point(5, 6), true, false);
            ctx.LineTo(new Point(0, 11), true, false);
            ctx.LineTo(new Point(-5, 6), true, false);
        }
        PlayheadHandleGeometry.Freeze();
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private MinimapNote[] _notes = Array.Empty<MinimapNote>();
    private int _minPitch = 36;
    private int _maxPitch = 84;
    private long _totalDurationMs = 1;
    private int _trackCount = 1;
    private DrawingGroup? _staticBackgroundCache;
    private double _lastCachedWidth = -1;
    private double _lastCachedHeight = -1;
    private readonly TranslateTransform _playheadTransform = new();

    private readonly HashSet<int> _disabledTrackIndices = new();
    private readonly bool[] _disabledTrackLookup = new bool[256];
    private readonly List<INotifyPropertyChanged> _subscribedTrackItems = new();
    private INotifyCollectionChanged? _subscribedTrackCollection;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsTrackDisabled(int trackIndex)
    {
        return (uint)trackIndex < (uint)_disabledTrackLookup.Length
            ? _disabledTrackLookup[trackIndex]
            : _disabledTrackIndices.Contains(trackIndex);
    }

    public TimelineCanvas()
    {
        Loaded += (_, _) =>
        {
            AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged += OnThemeResourcesChanged;
            UpdateTrackSubscriptions();
        };
        Unloaded += (_, _) =>
        {
            AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged -= OnThemeResourcesChanged;
            UnsubscribeTrackEvents();
        };
    }

    private void OnThemeResourcesChanged()
    {
        Dispatcher.BeginInvoke(InvalidateVisualCache);
    }

    public static readonly DependencyProperty MidiTracksProperty =
        DependencyProperty.Register(
            nameof(MidiTracks),
            typeof(IEnumerable<MidiTrack>),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMidiTracksChanged));

    private static void OnMidiTracksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            canvas.UpdateTrackSubscriptions();
        }
    }

    public IEnumerable<MidiTrack>? MidiTracks
    {
        get => (IEnumerable<MidiTrack>?)GetValue(MidiTracksProperty);
        set => SetValue(MidiTracksProperty, value);
    }

    private void UpdateTrackSubscriptions()
    {
        UnsubscribeTrackEvents();

        if (MidiTracks is INotifyCollectionChanged notifyCollection)
        {
            _subscribedTrackCollection = notifyCollection;
            notifyCollection.CollectionChanged += OnTrackCollectionChanged;
        }

        if (MidiTracks != null)
        {
            foreach (var track in MidiTracks)
            {
                track.PropertyChanged += OnTrackItemPropertyChanged;
                _subscribedTrackItems.Add(track);
            }
        }

        UpdateDisabledTrackIndices();
    }

    private void UnsubscribeTrackEvents()
    {
        if (_subscribedTrackCollection != null)
        {
            _subscribedTrackCollection.CollectionChanged -= OnTrackCollectionChanged;
            _subscribedTrackCollection = null;
        }

        foreach (var item in _subscribedTrackItems)
        {
            item.PropertyChanged -= OnTrackItemPropertyChanged;
        }
        _subscribedTrackItems.Clear();
    }

    private void OnTrackCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateTrackSubscriptions();
    }

    private void OnTrackItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MidiTrack.IsChecked))
        {
            UpdateDisabledTrackIndices();
        }
    }

    private void UpdateDisabledTrackIndices()
    {
        _disabledTrackIndices.Clear();
        Array.Clear(_disabledTrackLookup, 0, _disabledTrackLookup.Length);
        if (MidiTracks != null)
        {
            foreach (var t in MidiTracks)
            {
                if (!t.IsChecked)
                {
                    _disabledTrackIndices.Add(t.Index);
                    if ((uint)t.Index < (uint)_disabledTrackLookup.Length)
                    {
                        _disabledTrackLookup[t.Index] = true;
                    }
                }
            }
        }

        InvalidateVisualCache();
    }

    public static readonly DependencyProperty VisiblePastMsProperty =
        DependencyProperty.Register(
            nameof(VisiblePastMs),
            typeof(long),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public long VisiblePastMs
    {
        get => (long)GetValue(VisiblePastMsProperty);
        set => SetValue(VisiblePastMsProperty, value);
    }

    public static readonly DependencyProperty VisibleHorizonMsProperty =
        DependencyProperty.Register(
            nameof(VisibleHorizonMs),
            typeof(long),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(6000L, FrameworkPropertyMetadataOptions.AffectsRender));

    public long VisibleHorizonMs
    {
        get => (long)GetValue(VisibleHorizonMsProperty);
        set => SetValue(VisibleHorizonMsProperty, value);
    }

    public void InvalidateVisualCache()
    {
        _staticBackgroundCache = null;
        InvalidateVisual();
    }

    public static readonly DependencyProperty ViewTimeProperty =
        DependencyProperty.Register(
            nameof(ViewTime),
            typeof(TimeSpan?),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimeSpan? ViewTime
    {
        get => (TimeSpan?)GetValue(ViewTimeProperty);
        set => SetValue(ViewTimeProperty, value);
    }

    public static readonly DependencyProperty HoverTimeProperty =
        DependencyProperty.Register(
            nameof(HoverTime),
            typeof(TimeSpan?),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimeSpan? HoverTime
    {
        get => (TimeSpan?)GetValue(HoverTimeProperty);
        set => SetValue(HoverTimeProperty, value);
    }

    public static readonly DependencyProperty MidiFileProperty =
        DependencyProperty.Register(
            nameof(MidiFile),
            typeof(MidiFile),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMidiFileChanged));

    public MidiFile? MidiFile
    {
        get => (MidiFile?)GetValue(MidiFileProperty);
        set => SetValue(MidiFileProperty, value);
    }

    public static readonly DependencyProperty CurrentTimeProperty =
        DependencyProperty.Register(
            nameof(CurrentTime),
            typeof(TimeSpan),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimeSpan CurrentTime
    {
        get => (TimeSpan)GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(
            nameof(Duration),
            typeof(TimeSpan),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(TimeSpan.FromSeconds(1), FrameworkPropertyMetadataOptions.AffectsRender, OnDurationChanged));

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(
            nameof(DisplayMode),
            typeof(PianoRollDisplayMode),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(PianoRollDisplayMode.Auto, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayModeChanged));

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            canvas.UpdateNotePitches();
        }
    }

    public PianoRollDisplayMode DisplayMode
    {
        get => (PianoRollDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public static readonly DependencyProperty InstrumentIdProperty =
        DependencyProperty.Register(
            nameof(InstrumentId),
            typeof(string),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnInstrumentIdChanged));

    private static void OnInstrumentIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            canvas.UpdateNotePitches();
        }
    }

    public string? InstrumentId
    {
        get => (string?)GetValue(InstrumentIdProperty);
        set => SetValue(InstrumentIdProperty, value);
    }

    public static readonly DependencyProperty PitchRevisionProperty =
        DependencyProperty.Register(
            nameof(PitchRevision),
            typeof(int),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnPitchRevisionChanged));

    private static void OnPitchRevisionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            canvas.UpdateNotePitches();
        }
    }

    public int PitchRevision
    {
        get => (int)GetValue(PitchRevisionProperty);
        set => SetValue(PitchRevisionProperty, value);
    }

    public static readonly DependencyProperty KeyChangeMarkersProperty =
        DependencyProperty.Register(
            nameof(KeyChangeMarkers),
            typeof(IEnumerable<KeyChangeMarker>),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMarkersChanged));

    public IEnumerable<KeyChangeMarker>? KeyChangeMarkers
    {
        get => (IEnumerable<KeyChangeMarker>?)GetValue(KeyChangeMarkersProperty);
        set => SetValue(KeyChangeMarkersProperty, value);
    }

    public static readonly DependencyProperty SelectedMarkerProperty =
        DependencyProperty.Register(
            nameof(SelectedMarker),
            typeof(KeyChangeMarker),
            typeof(TimelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedMarkerChanged));

    public KeyChangeMarker? SelectedMarker
    {
        get => (KeyChangeMarker?)GetValue(SelectedMarkerProperty);
        set => SetValue(SelectedMarkerProperty, value);
    }

    private static void OnMidiFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            canvas.RebuildMinimap();
        }
    }

    private static void OnMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldColl)
            {
                oldColl.CollectionChanged -= canvas.OnMarkersCollectionChanged;
            }
            if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newColl)
            {
                newColl.CollectionChanged += canvas.OnMarkersCollectionChanged;
            }

            if (canvas.DisplayMode == PianoRollDisplayMode.Auto)
            {
                canvas.UpdateNotePitches();
            }
            else
            {
                canvas.InvalidateVisualCache();
            }
        }
    }

    private void OnMarkersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DisplayMode == PianoRollDisplayMode.Auto)
        {
            UpdateNotePitches();
        }
        else
        {
            InvalidateVisualCache();
        }
    }

    private static void OnSelectedMarkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            canvas.InvalidateVisualCache();
        }
    }

    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineCanvas canvas)
        {
            var dur = (TimeSpan)e.NewValue;
            canvas._totalDurationMs = Math.Max(1L, (long)dur.TotalMilliseconds);
            canvas.InvalidateVisualCache();
        }
    }

    public void RebuildMinimap()
    {
        var file = MidiFile;
        if (file == null)
        {
            _notes = Array.Empty<MinimapNote>();
            _totalDurationMs = Math.Max(1L, (long)Duration.TotalMilliseconds);
            _trackCount = 1;
            InvalidateVisualCache();
            return;
        }

        try
        {
            file.EnsureMidiLoaded();

            if (file.Midi is null)
            {
                _notes = Array.Empty<MinimapNote>();
                _totalDurationMs = Math.Max(1L, (long)Duration.TotalMilliseconds);
                _trackCount = 1;
                InvalidateVisualCache();
                return;
            }

            var tempoMap = file.OriginalTempoMap ?? file.Midi.GetTempoMap();
            var trackChunks = file.Midi.GetTrackChunks().ToList();
            var list = new List<MinimapNote>();

            if (trackChunks.Count > 0)
            {
                for (var t = 0; t < trackChunks.Count; t++)
                {
                    foreach (var note in trackChunks[t].GetNotes())
                    {
                        var startMs = (long)note.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                        var lenMs = (long)note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                        list.Add(new MinimapNote(note.NoteNumber, note.NoteNumber, startMs, lenMs, t));
                    }
                }
            }
            else
            {
                foreach (var note in file.Midi.GetNotes())
                {
                    var startMs = (long)note.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    var lenMs = (long)note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    list.Add(new MinimapNote(note.NoteNumber, note.NoteNumber, startMs, lenMs, (int)note.Channel));
                }
            }

            _notes = list.OrderBy(n => n.StartMs).ToArray();
            _trackCount = _notes.Length > 0 ? _notes.Select(n => n.TrackIndex).Distinct().Count() : 1;
            UpdateNotePitchesInternal();

            if (_notes.Length > 0)
            {
                var lastNote = _notes.Max(n => n.StartMs + n.LengthMs);
                _totalDurationMs = Math.Max(_totalDurationMs, Math.Max(1L, lastNote));
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to build timeline minimap notes.");
            _notes = Array.Empty<MinimapNote>();
            _trackCount = 1;
        }

        InvalidateVisualCache();
    }

    public void UpdateNotePitches()
    {
        UpdateNotePitchesInternal();
        InvalidateVisualCache();
        InvalidateVisual();
    }

    private void UpdateNotePitchesInternal()
    {
        if (_notes.Length == 0) return;

        var song = MidiFile?.Song;
        var mode = DisplayMode;
        var settings = AutoMidiPlayer.Data.Properties.Settings.Default;
        var instrumentId = !string.IsNullOrWhiteSpace(InstrumentId)
            ? InstrumentId
            : PianoRollCanvas.ResolveCurrentInstrumentId();
        var instrumentKeyCount = Keyboard.GetNotes(instrumentId).Count;
        var threshold = settings.AutoCorrectThreshold;
        var isAutoCorrect = threshold > 0
            && instrumentKeyCount <= threshold
            && (song?.Transpose == Transpose.Smart)
            && (song?.BaseKey is not null and not 0);

        for (var i = 0; i < _notes.Length; i++)
        {
            var note = _notes[i];
            var displayPitch = PianoRollNoteCalculator.CalculateDisplayPitch(
                note.RawPitch, note.StartMs, mode, song, instrumentId, isAutoCorrect);

            _notes[i] = new MinimapNote(note.RawPitch, displayPitch, note.StartMs, note.LengthMs, note.TrackIndex);
        }

        _minPitch = _notes.Min(n => n.Pitch);
        _maxPitch = _notes.Max(n => n.Pitch);
    }

    public long TotalDurationMs => Math.Max(1L, Math.Max(_totalDurationMs, (long)Duration.TotalMilliseconds));

    public KeyChangeMarker? HitTestMarker(Point point)
    {
        var markers = KeyChangeMarkers;
        var totalMs = TotalDurationMs;
        if (markers is null || ActualWidth <= 0 || totalMs <= 0)
            return null;

        var width = ActualWidth;
        foreach (var marker in markers)
        {
            var x = (marker.TimeMs / (double)totalMs) * width;
            if (Math.Abs(point.X - x) <= 12)
                return marker;
        }

        return null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 10 || height <= 10)
            return;

        var totalMs = TotalDurationMs;

        if (_staticBackgroundCache == null || Math.Abs(_lastCachedWidth - width) > 0.5 || Math.Abs(_lastCachedHeight - height) > 0.5)
        {
            _staticBackgroundCache = BuildStaticBackground(width, height, totalMs);
            _lastCachedWidth = width;
            _lastCachedHeight = height;
        }

        // Draw cached static background (minimap, ruler, markers) in a single fast call
        dc.DrawDrawing(_staticBackgroundCache);

        var currentMs = (long)CurrentTime.TotalMilliseconds;
        var playheadX = Math.Clamp((currentMs / (double)totalMs) * width, 0, width);

        // Draw Viewport Highlight Window (the span of notes currently visible in the piano roll ahead of playhead)
        var viewCenterMs = ViewTime.HasValue ? (long)ViewTime.Value.TotalMilliseconds : currentMs;
        if (VisibleHorizonMs > 0 && totalMs > 0)
        {
            var windowStartMs = Math.Clamp(viewCenterMs - VisiblePastMs, 0, totalMs);
            var windowEndMs = Math.Clamp(viewCenterMs + VisibleHorizonMs, 0, totalMs);

            var xStart = (windowStartMs / (double)totalMs) * width;
            var xEnd = (windowEndMs / (double)totalMs) * width;
            var boxW = Math.Max(2, xEnd - xStart);
            var boxRect = new Rect(xStart, 1, boxW, height - 2);

            // Shaded translucent white highlight window with rounded borders
            dc.DrawRoundedRectangle(ViewportFillBrush, ViewportBorderPen, boxRect, 3, 3);

            // Right tactile grip line
            if (boxW > 14)
            {
                dc.DrawRoundedRectangle(ViewportGripBrush, null, new Rect(xStart + boxW - 3, 4, 2, height - 8), 1, 1);
            }
        }

        // Draw faint hover guide line if hovering
        if (HoverTime.HasValue && totalMs > 0)
        {
            var hx = Math.Clamp((HoverTime.Value.TotalMilliseconds / (double)totalMs) * width, 0, width);
            dc.DrawLine(HoverLinePen, new Point(hx, 0), new Point(hx, height));
        }

        // Draw only the dynamic playhead (ultra-lightweight!)
        // Vertical playhead line
        dc.DrawLine(PlayheadPen, new Point(playheadX, 0), new Point(playheadX, height));

        // Playhead handle at top
        _playheadTransform.X = playheadX;
        dc.PushTransform(_playheadTransform);
        dc.DrawGeometry(PlayheadBrush, null, PlayheadHandleGeometry);
        dc.Pop();
    }

    private DrawingGroup BuildStaticBackground(double width, double height, long totalMs)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            // 1. Background
            dc.DrawRoundedRectangle(TrackBackgroundBrush, null, new Rect(0, 0, width, height), 4, 4);

            // 2. Minimap Notes
            if (_notes.Length > 0)
            {
                var pitchSpan = Math.Max(12, _maxPitch - _minPitch);
                var noteAreaH = Math.Max(10, height - 16);
                var isSingleTrack = _trackCount == 1;
                var singleTrackBrush = isSingleTrack ? TrackColorPalette.GetBrush(0, 1) : null;
                var disabledSingleTrackBrush = isSingleTrack && singleTrackBrush != null
                    ? CreateFrozenBrush(Color.FromArgb((byte)(singleTrackBrush.Color.A * 0.25), singleTrackBrush.Color.R, singleTrackBrush.Color.G, singleTrackBrush.Color.B))
                    : null;
                var lastRightByTrackPitch = new double[TrackBrushes.Length * 128];
                Array.Fill(lastRightByTrackPitch, -1.0);

                void DrawTimelineNote(MinimapNote note, bool isDisabled)
                {
                    var x = (note.StartMs / (double)totalMs) * width;
                    var y = (1.0 - (double)(note.Pitch - _minPitch) / pitchSpan) * (noteAreaH - 4) + 2;
                    var w = Math.Max(2.0, (note.LengthMs / (double)totalMs) * width);
                    var trackIdx = isSingleTrack ? 0 : (Math.Abs(note.TrackIndex) % TrackBrushes.Length);
                    var pitch = Math.Clamp(note.Pitch, 0, 127);
                    var trackerKey = (trackIdx << 7) | pitch;

                    var lastRight = lastRightByTrackPitch[trackerKey];
                    if (x + w <= lastRight + 0.5)
                        return;

                    if (x < lastRight)
                    {
                        w -= (lastRight - x);
                        x = lastRight;
                        if (w <= 0.5) return;
                    }

                    lastRightByTrackPitch[trackerKey] = x + w;
                    var brush = isDisabled
                        ? (disabledSingleTrackBrush ?? DisabledTrackBrushes[trackIdx])
                        : (singleTrackBrush ?? TrackBrushes[trackIdx]);

                    dc.DrawRectangle(brush, null, new Rect(x, y, w, 2.0));
                }

                if (_disabledTrackIndices.Count > 0)
                {
                    // Pass 1: Disabled notes first (at the back with low opacity)
                    foreach (var note in _notes)
                    {
                        if (IsTrackDisabled(note.TrackIndex))
                            DrawTimelineNote(note, isDisabled: true);
                    }
                    // Reset coalescing tracker for enabled pass so enabled notes draw properly over disabled ones
                    Array.Fill(lastRightByTrackPitch, -1.0);
                    // Pass 2: Enabled notes on top
                    foreach (var note in _notes)
                    {
                        if (!IsTrackDisabled(note.TrackIndex))
                            DrawTimelineNote(note, isDisabled: false);
                    }
                }
                else
                {
                    foreach (var note in _notes)
                    {
                        DrawTimelineNote(note, isDisabled: false);
                    }
                }
            }

            // 3. Ruler markings and timestamps
            var durationSec = totalMs / 1000.0;
            var stepSec = durationSec switch
            {
                <= 60 => 10,
                <= 180 => 30,
                <= 600 => 60,
                _ => 120
            };

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (var sec = 0; sec <= durationSec; sec += stepSec)
            {
                var x = (sec * 1000.0 / totalMs) * width;
                if (x >= 0 && x <= width)
                {
                    // Small tick
                    dc.DrawLine(MarkerPen, new Point(x, height - 12), new Point(x, height - 8));

                    var timeSpan = TimeSpan.FromSeconds(sec);
                    var timeText = timeSpan.ToString(@"m\:ss");
                    var formatted = new FormattedText(
                        timeText,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        RulerTypeface,
                        9,
                        RulerTextBrush,
                        dpi);

                    var textX = Math.Clamp(x - (formatted.Width / 2), 2, width - formatted.Width - 2);
                    dc.DrawText(formatted, new Point(textX, height - 11));
                }
            }

            // 4. Key Change Markers
            var markers = KeyChangeMarkers;
            if (markers != null)
            {
                var accentBrush = TrackColorPalette.GetBrush(0, 1);
                var accentPen = new Pen(accentBrush, 2);
                accentPen.Freeze();

                foreach (var marker in markers)
                {
                    var x = (marker.TimeMs / (double)totalMs) * width;
                    var isSelected = marker == SelectedMarker;

                    var pen = isSelected ? accentPen : MarkerPen;
                    var brush = isSelected ? accentBrush : MarkerPinBrush;

                    // Vertical marker line
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, height - 6));

                    // Badge at top
                    var badgeText = marker.NoteDisplay;
                    var formatted = new FormattedText(
                        badgeText,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        MarkerTypeface,
                        9.5,
                        Brushes.Black,
                        dpi);

                    var badgeW = formatted.Width + 8;
                    var badgeH = formatted.Height + 2;
                    var badgeRect = new Rect(x - (badgeW / 2), 1, badgeW, badgeH);

                    dc.DrawRoundedRectangle(brush, pen, badgeRect, 3, 3);
                    dc.DrawText(formatted, new Point(badgeRect.X + 4, badgeRect.Y + 1));
                }
            }
        }

        group.Freeze();
        return group;
    }
}
