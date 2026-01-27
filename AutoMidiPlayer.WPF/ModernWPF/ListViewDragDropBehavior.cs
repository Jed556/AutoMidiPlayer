using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoMidiPlayer.Data.Midi;
using ModernWpf;
using Stylet;

namespace AutoMidiPlayer.WPF.ModernWPF;

/// <summary>
/// Helper class for ListView drag-drop reordering
/// </summary>
public class ListViewDragDropHelper
{
    private Point _startPoint;
    private ListViewItem? _draggedItem;
    private MidiFile? _draggedData;
    private Line? _dropIndicator;
    private int _dropIndex = -1;
    private readonly ListView _listView;
    private readonly BindableCollection<MidiFile> _itemsSource;
    private readonly Action? _onReordered;

    public ListViewDragDropHelper(ListView listView, BindableCollection<MidiFile> itemsSource, Action? onReordered = null)
    {
        _listView = listView;
        _itemsSource = itemsSource;
        _onReordered = onReordered;

        _listView.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        _listView.PreviewMouseMove += OnPreviewMouseMove;
        _listView.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        _listView.DragOver += OnDragOver;
        _listView.Drop += OnDrop;
        _listView.DragLeave += OnDragLeave;
        _listView.AllowDrop = true;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null || _draggedData == null)
            return;

        var position = e.GetPosition(null);
        var diff = _startPoint - position;

        // Check if drag threshold exceeded
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _draggedItem.Opacity = 0.5;

            CreateDropIndicator();

            var data = new DataObject("MidiFile", _draggedData);
            DragDrop.DoDragDrop(_draggedItem, data, DragDropEffects.Move);

            // Reset after drag
            if (_draggedItem != null)
                _draggedItem.Opacity = 1.0;
            _draggedItem = null;
            _draggedData = null;
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
        if (!e.Data.GetDataPresent("MidiFile"))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        UpdateDropIndicator(e.GetPosition(_listView));
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("MidiFile"))
            return;

        var droppedData = e.Data.GetData("MidiFile") as MidiFile;
        if (droppedData == null) return;

        var oldIndex = _itemsSource.IndexOf(droppedData);
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
        newIndex = Math.Max(0, Math.Min(newIndex, _itemsSource.Count - 1));

        _itemsSource.Move(oldIndex, newIndex);
        _onReordered?.Invoke();

        RemoveDropIndicator();
        e.Handled = true;
    }

    private void CreateDropIndicator()
    {
        _dropIndicator = new Line
        {
            Stroke = new SolidColorBrush(ThemeManager.Current.ActualAccentColor),
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        // Add to the same parent as the ListView
        var parent = VisualTreeHelper.GetParent(_listView) as Panel;
        if (parent != null)
        {
            _dropIndicator.SetValue(Grid.RowProperty, _listView.GetValue(Grid.RowProperty));
            _dropIndicator.SetValue(Grid.ColumnProperty, _listView.GetValue(Grid.ColumnProperty));
            parent.Children.Add(_dropIndicator);
        }
    }

    private void UpdateDropIndicator(Point position)
    {
        if (_dropIndicator == null) return;

        _dropIndicator.Visibility = Visibility.Visible;

        // Find the item under the cursor
        var hitTestResult = VisualTreeHelper.HitTest(_listView, position);
        var targetItem = FindAncestor<ListViewItem>(hitTestResult?.VisualHit);

        if (targetItem != null)
        {
            var itemBounds = targetItem.TransformToAncestor(_listView).TransformBounds(
                new Rect(0, 0, targetItem.ActualWidth, targetItem.ActualHeight));

            var itemCenter = itemBounds.Y + itemBounds.Height / 2;
            var insertAbove = position.Y < itemCenter;

            if (targetItem.DataContext is MidiFile targetFile)
            {
                _dropIndex = _itemsSource.IndexOf(targetFile);
                if (!insertAbove) _dropIndex++;
            }

            // Position the indicator line
            var lineY = insertAbove ? itemBounds.Top : itemBounds.Bottom;

            _dropIndicator.X1 = 10;
            _dropIndicator.X2 = _listView.ActualWidth - 20;
            _dropIndicator.Y1 = lineY;
            _dropIndicator.Y2 = lineY;
        }
        else
        {
            // Below all items
            _dropIndex = _itemsSource.Count;
        }
    }

    private void RemoveDropIndicator()
    {
        if (_dropIndicator != null)
        {
            var parent = _dropIndicator.Parent as Panel;
            parent?.Children.Remove(_dropIndicator);
            _dropIndicator = null;
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
}
