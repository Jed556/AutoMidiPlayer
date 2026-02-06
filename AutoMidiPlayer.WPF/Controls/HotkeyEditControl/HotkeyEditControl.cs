using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoMidiPlayer.WPF.Services;

namespace AutoMidiPlayer.WPF.Controls;

public partial class HotkeyEditControl : UserControl
{
    public static readonly DependencyProperty HotkeyBindingProperty = DependencyProperty.Register(
        nameof(HotkeyBinding),
        typeof(HotkeyBinding),
        typeof(HotkeyEditControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing),
        typeof(bool),
        typeof(HotkeyEditControl),
        new PropertyMetadata(false, OnIsEditingChanged));

    public static readonly DependencyProperty IsNotEditingProperty = DependencyProperty.Register(
        nameof(IsNotEditing),
        typeof(bool),
        typeof(HotkeyEditControl),
        new PropertyMetadata(true));

    public static readonly DependencyProperty EditingPartsProperty = DependencyProperty.Register(
        nameof(EditingParts),
        typeof(List<HotkeyPart>),
        typeof(HotkeyEditControl),
        new PropertyMetadata(new List<HotkeyPart>()));

    public static readonly DependencyProperty IsGlowActiveProperty = DependencyProperty.Register(
        nameof(IsGlowActive),
        typeof(bool),
        typeof(HotkeyEditControl),
        new PropertyMetadata(false));

    public HotkeyBinding? HotkeyBinding
    {
        get => (HotkeyBinding?)GetValue(HotkeyBindingProperty);
        set => SetValue(HotkeyBindingProperty, value);
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

    private Key _pendingKey = Key.None;
    private ModifierKeys _pendingModifiers = ModifierKeys.None;
    private readonly System.Windows.Threading.DispatcherTimer _glowTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    public HotkeyEditControl()
    {
        InitializeComponent();
        _glowTimer.Tick += (_, _) =>
        {
            _glowTimer.Stop();
            IsGlowActive = false;
        };
    }

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyEditControl control)
        {
            control.IsNotEditing = !(bool)e.NewValue;

            if ((bool)e.NewValue)
            {
                // Focus the edit border when entering edit mode
                control.Dispatcher.BeginInvoke(new Action(() =>
                {
                    control.EditBorder.Focus();
                    Keyboard.Focus(control.EditBorder);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingKey = Key.None;
        _pendingModifiers = ModifierKeys.None;
        EditingParts = HotkeyBinding?.DisplayParts ?? new List<HotkeyPart> { new("Not Set", true) };
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
            EditingParts = BuildEditingParts(Keyboard.Modifiers, Key.None);
            return;
        }

        // Get current modifiers
        var modifiers = Keyboard.Modifiers;

        // Require at least one modifier for non-function keys
        if (modifiers == ModifierKeys.None && !IsFunctionKey(key))
        {
            // Don't allow single keys without modifiers (except F1-F12)
            return;
        }

        _pendingKey = key;
        _pendingModifiers = modifiers;

        EditingParts = BuildEditingParts(modifiers, key);

        // Apply the hotkey
        if (HotkeyBinding != null)
        {
            HotkeyChanged?.Invoke(this, new HotkeyChangedEventArgs(HotkeyBinding.Name, key, modifiers));
        }

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

    private static List<HotkeyPart> BuildEditingParts(ModifierKeys modifiers, Key key)
    {
        var parts = new List<HotkeyPart>();

        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add(new HotkeyPart("Ctrl", parts.Count == 0));
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(new HotkeyPart("Alt", parts.Count == 0));
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(new HotkeyPart("Shift", parts.Count == 0));
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add(new HotkeyPart("Win", parts.Count == 0));

        if (key != Key.None)
        {
            var text = HotkeyBinding.GetKeyDisplayName(key);
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

public class HotkeyChangedEventArgs : EventArgs
{
    public string Name { get; }
    public Key Key { get; }
    public ModifierKeys Modifiers { get; }

    public HotkeyChangedEventArgs(string name, Key key, ModifierKeys modifiers)
    {
        Name = name;
        Key = key;
        Modifiers = modifiers;
    }
}
