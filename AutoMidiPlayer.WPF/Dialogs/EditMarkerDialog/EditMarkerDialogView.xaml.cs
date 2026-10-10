using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class EditMarkerDialogView : UserControl
{
    public MusicConstants.KeyOption? SelectedKeyOption
    {
        get => KeyDropdown.SelectedItem as MusicConstants.KeyOption;
        set => KeyDropdown.SelectedItem = value;
    }

    public string MarkerLabel
    {
        get => LabelTextBox.Text?.Trim() ?? string.Empty;
        set => LabelTextBox.Text = value;
    }

    public EditMarkerDialogView(KeyChangeMarker marker, IEnumerable<MusicConstants.KeyOption> keyOptions)
    {
        InitializeComponent();
        TimeTextBlock.Text = $"Timestamp: {marker.TimeString} ({marker.TimeMs} ms)";
        var optionsList = keyOptions.ToList();
        KeyDropdown.ItemsSource = optionsList;
        SelectedKeyOption = optionsList.FirstOrDefault(k => k.Value == marker.KeyOffset);
        LabelTextBox.Text = marker.Label ?? string.Empty;
    }
}
