using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.Data.Entities;
using ModernWpf.Controls;

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
    public Transpose SongTranspose => MusicConstants.TransposeNames.Keys.ElementAt(_transposeComboBox.SelectedIndex);

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
        foreach (var kvp in MusicConstants.KeyOffsets.OrderBy(k => k.Key))
        {
            _keyComboBox.Items.Add(new ComboBoxItem { Content = MusicConstants.FormatKeyDisplay(kvp.Key), Tag = kvp.Key });
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

        foreach (var kvp in MusicConstants.TransposeNames)
        {
            var item = new ComboBoxItem
            {
                Content = kvp.Value,
                ToolTip = MusicConstants.TransposeTooltips[kvp.Key]
            };
            _transposeComboBox.Items.Add(item);
        }

        // Select the default transpose
        _transposeComboBox.SelectedIndex = MusicConstants.TransposeNames.Keys.ToList().IndexOf(defaultTranspose);
        if (_transposeComboBox.SelectedIndex < 0) _transposeComboBox.SelectedIndex = 0;
        stackPanel.Children.Add(_transposeComboBox);

        Content = stackPanel;
    }
}
