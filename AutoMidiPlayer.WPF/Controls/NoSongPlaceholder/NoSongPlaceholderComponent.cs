using AutoMidiPlayer.WPF.ViewModels;
using Stylet;

namespace AutoMidiPlayer.WPF.Controls.NoSongPlaceholder;

public class NoSongPlaceholderComponent : Screen
{
    private readonly MainWindowViewModel _main;

    public NoSongPlaceholderComponent(MainWindowViewModel main)
    {
        _main = main;
    }

    public QueueViewModel Queue => _main.QueueView;

    public PlaceholderDisplayMode DisplayMode { get; set; } = PlaceholderDisplayMode.Full;

    public Wpf.Ui.Controls.SymbolRegular Icon { get; set; } = Wpf.Ui.Controls.SymbolRegular.AppsListDetail24;

    public void NavigateToSongs()
    {
        _main.NavigateToSongs();
    }

    public void NavigateToQueue()
    {
        _main.NavigateToQueue();
    }
}

public enum PlaceholderDisplayMode
{
    TextOnly,
    TextAndIcon,
    Full
}
