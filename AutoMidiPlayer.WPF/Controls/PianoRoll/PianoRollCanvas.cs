using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Specialized;
using System.ComponentModel;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Midi;
using AutoMidiPlayer.WPF.Helpers;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

public class PianoRollCanvas : FrameworkElement
{
    public readonly record struct RollNote(int RawNoteNumber, int NoteNumber, long StartMs, long LengthMs, int TrackIndex);

    public event EventHandler<TimeSpan>? ScrubRequested;
    public event EventHandler<KeyChangeMarker>? MarkerClicked;
    public event EventHandler<(KeyChangeMarker Marker, double MarkerX, Point MousePos)?>? MarkerHoverChanged;
    public event EventHandler<(double? HoverX, long HoverMs)>? HoverPositionChanged;

    private static readonly HashSet<int> BlackKeys = [1, 3, 6, 8, 10];
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] MidiPitchLabels = new string[128];

    // Frozen brushes and pens for 60+ FPS zero-allocation rendering
    private static readonly SolidColorBrush[] TrackBrushes;
    private static readonly SolidColorBrush[] DisabledTrackBrushes;
    private static readonly SolidColorBrush WhiteKeyBrush;
    private static readonly SolidColorBrush BlackKeyBrush;
    private static readonly SolidColorBrush BlackKeyExtensionBrush;
    private static readonly SolidColorBrush WhiteKeyTextBrush;
    private static readonly SolidColorBrush BlackKeyTextBrush;
    private static readonly Pen KeyBorderPen;
    private static readonly Typeface KeyFontTypeface;
    private static readonly Pen MarkerLinePen;
    private static readonly SolidColorBrush MarkerBadgeBrush;
    private static readonly Pen MarkerBadgePen;
    private static readonly Typeface MarkerTypeface;
    private static readonly Geometry PlayheadMarkerGeometry;

    static PianoRollCanvas()
    {
        for (var p = 0; p < 128; p++)
        {
            var pitchClass = p % 12;
            var octave = (p / 12) - 1;
            MidiPitchLabels[p] = $"{NoteNames[pitchClass]}{octave}";
        }

        TrackBrushes = TrackColorPalette.AllBrushes;
        DisabledTrackBrushes = new SolidColorBrush[TrackBrushes.Length];
        for (var i = 0; i < TrackBrushes.Length; i++)
        {
            var c = TrackBrushes[i].Color;
            DisabledTrackBrushes[i] = CreateFrozenBrush(Color.FromArgb((byte)(c.A * 0.28), c.R, c.G, c.B));
        }

        WhiteKeyBrush = CreateFrozenBrush(Color.FromArgb(220, 240, 240, 240));
        BlackKeyBrush = CreateFrozenBrush(Color.FromArgb(255, 26, 26, 30));
        BlackKeyExtensionBrush = CreateFrozenBrush(Color.FromArgb(255, 42, 42, 48));
        WhiteKeyTextBrush = CreateFrozenBrush(Color.FromArgb(190, 20, 20, 20));
        BlackKeyTextBrush = CreateFrozenBrush(Color.FromArgb(190, 215, 215, 220));

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
    private readonly List<Rect>[] _trackRects;
    private readonly double[] _lastPitchStart = new double[TrackColorPalette.AllBrushes.Length * 128];
    private readonly double[] _lastPitchEnd = new double[TrackColorPalette.AllBrushes.Length * 128];
    private readonly TranslateTransform _lanesTransform = new();
    private readonly TranslateTransform _keyboardTransform = new();
    private readonly TranslateTransform _playheadTransform = new();
    private readonly Dictionary<string, FormattedText> _cachedMarkerLabels = new();
    private long _lastRenderTimestamp;
    private long _lastCurrentTimeChangeTimestamp;
    private long? _lastReportedHoverMs;
    private KeyChangeMarker? _lastHitMarker;
    private double _cachedDpi = -1;

    public bool IsPlaybackActive =>
        System.Diagnostics.Stopwatch.GetElapsedTime(_lastCurrentTimeChangeTimestamp).TotalMilliseconds < 150;

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

    private DrawingGroup? _cachedKeyboard;
    private double _lastKeyboardPitchHeight = -1;
    private int _lastKeyboardMinPitch = -1;
    private int _lastKeyboardMaxPitch = -1;
    private double _lastKeyboardWidth = -1;

    private double _lastReportedMaxScroll = -1;
    private bool _lastReportedIsOverflowing;

    private SolidColorBrush _activeKeyBrush = null!;
    private SolidColorBrush _disabledActiveKeyBrush = null!;
    private SolidColorBrush _activeKeyTextBrush = null!;
    private SolidColorBrush _playheadMarkerBrush = null!;
    private Pen _playheadLinePen = null!;
    private Pen _gridLinePen = null!;
    private SolidColorBrush _whiteLaneBrush = null!;
    private SolidColorBrush _blackLaneBrush = null!;
    private Color _lastAccentColor;
    private bool _lastIsDark;
    private bool _themeInitialized;
    private int _trackCount = 1;

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

    public PianoRollCanvas()
    {
        _trackRects = new List<Rect>[TrackBrushes.Length];
        for (var i = 0; i < TrackBrushes.Length; i++)
        {
            _trackRects[i] = new List<Rect>(1024);
        }

        EnsureThemeResources();
        Loaded += (_, _) =>
        {
            AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged += OnThemeResourcesChanged;
            UpdateTrackSubscriptions();
            EnsureThemeResources();
            InvalidateVisual();
        };
        Unloaded += (_, _) =>
        {
            AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged -= OnThemeResourcesChanged;
            UnsubscribeTrackEvents();
        };
    }

    private void OnThemeResourcesChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            EnsureThemeResources();
            InvalidateVisual();
        });
    }

    private void EnsureThemeResources()
    {
        var accent = AutoMidiPlayer.WPF.Core.AccentColorHelper.GetAccentColor();
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        var isDark = theme switch
        {
            Wpf.Ui.Appearance.ApplicationTheme.Dark => true,
            Wpf.Ui.Appearance.ApplicationTheme.Light => false,
            _ => AutoMidiPlayer.WPF.Services.SystemThemeService.GetSystemTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark
        };

        if (_themeInitialized && accent == _lastAccentColor && isDark == _lastIsDark)
            return;

        _lastAccentColor = accent;
        _lastIsDark = isDark;
        _themeInitialized = true;

        _activeKeyBrush = CreateFrozenBrush(accent);
        _disabledActiveKeyBrush = CreateFrozenBrush(Color.FromArgb((byte)(accent.A * 0.28), accent.R, accent.G, accent.B));

        var lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        _activeKeyTextBrush = CreateFrozenBrush(lum > 0.5 ? Color.FromRgb(18, 18, 20) : Colors.White);

        if (isDark)
        {
            // Dark Mode: White / Light shades
            _playheadMarkerBrush = CreateFrozenBrush(Color.FromRgb(255, 255, 255));
            _playheadLinePen = new Pen(_playheadMarkerBrush, 1.8);
            _playheadLinePen.Freeze();

            _gridLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(20, 255, 255, 255)), 1);
            _gridLinePen.Freeze();
            _whiteLaneBrush = CreateFrozenBrush(Color.FromArgb(12, 255, 255, 255));
            _blackLaneBrush = CreateFrozenBrush(Color.FromArgb(24, 0, 0, 0));
        }
        else
        {
            // Light Mode: Black / Dark shades
            _playheadMarkerBrush = CreateFrozenBrush(Color.FromRgb(24, 24, 28));
            _playheadLinePen = new Pen(_playheadMarkerBrush, 1.8);
            _playheadLinePen.Freeze();

            _gridLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(20, 0, 0, 0)), 1);
            _gridLinePen.Freeze();
            _whiteLaneBrush = CreateFrozenBrush(Color.FromArgb(8, 0, 0, 0));
            _blackLaneBrush = CreateFrozenBrush(Color.FromArgb(18, 0, 0, 0));
        }
        _cachedLanes = null;
        _cachedKeyboard = null;
        Array.Clear(_cachedLabelsNormal, 0, 128);
        Array.Clear(_cachedLabelsActive, 0, 128);
        _cachedMarkerLabels.Clear();
        InvalidateNoteBitmap();
    }

    public static readonly DependencyProperty MidiTracksProperty =
        DependencyProperty.Register(
            nameof(MidiTracks),
            typeof(IEnumerable<MidiTrack>),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMidiTracksChanged));

    private static void OnMidiTracksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
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

        InvalidateNoteBitmap();
        InvalidateVisual();
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
            new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender, OnCurrentTimePropertyChanged));

    private static void OnCurrentTimePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            canvas._lastCurrentTimeChangeTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            if (canvas.IsPlaybackActive || canvas.IsPlaying)
            {
                if (canvas._lastHitMarker != null)
                {
                    canvas._lastHitMarker = null;
                    canvas.MarkerHoverChanged?.Invoke(canvas, null);
                }
            }

            if (canvas._hoverX.HasValue)
            {
                var pixelsPerMs = 0.15 * canvas.ZoomLevel;
                var currentTimeMs = (long)canvas.CurrentTime.TotalMilliseconds;
                var effectiveViewTimeMs = (!canvas.IsSyncEnabled && canvas.ViewTime.HasValue)
                    ? (long)canvas.ViewTime.Value.TotalMilliseconds
                    : currentTimeMs;
                var hoverMs = effectiveViewTimeMs + (long)((canvas._hoverX.Value - canvas.KeyboardWidth) / pixelsPerMs);
                var totalMs = canvas.TotalDurationMs;
                if (totalMs > 0) hoverMs = Math.Clamp(hoverMs, 0, totalMs);
                else hoverMs = Math.Max(0, hoverMs);

                canvas.HoverPositionChanged?.Invoke(canvas, (canvas._hoverX.Value, hoverMs));
            }
            else if (canvas.HoverTime.HasValue)
            {
                canvas.UpdateHoverFromExternal();
            }
        }
    }

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
            canvas.InvalidateNoteBitmap();
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

    public int MinPitch => _minPitch;
    public int MaxPitch => _maxPitch;
    public int PitchCount => Math.Max(1, _maxPitch - _minPitch + 1);
    public bool HasNotes => _notes.Length > 0;
    public double TotalContentHeight => PitchCount * KeyHeight;
    public double MaxVerticalOffset => Math.Max(0.0, TotalContentHeight - ActualHeight);
    public bool IsOverflowing => TotalContentHeight > ActualHeight + 1.0 && ActualHeight > 0;

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

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(
            nameof(DisplayMode),
            typeof(PianoRollDisplayMode),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(PianoRollDisplayMode.Auto, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayModeChanged));

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
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
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnInstrumentIdChanged));

    private static void OnInstrumentIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
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
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnPitchRevisionChanged));

    private static void OnPitchRevisionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            canvas.UpdateNotePitches();
        }
    }

    public int PitchRevision
    {
        get => (int)GetValue(PitchRevisionProperty);
        set => SetValue(PitchRevisionProperty, value);
    }

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(PianoRollCanvas),
            new PropertyMetadata(false, (d, e) =>
            {
                if (d is PianoRollCanvas canvas && (bool)e.NewValue)
                {
                    if (canvas._lastHitMarker != null)
                    {
                        canvas._lastHitMarker = null;
                        canvas.MarkerHoverChanged?.Invoke(canvas, null);
                    }
                }
            }));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public static readonly DependencyProperty KeyChangeMarkersProperty =
        DependencyProperty.Register(
            nameof(KeyChangeMarkers),
            typeof(IEnumerable<AutoMidiPlayer.Data.Entities.KeyChangeMarker>),
            typeof(PianoRollCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnKeyChangeMarkersChanged));

    private static void OnKeyChangeMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
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
        }
    }

    private void OnMarkersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DisplayMode == PianoRollDisplayMode.Auto)
        {
            UpdateNotePitches();
        }
        InvalidateVisual();
    }

    public (KeyChangeMarker marker, double markerX)? HitTestMarker(Point point)
    {
        var markers = KeyChangeMarkers;
        if (markers is null || ActualWidth <= 0)
            return null;

        var pixelsPerMs = 0.15 * ZoomLevel;
        var currentTimeMs = (long)CurrentTime.TotalMilliseconds;
        var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
            ? (long)ViewTime.Value.TotalMilliseconds
            : currentTimeMs;

        foreach (var marker in markers)
        {
            var markerX = KeyboardWidth + (marker.TimeMs - effectiveViewTimeMs) * pixelsPerMs;
            if (markerX >= KeyboardWidth && markerX <= ActualWidth)
            {
                if (Math.Abs(point.X - markerX) <= 12)
                {
                    return (marker, markerX);
                }
            }
        }

        return null;
    }

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
            new PropertyMetadata(null, OnHoverTimePropertyChanged));

    private static void OnHoverTimePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PianoRollCanvas canvas)
        {
            canvas.UpdateHoverFromExternal();
        }
    }

    private void UpdateHoverFromExternal()
    {
        if (_hoverX.HasValue)
            return;

        if (HoverTime.HasValue)
        {
            var targetMs = (long)HoverTime.Value.TotalMilliseconds;
            var pixelsPerMs = 0.15 * ZoomLevel;
            var currentTimeMs = (long)CurrentTime.TotalMilliseconds;
            var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
                ? (long)ViewTime.Value.TotalMilliseconds
                : currentTimeMs;
            var targetX = KeyboardWidth + (targetMs - effectiveViewTimeMs) * pixelsPerMs;
            if (targetX >= KeyboardWidth && targetX <= ActualWidth)
            {
                HoverPositionChanged?.Invoke(this, (targetX, targetMs));
            }
            else
            {
                HoverPositionChanged?.Invoke(this, (null, 0));
            }
        }
        else
        {
            HoverPositionChanged?.Invoke(this, (null, 0));
        }
    }

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
            InvalidateNoteBitmap();
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
                InvalidateNoteBitmap();
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
                        noteList.Add(new RollNote(note.NoteNumber, note.NoteNumber, startMs, Math.Max(20, lengthMs), t));
                    }
                }
            }
            else
            {
                foreach (var note in file.Midi.GetNotes())
                {
                    var startMs = (long)note.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    var lengthMs = (long)note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    noteList.Add(new RollNote(note.NoteNumber, note.NoteNumber, startMs, Math.Max(20, lengthMs), (int)note.Channel));
                }
            }

            var notesArray = noteList.ToArray();
            Array.Sort(notesArray, static (a, b) => a.StartMs.CompareTo(b.StartMs));
            _notes = notesArray;

            long maxLen = 0;
            var uniqueTracks = new HashSet<int>();
            for (var i = 0; i < notesArray.Length; i++)
            {
                if (notesArray[i].LengthMs > maxLen)
                    maxLen = notesArray[i].LengthMs;
                uniqueTracks.Add(notesArray[i].TrackIndex);
            }
            _maxNoteLengthMs = maxLen;
            _trackCount = Math.Max(1, uniqueTracks.Count);

            UpdateNotePitchesInternal();
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
        InvalidateNoteBitmap();
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

    public void UpdateNotePitches()
    {
        UpdateNotePitchesInternal();
        _cachedLanes = null;
        _cachedKeyboard = null;
        InvalidateNoteBitmap();
        UpdateScrollMetrics();
        ScrollBoundsChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void UpdateNotePitchesInternal()
    {
        if (_notes.Length == 0)
        {
            _minPitch = 36;
            _maxPitch = 84;
            return;
        }

        var song = MidiFile?.Song;
        var mode = DisplayMode;
        var settings = AutoMidiPlayer.Data.Properties.Settings.Default;
        var instrumentId = !string.IsNullOrWhiteSpace(InstrumentId)
            ? InstrumentId
            : ResolveCurrentInstrumentId();
        var instrumentKeyCount = AutoMidiPlayer.WPF.Core.Keyboard.GetNotes(instrumentId).Count;
        var threshold = settings.AutoCorrectThreshold;
        var isAutoCorrect = threshold > 0
            && instrumentKeyCount <= threshold
            && (song?.Transpose == Transpose.Smart)
            && (song?.BaseKey is not null and not 0);

        for (var i = 0; i < _notes.Length; i++)
        {
            var note = _notes[i];
            var displayPitch = PianoRollNoteCalculator.CalculateDisplayPitch(
                note.RawNoteNumber, note.StartMs, mode, song, instrumentId, isAutoCorrect);

            _notes[i] = new RollNote(note.RawNoteNumber, displayPitch, note.StartMs, note.LengthMs, note.TrackIndex);
        }

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

    internal static string ResolveCurrentInstrumentId()
    {
        try
        {
            if (Application.Current?.MainWindow?.DataContext is AutoMidiPlayer.WPF.ViewModels.MainWindowViewModel main && main.InstrumentView != null)
            {
                var key = main.InstrumentView.SelectedInstrument.Key;
                if (!string.IsNullOrWhiteSpace(key))
                    return key;
            }
        }
        catch { }

        var list = AutoMidiPlayer.WPF.Core.Keyboard.InstrumentNames.ToList();
        return list.Count > 0 ? list[0].Key : string.Empty;
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
                var laneBrush = isBlack ? _blackLaneBrush : _whiteLaneBrush;

                dc.DrawRectangle(laneBrush, null, new Rect(KeyboardWidth, y, laneWidth, pitchHeight));
                dc.DrawLine(_gridLinePen, new Point(KeyboardWidth, y), new Point(width, y));
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
                var label = (p >= 0 && p < 128) ? MidiPitchLabels[p] : $"{NoteNames[pitchClass]}{(p / 12) - 1}";

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
        EnsureThemeResources();
        _lastRenderTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= KeyboardWidth + 10 || height <= 20)
            return;

        var pitchCount = _maxPitch - _minPitch + 1;
        var pitchHeight = Math.Clamp(KeyHeight, 6.0, 50.0);
        var totalHeight = pitchCount * pitchHeight;
        var maxScroll = Math.Max(0.0, totalHeight - height);
        var verticalOffset = Math.Clamp(VerticalOffset, 0.0, maxScroll);

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
        _lanesTransform.Y = -verticalOffset;
        dc.PushTransform(_lanesTransform);
        dc.DrawDrawing(_cachedLanes);
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
                dc.DrawLine(_gridLinePen, new Point(lineX, 0), new Point(lineX, height));
            }
        }

        // Reset active keys
        Array.Clear(_activeKeys, 0, _activeKeys.Length);

        // 3. Direct hardware-accelerated zero-allocation note rendering and active-key detection
        if (_notes.Length > 0)
        {
            var visibleEndMs = effectiveViewTimeMs + (long)(rollWidth / pixelsPerMs) + 500;
            var startIndex = FindFirstPossibleNoteIndex(effectiveViewTimeMs);
            var activeStartIndex = FindFirstPossibleNoteIndex(currentTimeMs);

            // Detect active keys at playhead
            for (var i = activeStartIndex; i < _notes.Length; i++)
            {
                var note = _notes[i];
                if (note.StartMs > currentTimeMs)
                    break;

                if (currentTimeMs <= note.StartMs + note.LengthMs)
                {
                    if (!IsTrackDisabled(note.TrackIndex) && (uint)note.NoteNumber < (uint)_activeKeys.Length)
                        _activeKeys[note.NoteNumber] = true;
                }
            }

            var radius = Math.Min(6.0, Math.Max(2.5, pitchHeight * 0.45));
            Array.Fill(_lastPitchStart, -1.0);
            Array.Fill(_lastPitchEnd, -1.0);

            if (_disabledTrackIndices.Count > 0)
            {
                // Pass 1: Disabled notes first (at the back with lower opacity)
                for (var i = startIndex; i < _notes.Length; i++)
                {
                    var note = _notes[i];
                    if (note.StartMs > visibleEndMs)
                        break;

                    if (!IsTrackDisabled(note.TrackIndex))
                        continue;

                    var noteEndMs = note.StartMs + note.LengthMs;
                    if (noteEndMs <= effectiveViewTimeMs)
                        continue;

                    var noteX = KeyboardWidth + (note.StartMs - effectiveViewTimeMs) * pixelsPerMs;
                    var noteW = Math.Max(3.0, note.LengthMs * pixelsPerMs);
                    if (noteX < KeyboardWidth)
                    {
                        noteW -= (KeyboardWidth - noteX);
                        noteX = KeyboardWidth;
                    }

                    if (noteW <= 0.5 || noteX > width)
                        continue;

                    if (noteX + noteW > width)
                    {
                        noteW = width - noteX;
                        if (noteW <= 0.5) continue;
                    }

                    var noteY = (_maxPitch - note.NoteNumber) * pitchHeight - verticalOffset;
                    var noteH = Math.Max(2, pitchHeight - 1);
                    if (noteY + noteH < 0 || noteY > height)
                        continue;

                    if (noteY < 0)
                    {
                        noteH += noteY;
                        noteY = 0;
                        if (noteH <= 0.5) continue;
                    }

                    if (noteY + noteH > height)
                    {
                        noteH = height - noteY;
                        if (noteH <= 0.5) continue;
                    }

                    var trackIdx = Math.Abs(note.TrackIndex) % TrackBrushes.Length;
                    var pitch = Math.Clamp(note.NoteNumber, 0, 127);
                    var trackerKey = (trackIdx << 7) | pitch;

                    var lastStart = _lastPitchStart[trackerKey];
                    var lastEnd = _lastPitchEnd[trackerKey];
                    if (Math.Abs(noteX - lastStart) < 0.75 && (noteX + noteW) <= lastEnd + 0.75)
                        continue;

                    _lastPitchStart[trackerKey] = noteX;
                    _lastPitchEnd[trackerKey] = noteX + noteW;

                    _trackRects[trackIdx].Add(new Rect(noteX, noteY + 0.5, noteW, noteH));
                }

                for (var t = 0; t < TrackBrushes.Length; t++)
                {
                    var rects = _trackRects[t];
                    var count = rects.Count;
                    if (count == 0) continue;

                    var brush = (_trackCount == 1) ? _disabledActiveKeyBrush : DisabledTrackBrushes[t];
                    for (var r = 0; r < count; r++)
                    {
                        var rect = rects[r];
                        var cornerR = Math.Min(radius, Math.Min(rect.Width * 0.45, rect.Height * 0.45));
                        dc.DrawRoundedRectangle(brush, null, rect, cornerR, cornerR);
                    }
                    rects.Clear();
                }

                // Reset coalescing tracker for Pass 2 (enabled notes)
                Array.Fill(_lastPitchStart, -1.0);
                Array.Fill(_lastPitchEnd, -1.0);

                // Pass 2: Enabled notes on top
                for (var i = startIndex; i < _notes.Length; i++)
                {
                    var note = _notes[i];
                    if (note.StartMs > visibleEndMs)
                        break;

                    if (IsTrackDisabled(note.TrackIndex))
                        continue;

                    var noteEndMs = note.StartMs + note.LengthMs;
                    if (noteEndMs <= effectiveViewTimeMs)
                        continue;

                    var noteX = KeyboardWidth + (note.StartMs - effectiveViewTimeMs) * pixelsPerMs;
                    var noteW = Math.Max(3.0, note.LengthMs * pixelsPerMs);
                    if (noteX < KeyboardWidth)
                    {
                        noteW -= (KeyboardWidth - noteX);
                        noteX = KeyboardWidth;
                    }

                    if (noteW <= 0.5 || noteX > width)
                        continue;

                    if (noteX + noteW > width)
                    {
                        noteW = width - noteX;
                        if (noteW <= 0.5) continue;
                    }

                    var noteY = (_maxPitch - note.NoteNumber) * pitchHeight - verticalOffset;
                    var noteH = Math.Max(2, pitchHeight - 1);
                    if (noteY + noteH < 0 || noteY > height)
                        continue;

                    if (noteY < 0)
                    {
                        noteH += noteY;
                        noteY = 0;
                        if (noteH <= 0.5) continue;
                    }

                    if (noteY + noteH > height)
                    {
                        noteH = height - noteY;
                        if (noteH <= 0.5) continue;
                    }

                    var trackIdx = Math.Abs(note.TrackIndex) % TrackBrushes.Length;
                    var pitch = Math.Clamp(note.NoteNumber, 0, 127);
                    var trackerKey = (trackIdx << 7) | pitch;

                    var lastStart = _lastPitchStart[trackerKey];
                    var lastEnd = _lastPitchEnd[trackerKey];
                    if (Math.Abs(noteX - lastStart) < 0.75 && (noteX + noteW) <= lastEnd + 0.75)
                        continue;

                    _lastPitchStart[trackerKey] = noteX;
                    _lastPitchEnd[trackerKey] = noteX + noteW;

                    _trackRects[trackIdx].Add(new Rect(noteX, noteY + 0.5, noteW, noteH));
                }

                for (var t = 0; t < TrackBrushes.Length; t++)
                {
                    var rects = _trackRects[t];
                    var count = rects.Count;
                    if (count == 0) continue;

                    var brush = (_trackCount == 1) ? _activeKeyBrush : TrackBrushes[t];
                    for (var r = 0; r < count; r++)
                    {
                        var rect = rects[r];
                        var cornerR = Math.Min(radius, Math.Min(rect.Width * 0.45, rect.Height * 0.45));
                        dc.DrawRoundedRectangle(brush, null, rect, cornerR, cornerR);
                    }
                    rects.Clear();
                }
            }
            else
            {
                // Fast path when all tracks are active
                for (var i = startIndex; i < _notes.Length; i++)
                {
                    var note = _notes[i];
                    if (note.StartMs > visibleEndMs)
                        break;

                    var noteEndMs = note.StartMs + note.LengthMs;
                    if (noteEndMs <= effectiveViewTimeMs)
                        continue;

                    var noteX = KeyboardWidth + (note.StartMs - effectiveViewTimeMs) * pixelsPerMs;
                    var noteW = Math.Max(3.0, note.LengthMs * pixelsPerMs);
                    if (noteX < KeyboardWidth)
                    {
                        noteW -= (KeyboardWidth - noteX);
                        noteX = KeyboardWidth;
                    }

                    if (noteW <= 0.5 || noteX > width)
                        continue;

                    if (noteX + noteW > width)
                    {
                        noteW = width - noteX;
                        if (noteW <= 0.5) continue;
                    }

                    var noteY = (_maxPitch - note.NoteNumber) * pitchHeight - verticalOffset;
                    var noteH = Math.Max(2, pitchHeight - 1);
                    if (noteY + noteH < 0 || noteY > height)
                        continue;

                    if (noteY < 0)
                    {
                        noteH += noteY;
                        noteY = 0;
                        if (noteH <= 0.5) continue;
                    }

                    if (noteY + noteH > height)
                    {
                        noteH = height - noteY;
                        if (noteH <= 0.5) continue;
                    }

                    var trackIdx = Math.Abs(note.TrackIndex) % TrackBrushes.Length;
                    var pitch = Math.Clamp(note.NoteNumber, 0, 127);
                    var trackerKey = (trackIdx << 7) | pitch;

                    var lastStart = _lastPitchStart[trackerKey];
                    var lastEnd = _lastPitchEnd[trackerKey];
                    if (Math.Abs(noteX - lastStart) < 0.75 && (noteX + noteW) <= lastEnd + 0.75)
                        continue;

                    _lastPitchStart[trackerKey] = noteX;
                    _lastPitchEnd[trackerKey] = noteX + noteW;

                    _trackRects[trackIdx].Add(new Rect(noteX, noteY + 0.5, noteW, noteH));
                }

                for (var t = 0; t < TrackBrushes.Length; t++)
                {
                    var rects = _trackRects[t];
                    var count = rects.Count;
                    if (count == 0) continue;

                    var brush = (_trackCount == 1) ? _activeKeyBrush : TrackBrushes[t];
                    for (var r = 0; r < count; r++)
                    {
                        var rect = rects[r];
                        var cornerR = Math.Min(radius, Math.Min(rect.Width * 0.45, rect.Height * 0.45));
                        dc.DrawRoundedRectangle(brush, null, rect, cornerR, cornerR);
                    }
                    rects.Clear();
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

        // 5. Draw cached stationary keyboard
        if (_cachedKeyboard == null || Math.Abs(_lastKeyboardPitchHeight - pitchHeight) > 0.001 || _lastKeyboardMinPitch != _minPitch || _lastKeyboardMaxPitch != _maxPitch || Math.Abs(_lastKeyboardWidth - KeyboardWidth) > 0.1)
        {
            _cachedKeyboard = BuildStaticKeyboard(pitchHeight, dpi);
            _lastKeyboardPitchHeight = pitchHeight;
            _lastKeyboardMinPitch = _minPitch;
            _lastKeyboardMaxPitch = _maxPitch;
            _lastKeyboardWidth = KeyboardWidth;
        }

        _keyboardTransform.Y = -verticalOffset;
        dc.PushTransform(_keyboardTransform);
        dc.DrawDrawing(_cachedKeyboard);
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
            var label = (p >= 0 && p < 128) ? MidiPitchLabels[p] : $"{NoteNames[pitchClass]}{(p / 12) - 1}";

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
                dc.DrawLine(_playheadLinePen, new Point(playheadX, 0), new Point(playheadX, height));
                _playheadTransform.X = playheadX;
                _playheadTransform.Y = 0;
                dc.PushTransform(_playheadTransform);
                dc.DrawGeometry(_playheadMarkerBrush, null, PlayheadMarkerGeometry);
                dc.Pop();
            }
        }
    }

    private void InvalidateNoteBitmap()
    {
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
        MarkerHoverChanged?.Invoke(this, null);
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
            HoverPositionChanged?.Invoke(this, (pos.X, clickedMs));

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        e.Handled = true;
        var pos = e.GetPosition(this);

        if (_isDragging && IsMouseCaptured)
        {
            MarkerHoverChanged?.Invoke(this, null);
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
                    _dragAnchorMouseX = pos.X;
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
            var currentEffectiveMs = (!IsSyncEnabled && ViewTime.HasValue) ? (long)ViewTime.Value.TotalMilliseconds : (long)CurrentTime.TotalMilliseconds;
            var currentHoverMs = currentEffectiveMs + (long)((_hoverX.Value - KeyboardWidth) / pixelsPerMs);
            HoverPositionChanged?.Invoke(this, (_hoverX.Value, currentHoverMs));
            InvalidateVisual();
            return;
        }

        if (pos.X >= KeyboardWidth && pos.X <= ActualWidth)
        {
            _hoverX = pos.X;
            if (Cursor != Cursors.Hand) Cursor = Cursors.Hand;

            var hitMarker = HitTestMarker(pos);
            if (hitMarker?.marker != _lastHitMarker)
            {
                _lastHitMarker = hitMarker?.marker;
                if (hitMarker.HasValue)
                {
                    MarkerHoverChanged?.Invoke(this, (hitMarker.Value.marker, hitMarker.Value.markerX, pos));
                }
                else
                {
                    MarkerHoverChanged?.Invoke(this, null);
                }
            }

            var pixelsPerMs = 0.15 * ZoomLevel;
            var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
                ? (long)ViewTime.Value.TotalMilliseconds
                : (long)CurrentTime.TotalMilliseconds;
            var hoverMs = effectiveViewTimeMs + (long)((pos.X - KeyboardWidth) / pixelsPerMs);
            var totalMs = TotalDurationMs;
            if (totalMs > 0) hoverMs = Math.Clamp(hoverMs, 0, totalMs);
            else hoverMs = Math.Max(0, hoverMs);

            if (!_lastReportedHoverMs.HasValue || Math.Abs(hoverMs - _lastReportedHoverMs.Value) >= 10)
            {
                _lastReportedHoverMs = hoverMs;
                HoverTimeChanged?.Invoke(this, TimeSpan.FromMilliseconds(hoverMs));
            }

            HoverPositionChanged?.Invoke(this, (pos.X, hoverMs));
        }
        else
        {
            if (_lastHitMarker != null)
            {
                _lastHitMarker = null;
                MarkerHoverChanged?.Invoke(this, null);
            }
            if (_hoverX.HasValue)
            {
                _hoverX = null;
                _lastReportedHoverMs = null;
                if (Cursor != Cursors.Arrow) Cursor = Cursors.Arrow;
                HoverTimeChanged?.Invoke(this, null);
                HoverPositionChanged?.Invoke(this, (null, 0));
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

            if (!_hasDragged || IsSyncEnabled)
            {
                var pos = e.GetPosition(this);
                if (pos.X >= KeyboardWidth && pos.X <= ActualWidth)
                {
                    var pixelsPerMs = 0.15 * ZoomLevel;
                    var effectiveViewTimeMs = (!IsSyncEnabled && ViewTime.HasValue)
                        ? (long)ViewTime.Value.TotalMilliseconds
                        : (long)CurrentTime.TotalMilliseconds;
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
                _lastReportedHoverMs = null;
                if (Cursor != Cursors.Arrow) Cursor = Cursors.Arrow;
                HoverTimeChanged?.Invoke(this, null);
                HoverPositionChanged?.Invoke(this, (null, 0));
            }
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        e.Handled = true;
        if (_lastHitMarker != null)
        {
            _lastHitMarker = null;
            MarkerHoverChanged?.Invoke(this, null);
        }
        if (!_isDragging)
        {
            if (_hoverX.HasValue)
            {
                _hoverX = null;
                _lastReportedHoverMs = null;
                if (Cursor != Cursors.Arrow) Cursor = Cursors.Arrow;
                HoverTimeChanged?.Invoke(this, null);
                HoverPositionChanged?.Invoke(this, (null, 0));
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
