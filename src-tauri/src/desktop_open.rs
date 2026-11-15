//! Open browser links and local paths with the desktop's native handler.
use std::{path::Path, process::Command};

#[derive(Clone, Copy)]
enum Platform { Windows, Macos, Linux }

#[cfg(not(windows))]
fn platform() -> Platform {
    if cfg!(windows) { Platform::Windows }
    else if cfg!(target_os = "macos") { Platform::Macos }
    else { Platform::Linux }
}

fn url_command(target: Platform, url: &str) -> Result<Command, String> {
    let url = reqwest::Url::parse(url).map_err(|error| format!("Invalid URL: {error}"))?;
    if !matches!(url.scheme(), "https" | "http") || url.host_str().is_none() {
        return Err("Only HTTP and HTTPS browser links can be opened.".into());
    }
    let mut command = match target {
        Platform::Windows => {
            let mut command = Command::new("rundll32");
            command.arg("url.dll,FileProtocolHandler");
            command
        }
        Platform::Macos => Command::new("open"),
        Platform::Linux => Command::new("xdg-open"),
    };
    command.arg(url.as_str());
    Ok(command)
}

fn path_command(target: Platform, path: &Path) -> Result<Command, String> {
    if path.as_os_str().is_empty() { return Err("A path is required.".into()); }
    // xdg-open does not support a leading '-' filename. An absolute path also
    // prevents macOS open from interpreting a relative filename as an option.
    let path = std::path::absolute(path).map_err(|error| format!("Invalid path: {error}"))?;
    let mut command = Command::new(match target {
        Platform::Windows => "explorer",
        Platform::Macos => "open",
        Platform::Linux => "xdg-open",
    });
    command.arg(path);
    Ok(command)
}

#[cfg(not(windows))]
fn spawn(mut command: Command) -> Result<(), String> {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000); // CREATE_NO_WINDOW
    }
    command.spawn().map_err(|error| format!("Failed to open desktop handler: {error}"))?;
    Ok(())
}

#[cfg(not(windows))]
pub fn open_url(url: &str) -> Result<(), String> {
    spawn(url_command(platform(), url)?)
}

/// The caller creates missing directories before invoking the desktop handler.
#[cfg(not(windows))]
pub fn open_path(path: &Path) -> Result<(), String> {
    std::fs::metadata(path).map_err(|error| format!("Cannot open path: {error}"))?;
    spawn(path_command(platform(), path)?)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::OsStr;

    #[test]
    fn browser_links_use_the_platform_handler_and_one_url_argument() {
        let url = "https://example.test/music?q=a%20b&track=1#preview";
        for (platform, program, expected_args) in [
            (Platform::Windows, "rundll32", vec!["url.dll,FileProtocolHandler", url]),
            (Platform::Macos, "open", vec![url]),
            (Platform::Linux, "xdg-open", vec![url]),
        ] {
            let command = url_command(platform, url).unwrap();
            assert_eq!(command.get_program(), OsStr::new(program));
            assert_eq!(command.get_args().collect::<Vec<_>>(), expected_args.iter().map(OsStr::new).collect::<Vec<_>>());
        }
    }

    #[test]
    fn invalid_links_and_desktop_options_are_rejected_before_spawning() {
        for url in ["", "-a Calculator", "file:///tmp/song.mid", "javascript:alert(1)", "https://"] {
            for platform in [Platform::Windows, Platform::Macos, Platform::Linux] {
                assert!(url_command(platform, url).is_err(), "{url}");
            }
        }
    }

    #[test]
    fn paths_with_spaces_or_leading_dashes_are_single_absolute_arguments() {
        for path in ["a folder with spaces", "-a", "--help"] {
            for (platform, program) in [
                (Platform::Windows, "explorer"), (Platform::Macos, "open"), (Platform::Linux, "xdg-open"),
            ] {
                let path = Path::new(path);
                let command = path_command(platform, path).unwrap();
                assert_eq!(command.get_program(), OsStr::new(program));
                let arguments: Vec<_> = command.get_args().collect();
                assert_eq!(arguments.len(), 1);
                assert_eq!(arguments[0], std::path::absolute(path).unwrap().as_os_str());
                assert!(Path::new(arguments[0]).is_absolute());
            }
        }
        assert!(path_command(Platform::Macos, Path::new("")).is_err());
    }
}
