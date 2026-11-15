use tauri_plugin_global_shortcut::{Code, Modifiers, Shortcut};

/// The portal uses XKB keysym names and XDG modifier names, rather than DOM
/// keyboard codes or the native global-shortcut parser's accelerator syntax.
pub(crate) fn portal_trigger(shortcut: &Shortcut) -> Result<String, String> {
    let code = shortcut.key.to_string();
    let key = match shortcut.key {
        Code::Backquote => "grave",
        Code::Backslash => "backslash",
        Code::BracketLeft => "bracketleft",
        Code::BracketRight => "bracketright",
        Code::Comma => "comma",
        Code::Equal => "equal",
        Code::Minus => "minus",
        Code::Period => "period",
        Code::Quote => "apostrophe",
        Code::Semicolon => "semicolon",
        Code::Slash => "slash",
        Code::Space => "space",
        Code::Enter => "Return",
        Code::Backspace => "BackSpace",
        Code::ArrowUp => "Up",
        Code::ArrowDown => "Down",
        Code::ArrowLeft => "Left",
        Code::ArrowRight => "Right",
        Code::PageUp => "Prior",
        Code::PageDown => "Next",
        Code::CapsLock => "Caps_Lock",
        Code::NumLock => "Num_Lock",
        Code::ScrollLock => "Scroll_Lock",
        Code::PrintScreen => "Print",
        Code::NumpadAdd => "KP_Add",
        Code::NumpadSubtract => "KP_Subtract",
        Code::NumpadMultiply => "KP_Multiply",
        Code::NumpadDivide => "KP_Divide",
        Code::NumpadDecimal => "KP_Decimal",
        Code::NumpadEnter => "KP_Enter",
        Code::NumpadEqual => "KP_Equal",
        Code::AudioVolumeUp => "XF86AudioRaiseVolume",
        Code::AudioVolumeDown => "XF86AudioLowerVolume",
        Code::AudioVolumeMute => "XF86AudioMute",
        Code::MediaPlay | Code::MediaPlayPause => "XF86AudioPlay",
        Code::MediaPause => "XF86AudioPause",
        Code::MediaStop => "XF86AudioStop",
        Code::MediaTrackNext => "XF86AudioNext",
        Code::MediaTrackPrevious => "XF86AudioPrev",
        Code::Escape
        | Code::Tab
        | Code::Delete
        | Code::End
        | Code::Home
        | Code::Insert
        | Code::Pause => &code,
        _ if code.starts_with("Key") => &code[3..],
        _ if code.starts_with("Digit") => &code[5..],
        _ if code.starts_with("Numpad") && code[6..].parse::<u8>().is_ok_and(|n| n <= 9) => {
            return Ok(with_modifiers(shortcut.mods, &format!("KP_{}", &code[6..])));
        }
        _ if code.starts_with('F')
            && code[1..].parse::<u8>().is_ok_and(|n| (1..=24).contains(&n)) =>
        {
            &code
        }
        _ => {
            return Err(format!(
                "Key '{code}' cannot be represented as a Wayland shortcut."
            ))
        }
    };
    let key = if code.starts_with("Key") {
        key.to_ascii_lowercase()
    } else {
        key.to_owned()
    };
    Ok(with_modifiers(shortcut.mods, &key))
}

fn with_modifiers(modifiers: Modifiers, key: &str) -> String {
    let mut parts = Vec::new();
    for (modifier, name) in [
        (Modifiers::CONTROL, "CTRL"),
        (Modifiers::ALT, "ALT"),
        (Modifiers::SHIFT, "SHIFT"),
        (Modifiers::SUPER, "LOGO"),
    ] {
        if modifiers.contains(modifier) {
            parts.push(name);
        }
    }
    parts.push(key);
    parts.join("+")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn portal_shortcuts_use_xdg_modifiers_and_xkb_keysyms() {
        for (native, expected) in [
            ("ctrl+alt+space", "CTRL+ALT+space"),
            ("super+shift+KeyA", "SHIFT+LOGO+a"),
            ("ctrl+BracketLeft", "CTRL+bracketleft"),
            ("alt+Numpad0", "ALT+KP_0"),
            ("ctrl+NumpadAdd", "CTRL+KP_Add"),
            ("ctrl+Enter", "CTRL+Return"),
            ("ctrl+PageDown", "CTRL+Next"),
            ("ctrl+Backspace", "CTRL+BackSpace"),
            ("alt+F24", "ALT+F24"),
            ("AudioVolumeUp", "XF86AudioRaiseVolume"),
        ] {
            assert_eq!(portal_trigger(&native.parse().unwrap()).unwrap(), expected);
        }
    }
}
