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
}
