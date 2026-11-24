use crate::logging::{self, LogChannel};
use chrono::{DateTime, Utc};
use sentry::protocol::Event;
use sentry::{ClientInitGuard, ClientOptions, Hub, Level, Scope, User};
use serde::{Deserialize, Serialize};
use std::borrow::Cow;
use std::fs;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
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
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub anonymous_id: Option<String>,
}

impl Default for PersistentTelemetryConfig {
    fn default() -> Self {
        Self {
            last_opted_out_date: None,
            has_shown_first_launch: true,
            anonymous_id: None,
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
    if let Some(parent) = path.parent() {
        let _ = fs::create_dir_all(parent);
    }
    if let Ok(json) = serde_json::to_string_pretty(cfg) {
        let _ = fs::write(path, json);
    }
}

fn ensure_anonymous_id_with(
    load_config: impl FnOnce() -> PersistentTelemetryConfig,
    save_config: impl FnOnce(&PersistentTelemetryConfig),
) -> String {
    let mut config = load_config();
    if let Some(existing) = config
        .anonymous_id
        .as_deref()
        .map(str::trim)
        .filter(|id| !id.is_empty())
    {
        return existing.to_string();
    }

    let generated = sentry::types::random_uuid().to_string();
    config.anonymous_id = Some(generated.clone());
    save_config(&config);
    generated
}

fn get_or_create_anonymous_id() -> String {
    ensure_anonymous_id_with(load_telemetry_config, save_telemetry_config)
}

/// Own the hub explicitly: startup and IPC commands run on different threads.
/// A thread-local hub can be empty, or still reference a client closed by opt-out.
struct TelemetryClient {
    hub: Arc<Hub>,
    guard: ClientInitGuard,
    anonymous_user_id: Option<String>,
}

impl TelemetryClient {
    fn new(options: ClientOptions, anonymous_user_id: Option<String>) -> Self {
        // Populate the scope before `sentry::init` so auto-started Release Health sessions capture `distinct_id`.
        let mut scope = Scope::default();
        if let Some(user_id) = anonymous_user_id.clone() {
            scope.set_user(Some(User {
                id: Some(user_id),
                ..Default::default()
            }));
        }
        let hub = Arc::new(Hub::new(None, Arc::new(scope)));
        let guard = Hub::run(hub.clone(), || sentry::init(options));
        Self {
            hub,
            guard,
            anonymous_user_id,
        }
    }

    fn capture_message(&self, message: &str, level: Level) -> sentry::types::Uuid {
        // Give each operation its own scope while sharing the live client/session.
        Hub::new_from_top(&self.hub).capture_message(&crate::log_privacy::redact_paths(message), level)
    }

    fn capture_metric<M: sentry::metrics::IntoProtocolMetric>(&self, metric: M) {
        Hub::new_from_top(&self.hub).capture_metric(metric);
    }

    fn track_app_started(&self) {
        let mut metric = sentry::metrics::counter("app.started", 1)
            .attribute("opted", "in")
            .attribute("distribution", logging::get_distribution_type())
            .attribute("os.name", std::env::consts::OS);
        if let Some(user_id) = &self.anonymous_user_id {
            metric = metric.attribute("user.id", user_id.clone());
        }
        self.capture_metric(metric);
    }

    fn track_playback_started(&self, event: &PlaybackMetricEvent) {
        let mut metric = sentry::metrics::counter("playback.started", 1)
            .attribute("game", event.game.clone())
            .attribute("instrument", event.instrument.clone())
            .attribute("mode", event.mode)
            .attribute("opted", "in")
            .attribute("distribution", logging::get_distribution_type())
            .attribute("os.name", std::env::consts::OS);
        if let Some(user_id) = &self.anonymous_user_id {
            metric = metric.attribute("user.id", user_id.clone());
            if let Some(song_key) = event.song_key.as_deref().filter(|s| !s.is_empty()) {
                metric = metric.attribute("song.id", salted_song_id(user_id, song_key));
            }
        }
        self.capture_metric(metric);
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PlaybackMetricEvent {
    pub game: String,
    pub instrument: String,
    pub mode: &'static str,
    pub song_key: Option<String>,
}

#[derive(Default)]
pub struct PlaybackTelemetryTracker {
    active_game: Option<String>,
    active_instrument: Option<String>,
    listen_mode: bool,
    current_song_key: Option<String>,
    is_playing: bool,
    recorded_in_run: std::collections::HashSet<(String, String, &'static str)>,
}

impl PlaybackTelemetryTracker {
    pub fn new(initial_listen_mode: bool) -> Self {
        Self {
            listen_mode: initial_listen_mode,
            ..Default::default()
        }
    }

    pub fn set_target(
        &mut self,
        game: Option<String>,
        instrument: Option<String>,
    ) -> Option<PlaybackMetricEvent> {
        self.active_game = game;
        self.active_instrument = instrument;
        if self.is_playing {
            self.maybe_record()
        } else {
            None
        }
    }

    pub fn set_listen_mode(&mut self, enabled: bool) -> Option<PlaybackMetricEvent> {
        self.listen_mode = enabled;
        if self.is_playing {
            self.maybe_record()
        } else {
            None
        }
    }

    pub fn on_song_loaded(&mut self, song_key: String) -> Option<PlaybackMetricEvent> {
        self.current_song_key = Some(song_key);
        self.recorded_in_run.clear();
        if self.is_playing {
            self.maybe_record()
        } else {
            None
        }
    }

    pub fn on_playing(&mut self) -> Option<PlaybackMetricEvent> {
        self.is_playing = true;
        self.maybe_record()
    }

    pub fn on_paused(&mut self) {
        self.is_playing = false;
    }

    pub fn on_stopped(&mut self) {
        self.is_playing = false;
        self.recorded_in_run.clear();
    }

    pub fn on_finished(&mut self) {
        self.is_playing = false;
        self.recorded_in_run.clear();
    }

    fn maybe_record(&mut self) -> Option<PlaybackMetricEvent> {
        let game = self
            .active_game
            .clone()
            .unwrap_or_else(|| "unknown".to_string());
        let instrument = self
            .active_instrument
            .clone()
            .unwrap_or_else(|| "unknown".to_string());
        let mode = if self.listen_mode { "listen" } else { "game" };
        if self
            .recorded_in_run
            .insert((game.clone(), instrument.clone(), mode))
        {
            Some(PlaybackMetricEvent {
                game,
                instrument,
                mode,
                song_key: self.current_song_key.clone(),
            })
        } else {
            None
        }
    }
}

fn salted_song_id(user_id: &str, song_key: &str) -> String {
    const OFFSET: u128 = 0x6c62272e07bb014262b821756295c58d;
    const PRIME: u128 = 0x0000000001000000000000000000013b;
    let mut hash = OFFSET;
    for &byte in user_id
        .as_bytes()
        .iter()
        .chain(std::iter::once(&0u8))
        .chain(song_key.as_bytes().iter())
    {
        hash ^= u128::from(byte);
        hash = hash.wrapping_mul(PRIME);
    }
    format!("{hash:032x}")
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
static MONITOR_STARTED: AtomicBool = AtomicBool::new(false);

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

fn report_opt_out_if_due() -> bool {
    let mut config = load_telemetry_config();
    if is_opt_out_refresh_due(config.last_opted_out_date) {
        report_opt_out();
        config.last_opted_out_date = Some(Utc::now());
        save_telemetry_config(&config);
        true
    } else {
        false
    }
}

fn ensure_monthly_monitor_started() {
    if MONITOR_STARTED
        .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
        .is_err()
    {
        return;
    }

    // Spawn background monitor to refresh opt-out status every 24 hours if the app runs continuously
    if std::thread::Builder::new()
        .name("telemetry-monthly-monitor".into())
        .spawn(|| {
            loop {
                std::thread::sleep(Duration::from_secs(24 * 3600)); // check once a day
                let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
                let enabled = state.client.is_some();
                drop(state);

                if !enabled && report_opt_out_if_due() {
                    logging::log_step(
                        "TELEMETRY",
                        Some("Refreshed monthly opt-out status from background scheduler"),
                        None,
                        None,
                        None,
                    );
                }
            }
        })
        .is_err()
    {
        MONITOR_STARTED.store(false, Ordering::Release);
    }
}

/// Initializes the telemetry service with the user's opt-in preference.
/// If opted in: starts Sentry and records the `app.started` metric.
/// If opted out: checks persisted 30-day refresh timer. If >= 30 days have elapsed
/// since last report, sends an anonymized `telemetry.opted_out` metric ping to refresh active metrics.
pub fn init(telemetry_opt_in: bool) {
    if telemetry_opt_in {
        start_sentry();
        track_app_started();
    } else if report_opt_out_if_due() {
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

    ensure_monthly_monitor_started();
}

fn disable_telemetry_with(
    state: &Mutex<TelemetryState>,
    load_config: impl FnOnce() -> PersistentTelemetryConfig,
    save_config: impl FnOnce(&PersistentTelemetryConfig),
    send_opt_out: impl FnOnce(),
) -> bool {
    let client = state.lock().unwrap_or_else(|e| e.into_inner()).client.take();
    let was_enabled = client.is_some();
    drop(client);

    let mut config = load_config();
    if !was_enabled && !is_opt_out_refresh_due(config.last_opted_out_date) {
        return false;
    }

    send_opt_out();
    config.last_opted_out_date = Some(Utc::now());
    save_config(&config);
    true
}

/// Enables or disables Sentry telemetry tracking dynamically.
/// When disabling, reports the anonymized opt-out event and records the timestamp.
pub fn set_telemetry_enabled(enabled: bool) {
    if enabled {
        let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
        if state.client.is_none() {
            drop(state);
            start_sentry();
            ensure_monthly_monitor_started();
            logging::log_step("TELEMETRY", Some("opted in"), None, None, None);
        }
    } else {
        if disable_telemetry_with(
            &STATE,
            load_telemetry_config,
            save_telemetry_config,
            report_opt_out,
        ) {
            logging::log_step("TELEMETRY", Some("opted out"), None, None, None);
        }
        ensure_monthly_monitor_started();
    }
}

/// Return to first-run setup without sending an opt-out refresh ping.
pub fn suspend_for_setup() {
    let client = STATE.lock().unwrap_or_else(|e| e.into_inner()).client.take();
    drop(client);
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

    let anonymous_id = get_or_create_anonymous_id();
    let client = TelemetryClient::new(options, Some(anonymous_id));

    client.hub.configure_scope(|scope| {
        scope.set_tag("Opted", "in");
        scope.set_tag("distribution", logging::get_distribution_type());
    });

    state.client = Some(client);
}

fn build_opt_out_options() -> ClientOptions {
    let environment_str: Cow<'static, str> = Cow::Borrowed(get_environment());
    let mut options = ClientOptions::default();
    options.dsn = SENTRY_DSN.parse().ok();
    options.release = None;
    options.environment = Some(environment_str);
    options.server_name = None;
    options.default_integrations = false;
    options.auto_session_tracking = false;
    options.send_default_pii = false;
    options.before_send_metric = Some(Arc::new(|mut metric: sentry::protocol::Metric| {
        metric.attributes.remove("server.address");
        metric.attributes.remove("sentry.release");
        metric.attributes.remove("user.id");
        metric.attributes.remove("user.name");
        metric.attributes.remove("user.email");
        metric.attributes.insert("opted".into(), "out".into());
        metric.attributes.insert(
            "distribution".into(),
            logging::get_distribution_type().into(),
        );
        Some(metric)
    }));
    options
}

fn send_opt_out_with_options(options: ClientOptions) {
    // Keep the temporary opt-out client isolated from the opted-in client/hubs.
    let client = TelemetryClient::new(options, None);
    client.capture_metric(sentry::metrics::counter("telemetry.opted_out", 1));
    client.guard.flush(Some(Duration::from_secs(2)));
}

/// Sends an anonymized metric reporting opt-out
fn report_opt_out() {
    send_opt_out_with_options(build_opt_out_options());
}

/// Reports application startup as a Sentry counter metric
pub fn track_app_started() {
    let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    if let Some(client) = &state.client {
        client.track_app_started();
    }
}

/// Reports a song play as a Sentry counter metric
pub fn track_playback_started(event: &PlaybackMetricEvent) {
    let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    if let Some(client) = &state.client {
        client.track_playback_started(event);
    }
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
        sessions: Mutex<Vec<sentry::protocol::SessionUpdate<'static>>>,
        metrics: Mutex<Vec<sentry::protocol::Metric>>,
        flush_succeeds: AtomicBool,
    }

    impl RecordingTransport {
        fn new() -> Arc<Self> {
            Arc::new(Self {
                events: Mutex::new(Vec::new()),
                sessions: Mutex::new(Vec::new()),
                metrics: Mutex::new(Vec::new()),
                flush_succeeds: AtomicBool::new(true),
            })
        }
    }

    impl sentry::Transport for RecordingTransport {
        fn send_envelope(&self, envelope: sentry::Envelope) {
            for item in envelope.items() {
                match item {
                    sentry::protocol::EnvelopeItem::Event(event) => {
                        self.events.lock().unwrap().push((**event).clone());
                    }
                    sentry::protocol::EnvelopeItem::SessionUpdate(session) => {
                        self.sessions.lock().unwrap().push(session.clone());
                    }
                    sentry::protocol::EnvelopeItem::ItemContainer(
                        sentry::protocol::ItemContainer::Metrics(metrics),
                    ) => {
                        self.metrics.lock().unwrap().extend(metrics.iter().cloned());
                    }
                    _ => {}
                }
            }
        }

        fn flush(&self, _timeout: Duration) -> bool {
            self.flush_succeeds.load(Ordering::SeqCst)
        }
    }

    fn test_state(drop_events: bool) -> (TelemetryState, Arc<RecordingTransport>) {
        let transport = RecordingTransport::new();
        let mut options = ClientOptions::default();
        options.dsn = Some(SENTRY_DSN.parse().unwrap());
        options.environment = Some(Cow::Borrowed(get_environment()));
        options.default_integrations = false;
        options.transport = Some(Arc::new(transport.clone()));
        options.before_send =
            drop_events.then(|| Arc::new(|_: Event<'static>| None) as Arc<_>);
        let client = TelemetryClient::new(options, Some("test-anonymous-id".to_string()));
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
    fn diagnostic_messages_are_redacted_before_reaching_transport() {
        let (state, transport) = test_state(false);
        state.client.as_ref().unwrap().capture_message(
            r"Could not read C:\Users\Jane Smith\Music\song.mid",
            Level::Error,
        );
        let events = transport.events.lock().unwrap();
        assert_eq!(events.len(), 1);
        assert_eq!(events[0].message.as_deref(), Some(r"Could not read %USERPROFILE%\Music\song.mid"));
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
            anonymous_id: Some("anon-id-123".to_string()),
        };

        let json = serde_json::to_string(&cfg).unwrap();
        fs::write(&config_path, &json).unwrap();

        let loaded: PersistentTelemetryConfig =
            serde_json::from_str(&fs::read_to_string(&config_path).unwrap()).unwrap();
        assert_eq!(loaded.has_shown_first_launch, true);
        assert!(loaded.last_opted_out_date.is_some());
        assert_eq!(loaded.anonymous_id.as_deref(), Some("anon-id-123"));

        let without_id = PersistentTelemetryConfig::default();
        let without_id_json = serde_json::to_string(&without_id).unwrap();
        assert!(!without_id_json.contains("anonymous_id"));

        let legacy_json =
            r#"{"last_opted_out_date":"2026-11-17T15:54:48.680689Z","has_shown_first_launch":true}"#;
        let legacy_loaded: PersistentTelemetryConfig = serde_json::from_str(legacy_json).unwrap();
        assert!(legacy_loaded.last_opted_out_date.is_some());
        assert!(legacy_loaded.has_shown_first_launch);
        assert!(legacy_loaded.anonymous_id.is_none());

        let _ = fs::remove_dir_all(temp_dir);
    }

    #[test]
    fn opt_out_metric_preserves_environment_and_strips_identifying_metadata() {
        let transport = RecordingTransport::new();
        let mut options = build_opt_out_options();
        options.transport = Some(Arc::new(transport.clone()));

        send_opt_out_with_options(options);

        assert!(transport.events.lock().unwrap().is_empty());
        assert!(transport.sessions.lock().unwrap().is_empty());

        let metrics = transport.metrics.lock().unwrap();
        assert_eq!(metrics.len(), 1);
        assert_eq!(metrics[0].name.as_ref(), "telemetry.opted_out");
        assert_eq!(metrics[0].r#type, sentry::protocol::MetricType::Counter);
        assert_eq!(metrics[0].value, 1.0);
        assert_eq!(
            metrics[0]
                .attributes
                .get("sentry.environment")
                .and_then(|attr| attr.0.as_str()),
            Some(get_environment())
        );
        assert_eq!(
            metrics[0]
                .attributes
                .get("opted")
                .and_then(|attr| attr.0.as_str()),
            Some("out")
        );
        assert_eq!(
            metrics[0]
                .attributes
                .get("distribution")
                .and_then(|attr| attr.0.as_str()),
            Some(logging::get_distribution_type())
        );
        assert!(!metrics[0].attributes.contains_key("sentry.release"));
        assert!(!metrics[0].attributes.contains_key("server.address"));
        assert!(!metrics[0].attributes.contains_key("user.id"));
        assert!(!metrics[0].attributes.contains_key("user.name"));
        assert!(!metrics[0].attributes.contains_key("user.email"));
    }

    #[test]
    fn app_started_sends_counter_metric_without_creating_issue_event() {
        let (state, transport) = test_state(false);
        let client = state.client.as_ref().unwrap();
        client.track_app_started();
        assert!(client.guard.flush(Some(Duration::from_secs(2))));

        assert!(transport.events.lock().unwrap().is_empty());

        let metrics = transport.metrics.lock().unwrap();
        assert_eq!(metrics.len(), 1);
        assert_eq!(metrics[0].name.as_ref(), "app.started");
        assert_eq!(metrics[0].r#type, sentry::protocol::MetricType::Counter);
        assert_eq!(metrics[0].value, 1.0);
        assert_eq!(
            metrics[0]
                .attributes
                .get("opted")
                .and_then(|attr| attr.0.as_str()),
            Some("in")
        );
        assert_eq!(
            metrics[0]
                .attributes
                .get("distribution")
                .and_then(|attr| attr.0.as_str()),
            Some(logging::get_distribution_type())
        );
        assert_eq!(
            metrics[0]
                .attributes
                .get("os.name")
                .and_then(|attr| attr.0.as_str()),
            Some(std::env::consts::OS)
        );
        assert_eq!(
            metrics[0]
                .attributes
                .get("user.id")
                .and_then(|attr| attr.0.as_str()),
            Some("test-anonymous-id")
        );
        assert_eq!(
            metrics[0]
                .attributes
                .get("sentry.environment")
                .and_then(|attr| attr.0.as_str()),
            Some(get_environment())
        );
    }

    #[test]
    fn playback_started_sends_counter_metric_with_game_instrument_mode_and_ids() {
        let (state, transport) = test_state(false);
        let client = state.client.as_ref().unwrap();

        let game_event = PlaybackMetricEvent {
            game: "Genshin Impact".to_string(),
            instrument: "windsong-lyre".to_string(),
            mode: "game",
            song_key: Some("song-abc".to_string()),
        };
        let listen_event = PlaybackMetricEvent {
            game: "Sky".to_string(),
            instrument: "piano".to_string(),
            mode: "listen",
            song_key: Some("song-xyz".to_string()),
        };

        client.track_playback_started(&game_event);
        client.track_playback_started(&game_event);
        client.track_playback_started(&listen_event);
        assert!(client.guard.flush(Some(Duration::from_secs(2))));

        assert!(transport.events.lock().unwrap().is_empty());

        let metrics = transport.metrics.lock().unwrap();
        assert_eq!(metrics.len(), 3);

        for m in metrics.iter() {
            assert_eq!(m.name.as_ref(), "playback.started");
            assert_eq!(m.r#type, sentry::protocol::MetricType::Counter);
            assert_eq!(m.value, 1.0);
            assert_eq!(
                m.attributes.get("opted").and_then(|a| a.0.as_str()),
                Some("in")
            );
            assert_eq!(
                m.attributes.get("distribution").and_then(|a| a.0.as_str()),
                Some(logging::get_distribution_type())
            );
            assert_eq!(
                m.attributes.get("os.name").and_then(|a| a.0.as_str()),
                Some(std::env::consts::OS)
            );
            assert_eq!(
                m.attributes.get("user.id").and_then(|a| a.0.as_str()),
                Some("test-anonymous-id")
            );
            assert_eq!(
                m.attributes
                    .get("sentry.environment")
                    .and_then(|a| a.0.as_str()),
                Some(get_environment())
            );
        }

        assert_eq!(
            metrics[0].attributes.get("game").and_then(|a| a.0.as_str()),
            Some("Genshin Impact")
        );
        assert_eq!(
            metrics[0]
                .attributes
                .get("instrument")
                .and_then(|a| a.0.as_str()),
            Some("windsong-lyre")
        );
        assert_eq!(
            metrics[0].attributes.get("mode").and_then(|a| a.0.as_str()),
            Some("game")
        );

        let song_id_0 = metrics[0]
            .attributes
            .get("song.id")
            .and_then(|a| a.0.as_str())
            .expect("song.id should be present");
        let song_id_1 = metrics[1]
            .attributes
            .get("song.id")
            .and_then(|a| a.0.as_str())
            .expect("song.id should be present");
        let song_id_2 = metrics[2]
            .attributes
            .get("song.id")
            .and_then(|a| a.0.as_str())
            .expect("song.id should be present");
        assert!(!song_id_0.is_empty());
        assert_ne!(song_id_0, "song-abc");
        assert_eq!(song_id_0, song_id_1);
        assert_ne!(song_id_0, song_id_2);

        assert_eq!(
            metrics[2].attributes.get("game").and_then(|a| a.0.as_str()),
            Some("Sky")
        );
        assert_eq!(
            metrics[2]
                .attributes
                .get("instrument")
                .and_then(|a| a.0.as_str()),
            Some("piano")
        );
        assert_eq!(
            metrics[2].attributes.get("mode").and_then(|a| a.0.as_str()),
            Some("listen")
        );
    }

    #[test]
    fn playback_tracker_deduplicates_pause_resume_and_tracks_mode_and_song_changes() {
        let mut tracker = PlaybackTelemetryTracker::new(false);
        assert!(tracker
            .set_target(
                Some("Genshin Impact".to_string()),
                Some("windsong-lyre".to_string())
            )
            .is_none());
        assert!(tracker.on_song_loaded("song-1".to_string()).is_none());

        let play_ev = tracker.on_playing().expect("initial play should emit");
        assert_eq!(
            play_ev,
            PlaybackMetricEvent {
                game: "Genshin Impact".to_string(),
                instrument: "windsong-lyre".to_string(),
                mode: "game",
                song_key: Some("song-1".to_string()),
            }
        );

        // Pause -> resume does not double-count.
        tracker.on_paused();
        assert!(tracker.on_playing().is_none());

        // Switching to listen mode mid-play records a listen-mode play, toggling back in same run does not re-emit.
        let listen_ev = tracker
            .set_listen_mode(true)
            .expect("listen mode switch while playing should emit");
        assert_eq!(listen_ev.mode, "listen");
        assert!(tracker.set_listen_mode(false).is_none());

        // Stop resets the run so next play emits again.
        tracker.on_stopped();
        assert!(tracker.on_playing().is_some());

        // Finish also resets the run.
        tracker.on_finished();
        assert!(tracker.on_playing().is_some());

        // Loading a new song while already playing emits immediately for the new song.
        let skip_ev = tracker
            .on_song_loaded("song-2".to_string())
            .expect("song change while playing should emit");
        assert_eq!(skip_ev.song_key.as_deref(), Some("song-2"));

        // Loading a song while stopped defers emission until play.
        tracker.on_stopped();
        assert!(tracker.on_song_loaded("song-3".to_string()).is_none());
        let deferred_ev = tracker.on_playing().expect("deferred play should emit");
        assert_eq!(deferred_ev.song_key.as_deref(), Some("song-3"));
    }

    #[test]
    fn ensure_anonymous_id_generates_and_reuses_uuid_v4() {
        let stored = Mutex::new(PersistentTelemetryConfig::default());
        let mut save_count = 0usize;

        let first_id = ensure_anonymous_id_with(
            || stored.lock().unwrap().clone(),
            |cfg| {
                *stored.lock().unwrap() = cfg.clone();
                save_count += 1;
            },
        );
        let parsed = sentry::types::Uuid::parse_str(&first_id).expect("valid UUID");
        assert_eq!(parsed.get_version(), Some(sentry::types::UuidVersion::Random));
        assert_eq!(save_count, 1);
        assert_eq!(stored.lock().unwrap().anonymous_id.as_deref(), Some(first_id.as_str()));

        let reused_id = ensure_anonymous_id_with(
            || stored.lock().unwrap().clone(),
            |cfg| {
                *stored.lock().unwrap() = cfg.clone();
                save_count += 1;
            },
        );
        assert_eq!(reused_id, first_id);
        assert_eq!(save_count, 1);

        stored.lock().unwrap().anonymous_id = Some("   ".to_string());
        let regenerated_id = ensure_anonymous_id_with(
            || stored.lock().unwrap().clone(),
            |cfg| {
                *stored.lock().unwrap() = cfg.clone();
                save_count += 1;
            },
        );
        let parsed_regenerated = sentry::types::Uuid::parse_str(&regenerated_id).expect("valid UUID");
        assert_eq!(
            parsed_regenerated.get_version(),
            Some(sentry::types::UuidVersion::Random)
        );
        assert_eq!(save_count, 2);
    }

    #[test]
    fn opted_in_client_attaches_anonymous_user_id_without_pii() {
        let (state, transport) = test_state(false);
        state
            .client
            .as_ref()
            .unwrap()
            .capture_message("Unhandled playback error", Level::Error);

        let events = transport.events.lock().unwrap();
        assert_eq!(events.len(), 1);
        let user = events[0].user.as_ref().expect("user context should be set");
        assert_eq!(user.id.as_deref(), Some("test-anonymous-id"));
        assert!(user.email.is_none());
        assert!(user.username.is_none());
        assert!(user.ip_address.is_none());
    }

    #[test]
    fn opted_in_client_attaches_distinct_id_to_auto_session() {
        let transport = RecordingTransport::new();
        let mut options = ClientOptions::default();
        options.dsn = Some(SENTRY_DSN.parse().unwrap());
        options.release = Some(Cow::Borrowed("auto-midi-player@test"));
        options.environment = Some(Cow::Borrowed(get_environment()));
        options.auto_session_tracking = true;
        options.default_integrations = false;
        options.send_default_pii = false;
        options.transport = Some(Arc::new(transport.clone()));

        let client = TelemetryClient::new(options, Some("test-anonymous-id".to_string()));
        drop(client);

        let sessions = transport.sessions.lock().unwrap();
        assert!(!sessions.is_empty());
        assert!(sessions
            .iter()
            .all(|s| s.distinct_id.as_deref() == Some("test-anonymous-id")));
    }

    #[test]
    fn disable_telemetry_reports_first_opt_out_and_skips_duplicate_within_30_days() {
        let state = Mutex::new(TelemetryState {
            client: None,
            last_test_sent_at: None,
        });
        let stored = Mutex::new(PersistentTelemetryConfig::default());
        let mut opt_out_count = 0usize;

        // First-run onboarding opt-out: no live client yet, no prior opt-out timestamp.
        let reported = disable_telemetry_with(
            &state,
            || stored.lock().unwrap().clone(),
            |cfg| *stored.lock().unwrap() = cfg.clone(),
            || opt_out_count += 1,
        );
        assert!(reported);
        assert_eq!(opt_out_count, 1);
        assert!(stored.lock().unwrap().last_opted_out_date.is_some());

        // Subsequent startup sync within 30 days should not send a duplicate opt-out event.
        let reported_again = disable_telemetry_with(
            &state,
            || stored.lock().unwrap().clone(),
            |cfg| *stored.lock().unwrap() = cfg.clone(),
            || opt_out_count += 1,
        );
        assert!(!reported_again);
        assert_eq!(opt_out_count, 1);

        // Explicitly toggling telemetry off from an active client always reports opt-out.
        let (active_state, _) = test_state(false);
        *state.lock().unwrap() = active_state;
        let reported_toggle = disable_telemetry_with(
            &state,
            || stored.lock().unwrap().clone(),
            |cfg| *stored.lock().unwrap() = cfg.clone(),
            || opt_out_count += 1,
        );
        assert!(reported_toggle);
        assert_eq!(opt_out_count, 2);
        assert!(state.lock().unwrap().client.is_none());
    }
}
