//! A Wayland portal cannot identify the foreground game. Keep song output
//! stopped while AMP owns focus, and make the handoff cancellable.
#[cfg(any(target_os = "linux", test))]
pub fn wait_for_handoff(
    mut app_focused: impl FnMut() -> bool,
    mut cancelled: impl FnMut() -> bool,
    timeout: std::time::Duration,
) -> Result<(), String> {
    let started = std::time::Instant::now();
    let mut unfocused_since = None;
    loop {
        if cancelled() { return Err("Playback request cancelled.".into()); }
        if app_focused() {
            unfocused_since = None;
        } else if unfocused_since.get_or_insert_with(std::time::Instant::now).elapsed()
            >= std::time::Duration::from_millis(150) {
            // Let focus events settle after the portal dialog closes before
            // treating a transient dialog focus loss as a game handoff.
            return Ok(());
        }
        if started.elapsed() >= timeout {
            return Err("Switch to the game before starting playback on Wayland.".into());
        }
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn cancellation_wins_even_if_focus_has_already_left_amp() {
        assert!(wait_for_handoff(|| false, || true, Duration::ZERO).unwrap_err().contains("cancelled"));
    }

    #[test]
    fn playback_can_start_only_after_the_focus_handoff() {
        assert!(wait_for_handoff(|| false, || false, Duration::from_secs(1)).is_ok());
        assert!(wait_for_handoff(|| true, || false, Duration::ZERO).unwrap_err().contains("Switch to the game"));
    }
}
