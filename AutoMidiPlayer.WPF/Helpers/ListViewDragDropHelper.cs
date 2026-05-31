using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AutoMidiPlayer.Data.Midi;
using Stylet;
using AutoMidiPlayer.WPF.Core;

namespace AutoMidiPlayer.WPF.Helpers;

/// <summary>
/// Helper class for ListView drag-drop reordering
/// </summary>
public class ListViewDragDropHelper
{
    private Point _startPoint;
    private ListViewItem? _draggedItem;
    private MidiFile? _draggedData;
    private DropIndicatorAdorner? _dropAdorner;
    private ListViewItem? _adornedItem;
    private int _dropIndex = -1;
    private readonly ListView _listView;
    private readonly Func<BindableCollection<MidiFile>> _getItemsSource;
    private readonly Action? _onReordered;

    // Ghost adorner
    private DragGhostAdorner? _ghostAdorner;

    public ListViewDragDropHelper(ListView listView, Func<BindableCollection<MidiFile>> getItemsSource, Action? onReordered = null)
    {
        _listView = listView;
        _getItemsSource = getItemsSource;
        _onReordered = onReordered;

        _listView.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        _listView.PreviewMouseMove += OnPreviewMouseMove;
        _listView.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        _listView.DragOver += OnDragOver;
        _listView.Drop += OnDrop;
        _listView.DragLeave += OnDragLeave;
        _listView.GiveFeedback += OnGiveFeedback;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_listView.AllowDrop) return;
        
        _startPoint = e.GetPosition(null);

        var item = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (item != null)
        {
            _draggedItem = item;
            _draggedData = item.DataContext as MidiFile;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_listView.AllowDrop || e.LeftButton != MouseButtonState.Pressed || _draggedItem == null || _draggedData == null)
            return;

        var position = e.GetPosition(null);
        var diff = _startPoint - position;

        // Check if drag threshold exceeded
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _draggedItem.Opacity = 0.5;

            // Create ghost adorner
            var layer = AdornerLayer.GetAdornerLayer(_listView);
            if (layer != null)
            {
                _ghostAdorner = new DragGhostAdorner(_listView, _draggedData);
                layer.Add(_ghostAdorner);
                UpdateGhostPosition(e.GetPosition(_listView));
            }

            var data = new DataObject("MidiFile", _draggedData);
            DragDrop.DoDragDrop(_draggedItem, data, DragDropEffects.Move);

            // Reset after drag
            if (_draggedItem != null)
                _draggedItem.Opacity = 1.0;
            _draggedItem = null;
            _draggedData = null;
            
            if (_ghostAdorner != null)
            {
                AdornerLayer.GetAdornerLayer(_listView)?.Remove(_ghostAdorner);
                _ghostAdorner = null;
            }
            
            RemoveDropIndicator();
        }
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedItem = null;
        _draggedData = null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("MidiFile") || !_listView.AllowDrop)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        var pos = e.GetPosition(_listView);
        UpdateDropIndicator(pos);
        UpdateGhostPosition(pos);
        e.Handled = true;
    }

    private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_ghostAdorner != null)
        {
            // Use Win32 API to get current cursor position since GiveFeedbackEventArgs lacks coordinates
            if (GetCursorPos(out var point))
            {
                var screenPoint = new Point(point.X, point.Y);
                var clientPoint = _listView.PointFromScreen(screenPoint);
                UpdateGhostPosition(clientPoint);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private void UpdateGhostPosition(Point pos)
    {
        if (_ghostAdorner != null)
        {
            _ghostAdorner.Offset = pos;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
        if (_ghostAdorner != null)
        {
            _ghostAdorner.Visibility = Visibility.Collapsed;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("MidiFile") || !_listView.AllowDrop)
            return;

        var droppedData = e.Data.GetData("MidiFile") as MidiFile;
        if (droppedData == null) return;

        var itemsSource = _getItemsSource();
        var oldIndex = itemsSource.IndexOf(droppedData);
        var newIndex = _dropIndex;

        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            RemoveDropIndicator();
            return;
        }

        // Adjust new index if moving down
        if (newIndex > oldIndex)
            newIndex--;

        // Clamp to valid range
        newIndex = Math.Max(0, Math.Min(newIndex, itemsSource.Count - 1));

        itemsSource.Move(oldIndex, newIndex);
        _onReordered?.Invoke();

        RemoveDropIndicator();
        e.Handled = true;
    }

    private void UpdateDropIndicator(Point position)
    {
        if (_ghostAdorner != null && _ghostAdorner.Visibility == Visibility.Collapsed)
        {
            _ghostAdorner.Visibility = Visibility.Visible;
        }

        var element = _listView.InputHitTest(position) as DependencyObject;
        var targetItem = FindAncestor<ListViewItem>(element);
        var itemsSource = _getItemsSource();

        if (targetItem != null)
        {
            var positionInItem = _listView.TranslatePoint(position, targetItem);
            var insertAbove = positionInItem.Y < targetItem.ActualHeight / 2;

            if (targetItem.DataContext is MidiFile targetFile)
            {
                _dropIndex = itemsSource.IndexOf(targetFile);
                if (!insertAbove) _dropIndex++;
            }

            if (_adornedItem != targetItem || _dropAdorner == null || _dropAdorner.InsertAbove != insertAbove)
            {
                RemoveDropIndicator();
                _adornedItem = targetItem;
                
                var layer = AdornerLayer.GetAdornerLayer(targetItem);
                if (layer != null)
                {
                    _dropAdorner = new DropIndicatorAdorner(targetItem, insertAbove);
                    layer.Add(_dropAdorner);
                }
            }
        }
        else
        {
            // Below all items or empty space
            _dropIndex = itemsSource.Count;
            
            if (_listView.Items.Count > 0)
            {
                var lastItem = _listView.ItemContainerGenerator.ContainerFromIndex(_listView.Items.Count - 1) as ListViewItem;
                if (lastItem != null)
                {
                    if (_adornedItem != lastItem || _dropAdorner == null || _dropAdorner.InsertAbove)
                    {
                        RemoveDropIndicator();
                        _adornedItem = lastItem;
                        
                        var layer = AdornerLayer.GetAdornerLayer(lastItem);
                        if (layer != null)
                        {
                            _dropAdorner = new DropIndicatorAdorner(lastItem, false);
                            layer.Add(_dropAdorner);
                        }
                    }
                }
            }
            else
            {
                RemoveDropIndicator();
            }
        }
    }

    private void RemoveDropIndicator()
    {
        if (_dropAdorner != null && _adornedItem != null)
        {
            var layer = AdornerLayer.GetAdornerLayer(_adornedItem);
            layer?.Remove(_dropAdorner);
            _dropAdorner = null;
            _adornedItem = null;
        }
        _dropIndex = -1;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T target)
                return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private class DropIndicatorAdorner : Adorner
    {
        private readonly Pen _pen;
        public bool InsertAbove { get; }

        public DropIndicatorAdorner(UIElement adornedElement, bool insertAbove) : base(adornedElement)
        {
            InsertAbove = insertAbove;
            IsHitTestVisible = false;
            
            var brush = new SolidColorBrush(AccentColorHelper.GetAccentColor());
            brush.Freeze();
            
            _pen = new Pen(brush, 3)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            _pen.Freeze();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var y = InsertAbove ? 0 : AdornedElement.RenderSize.Height;
            drawingContext.DrawLine(_pen, new Point(10, y), new Point(AdornedElement.RenderSize.Width - 10, y));
        }
    }

    private class DragGhostAdorner : Adorner
    {
        private Point _offset;
        private readonly VisualBrush _visualBrush;
        private readonly double _width;
        private readonly double _height;

        public DragGhostAdorner(UIElement adornedElement, MidiFile data) : base(adornedElement)
        {
            IsHitTestVisible = false;
            Opacity = 0.8;

            // Create a compact visual block (Art + Title + Artist)
            var grid = new Grid
            {
                Background = (Brush)Application.Current.FindResource("ControlFillColorDefaultBrush"),
                Width = 250,
                Margin = new Thickness(0),
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.FindResource("SolidBackgroundFillColorBaseBrush"),
                BorderBrush = (Brush)Application.Current.FindResource("ControlElevationBorderBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                Child = grid
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Album Art Placeholder or icon
            var artPlaceholder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(4),
                Background = (Brush)Application.Current.FindResource("ControlFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var artIcon = new TextBlock
            {
                Text = "\xE8D6", // MusicInfo icon
                FontFamily = (FontFamily)Application.Current.FindResource("MdlFontFamily"),
                FontSize = 16,
                Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            artPlaceholder.Child = artIcon;
            Grid.SetColumn(artPlaceholder, 0);
            grid.Children.Add(artPlaceholder);

            // Text Info
            var textStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(textStack, 1);

            var titleBlock = new TextBlock
            {
                Text = data.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = (Brush)Application.Current.FindResource("TextFillColorPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 20
            };
            
            var artistBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(data.Artist) ? "Unknown Artist" : data.Artist,
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 18
            };

            textStack.Children.Add(titleBlock);
            textStack.Children.Add(artistBlock);
            grid.Children.Add(textStack);

            // Force layout pass to render visual brush
            border.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            border.Arrange(new Rect(border.DesiredSize));

            _width = border.DesiredSize.Width;
            _height = border.DesiredSize.Height;
            _visualBrush = new VisualBrush(border) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
        }

        public Point Offset
        {
            get => _offset;
            set
            {
                _offset = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Position the ghost slightly offset from cursor so it doesn't block it completely
            var rect = new Rect(_offset.X + 15, _offset.Y + 15, _width, _height);
            drawingContext.DrawRectangle(_visualBrush, null, rect);
        }
    }
}
