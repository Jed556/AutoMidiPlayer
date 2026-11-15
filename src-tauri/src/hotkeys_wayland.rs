//! Global shortcuts on native Wayland, using the compositor's permission and
//! binding UI. No XWayland grabs or keyboard hooks are used.
use ashpd::desktop::global_shortcuts::{GlobalShortcuts, NewShortcut, Shortcut};
use futures_util::StreamExt;
use std::{
    collections::HashSet,
    future::Future,
    sync::{
        atomic::{AtomicU64, Ordering},
        mpsc, Arc, Mutex,
    },
    time::Duration,
};
use tauri::{AppHandle, Emitter, Manager};

#[derive(Clone, Debug, PartialEq, Eq)]
struct Binding {
    action: String,
    description: String,
    trigger: String,
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct BoundTrigger {
    action: String,
    trigger: String,
}

struct Configuration {
    bindings: Vec<Binding>,
    replies: Vec<mpsc::Sender<Result<(), String>>>,
    generation: u64,
    latest: Arc<AtomicU64>,
}
enum Command {
    Configure(Configuration),
    Shutdown,
}

pub(crate) struct Service {
    tx: tokio::sync::mpsc::UnboundedSender<Command>,
    bindings: Arc<Mutex<Vec<BoundTrigger>>>,
    latest: Arc<AtomicU64>,
}

pub(crate) fn install(app: &AppHandle) -> Result<(), String> {
    let (tx, rx) = tokio::sync::mpsc::unbounded_channel();
    let handle = app.clone();
    let bindings = Arc::new(Mutex::new(Vec::new()));
    let shared = bindings.clone();
    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .map_err(|error| error.to_string())?;
    std::thread::Builder::new()
        .name("wayland-hotkeys".into())
        .spawn(move || runtime.block_on(run(handle, rx, shared)))
        .map_err(|error| error.to_string())?;
    app.manage(Service {
        tx,
        bindings,
        latest: Arc::new(AtomicU64::new(0)),
    });
    Ok(())
}

pub(crate) fn shutdown(app: &AppHandle) {
    if let Some(service) = app.try_state::<Service>() {
        let _ = service.tx.send(Command::Shutdown);
    }
}

impl Service {
    pub(crate) fn bindings(&self) -> Vec<BoundTrigger> {
        self.bindings
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone()
    }
    /// Called from a blocking task; the permission dialog never stalls Tauri's
    /// event loop. A later disable or edit replaces a pending configuration.
    pub(crate) fn apply(
        &self,
        enabled: bool,
        bindings: &[crate::HotkeyBindingPayload],
        generation: u64,
    ) -> Result<(), String> {
        let mut configured = Vec::new();
        let mut triggers = HashSet::new();
        let mut actions = HashSet::new();
        if enabled {
            for binding in bindings {
                let Some(accelerator) = crate::format_shortcut_string(binding) else {
                    continue;
                };
                let shortcut = accelerator
                    .parse()
                    .map_err(|error| format!("Invalid hotkey '{accelerator}': {error}"))?;
                let trigger = crate::hotkey_format::portal_trigger(&shortcut)?;
                if !triggers.insert(trigger.clone()) || !actions.insert(binding.action.clone()) {
                    return Err(format!(
                        "Hotkey '{accelerator}' or action '{}' is assigned more than once.",
                        binding.action
                    ));
                }
                configured.push(Binding {
                    action: binding.action.clone(),
                    description: binding
                        .label
                        .clone()
                        .unwrap_or_else(|| binding.action.clone()),
                    trigger,
                });
            }
        }
        let (reply, result) = mpsc::channel();
        self.latest.fetch_max(generation, Ordering::AcqRel);
        self.tx
            .send(Command::Configure(Configuration {
                bindings: configured,
                replies: vec![reply],
                generation,
                latest: self.latest.clone(),
            }))
            .map_err(|_| "The Wayland global shortcut service has stopped.".to_owned())?;
        result
            .recv()
            .map_err(|_| "The Wayland global shortcut service has stopped.".to_owned())?
    }
}

fn finish(configuration: &mut Configuration, result: Result<(), String>) {
    for reply in configuration.replies.drain(..) {
        let _ = reply.send(result.clone());
    }
}

enum Outcome<T> {
    Ready(Result<T, String>),
    Replaced(Option<Command>),
}

/// Keep one pending portal request alive for repeated identical settings syncs,
/// while still allowing Disable and a different binding to cancel its dialog.
async fn wait_or_replace<T>(
    future: impl Future<Output = Result<T, String>>,
    configuration: &mut Configuration,
    rx: &mut tokio::sync::mpsc::UnboundedReceiver<Command>,
) -> Outcome<T> {
    tokio::pin!(future);
    loop {
        tokio::select! {
            biased;
            next = rx.recv() => match next {
                Some(Command::Configure(mut next)) if next.generation < configuration.latest.load(Ordering::Acquire) => finish(&mut next, Ok(())),
                Some(Command::Configure(mut next)) if next.bindings == configuration.bindings => {
                    configuration.generation = configuration.generation.max(next.generation);
                    configuration.replies.append(&mut next.replies);
                }
                Some(Command::Configure(mut next)) if next.generation < configuration.generation => finish(&mut next, Ok(())),
                next => {
                    // Superseded work is successful cancellation, not a failure
                    // of the user's newer configuration.
                    finish(configuration, Ok(()));
                    return Outcome::Replaced(next);
                }
            },
            result = &mut future => {
                if configuration.generation < configuration.latest.load(Ordering::Acquire) {
                    finish(configuration, Ok(()));
                    return Outcome::Replaced(rx.recv().await);
                }
                return Outcome::Ready(result);
            },
        }
    }
}

fn bound_triggers(shortcuts: &[Shortcut]) -> Vec<BoundTrigger> {
    shortcuts
        .iter()
        .map(|shortcut| BoundTrigger {
            action: shortcut.id().to_owned(),
            trigger: shortcut.trigger_description().to_owned(),
        })
        .collect()
}

fn publish_bindings(app: &AppHandle, shared: &Mutex<Vec<BoundTrigger>>, bindings: &[BoundTrigger]) {
    *shared.lock().unwrap_or_else(|error| error.into_inner()) = bindings.to_vec();
    let _ = app.emit("hotkeys:bindings_changed", bindings);
}

async fn run(
    app: AppHandle,
    mut rx: tokio::sync::mpsc::UnboundedReceiver<Command>,
    shared: Arc<Mutex<Vec<BoundTrigger>>>,
) {
    let mut next = rx.recv().await;
    while let Some(Command::Configure(mut configuration)) = next.take() {
        if configuration.generation < configuration.latest.load(Ordering::Acquire) {
            finish(&mut configuration, Ok(()));
            next = rx.recv().await;
            continue;
        }
        if configuration.bindings.is_empty() {
            publish_bindings(&app, &shared, &[]);
            finish(&mut configuration, Ok(()));
            next = rx.recv().await;
            continue;
        }
        let portal = match wait_or_replace(async {
            GlobalShortcuts::new().await.map_err(|error| format!("Wayland global shortcuts are unavailable: {error}. Check your desktop's GlobalShortcuts portal support."))
        }, &mut configuration, &mut rx).await {
            Outcome::Ready(Ok(portal)) => portal,
            Outcome::Ready(Err(error)) => { finish(&mut configuration, Err(error)); next = rx.recv().await; continue; }
            Outcome::Replaced(command) => { next = command; continue; }
        };
        let session = match wait_or_replace(
            async {
                portal
                    .create_session(Default::default())
                    .await
                    .map_err(|error| error.to_string())
            },
            &mut configuration,
            &mut rx,
        )
        .await
        {
            Outcome::Ready(Ok(session)) => session,
            Outcome::Ready(Err(error)) => {
                finish(&mut configuration, Err(error));
                next = rx.recv().await;
                continue;
            }
            Outcome::Replaced(command) => {
                next = command;
                continue;
            }
        };
        // Session's path accessor is internal to ashpd; its Serialize contract
        // is the public D-Bus object path used by activation/change signals.
        let Some(session_handle) = serde_json::to_value(&session)
            .ok()
            .and_then(|value| value.as_str().map(str::to_owned))
        else {
            finish(
                &mut configuration,
                Err("Unable to identify the Wayland global shortcut session.".into()),
            );
            let _ = tokio::time::timeout(Duration::from_secs(1), session.close()).await;
            next = rx.recv().await;
            continue;
        };
        let shortcuts: Vec<_> = configuration
            .bindings
            .iter()
            .map(|binding| {
                NewShortcut::new(&binding.action, &binding.description)
                    .preferred_trigger(Some(binding.trigger.as_str()))
            })
            .collect();
        let bound = wait_or_replace(
            async {
                portal
                    .bind_shortcuts(&session, &shortcuts, None, Default::default())
                    .await
                    .map_err(|error| error.to_string())?
                    .response()
                    .map_err(|error| error.to_string())
            },
            &mut configuration,
            &mut rx,
        )
        .await;
        let bound = match bound {
            Outcome::Ready(Ok(bound)) => bound,
            Outcome::Ready(Err(error)) => {
                finish(
                    &mut configuration,
                    Err(format!(
                        "Wayland global shortcut permission failed: {error}"
                    )),
                );
                let _ = tokio::time::timeout(Duration::from_secs(1), session.close()).await;
                next = rx.recv().await;
                continue;
            }
            Outcome::Replaced(command) => {
                let _ = tokio::time::timeout(Duration::from_secs(1), session.close()).await;
                next = command;
                continue;
            }
        };
        if !configuration.bindings.iter().all(|binding| {
            bound
                .shortcuts()
                .iter()
                .any(|shortcut| shortcut.id() == binding.action && !shortcut.trigger_description().trim().is_empty())
        }) {
            finish(&mut configuration, Err("The desktop did not grant every requested hotkey. Enable the missing shortcuts in the desktop permission dialog and retry.".into()));
            let _ = tokio::time::timeout(Duration::from_secs(1), session.close()).await;
            next = rx.recv().await;
            continue;
        }
        let activated = portal.receive_activated().await;
        let changed = portal.receive_shortcuts_changed().await;
        let closed = session.receive_closed().await;
        match (activated, changed, closed) {
            (Ok(mut activated), Ok(mut changed), Ok(mut closed)) => {
                let mut current_triggers = bound_triggers(bound.shortcuts());
                publish_bindings(&app, &shared, &current_triggers);
                finish(&mut configuration, Ok(()));
                loop {
                    tokio::select! {
                        biased;
                        command = rx.recv() => {
                            if let Some(Command::Configure(mut identical)) = command {
                                if identical.generation < configuration.latest.load(Ordering::Acquire) {
                                    finish(&mut identical, Ok(()));
                                    continue;
                                }
                                if identical.bindings == configuration.bindings {
                                    configuration.generation = configuration.generation.max(identical.generation);
                                    publish_bindings(&app, &shared, &current_triggers);
                                    finish(&mut identical, Ok(()));
                                    continue;
                                }
                                next = Some(Command::Configure(identical));
                            } else { next = command; }
                            break;
                        }
                        event = activated.next() => {
                            let Some(event) = event else {
                                let _ = app.emit("hotkeys:error", "Wayland global shortcut connection closed. Re-enable hotkeys to reconnect.");
                                next = None; break;
                            };
                            if event.session_handle().as_str() == session_handle
                                && current_triggers.iter().any(|binding| binding.action == event.shortcut_id()) {
                                if let Some(state) = app.try_state::<crate::AppState>() {
                                    crate::dispatch_hotkey(&app, &state.engine, event.shortcut_id());
                                }
                            }
                        }
                        event = changed.next() => {
                            if let Some(event) = event {
                                if event.session_handle().as_str() == session_handle {
                                    current_triggers = bound_triggers(event.shortcuts());
                                    publish_bindings(&app, &shared, &current_triggers);
                                }
                            } else {
                                let _ = app.emit("hotkeys:error", "Wayland global shortcut connection closed. Re-enable hotkeys to reconnect.");
                                next = None; break;
                            }
                        }
                        _ = closed.next() => {
                            publish_bindings(&app, &shared, &[]);
                            let _ = app.emit("hotkeys:error", "Wayland global shortcut permission was revoked. Re-enable hotkeys to reconnect.");
                            next = None; break;
                        }
                    }
                }
            }
            _ => {
                finish(
                    &mut configuration,
                    Err("Unable to listen for Wayland global shortcut events.".into()),
                );
                next = None;
            }
        }
        let _ = tokio::time::timeout(Duration::from_secs(1), session.close()).await;
        publish_bindings(&app, &shared, &[]);
        if next.is_none() {
            next = rx.recv().await;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn configuration(trigger: &str) -> (Configuration, mpsc::Receiver<Result<(), String>>) {
        let (reply, result) = mpsc::channel();
        (
            Configuration {
                bindings: vec![Binding {
                    action: "PlayPause".into(),
                    description: "Play / Pause".into(),
                    trigger: trigger.into(),
                }],
                replies: vec![reply],
                generation: 1,
                latest: Arc::new(AtomicU64::new(1)),
            },
            result,
        )
    }

    #[test]
    fn identical_configs_coalesce_and_disable_cancels_pending_authorization() {
        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        runtime.block_on(async {
            let (mut original, first) = configuration("CTRL+space");
            let (duplicate, second) = configuration("CTRL+space");
            let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel();
            tx.send(Command::Configure(duplicate)).unwrap();
            tx.send(Command::Configure(Configuration { bindings: vec![], replies: vec![], generation: 2, latest: original.latest.clone() })).unwrap();
            let result = wait_or_replace(std::future::pending::<Result<(), String>>(), &mut original, &mut rx).await;
            assert!(matches!(result, Outcome::Replaced(Some(Command::Configure(config))) if config.bindings.is_empty()));
            assert_eq!(first.recv().unwrap(), Ok(()));
            assert_eq!(second.recv().unwrap(), Ok(()));
        });
    }

    #[test]
    fn out_of_order_configurations_cannot_restore_old_bindings() {
        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        runtime.block_on(async {
            let (mut current, current_result) = configuration("CTRL+space");
            current.generation = 3;
            current.latest.store(3, Ordering::Release);
            let (mut stale, stale_result) = configuration("ALT+space");
            stale.generation = 2;
            let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel();
            tx.send(Command::Configure(stale)).unwrap();
            tx.send(Command::Configure(Configuration {
                bindings: vec![], replies: vec![], generation: 4, latest: current.latest.clone(),
            })).unwrap();
            let result = wait_or_replace(std::future::pending::<Result<(), String>>(), &mut current, &mut rx).await;
            assert!(matches!(result, Outcome::Replaced(Some(Command::Configure(config))) if config.generation == 4));
            assert_eq!(stale_result.recv().unwrap(), Ok(()));
            assert_eq!(current_result.recv().unwrap(), Ok(()));
        });
    }

    #[test]
    fn late_permission_result_is_discarded_after_a_new_request() {
        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        runtime.block_on(async {
            let (mut current, result) = configuration("CTRL+space");
            current.latest.store(2, Ordering::Release);
            let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel();
            let latest = current.latest.clone();
            tokio::spawn(async move {
                tokio::task::yield_now().await;
                let _ = tx.send(Command::Configure(Configuration { bindings: vec![], replies: vec![], generation: 2, latest }));
            });
            let outcome = wait_or_replace(std::future::ready(Ok("late permission grant")), &mut current, &mut rx).await;
            assert!(matches!(outcome, Outcome::Replaced(Some(Command::Configure(config))) if config.generation == 2));
            assert_eq!(result.recv().unwrap(), Ok(()));
        });
    }
}
