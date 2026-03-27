using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.Services;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public class EditDialog : ContentDialog
{
    static EditDialog()
    {
        // Ensure the base ContentDialog style is applied to this derived dialog.
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EditDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    private readonly Wpf.Ui.Controls.TextBox _titleBox;
    private readonly Wpf.Ui.Controls.TextBox _authorBox;
    private readonly Wpf.Ui.Controls.TextBox _albumBox;
    private readonly System.Windows.Controls.ComboBox _keyComboBox;
    private readonly System.Windows.Controls.ComboBox _transposeComboBox;
    private readonly System.Windows.Controls.TextBlock _dateText;
    private readonly Wpf.Ui.Controls.TextBox _bpmBox;
    private readonly ToggleSwitch _mergeNotesToggle;
    private readonly Wpf.Ui.Controls.TextBox _mergeMillisecondsBox;
    private readonly ToggleSwitch _holdNotesToggle;
    private readonly System.Windows.Controls.ComboBox _speedComboBox;
    private readonly string _midiFilePath;
    private readonly System.Collections.Generic.List<MusicConstants.SpeedOption> _speedOptions;

    private readonly string _initialTitle;
    private readonly string _initialAuthor;
    private readonly string _initialAlbum;
    private readonly int _initialKey;
    private readonly int? _initialDefaultKeyRoot;
    private readonly Transpose _initialTranspose;
    private readonly DateTime _initialDateAdded;
    private readonly double _initialNativeBpm;
    private readonly double? _initialCustomBpm;
    private readonly bool _initialMergeNotes;
    private readonly uint _initialMergeMilliseconds;
    private readonly bool _initialHoldNotes;
    private readonly double _initialSpeed;

    private int _defaultKeyRoot;
    private bool _hasDefaultKeyRoot;
    private DateTime _songDateAdded;
    private double _nativeBpm;

    public string SongTitle => _titleBox.Text;
    public string SongAuthor => _authorBox.Text;
    public string SongAlbum => _albumBox.Text;
    public DateTime? SongDateAdded => _songDateAdded;
    public int? SongDefaultKey => _hasDefaultKeyRoot ? _defaultKeyRoot : null;
    public int SongKey { get; private set; }
    public Transpose SongTranspose => MusicConstants.TransposeNames.Keys.ElementAt(_transposeComboBox.SelectedIndex);

    /// <summary>
    /// Gets the per-song speed override. Returns null for default 1.0x.
    /// </summary>
    public double? SongSpeed
    {
        get
        {
            if (_speedComboBox.SelectedItem is MusicConstants.SpeedOption opt)
                return Math.Abs(opt.Value - 1.0) < 0.01 ? null : opt.Value;
            return null;
        }
    }

    /// <summary>
    /// Gets the BPM override. Returns null when value matches native BPM.
    /// </summary>
    public double? SongBpm
    {
        get
        {
            if (double.TryParse(_bpmBox.Text, out var bpm) && bpm > 0 && bpm <= 999)
                return Math.Abs(bpm - _nativeBpm) < 0.01 ? null : bpm;
            return null;
        }
    }

    /// <summary>
    /// Gets the per-song merge notes setting.
    /// </summary>
    public bool SongMergeNotes => _mergeNotesToggle.IsChecked == true;

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
    public bool SongHoldNotes => _holdNotesToggle.IsChecked == true;

    public EditDialog(string defaultTitle, string midiFilePath, int defaultKey = 0, int? defaultKeyRoot = null, Transpose defaultTranspose = Transpose.Ignore, string? defaultAuthor = null, string? defaultAlbum = null, DateTime? defaultDateAdded = null, double nativeBpm = 120, double? customBpm = null, bool? mergeNotes = null, uint? mergeMilliseconds = null, bool? holdNotes = false, double? speed = null)
    {
        // Set up the DialogHost for this ContentDialog
        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
        {
            Style = dialogStyle;
        }

        // Keep the dialog within the active window bounds to avoid clipping on fullscreen toggle.
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                           ?? Application.Current.MainWindow;
        if (activeWindow != null)
        {
            void UpdateDialogBounds()
            {
                var maxHeight = Math.Max(0, activeWindow.ActualHeight - 120);
                var maxWidth = Math.Max(0, activeWindow.ActualWidth - 120);
                DialogMaxHeight = maxHeight;
                DialogMaxWidth = maxWidth;
                DialogMargin = new Thickness(24);
            }

            UpdateDialogBounds();
            SizeChangedEventHandler? sizeChangedHandler = (_, _) => UpdateDialogBounds();
            activeWindow.SizeChanged += sizeChangedHandler;
            EventHandler? stateChangedHandler = (_, _) => UpdateDialogBounds();
            activeWindow.StateChanged += stateChangedHandler;
            Closed += (_, _) =>
            {
                activeWindow.SizeChanged -= sizeChangedHandler;
                activeWindow.StateChanged -= stateChangedHandler;
            };
        }

        Title = "Edit Song";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        PrimaryButtonAppearance = ControlAppearance.Primary;
        CloseButtonAppearance = ControlAppearance.Secondary;
        DefaultButton = ContentDialogButton.Primary;
        Loaded += (_, _) =>
        {
            ApplyPrimaryButtonAccent();
            ApplyDialogButtonCursors(this);
        };

        _midiFilePath = midiFilePath;
        _hasDefaultKeyRoot = defaultKeyRoot.HasValue;
        _defaultKeyRoot = defaultKeyRoot ?? 0;
        _songDateAdded = ResolveMidiDate(midiFilePath, defaultDateAdded);
        _nativeBpm = nativeBpm;

        _initialTitle = defaultTitle;
        _initialAuthor = defaultAuthor ?? string.Empty;
        _initialAlbum = defaultAlbum ?? string.Empty;
        _initialKey = defaultKey;
        _initialDefaultKeyRoot = defaultKeyRoot;
        _initialTranspose = defaultTranspose;
        _initialDateAdded = _songDateAdded;
        _initialNativeBpm = nativeBpm;
        _initialCustomBpm = customBpm;
        _initialMergeNotes = mergeNotes ?? false;
        _initialMergeMilliseconds = mergeMilliseconds ?? 100;
        _initialHoldNotes = holdNotes ?? false;
        _initialSpeed = speed ?? 1.0;

        _speedOptions = MusicConstants.GenerateSpeedOptions();

        var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 12, 0) };

        // Title + Album row
        var titleAlbumGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 12) };
        titleAlbumGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleAlbumGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(12) });
        titleAlbumGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titlePanel = new System.Windows.Controls.StackPanel();
        titlePanel.Children.Add(new TextBlock { Text = "Title", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _titleBox = new Wpf.Ui.Controls.TextBox { Text = defaultTitle };
        titlePanel.Children.Add(_titleBox);
        System.Windows.Controls.Grid.SetColumn(titlePanel, 0);
        titleAlbumGrid.Children.Add(titlePanel);

        var albumPanel = new System.Windows.Controls.StackPanel();
        albumPanel.Children.Add(new TextBlock { Text = "Album", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _albumBox = new Wpf.Ui.Controls.TextBox { Text = defaultAlbum ?? string.Empty };
        albumPanel.Children.Add(_albumBox);
        System.Windows.Controls.Grid.SetColumn(albumPanel, 2);
        titleAlbumGrid.Children.Add(albumPanel);

        stackPanel.Children.Add(titleAlbumGrid);

        // Author
        stackPanel.Children.Add(new TextBlock { Text = "Author/s", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _authorBox = new Wpf.Ui.Controls.TextBox { Text = defaultAuthor ?? string.Empty, Margin = new Thickness(0, 0, 0, 12) };
        stackPanel.Children.Add(_authorBox);

        // Key + transpose grouped in columns
        var keyTransposeGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 12) };
        keyTransposeGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyTransposeGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(12) });
        keyTransposeGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyPanel = new System.Windows.Controls.StackPanel();
        keyPanel.Children.Add(new TextBlock { Text = "Key Offset", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _keyComboBox = new System.Windows.Controls.ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        _keyComboBox.ItemContainerStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem))
        {
            Setters =
            {
                new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left)
            }
        };
        _keyComboBox.SelectionChanged += (_, _) =>
        {
            if (_keyComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is int key)
                SongKey = key;
        };
        PopulateKeyOptions(defaultKey);
        keyPanel.Children.Add(_keyComboBox);
        System.Windows.Controls.Grid.SetColumn(keyPanel, 0);
        keyTransposeGrid.Children.Add(keyPanel);

        var transposePanel = new System.Windows.Controls.StackPanel();
        transposePanel.Children.Add(new TextBlock { Text = "Transpose", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _transposeComboBox = new System.Windows.Controls.ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };

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
        transposePanel.Children.Add(_transposeComboBox);
        System.Windows.Controls.Grid.SetColumn(transposePanel, 2);
        keyTransposeGrid.Children.Add(transposePanel);
        stackPanel.Children.Add(keyTransposeGrid);

        // BPM + speed grouped in columns
        var tempoGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 8, 0, 0) };
        tempoGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tempoGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(12) });
        tempoGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bpmPanel = new System.Windows.Controls.StackPanel();
        bpmPanel.Children.Add(new TextBlock { Text = "BPM", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        _bpmBox = new Wpf.Ui.Controls.TextBox
        {
            Text = customBpm?.ToString("F1") ?? string.Empty,
            PlaceholderText = nativeBpm.ToString("F1"),
            Width = 88,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        UpdateNativeBpmText();
        bpmPanel.Children.Add(_bpmBox);
        System.Windows.Controls.Grid.SetColumn(bpmPanel, 0);
        tempoGrid.Children.Add(bpmPanel);

        var speedPanel = new System.Windows.Controls.StackPanel();
        speedPanel.Children.Add(new TextBlock { Text = "Speed", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        _speedComboBox = new System.Windows.Controls.ComboBox { MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        foreach (var opt in _speedOptions)
            _speedComboBox.Items.Add(opt);
        _speedComboBox.DisplayMemberPath = "Display";
        SetSpeedSelection(speed ?? 1.0);
        speedPanel.Children.Add(_speedComboBox);

        System.Windows.Controls.Grid.SetColumn(speedPanel, 2);
        tempoGrid.Children.Add(speedPanel);
        stackPanel.Children.Add(tempoGrid);

        // Merge/Hold grouped in columns
        var mergeHoldGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 12, 0, 0) };
        mergeHoldGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mergeHoldGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(12) });
        mergeHoldGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mergeSection = new System.Windows.Controls.StackPanel();
        mergeSection.Children.Add(new TextBlock { Text = "Merge Notes", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        var mergeInputPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        _mergeNotesToggle = new ToggleSwitch
        {
            Content = string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = mergeNotes ?? false,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        mergeInputPanel.Children.Add(_mergeNotesToggle);

        mergeInputPanel.Children.Add(new TextBlock { Text = "Tolerance (ms):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _mergeMillisecondsBox = new Wpf.Ui.Controls.TextBox
        {
            Text = (mergeMilliseconds ?? 100).ToString(),
            Width = 60,
            IsEnabled = mergeNotes ?? false
        };
        mergeInputPanel.Children.Add(_mergeMillisecondsBox);

        _mergeNotesToggle.Checked += (_, _) => _mergeMillisecondsBox.IsEnabled = true;
        _mergeNotesToggle.Unchecked += (_, _) => _mergeMillisecondsBox.IsEnabled = false;

        mergeSection.Children.Add(mergeInputPanel);
        System.Windows.Controls.Grid.SetColumn(mergeSection, 0);
        mergeHoldGrid.Children.Add(mergeSection);

        var holdSection = new System.Windows.Controls.StackPanel();
        holdSection.Children.Add(new TextBlock { Text = "Hold Notes", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        var holdPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        _holdNotesToggle = new ToggleSwitch
        {
            Content = string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = holdNotes ?? false,
            Cursor = Cursors.Hand
        };
        holdPanel.Children.Add(_holdNotesToggle);
        holdSection.Children.Add(holdPanel);
        System.Windows.Controls.Grid.SetColumn(holdSection, 2);
        mergeHoldGrid.Children.Add(holdSection);

        stackPanel.Children.Add(mergeHoldGrid);

        // Reset defaults action above path reference section.
        var resetNormalBrush = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        var resetHoverBrush = new SolidColorBrush(Color.FromRgb(172, 37, 24));
        var resetPressedBrush = new SolidColorBrush(Color.FromRgb(150, 30, 20));

        var resetButton = new System.Windows.Controls.Button
        {
            ToolTip = "Reset fields and rescan MIDI defaults",
            Margin = new Thickness(0, 12, 0, 14),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 132,
            Cursor = Cursors.Hand,
            Background = resetNormalBrush,
            BorderBrush = resetNormalBrush,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1)
        };

        var resetContent = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        resetContent.Children.Add(new SymbolIcon
        {
            Symbol = SymbolRegular.ArrowClockwise24,
            FontSize = 14,
            Foreground = Brushes.White
        });
        resetContent.Children.Add(new TextBlock
        {
            Text = "Reset Defaults",
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White
        });
        resetButton.Content = resetContent;

        resetButton.MouseEnter += (_, _) =>
        {
            resetButton.Background = resetHoverBrush;
            resetButton.BorderBrush = resetHoverBrush;
        };
        resetButton.MouseLeave += (_, _) =>
        {
            resetButton.Background = resetNormalBrush;
            resetButton.BorderBrush = resetNormalBrush;
        };
        resetButton.PreviewMouseLeftButtonDown += (_, _) =>
        {
            resetButton.Background = resetPressedBrush;
            resetButton.BorderBrush = resetPressedBrush;
        };
        resetButton.PreviewMouseLeftButtonUp += (_, _) =>
        {
            var targetBrush = resetButton.IsMouseOver ? resetHoverBrush : resetNormalBrush;
            resetButton.Background = targetBrush;
            resetButton.BorderBrush = targetBrush;
        };
        resetButton.Click += (_, _) => ResetAndRescan();
        stackPanel.Children.Add(resetButton);

        // MIDI path reference above footer buttons
        stackPanel.Children.Add(new System.Windows.Controls.Separator { Margin = new Thickness(0, 0, 0, 10) });
        stackPanel.Children.Add(CreatePathReferenceBlock());

        _dateText = new System.Windows.Controls.TextBlock
        {
            Margin = new Thickness(0, 2, 0, 2),
            FontSize = 11,
            Opacity = 0.7,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 210, 210))
        };
        UpdateDateText();
        stackPanel.Children.Add(_dateText);

        Content = stackPanel;
    }

    private void ApplyPrimaryButtonAccent()
    {
        var primaryButton = FindPrimaryButton(this);
        if (primaryButton == null)
            return;

        primaryButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "SystemAccentColorPrimaryBrush");
        primaryButton.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "SystemAccentColorPrimaryBrush");
        primaryButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
    }

    private static void ApplyDialogButtonCursors(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is System.Windows.Controls.Button button)
                button.Cursor = Cursors.Hand;

            ApplyDialogButtonCursors(child);
        }
    }

    private static System.Windows.Controls.Button? FindPrimaryButton(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is System.Windows.Controls.Button button)
            {
                var text = button.Content?.ToString();
                if (string.Equals(text, "Save", StringComparison.OrdinalIgnoreCase))
                    return button;
            }

            var nested = FindPrimaryButton(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void PopulateKeyOptions(int selectedKey)
    {
        _keyComboBox.Items.Clear();

        var keyRoot = _hasDefaultKeyRoot ? _defaultKeyRoot : (int?)null;
        var minKey = MusicConstants.GetRelativeMinKeyOffset(keyRoot);
        var maxKey = MusicConstants.GetRelativeMaxKeyOffset(keyRoot);
        var clampedKey = Math.Clamp(selectedKey, minKey, maxKey);

        var selectedIndex = 0;
        var index = 0;
        foreach (var option in MusicConstants.GenerateKeyOptions(keyRoot))
        {
            _keyComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = $"{option.OffsetDisplay} {option.NoteDisplay}",
                Tag = option.Value
            });

            if (option.Value == clampedKey)
                selectedIndex = index;

            index++;
        }

        _keyComboBox.SelectedIndex = selectedIndex;
        SongKey = clampedKey;
    }

    private void SetSpeedSelection(double speed)
    {
        var matchIdx = _speedOptions.FindIndex(s => Math.Abs(s.Value - speed) < 0.01);
        if (matchIdx < 0)
            matchIdx = _speedOptions.FindIndex(s => Math.Abs(s.Value - 1.0) < 0.01);

        if (matchIdx >= 0)
            _speedComboBox.SelectedIndex = matchIdx;
    }

    private void UpdateDateText() =>
        _dateText.Text = _songDateAdded.ToString("yyyy-MM-dd HH:mm");

    private void UpdateNativeBpmText() =>
        _bpmBox.PlaceholderText = _nativeBpm.ToString("F1");

    private static DateTime ResolveMidiDate(string filePath, DateTime? fallbackDate)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return File.GetLastWriteTime(filePath);

        return fallbackDate ?? DateTime.Now;
    }

    private void ResetAndRescan()
    {
        ResetToInitialValues();
        RescanMidiDefaults();
    }

    private void ResetToInitialValues()
    {
        _titleBox.Text = _initialTitle;
        _authorBox.Text = _initialAuthor;
        _albumBox.Text = _initialAlbum;

        _hasDefaultKeyRoot = _initialDefaultKeyRoot.HasValue;
        _defaultKeyRoot = _initialDefaultKeyRoot ?? 0;
        PopulateKeyOptions(_initialKey);

        _transposeComboBox.SelectedIndex = Math.Max(0, MusicConstants.TransposeNames.Keys.ToList().IndexOf(_initialTranspose));

        _songDateAdded = _initialDateAdded;
        UpdateDateText();

        _nativeBpm = _initialNativeBpm;
        UpdateNativeBpmText();
        _bpmBox.Text = _initialCustomBpm?.ToString("F1") ?? string.Empty;

        _mergeNotesToggle.IsChecked = _initialMergeNotes;
        _mergeMillisecondsBox.IsEnabled = _initialMergeNotes;
        _mergeMillisecondsBox.Text = _initialMergeMilliseconds.ToString();
        _holdNotesToggle.IsChecked = _initialHoldNotes;

        SetSpeedSelection(_initialSpeed);
    }

    private void RescanMidiDefaults()
    {
        if (!FileService.TryAnalyzeMidiFile(_midiFilePath, out var analysis))
            return;

        _songDateAdded = analysis.FileDate;
        UpdateDateText();

        var currentKey = SongKey;
        _nativeBpm = analysis.NativeBpm;
        UpdateNativeBpmText();

        if (analysis.DetectedDefaultKeyOffset.HasValue)
        {
            _hasDefaultKeyRoot = true;
            _defaultKeyRoot = analysis.DetectedDefaultKeyOffset.Value;
        }

        PopulateKeyOptions(currentKey);
    }

    private FrameworkElement CreatePathReferenceBlock()
    {
        var pathTextBlock = new System.Windows.Controls.TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        };

        var pathRun = string.IsNullOrWhiteSpace(_midiFilePath) ? "(unknown)" : _midiFilePath;
        var pathLink = new Hyperlink(new Run(pathRun));
        pathLink.Cursor = Cursors.Hand;

        if (Application.Current.TryFindResource("AppHyperlinkStyle") is Style hyperlinkStyle)
            pathLink.Style = hyperlinkStyle;

        pathLink.Click += (_, args) =>
        {
            OpenMidiPathInExplorer();
            args.Handled = true;
        };

        pathTextBlock.Inlines.Add(pathLink);
        return pathTextBlock;
    }

    private void OpenMidiPathInExplorer()
    {
        if (string.IsNullOrWhiteSpace(_midiFilePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_midiFilePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Best effort: opening Explorer should not block saving edits.
        }
    }

}
