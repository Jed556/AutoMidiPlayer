using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AutoMidiPlayer.Data.Properties;
using Stylet;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Service for registering and handling global hotkeys (work even when app is not focused).
/// </summary>
public class GlobalHotkeyService : PropertyChangedBase, IDisposable
{
    #region Win32 API

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const int WM_HOTKEY = 0x0312;
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    // Modifier keys
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    #endregion

    #region Fields

    private static readonly Settings Settings = Settings.Default;
    private readonly Dictionary<int, HotkeyBinding> _registeredHotkeys = new();
    private HwndSource? _hwndSource;
    private IntPtr _windowHandle;
    private IntPtr _mouseHookHandle;
    private LowLevelMouseProc? _mouseHookProc;
    private int _nextHotkeyId = 0x5000;
    private bool _isEnabled = true;
    private MouseStopClickMode _mouseStopClickMode;

    #endregion

    #region Events

    public event EventHandler? PlayPausePressed;
    public event EventHandler? NextPressed;
    public event EventHandler? PreviousPressed;
    public event EventHandler? SpeedUpPressed;
    public event EventHandler? SpeedDownPressed;
    public event EventHandler? PanicPressed;
    public event EventHandler? MouseStopRequested;

    #endregion

    #region Properties

    private bool _isSuspended;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetAndNotify(ref _isEnabled, value))
            {
                if (value && !_isSuspended)
                    RegisterAllHotkeys();
                else
                    UnregisterAllHotkeys();
            }
        }
    }

    /// <summary>
    /// Temporarily unregister all hotkeys (e.g. during hotkey editing).
    /// </summary>
    public void SuspendHotkeys()
    {
        _isSuspended = true;
        UnregisterAllHotkeys();
    }

    /// <summary>
    /// Re-register hotkeys after suspension.
    /// </summary>
    public void ResumeHotkeys()
    {
        _isSuspended = false;
        if (_isEnabled)
            RegisterAllHotkeys();
    }

    public HotkeyBinding PlayPauseHotkey { get; private set; }
    public HotkeyBinding NextHotkey { get; private set; }
    public HotkeyBinding PreviousHotkey { get; private set; }
    public HotkeyBinding SpeedUpHotkey { get; private set; }
    public HotkeyBinding SpeedDownHotkey { get; private set; }
    public HotkeyBinding PanicHotkey { get; private set; }

    #endregion

    #region Constructor

    public GlobalHotkeyService()
    {
        _mouseStopClickMode = ResolveMouseStopClickMode();

        // Load hotkeys from settings or use defaults
        PlayPauseHotkey = LoadOrCreateHotkey("PlayPause", Key.Space, ModifierKeys.Control | ModifierKeys.Alt);
        NextHotkey = LoadOrCreateHotkey("Next", Key.Right, ModifierKeys.Control | ModifierKeys.Alt);
        PreviousHotkey = LoadOrCreateHotkey("Previous", Key.Left, ModifierKeys.Control | ModifierKeys.Alt);
        SpeedUpHotkey = LoadOrCreateHotkey("SpeedUp", Key.Up, ModifierKeys.Control | ModifierKeys.Alt);
        SpeedDownHotkey = LoadOrCreateHotkey("SpeedDown", Key.Down, ModifierKeys.Control | ModifierKeys.Alt);
        PanicHotkey = LoadOrCreateHotkey("Panic", Key.Escape, ModifierKeys.Control | ModifierKeys.Alt);
    }

    #endregion

    #region Initialization

    public void Initialize(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(HwndHook);

        _mouseStopClickMode = ResolveMouseStopClickMode();
        UpdateMouseHookState();

        if (_isEnabled)
            RegisterAllHotkeys();
    }

    public void RefreshMouseStopClickMode()
    {
        _mouseStopClickMode = ResolveMouseStopClickMode();
        UpdateMouseHookState();
    }

    #endregion

    #region Hotkey Management

    private HotkeyBinding LoadOrCreateHotkey(string name, Key defaultKey, ModifierKeys defaultModifiers)
    {
        var settingValue = name switch
        {
            "PlayPause" => Settings.HotkeyPlayPause,
            "Next" => Settings.HotkeyNext,
            "Previous" => Settings.HotkeyPrevious,
            "SpeedUp" => Settings.HotkeySpeedUp,
            "SpeedDown" => Settings.HotkeySpeedDown,
            "Panic" => Settings.HotkeyPanic,
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(settingValue) && HotkeyBinding.TryParse(settingValue, name, out var hotkey))
            return hotkey;

        return new HotkeyBinding(name, defaultKey, defaultModifiers);
    }

    public void UpdateHotkey(string name, Key key, ModifierKeys modifiers)
    {
        var hotkey = name switch
        {
            "PlayPause" => PlayPauseHotkey,
            "Next" => NextHotkey,
            "Previous" => PreviousHotkey,
            "SpeedUp" => SpeedUpHotkey,
            "SpeedDown" => SpeedDownHotkey,
            "Panic" => PanicHotkey,
            _ => throw new ArgumentException($"Unknown hotkey: {name}")
        };

        // Unregister old hotkey
        if (hotkey.IsRegistered)
            UnregisterHotkey(hotkey);

        // Update binding
        hotkey.Key = key;
        hotkey.Modifiers = modifiers;

        // Save to settings
        var serialized = hotkey.Serialize();
        switch (name)
        {
            case "PlayPause": Settings.HotkeyPlayPause = serialized; break;
            case "Next": Settings.HotkeyNext = serialized; break;
            case "Previous": Settings.HotkeyPrevious = serialized; break;
            case "SpeedUp": Settings.HotkeySpeedUp = serialized; break;
            case "SpeedDown": Settings.HotkeySpeedDown = serialized; break;
            case "Panic": Settings.HotkeyPanic = serialized; break;
        }
        Settings.Save();

        // Register new hotkey
        if (_isEnabled && _hwndSource != null)
            RegisterHotkey(hotkey);
    }

    public void ClearHotkey(string name)
    {
        var hotkey = name switch
        {
            "PlayPause" => PlayPauseHotkey,
            "Next" => NextHotkey,
            "Previous" => PreviousHotkey,
            "SpeedUp" => SpeedUpHotkey,
            "SpeedDown" => SpeedDownHotkey,
            "Panic" => PanicHotkey,
            _ => throw new ArgumentException($"Unknown hotkey: {name}")
        };

        // Unregister
        if (hotkey.IsRegistered)
            UnregisterHotkey(hotkey);

        // Clear binding
        hotkey.Key = Key.None;
        hotkey.Modifiers = ModifierKeys.None;

        // Save to settings
        switch (name)
        {
            case "PlayPause": Settings.HotkeyPlayPause = string.Empty; break;
            case "Next": Settings.HotkeyNext = string.Empty; break;
            case "Previous": Settings.HotkeyPrevious = string.Empty; break;
            case "SpeedUp": Settings.HotkeySpeedUp = string.Empty; break;
            case "SpeedDown": Settings.HotkeySpeedDown = string.Empty; break;
            case "Panic": Settings.HotkeyPanic = string.Empty; break;
        }
        Settings.Save();
    }

    /// <summary>
    /// Resets all hotkeys to their default values.
    /// </summary>
    public void ResetToDefaults()
    {
        UnregisterAllHotkeys();

        // Reset to defaults
        PlayPauseHotkey.Key = Key.Space;
        PlayPauseHotkey.Modifiers = ModifierKeys.Control | ModifierKeys.Alt;
        NextHotkey.Key = Key.Right;
        NextHotkey.Modifiers = ModifierKeys.Control | ModifierKeys.Alt;
        PreviousHotkey.Key = Key.Left;
        PreviousHotkey.Modifiers = ModifierKeys.Control | ModifierKeys.Alt;
        SpeedUpHotkey.Key = Key.Up;
        SpeedUpHotkey.Modifiers = ModifierKeys.Control | ModifierKeys.Alt;
        SpeedDownHotkey.Key = Key.Down;
        SpeedDownHotkey.Modifiers = ModifierKeys.Control | ModifierKeys.Alt;
        PanicHotkey.Key = Key.Escape;
        PanicHotkey.Modifiers = ModifierKeys.Control | ModifierKeys.Alt;

        // Save defaults
        Settings.HotkeyPlayPause = PlayPauseHotkey.Serialize();
        Settings.HotkeyNext = NextHotkey.Serialize();
        Settings.HotkeyPrevious = PreviousHotkey.Serialize();
        Settings.HotkeySpeedUp = SpeedUpHotkey.Serialize();
        Settings.HotkeySpeedDown = SpeedDownHotkey.Serialize();
        Settings.HotkeyPanic = PanicHotkey.Serialize();
        Settings.Save();

        // Re-register if enabled
        if (_isEnabled && !_isSuspended)
            RegisterAllHotkeys();
    }

    private void RegisterAllHotkeys()
    {
        RegisterHotkey(PlayPauseHotkey);
        RegisterHotkey(NextHotkey);
        RegisterHotkey(PreviousHotkey);
        RegisterHotkey(SpeedUpHotkey);
        RegisterHotkey(SpeedDownHotkey);
        RegisterHotkey(PanicHotkey);
    }

    private void UnregisterAllHotkeys()
    {
        UnregisterHotkey(PlayPauseHotkey);
        UnregisterHotkey(NextHotkey);
        UnregisterHotkey(PreviousHotkey);
        UnregisterHotkey(SpeedUpHotkey);
        UnregisterHotkey(SpeedDownHotkey);
        UnregisterHotkey(PanicHotkey);
    }

    private void RegisterHotkey(HotkeyBinding hotkey)
    {
        if (_windowHandle == IntPtr.Zero || hotkey.Key == Key.None || hotkey.Key == Key.PrintScreen)
            return;

        var id = _nextHotkeyId++;
        var modifiers = GetWin32Modifiers(hotkey.Modifiers) | MOD_NOREPEAT;
        var vk = KeyInterop.VirtualKeyFromKey(hotkey.Key);

        if (RegisterHotKey(_windowHandle, id, modifiers, (uint)vk))
        {
            hotkey.Id = id;
            hotkey.IsRegistered = true;
            _registeredHotkeys[id] = hotkey;
        }
    }

    private void RemoveMouseHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    private void UpdateMouseHookState()
    {
        var shouldHookMouse = _isEnabled
                              && !_isSuspended
                              && _mouseStopClickMode != MouseStopClickMode.Off;

        if (shouldHookMouse)
            InstallMouseHook();
        else
            RemoveMouseHook();
    }

    private void UnregisterHotkey(HotkeyBinding hotkey)
    {
        if (!hotkey.IsRegistered || _windowHandle == IntPtr.Zero)
            return;

        UnregisterHotKey(_windowHandle, hotkey.Id);
        _registeredHotkeys.Remove(hotkey.Id);
        hotkey.IsRegistered = false;
    }

    private static uint GetWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = MOD_NONE;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= MOD_WIN;
        return result;
    }

    private void InstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
            return;

        _mouseHookProc = MouseHookCallback;

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = GetModuleHandle(moduleName);
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, moduleHandle, 0);

        if (_mouseHookHandle == IntPtr.Zero)
        {
            // Fallback in case module handle resolution fails in some hosting scenarios.
            _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, IntPtr.Zero, 0);
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_isEnabled)
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);

        var message = wParam.ToInt32();
        if (message != WM_LBUTTONDOWN && message != WM_RBUTTONDOWN && message != WM_MBUTTONDOWN)
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);

        if (_mouseStopClickMode != MouseStopClickMode.Off)
        {
            var isMatch = _mouseStopClickMode switch
            {
                MouseStopClickMode.LeftClick => message == WM_LBUTTONDOWN,
                MouseStopClickMode.RightClick => message == WM_RBUTTONDOWN,
                MouseStopClickMode.MiddleClick => message == WM_MBUTTONDOWN,
                _ => false
            };

            if (isMatch)
                MouseStopRequested?.Invoke(this, EventArgs.Empty);
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private static MouseStopClickMode ResolveMouseStopClickMode()
    {
        var configured = Settings.MouseStopClickMode;
        return Enum.IsDefined(typeof(MouseStopClickMode), configured)
            ? (MouseStopClickMode)configured
            : MouseStopClickMode.Off;
    }

    #endregion

    #region Message Hook

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _isEnabled)
        {
            var id = wParam.ToInt32();
            if (_registeredHotkeys.TryGetValue(id, out var hotkey)
                && MatchesHotkeyMessage(hotkey, lParam))
            {
                switch (hotkey.Name)
                {
                    case "PlayPause":
                        PlayPausePressed?.Invoke(this, EventArgs.Empty);
                        break;
                    case "Next":
                        NextPressed?.Invoke(this, EventArgs.Empty);
                        break;
                    case "Previous":
                        PreviousPressed?.Invoke(this, EventArgs.Empty);
                        break;
                    case "SpeedUp":
                        SpeedUpPressed?.Invoke(this, EventArgs.Empty);
                        break;
                    case "SpeedDown":
                        SpeedDownPressed?.Invoke(this, EventArgs.Empty);
                        break;
                    case "Panic":
                        PanicPressed?.Invoke(this, EventArgs.Empty);
                        break;
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static bool MatchesHotkeyMessage(HotkeyBinding hotkey, IntPtr lParam)
    {
        if (hotkey.Key == Key.None)
            return false;

        var messageData = unchecked((long)lParam);
        var messageModifiers = (uint)(messageData & 0xFFFF);
        var messageVirtualKey = (uint)((messageData >> 16) & 0xFFFF);

        var expectedModifiers = GetWin32Modifiers(hotkey.Modifiers);
        var expectedVirtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);

        return messageModifiers == expectedModifiers
               && messageVirtualKey == expectedVirtualKey;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        UnregisterAllHotkeys();
        RemoveMouseHook();

        _hwndSource?.RemoveHook(HwndHook);
        _hwndSource?.Dispose();
        _mouseHookProc = null;
    }

    #endregion
}

internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

/// <summary>
/// Represents a configurable hotkey binding.
/// </summary>
public class HotkeyBinding(string name, Key key, ModifierKeys modifiers) : PropertyChangedBase
{
    private Key _key = key;
    private ModifierKeys _modifiers = modifiers;

    public string Name { get; } = name;

    public Key Key
    {
        get => _key;
        set
        {
            if (_key != value)
            {
                _key = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(nameof(DisplayHotkey));
                NotifyOfPropertyChange(nameof(DisplayParts));
            }
        }
    }

    public ModifierKeys Modifiers
    {
        get => _modifiers;
        set
        {
            if (_modifiers != value)
            {
                _modifiers = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(nameof(DisplayHotkey));
                NotifyOfPropertyChange(nameof(DisplayParts));
            }
        }
    }

    public int Id { get; set; }
    public bool IsRegistered { get; set; }

    public string DisplayName => Name switch
    {
        "PlayPause" => "Play / Pause",
        "Next" => "Next Track",
        "Previous" => "Previous Track",
        "SpeedUp" => "Speed Up",
        "SpeedDown" => "Speed Down",
        "Panic" => "Panic (Exit)",
        _ => Name
    };

    public string DisplayHotkey
    {
        get
        {
            if (Key == Key.None)
                return "Not Set";

            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(GetKeyDisplayName(Key));
            return string.Join(" + ", parts);
        }
    }

    /// <summary>
    /// Returns key parts as a list of display objects for chip-style rendering.
    /// </summary>
    public List<HotkeyPart> DisplayParts
    {
        get
        {
            if (Key == Key.None)
                return new List<HotkeyPart> { new("Not Set", true) };

            var parts = new List<HotkeyPart>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add(new("Ctrl", parts.Count == 0));
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(new("Alt", parts.Count == 0));
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(new("Shift", parts.Count == 0));
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add(new("Win", parts.Count == 0));
            parts.Add(new(GetKeyDisplayName(Key), parts.Count == 0));
            return parts;
        }
    }

    public static string GetKeyDisplayName(Key key)
    {
        return key switch
        {
            // Number keys
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            // Numpad keys
            Key.NumPad0 => "Numpad 0",
            Key.NumPad1 => "Numpad 1",
            Key.NumPad2 => "Numpad 2",
            Key.NumPad3 => "Numpad 3",
            Key.NumPad4 => "Numpad 4",
            Key.NumPad5 => "Numpad 5",
            Key.NumPad6 => "Numpad 6",
            Key.NumPad7 => "Numpad 7",
            Key.NumPad8 => "Numpad 8",
            Key.NumPad9 => "Numpad 9",
            Key.Multiply => "Numpad *",
            Key.Add => "Numpad +",
            Key.Subtract => "Numpad -",
            Key.Decimal => "Numpad .",
            Key.Divide => "Numpad /",
            // OEM keys
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemTilde => "`",
            // Arrow keys
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            // Navigation
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Escape => "Escape",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.Tab => "Tab",
            Key.CapsLock => "Caps Lock",
            Key.NumLock => "Num Lock",
            Key.Scroll => "Scroll Lock",
            Key.PrintScreen => "Print Screen",
            Key.Pause => "Pause",
            _ => key.ToString()
        };
    }

    public string Serialize() => $"{(int)Modifiers}|{(int)Key}";

    public static bool TryParse(string value, string name, out HotkeyBinding hotkey)
    {
        hotkey = new HotkeyBinding(name, Key.None, ModifierKeys.None);

        var parts = value.Split('|');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out var modifiers) || !int.TryParse(parts[1], out var key))
            return false;

        hotkey.Modifiers = (ModifierKeys)modifiers;
        hotkey.Key = (Key)key;
        return true;
    }
}

/// <summary>
/// Represents a single part of a hotkey display (e.g. "Ctrl", "Alt", "Space").
/// Used for chip-style rendering in the UI.
/// </summary>
public class HotkeyPart(string text, bool isFirst)
{
    public string Text { get; } = text;
    public bool IsFirst { get; } = isFirst;
}
