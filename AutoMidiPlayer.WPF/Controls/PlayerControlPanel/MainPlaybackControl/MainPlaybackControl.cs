using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoMidiPlayer.WPF.Controls.PlayerControlPanel;

public partial class MainPlaybackControl : UserControl
{
    private bool _isPedalPopupRequested;

    public MainPlaybackControl()
    {
        InitializeComponent();
    }

    private void PedalButton_MouseEnter(object sender, MouseEventArgs e)
    {
        _isPedalPopupRequested = true;
        if (!PedalPopup.IsOpen)
        {
            PedalPopup.IsOpen = true;
            PedalPopupContent.PlayEntranceAnimation();
        }
    }

    private async void PedalButton_MouseLeave(object sender, MouseEventArgs e)
    {
        _isPedalPopupRequested = false;
        await Task.Delay(100);
        if (!_isPedalPopupRequested)
        {
            PedalPopupContent.PlayExitAnimation(() => { PedalPopup.IsOpen = false; });
        }
    }
}
