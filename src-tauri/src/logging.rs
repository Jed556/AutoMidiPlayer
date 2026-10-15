use chrono::Local;
use log::{Level, LevelFilter, Metadata, Record, SetLoggerError};
use std::fs::{self, OpenOptions};
use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU8, Ordering};
use std::sync::Mutex;

pub const ERRORS_ONLY_VERBOSITY: u8 = 0;
pub const WARNINGS_AND_ERRORS_VERBOSITY: u8 = 1;
pub const ALL_STEPS_VERBOSITY: u8 = 2;

static VERBOSITY: AtomicU8 = AtomicU8::new(ALL_STEPS_VERBOSITY);
static DEBUG_MODE: AtomicBool = AtomicBool::new(cfg!(debug_assertions));
static FILE_LOCK: Mutex<()> = Mutex::new(());

const ERROR_KEYWORDS: &[&str] = &[
    "error",
    "exception",
    "failed",
    "unable",
    "could not",
    "fatal",
    "panic",
];

const WARNING_KEYWORDS: &[&str] = &[
    "warning",
    "not ready",
    "fallback",
    "missing",
];

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LogChannel {
    App,
    Errors,
    MidiParser,
    Playback,
    Scheduler,
    InputOutput,
    Mapping,
    Performance,
}

impl LogChannel {
    pub fn file_name(&self) -> &'static str {
        match self {
            LogChannel::App => "app.log",
            LogChannel::Errors => "errors.log",
            LogChannel::MidiParser => "midi-parser.log",
            LogChannel::Playback => "playback.log",
            LogChannel::Scheduler => "scheduler.log",
            LogChannel::InputOutput => "input-output.log",
            LogChannel::Mapping => "mapping.log",
            LogChannel::Performance => "performance.log",
        }
    }

    pub fn from_name(name: &str) -> Self {
        match name.to_lowercase().as_str() {
            "errors" | "error" | "crash" => LogChannel::Errors,
            "midi" | "midiparser" | "midi-parser" | "parser" => LogChannel::MidiParser,
            "playback" | "play" => LogChannel::Playback,
            "scheduler" => LogChannel::Scheduler,
            "input" | "inputoutput" | "input-output" | "io" => LogChannel::InputOutput,
            "mapping" => LogChannel::Mapping,
            "performance" | "perf" => LogChannel::Performance,
            _ => LogChannel::App,
        }
    }
}

/// Resolves the app data base directory: %LocalAppData%\AutoMidiPlayer
pub fn get_appdata_dir() -> PathBuf {
    amp_core::paths::app_data_dir()
}

/// Returns the logs directory: %LocalAppData%\AutoMidiPlayer\logs
pub fn get_logs_dir() -> PathBuf {
    let dir = get_appdata_dir().join("logs");
    let _ = fs::create_dir_all(&dir);
    dir
}

/// Primary app log path: %LocalAppData%\AutoMidiPlayer\logs\app.log
pub fn get_primary_log_path() -> PathBuf {
    get_logs_dir().join(LogChannel::App.file_name())
}

/// Errors log path: %LocalAppData%\AutoMidiPlayer\logs\errors.log
pub fn get_errors_log_path() -> PathBuf {
    get_logs_dir().join(LogChannel::Errors.file_name())
}

/// Returns the application distribution type ("development", "net-install", or "portable")
pub fn get_distribution_type() -> &'static str {
    if cfg!(debug_assertions) {
        "development"
    } else {
        // Check if running from an installed directory vs portable
        if let Ok(current_exe) = std::env::current_exe() {
            if let Some(parent) = current_exe.parent() {
                if parent.join("resources").exists() || parent.join("unins000.exe").exists() {
                    return "net-install";
                }
            }
        }
        "portable"
    }
}

/// Sets the current crash log verbosity level (0 = ErrorsOnly, 1 = WarningsAndErrors, 2 = AllSteps)
pub fn set_verbosity(verbosity: u8) {
    let clamped = verbosity.clamp(ERRORS_ONLY_VERBOSITY, ALL_STEPS_VERBOSITY);
    VERBOSITY.store(clamped, Ordering::SeqCst);
}

/// Sets whether debug mode is enabled
pub fn set_debug_mode(enabled: bool) {
    DEBUG_MODE.store(enabled, Ordering::SeqCst);
}

/// Returns the active verbosity level. Outside debug mode, forces full diagnostics (AllSteps)
pub fn get_effective_verbosity() -> u8 {
    if !DEBUG_MODE.load(Ordering::Relaxed) {
        ALL_STEPS_VERBOSITY
    } else {
        VERBOSITY.load(Ordering::Relaxed)
    }
}

fn contains_any(source: &str, keywords: &[&str]) -> bool {
    let lower = source.to_lowercase();
    keywords.iter().any(|&k| lower.contains(k))
}

fn should_write_message(message: &str, verbosity: u8) -> bool {
    match verbosity {
        v if v >= ALL_STEPS_VERBOSITY => true,
        v if v <= ERRORS_ONLY_VERBOSITY => contains_any(message, ERROR_KEYWORDS),
        _ => contains_any(message, ERROR_KEYWORDS) || contains_any(message, WARNING_KEYWORDS),
    }
}

fn resolve_step_channel(step: &str) -> LogChannel {
    let upper = step.to_uppercase();
    if upper.contains("MIDI_")
        || upper.contains("TRACK")
        || upper.contains("TEMPO")
        || upper.contains("PARSE")
    {
        LogChannel::MidiParser
    } else if upper.contains("PLAYBACK")
        || upper.contains("LISTEN_MODE")
        || upper.contains("GAME_FOCUS")
        || upper.contains("SEEK")
    {
        LogChannel::Playback
    } else if upper.contains("SCHEDULER") || upper.contains("TICK") {
        LogChannel::Scheduler
    } else if upper.contains("MAPPING") {
        LogChannel::Mapping
    } else if upper.contains("PERF") {
        LogChannel::Performance
    } else if upper.contains("INPUT") || upper.contains("KEY_") || upper.contains("MOUSE") {
        LogChannel::InputOutput
    } else {
        LogChannel::App
    }
}

/// Writes a formatted line to the given channel file
pub fn write_log_line(
    channel: LogChannel,
    message: &str,
    caller: Option<&str>,
    file: Option<&str>,
    line: Option<u32>,
) {
    let time_str = Local::now().format("%H:%M:%S%.3f").to_string();
    let file_display = file
        .and_then(|f| Path::new(f).file_name()?.to_str())
        .unwrap_or("unknown");
    let line_num = line.unwrap_or(0);
    let caller_str = caller.unwrap_or("app");

    let formatted = format!(
        "[{}] [{}:{}] [{}] {}\n",
        time_str, file_display, line_num, caller_str, message
    );

    // Also write to stdout/stderr in debug mode or console
    if cfg!(debug_assertions) {
        if channel == LogChannel::Errors {
            eprint!("{}", formatted);
        } else {
            print!("{}", formatted);
        }
    }

    let logs_dir = get_logs_dir();
    let file_path = logs_dir.join(channel.file_name());

    let _guard = FILE_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(&file_path) {
        let _ = f.write_all(formatted.as_bytes());
    }
}

/// General log to app.log, filtered by verbosity
pub fn log(
    message: &str,
    caller: Option<&str>,
    file: Option<&str>,
    line: Option<u32>,
) {
    if !should_write_message(message, get_effective_verbosity()) {
        return;
    }
    write_log_line(LogChannel::App, message, caller, file, line);
}

/// Explicit error log to errors.log and app.log
pub fn log_error(
    message: &str,
    caller: Option<&str>,
    file: Option<&str>,
    line: Option<u32>,
) {
    write_log_line(LogChannel::Errors, message, caller, file, line);
    write_log_line(LogChannel::App, message, caller, file, line);
}

/// Logs an exception with stack trace to errors.log and app.log
pub fn log_exception(
    error_type: &str,
    message: &str,
    stack_trace: Option<&str>,
    caller: Option<&str>,
    file: Option<&str>,
    line: Option<u32>,
) {
    let msg = format!("EXCEPTION: {}: {}", error_type, message);
    write_log_line(LogChannel::Errors, &msg, caller, file, line);
    write_log_line(LogChannel::App, &msg, caller, file, line);

    if let Some(st) = stack_trace {
        write_log_line(LogChannel::Errors, st, caller, file, line);
        write_log_line(LogChannel::App, st, caller, file, line);
    }
}

/// Logs a diagnostic step routed to its respective channel
pub fn log_step(
    step: &str,
    details: Option<&str>,
    caller: Option<&str>,
    file: Option<&str>,
    line: Option<u32>,
) {
    if get_effective_verbosity() < ALL_STEPS_VERBOSITY {
        return;
    }

    let msg = match details {
        Some(d) if !d.trim().is_empty() => format!("STEP: {} | {}", step, d),
        _ => format!("STEP: {}", step),
    };

    let channel = resolve_step_channel(step);
    write_log_line(channel, &msg, caller, file, line);
}

/// Logs UI page navigation visits: PAGE_VISIT: page={page_name} | source={source}
pub fn log_page_visit(
    page_name: &str,
    source: Option<&str>,
    caller: Option<&str>,
    file: Option<&str>,
    line: Option<u32>,
) {
    if get_effective_verbosity() < ALL_STEPS_VERBOSITY {
        return;
    }

    let details = match source {
        Some(src) if !src.trim().is_empty() => format!("page={} | source={}", page_name, src),
        _ => format!("page={}", page_name),
    };

    let msg = format!("PAGE_VISIT: {}", details);
    write_log_line(LogChannel::App, &msg, caller, file, line);
}

/// Logs startup banner and environment distribution info
pub fn log_startup(
    product_name: &str,
    app_version_display: &str,
    caller: Option<&str>,
    file: Option<&str>,
    line: Option<u32>,
) {
    let msg = format!("{} v{} Starting", product_name, app_version_display);
    write_log_line(LogChannel::App, &msg, caller, file, line);

    let dist_msg = format!("Distribution: {}", get_distribution_type());
    write_log_line(LogChannel::App, &dist_msg, caller, file, line);
}

/// Deletes all channel log files in %LocalAppData%\AutoMidiPlayer\logs
pub fn clear_logs() {
    let _guard = FILE_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let logs_dir = get_logs_dir();

    let channels = [
        LogChannel::App,
        LogChannel::Errors,
        LogChannel::MidiParser,
        LogChannel::Playback,
        LogChannel::Scheduler,
        LogChannel::InputOutput,
        LogChannel::Mapping,
        LogChannel::Performance,
    ];

    for ch in channels {
        let p = logs_dir.join(ch.file_name());
        let _ = fs::remove_file(p);
    }
}

/// Reads the last `max_lines` from the specified log file or channel
pub fn read_log_tail(path: &Path, max_lines: usize) -> String {
    if !path.exists() {
        return String::new();
    }

    let file = match OpenOptions::new().read(true).open(path) {
        Ok(f) => f,
        Err(_) => return "Unable to read log file.".to_string(),
    };

    let reader = BufReader::new(file);
    let mut lines = Vec::new();

    for line in reader.lines() {
        if let Ok(l) = line {
            lines.push(l);
            if lines.len() > max_lines * 2 {
                lines.drain(0..max_lines);
            }
        }
    }

    let start = lines.len().saturating_sub(max_lines);
    lines[start..].join("\n")
}

/// Opens the logs directory in Windows Explorer (or default file manager)
pub fn open_logs_folder() -> Result<(), String> {
    let logs_dir = get_logs_dir();
    #[cfg(windows)]
    {
        std::process::Command::new("explorer")
            .arg(logs_dir.as_os_str())
            .spawn()
            .map_err(|e| format!("Failed to open logs directory in explorer: {}", e))?;
        Ok(())
    }
    #[cfg(not(windows))]
    {
        std::process::Command::new("xdg-open")
            .arg(logs_dir.as_os_str())
            .spawn()
            .map_err(|e| format!("Failed to open logs directory: {}", e))?;
        Ok(())
    }
}

/// Custom log::Log implementation that dispatches to our multi-channel file logger
pub struct AmpLogger;

impl log::Log for AmpLogger {
    fn enabled(&self, metadata: &Metadata) -> bool {
        let verbosity = get_effective_verbosity();
        match metadata.level() {
            Level::Error => true,
            Level::Warn => verbosity >= WARNINGS_AND_ERRORS_VERBOSITY,
            _ => verbosity >= ALL_STEPS_VERBOSITY,
        }
    }

    fn log(&self, record: &Record) {
        if !self.enabled(record.metadata()) {
            return;
        }

        let target = record.target();
        let channel = if target.starts_with("amp_core::midi") {
            LogChannel::MidiParser
        } else if target.starts_with("amp_core::player") || target.starts_with("amp_core::audio") {
            LogChannel::Playback
        } else if target.starts_with("amp_core::input") {
            LogChannel::InputOutput
        } else {
            LogChannel::App
        };

        let message = format!("{}", record.args());

        if record.level() == Level::Error {
            write_log_line(
                LogChannel::Errors,
                &message,
                Some(record.target()),
                record.file(),
                record.line(),
            );
        }

        write_log_line(
            channel,
            &message,
            Some(record.target()),
            record.file(),
            record.line(),
        );
    }

    fn flush(&self) {}
}

static LOGGER_INSTANCE: AmpLogger = AmpLogger;

/// Initializes the global logger
pub fn init_logger(verbosity: u8, debug_mode: bool) -> Result<(), SetLoggerError> {
    set_verbosity(verbosity);
    set_debug_mode(debug_mode);

    log::set_logger(&LOGGER_INSTANCE)?;
    log::set_max_level(LevelFilter::Debug);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_channel_resolution() {
        assert_eq!(resolve_step_channel("MIDI_PARSE_HEADER"), LogChannel::MidiParser);
        assert_eq!(resolve_step_channel("TRACK_CHUNK"), LogChannel::MidiParser);
        assert_eq!(resolve_step_channel("PLAYBACK_START"), LogChannel::Playback);
        assert_eq!(resolve_step_channel("GAME_FOCUS_LOST"), LogChannel::Playback);
        assert_eq!(resolve_step_channel("SEEK_OFFSET"), LogChannel::Playback);
        assert_eq!(resolve_step_channel("SCHEDULER_TICK"), LogChannel::Scheduler);
        assert_eq!(resolve_step_channel("MAPPING_RESOLVE"), LogChannel::Mapping);
        assert_eq!(resolve_step_channel("PERF_STATS"), LogChannel::Performance);
        assert_eq!(resolve_step_channel("INPUT_KEY_DOWN"), LogChannel::InputOutput);
        assert_eq!(resolve_step_channel("CUSTOM_EVENT"), LogChannel::App);
    }

    #[test]
    fn test_log_channel_filenames() {
        assert_eq!(LogChannel::App.file_name(), "app.log");
        assert_eq!(LogChannel::Errors.file_name(), "errors.log");
        assert_eq!(LogChannel::MidiParser.file_name(), "midi-parser.log");
        assert_eq!(LogChannel::Playback.file_name(), "playback.log");
        assert_eq!(LogChannel::Scheduler.file_name(), "scheduler.log");
        assert_eq!(LogChannel::InputOutput.file_name(), "input-output.log");
        assert_eq!(LogChannel::Mapping.file_name(), "mapping.log");
        assert_eq!(LogChannel::Performance.file_name(), "performance.log");
    }

    #[test]
    fn test_should_write_message_verbosity() {
        // Errors only (0)
        assert!(should_write_message("Failed to open file", ERRORS_ONLY_VERBOSITY));
        assert!(should_write_message("Fatal crash encountered", ERRORS_ONLY_VERBOSITY));
        assert!(!should_write_message("Everything normal", ERRORS_ONLY_VERBOSITY));
        assert!(!should_write_message("Missing fallback warning", ERRORS_ONLY_VERBOSITY));

        // Warnings and errors (1)
        assert!(should_write_message("Failed to open file", WARNINGS_AND_ERRORS_VERBOSITY));
        assert!(should_write_message("Missing fallback warning", WARNINGS_AND_ERRORS_VERBOSITY));
        assert!(!should_write_message("Everything normal", WARNINGS_AND_ERRORS_VERBOSITY));

        // All steps (2)
        assert!(should_write_message("Everything normal", ALL_STEPS_VERBOSITY));
    }

    #[test]
    fn test_read_log_tail() {
        let temp_dir = std::env::temp_dir().join("amp_log_test");
        let _ = fs::create_dir_all(&temp_dir);
        let log_file = temp_dir.join("test_tail.log");

        let content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5\n";
        fs::write(&log_file, content).unwrap();

        let tail = read_log_tail(&log_file, 3);
        assert_eq!(tail, "Line 3\nLine 4\nLine 5");

        let tail_all = read_log_tail(&log_file, 10);
        assert_eq!(tail_all, "Line 1\nLine 2\nLine 3\nLine 4\nLine 5");

        let _ = fs::remove_dir_all(temp_dir);
    }
}
