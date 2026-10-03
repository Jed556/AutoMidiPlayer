using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.Helpers;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

public class PianoRollCanvas : FrameworkElement
{
    public readonly record struct RollNote(int NoteNumber, long StartMs, long LengthMs, int TrackIndex);

    public event EventHandler<TimeSpan>? ScrubRequested;
    public event EventHandler<KeyChangeMarker>? MarkerClicked;

    private static readonly HashSet<int> BlackKeys = [1, 3, 6, 8, 10];
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    // Frozen brushes and pens for 60+ FPS zero-allocation rendering
    private static readonly SolidColorBrush[] TrackBrushes;
    private static readonly SolidColorBrush WhiteKeyBrush;
    private static readonly SolidColorBrush BlackKeyBrush;
    private static readonly SolidColorBrush BlackKeyExtensionBrush;
    private static readonly SolidColorBrush WhiteLaneBrush;
    private static readonly SolidColorBrush BlackLaneBrush;
    private static readonly SolidColorBrush WhiteKeyTextBrush;
    private static readonly SolidColorBrush BlackKeyTextBrush;
    private static readonly Pen GridLinePen;
    private static readonly Pen KeyBorderPen;
    private static readonly Typeface KeyFontTypeface;
    private static readonly Pen MarkerLinePen;
    private static readonly SolidColorBrush MarkerBadgeBrush;
    private static readonly Pen MarkerBadgePen;
    private static readonly Typeface MarkerTypeface;
    private static readonly Pen HoverLinePen;
    private static readonly SolidColorBrush HoverPillBrush;
    private static readonly Pen HoverPillPen;
    private static readonly SolidColorBrush HoverTextBrush;
    private static readonly Typeface HoverTypeface;
    private static readonly Pen PlayheadLinePen;
    private static readonly SolidColorBrush PlayheadMarkerBrush;
    private static readonly Geometry PlayheadMarkerGeometry;

    static PianoRollCanvas()
    {
        TrackBrushes = TrackColorPalette.AllBrushes;

        WhiteKeyBrush = CreateFrozenBrush(Color.FromArgb(220, 240, 240, 240));
        BlackKeyBrush = CreateFrozenBrush(Color.FromArgb(255, 26, 26, 30));
        BlackKeyExtensionBrush = CreateFrozenBrush(Color.FromArgb(255, 42, 42, 48));
        WhiteLaneBrush = CreateFrozenBrush(Color.FromArgb(12, 255, 255, 255));
        BlackLaneBrush = CreateFrozenBrush(Color.FromArgb(24, 0, 0, 0));
        WhiteKeyTextBrush = CreateFrozenBrush(Color.FromArgb(190, 20, 20, 20));
        BlackKeyTextBrush = CreateFrozenBrush(Color.FromArgb(190, 215, 215, 220));

        GridLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(20, 255, 255, 255)), 1);
        GridLinePen.Freeze();

        KeyBorderPen = new Pen(CreateFrozenBrush(Color.FromArgb(40, 128, 128, 128)), 1);
        KeyBorderPen.Freeze();

        KeyFontTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        MarkerLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(200, 255, 185, 0)), 1.5)
        {
            DashStyle = DashStyles.Dash
        };
        MarkerLinePen.Freeze();

        MarkerBadgeBrush = CreateFrozenBrush(Color.FromRgb(255, 185, 0));
        MarkerBadgePen = new Pen(CreateFrozenBrush(Color.FromRgb(220, 160, 0)), 1);
        MarkerBadgePen.Freeze();

        MarkerTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        HoverLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(120, 255, 255, 255)), 1.2);
        HoverLinePen.Freeze();

        HoverPillBrush = CreateFrozenBrush(Color.FromArgb(220, 20, 20, 24));
        HoverPillPen = new Pen(CreateFrozenBrush(Color.FromArgb(80, 255, 255, 255)), 1);
        HoverPillPen.Freeze();

        HoverTextBrush = CreateFrozenBrush(Color.FromArgb(235, 255, 255, 255));
        HoverTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

        PlayheadMarkerBrush = CreateFrozenBrush(Color.FromRgb(255, 255, 255));
        PlayheadLinePen = new Pen(PlayheadMarkerBrush, 1.8);
        PlayheadLinePen.Freeze();

        var markerGeo = new StreamGeometry();
        using (var ctx = markerGeo.Open())
        {
            ctx.BeginFigure(new Point(-6, 0), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(6, 0), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(0, 10), isStroked: false, isSmoothJoin: false);
        }
        markerGeo.Freeze();
        PlayheadMarkerGeometry = markerGeo;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private double? _hoverX;
    private bool _isDragging;
    private double _dragAnchorMouseX;
    private long _dragAnchorTimeMs;
    private Point _dragStartPos;
    private bool _hasDragged;

    // Zero-allocation batched note rendering buffers per track brush
    private readonly List<Rect>[] _trackRectsSharp;
    private readonly List<Rect>[] _trackRectsRound;
    private readonly double[] _lastPitchRight = new double[128];
    private readonly int[] _lastPitchTrack = new int[128];
    private readonly Dictionary<string, FormattedText> _cachedMarkerLabels = new();
    private double _cachedDpi = -1;

    private RollNote[] _notes = Array.Empty<RollNote>();
    private long _maxNoteLengthMs = 0;
    private int _minPitch = 36;
    private int _maxPitch = 84;
    private readonly bool[] _activeKeys = new bool[128];
    private readonly FormattedText?[] _cachedLabelsNormal = new FormattedText?[128];
    private readonly FormattedText?[] _cachedLabelsActive = new FormattedText?[128];
    private double _lastPitchHeight = -1;
    private double _lastDpi = -1;

    // Cached DrawingGroups for static layers
    private DrawingGroup? _cachedLanes;
    private double _lastLanesWidth = -1;
    private double _lastLanesPitchHeight = -1;
    private int _lastLanesMinPitch = -1;
    private int _lastLanesMaxPitch = -1;
    private RectangleGeometry? _cachedLanesClip;
    private double _lastLanesClipW = -1;
    private double _lastLanesClipH = -1;

    private DrawingGroup? _cachedKeyboard;
    private double _lastKeyboardPitchHeight = -1;
    private int _lastKeyboardMinPitch = -1;
    private int _lastKeyboardMaxPitch = -1;
    private double _lastKeyboardWidth = -1;
    private RectangleGeometry? _cachedKeyboardClip;
    private double _lastKeyboardClipH = -1;

    private double _lastReportedMaxScroll = -1;
    private bool _lastReportedIsOverflowing;

    private SolidColorBrush _activeKeyBrush = null!;
    private SolidColorBrush _activeKeyTextBrush = null!;
    private Color _lastAccentColor;
    private int _trackCount = 1;

    public PianoRollCanvas()
    {
        _trackRectsSharp = new List<Rect>[TrackBrushes.Length];
        _trackRectsRound = new List<Rect>[TrackBrushes.Length];
        for (var i = 0; i < TrackBrushes.Length; i++)
        {
            _trackRectsSharp[i] = new List<Rect>(256);
            _trackRectsRound[i] = new List<Rect>(256);
        }

        EnsureAccentResources();
        Loaded += (_, _) => AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged += OnThemeResourcesChanged;
        Unloaded += (_, _) => AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged -= OnThemeResourcesChanged;
    }

    private void OnThemeResourcesChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            EnsureAccentResources();
            InvalidateVisual();
        });
    }

    private void EnsureAccentResources()
    {
        var accent = AutoMidiPlayer.WPF.Core.AccentColorHelper.GetAccentColor();
        if (_activeKeyBrush != null && accent == _lastAccentColor)
            return;

        _lastAccentColor = accent;
        _activeKeyBrush = CreateFrozenBrush(accent);

        var lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        _activeKeyTextBrush = CreateFrozenBrush(lum > 0.5 ? Color.FromRgb(18, 18, 20) : Colors.White);

        Array.Clear(_cachedLabelsActive, 0, 128);
    }

    public static readonly DependencyProperty MidiFileProperty =
        DependencyProperty.Register(
            nameof(MidiFile),
            typeof(MidiFile),
            typeof(PianoRollCanvas),
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
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimeSpan CurrentTime
    {
        get => (TimeSpan)GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel),
            typeof(double),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, Math.Clamp(value, 0.2, 5.0));
    }

    public static readonly DependencyProperty KeyHeightProperty =
        DependencyProperty.Register(
            nameof(KeyHeight),
            typeof(double),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender, OnKeyHeightChanged));

    private static void OnKeyHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            var oldHeight = (double)e.OldValue;
            var newHeight = (double)e.NewValue;

            if (oldHeight > 0 && Math.Abs(newHeight - oldHeight) > 0.001 && canvas.ActualHeight > 0)
            {
                var viewportHeight = canvas.ActualHeight;
                var centerOffset = canvas.VerticalOffset + (viewportHeight / 2.0);
                var ratio = newHeight / oldHeight;
                var newCenter = centerOffset * ratio;
                var targetOffset = newCenter - (viewportHeight / 2.0);
                var newTotalHeight = (canvas._maxPitch - canvas._minPitch + 1) * newHeight;
                var newMaxScroll = Math.Max(0.0, newTotalHeight - viewportHeight);
                canvas.VerticalOffset = Math.Clamp(targetOffset, 0.0, newMaxScroll);
            }

            canvas._cachedLanes = null;
            canvas._cachedKeyboard = null;
            canvas.UpdateScrollMetrics();
            canvas.ScrollBoundsChanged?.Invoke(canvas, EventArgs.Empty);
            canvas.InvalidateVisual();
        }
    }

    public double KeyHeight
    {
        get => (double)GetValue(KeyHeightProperty);
        set => SetValue(KeyHeightProperty, Math.Clamp(value, 6.0, 50.0));
    }

    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(
            nameof(VerticalOffset),
            typeof(double),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnVerticalOffsetChanged));

    private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            canvas.InvalidateVisual();
            canvas.VerticalOffsetChanged?.Invoke(canvas, (double)e.NewValue);
        }
    }

    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, Math.Max(0.0, value));
    }

    public event EventHandler<double>? VerticalOffsetChanged;
    public event EventHandler? ScrollBoundsChanged;

    public double TotalContentHeight => (_maxPitch - _minPitch + 1) * KeyHeight;
    public double MaxVerticalOffset => Math.Max(0.0, TotalContentHeight - ActualHeight);
    public bool IsOverflowing => TotalContentHeight > ActualHeight && ActualHeight > 0;

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateScrollMetrics();
        ScrollBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateScrollMetrics()
    {
        var maxScroll = MaxVerticalOffset;
        if (VerticalOffset > maxScroll)
        {
            VerticalOffset = maxScroll;
        }
    }

    public static readonly DependencyProperty KeyChangeMarkersProperty =
        DependencyProperty.Register(
            nameof(KeyChangeMarkers),
            typeof(IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>? KeyChangeMarkers
    {
        get => (IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>?)GetValue(KeyChangeMarkersProperty);
        set => SetValue(KeyChangeMarkersProperty, value);
    }

    public double KeyboardWidth { get; set; } = 64;

    public static readonly DependencyProperty HoverTimeProperty =
        DependencyProperty.Register(
            nameof(HoverTime),
            typeof(TimeSpan?),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimeSpan? HoverTime
    {
        get => (TimeSpan?)GetValue(HoverTimeProperty);
        set => SetValue(HoverTimeProperty, value);
    }

    public event EventHandler<TimeSpan?>? HoverTimeChanged;

    public static readonly DependencyProperty IsSyncEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSyncEnabled),
            typeof(bool),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnIsSyncEnabledChanged));

    private static void OnIsSyncEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            var isSync = (bool)e.NewValue;
            if (isSync)
            {
                canvas.ViewTime = null;
            }
            else
            {
                canvas.ViewTime = canvas.CurrentTime;
            }
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
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewTimeChanged));

    private static void OnViewTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            canvas.ViewTimeChanged?.Invoke(canvas, (TimeSpan?)e.NewValue);
        }
    }

    public TimeSpan? ViewTime
    {
        get => (TimeSpan?)GetValue(ViewTimeProperty);
        set => SetValue(ViewTimeProperty, value);
    }

    public event EventHandler<TimeSpan?>? ViewTimeChanged;

    public long TotalDurationMs
    {
        get
        {
            if (MidiFile?.Duration.TotalMilliseconds > 0)
                return (long)MidiFile.Duration.TotalMilliseconds;

            if (_notes.Length > 0)
                return _notes.Max(n => n.StartMs + n.LengthMs);

            return 0;
        }
    }

    private static void OnMidiFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            canvas.RebuildNoteIndex();
        }
    }

    public void RebuildNoteIndex()
    {
        var file = MidiFile;
        if (file == null)
        {
            _notes = Array.Empty<RollNote>();
            _maxNoteLengthMs = 0;
            _trackCount = 1;
            _minPitch = 36;
            _maxPitch = 84;
            _cachedLanes = null;
            _cachedKeyboard = null;
            InvalidateVisual();
            return;
        }

        try
        {
            file.EnsureMidiLoaded();

            if (file.Midi is null)
            {
                _notes = Array.Empty<RollNote>();
                _maxNoteLengthMs = 0;
                _trackCount = 1;
                _minPitch = 36;
                _maxPitch = 84;
                _cachedLanes = null;
                _cachedKeyboard = null;
                InvalidateVisual();
                return;
            }

            var tempoMap = file.OriginalTempoMap ?? file.Midi.GetTempoMap();
            var trackChunks = file.Midi.GetTrackChunks().ToList();
            var noteList = new List<RollNote>();

            if (trackChunks.Count > 0)
            {
                for (var t = 0; t < trackChunks.Count; t++)
                {
                    foreach (var note in trackChunks[t].GetNotes())
                    {
                        var startMs = (long)note.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                        var lengthMs = (long)note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                        noteList.Add(new RollNote(note.NoteNumber, startMs, Math.Max(20, lengthMs), t));
                    }
                }
            }
            else
            {
                foreach (var note in file.Midi.GetNotes())
                {
                    var startMs = (long)note.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    var lengthMs = (long)note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    noteList.Add(new RollNote(note.NoteNumber, startMs, Math.Max(20, lengthMs), (int)note.Channel));
                }
            }

            _notes = noteList.OrderBy(n => n.StartMs).ToArray();
            _maxNoteLengthMs = _notes.Length > 0 ? _notes.Max(n => n.LengthMs) : 0;
            _trackCount = _notes.Length > 0 ? _notes.Select(n => n.TrackIndex).Distinct().Count() : 1;

            if (_notes.Length > 0)
            {
                var min = _notes.Min(n => n.NoteNumber);
                var max = _notes.Max(n => n.NoteNumber);

                _minPitch = Math.Max(21, min - 2);
                _maxPitch = Math.Min(108, max + 2);

                if (_maxPitch - _minPitch < 24)
                {
                    var diff = 24 - (_maxPitch - _minPitch);
                    _minPitch = Math.Max(21, _minPitch - diff / 2);
                    _maxPitch = Math.Min(108, _minPitch + 24);
                }
            }
            else
            {
                _minPitch = 36;
                _maxPitch = 84;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to build piano roll note index.");
            _notes = Array.Empty<RollNote>();
            _maxNoteLengthMs = 0;
            _trackCount = 1;
        }

        _cachedLanes = null;
        _cachedKeyboard = null;
        var totalHeight = (_maxPitch - _minPitch + 1) * KeyHeight;
        if (ActualHeight > 0 && totalHeight > ActualHeight)
        {
            VerticalOffset = (totalHeight - ActualHeight) / 2.0;
        }
        else
        {
            VerticalOffset = 0.0;
        }
        ScrollBoundsChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private DrawingGroup BuildStaticLanes(double width, double pitchHeight)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var laneWidth = width - KeyboardWidth;
            for (var p = _minPitch; p <= _maxPitch; p++)
            {
                var y = (_maxPitch - p) * pitchHeight;
                var isBlack = BlackKeys.Contains(p % 12);
                var laneBrush = isBlack ? BlackLaneBrush : WhiteLaneBrush;

                dc.DrawRectangle(laneBrush, null, new Rect(KeyboardWidth, y, laneWidth, pitchHeight));
                dc.DrawLine(GridLinePen, new Point(KeyboardWidth, y), new Point(width, y));
            }
        }
        group.Freeze();
        return group;
    }

    private DrawingGroup BuildStaticKeyboard(double pitchHeight, double dpi)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var fontSize = Math.Min(10, Math.Max(7.5, pitchHeight * 0.65));
            var blackCapW = KeyboardWidth * 0.62;
            var extW = KeyboardWidth - blackCapW;

            for (var p = _minPitch; p <= _maxPitch; p++)
            {
                var y = (_maxPitch - p) * pitchHeight;
                var pitchClass = p % 12;
                var isBlack = BlackKeys.Contains(pitchClass);
                var octave = (p / 12) - 1;
                var label = $"{NoteNames[pitchClass]}{octave}";

                if (isBlack)
                {
                    // 1. Black key cap (0 to blackCapW)
                    dc.DrawRectangle(BlackKeyBrush, KeyBorderPen, new Rect(0, y, blackCapW, pitchHeight));

                    // 2. Lower grey extension of the black key (blackCapW to KeyboardWidth)
                    dc.DrawRectangle(BlackKeyExtensionBrush, KeyBorderPen, new Rect(blackCapW, y, extW, pitchHeight));

                    // 3. Key text on the lower grey part
                    if (pitchHeight >= 11)
                    {
                        var text = GetOrCreateLabel(p, label, BlackKeyTextBrush, fontSize, dpi, isNormal: true);
                        var textY = y + (pitchHeight - text.Height) / 2;
                        var textX = Math.Max(blackCapW + 2, KeyboardWidth - text.Width - 4);
                        dc.DrawText(text, new Point(textX, textY));
                    }
                }
                else
                {
                    // White key (0 to KeyboardWidth)
                    dc.DrawRectangle(WhiteKeyBrush, KeyBorderPen, new Rect(0, y, KeyboardWidth, pitchHeight));

                    // Note label for C keys or when pitchHeight is large enough
                    if (pitchClass == 0 || pitchHeight >= 11)
                    {
                        var text = GetOrCreateLabel(p, label, WhiteKeyTextBrush, fontSize, dpi, isNormal: true);
                        var textY = y + (pitchHeight - text.Height) / 2;
                        var textX = Math.Max(4, KeyboardWidth - text.Width - 4);
                        dc.DrawText(text, new Point(textX, textY));
                    }
                }
            }
        }
        group.Freeze();
        return group;
    }

    private FormattedText GetOrCreateLabel(int pitch, string label, Brush brush, double fontSize, double dpi, bool isNormal)
    {
        var cache = isNormal ? _cachedLabelsNormal : _cachedLabelsActive;
        if (pitch >= 0 && pitch < 128 && cache[pitch] != null)
        {
            return cache[pitch]!;
        }

        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            KeyFontTypeface,
            fontSize,
            brush,
            dpi);

        if (pitch >= 0 && pitch < 128)
        {
            cache[pitch] = text;
        }

        return text;
    }

    private FormattedText GetOrCreateMarkerLabel(string text, double dpi)
    {
        if (!_cachedMarkerLabels.TryGetValue(text, out var ft))
        {
            ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                MarkerTypeface,
                9.5,
                Brushes.Black,
                dpi);
            _cachedMarkerLabels[text] = ft;
        }
        return ft;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _cachedDpi = newDpi.PixelsPerDip;
        _cachedMarkerLabels.Clear();
        Array.Clear(_cachedLabelsNormal, 0, 128);
        Array.Clear(_cachedLabelsActive, 0, 128);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        EnsureAccentResources();

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= KeyboardWidth + 10 || height <= 20)
            return;

        var pitchCount = _maxPitch - _minPitch + 1;
        var pitchHeight = Math.Clamp(KeyHeight, 6.0, 50.0);
        var totalHeight = pitchCount * pitchHeight;
        var maxScroll = Math.Max(0.0, totalHeight - height);
        var verticalOffset = Math.Clamp(VerticalOffset, 0.0, maxScroll);
        if (Math.Abs(verticalOffset - VerticalOffset) > 0.001)
        {
            VerticalOffset = verticalOffset;
        }

        if (Math.Abs(maxScroll - _lastReportedMaxScroll) > 0.5 || (totalHeight > height) != _lastReportedIsOverflowing)
        {
            _lastReportedMaxScroll = maxScroll;
            _lastReportedIsOverflowing = totalHeight > height;
            Dispatcher.BeginInvoke(() => ScrollBoundsChanged?.Invoke(this, EventArgs.Empty));
        }

        var currentTimeMs = (long)CurrentTime.TotalMilliseconds;
        var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
            ? (long)ViewTime.Value.TotalMilliseconds
            : currentTimeMs;
        var pixelsPerMs = 0.15 * ZoomLevel; // 150px per second at zoom 1.0

        if (_cachedDpi <= 0)
        {
            _cachedDpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }
        var dpi = _cachedDpi;

        // Invalidate label caches if geometry or DPI changed
        if (Math.Abs(pitchHeight - _lastPitchHeight) > 0.001 || Math.Abs(dpi - _lastDpi) > 0.001)
        {
            Array.Clear(_cachedLabelsNormal, 0, 128);
            Array.Clear(_cachedLabelsActive, 0, 128);
            _lastPitchHeight = pitchHeight;
            _lastDpi = dpi;
            _cachedKeyboard = null;
        }

        // 1. Draw cached pitch lanes
        if (_cachedLanes == null || Math.Abs(_lastLanesWidth - width) > 0.5 || Math.Abs(_lastLanesPitchHeight - pitchHeight) > 0.001 || _lastLanesMinPitch != _minPitch || _lastLanesMaxPitch != _maxPitch)
        {
            _cachedLanes = BuildStaticLanes(width, pitchHeight);
            _lastLanesWidth = width;
            _lastLanesPitchHeight = pitchHeight;
            _lastLanesMinPitch = _minPitch;
            _lastLanesMaxPitch = _maxPitch;
        }
        if (_cachedLanesClip == null || Math.Abs(_lastLanesClipW - width) > 0.5 || Math.Abs(_lastLanesClipH - height) > 0.5)
        {
            _cachedLanesClip = new RectangleGeometry(new Rect(KeyboardWidth, 0, Math.Max(0, width - KeyboardWidth), height));
            _cachedLanesClip.Freeze();
            _lastLanesClipW = width;
            _lastLanesClipH = height;
        }
        dc.PushClip(_cachedLanesClip);
        dc.PushTransform(new TranslateTransform(0, -verticalOffset));
        dc.DrawDrawing(_cachedLanes);
        dc.Pop();
        dc.Pop();

        // 2. Draw vertical time grid lines (every 1 second)
        var msStep = 1000L;
        if (ZoomLevel < 0.5) msStep = 2000L;
        else if (ZoomLevel > 2.0) msStep = 500L;

        var firstGridMs = (effectiveViewTimeMs / msStep) * msStep;
        var rollWidth = width - KeyboardWidth;
        var visibleHorizonMs = (long)(rollWidth / pixelsPerMs);

        for (var gridMs = firstGridMs; gridMs <= effectiveViewTimeMs + visibleHorizonMs + msStep; gridMs += msStep)
        {
            var lineX = KeyboardWidth + (gridMs - effectiveViewTimeMs) * pixelsPerMs;
            if (lineX >= KeyboardWidth && lineX <= width)
            {
                dc.DrawLine(GridLinePen, new Point(lineX, 0), new Point(lineX, height));
            }
        }

        // Reset active keys
        Array.Clear(_activeKeys, 0, _activeKeys.Length);

        // Clear pre-allocated track batch buffers
        for (var t = 0; t < TrackBrushes.Length; t++)
        {
            _trackRectsSharp[t].Clear();
            _trackRectsRound[t].Clear();
        }
        Array.Fill(_lastPitchRight, -1.0);
        Array.Fill(_lastPitchTrack, -1);

        // 3. Viewport-culled batched note rendering
        if (_notes.Length > 0)
        {
            var visibleEndMs = effectiveViewTimeMs + visibleHorizonMs + 500;
            // Notes ending at or before effectiveViewTimeMs have their right edge <= KeyboardWidth (behind keyboard)
            var startIndex = FindFirstPossibleNoteIndex(effectiveViewTimeMs);

            // Adaptive aesthetic rounding: only when individual notes are large enough to appreciate rounded corners
            var allowRounding = ZoomLevel >= 0.5 && pitchHeight >= 5.0;

            for (var i = startIndex; i < _notes.Length; i++)
            {
                var note = _notes[i];
                if (note.StartMs > visibleEndMs)
                    break;

                var noteEndMs = note.StartMs + note.LengthMs;
                if (noteEndMs <= effectiveViewTimeMs)
                    continue;

                // Check active hit state
                if (note.StartMs <= currentTimeMs && currentTimeMs <= noteEndMs)
                {
                    if (note.NoteNumber >= 0 && note.NoteNumber < _activeKeys.Length)
                        _activeKeys[note.NoteNumber] = true;
                }

                // Compute position: moving right to left toward KeyboardWidth
                var noteX = KeyboardWidth + (note.StartMs - effectiveViewTimeMs) * pixelsPerMs;
                var noteW = Math.Max(3.0, note.LengthMs * pixelsPerMs);

                // Clip notes as they pass behind the keyboard
                if (noteX < KeyboardWidth)
                {
                    noteW -= (KeyboardWidth - noteX);
                    noteX = KeyboardWidth;
                }

                if (noteW <= 0.5)
                    continue;

                var pitch = note.NoteNumber;
                var trackIdx = Math.Abs(note.TrackIndex) % TrackBrushes.Length;

                // Sub-pixel coalescing for dense notes on the same track and pitch
                if (pitch >= 0 && pitch < 128)
                {
                    if (_lastPitchTrack[pitch] == trackIdx)
                    {
                        var lastRight = _lastPitchRight[pitch];
                        if (noteX + noteW <= lastRight + 0.5)
                        {
                            // Completely covered by previous note on same track and pitch
                            continue;
                        }

                        if (noteX < lastRight)
                        {
                            // Overlaps tail of previous note: trim overlapping segment
                            noteW -= (lastRight - noteX);
                            noteX = lastRight;
                            if (noteW <= 0.5) continue;
                        }
                    }

                    _lastPitchTrack[pitch] = trackIdx;
                    _lastPitchRight[pitch] = noteX + noteW;
                }

                var noteY = (_maxPitch - pitch) * pitchHeight - verticalOffset;
                var noteH = Math.Max(2, pitchHeight - 1);
                if (noteY + noteH < 0 || noteY > height)
                    continue;

                var rect = new Rect(noteX, noteY + 0.5, noteW, noteH);

                if (allowRounding && noteW >= 6.0)
                {
                    _trackRectsRound[trackIdx].Add(rect);
                }
                else
                {
                    _trackRectsSharp[trackIdx].Add(rect);
                }
            }

            // Draw batched notes track by track with single frozen brushes
            for (var t = 0; t < TrackBrushes.Length; t++)
            {
                var brush = (_trackCount == 1) ? _activeKeyBrush : TrackBrushes[t];

                var sharps = _trackRectsSharp[t];
                var sharpCount = sharps.Count;
                for (var r = 0; r < sharpCount; r++)
                {
                    dc.DrawRectangle(brush, null, sharps[r]);
                }

                var rounds = _trackRectsRound[t];
                var roundCount = rounds.Count;
                if (roundCount > 0)
                {
                    var radius = Math.Min(6.0, Math.Max(2.5, pitchHeight * 0.45));
                    for (var r = 0; r < roundCount; r++)
                    {
                        var rect = rounds[r];
                        var cornerR = Math.Min(radius, rect.Width * 0.45);
                        dc.DrawRoundedRectangle(brush, null, rect, cornerR, cornerR);
                    }
                }
            }
        }

        // 4. Draw key change markers scrolling with the notes
        var markers = KeyChangeMarkers;
        if (markers != null)
        {
            foreach (var marker in markers)
            {
                var markerX = KeyboardWidth + (marker.TimeMs - effectiveViewTimeMs) * pixelsPerMs;
                if (markerX >= KeyboardWidth && markerX <= width)
                {
                    // Vertical marker guide line
                    dc.DrawLine(MarkerLinePen, new Point(markerX, 0), new Point(markerX, height));

                    // Key badge at top
                    var badgeText = marker.NoteDisplay;
                    var formatted = GetOrCreateMarkerLabel(badgeText, dpi);

                    var badgeW = formatted.Width + 8;
                    var badgeH = formatted.Height + 2;
                    var badgeRect = new Rect(Math.Max(KeyboardWidth + 1, markerX - (badgeW / 2)), 2, badgeW, badgeH);

                    dc.DrawRoundedRectangle(MarkerBadgeBrush, MarkerBadgePen, badgeRect, 3, 3);
                    dc.DrawText(formatted, new Point(badgeRect.X + 4, badgeRect.Y + 1));
                }
            }
        }

        // 4.5. Draw hover guide line and timestamp pill
        double? effectiveHoverX = null;
        long hoverMs = 0;

        if (_hoverX.HasValue && _hoverX.Value >= KeyboardWidth && _hoverX.Value <= width)
        {
            effectiveHoverX = _hoverX.Value;
            hoverMs = effectiveViewTimeMs + (long)((effectiveHoverX.Value - KeyboardWidth) / pixelsPerMs);
        }
        else if (HoverTime.HasValue)
        {
            var targetMs = (long)HoverTime.Value.TotalMilliseconds;
            var targetX = KeyboardWidth + (targetMs - effectiveViewTimeMs) * pixelsPerMs;
            if (targetX >= KeyboardWidth && targetX <= width)
            {
                effectiveHoverX = targetX;
                hoverMs = targetMs;
            }
        }

        if (effectiveHoverX.HasValue)
        {
            var hx = effectiveHoverX.Value;
            dc.DrawLine(HoverLinePen, new Point(hx, 0), new Point(hx, height));
            var totalMs = TotalDurationMs;
            if (totalMs > 0) hoverMs = Math.Clamp(hoverMs, 0, totalMs);
            else hoverMs = Math.Max(0, hoverMs);

            var hoverTime = TimeSpan.FromMilliseconds(hoverMs);
            var timeText = hoverTime.TotalHours >= 1
                ? hoverTime.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture)
                : hoverTime.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);

            var formattedHover = new FormattedText(
                timeText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                HoverTypeface,
                9.5,
                HoverTextBrush,
                dpi);

            var pillW = formattedHover.Width + 8;
            var pillH = formattedHover.Height + 2;
            var pillX = Math.Clamp(hx - (pillW / 2), KeyboardWidth + 2, width - pillW - 2);
            var pillY = height - pillH - 3;
            var pillRect = new Rect(pillX, pillY, pillW, pillH);

            dc.DrawRoundedRectangle(HoverPillBrush, HoverPillPen, pillRect, 3, 3);
            dc.DrawText(formattedHover, new Point(pillRect.X + 4, pillRect.Y + 1));
        }

        // 5. Draw cached stationary keyboard
        if (_cachedKeyboard == null || Math.Abs(_lastKeyboardPitchHeight - pitchHeight) > 0.001 || _lastKeyboardMinPitch != _minPitch || _lastKeyboardMaxPitch != _maxPitch || Math.Abs(_lastKeyboardWidth - KeyboardWidth) > 0.1)
        {
            _cachedKeyboard = BuildStaticKeyboard(pitchHeight, dpi);
            _lastKeyboardPitchHeight = pitchHeight;
            _lastKeyboardMinPitch = _minPitch;
            _lastKeyboardMaxPitch = _maxPitch;
            _lastKeyboardWidth = KeyboardWidth;
        }

        if (_cachedKeyboardClip == null || Math.Abs(_lastKeyboardClipH - height) > 0.5)
        {
            _cachedKeyboardClip = new RectangleGeometry(new Rect(0, 0, KeyboardWidth, height));
            _cachedKeyboardClip.Freeze();
            _lastKeyboardClipH = height;
        }
        dc.PushClip(_cachedKeyboardClip);
        dc.PushTransform(new TranslateTransform(0, -verticalOffset));
        dc.DrawDrawing(_cachedKeyboard);
        dc.Pop();
        dc.Pop();

        // 5. Draw active key overlays on top (only for pressed keys)
        var fontSize = Math.Min(10, Math.Max(7.5, pitchHeight * 0.65));
        for (var p = _minPitch; p <= _maxPitch; p++)
        {
            if (p < 0 || p >= _activeKeys.Length || !_activeKeys[p])
                continue;

            var y = (_maxPitch - p) * pitchHeight - verticalOffset;
            if (y + pitchHeight < 0 || y > height)
                continue;

            var pitchClass = p % 12;
            var isBlack = BlackKeys.Contains(pitchClass);
            var octave = (p / 12) - 1;
            var label = $"{NoteNames[pitchClass]}{octave}";

            // Highlight full key with active accent
            dc.DrawRectangle(_activeKeyBrush, KeyBorderPen, new Rect(0, y, KeyboardWidth, pitchHeight));

            if (!isBlack || pitchHeight >= 11)
            {
                var text = GetOrCreateLabel(p, label, _activeKeyTextBrush, fontSize, dpi, isNormal: false);
                var textY = y + (pitchHeight - text.Height) / 2;
                var textX = Math.Max(4, KeyboardWidth - text.Width - 4);
                dc.DrawText(text, new Point(textX, textY));
            }
        }

        // 6. Draw Subtle Keyboard Separator Border at KeyboardWidth
        dc.DrawLine(KeyBorderPen, new Point(KeyboardWidth, 0), new Point(KeyboardWidth, height));

        // 7. Draw Moving Playhead Marker when Follow Marker is OFF (!IsSyncEnabled)
        if (!IsSyncEnabled)
        {
            var playheadX = KeyboardWidth + (currentTimeMs - effectiveViewTimeMs) * pixelsPerMs;
            if (playheadX >= KeyboardWidth && playheadX <= width)
            {
                dc.DrawLine(PlayheadLinePen, new Point(playheadX, 0), new Point(playheadX, height));
                dc.PushTransform(new TranslateTransform(playheadX, 0));
                dc.DrawGeometry(PlayheadMarkerBrush, null, PlayheadMarkerGeometry);
                dc.Pop();
            }
        }
    }

    private int FindFirstPossibleNoteIndex(long targetMs)
    {
        if (_notes.Length == 0) return 0;
        var minStart = targetMs - _maxNoteLengthMs;
        var low = 0;
        var high = _notes.Length - 1;
        var result = 0;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (_notes[mid].StartMs >= minStart)
            {
                result = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return result;
    }

    #region Mouse & Scrub Interactions

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var pt = hitTestParameters.HitPoint;
        if (pt.X >= 0 && pt.X <= ActualWidth && pt.Y >= 0 && pt.Y <= ActualHeight)
        {
            return new PointHitTestResult(this, pt);
        }
        return base.HitTestCore(hitTestParameters);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        if (pos.X >= KeyboardWidth && pos.X <= ActualWidth && ActualWidth > KeyboardWidth + 10)
        {
            _isDragging = true;
            _hasDragged = false;
            _dragStartPos = pos;
            CaptureMouse();

            var pixelsPerMs = 0.15 * ZoomLevel;
            var currentTimeMs = (long)CurrentTime.TotalMilliseconds;
            var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
                ? (long)ViewTime.Value.TotalMilliseconds
                : currentTimeMs;
            var clickedMs = effectiveViewTimeMs + (long)((pos.X - KeyboardWidth) / pixelsPerMs);

            // Check if user clicked on / near a key change marker (within 8px)
            if (KeyChangeMarkers != null)
            {
                foreach (var marker in KeyChangeMarkers)
                {
                    var markerX = KeyboardWidth + (marker.TimeMs - effectiveViewTimeMs) * pixelsPerMs;
                    if (Math.Abs(pos.X - markerX) <= 8)
                    {
                        clickedMs = marker.TimeMs;
                        MarkerClicked?.Invoke(this, marker);
                        break;
                    }
                }
            }

            var totalMs = TotalDurationMs;
            if (totalMs > 0) clickedMs = Math.Clamp(clickedMs, 0, totalMs);
            else clickedMs = Math.Max(0, clickedMs);

            _dragAnchorTimeMs = effectiveViewTimeMs;
            _dragAnchorMouseX = pos.X;
            _hoverX = pos.X;

            if (IsSyncEnabled)
            {
                ScrubRequested?.Invoke(this, TimeSpan.FromMilliseconds(clickedMs));
            }
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);

        if (_isDragging && IsMouseCaptured)
        {
            var deltaX = pos.X - _dragAnchorMouseX;
            var pixelsPerMs = 0.15 * ZoomLevel;

            if (Math.Abs(pos.X - _dragStartPos.X) > 3.0)
            {
                _hasDragged = true;
            }

            if (IsSyncEnabled)
            {
                if (pixelsPerMs > 0 && Math.Abs(deltaX) >= 1.0)
                {
                    var currentTimeMs = (long)CurrentTime.TotalMilliseconds;
                    var deltaMs = (long)(deltaX / pixelsPerMs);
                    var newTargetMs = currentTimeMs + deltaMs;
                    var totalMs = TotalDurationMs;
                    if (totalMs > 0) newTargetMs = Math.Clamp(newTargetMs, 0, totalMs);
                    else newTargetMs = Math.Max(0, newTargetMs);

                    _dragAnchorMouseX = pos.X;
                    ScrubRequested?.Invoke(this, TimeSpan.FromMilliseconds(newTargetMs));
                }
            }
            else
            {
                if (pixelsPerMs > 0 && Math.Abs(deltaX) >= 1.0)
                {
                    var currentViewMs = ViewTime.HasValue ? (long)ViewTime.Value.TotalMilliseconds : (long)CurrentTime.TotalMilliseconds;
                    var deltaMs = (long)(deltaX / pixelsPerMs);
                    var newViewMs = currentViewMs - deltaMs;
                    var totalMs = TotalDurationMs;
                    if (totalMs > 0) newViewMs = Math.Clamp(newViewMs, 0, totalMs);
                    else newViewMs = Math.Max(0, newViewMs);

                    _dragAnchorMouseX = pos.X;
                    ViewTime = TimeSpan.FromMilliseconds(newViewMs);
                }
            }

            _hoverX = Math.Clamp(pos.X, KeyboardWidth, ActualWidth);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (pos.X >= KeyboardWidth && pos.X <= ActualWidth)
        {
            _hoverX = pos.X;
            Cursor = Cursors.Hand;
            var pixelsPerMs = 0.15 * ZoomLevel;
            var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
                ? (long)ViewTime.Value.TotalMilliseconds
                : (long)CurrentTime.TotalMilliseconds;
            var hoverMs = effectiveViewTimeMs + (long)((pos.X - KeyboardWidth) / pixelsPerMs);
            var totalMs = TotalDurationMs;
            if (totalMs > 0) hoverMs = Math.Clamp(hoverMs, 0, totalMs);
            else hoverMs = Math.Max(0, hoverMs);
            HoverTimeChanged?.Invoke(this, TimeSpan.FromMilliseconds(hoverMs));
            InvalidateVisual();
        }
        else
        {
            if (_hoverX.HasValue)
            {
                _hoverX = null;
                Cursor = Cursors.Arrow;
                HoverTimeChanged?.Invoke(this, null);
                InvalidateVisual();
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();

            if (!IsSyncEnabled && !_hasDragged)
            {
                var pos = e.GetPosition(this);
                if (pos.X >= KeyboardWidth && pos.X <= ActualWidth)
                {
                    var pixelsPerMs = 0.15 * ZoomLevel;
                    var effectiveViewTimeMs = ViewTime.HasValue ? (long)ViewTime.Value.TotalMilliseconds : (long)CurrentTime.TotalMilliseconds;
                    var targetMs = effectiveViewTimeMs + (long)((pos.X - KeyboardWidth) / pixelsPerMs);
                    var totalMs = TotalDurationMs;
                    if (totalMs > 0) targetMs = Math.Clamp(targetMs, 0, totalMs);
                    else targetMs = Math.Max(0, targetMs);

                    ScrubRequested?.Invoke(this, TimeSpan.FromMilliseconds(targetMs));
                }
            }

            var posUp = e.GetPosition(this);
            if (posUp.X < KeyboardWidth || posUp.X > ActualWidth)
            {
                _hoverX = null;
                Cursor = Cursors.Arrow;
                HoverTimeChanged?.Invoke(this, null);
            }
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_isDragging)
        {
            if (_hoverX.HasValue)
            {
                _hoverX = null;
                Cursor = Cursors.Arrow;
                HoverTimeChanged?.Invoke(this, null);
                InvalidateVisual();
            }
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isDragging = false;
    }

    #endregion
}
