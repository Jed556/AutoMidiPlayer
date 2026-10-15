use super::{GameWindow, WindowSnapshot};
use x11rb::{
    connection::Connection,
    protocol::xproto::{
        AtomEnum, ClientMessageEvent, ConnectionExt, EventMask, Window, CLIENT_MESSAGE_EVENT,
    },
    rust_connection::RustConnection,
};

fn atom(connection: &RustConnection, name: &[u8]) -> Option<u32> {
    Some(connection.intern_atom(false, name).ok()?.reply().ok()?.atom)
}

fn property(connection: &RustConnection, window: Window, name: &[u8]) -> Option<Vec<u32>> {
    connection
        .get_property(
            false,
            window,
            atom(connection, name)?,
            AtomEnum::ANY,
            0,
            65536,
        )
        .ok()?
        .reply()
        .ok()?
        .value32()
        .map(|values| values.collect())
}

fn window_ids(connection: &RustConnection, root: Window) -> Vec<u32> {
    property(connection, root, b"_NET_CLIENT_LIST").unwrap_or_default()
}

fn window_pid(connection: &RustConnection, window: Window) -> Option<u32> {
    property(connection, window, b"_NET_WM_PID")?
        .first()
        .copied()
        .filter(|pid| *pid != 0)
}

pub(super) fn snapshot() -> WindowSnapshot {
    let mut snapshot = WindowSnapshot::default();
    let Ok((connection, screen)) = x11rb::connect(None) else {
        return snapshot;
    };
    let root = connection.setup().roots[screen].root;
    // XWayland may retain a stale active-window property when a native Wayland
    // window has focus. Do not report that as current foreground focus.
    if std::env::var_os("WAYLAND_DISPLAY").is_none() {
        snapshot.foreground_pid = property(&connection, root, b"_NET_ACTIVE_WINDOW")
            .and_then(|ids| ids.first().copied())
            .and_then(|id| window_pid(&connection, id));
    }
    for window in window_ids(&connection, root) {
        let Some(pid) = window_pid(&connection, window) else {
            continue;
        };
        let title = [b"_NET_WM_NAME".as_slice(), b"WM_NAME".as_slice()]
            .iter()
            .find_map(|name| {
                let reply = connection
                    .get_property(
                        false,
                        window,
                        atom(&connection, name)?,
                        AtomEnum::ANY,
                        0,
                        4096,
                    )
                    .ok()?
                    .reply()
                    .ok()?;
                (!reply.value.is_empty())
                    .then(|| String::from_utf8_lossy(&reply.value).into_owned())
            });
        if let Some(title) = title {
            snapshot.windows.push(GameWindow { pid, title });
        }
    }
    snapshot
}

pub fn focus_window_by_pid(pid: u32) -> bool {
    if pid == 0 || std::env::var_os("WAYLAND_DISPLAY").is_some() {
        return false;
    }
    let Ok((connection, screen)) = x11rb::connect(None) else {
        return false;
    };
    let root = connection.setup().roots[screen].root;
    let Some(window) = window_ids(&connection, root)
        .into_iter()
        .find(|window| window_pid(&connection, *window) == Some(pid))
    else {
        return false;
    };
    let Some(active_atom) = atom(&connection, b"_NET_ACTIVE_WINDOW") else {
        return false;
    };
    let event = ClientMessageEvent {
        response_type: CLIENT_MESSAGE_EVENT,
        format: 32,
        sequence: 0,
        window,
        type_: active_atom,
        data: [2, x11rb::CURRENT_TIME, 0, 0, 0].into(),
    };
    let Ok(cookie) = connection.send_event(
        false,
        root,
        EventMask::SUBSTRUCTURE_REDIRECT | EventMask::SUBSTRUCTURE_NOTIFY,
        event,
    ) else {
        return false;
    };
    if cookie.check().is_err() {
        return false;
    }
    if connection.flush().is_err() {
        return false;
    }
    // The window manager may decline activation; only report observed success.
    for _ in 0..10 {
        if property(&connection, root, b"_NET_ACTIVE_WINDOW")
            .and_then(|ids| ids.first().copied())
            .and_then(|id| window_pid(&connection, id))
            == Some(pid)
        {
            return true;
        }
        std::thread::sleep(std::time::Duration::from_millis(20));
    }
    false
}
