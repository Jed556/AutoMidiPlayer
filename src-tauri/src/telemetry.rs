use crate::logging::{self, LogChannel};
use chrono::{DateTime, Utc};
use sentry::protocol::Event;
use sentry::{ClientInitGuard, ClientOptions, Hub, Level};
use serde::{Deserialize, Serialize};
use std::borrow::Cow;
use std::fs;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

pub const SENTRY_DSN: &str =
    "https://b023bd031b673f35306771bae355120d@o4511550890967040.ingest.us.sentry.io/4511550899355648";

const SENTRY_TEST_COOLDOWN_SECS: u64 = 600; // 10 minutes
pub const OPT_OUT_REFRESH_INTERVAL_DAYS: i64 = 30; // 30 days (1 month)

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct PersistentTelemetryConfig {
    pub last_opted_out_date: Option<DateTime<Utc>>,
    pub has_shown_first_launch: bool,
}

impl Default for PersistentTelemetryConfig {
    fn default() -> Self {
        Self {
            last_opted_out_date: None,
            has_shown_first_launch: true,
        }
    }
}

pub fn get_telemetry_config_path() -> PathBuf {
    logging::get_appdata_dir().join("telemetry.json")
}

pub fn load_telemetry_config() -> PersistentTelemetryConfig {
    let path = get_telemetry_config_path();
    if let Ok(contents) = fs::read_to_string(&path) {
        if let Ok(cfg) = serde_json::from_str::<PersistentTelemetryConfig>(&contents) {
            return cfg;
        }
    }
    PersistentTelemetryConfig::default()
}

pub fn save_telemetry_config(cfg: &PersistentTelemetryConfig) {
    let path = get_telemetry_config_path();
    if let Ok(json) = serde_json::to_string_pretty(cfg) {
        let _ = fs::write(path, json);
    }
}

/// Own the hub explicitly: startup and IPC commands run on different threads.
/// A thread-local hub can be empty, or still reference a client closed by opt-out.
struct TelemetryClient {
    hub: Arc<Hub>,
    guard: ClientInitGuard,
}

impl TelemetryClient {
    fn new(options: ClientOptions) -> Self {
        let hub = Arc::new(Hub::new(None, Default::default()));
        let guard = Hub::run(hub.clone(), || sentry::init(options));
        Self { hub, guard }
    }

    fn capture_message(&self, message: &str, level: Level) -> sentry::types::Uuid {
        // Give each operation its own scope while sharing the live client/session.
        Hub::new_from_top(&self.hub).capture_message(message, level)
    }
}

impl Drop for TelemetryClient {
    fn drop(&mut self) {
        self.hub.end_session();
        // The guard subsequently flushes and closes this client's transport.
    }
}

struct TelemetryState {
    client: Option<TelemetryClient>,
    last_test_sent_at: Option<Instant>,
}

static STATE: Mutex<TelemetryState> = Mutex::new(TelemetryState {
    client: None,
    last_test_sent_at: None,
});

fn get_app_version() -> &'static str {
    env!("CARGO_PKG_VERSION")
}

fn get_release_tag() -> String {
    format!("auto-midi-player@{}", get_app_version())
}

fn get_environment() -> &'static str {
    if cfg!(debug_assertions) {
        "debug"
    } else {
        "production"
    }
}

/// Checks whether an opt-out refresh is due (every 30 days)
pub fn is_opt_out_refresh_due(last_date: Option<DateTime<Utc>>) -> bool {
    match last_date {
        Some(date) => Utc::now().signed_duration_since(date).num_days() >= OPT_OUT_REFRESH_INTERVAL_DAYS,
        None => true,
    }
}

/// Initializes the telemetry service with the user's opt-in preference.
/// If opted in: starts Sentry and logs "User started app" for DAU metrics.
/// If opted out: checks persisted 30-day refresh timer. If >= 30 days have elapsed
/// since last report, sends an anonymized "User opted out of telemetry" ping to refresh active metrics.
pub fn init(telemetry_opt_in: bool) {
    let mut config = load_telemetry_config();

    if telemetry_opt_in {
        start_sentry();
        track_app_started();
    } else {
        if is_opt_out_refresh_due(config.last_opted_out_date) {
            report_opt_out();
            config.last_opted_out_date = Some(Utc::now());
            save_telemetry_config(&config);
            logging::log_step(
                "TELEMETRY",
                Some("Refreshed monthly opt-out status (30-day interval)"),
                None,
                None,
                None,
            );
        } else {
            logging::log_step(
                "TELEMETRY",
                Some("Opt-out status is within active 30-day window; no refresh needed"),
                None,
                None,
                None,
            );
        }
    }

    // Spawn background monitor to refresh opt-out status every 24 hours if the app runs continuously
    std::thread::Builder::new()
        .name("telemetry-monthly-monitor".into())
        .spawn(|| {
            loop {
                std::thread::sleep(Duration::from_secs(24 * 3600)); // check once a day
                let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
                let enabled = state.client.is_some();
                drop(state);

                if !enabled {
                    let mut cfg = load_telemetry_config();
                    if is_opt_out_refresh_due(cfg.last_opted_out_date) {
                        report_opt_out();
                        cfg.last_opted_out_date = Some(Utc::now());
                        save_telemetry_config(&cfg);
                        logging::log_step(
                            "TELEMETRY",
                            Some("Refreshed monthly opt-out status from background scheduler"),
                            None,
                            None,
                            None,
                        );
                    }
                }
            }
        })
        .ok();
}

/// Enables or disables Sentry telemetry tracking dynamically.
/// When disabling, reports the anonymized opt-out event and records the timestamp.
pub fn set_telemetry_enabled(enabled: bool) {
    let mut state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    if enabled && state.client.is_none() {
        drop(state);
        start_sentry();
        logging::log_step("TELEMETRY", Some("opted in"), None, None, None);
    } else if !enabled && state.client.is_some() {
        let client = state.client.take();
        drop(state);
        drop(client);

        report_opt_out();
        let mut config = load_telemetry_config();
        config.last_opted_out_date = Some(Utc::now());
        save_telemetry_config(&config);

        logging::log_step("TELEMETRY", Some("opted out"), None, None, None);
    }
}

/// Starts Sentry SDK with full session stability tracking and diagnostic extras
fn start_sentry() {
    let mut state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    if state.client.is_some() {
        return;
    }

    let release_str: Cow<'static, str> = Cow::Owned(get_release_tag());
    let environment_str: Cow<'static, str> = Cow::Borrowed(get_environment());

    let mut options = ClientOptions::default();
    options.dsn = SENTRY_DSN.parse().ok();
    options.release = Some(release_str);
    options.environment = Some(environment_str);
    options.auto_session_tracking = true;
    options.debug = cfg!(debug_assertions);
    options.send_default_pii = false;
    options.before_send = Some(Arc::new(|mut event: Event<'static>| {
        if event.level == Level::Error
            || event.level == Level::Fatal
            || !event.exception.is_empty()
        {
            let primary_log = logging::get_primary_log_path();
            if primary_log.exists() {
                let tail = logging::read_log_tail(&primary_log, 100);
                if !tail.is_empty() {
                    event.extra.insert("app.log".to_string(), tail.into());
                }
            }
        }
        Some(event)
    }));

    let client = TelemetryClient::new(options);

    client.hub.configure_scope(|scope| {
        scope.set_tag("Opted", "in");
        scope.set_tag("distribution", logging::get_distribution_type());
    });

    state.client = Some(client);
}

/// Sends an anonymized event reporting opt-out
fn report_opt_out() {
    let mut options = ClientOptions::default();
    options.dsn = SENTRY_DSN.parse().ok();
    options.release = None;
    options.environment = None;
    options.server_name = None;
    options.send_default_pii = false;
    options.before_send = Some(Arc::new(|mut event: Event<'static>| {
        event.contexts.clear();
        event.server_name = None;
        event.release = None;
        event.environment = None;
        event.tags.insert("Opted".to_string(), "out".to_string());
        event.tags.insert(
            "distribution".to_string(),
            logging::get_distribution_type().to_string(),
        );
        Some(event)
    }));

    // Keep the temporary opt-out client isolated from the opted-in client/hubs.
    let client = TelemetryClient::new(options);
    client.capture_message("User opted out of telemetry", Level::Info);
    client.guard.flush(Some(Duration::from_secs(2)));
}

/// Reports application startup for Daily Active User metrics
pub fn track_app_started() {
    capture_message("User started app", Level::Info);
}

/// Sends a test message with a 10-minute cooldown
pub fn send_test_message() -> Result<String, String> {
    let mut state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    let message = send_test_message_with_state(&mut state)?;
    logging::log_step("SENTRY_TEST", Some(&message), None, None, None);
    Ok(message)
}

fn send_test_message_with_state(state: &mut TelemetryState) -> Result<String, String> {
    let client = state
        .client
        .as_ref()
        .ok_or("Cannot send test event: Telemetry is currently disabled (opted out).")?;
    if !client.guard.is_enabled() {
        return Err("Cannot send test event: Sentry client is not active.".into());
    }

    if let Some(last_sent) = state.last_test_sent_at {
        let elapsed = last_sent.elapsed();
        if elapsed < Duration::from_secs(SENTRY_TEST_COOLDOWN_SECS) {
            let remaining = Duration::from_secs(SENTRY_TEST_COOLDOWN_SECS) - elapsed;
            let mins = remaining.as_secs() / 60;
            let secs = remaining.as_secs() % 60;
            return Err(format!(
                "Sentry test on cooldown. Please wait {}m {}s before sending another test message.",
                mins, secs
            ));
        }
    }

    let event_id = client.capture_message("Test Message", Level::Info);
    if event_id.is_nil() {
        return Err("Sentry did not capture the test event. No cooldown was applied.".into());
    }
    if !client.guard.flush(Some(Duration::from_secs(10))) {
        return Err(format!(
            "Sentry test is still pending after 10 seconds. Event ID: {event_id}. No cooldown was applied."
        ));
    }
    state.last_test_sent_at = Some(Instant::now());

    // SDK flush confirms queue drainage, not server acceptance or ingestion.
    let message = format!(
        "Sentry test queued and flushed. Event ID: {event_id}. Environment: {}. Check Sentry for receipt.",
        get_environment()
    );
    Ok(message)
}

/// Routes all explicit events through the live client and honors opt-out.
pub fn capture_message(message: &str, level: Level) {
    let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    if let Some(client) = &state.client {
        client.capture_message(message, level);
    }
}

/// Captures an explicit error into Sentry and local logs
pub fn capture_error(error_type: &str, message: &str, stack_trace: Option<&str>) {
    logging::log_exception(error_type, message, stack_trace, None, None, None);

    let err_msg = format!("{}: {}", error_type, message);
    capture_message(&err_msg, Level::Error);
}

/// Registers a global Rust panic hook to write to errors.log and flush Sentry
pub fn register_panic_hook() {
    let default_hook = std::panic::take_hook();

    std::panic::set_hook(Box::new(move |panic_info| {
        let payload = if let Some(s) = panic_info.payload().downcast_ref::<&str>() {
            *s
        } else if let Some(s) = panic_info.payload().downcast_ref::<String>() {
            s.as_str()
        } else {
            "Box<Any>"
        };

        let location = panic_info
            .location()
            .map(|l| format!("{}:{}:{}", l.file(), l.line(), l.column()))
            .unwrap_or_else(|| "unknown location".to_string());

        let msg = format!("PANIC at {}: {}", location, payload);
        logging::write_log_line(LogChannel::Errors, &msg, Some("panic_hook"), None, None);
        logging::write_log_line(LogChannel::App, &msg, Some("panic_hook"), None, None);

        // If sentry is enabled, capture event
        let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
        if let Some(client) = &state.client {
            client.capture_message(&msg, Level::Fatal);
            client.guard.flush(Some(Duration::from_secs(2)));
        }

        // Call the previous hook for standard stderr reporting
        default_hook(panic_info);
    }));
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicBool, Ordering};

    struct RecordingTransport {
        events: Mutex<Vec<Event<'static>>>,
        flush_succeeds: AtomicBool,
    }

    impl sentry::Transport for RecordingTransport {
        fn send_envelope(&self, envelope: sentry::Envelope) {
            if let Some(event) = envelope.event() {
                self.events.lock().unwrap().push(event.clone());
            }
        }

        fn flush(&self, _timeout: Duration) -> bool {
            self.flush_succeeds.load(Ordering::SeqCst)
        }
    }

    fn test_state(drop_events: bool) -> (TelemetryState, Arc<RecordingTransport>) {
        let transport = Arc::new(RecordingTransport {
            events: Mutex::new(Vec::new()),
            flush_succeeds: AtomicBool::new(true),
        });
        let mut options = ClientOptions::default();
        options.dsn = Some(SENTRY_DSN.parse().unwrap());
        options.environment = Some(Cow::Borrowed(get_environment()));
        options.default_integrations = false;
        options.transport = Some(Arc::new(transport.clone()));
        options.before_send =
            drop_events.then(|| Arc::new(|_: Event<'static>| None) as Arc<_>);
        let client = TelemetryClient::new(options);
        client.hub.configure_scope(|scope| scope.set_tag("Opted", "in"));
        (
            TelemetryState {
                client: Some(client),
                last_test_sent_at: None,
            },
            transport,
        )
    }

    #[test]
    fn test_message_reaches_transport_from_a_different_thread_with_no_current_client() {
        let (mut state, transport) = std::thread::spawn(|| test_state(false)).join().unwrap();
        let command = std::thread::spawn(move || {
            Hub::run(Arc::new(Hub::new(None, Default::default())), || {
                assert!(Hub::current().client().is_none());
                let result = send_test_message_with_state(&mut state).unwrap();
                assert!(result.contains("Event ID:"));
                assert!(result.contains(get_environment()));
                assert!(state.last_test_sent_at.is_some());
                assert!(send_test_message_with_state(&mut state).unwrap_err().contains("cooldown"));
                assert!(Hub::current().client().is_none());
            });
        });
        command.join().unwrap();
        let events = transport.events.lock().unwrap();
        assert_eq!(events.len(), 1);
        assert_eq!(events[0].message.as_deref(), Some("Test Message"));
        assert_eq!(events[0].tags.get("Opted").map(String::as_str), Some("in"));
    }

    #[test]
    fn test_dropped_event_does_not_report_success_or_start_cooldown() {
        let (mut state, transport) = test_state(true);
        assert!(send_test_message_with_state(&mut state).unwrap_err().contains("did not capture"));
        assert!(state.last_test_sent_at.is_none());
        assert!(transport.events.lock().unwrap().is_empty());
    }

    #[test]
    fn test_flush_timeout_does_not_report_success_or_start_cooldown() {
        let (mut state, transport) = test_state(false);
        transport.flush_succeeds.store(false, Ordering::SeqCst);
        assert!(send_test_message_with_state(&mut state).unwrap_err().contains("still pending"));
        assert!(state.last_test_sent_at.is_none());
        transport.flush_succeeds.store(true, Ordering::SeqCst);
        assert!(send_test_message_with_state(&mut state).is_ok());
    }

    #[test]
    fn test_opt_out_and_reenable_use_the_live_client() {
        let (mut state, old_transport) = test_state(false);
        drop(state.client.take());
        assert!(send_test_message_with_state(&mut state).unwrap_err().contains("disabled"));
        let (replacement, new_transport) = test_state(false);
        state.client = replacement.client;
        assert!(send_test_message_with_state(&mut state).is_ok());
        assert!(old_transport.events.lock().unwrap().is_empty());
        assert_eq!(new_transport.events.lock().unwrap().len(), 1);
    }

    #[test]
    fn test_temporary_client_does_not_replace_the_current_hub_client() {
        let (state, _) = test_state(false);
        let hub = state.client.as_ref().unwrap().hub.clone();
        Hub::run(hub, || {
            let original = Hub::current().client().unwrap();
            let (temporary, _) = test_state(false);
            drop(temporary);
            assert!(Arc::ptr_eq(&original, &Hub::current().client().unwrap()));
        });
    }

    #[test]
    fn test_release_tag_format() {
        let tag = get_release_tag();
        assert!(tag.starts_with("auto-midi-player@"));
        assert!(tag.contains('.'));
    }

    #[test]
    fn test_dsn_validity() {
        assert!(SENTRY_DSN.starts_with("https://"));
        assert!(SENTRY_DSN.contains("@"));
        assert!(SENTRY_DSN.contains("sentry.io"));
    }

    #[test]
    fn test_environment() {
        let env = get_environment();
        assert!(env == "debug" || env == "production");
    }

    #[test]
    fn test_is_opt_out_refresh_due() {
        assert!(is_opt_out_refresh_due(None));

        let now = Utc::now();
        let ten_days_ago = now - chrono::Duration::days(10);
        let twenty_nine_days_ago = now - chrono::Duration::days(29);
        let thirty_days_ago = now - chrono::Duration::days(30);
        let sixty_days_ago = now - chrono::Duration::days(60);

        assert!(!is_opt_out_refresh_due(Some(ten_days_ago)));
        assert!(!is_opt_out_refresh_due(Some(twenty_nine_days_ago)));
        assert!(is_opt_out_refresh_due(Some(thirty_days_ago)));
        assert!(is_opt_out_refresh_due(Some(sixty_days_ago)));
    }

    #[test]
    fn test_persistent_telemetry_config_serde() {
        let temp_dir = std::env::temp_dir().join("amp_telemetry_test");
        let _ = fs::create_dir_all(&temp_dir);
        let config_path = temp_dir.join("telemetry.json");

        let cfg = PersistentTelemetryConfig {
            last_opted_out_date: Some(Utc::now()),
            has_shown_first_launch: true,
        };

        let json = serde_json::to_string(&cfg).unwrap();
        fs::write(&config_path, &json).unwrap();

        let loaded: PersistentTelemetryConfig =
            serde_json::from_str(&fs::read_to_string(&config_path).unwrap()).unwrap();
        assert_eq!(loaded.has_shown_first_launch, true);
        assert!(loaded.last_opted_out_date.is_some());

        let _ = fs::remove_dir_all(temp_dir);
    }
}
