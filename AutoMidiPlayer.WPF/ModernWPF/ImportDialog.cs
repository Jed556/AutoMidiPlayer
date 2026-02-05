using System;
using System.Linq;
using System.Windows;
using AutoMidiPlayer.Data.Entities;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.ModernWPF;

public class ImportDialog : ContentDialog
{
    private readonly Wpf.Ui.Controls.TextBox _titleBox;
    private readonly Wpf.Ui.Controls.TextBox _authorBox;
    private readonly Wpf.Ui.Controls.TextBox _albumBox;
    private readonly System.Windows.Controls.ComboBox _keyComboBox;
    private readonly System.Windows.Controls.ComboBox _transposeComboBox;
    private readonly System.Windows.Controls.DatePicker _dateAddedPicker;
    private readonly Wpf.Ui.Controls.TextBox _bpmBox;
    private readonly System.Windows.Controls.CheckBox _useCustomBpmCheckBox;
    private readonly System.Windows.Controls.CheckBox _mergeNotesCheckBox;
    private readonly System.Windows.Controls.CheckBox _useCustomMergeCheckBox;
    private readonly Wpf.Ui.Controls.TextBox _mergeMillisecondsBox;
    private readonly System.Windows.Controls.CheckBox _holdNotesCheckBox;
    private readonly System.Windows.Controls.CheckBox _useCustomHoldCheckBox;
    private readonly double _nativeBpm;

    public string SongTitle => _titleBox.Text;
    public string SongAuthor => _authorBox.Text;
    public string SongAlbum => _albumBox.Text;
    public DateTime? SongDateAdded => _dateAddedPicker.SelectedDate;
    public int SongKey { get; private set; }
    public Transpose SongTranspose => MusicConstants.TransposeNames.Keys.ElementAt(_transposeComboBox.SelectedIndex);

    /// <summary>
    /// Gets the custom BPM value if enabled, otherwise null (use native MIDI BPM).
    /// </summary>
    public double? SongBpm
    {
        get
        {
            if (_useCustomBpmCheckBox.IsChecked != true) return null;
            if (double.TryParse(_bpmBox.Text, out var bpm) && bpm > 0 && bpm <= 999)
                return bpm;
            return null;
        }
    }

    /// <summary>
    /// Gets the per-song merge notes setting.
    /// </summary>
    public bool SongMergeNotes => _mergeNotesCheckBox.IsChecked ?? false;

    /// <summary>
    /// Gets the per-song merge milliseconds setting.
    /// </summary>
    public uint SongMergeMilliseconds
    {
        get
        {
            if (uint.TryParse(_mergeMillisecondsBox.Text, out var ms) && ms > 0 && ms <= 1000)
                return ms;
            return 100; // Default
        }
    }

    /// <summary>
    /// Gets the per-song hold notes setting.
    /// </summary>
    public bool SongHoldNotes => _holdNotesCheckBox.IsChecked ?? false;

    public ImportDialog(string defaultTitle, int defaultKey = 0, Transpose defaultTranspose = Transpose.Ignore, string? defaultAuthor = null, string? defaultAlbum = null, DateTime? defaultDateAdded = null, double nativeBpm = 120, double? customBpm = null, bool? mergeNotes = null, uint? mergeMilliseconds = null, bool? holdNotes = null)
    {
        Title = "Edit Song";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 0, 0) };

        // Title
        stackPanel.Children.Add(new TextBlock { Text = "Title", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _titleBox = new Wpf.Ui.Controls.TextBox { Text = defaultTitle, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_titleBox);

        // Author
        stackPanel.Children.Add(new TextBlock { Text = "Author (optional)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _authorBox = new Wpf.Ui.Controls.TextBox { Text = defaultAuthor ?? string.Empty, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_authorBox);

        // Album
        stackPanel.Children.Add(new TextBlock { Text = "Album (optional)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _albumBox = new Wpf.Ui.Controls.TextBox { Text = defaultAlbum ?? string.Empty, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_albumBox);

        // Date Added
        stackPanel.Children.Add(new TextBlock { Text = "Date Added", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _dateAddedPicker = new System.Windows.Controls.DatePicker { SelectedDate = defaultDateAdded ?? DateTime.Now, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_dateAddedPicker);

        // Key Offset
        stackPanel.Children.Add(new TextBlock { Text = "Key Offset", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _keyComboBox = new System.Windows.Controls.ComboBox { Width = 200, Margin = new Thickness(0, 0, 0, 12) };

        int selectedKeyIndex = 0;
        int index = 0;
        foreach (var kvp in MusicConstants.KeyOffsets.OrderBy(k => k.Key))
        {
            _keyComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = MusicConstants.FormatKeyDisplay(kvp.Key), Tag = kvp.Key });
            if (kvp.Key == defaultKey)
                selectedKeyIndex = index;
            index++;
        }

        _keyComboBox.SelectedIndex = selectedKeyIndex;
        _keyComboBox.SelectionChanged += (_, _) =>
        {
            if (_keyComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is int key)
                SongKey = key;
        };
        SongKey = defaultKey;
        stackPanel.Children.Add(_keyComboBox);

        // Transpose
        stackPanel.Children.Add(new TextBlock { Text = "Transpose", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _transposeComboBox = new System.Windows.Controls.ComboBox { Width = 200 };

        foreach (var kvp in MusicConstants.TransposeNames)
        {
            var item = new System.Windows.Controls.ComboBoxItem
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

        // BPM Section
        _nativeBpm = nativeBpm;
        var bpmPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        // Native BPM display
        var nativeBpmText = new TextBlock
        {
            Text = $"Native BPM: {nativeBpm:F1}",
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136))
        };
        bpmPanel.Children.Add(nativeBpmText);

        // Custom BPM checkbox and input
        var bpmInputPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        _useCustomBpmCheckBox = new System.Windows.Controls.CheckBox
        {
            Content = "Custom BPM:",
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = customBpm.HasValue,
            Margin = new Thickness(0, 0, 8, 0)
        };
        bpmInputPanel.Children.Add(_useCustomBpmCheckBox);

        _bpmBox = new Wpf.Ui.Controls.TextBox
        {
            Text = customBpm.HasValue ? customBpm.Value.ToString("F1") : nativeBpm.ToString("F1"),
            Width = 80,
            IsEnabled = customBpm.HasValue
        };
        bpmInputPanel.Children.Add(_bpmBox);

        _useCustomBpmCheckBox.Checked += (_, _) => _bpmBox.IsEnabled = true;
        _useCustomBpmCheckBox.Unchecked += (_, _) =>
        {
            _bpmBox.IsEnabled = false;
            _bpmBox.Text = _nativeBpm.ToString("F1");
        };

        bpmPanel.Children.Add(bpmInputPanel);
        stackPanel.Children.Add(bpmPanel);

        // Merge Notes Section
        var mergePanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        stackPanel.Children.Add(new TextBlock { Text = "Merge Notes", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        var mergeInputPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _mergeNotesCheckBox = new System.Windows.Controls.CheckBox
        {
            Content = "Enable Merge Notes",
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = mergeNotes ?? false,
            Margin = new Thickness(0, 0, 12, 0)
        };
        mergeInputPanel.Children.Add(_mergeNotesCheckBox);

        mergeInputPanel.Children.Add(new TextBlock { Text = "Tolerance (ms):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _mergeMillisecondsBox = new Wpf.Ui.Controls.TextBox
        {
            Text = (mergeMilliseconds ?? 100).ToString(),
            Width = 60,
            IsEnabled = mergeNotes ?? false
        };
        mergeInputPanel.Children.Add(_mergeMillisecondsBox);

        _mergeNotesCheckBox.Checked += (_, _) => _mergeMillisecondsBox.IsEnabled = true;
        _mergeNotesCheckBox.Unchecked += (_, _) => _mergeMillisecondsBox.IsEnabled = false;

        mergePanel.Children.Add(mergeInputPanel);
        stackPanel.Children.Add(mergePanel);

        // Hold Notes Section
        stackPanel.Children.Add(new TextBlock { Text = "Hold Notes", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });

        var holdPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _holdNotesCheckBox = new System.Windows.Controls.CheckBox
        {
            Content = "Enable Hold Notes",
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = holdNotes ?? false
        };
        holdPanel.Children.Add(_holdNotesCheckBox);
        stackPanel.Children.Add(holdPanel);

        // Hidden checkboxes for compatibility - always considered "custom" now
        _useCustomMergeCheckBox = new System.Windows.Controls.CheckBox { IsChecked = true, Visibility = Visibility.Collapsed };
        _useCustomHoldCheckBox = new System.Windows.Controls.CheckBox { IsChecked = true, Visibility = Visibility.Collapsed };

        Content = stackPanel;
    }
}
