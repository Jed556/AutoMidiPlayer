using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoMidiPlayer.Data.Midi;
using Stylet;

namespace AutoMidiPlayer.WPF.Controls;

/// <summary>
/// Reusable song list control for displaying MIDI files
/// </summary>
public partial class SongListControl : UserControl
{
    private readonly DispatcherTimer _scrollbarFadeTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private ScrollBar? _verticalScrollBar;

    #region Dependency Properties

    /// <summary>
    /// Items to display in the list
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SongListControl),
            new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Currently selected item
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SongListControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Currently opened/playing file (for highlighting)
    /// </summary>
    public static readonly DependencyProperty OpenedFileProperty =
        DependencyProperty.Register(nameof(OpenedFile), typeof(MidiFile), typeof(SongListControl),
            new PropertyMetadata(null));

    public MidiFile? OpenedFile
    {
        get => (MidiFile?)GetValue(OpenedFileProperty);
        set => SetValue(OpenedFileProperty, value);
    }

    /// <summary>
    /// Whether playback is currently active
    /// </summary>
    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(SongListControl),
            new PropertyMetadata(false));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>
    /// Whether drag-drop reordering is allowed
    /// </summary>
    public static readonly DependencyProperty AllowReorderProperty =
        DependencyProperty.Register(nameof(AllowReorder), typeof(bool), typeof(SongListControl),
            new PropertyMetadata(false));

    public bool AllowReorder
    {
        get => (bool)GetValue(AllowReorderProperty);
        set => SetValue(AllowReorderProperty, value);
    }

    /// <summary>
    /// Collection of selected MidiFiles - owned by the control, automatically synced with ListView selection
    /// </summary>
    public BindableCollection<MidiFile> SelectedFiles { get; } = new();

    /// <summary>
    /// Whether multiple items are currently selected
    /// </summary>
    public static readonly DependencyProperty IsMultiSelectProperty =
        DependencyProperty.Register(nameof(IsMultiSelect), typeof(bool), typeof(SongListControl),
            new PropertyMetadata(false));

    public bool IsMultiSelect
    {
        get => (bool)GetValue(IsMultiSelectProperty);
        set => SetValue(IsMultiSelectProperty, value);
    }

    /// <summary>
    /// Context menu to show for items
    /// </summary>
    public static readonly DependencyProperty ItemContextMenuProperty =
        DependencyProperty.Register(nameof(ItemContextMenu), typeof(ContextMenu), typeof(SongListControl),
            new PropertyMetadata(null, OnItemContextMenuChanged));

    public ContextMenu? ItemContextMenu
    {
        get => (ContextMenu?)GetValue(ItemContextMenuProperty);
        set => SetValue(ItemContextMenuProperty, value);
    }

    private static void OnItemContextMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SongListControl control && e.NewValue is ContextMenu menu)
        {
            // Set the context menu on the ListView but ensure PlacementTarget points to the SongListControl
            control.TrackListView.ContextMenu = menu;
            // Set PlacementTarget to the SongListControl so bindings like PlacementTarget.IsMultiSelect work
            menu.PlacementTarget = control;
        }
    }

    #endregion

    #region Routed Events

    /// <summary>
    /// Raised when an item is double-clicked
    /// </summary>
    public static readonly RoutedEvent ItemDoubleClickEvent =
        EventManager.RegisterRoutedEvent(nameof(ItemDoubleClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(SongListControl));

    public event RoutedEventHandler ItemDoubleClick
    {
        add => AddHandler(ItemDoubleClickEvent, value);
        remove => RemoveHandler(ItemDoubleClickEvent, value);
    }

    /// <summary>
    /// Raised when the play/pause button is clicked
    /// </summary>
    public static readonly RoutedEvent PlayPauseClickEvent =
        EventManager.RegisterRoutedEvent(nameof(PlayPauseClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(SongListControl));

    public event RoutedEventHandler PlayPauseClick
    {
        add => AddHandler(PlayPauseClickEvent, value);
        remove => RemoveHandler(PlayPauseClickEvent, value);
    }

    /// <summary>
    /// Raised when the menu button is clicked
    /// </summary>
    public static readonly RoutedEvent MenuClickEvent =
        EventManager.RegisterRoutedEvent(nameof(MenuClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(SongListControl));

    public event RoutedEventHandler MenuClick
    {
        add => AddHandler(MenuClickEvent, value);
        remove => RemoveHandler(MenuClickEvent, value);
    }

    #endregion

    public SongListControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _scrollbarFadeTimer.Tick += (_, _) => FadeOutScrollBar();
    }

    /// <summary>
    /// Get the internal ListView for drag-drop setup
    /// </summary>
    public ListView ListView => TrackListView;

    /// <summary>
    /// Ensure PlacementTarget is correctly set when ContextMenu opens
    /// </summary>
    private void TrackListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (TrackListView.ContextMenu != null)
        {
            TrackListView.ContextMenu.PlacementTarget = this;
        }
    }

    private void TrackListView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Find the clicked ListViewItem by walking up the visual tree
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not ListViewItem)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is ListViewItem item && item.Content is MidiFile file)
        {
            SelectedItem = file;
            RaiseEvent(new SongListEventArgs(ItemDoubleClickEvent, this, file));
            e.Handled = true;
        }
    }

    private void PlayPauseButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag is MidiFile file)
        {
            SelectedItem = file;
            RaiseEvent(new SongListEventArgs(PlayPauseClickEvent, this, file));
            e.Handled = true;
        }
    }

    private void MenuButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag is MidiFile file)
        {
            // Select the item in ListView if not already in selection
            if (!TrackListView.SelectedItems.Contains(file))
            {
                TrackListView.SelectedItem = file;
            }

            SelectedItem = file;
            RaiseEvent(new SongListEventArgs(MenuClickEvent, this, file));

            // Open context menu if one is set
            if (TrackListView.ContextMenu != null)
            {
                TrackListView.ContextMenu.IsOpen = true;
            }
            e.Handled = true;
        }
    }

    private void TrackListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update IsMultiSelect based on selection count
        IsMultiSelect = TrackListView.SelectedItems.Count > 1;

        // Sync selected items to the SelectedFiles collection
        SelectedFiles.Clear();
        foreach (var item in TrackListView.SelectedItems)
        {
            if (item is MidiFile file)
                SelectedFiles.Add(file);
        }
    }

    private void TrackListView_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            return;

        EnsureScrollBarReference();
        if (_verticalScrollBar == null)
            return;

        if (e.ExtentHeight <= e.ViewportHeight)
        {
            _verticalScrollBar.BeginAnimation(OpacityProperty, null);
            _verticalScrollBar.Opacity = 0;
            return;
        }

        ShowScrollBarPill();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollBarReference();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _scrollbarFadeTimer.Stop();
    }

    private void EnsureScrollBarReference()
    {
        if (_verticalScrollBar != null)
            return;

        _verticalScrollBar = FindDescendant<ScrollBar>(TrackListView, bar => bar.Orientation == Orientation.Vertical);
    }

    private void ShowScrollBarPill()
    {
        if (_verticalScrollBar == null)
            return;

        var fadeIn = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        _verticalScrollBar.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);

        _scrollbarFadeTimer.Stop();
        _scrollbarFadeTimer.Start();
    }

    private void FadeOutScrollBar()
    {
        _scrollbarFadeTimer.Stop();

        if (_verticalScrollBar == null)
            return;

        if (_verticalScrollBar.IsMouseOver || _verticalScrollBar.IsMouseCaptureWithin)
            return;

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        _verticalScrollBar.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild && (predicate == null || predicate(typedChild)))
                return typedChild;

            var result = FindDescendant(child, predicate);
            if (result != null)
                return result;
        }

        return null;
    }
}

/// <summary>
/// Event args that includes the clicked MidiFile
/// </summary>
public class SongListEventArgs : RoutedEventArgs
{
    public MidiFile File { get; }

    public SongListEventArgs(RoutedEvent routedEvent, object source, MidiFile file)
        : base(routedEvent, source)
    {
        File = file;
    }
}
