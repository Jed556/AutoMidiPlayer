using System;
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

    public event EventHandler<HotkeyChangedEventArgs>? HotkeyChanged;
    public event EventHandler<string>? HotkeyCleared;
    public event EventHandler? EditStarted;
    public event EventHandler? EditEnded;

    private Key _pendingKey = Key.None;
    private ModifierKeys _pendingModifiers = ModifierKeys.None;

    public HotkeyEditControl()
    {
        InitializeComponent();
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

        // Apply the hotkey
        if (HotkeyBinding != null)
        {
            HotkeyChanged?.Invoke(this, new HotkeyChangedEventArgs(HotkeyBinding.Name, key, modifiers));
        }

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
