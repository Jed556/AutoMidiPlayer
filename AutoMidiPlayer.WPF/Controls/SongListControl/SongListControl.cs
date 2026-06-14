using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutoMidiPlayer.Data.Midi;
using Stylet;

namespace AutoMidiPlayer.WPF.Controls;

/// <summary>
/// Reusable song list control for displaying MIDI files
/// </summary>
public partial class SongListControl : UserControl
{
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
            // ContextMenu.PlacementTarget may be the inner ListView at runtime;
            // TrackListView.Tag stores SongListControl for bindings like PlacementTarget.Tag.IsMultiSelect.
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

    // Spacing applied only when the scrollbar is visible
    private const double ScrollbarLeftGap = 12;   // gap between content and scrollbar
    private const double ScrollbarRightGap = 10;  // gap between scrollbar and right edge

    private ScrollViewer? _scrollViewer;

    public SongListControl()
    {
        InitializeComponent();
        // Keep a stable reference to the owning control for ContextMenu bindings.
        TrackListView.Tag = this;

        TrackListView.Loaded += TrackListView_Loaded;
    }

    /// <summary>
    /// Get the internal ListView for drag-drop setup
    /// </summary>
    public ListView ListView => TrackListView;

    private void TrackListView_Loaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindScrollViewer(TrackListView);
        if (_scrollViewer != null)
        {
            // Override the global 12px right padding — we control this dynamically
            _scrollViewer.Padding = new Thickness(0);
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.SizeChanged += (_, _) => SyncScrollbarSpacing();
        }

        // Defer until after layout so ActualWidth/ViewportWidth are valid
        Dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(SyncScrollbarSpacing));
    }

    /// <summary>
    /// Walk the visual tree to find the ScrollViewer inside the ListView
    /// </summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
                return sv;

            var result = FindScrollViewer(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 || e.ViewportWidthChange != 0)
            SyncScrollbarSpacing();
    }

    /// <summary>
    /// Set ScrollViewer, ListView, and header right padding based on
    /// whether the list content overflows.
    ///   • Not scrollable → all three are 0, rows fill the full width.
    ///   • Scrollable     → SV padding reserves gap before the scrollbar,
    ///     LV padding reserves gap after, and header matches the total.
    /// </summary>
    private void SyncScrollbarSpacing()
    {
        if (_scrollViewer == null || _scrollViewer.ActualWidth < 1) return;

        bool isScrollable = _scrollViewer.ExtentHeight > _scrollViewer.ViewportHeight;

        double svRight = isScrollable ? ScrollbarLeftGap : 0;
        double lvRight = isScrollable ? ScrollbarRightGap : 0;

        bool paddingChanged = false;

        // ScrollViewer padding (gap between row content and scrollbar)
        var svPad = _scrollViewer.Padding;
        if (Math.Abs(svPad.Right - svRight) > 0.5)
        {
            _scrollViewer.Padding = new Thickness(svPad.Left, svPad.Top, svRight, svPad.Bottom);
            paddingChanged = true;
        }

        // ListView padding (gap between scrollbar and right edge)
        var lvPad = TrackListView.Padding;
        if (Math.Abs(lvPad.Right - lvRight) > 0.5)
        {
            TrackListView.Padding = new Thickness(lvPad.Left, lvPad.Top, lvRight, lvPad.Bottom);
            paddingChanged = true;
        }

        if (paddingChanged)
        {
            // Padding changes need a layout pass before ViewportWidth updates,
            // so defer the header sync.
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(SyncHeaderToRows));
        }
        else
        {
            SyncHeaderToRows();
        }
    }

    /// <summary>
    /// Forcibly sync the header grid's width to exactly match the 
    /// viewport width of the ListView's inner ScrollViewer.
    /// This mathematically guarantees perfect column alignment
    /// regardless of padding, margins, or scrollbar visibility.
    /// </summary>
    private void SyncHeaderToRows()
    {
        if (_scrollViewer == null || _scrollViewer.ActualWidth < 1) return;

        // By forcing the HeaderGrid to match the exact pixel width of the Viewport,
        // the '*' column (Title) will compute to the exact same width in both grids.
        if (double.IsNaN(HeaderGrid.Width) || Math.Abs(HeaderGrid.Width - _scrollViewer.ViewportWidth) > 0.5)
        {
            HeaderGrid.Width = _scrollViewer.ViewportWidth;
        }
    }

    /// <summary>
    /// Ensure PlacementTarget is correctly set when ContextMenu opens
    /// </summary>
    private void TrackListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (TrackListView.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.MousePoint;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
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

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is MidiFile file)
        {
            RaiseEvent(new SongListEventArgs(PlayPauseClickEvent, this, file));
            e.Handled = true;
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: MidiFile file } button)
        {
            RaiseEvent(new SongListEventArgs(MenuClickEvent, this, file));

            // Open context menu if one is set
            if (TrackListView.ContextMenu is ContextMenu menu)
            {
                var origin = button.TranslatePoint(new Point(0, button.ActualHeight), this);
                menu.PlacementTarget = this;
                menu.Placement = PlacementMode.Relative;
                menu.HorizontalOffset = origin.X;
                menu.VerticalOffset = origin.Y + 2;

                if (menu.IsOpen)
                {
                    menu.IsOpen = false;
                }

                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }

                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { menu.IsOpen = true; }));
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

}

/// <summary>
/// Event args that includes the clicked MidiFile
/// </summary>
public class SongListEventArgs(RoutedEvent routedEvent, object source, MidiFile file) : RoutedEventArgs(routedEvent, source)
{
    public MidiFile File { get; } = file;
}
