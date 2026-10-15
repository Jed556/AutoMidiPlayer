use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use sysinfo::{ProcessRefreshKind, System, UpdateKind};

#[cfg(target_os = "linux")]
#[path = "process_monitor/linux.rs"]
mod platform;
#[cfg(target_os = "macos")]
#[path = "process_monitor/macos.rs"]
mod platform;
#[cfg(windows)]
#[path = "process_monitor/windows.rs"]
mod platform;
#[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
mod platform {
    pub(super) fn snapshot() -> super::WindowSnapshot {
        super::WindowSnapshot::default()
    }
    pub fn focus_window_by_pid(_: u32) -> bool {
        false
    }
}
pub use platform::focus_window_by_pid;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameProcessTarget {
    pub id: String,
    pub display_name: String,
    pub process_names: Vec<String>,
    #[serde(default)]
    pub window_names: Vec<String>,
}

impl From<&amp_core::LoadedGameBundle> for GameProcessTarget {
    fn from(bundle: &amp_core::LoadedGameBundle) -> Self {
        Self {
            id: bundle.config.id.clone(),
            display_name: bundle.config.display_name.clone(),
            process_names: bundle.config.process_names.clone(),
            window_names: bundle.config.window_names.clone(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameStatusPayload {
    pub id: String,
    pub is_running: bool,
    pub is_focused: bool,
    pub pid: Option<u32>,
}

#[derive(Default)]
struct WindowSnapshot {
    foreground_pid: Option<u32>,
    windows: Vec<GameWindow>,
}
struct GameWindow {
    pid: u32,
    title: String,
}
struct ProcessIdentity {
    pid: u32,
    names: Vec<String>,
}

pub struct ProcessMonitor {
    system: System,
    targets: Vec<GameProcessTarget>,
    locations: HashMap<String, String>,
}

impl ProcessMonitor {
    pub fn new(targets: Vec<GameProcessTarget>) -> Self {
        Self {
            system: System::new(),
            targets,
            locations: HashMap::new(),
        }
    }

    pub fn set_targets(&mut self, targets: Vec<GameProcessTarget>) {
        self.targets = targets;
    }

    pub fn set_locations(&mut self, settings: &serde_json::Value) {
        self.locations = settings
            .get("gameLocations")
            .and_then(|value| serde_json::from_value(value.clone()).ok())
            .unwrap_or_default();
    }

    pub fn check_statuses(&mut self) -> Vec<GameStatusPayload> {
        // Linux's comm name is truncated to 15 bytes. Read exe and argv[0] too
        // for long native executable names and Wine/Proton executable paths.
        self.system.refresh_processes_specifics(
            sysinfo::ProcessesToUpdate::All,
            true,
            ProcessRefreshKind::nothing()
                .with_exe(UpdateKind::OnlyIfNotSet)
                .with_cmd(UpdateKind::OnlyIfNotSet),
        );
        let processes: Vec<_> = self
            .system
            .processes()
            .iter()
            .map(|(pid, process)| {
                let mut names = vec![normalize_executable(&process.name().to_string_lossy())];
                if let Some(exe) = process.exe() {
                    names.push(normalize_executable(&exe.to_string_lossy()));
                }
                if let Some(command) = process.cmd().first() {
                    names.push(normalize_executable(&command.to_string_lossy()));
                }
                ProcessIdentity {
                    pid: pid.as_u32(),
                    names,
                }
            })
            .collect();
        let windows = platform::snapshot();
        self.targets
            .iter()
            .map(|target| {
                status_for_target(
                    target,
                    self.locations.get(&target.id).map(String::as_str),
                    &processes,
                    &windows,
                )
            })
            .collect()
    }
}

fn normalize_executable(value: &str) -> String {
    let name = value
        .trim()
        .trim_matches('"')
        .rsplit(['/', '\\'])
        .next()
        .unwrap_or("")
        .to_lowercase();
    name.strip_suffix(".exe").unwrap_or(&name).to_owned()
}

fn status_for_target(
    target: &GameProcessTarget,
    location: Option<&str>,
    processes: &[ProcessIdentity],
    windows: &WindowSnapshot,
) -> GameStatusPayload {
    let mut names: Vec<_> = target
        .process_names
        .iter()
        .map(|name| normalize_executable(name))
        .collect();
    // Old installed profiles used the Windows file description. Keep them working
    // without rewriting users' config files.
    if names.iter().any(|name| name == "roblox game client") {
        names.extend(["robloxplayerbeta".into(), "robloxplayer".into()]);
    }
    if let Some(location) = location {
        names.push(normalize_executable(location));
    }
    names.retain(|name| !name.is_empty());
    let titles: Vec<_> = target
        .window_names
        .iter()
        .map(|name| name.trim().to_lowercase())
        .filter(|name| !name.is_empty())
        .collect();
    let pid = processes
        .iter()
        .filter(|process| {
            process.names.iter().any(|name| names.contains(name))
                && (titles.is_empty()
                    || windows.windows.iter().any(|window| {
                        window.pid == process.pid
                            && titles.contains(&window.title.trim().to_lowercase())
                    }))
        })
        .map(|process| process.pid)
        // Prefer the focused instance; otherwise choose a stable PID.
        .min_by_key(|pid| (Some(*pid) != windows.foreground_pid, *pid));
    GameStatusPayload {
        id: target.id.clone(),
        is_running: pid.is_some(),
        is_focused: pid.is_some() && pid == windows.foreground_pid,
        pid,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn target(names: &[&str], titles: &[&str]) -> GameProcessTarget {
        GameProcessTarget {
            id: "game".into(),
            display_name: "Game".into(),
            process_names: names.iter().map(|s| s.to_string()).collect(),
            window_names: titles.iter().map(|s| s.to_string()).collect(),
        }
    }
    fn process(pid: u32, names: &[&str]) -> ProcessIdentity {
        ProcessIdentity {
            pid,
            names: names.iter().map(|s| normalize_executable(s)).collect(),
        }
    }
    #[test]
    fn matches_full_executables_without_prefix_collisions() {
        let target = target(&["Client-Win64-Shipping.EXE"], &[]);
        let processes = [
            process(1, &["Client-Win64-Shi", "/games/Client-Win64-Shipping.exe"]),
            process(2, &["Client-Win64-Shipping-launcher.exe"]),
        ];
        assert_eq!(
            status_for_target(&target, None, &processes, &WindowSnapshot::default()).pid,
            Some(1)
        );
        assert!(
            !status_for_target(&target, None, &processes[1..], &WindowSnapshot::default())
                .is_running
        );
        assert_eq!(
            normalize_executable(r#" "C:\Games\RobloxPlayerBeta.EXE" "#),
            "robloxplayerbeta"
        );
    }
    #[test]
    fn prefers_focused_instance_and_clears_exited_processes() {
        let target = target(&["Sky"], &[]);
        let processes = [
            process(9, &["Sky.exe"]),
            process(7, &["Sky"]),
            process(3, &["Sky"]),
        ];
        let mut windows = WindowSnapshot {
            foreground_pid: Some(7),
            ..Default::default()
        };
        let status = status_for_target(&target, None, &processes, &windows);
        assert_eq!(status.pid, Some(7));
        assert!(status.is_focused);
        windows.foreground_pid = Some(100);
        assert_eq!(
            status_for_target(&target, None, &processes, &windows).pid,
            Some(3)
        );
        assert!(!status_for_target(&target, None, &[], &windows).is_running);
    }
    #[test]
    fn titles_disambiguate_generic_launchers_and_cannot_match_other_processes() {
        let target = target(&["launcher"], &["Harry Potter: Magic Awakened"]);
        let processes = [process(1, &["launcher.exe"]), process(2, &["browser"])];
        let mut windows = WindowSnapshot::default();
        assert!(!status_for_target(&target, None, &processes, &windows).is_running);
        windows.windows.push(GameWindow {
            pid: 2,
            title: "Harry Potter: Magic Awakened".into(),
        });
        assert!(!status_for_target(&target, None, &processes, &windows).is_running);
        windows.windows.push(GameWindow {
            pid: 1,
            title: "Harry Potter: Magic Awakened".into(),
        });
        assert!(status_for_target(&target, None, &processes, &windows).is_running);
    }
    #[test]
    fn supports_custom_executables_legacy_roblox_profiles_and_empty_targets() {
        let windows = WindowSnapshot::default();
        let processes = [
            process(1, &["/Applications/Custom.app/Contents/MacOS/Custom"]),
            process(2, &["RobloxPlayerBeta.exe"]),
        ];
        assert_eq!(
            status_for_target(
                &target(&[], &[]),
                Some("/games/Custom"),
                &processes,
                &windows
            )
            .pid,
            Some(1)
        );
        assert_eq!(
            status_for_target(
                &target(&["Roblox Game Client"], &[]),
                None,
                &processes,
                &windows
            )
            .pid,
            Some(2)
        );
        assert!(
            !status_for_target(&target(&["", " "], &[]), None, &processes, &windows).is_running
        );
    }
    #[test]
    fn detects_real_process_and_clears_removed_targets_and_locations() {
        let mut monitor = ProcessMonitor::new(vec![target(&[], &[])]);
        monitor.set_locations(
            &serde_json::json!({"gameLocations": {"game": std::env::current_exe().unwrap()}}),
        );
        assert!(monitor.check_statuses()[0].is_running);
        monitor.set_locations(&serde_json::json!({}));
        assert!(!monitor.check_statuses()[0].is_running);
        monitor.set_targets(vec![]);
        assert!(monitor.check_statuses().is_empty());
    }
}
