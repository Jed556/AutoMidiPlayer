using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Services;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class EditDialog : ContentDialog
{
    private static readonly Settings UserSettings = Settings.Default;

    static EditDialog()
    {
        // Ensure the base ContentDialog style is applied to this derived dialog.
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EditDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    private Wpf.Ui.Controls.TextBox _titleBox => TitleBox;
    private Wpf.Ui.Controls.TextBox _artistBox => ArtistBox;
    private Wpf.Ui.Controls.TextBox _albumBox => AlbumBox;
    private System.Windows.Controls.ComboBox _defaultKeyComboBox => DefaultKeyComboBox;
    private System.Windows.Controls.ComboBox _keyComboBox => KeyComboBox;
    private System.Windows.Controls.ComboBox _transposeComboBox => TransposeComboBox;
    private System.Windows.Controls.TextBlock _dateText => DateText;
    private Wpf.Ui.Controls.TextBox _bpmBox => BpmBox;
    private ToggleSwitch _mergeNotesToggle => MergeNotesToggle;
    private Wpf.Ui.Controls.TextBox _mergeMillisecondsBox => MergeMillisecondsBox;
    private ToggleSwitch _holdNotesToggle => HoldNotesToggle;
    private System.Windows.Controls.ComboBox _speedComboBox => SpeedComboBox;

    private readonly string _midiFilePath;
    private readonly System.Collections.Generic.List<MusicConstants.SpeedOption> _speedOptions;
    private readonly System.Collections.Generic.List<TransposeOption> _transposeOptions;

    private readonly string _initialTitle;
    private readonly string _initialArtist;
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
    private bool _isForwardingWheelEvent;

    public string SongTitle => _titleBox.Text;
    public string SongArtist => _artistBox.Text;
    public string SongAlbum => _albumBox.Text;
    public DateTime? SongDateAdded => _songDateAdded;
    public int? SongDefaultKey => _hasDefaultKeyRoot ? _defaultKeyRoot : null;
    public int SongKey { get; private set; }
    public Transpose SongTranspose => _transposeComboBox.SelectedItem is TransposeOption option ? option.Value : Transpose.Ignore;

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
            return 100;
        }
    }

    /// <summary>
    /// Gets the per-song hold notes setting.
    /// </summary>
    public bool SongHoldNotes => _holdNotesToggle.IsChecked == true;

    public EditDialog(
        string defaultTitle,
        string midiFilePath,
        int defaultKey = 0,
        int? defaultKeyRoot = null,
        Transpose defaultTranspose = Transpose.Ignore,
        string? defaultArtist = null,
        string? defaultAlbum = null,
        DateTime? defaultDateAdded = null,
        double nativeBpm = 120,
        double? customBpm = null,
        bool? mergeNotes = null,
        uint? mergeMilliseconds = null,
        bool? holdNotes = false,
        double? speed = null)
    {
        DialogHelper.SetupDialogHost(this);
        InitializeComponent();

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

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
        _initialArtist = defaultArtist ?? string.Empty;
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
        _transposeOptions = MusicConstants.TransposeNames
            .Select(kvp => new TransposeOption(kvp.Key, kvp.Value))
            .ToList();

        InitializeUi(defaultTitle, defaultArtist, defaultAlbum, defaultKey, defaultKeyRoot, defaultTranspose, customBpm, mergeNotes, mergeMilliseconds, holdNotes, speed);
    }

    private void InitializeUi(
        string defaultTitle,
        string? defaultArtist,
        string? defaultAlbum,
        int defaultKey,
        int? defaultKeyRoot,
        Transpose defaultTranspose,
        double? customBpm,
        bool? mergeNotes,
        uint? mergeMilliseconds,
        bool? holdNotes,
        double? speed)
    {
        _titleBox.Text = defaultTitle;
        _artistBox.Text = defaultArtist ?? string.Empty;
        _albumBox.Text = defaultAlbum ?? string.Empty;

        _defaultKeyComboBox.ItemTemplate = CreateOffsetNoteSelectorTemplate(nameof(DefaultKeyOption.OffsetDisplay), nameof(DefaultKeyOption.NoteDisplay));
        _keyComboBox.ItemTemplate = CreateOffsetNoteSelectorTemplate(nameof(MusicConstants.KeyOption.OffsetDisplay), nameof(MusicConstants.KeyOption.NoteDisplay));
        _transposeComboBox.ItemTemplate = CreateSelectorDisplayTemplate(nameof(TransposeOption.Display));
        _speedComboBox.ItemTemplate = CreateSelectorDisplayTemplate(nameof(MusicConstants.SpeedOption.Display));

        _transposeComboBox.ItemsSource = _transposeOptions;
        _speedComboBox.ItemsSource = _speedOptions;

        PopulateDefaultKeyOptions(defaultKeyRoot);
        PopulateKeyOptions(defaultKey);
        SetTransposeSelection(defaultTranspose);
        SetSpeedSelection(speed ?? 1.0);

        _bpmBox.Text = customBpm?.ToString("F1") ?? string.Empty;
        _mergeNotesToggle.IsChecked = mergeNotes ?? false;
        _mergeMillisecondsBox.Text = (mergeMilliseconds ?? 100).ToString();
        _mergeMillisecondsBox.IsEnabled = _mergeNotesToggle.IsChecked == true;
        _holdNotesToggle.IsChecked = holdNotes ?? false;

        UpdateNativeBpmText();
        UpdateDateText();

        PathRun.Text = string.IsNullOrWhiteSpace(_midiFilePath) ? "(unknown)" : _midiFilePath;
        if (Application.Current.TryFindResource("AppHyperlinkStyle") is Style hyperlinkStyle)
            PathHyperlink.Style = hyperlinkStyle;
    }

    private void RescanDefaultKeyButton_Click(object sender, RoutedEventArgs e)
    {
        RescanMidiDefaults();
    }

    private void MergeNotesToggle_Checked(object sender, RoutedEventArgs e)
    {
        _mergeMillisecondsBox.IsEnabled = true;
    }

    private void MergeNotesToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _mergeMillisecondsBox.IsEnabled = false;
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        ResetAndRescan();
    }

    private void PathHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenMidiPathInExplorer();
        e.Handled = true;
    }

    private void OnDialogPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isForwardingWheelEvent || e.Handled)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        if (DialogScrollViewer.ScrollableHeight <= 0 || DialogScrollViewer.ComputedVerticalScrollBarVisibility != Visibility.Visible)
            return;

        if (e.OriginalSource is not DependencyObject source)
            return;

        if (FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null)
            return;

        var sourceComboBox = FindAncestor<System.Windows.Controls.ComboBox>(source);
        if (sourceComboBox?.IsDropDownOpen == true)
            return;

        _isForwardingWheelEvent = true;
        try
        {
            e.Handled = true;

            var previewEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
                Source = e.OriginalSource
            };
            DialogScrollViewer.RaiseEvent(previewEvent);

            if (!previewEvent.Handled)
            {
                var bubbleEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = e.OriginalSource
                };
                DialogScrollViewer.RaiseEvent(bubbleEvent);
            }
        }
        finally
        {
            _isForwardingWheelEvent = false;
        }
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
        var keyRoot = _hasDefaultKeyRoot ? _defaultKeyRoot : (int?)null;
        var minKey = MusicConstants.GetRelativeMinKeyOffset(keyRoot);
        var maxKey = MusicConstants.GetRelativeMaxKeyOffset(keyRoot);
        var clampedKey = Math.Clamp(selectedKey, minKey, maxKey);

        var keyOptions = MusicConstants.GenerateKeyOptions(keyRoot);
        _keyComboBox.ItemsSource = keyOptions;

        var selectedOption = keyOptions.FirstOrDefault(option => option.Value == clampedKey)
            ?? keyOptions.FirstOrDefault();

        _keyComboBox.SelectedItem = selectedOption;

        if (selectedOption != null)
            SongKey = selectedOption.Value;
        else
            SongKey = clampedKey;
    }

    private void PopulateDefaultKeyOptions(int? selectedDefaultKey)
    {
        var clampedDefaultKey = selectedDefaultKey.HasValue
            ? Math.Clamp(selectedDefaultKey.Value, MusicConstants.MinKeyOffset, MusicConstants.MaxKeyOffset)
            : (int?)null;

        var options = new System.Collections.Generic.List<DefaultKeyOption>
        {
            new(null, "Legacy", "(None)")
        };

        options.AddRange(
            MusicConstants.GenerateKeyOptions().Select(option =>
                new DefaultKeyOption(option.Value, option.OffsetDisplay, option.NoteDisplay))
        );

        _defaultKeyComboBox.ItemsSource = options;

        var selectedOption = options.FirstOrDefault(option => option.Value == clampedDefaultKey)
            ?? options.First();

        _defaultKeyComboBox.SelectedItem = selectedOption;
    }

    private static DataTemplate CreateOffsetNoteSelectorTemplate(string offsetPath, string notePath)
    {
        var panelFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.StackPanel));
        panelFactory.SetValue(System.Windows.Controls.StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);

        var offsetTextFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        offsetTextFactory.Name = "KeyOffsetText";
        offsetTextFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new Binding(offsetPath));
        offsetTextFactory.SetValue(FrameworkElement.MinWidthProperty, 24d);
        offsetTextFactory.SetValue(System.Windows.Controls.TextBlock.TextAlignmentProperty, TextAlignment.Right);
        offsetTextFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        offsetTextFactory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 12d);
        offsetTextFactory.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var noteTextFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        noteTextFactory.Name = "KeyNoteText";
        noteTextFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new Binding(notePath));
        noteTextFactory.SetValue(System.Windows.Controls.TextBlock.TextAlignmentProperty, TextAlignment.Left);
        noteTextFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        noteTextFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        noteTextFactory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 12d);
        noteTextFactory.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        panelFactory.AppendChild(offsetTextFactory);
        panelFactory.AppendChild(noteTextFactory);

        return new DataTemplate
        {
            VisualTree = panelFactory
        };
    }

    private void OnDefaultKeyComboBoxSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_defaultKeyComboBox.SelectedItem is not DefaultKeyOption option)
            return;

        if (option.Value.HasValue)
        {
            _hasDefaultKeyRoot = true;
            _defaultKeyRoot = option.Value.Value;
        }
        else
        {
            _hasDefaultKeyRoot = false;
            _defaultKeyRoot = 0;
        }

        PopulateKeyOptions(SongKey);
    }

    private void SetSpeedSelection(double speed)
    {
        var matchIdx = _speedOptions.FindIndex(s => Math.Abs(s.Value - speed) < 0.01);
        if (matchIdx < 0)
            matchIdx = _speedOptions.FindIndex(s => Math.Abs(s.Value - 1.0) < 0.01);

        if (matchIdx >= 0)
            _speedComboBox.SelectedItem = _speedOptions[matchIdx];
    }

    private void SetTransposeSelection(Transpose transpose)
    {
        var selectedOption = _transposeOptions.FirstOrDefault(option => option.Value == transpose)
            ?? _transposeOptions.First();

        _transposeComboBox.SelectedItem = selectedOption;
    }

    private static DataTemplate CreateSelectorDisplayTemplate(string bindingPath)
    {
        var textBlockFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        textBlockFactory.Name = "DisplayText";
        textBlockFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new Binding(bindingPath));
        textBlockFactory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 12d);
        textBlockFactory.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        return new DataTemplate
        {
            VisualTree = textBlockFactory
        };
    }

    private void OnKeyComboBoxSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_keyComboBox.SelectedItem is not MusicConstants.KeyOption option)
            return;

        SongKey = option.Value;
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

        if (!UserSettings.AutoDetectDefaultKey)
        {
            _hasDefaultKeyRoot = true;
            _defaultKeyRoot = 0;
            PopulateDefaultKeyOptions(_defaultKeyRoot);
            PopulateKeyOptions(0);
            return;
        }

        RescanMidiDefaults();
    }

    private void ResetToInitialValues()
    {
        _titleBox.Text = _initialTitle;
        _artistBox.Text = _initialArtist;
        _albumBox.Text = _initialAlbum;

        _hasDefaultKeyRoot = _initialDefaultKeyRoot.HasValue;
        _defaultKeyRoot = _initialDefaultKeyRoot ?? 0;
        PopulateDefaultKeyOptions(_initialDefaultKeyRoot);
        PopulateKeyOptions(_initialKey);

        SetTransposeSelection(_initialTranspose);

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

        PopulateDefaultKeyOptions(SongDefaultKey);
        PopulateKeyOptions(currentKey);
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

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
                return typed;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class TransposeOption(Transpose value, string display)
    {
        public Transpose Value { get; } = value;
        public string Display { get; } = display;
    }

    private sealed class DefaultKeyOption(int? value, string offsetDisplay, string noteDisplay)
    {
        public int? Value { get; } = value;
        public string OffsetDisplay { get; } = offsetDisplay;
        public string NoteDisplay { get; } = noteDisplay;
        public string Display => $"{OffsetDisplay} {NoteDisplay}";
    }
}
