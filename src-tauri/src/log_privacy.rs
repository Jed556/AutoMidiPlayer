//! Redact account directories only at diagnostic output boundaries.
use regex::{NoExpand, Regex};
use std::sync::OnceLock;

struct RedactEnv<'a> {
    home: Option<&'a str>,
    local_app_data: Option<&'a str>,
    roaming_app_data: Option<&'a str>,
    user: Option<&'a str>,
    xdg_data_home: Option<&'a str>,
    windows: bool,
}

struct PathRedactor {
    windows_local_app_data: Regex,
    windows_roaming_app_data: Regex,
    windows: Regex,
    unix: Regex,
    unix_run_media: Regex,
    env_patterns: Vec<(usize, Regex, &'static str)>,
}

fn add_env_patterns(
    patterns: &mut Vec<(usize, Regex, &'static str)>,
    path: Option<&str>,
    placeholder: &'static str,
    windows: bool,
) {
    let Some(path) = path else {
        return;
    };
    let path = path.trim_end_matches(['/', '\\']);
    // Never treat a filesystem root or an empty variable as a user directory.
    if path.len() <= 3 {
        return;
    }
    let forward = path.replace('\\', "/");
    let mut variants = vec![forward.clone()];
    if windows {
        let back = forward.replace('/', "\\");
        variants.push(back.replace('\\', "\\\\"));
        variants.push(back);
    }
    variants.sort();
    variants.dedup();
    for variant in variants {
        let pattern = format!(
            r#"{}{}(?P<end>[\\/\s"'<>|,;:)\]}}]|$)"#,
            if windows { "(?i)" } else { "" },
            regex::escape(&variant),
        );
        patterns.push((
            variant.len(),
            Regex::new(&pattern).expect("escaped env path"),
            placeholder,
        ));
    }
}

fn is_under_dir(candidate: &str, parent: Option<&str>) -> bool {
    let Some(parent) = parent else {
        return false;
    };
    let parent = parent.trim_end_matches(['/', '\\']);
    let candidate = candidate.trim_end_matches(['/', '\\']);
    candidate == parent
        || candidate
            .strip_prefix(parent)
            .is_some_and(|rest| rest.starts_with('/') || rest.starts_with('\\'))
}

impl PathRedactor {
    #[cfg(test)]
    fn new(home: Option<&str>, windows: bool) -> Self {
        Self::with_env(RedactEnv {
            home,
            local_app_data: None,
            roaming_app_data: None,
            user: None,
            xdg_data_home: None,
            windows,
        })
    }

    fn with_env(env: RedactEnv<'_>) -> Self {
        let mut patterns = Vec::new();
        if env.windows {
            add_env_patterns(&mut patterns, env.local_app_data, "%LOCALAPPDATA%", true);
            add_env_patterns(&mut patterns, env.roaming_app_data, "%APPDATA%", true);
            add_env_patterns(&mut patterns, env.home, "%USERPROFILE%", true);
        } else {
            add_env_patterns(&mut patterns, env.home, "$HOME", false);
            if let Some(xdg) = env.xdg_data_home.filter(|p| !is_under_dir(p, env.home)) {
                add_env_patterns(&mut patterns, Some(xdg), "$XDG_DATA_HOME", false);
            }
            if let Some(user) = env.user.map(str::trim).filter(|u| !u.is_empty()) {
                let media_path = format!("/media/{user}");
                let run_media_path = format!("/run/media/{user}");
                add_env_patterns(&mut patterns, Some(&media_path), "/media/$USER", false);
                add_env_patterns(&mut patterns, Some(&run_media_path), "/run/media/$USER", false);
            }
        }
        // Match longest/most-specific paths first (e.g. %LOCALAPPDATA% before %USERPROFILE%).
        patterns.sort_by(|a, b| b.0.cmp(&a.0));

        Self {
            windows_local_app_data: Regex::new(
                r#"(?i)(?:\\{2,4}\?\\+)?[a-z]:[\\/]+Users[\\/]+[^\\/\r\n"<>|?#]+[\\/]+AppData[\\/]+Local(?P<end>[\\/\s"'<>|,;:)\]}}]|$)"#,
            )
            .expect("Windows local app data pattern"),
            windows_roaming_app_data: Regex::new(
                r#"(?i)(?:\\{2,4}\?\\+)?[a-z]:[\\/]+Users[\\/]+[^\\/\r\n"<>|?#]+[\\/]+AppData[\\/]+Roaming(?P<end>[\\/\s"'<>|,;:)\]}}]|$)"#,
            )
            .expect("Windows roaming app data pattern"),
            windows: Regex::new(r#"(?i)(?:\\{2,4}\?\\+)?[a-z]:[\\/]+Users[\\/]+[^\\/\r\n"<>|?#]+"#)
                .expect("Windows profile pattern"),
            unix: Regex::new(r#"/(?:var/)?(?:home|Users)/[^\\/\r\n"<>|?#]+"#)
                .expect("Unix profile pattern"),
            unix_run_media: Regex::new(r#"/run/media/[^\\/\r\n"<>|?#]+"#)
                .expect("Unix removable media pattern"),
            env_patterns: patterns,
        }
    }

    fn redact(&self, text: &str) -> String {
        let mut text = text.to_owned();
        // Also handle relocated profiles and AppData directories via the actual environment,
        // before the standard layouts (which cover older logs from other machines/users).
        for (_, pattern, placeholder) in &self.env_patterns {
            text = pattern
                .replace_all(&text, |captures: &regex::Captures<'_>| {
                    format!("{}{}", placeholder, &captures["end"])
                })
                .into_owned();
        }
        let text = self
            .windows_local_app_data
            .replace_all(&text, |captures: &regex::Captures<'_>| {
                format!("%LOCALAPPDATA%{}", &captures["end"])
            });
        let text = self
            .windows_roaming_app_data
            .replace_all(&text, |captures: &regex::Captures<'_>| {
                format!("%APPDATA%{}", &captures["end"])
            });
        let text = self.windows.replace_all(&text, NoExpand("%USERPROFILE%"));
        let text = self.unix.replace_all(&text, NoExpand("$HOME"));
        self.unix_run_media
            .replace_all(&text, NoExpand("/run/media/$USER"))
            .into_owned()
    }
}

pub fn redact_paths(text: &str) -> String {
    static REDACTOR: OnceLock<PathRedactor> = OnceLock::new();
    REDACTOR
        .get_or_init(|| {
            if cfg!(windows) {
                let home = std::env::var("USERPROFILE").ok();
                let local_app_data = std::env::var("LOCALAPPDATA").ok();
                let roaming_app_data = std::env::var("APPDATA").ok();
                PathRedactor::with_env(RedactEnv {
                    home: home.as_deref(),
                    local_app_data: local_app_data.as_deref(),
                    roaming_app_data: roaming_app_data.as_deref(),
                    user: None,
                    xdg_data_home: None,
                    windows: true,
                })
            } else {
                let home = std::env::var("HOME").ok();
                let xdg_data_home = std::env::var("XDG_DATA_HOME").ok();
                let user = std::env::var("USER")
                    .or_else(|_| std::env::var("LOGNAME"))
                    .ok();
                PathRedactor::with_env(RedactEnv {
                    home: home.as_deref(),
                    local_app_data: None,
                    roaming_app_data: None,
                    user: user.as_deref(),
                    xdg_data_home: xdg_data_home.as_deref(),
                    windows: false,
                })
            }
        })
        .redact(text)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn redacts_config_reload_and_preserves_application_author_directory() {
        let r = PathRedactor::new(None, true);
        let message = r#"[ConfigManager] Reloading configs from "C:\\Users\\Jane Smith\\AppData\\Local\\Jed556\\AutoMIDIPlayer""#;
        assert_eq!(
            r.redact(message),
            r#"[ConfigManager] Reloading configs from "%LOCALAPPDATA%\\Jed556\\AutoMIDIPlayer""#
        );
    }

    #[test]
    fn shortens_windows_appdata_paths_before_userprofile() {
        let r = PathRedactor::with_env(RedactEnv {
            home: Some(r"C:\Users\Jane Smith"),
            local_app_data: Some(r"C:\Users\Jane Smith\AppData\Local"),
            roaming_app_data: Some(r"D:\CustomRoaming"),
            user: None,
            xdg_data_home: None,
            windows: true,
        });
        assert_eq!(
            r.redact(r"C:\Users\Jane Smith\AppData\Local\Jed556\AutoMIDIPlayer\app.log"),
            r"%LOCALAPPDATA%\Jed556\AutoMIDIPlayer\app.log"
        );
        assert_eq!(
            r.redact(r"C:\Users\Other User\AppData\Roaming\AutoMidiPlayer\user.config"),
            r"%APPDATA%\AutoMidiPlayer\user.config"
        );
        assert_eq!(
            r.redact(r"D:\CustomRoaming\AutoMidiPlayer\user.config"),
            r"%APPDATA%\AutoMidiPlayer\user.config"
        );
        assert_eq!(
            r.redact(r"C:\Users\Jane Smith\AppData\LocalLow\Temp\test.log"),
            r"%USERPROFILE%\AppData\LocalLow\Temp\test.log"
        );
    }

    #[test]
    fn handles_multiple_paths_unicode_case_and_separator_styles() {
        let r = PathRedactor::new(None, true);
        assert_eq!(
            r.redact("c:\\uSeRs\\María 李\\Music\\a.mid -> D:/Users/Other Person/Desktop/a.mid"),
            "%USERPROFILE%\\Music\\a.mid -> %USERPROFILE%/Desktop/a.mid"
        );
        assert_eq!(
            r.redact(r#"\\?\C:\Users\Jane Smith\Music\a.mid"#),
            r#"%USERPROFILE%\Music\a.mid"#
        );
        assert_eq!(
            r.redact("file:///C:/Users/Jane%20Smith/Music/a.mid"),
            "file:///%USERPROFILE%/Music/a.mid"
        );
    }

    #[test]
    fn custom_home_matches_boundaries_and_debug_escaping() {
        let r = PathRedactor::new(Some(r"D:\Profiles\Jane Smith"), true);
        assert_eq!(
            r.redact(r#""d:\profiles\Jane Smith\a.mid" "D:\\Profiles\\Jane Smith\\b.mid""#),
            r#""%USERPROFILE%\a.mid" "%USERPROFILE%\\b.mid""#
        );
        assert_eq!(
            r.redact("D:/Profiles/Jane Smith/a.mid"),
            "%USERPROFILE%/a.mid"
        );
        assert_eq!(
            r.redact(r"D:\Profiles\Jane Smithsonian\a.mid"),
            r"D:\Profiles\Jane Smithsonian\a.mid"
        );
    }

    #[test]
    fn handles_unix_profiles_and_is_idempotent_without_changing_other_text() {
        let r = PathRedactor::new(Some("/mnt/profiles/Jane Smith"), false);
        let text = "/home/Jane Smith/a.mid /Users/María/b.mid /var/home/alice/c.mid /mnt/profiles/Jane Smith/d.mid";
        let expected = "$HOME/a.mid $HOME/b.mid $HOME/c.mid $HOME/d.mid";
        assert_eq!(r.redact(text), expected);
        assert_eq!(r.redact(expected), expected);
        let ordinary = r"C:\Music\Jed556\a.mid; user Jane Smith; %USERPROFILE%\Music\a.mid; https://example.com/users/Jane";
        assert_eq!(r.redact(ordinary), ordinary);
        assert_eq!(
            PathRedactor::new(Some("/"), false).redact("/tmp/a.mid"),
            "/tmp/a.mid"
        );
    }

    #[test]
    fn masks_unix_media_and_custom_xdg_paths_without_altering_default_xdg() {
        let default_xdg = PathRedactor::with_env(RedactEnv {
            home: Some("/home/alice"),
            local_app_data: None,
            roaming_app_data: None,
            user: Some("alice"),
            xdg_data_home: Some("/home/alice/.local/share"),
            windows: false,
        });
        assert_eq!(
            default_xdg.redact("/home/alice/.local/share/Jed556/AutoMIDIPlayer"),
            "$HOME/.local/share/Jed556/AutoMIDIPlayer"
        );
        assert_eq!(
            default_xdg.redact("/media/alice/USB/song.mid /run/media/bob/MIDI/a.mid /media/cdrom/b.mid"),
            "/media/$USER/USB/song.mid /run/media/$USER/MIDI/a.mid /media/cdrom/b.mid"
        );

        let custom_xdg = PathRedactor::with_env(RedactEnv {
            home: Some("/Users/alice"),
            local_app_data: None,
            roaming_app_data: None,
            user: Some("alice"),
            xdg_data_home: Some("/data/alice/share"),
            windows: false,
        });
        assert_eq!(
            custom_xdg.redact("/data/alice/share/Jed556/AutoMIDIPlayer"),
            "$XDG_DATA_HOME/Jed556/AutoMIDIPlayer"
        );
        assert_eq!(
            custom_xdg.redact("/Users/alice/Library/Application Support/Jed556/AutoMIDIPlayer"),
            "$HOME/Library/Application Support/Jed556/AutoMIDIPlayer"
        );
    }
}
