use std::collections::HashMap;
use quick_xml::events::{BytesStart, Event};
use quick_xml::Reader;

use crate::input::chord_wheel::StrumDirection;
use super::model::*;

#[derive(Debug, Default)]
struct XmlNode {
    name: String,
    attrs: Vec<(String, String)>,
    text: String,
    children: Vec<XmlNode>,
}

impl XmlNode {
    fn attr(&self, key: &str) -> Option<&str> {
        self.attrs
            .iter()
            .find(|(k, _)| k == key)
            .map(|(_, v)| v.as_str())
    }

    fn child(&self, name: &str) -> Option<&XmlNode> {
        self.children.iter().find(|c| c.name == name)
    }

    fn child_text(&self, name: &str) -> Option<&str> {
        self.child(name).map(|c| c.text.as_str())
    }
}

fn push_general_ref(buf: &mut String, raw: &quick_xml::events::BytesRef<'_>) {
    if let Ok(Some(ch)) = raw.resolve_char_ref() {
        buf.push(ch);
        return;
    }
    match raw.as_ref() {
        "amp" => buf.push('&'),
        "lt" => buf.push('<'),
        "gt" => buf.push('>'),
        "quot" => buf.push('"'),
        "apos" => buf.push('\''),
        other => {
            buf.push('&');
            buf.push_str(other);
            buf.push(';');
        }
    }
}

fn make_node(start: &BytesStart<'_>) -> Result<XmlNode, String> {
    let local = start.local_name();
    let name: &str = local.as_ref();
    let name = name.to_string();
    let mut attrs = Vec::new();
    for attr in start.attributes() {
        let attr = attr.map_err(|e| format!("Invalid XML attribute: {e}"))?;
        let key_local = attr.key.local_name();
        let key: &str = key_local.as_ref();
        let key = key.to_string();
        let val = attr
            .normalized_value(quick_xml::XmlVersion::Implicit1_0)
            .map_err(|e| format!("Invalid XML attribute value: {e}"))?
            .into_owned();
        attrs.push((key, val));
    }
    Ok(XmlNode {
        name,
        attrs,
        text: String::new(),
        children: Vec::new(),
    })
}

fn parse_xml_dom(xml: &str) -> Result<XmlNode, String> {
    let xml = xml.trim_start_matches('\u{FEFF}');
    let mut reader = Reader::from_str(xml);
    let mut stack: Vec<XmlNode> = Vec::new();
    let mut root: Option<XmlNode> = None;
    let mut skip_depth: usize = 0;

    loop {
        match reader.read_event() {
            Ok(Event::Start(e)) => {
                if skip_depth > 0 {
                    skip_depth += 1;
                    continue;
                }
                let node = make_node(&e)?;
                if node.name == "Assets" && stack.len() == 1 {
                    skip_depth = 1;
                    continue;
                }
                stack.push(node);
            }
            Ok(Event::Empty(e)) => {
                if skip_depth > 0 {
                    continue;
                }
                let node = make_node(&e)?;
                if let Some(parent) = stack.last_mut() {
                    parent.children.push(node);
                } else {
                    root = Some(node);
                }
            }
            Ok(Event::End(_)) => {
                if skip_depth > 0 {
                    skip_depth -= 1;
                    continue;
                }
                let finished = stack
                    .pop()
                    .ok_or_else(|| "Mismatched closing tag in score.gpif".to_string())?;
                if let Some(parent) = stack.last_mut() {
                    parent.children.push(finished);
                } else {
                    root = Some(finished);
                }
            }
            Ok(Event::Text(e)) => {
                if skip_depth == 0 {
                    if let Some(top) = stack.last_mut() {
                        top.text.push_str(&e.xml10_content());
                    }
                }
            }
            Ok(Event::CData(e)) => {
                if skip_depth == 0 {
                    if let Some(top) = stack.last_mut() {
                        top.text.push_str(&e.xml10_content());
                    }
                }
            }
            Ok(Event::GeneralRef(e)) => {
                if skip_depth == 0 {
                    if let Some(top) = stack.last_mut() {
                        push_general_ref(&mut top.text, &e);
                    }
                }
            }
            Ok(Event::Eof) => break,
            Ok(_) => {}
            Err(e) => return Err(format!("Failed to parse GPIF XML: {e}")),
        }
    }

    let root = root.ok_or_else(|| "Empty GPIF XML document".to_string())?;
    if root.name != "GPIF" {
        return Err(format!("Unexpected GPIF root node <{}>", root.name));
    }
    Ok(root)
}

#[derive(Debug, Clone)]
struct GpifRhythm {
    duration: GpDuration,
    dots: i32,
    tuplet_num: i32,
    tuplet_den: i32,
}

#[derive(Debug, Clone)]
struct GpifSound {
    program: u8,
}

#[derive(Debug, Clone)]
enum TrackAutomationEvent {
    Sound {
        bar: usize,
        program: u8,
    },
}

fn parse_int(text: Option<&str>, fallback: i32) -> i32 {
    text.and_then(|s| s.trim().parse::<i32>().ok())
        .unwrap_or(fallback)
}

fn parse_float(text: Option<&str>, fallback: f64) -> f64 {
    text.and_then(|s| s.trim().parse::<f64>().ok())
        .unwrap_or(fallback)
}

fn split_tokens(text: &str) -> Vec<&str> {
    text.split_whitespace().filter(|s| !s.is_empty()).collect()
}

fn to_bend_value(gpx_value: f64) -> i16 {
    (gpx_value / 25.0).round() as i16
}

fn to_bend_offset(gpx_offset: f64) -> u16 {
    (gpx_offset * 0.6).round().clamp(0.0, 60.0) as u16
}

fn dynamic_to_velocity(text: &str) -> u8 {
    match text.trim() {
        "PPP" => 15,
        "PP" => 31,
        "P" => 47,
        "MP" => 63,
        "MF" => 79,
        "F" => 95,
        "FF" => 111,
        "FFF" => 127,
        _ => 95,
    }
}

fn tempo_reference_factor(reference: i32) -> f64 {
    match reference {
        1 => 0.5,
        3 => 1.5,
        4 => 2.0,
        5 => 3.0,
        _ => 1.0,
    }
}

/// GP6 `(element, variation)` -> alphaTab articulation ID -> GM drum MIDI note.
pub(super) fn gp6_element_variation_to_midi(element: i32, variation: i32) -> u8 {
    const TABLE: [[u8; 3]; 17] = [
        [35, 35, 35],   // 0: Kick
        [38, 38, 37],   // 1: Snare (hit, rim shot -> 38, side stick -> 37)
        [56, 56, 56],   // 2: Cowbell low
        [56, 56, 56],   // 3: Cowbell medium
        [56, 56, 56],   // 4: Cowbell high
        [43, 43, 43],   // 5: Tom very low
        [45, 45, 45],   // 6: Tom low
        [47, 47, 47],   // 7: Tom medium
        [48, 48, 48],   // 8: Tom high
        [50, 50, 50],   // 9: Tom very high
        [42, 46, 46],   // 10: Hihat (closed, half -> 46, open -> 46)
        [44, 44, 44],   // 11: Pedal hihat
        [57, 57, 57],   // 12: Crash medium
        [49, 49, 49],   // 13: Crash high
        [55, 55, 55],   // 14: Splash
        [51, 51, 53],   // 15: Ride (middle, edge -> 51, bell -> 53)
        [52, 52, 52],   // 16: China
    ];
    if element >= 0 && (element as usize) < TABLE.len() {
        let v = if variation >= 0 && (variation as usize) < 3 {
            variation as usize
        } else {
            0
        };
        TABLE[element as usize][v]
    } else {
        38
    }
}

pub(super) fn parse_gpif_xml(xml: &str) -> Result<GpScore, String> {
    let root = parse_xml_dom(xml)?;
    let mut score = GpScore::default();
    let mut has_anacrusis = false;
    let mut master_tempo_automations: HashMap<usize, Vec<GpTempoAutomation>> = HashMap::new();
    let mut tracks_mapping: Vec<String> = Vec::new();
    let mut tracks_by_id: HashMap<String, GpTrack> = HashMap::new();
    let mut chords_by_track: HashMap<String, Vec<HashMap<String, String>>> = HashMap::new();
    let mut track_sound_automations: HashMap<String, Vec<TrackAutomationEvent>> = HashMap::new();
    let mut master_bars: Vec<GpMasterBar> = Vec::new();
    let mut bars_of_master_bar: Vec<Vec<String>> = Vec::new();
    let mut bar_voices: HashMap<String, Vec<String>> = HashMap::new();
    let mut voice_beats: HashMap<String, Vec<String>> = HashMap::new();
    let mut beats_by_id: HashMap<String, GpBeat> = HashMap::new();
    let mut beat_notes: HashMap<String, Vec<String>> = HashMap::new();
    let mut beat_rhythm: HashMap<String, String> = HashMap::new();
    let mut beat_chord_id: HashMap<String, String> = HashMap::new();
    let mut notes_by_id: HashMap<String, (GpNote, Option<i32>)> = HashMap::new(); // (note, raw gpif string 0-based)
    let mut rhythms_by_id: HashMap<String, GpifRhythm> = HashMap::new();

    for section in &root.children {
        match section.name.as_str() {
            "Score" => {
                score.title = section.child_text("Title").unwrap_or("").trim().to_string();
                score.artist = section.child_text("Artist").unwrap_or("").trim().to_string();
                score.album = section.child_text("Album").unwrap_or("").trim().to_string();
            }
            "MasterTrack" => {
                for c in &section.children {
                    match c.name.as_str() {
                        "Tracks" => {
                            tracks_mapping = split_tokens(&c.text)
                                .into_iter()
                                .map(str::to_string)
                                .collect();
                        }
                        "Anacrusis" => has_anacrusis = true,
                        "Automations" => {
                            for auto in &c.children {
                                if auto.name != "Automation" {
                                    continue;
                                }
                                if auto.child_text("Type").map(str::trim) == Some("Tempo") {
                                    let bar = parse_int(auto.child_text("Bar"), -1);
                                    if bar >= 0 {
                                        let ratio = parse_float(auto.child_text("Position"), 0.0);
                                        let val_text = auto.child_text("Value").unwrap_or("");
                                        let parts = split_tokens(val_text);
                                        let (num, ref_val) = match parts.len() {
                                            0 => (120.0, 2),
                                            1 => (parse_float(Some(parts[0]), 120.0), 1),
                                            _ => (
                                                parse_float(Some(parts[0]), 120.0),
                                                parse_int(Some(parts[1]), 2),
                                            ),
                                        };
                                        let bpm = num * tempo_reference_factor(ref_val);
                                        if bpm > 0.0 {
                                            master_tempo_automations
                                                .entry(bar as usize)
                                                .or_default()
                                                .push(GpTempoAutomation {
                                                    ratio_position: ratio,
                                                    bpm,
                                                });
                                        }
                                    }
                                }
                            }
                        }
                        _ => {}
                    }
                }
            }
            "Tracks" => {
                for t_node in &section.children {
                    if t_node.name != "Track" {
                        continue;
                    }
                    let id = t_node.attr("id").unwrap_or("").to_string();
                    let mut track = GpTrack {
                        volume: 100,
                        balance: 64,
                        ..GpTrack::default()
                    };
                    track.staves.push(GpStaff::default());
                    let mut staff_chords: Vec<HashMap<String, String>> = vec![HashMap::new()];
                    let mut sounds: HashMap<String, GpifSound> = HashMap::new();
                    let mut first_sound_applied = false;

                    for c in &t_node.children {
                        match c.name.as_str() {
                            "Name" => track.name = c.text.trim().to_string(),
                            "Instrument" => {
                                let r = c.attr("ref").unwrap_or("");
                                if (r.ends_with("-gs") || r.ends_with("GrandStaff"))
                                    && track.staves.len() < 2
                                {
                                    track.staves.push(GpStaff::default());
                                    staff_chords.push(HashMap::new());
                                }
                            }
                            "InstrumentSet" => {
                                if c.child_text("Type").map(str::trim) == Some("drumKit") {
                                    for st in &mut track.staves {
                                        st.is_percussion = true;
                                    }
                                }
                                if let Some(elems) = c.child("Elements") {
                                    for elem in &elems.children {
                                        if let Some(arts) = elem.child("Articulations") {
                                            for art in &arts.children {
                                                if art.name != "Articulation" {
                                                    continue;
                                                }
                                                let input_id = art
                                                    .child_text("InputMidiNumbers")
                                                    .and_then(|s| split_tokens(s).into_iter().next())
                                                    .and_then(|t| t.parse::<i32>().ok())
                                                    .unwrap_or(-1);
                                                let output_midi =
                                                    parse_int(art.child_text("OutputMidiNumber"), 0);
                                                track.percussion_articulations.push(GpArticulation {
                                                    id: input_id,
                                                    output_midi,
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                            "GeneralMidi" | "MidiConnection" | "MIDISettings" => {
                                if let Some(prog) = c.child_text("Program") {
                                    track.program = parse_int(Some(prog), 0).clamp(0, 127) as u8;
                                }
                                if c.attr("table") == Some("Percussion") {
                                    for st in &mut track.staves {
                                        st.is_percussion = true;
                                    }
                                }
                            }
                            "Sounds" => {
                                for s_node in &c.children {
                                    if s_node.name != "Sound" {
                                        continue;
                                    }
                                    let s_name = s_node.child_text("Name").unwrap_or("").trim();
                                    let s_path = s_node.child_text("Path").unwrap_or("").trim();
                                    let s_role = s_node.child_text("Role").unwrap_or("").trim();
                                    let program = s_node
                                        .child("MIDI")
                                        .and_then(|m| m.child_text("Program"))
                                        .map(|p| parse_int(Some(p), 0).clamp(0, 127) as u8)
                                        .unwrap_or(0);
                                    if !first_sound_applied {
                                        track.program = program;
                                        first_sound_applied = true;
                                    }
                                    let uid = format!("{s_path};{s_name};{s_role}");
                                    sounds.insert(
                                        uid,
                                        GpifSound {
                                            program,
                                        },
                                    );
                                }
                            }
                            "PlaybackState" => {
                                let state = c.text.trim();
                                track.is_solo = state == "Solo";
                                track.is_mute = state == "Mute";
                            }
                            "Properties" => {
                                parse_track_or_staff_properties(
                                    c,
                                    &mut track.staves,
                                    &mut staff_chords,
                                );
                            }
                            "Staves" => {
                                let mut s_idx = 0usize;
                                for s_node in &c.children {
                                    if s_node.name != "Staff" {
                                        continue;
                                    }
                                    while track.staves.len() <= s_idx {
                                        track.staves.push(GpStaff::default());
                                        staff_chords.push(HashMap::new());
                                    }
                                    if let Some(props) = s_node.child("Properties") {
                                        let mut single = vec![std::mem::take(&mut track.staves[s_idx])];
                                        let mut single_c = vec![std::mem::take(&mut staff_chords[s_idx])];
                                        parse_track_or_staff_properties(
                                            props,
                                            &mut single,
                                            &mut single_c,
                                        );
                                        track.staves[s_idx] = single.pop().unwrap();
                                        staff_chords[s_idx] = single_c.pop().unwrap();
                                    }
                                    s_idx += 1;
                                }
                            }
                            "RSE" => {
                                if let Some(params) = c
                                    .child("ChannelStrip")
                                    .and_then(|cs| cs.child_text("Parameters"))
                                {
                                    let parts = split_tokens(params);
                                    if parts.len() >= 13 {
                                        let bal = parse_float(Some(parts[11]), 0.5).clamp(0.0, 1.0);
                                        let vol = parse_float(Some(parts[12]), 0.9).clamp(0.0, 1.0);
                                        track.balance = (bal * 127.0).round() as u8;
                                        track.volume = (vol * 127.0).round() as u8;
                                    }
                                }
                            }
                            "Automations" => {
                                for auto in &c.children {
                                    if auto.name != "Automation" {
                                        continue;
                                    }
                                    if auto.child_text("Type").map(str::trim) == Some("Sound") {
                                        let bar = parse_int(auto.child_text("Bar"), -1);
                                        let val = auto.child_text("Value").unwrap_or("").trim();
                                        if bar >= 0 {
                                            if let Some(s) = sounds.get(val) {
                                                track_sound_automations
                                                    .entry(id.clone())
                                                    .or_default()
                                                    .push(TrackAutomationEvent::Sound {
                                                        bar: bar as usize,
                                                        program: s.program,
                                                    });
                                            }
                                        }
                                    }
                                }
                            }
                            _ => {}
                        }
                    }
                    chords_by_track.insert(id.clone(), staff_chords);
                    tracks_by_id.insert(id, track);
                }
            }
            "MasterBars" => {
                for mb_node in &section.children {
                    if mb_node.name != "MasterBar" {
                        continue;
                    }
                    let mut mb = GpMasterBar {
                        num: 4,
                        den: 4,
                        is_anacrusis: master_bars.is_empty() && has_anacrusis,
                        repeat_count: 1,
                        ..GpMasterBar::default()
                    };
                    let mut mb_bars = Vec::new();
                    for c in &mb_node.children {
                        match c.name.as_str() {
                            "Time" => {
                                let parts: Vec<&str> = c.text.trim().split('/').collect();
                                if parts.len() == 2 {
                                    mb.num = parse_int(Some(parts[0]), 4);
                                    mb.den = parse_int(Some(parts[1]), 4);
                                }
                            }
                            "FreeTime" => mb.is_free_time = true,
                            "Repeat" => {
                                if c.attr("start").is_some_and(|v| v.eq_ignore_ascii_case("true")) {
                                    mb.is_repeat_start = true;
                                }
                                if c.attr("end").is_some_and(|v| v.eq_ignore_ascii_case("true")) {
                                    if let Some(cnt) = c.attr("count") {
                                        mb.repeat_count = parse_int(Some(cnt), 1).max(1);
                                    }
                                }
                            }
                            "AlternateEndings" => {
                                let mut mask = 0u32;
                                for tok in split_tokens(&c.text) {
                                    let idx = parse_int(Some(tok), 0);
                                    if (1..=32).contains(&idx) {
                                        mask |= 1u32 << (idx - 1);
                                    }
                                }
                                mb.alternate_endings = mask;
                            }
                            "Bars" => {
                                mb_bars = split_tokens(&c.text)
                                    .into_iter()
                                    .map(str::to_string)
                                    .collect();
                            }
                            "TripletFeel" => {
                                mb.triplet_feel = match c.text.trim() {
                                    "Triplet8th" => GpTripletFeel::Triplet8th,
                                    "Triplet16th" => GpTripletFeel::Triplet16th,
                                    "Dotted8th" => GpTripletFeel::Dotted8th,
                                    "Dotted16th" => GpTripletFeel::Dotted16th,
                                    "Scottish8th" => GpTripletFeel::Scottish8th,
                                    "Scottish16th" => GpTripletFeel::Scottish16th,
                                    _ => GpTripletFeel::None,
                                };
                            }
                            "Key" => {
                                let sf = parse_int(c.child_text("AccidentalCount"), 0)
                                    .clamp(-7, 7) as i8;
                                let is_minor = c
                                    .child_text("Mode")
                                    .is_some_and(|m| m.trim().eq_ignore_ascii_case("minor"));
                                mb.key_sig = Some((sf, is_minor));
                            }
                            "Directions" => {
                                for d in &c.children {
                                    let dir = match (d.name.as_str(), d.text.trim()) {
                                        ("Target", "Coda") => Some(GpDirection::TargetCoda),
                                        ("Target", "DoubleCoda") => {
                                            Some(GpDirection::TargetDoubleCoda)
                                        }
                                        ("Target", "Segno") => Some(GpDirection::TargetSegno),
                                        ("Target", "SegnoSegno") => {
                                            Some(GpDirection::TargetSegnoSegno)
                                        }
                                        ("Target", "Fine") => Some(GpDirection::TargetFine),
                                        ("Jump", "DaCapo") => Some(GpDirection::JumpDaCapo),
                                        ("Jump", "DaCapoAlCoda") => {
                                            Some(GpDirection::JumpDaCapoAlCoda)
                                        }
                                        ("Jump", "DaCapoAlDoubleCoda") => {
                                            Some(GpDirection::JumpDaCapoAlDoubleCoda)
                                        }
                                        ("Jump", "DaCapoAlFine") => {
                                            Some(GpDirection::JumpDaCapoAlFine)
                                        }
                                        ("Jump", "DaSegno" | "DalSegno") => {
                                            Some(GpDirection::JumpDalSegno)
                                        }
                                        ("Jump", "DaSegnoAlCoda" | "DalSegnoAlCoda") => {
                                            Some(GpDirection::JumpDalSegnoAlCoda)
                                        }
                                        ("Jump", "DaSegnoAlDoubleCoda" | "DalSegnoAlDoubleCoda") => {
                                            Some(GpDirection::JumpDalSegnoAlDoubleCoda)
                                        }
                                        ("Jump", "DaSegnoAlFine" | "DalSegnoAlFine") => {
                                            Some(GpDirection::JumpDalSegnoAlFine)
                                        }
                                        ("Jump", "DaSegnoSegno" | "DalSegnoSegno") => {
                                            Some(GpDirection::JumpDalSegnoSegno)
                                        }
                                        ("Jump", "DaSegnoSegnoAlCoda" | "DalSegnoSegnoAlCoda") => {
                                            Some(GpDirection::JumpDalSegnoSegnoAlCoda)
                                        }
                                        (
                                            "Jump",
                                            "DaSegnoSegnoAlDoubleCoda" | "DalSegnoSegnoAlDoubleCoda",
                                        ) => Some(GpDirection::JumpDalSegnoSegnoAlDoubleCoda),
                                        ("Jump", "DaSegnoSegnoAlFine" | "DalSegnoSegnoAlFine") => {
                                            Some(GpDirection::JumpDalSegnoSegnoAlFine)
                                        }
                                        ("Jump", "DaCoda") => Some(GpDirection::JumpDaCoda),
                                        ("Jump", "DaDoubleCoda") => {
                                            Some(GpDirection::JumpDaDoubleCoda)
                                        }
                                        _ => None,
                                    };
                                    if let Some(dir) = dir {
                                        mb.directions.push(dir);
                                    }
                                }
                            }
                            _ => {}
                        }
                    }
                    let mb_idx = master_bars.len();
                    if let Some(mut autos) = master_tempo_automations.remove(&mb_idx) {
                        mb.tempo_automations.append(&mut autos);
                    }
                    if mb_idx == 0 {
                        if let Some(first_tempo) = mb.tempo_automations.first() {
                            score.tempo_bpm = first_tempo.bpm;
                        }
                    }
                    master_bars.push(mb);
                    bars_of_master_bar.push(mb_bars);
                }
            }
            "Bars" => {
                for bar_node in &section.children {
                    if bar_node.name != "Bar" {
                        continue;
                    }
                    let id = bar_node.attr("id").unwrap_or("").to_string();
                    let voices = bar_node
                        .child_text("Voices")
                        .map(|t| split_tokens(t).into_iter().map(str::to_string).collect())
                        .unwrap_or_default();
                    bar_voices.insert(id, voices);
                }
            }
            "Voices" => {
                for v_node in &section.children {
                    if v_node.name != "Voice" {
                        continue;
                    }
                    let id = v_node.attr("id").unwrap_or("").to_string();
                    let beats = v_node
                        .child_text("Beats")
                        .map(|t| split_tokens(t).into_iter().map(str::to_string).collect())
                        .unwrap_or_default();
                    voice_beats.insert(id, beats);
                }
            }
            "Beats" => {
                for b_node in &section.children {
                    if b_node.name != "Beat" {
                        continue;
                    }
                    let id = b_node.attr("id").unwrap_or("").to_string();
                    let mut beat = GpBeat::default();
                    let mut beat_velocity = 95u8;
                    for c in &b_node.children {
                        match c.name.as_str() {
                            "Notes" => {
                                beat_notes.insert(
                                    id.clone(),
                                    split_tokens(&c.text).into_iter().map(str::to_string).collect(),
                                );
                            }
                            "Rhythm" => {
                                if let Some(r_id) = c.attr("ref") {
                                    beat_rhythm.insert(id.clone(), r_id.to_string());
                                }
                            }
                            "Chord" => {
                                beat_chord_id.insert(id.clone(), c.text.trim().to_string());
                            }
                            "Dynamic" => beat_velocity = dynamic_to_velocity(&c.text),
                            "GraceNotes" => {
                                beat.grace_type = match c.text.trim() {
                                    "OnBeat" => Some(GpGraceType::OnBeat),
                                    "BeforeBeat" => Some(GpGraceType::BeforeBeat),
                                    _ => None,
                                };
                            }
                            "Properties" => {
                                for prop in &c.children {
                                    if prop.name == "Property" && prop.attr("name") == Some("Brush")
                                    {
                                        beat.brush = if prop.child_text("Direction").map(str::trim)
                                            == Some("Up")
                                        {
                                            Some(StrumDirection::Up)
                                        } else {
                                            Some(StrumDirection::Down)
                                        };
                                    }
                                }
                            }
                            _ => {}
                        }
                    }
                    // Store beat velocity temporarily inside first unused field via beat_notes resolution;
                    // we apply beat_velocity when cloning notes into each beat below.
                    beats_by_id.insert(id.clone(), beat);
                    beat_chord_id
                        .entry(format!("__vel__{id}"))
                        .or_insert_with(|| beat_velocity.to_string());
                }
            }
            "Notes" => {
                for n_node in &section.children {
                    if n_node.name != "Note" {
                        continue;
                    }
                    let id = n_node.attr("id").unwrap_or("").to_string();
                    let mut note = GpNote {
                        velocity: 95,
                        duration_percent: 1.0,
                        octave: -1,
                        tone: -1,
                        ..GpNote::default()
                    };
                    let mut raw_string: Option<i32> = None;
                    let mut elem = -1i32;
                    let mut variation = -1i32;
                    let mut is_bended = false;
                    let mut bend_origin: Option<GpBendPoint> = None;
                    let mut bend_mid_val: Option<i16> = None;
                    let mut bend_mid_off1: Option<u16> = None;
                    let mut bend_mid_off2: Option<u16> = None;
                    let mut bend_dest: Option<GpBendPoint> = None;

                    for c in &n_node.children {
                        match c.name.as_str() {
                            "AntiAccent" => {
                                if c.text.trim().eq_ignore_ascii_case("normal") {
                                    note.is_ghost = true;
                                }
                            }
                            "Tie" => {
                                if c.attr("destination")
                                    .is_some_and(|v| v.eq_ignore_ascii_case("true"))
                                {
                                    note.is_tie_destination = true;
                                }
                            }
                            "InstrumentArticulation" => {
                                note.percussion_articulation =
                                    Some(parse_int(Some(&c.text), 0));
                            }
                            "Properties" => {
                                for prop in &c.children {
                                    if prop.name != "Property" {
                                        continue;
                                    }
                                    match prop.attr("name").unwrap_or("") {
                                        "String" => {
                                            raw_string =
                                                Some(parse_int(prop.child_text("String"), 0));
                                        }
                                        "Fret" => {
                                            note.fret = parse_int(prop.child_text("Fret"), 0);
                                        }
                                        "Element" => {
                                            elem = parse_int(prop.child_text("Element"), 0);
                                        }
                                        "Variation" => {
                                            variation = parse_int(prop.child_text("Variation"), 0);
                                        }
                                        "Muted" => {
                                            if prop.child("Enable").is_some() {
                                                note.is_dead = true;
                                            }
                                        }
                                        "Octave" => {
                                            note.octave = parse_int(prop.child_text("Number"), 0);
                                            if note.tone == -1 {
                                                note.tone = 0;
                                            }
                                        }
                                        "Tone" => {
                                            note.tone = parse_int(prop.child_text("Step"), 0);
                                        }
                                        "Bended" => is_bended = true,
                                        "BendOriginValue" => {
                                            let p = bend_origin
                                                .get_or_insert(GpBendPoint { offset: 0, value: 0 });
                                            p.value = to_bend_value(parse_float(
                                                prop.child_text("Float"),
                                                0.0,
                                            ));
                                        }
                                        "BendOriginOffset" => {
                                            let p = bend_origin
                                                .get_or_insert(GpBendPoint { offset: 0, value: 0 });
                                            p.offset = to_bend_offset(parse_float(
                                                prop.child_text("Float"),
                                                0.0,
                                            ));
                                        }
                                        "BendMiddleValue" => {
                                            bend_mid_val = Some(to_bend_value(parse_float(
                                                prop.child_text("Float"),
                                                0.0,
                                            )));
                                        }
                                        "BendMiddleOffset1" => {
                                            bend_mid_off1 = Some(to_bend_offset(parse_float(
                                                prop.child_text("Float"),
                                                0.0,
                                            )));
                                        }
                                        "BendMiddleOffset2" => {
                                            bend_mid_off2 = Some(to_bend_offset(parse_float(
                                                prop.child_text("Float"),
                                                0.0,
                                            )));
                                        }
                                        "BendDestinationValue" => {
                                            let p = bend_dest
                                                .get_or_insert(GpBendPoint { offset: 60, value: 0 });
                                            p.value = to_bend_value(parse_float(
                                                prop.child_text("Float"),
                                                0.0,
                                            ));
                                        }
                                        "BendDestinationOffset" => {
                                            let p = bend_dest
                                                .get_or_insert(GpBendPoint { offset: 0, value: 0 });
                                            p.offset = to_bend_offset(parse_float(
                                                prop.child_text("Float"),
                                                0.0,
                                            ));
                                        }
                                        _ => {}
                                    }
                                }
                            }
                            _ => {}
                        }
                    }

                    if is_bended {
                        let origin = bend_origin.unwrap_or(GpBendPoint { offset: 0, value: 0 });
                        let dest = bend_dest.unwrap_or(GpBendPoint {
                            offset: 60,
                            value: 0,
                        });
                        note.bends.push(origin);
                        if let Some(mid_val) = bend_mid_val {
                            if let Some(off1) = bend_mid_off1.filter(|&o| o > 0) {
                                note.bends.push(GpBendPoint {
                                    offset: off1,
                                    value: mid_val,
                                });
                            }
                            if let Some(off2) = bend_mid_off2.filter(|&o| o > 0) {
                                note.bends.push(GpBendPoint {
                                    offset: off2,
                                    value: mid_val,
                                });
                            }
                            if bend_mid_off1.unwrap_or(0) == 0 && bend_mid_off2.unwrap_or(0) == 0 {
                                note.bends.push(GpBendPoint {
                                    offset: 30,
                                    value: mid_val,
                                });
                            }
                        }
                        note.bends.push(dest);
                    }
                    if elem != -1 && variation != -1 {
                        note.percussion_articulation =
                            Some(i32::from(gp6_element_variation_to_midi(elem, variation)));
                    }
                    notes_by_id.insert(id, (note, raw_string));
                }
            }
            "Rhythms" => {
                for r_node in &section.children {
                    if r_node.name != "Rhythm" {
                        continue;
                    }
                    let id = r_node.attr("id").unwrap_or("").to_string();
                    let duration = match r_node.child_text("NoteValue").map(str::trim).unwrap_or("")
                    {
                        "Long" => GpDuration::Long,
                        "DoubleWhole" => GpDuration::DoubleWhole,
                        "Whole" => GpDuration::Whole,
                        "Half" => GpDuration::Half,
                        "Quarter" => GpDuration::Quarter,
                        "Eighth" => GpDuration::Eighth,
                        "16th" => GpDuration::Sixteenth,
                        "32nd" => GpDuration::ThirtySecond,
                        "64th" => GpDuration::SixtyFourth,
                        "128th" => GpDuration::OneHundredTwentyEighth,
                        "256th" => GpDuration::TwoHundredFiftySixth,
                        _ => GpDuration::Quarter,
                    };
                    let dots = r_node
                        .child("AugmentationDot")
                        .and_then(|d| d.attr("count"))
                        .and_then(|v| v.trim().parse::<i32>().ok())
                        .unwrap_or(0);
                    let (tuplet_num, tuplet_den) = r_node
                        .child("PrimaryTuplet")
                        .map(|t| (parse_int(t.attr("num"), -1), parse_int(t.attr("den"), -1)))
                        .unwrap_or((-1, -1));
                    rhythms_by_id.insert(
                        id,
                        GpifRhythm {
                            duration,
                            dots,
                            tuplet_num,
                            tuplet_den,
                        },
                    );
                }
            }
            _ => {}
        }
    }

    let ordered_track_ids: Vec<String> = tracks_mapping
        .into_iter()
        .filter(|id| !id.is_empty() && tracks_by_id.contains_key(id))
        .collect();
    let mut track_sound_by_bar: Vec<HashMap<usize, u8>> = Vec::with_capacity(ordered_track_ids.len());
    let mut track_chords: Vec<Vec<HashMap<String, String>>> = Vec::with_capacity(ordered_track_ids.len());

    for tid in &ordered_track_ids {
        score.tracks.push(tracks_by_id.remove(tid).unwrap());
        track_chords.push(chords_by_track.remove(tid).unwrap_or_default());
        let mut sound_map = HashMap::new();
        if let Some(autos) = track_sound_automations.get(tid) {
            for TrackAutomationEvent::Sound { bar, program } in autos {
                sound_map.insert(*bar, *program);
            }
        }
        track_sound_by_bar.push(sound_map);
    }

    // Build vertical bar layout across tracks and staves, mirroring alphaTab _buildModel().
    for bar_ids in &bars_of_master_bar {
        let mut track_idx = 0usize;
        let mut staff_idx = 0usize;
        for bar_id in bar_ids {
            if track_idx >= score.tracks.len() {
                break;
            }
            if bar_id == "-1" {
                track_idx += 1;
                staff_idx = 0;
                continue;
            }

            let staff = &mut score.tracks[track_idx].staves[staff_idx];
            let chord_map = &track_chords[track_idx][staff_idx];
            let tuning_len = staff.tuning.len();
            let is_perc = staff.is_percussion;

            let mut bar = GpBar::default();
            if let Some(voice_ids) = bar_voices.get(bar_id) {
                for voice_id in voice_ids {
                    if voice_id == "-1" {
                        let mut empty_voice = GpVoice::default();
                        empty_voice.beats.push(GpBeat {
                            is_empty: true,
                            duration: GpDuration::Quarter,
                            ..GpBeat::default()
                        });
                        bar.voices.push(empty_voice);
                        continue;
                    }
                    let mut voice = GpVoice::default();
                    if let Some(b_ids) = voice_beats.get(voice_id) {
                        for beat_id in b_ids {
                            if beat_id == "-1" {
                                continue;
                            }
                            let mut beat = beats_by_id.get(beat_id).cloned().unwrap_or_default();
                            if let Some(rhythm) =
                                beat_rhythm.get(beat_id).and_then(|rid| rhythms_by_id.get(rid))
                            {
                                beat.duration = rhythm.duration;
                                beat.dots = rhythm.dots;
                                beat.tuplet_num = rhythm.tuplet_num;
                                beat.tuplet_den = rhythm.tuplet_den;
                            }
                            if let Some(cid) = beat_chord_id.get(beat_id) {
                                if let Some(name) = chord_map.get(cid) {
                                    if !name.is_empty() {
                                        beat.chord_name = Some(name.clone());
                                    }
                                }
                            }
                            let beat_vel = beat_chord_id
                                .get(&format!("__vel__{beat_id}"))
                                .and_then(|v| v.parse::<u8>().ok())
                                .unwrap_or(95);

                            if let Some(n_ids) = beat_notes.get(beat_id) {
                                for note_id in n_ids {
                                    if note_id == "-1" {
                                        continue;
                                    }
                                    if let Some((mut note, raw_string)) = notes_by_id.get(note_id).cloned()
                                    {
                                        note.velocity = if note.is_ghost {
                                            (beat_vel / 2).max(1)
                                        } else {
                                            beat_vel
                                        };
                                        if !is_perc && tuning_len > 0 {
                                            if let Some(s) = raw_string {
                                                if s >= 0 && (s as usize) < tuning_len {
                                                    note.string_idx =
                                                        Some(tuning_len - 1 - (s as usize));
                                                }
                                            }
                                        }
                                        beat.notes.push(note);
                                    }
                                }
                            }
                            voice.beats.push(beat);
                        }
                    }
                    bar.voices.push(voice);
                }
            }
            staff.bars.push(bar);

            if staff_idx + 1 >= score.tracks[track_idx].staves.len() {
                track_idx += 1;
                staff_idx = 0;
            } else {
                staff_idx += 1;
            }
        }
    }

    // Apply per-bar track program changes from Sound automations onto the first beat of that bar.
    for (track_idx, sound_map) in track_sound_by_bar.into_iter().enumerate() {
        for (bar_idx, program) in sound_map {
            if let Some(bar) = score
                .tracks
                .get_mut(track_idx)
                .and_then(|t| t.staves.first_mut())
                .and_then(|s| s.bars.get_mut(bar_idx))
            {
                if let Some(first_beat) = bar.voices.first_mut().and_then(|v| v.beats.first_mut()) {
                    if bar_idx > 0 || first_beat.program_change.is_none() {
                        first_beat.program_change = Some(program);
                    }
                }
            }
        }
    }

    score.master_bars = master_bars;
    Ok(score)
}

fn parse_track_or_staff_properties(
    props_node: &XmlNode,
    staves: &mut [GpStaff],
    staff_chords: &mut [HashMap<String, String>],
) {
    for prop in &props_node.children {
        if prop.name != "Property" {
            continue;
        }
        match prop.attr("name").unwrap_or("") {
            "Tuning" => {
                if let Some(pitches_str) = prop.child_text("Pitches") {
                    // GPIF stores low string first; reverse so index 0 is String 1 (high E).
                    let tuning: Vec<i32> = split_tokens(pitches_str)
                        .into_iter()
                        .rev()
                        .filter_map(|p| p.parse::<i32>().ok())
                        .collect();
                    if !tuning.is_empty() {
                        for staff in staves.iter_mut() {
                            staff.tuning = tuning.clone();
                        }
                    }
                }
            }
            "CapoFret" => {
                let capo = parse_int(prop.child_text("Fret"), 0);
                for staff in staves.iter_mut() {
                    staff.capo = capo;
                }
            }
            "DiagramCollection" | "ChordCollection" => {
                if let Some(items) = prop.child("Items") {
                    for item in &items.children {
                        if item.name != "Item" {
                            continue;
                        }
                        if let (Some(id), Some(name)) = (item.attr("id"), item.attr("name")) {
                            for map in staff_chords.iter_mut() {
                                map.insert(id.to_string(), name.trim().to_string());
                            }
                        }
                    }
                }
            }
            _ => {}
        }
    }
}
