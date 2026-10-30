use crate::{KeyboardLayoutJson, LibraryDatabase, LoadedGameBundle};
use quick_xml::{events::Event, Reader};
use serde_json::{json, Value};
use std::{
    collections::{BTreeMap, HashMap},
    fs,
    path::Path,
};

/// Both PortableSettingsProvider and the older .NET <setting name="..."> form.
pub(super) fn read(path: &Path) -> Result<BTreeMap<String, String>, String> {
    let xml = fs::read_to_string(path).map_err(|e| e.to_string())?;
    let mut reader = Reader::from_str(&xml);
    let mut stack = Vec::<String>::new();
    let mut setting = None::<(String, usize)>;
    let mut text = String::new();
    let mut values = BTreeMap::new();
    let mut found = false;
    loop {
        match reader
            .read_event()
            .map_err(|e| format!("Invalid user.config: {e}"))?
        {
            Event::Start(element) => {
                let name = element.name().as_ref().to_owned();
                if name == "userSettings" {
                    found = true;
                }
                if stack.iter().any(|tag| tag == "userSettings") {
                    let attributes = element
                        .attributes()
                        .map(|attr| {
                            let attr = attr.map_err(|e| e.to_string())?;
                            Ok((
                                attr.key.as_ref().to_owned(),
                                attr.normalized_value(quick_xml::XmlVersion::Implicit1_0)
                                    .map_err(|e| e.to_string())?
                                    .into_owned(),
                            ))
                        })
                        .collect::<Result<HashMap<_, _>, String>>()?;
                    if name == "setting" {
                        if let Some(key) = attributes.get("name") {
                            setting = Some((key.clone(), stack.len() + 1));
                        }
                    } else if attributes.contains_key("serializeAs") {
                        setting = Some((name.clone(), stack.len() + 1));
                    }
                }
                if name == "value" {
                    text.clear();
                }
                stack.push(name);
            }
            Event::Text(value) if stack.last().is_some_and(|tag| tag == "value") => {
                text.push_str(&value.xml10_content())
            }
            Event::CData(value) if stack.last().is_some_and(|tag| tag == "value") => {
                text.push_str(&value.xml10_content())
            }
            Event::GeneralRef(reference) if stack.last().is_some_and(|tag| tag == "value") => {
                text.push_str(
                    &quick_xml::escape::unescape(&format!("&{};", &*reference))
                        .map_err(|e| e.to_string())?,
                );
            }
            Event::Empty(element) if element.name().as_ref() == "value" => {
                if let Some((name, _)) = &setting {
                    values.insert(name.clone(), String::new());
                }
            }
            Event::End(element) => {
                if element.name().as_ref() == "value" {
                    if let Some((name, _)) = &setting {
                        values.insert(name.clone(), text.clone());
                    }
                }
                if setting
                    .as_ref()
                    .is_some_and(|(_, depth)| *depth == stack.len())
                {
                    setting = None;
                }
                stack.pop();
            }
            Event::DocType(_) => return Err("user.config must not contain a DTD".into()),
            Event::Eof => break,
            _ => {}
        }
    }
    if !found || !stack.is_empty() {
        return Err("Invalid user.config structure".into());
    }
    Ok(values)
}

fn boolean(raw: &BTreeMap<String, String>, name: &str) -> Option<bool> {
    match raw.get(name)?.trim().to_ascii_lowercase().as_str() {
        "true" => Some(true),
        "false" => Some(false),
        _ => None,
    }
}
fn integer(raw: &BTreeMap<String, String>, name: &str) -> Option<i64> {
    raw.get(name)?.trim().parse().ok()
}
fn number(raw: &BTreeMap<String, String>, name: &str) -> Option<f64> {
    raw.get(name)?
        .trim()
        .parse::<f64>()
        .ok()
        .filter(|v| v.is_finite())
}
fn normalized(text: &str) -> String {
    text.chars()
        .filter(|c| c.is_alphanumeric())
        .flat_map(char::to_lowercase)
        .collect()
}
fn game<'a>(games: &'a [LoadedGameBundle], name: &str) -> Option<&'a LoadedGameBundle> {
    let key = normalized(name);
    games.iter().find(|g| {
        [
            &g.config.id,
            &g.config.display_name,
            g.config.short_name.as_deref().unwrap_or(""),
        ]
        .iter()
        .any(|name| normalized(name) == key)
    })
}

fn hotkey(raw: &str, action: &str, label: &str) -> Option<Value> {
    let (modifiers, key) = raw.split_once('|')?;
    let modifiers: u32 = modifiers.parse().ok()?;
    if modifiers & !7 != 0 {
        return None;
    } // The rewrite has no Windows-modifier field.
    let key: u32 = key.parse().ok()?;
    // System.Windows.Input.Key values, not Win32 virtual-key codes.
    let key = match key {
        0 => String::new(),
        2 => "Backspace".into(),
        3 => "Tab".into(),
        6 => "Enter".into(),
        13 => "Escape".into(),
        18 => "Space".into(),
        19 => "PageUp".into(),
        20 => "PageDown".into(),
        21 => "End".into(),
        22 => "Home".into(),
        23 => "Left".into(),
        24 => "Up".into(),
        25 => "Right".into(),
        26 => "Down".into(),
        31 => "Insert".into(),
        32 => "Delete".into(),
        34..=43 => char::from_u32('0' as u32 + key - 34)?.to_string(),
        44..=69 => char::from_u32('A' as u32 + key - 44)?.to_string(),
        90..=113 => format!("F{}", key - 89),
        _ => return None,
    };
    Some(json!({"action": action, "label": label, "key": key,
        "ctrl": modifiers & 2 != 0, "alt": modifiers & 1 != 0, "shift": modifiers & 4 != 0}))
}

pub(super) fn map(
    raw: &BTreeMap<String, String>,
    games: &[LoadedGameBundle],
    layouts: &HashMap<String, KeyboardLayoutJson>,
    db: &LibraryDatabase,
) -> Result<Value, String> {
    let mut settings = json!({});
    for (old, new) in [
        ("SmoothScrollingEnabled", "smoothScrolling"),
        ("AutoEnableListenMode", "autoEnableListenMode"),
        ("EnableKeyUp", "enableKeyUp"),
        ("HotkeysEnabled", "hotkeysEnabled"),
        ("AutoScanMidiFolder", "autoScanMidiFolder"),
        ("AutoDetectDefaultKey", "autoDetectBaseKey"),
        ("DefaultSongHoldNotes", "defaultHoldNotes"),
        ("DefaultSongMergeNotes", "defaultMergeNotes"),
        ("AutoCheckUpdates", "autoCheckUpdates"),
        ("AutoDownloadUpdates", "autoDownloadUpdates"),
        ("IncludeBetaUpdates", "includeBetaUpdates"),
        ("TelemetryOptIn", "telemetryOptIn"),
        ("NotifyAccountRisk", "notifyAccountRisk"),
        ("DebugModeEnabled", "debugModeEnabled"),
        ("LogPlayedNotes", "logPlayedNotes"),
        ("PageCaching", "pageCaching"),
        ("HasShownFirstLaunch", "onboardingCompleted"),
    ] {
        if let Some(value) = boolean(raw, old) {
            settings[new] = json!(value);
        }
    }
    for (old, new, min, max) in [
        ("KeyboardPressDelayMs", "keyPressDelayMs", 0, 1000),
        ("AutoCorrectThreshold", "autoCorrectThreshold", 0, 128),
        ("DefaultSongDefaultKey", "defaultBaseKeyOffset", -27, 27),
        ("DefaultSongKey", "defaultKeyOffset", -27, 27),
        (
            "DefaultSongMergeMilliseconds",
            "defaultMergeToleranceMs",
            0,
            1000,
        ),
        ("CrashLogVerbosity", "crashLogVerbosity", 0, 3),
        ("CacheMaxSizeMB", "cacheMaxSizeMb", 0, i64::MAX),
    ] {
        if let Some(value) = integer(raw, old).filter(|v| (min..=max).contains(v)) {
            settings[new] = json!(value);
        }
    }
    for name in ["MidiFolder", "AccentColor"] {
        if let Some(value) = raw.get(name) {
            settings[if name == "MidiFolder" {
                "midiFolder"
            } else {
                "accentColor"
            }] = json!(value);
        }
    }
    if let Some(value) = number(raw, "DefaultSongSpeed").filter(|v| (0.1..=4.0).contains(v)) {
        settings["defaultSpeed"] = json!(value);
    }
    for (old, new, options) in [
        (
            "DefaultSongTranspose",
            "defaultTranspose",
            &[(0, "Ignore"), (1, "Smart"), (2, "Up"), (3, "Down")][..],
        ),
        (
            "AppTheme",
            "theme",
            &[(-1, "System"), (0, "Light"), (1, "Dark")][..],
        ),
        (
            "CacheAutoCleanInterval",
            "cacheAutoClean",
            &[(0, "daily"), (1, "weekly"), (2, "monthly"), (3, "never")][..],
        ),
        (
            "UpdateCheckFrequency",
            "updateCheckFrequency",
            &[
                (0, "10min"),
                (1, "hourly"),
                (2, "daily"),
                (3, "weekly"),
                (4, "launch"),
            ][..],
        ),
        (
            "MouseStopClickMode",
            "mouseStopClick",
            &[(0, "Disabled"), (1, "LeftClick"), (2, "RightClick")][..],
        ),
    ] {
        if let Some(value) =
            integer(raw, old).and_then(|n| options.iter().find(|(v, _)| *v == n).map(|(_, v)| *v))
        {
            settings[new] = json!(value);
        }
    }
    if let Some(window) = boolean(raw, "UseWindowMessage") {
        settings["keypressMethod"] = json!(if window { "PostMessage" } else { "SendInput" });
    }
    let mut bindings = Vec::new();
    for (action, label) in [
        ("PlayPause", "Play / Pause"),
        ("Next", "Next Song"),
        ("Previous", "Previous Song"),
        ("SpeedUp", "Increase Speed"),
        ("SpeedDown", "Decrease Speed"),
        ("Panic", "Panic / Stop All"),
    ] {
        if let Some(value) = raw
            .get(&format!("Hotkey{action}"))
            .and_then(|raw| hotkey(raw, action, label))
        {
            bindings.push(value);
        }
    }
    // A partial array would remove unspecified default actions. Only replace it
    // if every binding can be represented; the source remains archived otherwise.
    if bindings.len() == 6 {
        settings["hotkeys"] = json!(bindings);
    }
    super::merge(&mut settings, &selections(raw, games, layouts));
    if let Some(page) = raw
        .get("LastViewedPage")
        .map(|s| s.to_lowercase())
        .filter(|s| {
            [
                "songs",
                "tracks",
                "studio",
                "sheet",
                "instrument",
                "queue",
                "discover",
                "settings",
                "about",
            ]
            .contains(&s.as_str())
        })
    {
        settings["ui"]["lastViewedPage"] = json!(page);
    }
    if let Some(sort) = integer(raw, "SongsSortMode").and_then(|n| match n {
        1 => Some("title"),
        2 => Some("dateAdded"),
        3 => Some("duration"),
        _ => None,
    }) {
        settings["ui"]["songsSortMode"] = json!(sort);
    }
    if let Some(shuffle) = boolean(raw, "QueueShuffle") {
        settings["ui"]["playback"]["isShuffled"] = json!(shuffle);
    }
    if let Some(mode) = integer(raw, "QueueLoopMode").and_then(|n| match n {
        0 => Some("off"),
        1 => Some("all"),
        2 => Some("single"),
        _ => None,
    }) {
        settings["ui"]["playback"]["loopMode"] = json!(mode);
    }
    if let Some(speakers) = boolean(raw, "UseSpeakers") {
        settings["ui"]["playback"]["isListenMode"] = json!(speakers);
    }
    if let Some(id) = raw.get("CurrentSongId") {
        if let Some(id) = db.canonical_song_id(id).map_err(|e| e.to_string())? {
            settings["ui"]["playback"]["activeSongId"] = json!(id);
            if let Some(position) = number(raw, "CurrentSongPosition").filter(|n| *n >= 0.0) {
                settings["ui"]["playback"]["positionSeconds"] = json!(position);
            }
        }
    }
    Ok(settings)
}

pub(super) fn selections(
    raw: &BTreeMap<String, String>,
    games: &[LoadedGameBundle],
    layouts: &HashMap<String, KeyboardLayoutJson>,
) -> Value {
    let mut settings = json!({});
    for (suffix, id) in [
        ("Genshin", "Genshin Impact"),
        ("WutheringWaves", "Wuthering Waves"),
        ("NTE", "NTE"),
        ("WWM", "WWM"),
        ("BPSR", "BPSR"),
        ("Sky", "Sky"),
        ("Roblox", "Roblox"),
        ("Heartopia", "Heartopia"),
        ("CoreKeeper", "Core Keeper"),
        ("HPMA", "HPMA"),
    ] {
        if let Some(game) = game(games, id) {
            if boolean(raw, &format!("Active{suffix}")) == Some(true)
                && settings.get("activeGameId").is_none()
            {
                settings["activeGameId"] = json!(game.config.id);
            }
            if let Some(location) = raw
                .get(&format!("{suffix}Location"))
                .filter(|s| !s.is_empty())
            {
                settings["gameLocations"][&game.config.id] = json!(location);
            }
        }
    }
    if let Some(instruments) = raw
        .get("SelectedInstrumentByGame")
        .and_then(|s| serde_json::from_str::<BTreeMap<String, String>>(s).ok())
    {
        for (game_name, selected) in instruments {
            let Some(game) = game(games, &game_name) else {
                continue;
            };
            let name = selected
                .rsplit_once(':')
                .map(|(_, name)| name)
                .unwrap_or(&selected);
            let Some(instrument) = game.instruments.iter().find(|i| {
                normalized(&i.id) == normalized(name) || normalized(&i.name) == normalized(name)
            }) else {
                continue;
            };
            settings["selectedInstrumentIds"][&game.config.id] = json!(instrument.id);
            if let Some(selected) = raw.get("SelectedLayoutName") {
                if let Some(layout) = instrument
                    .supported_layouts
                    .iter()
                    .filter_map(|id| {
                        layouts
                            .values()
                            .find(|layout| layout.id.eq_ignore_ascii_case(id))
                    })
                    .find(|l| {
                        normalized(&l.name) == normalized(selected)
                            || normalized(&l.id) == normalized(selected)
                    })
                {
                    settings["selectedLayoutIds"]
                        [format!("{}/{}", game.config.id, instrument.id)] = json!(layout.id);
                }
            }
        }
    }
    settings
}
