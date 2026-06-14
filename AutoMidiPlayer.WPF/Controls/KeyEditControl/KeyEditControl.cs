using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoMidiPlayer.WPF.Services;
using AutoMidiPlayer.WPF.Core;

namespace AutoMidiPlayer.WPF.Controls;

public partial class KeyEditControl : UserControl
{
    public static readonly DependencyProperty HotkeyBindingProperty = DependencyProperty.Register(
        nameof(HotkeyBinding),
        typeof(HotkeyBinding),
        typeof(KeyEditControl),
        new PropertyMetadata(null, OnBindingChanged));

    public static readonly DependencyProperty KeyBindingProperty = DependencyProperty.Register(
        nameof(KeyBinding),
        typeof(VirtualKeyCode?),
        typeof(KeyEditControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindingChanged));

    public static readonly DependencyProperty MaxKeysAcceptedProperty = DependencyProperty.Register(
        nameof(MaxKeysAccepted),
        typeof(int),
        typeof(KeyEditControl),
        new PropertyMetadata(int.MaxValue));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(KeyEditControl),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing),
        typeof(bool),
        typeof(KeyEditControl),
        new PropertyMetadata(false, OnIsEditingChanged));

    public static readonly DependencyProperty IsNotEditingProperty = DependencyProperty.Register(
        nameof(IsNotEditing),
        typeof(bool),
        typeof(KeyEditControl),
        new PropertyMetadata(true));

    public static readonly DependencyProperty DisplayPartsProperty = DependencyProperty.Register(
        nameof(DisplayParts),
        typeof(List<HotkeyPart>),
        typeof(KeyEditControl),
        new PropertyMetadata(new List<HotkeyPart>()));

    public static readonly DependencyProperty EditingPartsProperty = DependencyProperty.Register(
        nameof(EditingParts),
        typeof(List<HotkeyPart>),
        typeof(KeyEditControl),
        new PropertyMetadata(new List<HotkeyPart>()));

    public static readonly DependencyProperty IsGlowActiveProperty = DependencyProperty.Register(
        nameof(IsGlowActive),
        typeof(bool),
        typeof(KeyEditControl),
        new PropertyMetadata(false));

    public HotkeyBinding? HotkeyBinding
    {
        get => (HotkeyBinding?)GetValue(HotkeyBindingProperty);
        set => SetValue(HotkeyBindingProperty, value);
    }

    public VirtualKeyCode? KeyBinding
    {
        get => (VirtualKeyCode?)GetValue(KeyBindingProperty);
        set => SetValue(KeyBindingProperty, value);
    }

    public int MaxKeysAccepted
    {
        get => (int)GetValue(MaxKeysAcceptedProperty);
        set => SetValue(MaxKeysAcceptedProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty DefaultTextProperty = DependencyProperty.Register(
        nameof(DefaultText),
        typeof(string),
        typeof(KeyEditControl),
        new PropertyMetadata("Not Set"));

    public string DefaultText
    {
        get => (string)GetValue(DefaultTextProperty);
        set => SetValue(DefaultTextProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public bool IsNotEditing
    {
        get => (bool)GetValue(IsNotEditingProperty);
        set => SetValue(IsNotEditingProperty, value);
    }

    public List<HotkeyPart> DisplayParts
    {
        get => (List<HotkeyPart>)GetValue(DisplayPartsProperty);
        set => SetValue(DisplayPartsProperty, value);
    }

    public List<HotkeyPart> EditingParts
    {
        get => (List<HotkeyPart>)GetValue(EditingPartsProperty);
        set => SetValue(EditingPartsProperty, value);
    }

    public bool IsGlowActive
    {
        get => (bool)GetValue(IsGlowActiveProperty);
        set => SetValue(IsGlowActiveProperty, value);
    }

    public event EventHandler<HotkeyChangedEventArgs>? HotkeyChanged;
    public event EventHandler<string>? HotkeyCleared;
    public event EventHandler? EditStarted;
    public event EventHandler? EditEnded;

    private readonly System.Windows.Threading.DispatcherTimer _glowTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    public KeyEditControl()
    {
        InitializeComponent();
        _glowTimer.Tick += (_, _) =>
        {
            _glowTimer.Stop();
            IsGlowActive = false;
        };
        UpdateDisplay();
    }

    private static void OnBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyEditControl control)
        {
            if (e.OldValue is System.ComponentModel.INotifyPropertyChanged oldObj)
            {
                oldObj.PropertyChanged -= control.OnBindingPropertyChanged;
            }
            if (e.NewValue is System.ComponentModel.INotifyPropertyChanged newObj)
            {
                newObj.PropertyChanged += control.OnBindingPropertyChanged;
            }
            control.UpdateDisplay();
            
            if (control.IsLoaded)
            {
                control.TriggerGlow();
            }
        }
    }

    private void OnBindingPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "DisplayParts" || e.PropertyName == "DisplayName")
        {
            UpdateDisplay();
            if (IsLoaded)
            {
                TriggerGlow();
            }
        }
    }

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyEditControl control)
        {
            control.IsNotEditing = !(bool)e.NewValue;

            if ((bool)e.NewValue)
            {
                // Focus the edit border when entering edit mode
                control.Dispatcher.BeginInvoke(new Action(() =>
                {
                    control.EditBorder.Focus();
                    System.Windows.Input.Keyboard.Focus(control.EditBorder);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
    }

    private void UpdateDisplay()
    {
        if (HotkeyBinding != null)
        {
            if (string.IsNullOrEmpty(Title))
            {
                Title = HotkeyBinding.DisplayName;
            }
            if (HotkeyBinding.Key == Key.None)
            {
                DisplayParts = new List<HotkeyPart> { new(DefaultText, true) };
            }
            else
            {
                DisplayParts = HotkeyBinding.DisplayParts;
            }
        }
        else if (KeyBinding != null)
        {
            DisplayParts = new List<HotkeyPart> { new(KeyBinding.Value.ToString().Replace("VK_", ""), true) };
        }
        else
        {
            DisplayParts = new List<HotkeyPart> { new(DefaultText, true) };
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        EditingParts = DisplayParts;
        IsGlowActive = false;
        EditStarted?.Invoke(this, EventArgs.Empty);
        IsEditing = true;
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        IsEditing = false;
        EditEnded?.Invoke(this, EventArgs.Empty);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeyBinding != null)
        {
            HotkeyCleared?.Invoke(this, HotkeyBinding.Name);
        }
        else
        {
            KeyBinding = null;
        }
        UpdateDisplay();
    }

    private void EditBorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Handle Escape to cancel
        if (key == Key.Escape)
        {
            IsEditing = false;
            EditEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Ignore modifier-only keys
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
        {
            EditingParts = BuildEditingParts(System.Windows.Input.Keyboard.Modifiers, Key.None);
            return;
        }

        // Get current modifiers
        var modifiers = System.Windows.Input.Keyboard.Modifiers;

        // Check MaxKeysAccepted
        int keyCount = 1; // The primary key
        if (modifiers.HasFlag(ModifierKeys.Control)) keyCount++;
        if (modifiers.HasFlag(ModifierKeys.Alt)) keyCount++;
        if (modifiers.HasFlag(ModifierKeys.Shift)) keyCount++;
        if (modifiers.HasFlag(ModifierKeys.Windows)) keyCount++;

        if (keyCount > MaxKeysAccepted)
        {
            // Too many keys, ignore or strip modifiers
            if (MaxKeysAccepted == 1)
            {
                modifiers = ModifierKeys.None;
            }
            else
            {
                return; // Or we can reject the input
            }
        }

        // Require at least one modifier for non-function keys IF we support HotkeyBinding and not MaxKeysAccepted=1
        if (HotkeyBinding != null && modifiers == ModifierKeys.None && !IsFunctionKey(key) && MaxKeysAccepted > 1)
        {
            // Don't allow single keys without modifiers (except F1-F12) for global hotkeys
            return;
        }

        EditingParts = BuildEditingParts(modifiers, key);

        // Apply the hotkey
        if (HotkeyBinding != null)
        {
            HotkeyChanged?.Invoke(this, new HotkeyChangedEventArgs(HotkeyBinding.Name, key, modifiers));
        }
        else
        {
            var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
            if (Enum.IsDefined(typeof(VirtualKeyCode), virtualKey))
            {
                KeyBinding = (VirtualKeyCode)virtualKey;
            }
        }

        UpdateDisplay();
        TriggerGlow();
        IsEditing = false;
        EditEnded?.Invoke(this, EventArgs.Empty);
    }

    private void EditBorder_KeyDown(object sender, KeyEventArgs e)
    {
        // Handled in PreviewKeyDown
        e.Handled = true;
    }

    private static bool IsFunctionKey(Key key)
    {
        return key >= Key.F1 && key <= Key.F24;
    }

    private List<HotkeyPart> BuildEditingParts(ModifierKeys modifiers, Key key)
    {
        var parts = new List<HotkeyPart>();

        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add(new HotkeyPart("Ctrl", parts.Count == 0));
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(new HotkeyPart("Alt", parts.Count == 0));
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(new HotkeyPart("Shift", parts.Count == 0));
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add(new HotkeyPart("Win", parts.Count == 0));

        if (key != Key.None)
        {
            string text;
            if (HotkeyBinding != null)
            {
                text = HotkeyBinding.GetKeyDisplayName(key);
            }
            else
            {
                var virtualKey = KeyInterop.VirtualKeyFromKey(key);
                text = ((VirtualKeyCode)virtualKey).ToString().Replace("VK_", "");
            }
            parts.Add(new HotkeyPart(text, parts.Count == 0));
        }

        if (parts.Count == 0)
        {
            parts.Add(new HotkeyPart("Not Set", true));
        }

        return parts;
    }

    private void TriggerGlow()
    {
        IsGlowActive = true;
        _glowTimer.Stop();
        _glowTimer.Start();
    }
}

public class HotkeyChangedEventArgs(string name, Key key, ModifierKeys modifiers) : EventArgs
{
    public string Name { get; } = name;
    public Key Key { get; } = key;
    public ModifierKeys Modifiers { get; } = modifiers;
}
