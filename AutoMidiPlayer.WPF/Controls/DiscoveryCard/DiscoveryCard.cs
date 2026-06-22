using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AutoMidiPlayer.WPF.Controls;

public partial class DiscoveryCard : UserControl
{
    private static readonly Random s_rng = new();

    public DiscoveryCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is Services.MidiShow.MidiShowItem { IsLoading: true })
        {
            RandomizeSkeletonWidths();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldNpc)
            oldNpc.PropertyChanged -= OnItemPropertyChanged;

        if (e.NewValue is INotifyPropertyChanged newNpc)
            newNpc.PropertyChanged += OnItemPropertyChanged;

        if (DataContext is Services.MidiShow.MidiShowItem { IsLoading: true })
        {
            RandomizeSkeletonWidths();
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Services.MidiShow.MidiShowItem.IsLoading))
        {
            if (DataContext is Services.MidiShow.MidiShowItem { IsLoading: true })
            {
                RandomizeSkeletonWidths();
            }
        }
    }


    private void RandomizeSkeletonWidths()
    {
        if (FindName("SkeletonTitle") is Border title)
            title.Width = s_rng.Next(140, 260);
        if (FindName("SkeletonDesc") is Border desc)
            desc.Width = s_rng.Next(160, 320);
        if (FindName("SkeletonChip") is Border chip)
            chip.Width = s_rng.Next(60, 110);
        if (FindName("SkeletonTags") is Border tags)
            tags.Width = s_rng.Next(80, 180);
    }


    #region Routed Events

    public static readonly RoutedEvent DetailsClickEvent = EventManager.RegisterRoutedEvent(
        nameof(DetailsClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DiscoveryCard));

    public event RoutedEventHandler DetailsClick
    {
        add => AddHandler(DetailsClickEvent, value);
        remove => RemoveHandler(DetailsClickEvent, value);
    }

    public static readonly RoutedEvent PreviewClickEvent = EventManager.RegisterRoutedEvent(
        nameof(PreviewClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DiscoveryCard));

    public event RoutedEventHandler PreviewClick
    {
        add => AddHandler(PreviewClickEvent, value);
        remove => RemoveHandler(PreviewClickEvent, value);
    }

    public static readonly RoutedEvent AddToSongsClickEvent = EventManager.RegisterRoutedEvent(
        nameof(AddToSongsClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DiscoveryCard));

    public event RoutedEventHandler AddToSongsClick
    {
        add => AddHandler(AddToSongsClickEvent, value);
        remove => RemoveHandler(AddToSongsClickEvent, value);
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(DetailsClickEvent, this));
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(PreviewClickEvent, this));
    }

    private void AddToSongs_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(AddToSongsClickEvent, this));
    }

    #endregion
}
