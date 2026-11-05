//! Redact account directories only at diagnostic output boundaries.
use regex::{NoExpand, Regex};
use std::sync::OnceLock;

struct PathRedactor {
    windows: Regex,
    unix: Regex,
    home: Vec<(Regex, &'static str)>,
}

impl PathRedactor {
    fn new(home: Option<&str>, windows: bool) -> Self {
        let mut patterns = Vec::new();
        if let Some(home) = home {
            let home = home.trim_end_matches(['/', '\\']);
            // Never treat a filesystem root or an empty variable as a profile.
            if home.len() > 3 {
                let forward = home.replace('\\', "/");
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
                        Regex::new(&pattern).expect("escaped profile path"),
                        if windows { "%USERPROFILE%" } else { "$HOME" },
                    ));
                }
            }
        }
        Self {
            windows: Regex::new(r#"(?i)(?:\\{2,4}\?\\+)?[a-z]:[\\/]+Users[\\/]+[^\\/\r\n"<>|?#]+"#)
                .expect("Windows profile pattern"),
            unix: Regex::new(r#"/(?:home|Users)/[^\\/\r\n"<>|?#]+"#).expect("Unix profile pattern"),
            home: patterns,
        }
    }

    fn redact(&self, text: &str) -> String {
        let mut text = text.to_owned();
        // Also handle relocated profiles via the actual environment, before the
        // standard layouts (which cover older logs from other machines/users).
        for (pattern, placeholder) in &self.home {
            text = pattern
                .replace_all(&text, |captures: &regex::Captures<'_>| {
                    format!("{}{}", placeholder, &captures["end"])
                })
                .into_owned();
        }
        let text = self.windows.replace_all(&text, NoExpand("%USERPROFILE%"));
        self.unix.replace_all(&text, NoExpand("$HOME")).into_owned()
    }
}

pub fn redact_paths(text: &str) -> String {
    static REDACTOR: OnceLock<PathRedactor> = OnceLock::new();
    REDACTOR
        .get_or_init(|| {
            let home = std::env::var(if cfg!(windows) { "USERPROFILE" } else { "HOME" }).ok();
            PathRedactor::new(home.as_deref(), cfg!(windows))
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
            r#"[ConfigManager] Reloading configs from "%USERPROFILE%\\AppData\\Local\\Jed556\\AutoMIDIPlayer""#
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
        let text = "/home/Jane Smith/a.mid /Users/María/b.mid /mnt/profiles/Jane Smith/c.mid";
        let expected = "$HOME/a.mid $HOME/b.mid $HOME/c.mid";
        assert_eq!(r.redact(text), expected);
        assert_eq!(r.redact(expected), expected);
        let ordinary = r"C:\Music\Jed556\a.mid; user Jane Smith; %USERPROFILE%\Music\a.mid; https://example.com/users/Jane";
        assert_eq!(r.redact(ordinary), ordinary);
        assert_eq!(
            PathRedactor::new(Some("/"), false).redact("/tmp/a.mid"),
            "/tmp/a.mid"
        );
    }
}
