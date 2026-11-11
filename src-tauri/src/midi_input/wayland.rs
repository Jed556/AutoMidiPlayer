use amp_core::{input::simulator::KeyboardOutput, OutputKey};
use ashpd::desktop::{
    remote_desktop::{DeviceType, KeyState, RemoteDesktop, SelectDevicesOptions},
    PersistMode,
};
use futures_util::StreamExt;
use std::collections::HashSet;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    mpsc, Arc, Mutex,
};
use std::time::Duration;

struct Request {
    key: OutputKey,
    up: bool,
    reply: mpsc::Sender<Result<(), String>>,
}
struct PortalKeyboard {
    tx: tokio::sync::mpsc::Sender<Request>,
    error: Arc<Mutex<Option<String>>>,
}
impl KeyboardOutput for PortalKeyboard {
    fn key(&mut self, key: OutputKey, up: bool) -> Result<(), String> {
        if let Some(error) = self.error() {
            return Err(error);
        }
        let (reply, response) = mpsc::channel();
        self.tx
            .try_send(Request { key, up, reply })
            .map_err(|e| e.to_string())?;
        response
            .recv_timeout(Duration::from_millis(500))
            .map_err(|_| "Wayland keyboard output timed out.".to_owned())?
    }
    fn error(&self) -> Option<String> {
        self.error.lock().unwrap_or_else(|e| e.into_inner()).clone()
    }
}

/// Keep portal IO on its own runtime; neither MIDI callbacks nor the Tauri UI
/// thread wait on a desktop permission prompt. Dropping the output ends it.
pub(super) fn connect(cancel: Arc<AtomicBool>) -> Result<Box<dyn KeyboardOutput>, String> {
    let (ready, response) = mpsc::channel();
    std::thread::Builder::new().name("wayland-midi-keyboard".into()).spawn(move || {
        let result = (|| {
            let runtime = tokio::runtime::Builder::new_current_thread().enable_all().build().map_err(|e| e.to_string())?;
            runtime.block_on(async {
                let portal = RemoteDesktop::new().await.map_err(|e| format!("Wayland keyboard portal unavailable: {e}. Use Listen Mode or an X11 session."))?;
                let session = portal.create_session(Default::default()).await.map_err(|e| e.to_string())?;
                let authorization = async {
                    portal.select_devices(&session, SelectDevicesOptions::default()
                        .set_devices(DeviceType::KEYBOARD).set_persist_mode(PersistMode::DoNot))
                        .await.map_err(|e| e.to_string())?.response().map_err(|e| e.to_string())?;
                    let devices = portal.start(&session, None, Default::default()).await
                        .map_err(|e| e.to_string())?.response().map_err(|e| e.to_string())?;
                    if !devices.devices().contains(DeviceType::KEYBOARD) { return Err("Keyboard access was not granted.".to_owned()); }
                    Ok(())
                };
                let authorized = tokio::select! {
                    result = authorization => result,
                    _ = async { while !cancel.load(Ordering::Acquire) { tokio::time::sleep(Duration::from_millis(50)).await; } } => Err("MIDI input authorization cancelled.".into()),
                };
                if let Err(error) = authorized { let _ = session.close().await; return Err(error); }
                let mut closed = session.receive_closed().await.map_err(|e| e.to_string())?;
                let (tx, mut rx) = tokio::sync::mpsc::channel::<Request>(64);
                let error = Arc::new(Mutex::new(None));
                let output = PortalKeyboard { tx, error: error.clone() };
                if ready.send(Ok(Box::new(output) as Box<dyn KeyboardOutput>)).is_err() {
                    let _ = session.close().await; return Ok(());
                }
                let mut held = HashSet::<i32>::new();
                let failure = loop {
                    tokio::select! {
                        _ = closed.next() => break Some("Wayland keyboard access was revoked. Start MIDI Input to reconnect.".to_owned()),
                        request = rx.recv() => {
                            let Some(request) = request else { break None };
                            let symbol = xkeysym::Keysym::from(request.key).raw() as i32;
                            let result = tokio::time::timeout(Duration::from_millis(400), portal.notify_keyboard_keysym(
                                &session, symbol, if request.up { KeyState::Released } else { KeyState::Pressed }, Default::default()))
                                .await.map_err(|_| "Wayland keyboard output timed out.".to_owned())
                                .and_then(|r| r.map_err(|e| e.to_string()));
                            if result.is_ok() { if request.up { held.remove(&symbol); } else { held.insert(symbol); } }
                            let failure = result.as_ref().err().cloned();
                            let _ = request.reply.send(result);
                            if failure.is_some() { break failure; }
                        }
                    }
                };
                *error.lock().unwrap() = failure;
                for key in held {
                    let _ = tokio::time::timeout(Duration::from_millis(100), portal.notify_keyboard_keysym(
                        &session, key, KeyState::Released, Default::default())).await;
                }
                let _ = session.close().await;
                Ok(())
            })
        })();
        if let Err(error) = result { let _ = ready.send(Err(error)); }
    }).map_err(|e| e.to_string())?;
    response.recv().map_err(|e| e.to_string())?
}
