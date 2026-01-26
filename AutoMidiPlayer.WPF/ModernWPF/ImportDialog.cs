using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.ViewModels;
using ModernWpf.Controls;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.ModernWPF;

public class ImportDialog : ContentDialog
{
    private readonly System.Windows.Controls.TextBox _titleBox;
    private readonly System.Windows.Controls.TextBox _authorBox;
    private readonly System.Windows.Controls.TextBox _albumBox;
    private readonly System.Windows.Controls.ComboBox _keyComboBox;
    private readonly System.Windows.Controls.ComboBox _transposeComboBox;
    private readonly DatePicker _dateAddedPicker;

    public string SongTitle => _titleBox.Text;
    public string SongAuthor => _authorBox.Text;
    public string SongAlbum => _albumBox.Text;
    public DateTime? SongDateAdded => _dateAddedPicker.SelectedDate;
    public int SongKey { get; private set; }
    public Transpose SongTranspose => SettingsPageViewModel.TransposeNames.Keys.ElementAt(_transposeComboBox.SelectedIndex);

    // Key offsets from -27 to +27 with note names
    private static readonly Dictionary<int, string> KeyOffsets = new()
    {
        [-27] = "A0",
        [-26] = "A♯0",
        [-25] = "B0",
        [-24] = "C1",
        [-23] = "C♯1",
        [-22] = "D1",
        [-21] = "D♯1",
        [-20] = "E1",
        [-19] = "F1",
        [-18] = "F♯1",
        [-17] = "G1",
        [-16] = "G♯1",
        [-15] = "A1",
        [-14] = "A♯1",
        [-13] = "B1",
        [-12] = "C2",
        [-11] = "C♯2",
        [-10] = "D2",
        [-9] = "D♯2",
        [-8] = "E2",
        [-7] = "F2",
        [-6] = "F♯2",
        [-5] = "G2",
        [-4] = "G♯2",
        [-3] = "A2",
        [-2] = "A♯2",
        [-1] = "B2",
        [0] = "C3 (Default)",
        [1] = "C♯3",
        [2] = "D3",
        [3] = "D♯3",
        [4] = "E3",
        [5] = "F3",
        [6] = "F♯3",
        [7] = "G3",
        [8] = "G♯3",
        [9] = "A3",
        [10] = "A♯3",
        [11] = "B3",
        [12] = "C4",
        [13] = "C♯4",
        [14] = "D4",
        [15] = "D♯4",
        [16] = "E4",
        [17] = "F4",
        [18] = "F♯4",
        [19] = "G4",
        [20] = "G♯4",
        [21] = "A4",
        [22] = "A♯4",
        [23] = "B4",
        [24] = "C5",
        [25] = "C♯5",
        [26] = "D5",
        [27] = "D♯5"
    };

    public ImportDialog(string defaultTitle, int defaultKey = 0, Transpose defaultTranspose = Transpose.Ignore, string? defaultAuthor = null, string? defaultAlbum = null, DateTime? defaultDateAdded = null)
    {
        Title = "Edit Song";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };

        // Title
        stackPanel.Children.Add(new TextBlock { Text = "Title", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _titleBox = new System.Windows.Controls.TextBox { Text = defaultTitle, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_titleBox);

        // Author
        stackPanel.Children.Add(new TextBlock { Text = "Author (optional)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _authorBox = new System.Windows.Controls.TextBox { Text = defaultAuthor ?? string.Empty, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_authorBox);

        // Album
        stackPanel.Children.Add(new TextBlock { Text = "Album (optional)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _albumBox = new System.Windows.Controls.TextBox { Text = defaultAlbum ?? string.Empty, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_albumBox);

        // Date Added
        stackPanel.Children.Add(new TextBlock { Text = "Date Added", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _dateAddedPicker = new DatePicker { SelectedDate = defaultDateAdded ?? DateTime.Now, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_dateAddedPicker);

        // Key Offset
        stackPanel.Children.Add(new TextBlock { Text = "Key Offset", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _keyComboBox = new System.Windows.Controls.ComboBox { Width = 200, Margin = new Thickness(0, 0, 0, 12) };

        int selectedKeyIndex = 0;
        int index = 0;
        foreach (var kvp in KeyOffsets.OrderBy(k => k.Key))
        {
            _keyComboBox.Items.Add(new ComboBoxItem { Content = $"{kvp.Key:+#;-#;0} ({kvp.Value})", Tag = kvp.Key });
            if (kvp.Key == defaultKey)
                selectedKeyIndex = index;
            index++;
        }

        _keyComboBox.SelectedIndex = selectedKeyIndex;
        _keyComboBox.SelectionChanged += (_, _) =>
        {
            if (_keyComboBox.SelectedItem is ComboBoxItem item && item.Tag is int key)
                SongKey = key;
        };
        SongKey = defaultKey;
        stackPanel.Children.Add(_keyComboBox);

        // Transpose
        stackPanel.Children.Add(new TextBlock { Text = "Transpose", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _transposeComboBox = new System.Windows.Controls.ComboBox { Width = 200 };

        foreach (var kvp in SettingsPageViewModel.TransposeNames)
        {
            var item = new ComboBoxItem
            {
                Content = kvp.Value,
                ToolTip = SettingsPageViewModel.TransposeTooltips[kvp.Key]
            };
            _transposeComboBox.Items.Add(item);
        }

        // Select the default transpose
        _transposeComboBox.SelectedIndex = SettingsPageViewModel.TransposeNames.Keys.ToList().IndexOf(defaultTranspose);
        if (_transposeComboBox.SelectedIndex < 0) _transposeComboBox.SelectedIndex = 0;
        stackPanel.Children.Add(_transposeComboBox);

        Content = stackPanel;
    }
}
