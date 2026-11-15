use std::collections::HashSet;

use amp_core::{
    char_to_key, parse_key_stroke, process_note_pitch, InstrumentConfigJson,
    KeyboardLayout, KeyboardLayoutJson, KeyStroke, NoteKeyMapping, TransposeMode,
};
use enigo::Key;

fn key(c: char) -> Key { char_to_key(c).unwrap() }

fn stroke(key: Key, shift: bool, control: bool) -> KeyStroke {
    KeyStroke { key, shift, control }
}

#[test]
fn roblox_bindings_preserve_shift_ctrl_and_the_literal_caret() {
    for (text, expected) in [
        ("q", stroke(key('q'), false, false)),
        ("Q", stroke(key('q'), true, false)),
        ("!", stroke(key('1'), true, false)),
        ("@", stroke(key('2'), true, false)),
        ("$", stroke(key('4'), true, false)),
        ("%", stroke(key('5'), true, false)),
        ("^", stroke(key('6'), true, false)),
        ("*", stroke(key('8'), true, false)),
        ("(", stroke(key('9'), true, false)),
        ("^1", stroke(key('1'), false, true)),
        ("^q", stroke(key('q'), false, true)),
        ("^A", stroke(key('a'), true, true)),
        ("Enter", stroke(Key::Return, false, false)),
        ("space", stroke(Key::Space, false, false)),
        (" ", stroke(Key::Space, false, false)),
    ] {
        assert_eq!(parse_key_stroke(text), Some(expected), "{text}");
    }
    assert_eq!(parse_key_stroke(""), None);
    assert_eq!(parse_key_stroke("not-a-key"), None);
}

#[test]
fn mixed_chords_scope_modifiers_to_each_key_down() {
    let mut events = Vec::new();
    for text in ["^A", "q", "Q", "1"] {
        events.extend(parse_key_stroke(text).unwrap().press_events());
    }
    assert_eq!(events, vec![
        (Key::Control, false), (Key::Shift, false), (key('a'), false),
        (Key::Shift, true), (Key::Control, true),
        (key('q'), false),
        (Key::Shift, false), (key('q'), false), (Key::Shift, true),
        (key('1'), false),
    ]);
}

fn roblox_mapping(instrument: &str, layout: &str) -> NoteKeyMapping {
    let instrument: InstrumentConfigJson = serde_json::from_str(instrument).unwrap();
    let layout: KeyboardLayoutJson = serde_json::from_str(layout).unwrap();
    assert_eq!(instrument.notes.len(), layout.keys.len());
    let pairs: Vec<_> = instrument.notes.into_iter().zip(layout.keys.iter().cloned()).collect();
    NoteKeyMapping::from_key_pairs(KeyboardLayout::Chromatic36, &pairs).with_pedal_bindings(&layout)
}

fn piano61() -> NoteKeyMapping {
    roblox_mapping(
        include_str!("../../../src-tauri/resources/defaults/Games/Roblox/Instruments/Pianos/Piano61Key.json"),
        include_str!("../../../src-tauri/resources/defaults/KeyboardLayouts/QWERTY_61Keys.json"),
    )
}

fn piano88() -> NoteKeyMapping {
    roblox_mapping(
        include_str!("../../../src-tauri/resources/defaults/Games/Roblox/Instruments/Pianos/Piano88Key.json"),
        include_str!("../../../src-tauri/resources/defaults/KeyboardLayouts/QWERTY_88Keys.json"),
    )
}

#[test]
fn every_configured_roblox_pitch_has_a_distinct_binding_without_transposition() {
    for (mapping, low, high) in [(piano61(), 36, 96), (piano88(), 21, 108)] {
        assert_eq!(mapping.pitch_range(), (low, high));
        let mut bindings = HashSet::new();
        for note in low..=high {
            // Even accidentals and extended-range notes stay unchanged when
            // Smart is off. Pitch adaptation cannot repair a broken key binding.
            let output = process_note_pitch(note, 0, TransposeMode::Ignore,
                low, high, mapping.playable_notes()).unwrap();
            assert_eq!(output, note);
            assert!(bindings.insert(mapping.get_stroke(output).unwrap()), "duplicate binding for {note}");
        }
        assert_eq!(bindings.len(), (high - low + 1) as usize);
    }
}

#[test]
fn piano88_extended_ranges_use_the_original_players_ctrl_keys() {
    let mapping = piano88();
    for (note, expected) in [
        (21, stroke(key('1'), false, true)),
        (35, stroke(key('t'), false, true)),
        (36, stroke(key('1'), false, false)),
        (37, stroke(key('1'), true, false)),
        (46, stroke(key('6'), true, false)),
        (54, stroke(key('q'), true, false)),
        (60, stroke(key('t'), false, false)),
        (61, stroke(key('t'), true, false)),
        (96, stroke(key('m'), false, false)),
        (97, stroke(key('y'), false, true)),
        (108, stroke(key('j'), false, true)),
    ] {
        assert_eq!(mapping.get_stroke(note), Some(expected), "MIDI {note}");
    }
    let strokes: Vec<_> = mapping.playable_notes().iter()
        .map(|&note| mapping.get_stroke(note).unwrap()).collect();
    assert_eq!(strokes.iter().filter(|s| s.control).count(), 27);
    assert_eq!(strokes.iter().filter(|s| s.shift).count(), 25);
    let keys = mapping.all_keys();
    assert!(keys.contains(&Key::Shift));
    assert!(keys.contains(&Key::Control));
    assert_eq!(keys.len(), keys.iter().collect::<HashSet<_>>().len());
}

#[test]
fn both_roblox_pianos_use_space_for_sustain_and_release_it_during_cleanup() {
    for mapping in [piano61(), piano88()] {
        assert_eq!(mapping.pedal_bindings(), &[Some(KeyStroke::plain(Key::Space)), None, None]);
        assert!(mapping.all_keys().contains(&Key::Space));
    }
}

#[test]
fn edited_layout_pedal_fields_replace_and_clear_native_bindings() {
    let mut layout: KeyboardLayoutJson = serde_json::from_str(
        include_str!("../../../src-tauri/resources/defaults/KeyboardLayouts/QWERTY_61Keys.json")
    ).unwrap();
    layout.sustain_key = None;
    layout.sostenuto_key = Some("Shift".into());
    layout.una_corda_key = Some("q".into());
    let mapping = piano61().with_pedal_bindings(&layout);
    assert_eq!(mapping.pedal_bindings(), &[
        None, Some(KeyStroke::plain(Key::Shift)), Some(KeyStroke::plain(key('q')))
    ]);
    assert!(!mapping.all_keys().contains(&Key::Space));
    assert!(mapping.all_keys().contains(&Key::Shift));
}

#[test]
fn pedal_recording_named_keys_resolve_to_native_keys() {
    for (binding, key) in [
        ("Control", Key::Control), ("Shift", Key::Shift), ("Alt", Key::Alt),
        ("ArrowUp", Key::UpArrow), ("ArrowDown", Key::DownArrow),
        ("Delete", Key::Delete), ("PageDown", Key::PageDown),
        ("Numpad0", Key::Numpad0), ("NumpadAdd", Key::Add),
        ("F1", Key::F1), ("F12", Key::F12),
    ] {
        assert_eq!(parse_key_stroke(binding), Some(KeyStroke::plain(key)), "{binding}");
    }
}

#[cfg(not(windows))]
#[test]
fn unix_modifier_names_resolve_to_native_keys() {
    for (binding, key) in [
        ("Option", Key::Alt), ("Command", Key::Meta), ("Cmd", Key::Meta),
        ("Super", Key::Meta), ("Windows", Key::Meta),
    ] {
        assert_eq!(parse_key_stroke(binding), Some(KeyStroke::plain(key)), "{binding}");
    }
}

#[test]
fn pigstep_regression_preserves_playable_pitches_with_smart_off() {
    let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../MIDIs/Pigstep - Lena Raine.mid");
    if !path.is_file() { return; } // Local reproduction; exhaustive coverage above runs everywhere.
    let song = amp_core::parse_midi_file(path).unwrap();
    let mapping = piano88();
    let mut total = 0;
    let mut shifted = 0;
    let mut controlled = 0;
    let mut outside = 0;
    let mut pitches = HashSet::new();
    for event in song.events.iter().filter(|e| e.is_note_on && e.velocity > 0) {
        total += 1;
        let output = process_note_pitch(event.note, 0, TransposeMode::Ignore,
            21, 108, mapping.playable_notes());
        if !(21..=108).contains(&event.note) {
            outside += 1;
            assert_eq!(output, None);
            continue;
        }
        assert_eq!(output, Some(event.note));
        let binding = mapping.get_stroke(output.unwrap()).unwrap();
        shifted += usize::from(binding.shift);
        controlled += usize::from(binding.control);
        pitches.insert(event.note);
    }
    assert!(total > 0);
    println!("Pigstep: {total} note-ons, {} distinct playable pitches, {shifted} Shift notes, {controlled} Ctrl notes, {outside} out of 88-key range", pitches.len());
}
