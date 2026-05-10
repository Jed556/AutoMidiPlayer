using System;
using System.Windows.Controls;
using AutoMidiPlayer.WPF.Controls;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Views;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
    }

    private void OnHotkeyChanged(object sender, HotkeyChangedEventArgs e)
    {
        if (DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.UpdateHotkey(e.Name, e.Key, e.Modifiers);
        }
    }

    private void OnHotkeyCleared(object sender, string name)
    {
        if (DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.ClearHotkey(name);
        }
    }

    private void OnHotkeyEditStarted(object sender, EventArgs e)
    {
        if (DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.SuspendHotkeys();
        }
    }

    private void OnHotkeyEditEnded(object sender, EventArgs e)
    {
        if (DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.ResumeHotkeys();
        }
    }

    public void ScrollToVersionSection()
    {
        if (VersionSection is null || RootScrollViewer is null) return;

        // Defer scroll to ensure layout is complete
        Dispatcher.InvokeAsync(
            () =>
            {
                // Get the position of Version section relative to the scroll viewer
                var transform = VersionSection.TranslatePoint(new System.Windows.Point(0, 0), RootScrollViewer);
                // Scroll to position it near the top (with some padding), not just barely in view
                RootScrollViewer.ScrollToVerticalOffset(transform.Y - 100);
            },
            System.Windows.Threading.DispatcherPriority.Render);
    }
}
