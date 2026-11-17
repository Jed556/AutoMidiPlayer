use amp_core::{
    parse_key_stroke, InstrumentConfigJson, KeyboardLayout, KeyboardLayoutJson,
    NoteKeyMapping, OutputKey,
};

fn bundled() -> (InstrumentConfigJson, KeyboardLayoutJson) {
    let instrument = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/Games/Genshin Impact/Instruments/Strings/UkuleleChords.json"
    )).unwrap();
    let layout = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/Genshin_QWERTY_Chords.json"
    )).unwrap();
    (instrument, layout)
}

fn melody_mapping(instrument: &InstrumentConfigJson, layout: &KeyboardLayoutJson) -> NoteKeyMapping {
    // Retain coverage of custom combined mappings independently of the split defaults.
    let melody: KeyboardLayoutJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/Genshin_QWERTY_Melody.json"
    )).unwrap();
    let pairs: Vec<_> = instrument.notes.iter().copied().zip(melody.keys).collect();
    NoteKeyMapping::from_key_pairs(KeyboardLayout::Diatonic21, &pairs).with_pedal_bindings(layout)
}

#[test]
fn bundled_ukulele_maps_all_seven_pads_without_changing_notes_or_pedals() {
    let (instrument, mut layout) = bundled();
    layout.sustain_key = Some("space".into());
    layout.sostenuto_key = Some("Enter".into());
    layout.una_corda_key = Some("^a".into());
    let original = melody_mapping(&instrument, &layout);
    let mapping = original.clone().with_chord_pads(&instrument, &layout);

    let expected_pitch_classes: [&[u8]; 7] = [
        &[0, 4, 7], &[2, 5, 9], &[4, 7, 11], &[0, 5, 9],
        &[2, 7, 11], &[0, 4, 9], &[2, 5, 7, 11],
    ];
    assert_eq!(mapping.chord_pads().len(), 7);
    for ((pad, pitches), binding) in mapping.chord_pads().iter()
        .zip(expected_pitch_classes).zip(["q", "w", "e", "r", "t", "y", "u"]) {
        assert_eq!(pad.pitch_classes, pitches);
        assert_eq!(pad.binding, amp_core::input::mapping::ChordBinding::Key(parse_key_stroke(binding).unwrap()));
    }
    assert_eq!(mapping.playable_notes(), original.playable_notes());
    assert_eq!(mapping.pitch_range(), original.pitch_range());
    assert_eq!(mapping.layout(), original.layout());
    assert_eq!(mapping.pedal_bindings(), original.pedal_bindings());
    for pitch in &instrument.notes {
        assert_eq!(mapping.get_stroke(*pitch), original.get_stroke(*pitch));
    }
}

#[test]
fn genshin_chord_only_layout_preserves_detection_pitches_without_melody_output() {
    let layout: KeyboardLayoutJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/Genshin_QWERTY_Chords.json"
    )).unwrap();
    let melody: KeyboardLayoutJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/Genshin_QWERTY_Melody.json"
    )).unwrap();
    assert!(layout.keys.is_empty());
    assert!(melody.chord_keys.is_none());
    for source in [include_str!("../../../src-tauri/resources/defaults/Games/Genshin Impact/Instruments/Strings/UkuleleChords.json"),
        include_str!("../../../src-tauri/resources/defaults/Games/Genshin Impact/Instruments/Strings/LingeringEuphoniaChords.json")] {
        let instrument: InstrumentConfigJson = serde_json::from_str(source).unwrap();
        assert!(instrument.keyboard_layouts.is_empty());
        assert!(instrument.keys.is_none());
        assert_eq!(instrument.chord_layouts, [layout.id.clone()]);
        let mapping = NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[]).with_chord_pads(&instrument, &layout);
        assert_eq!(mapping.chord_pads().len(), 7);
        assert_eq!(mapping.pitch_range(), (48, 71));
        assert_eq!(mapping.playable_notes().len(), 14);
        assert!(instrument.notes.iter().all(|pitch| mapping.get_stroke(*pitch).is_none()));
        assert!(melody_mapping(&instrument, &melody).with_chord_pads(&instrument, &melody).chord_pads().is_empty());
    }
}

#[test]
fn unsupported_layout_does_not_acquire_pads_or_retain_previous_bindings() {
    let (instrument, layout) = bundled();
    let mapping = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    assert_eq!(mapping.chord_pads().len(), 7);
    let mut incompatible = layout.clone();
    incompatible.id = "Genshin_QWERTY_Two_Octaves".into();
    // A layout can have chord keys while remaining incompatible with this instrument.
    assert!(incompatible.chord_keys.is_some());
    assert!(mapping.with_chord_pads(&instrument, &incompatible).chord_pads().is_empty());
}

#[test]
fn missing_chord_keys_and_explicitly_disabled_support_clear_pads() {
    let (mut instrument, mut layout) = bundled();
    let mapping = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    let mut no_keys = layout.clone();
    no_keys.chord_keys = None;
    assert!(mapping.clone().with_chord_pads(&instrument, &no_keys).chord_pads().is_empty());
    layout.chord_keys = Some(Vec::new());
    assert!(mapping.clone().with_chord_pads(&instrument, &layout).chord_pads().is_empty());
    let (_, layout) = bundled();
    instrument.supports_chords = Some(false);
    assert!(mapping.with_chord_pads(&instrument, &layout).chord_pads().is_empty());
}

#[test]
fn invalid_and_missing_bindings_do_not_shift_pad_associations() {
    let (instrument, mut layout) = bundled();
    layout.chord_keys = Some(vec!["q".into(), "".into(), "^A".into(), "NotAKey".into(), "u".into()]);
    let mapping = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    let expected = [(0, "q"), (2, "^A"), (4, "u")];
    assert_eq!(mapping.chord_pads().len(), expected.len());
    for (pad, (index, binding)) in mapping.chord_pads().iter().zip(expected) {
        assert_eq!(pad.pitch_classes, instrument.chords[index].pitch_classes);
        assert_eq!(pad.binding, amp_core::input::mapping::ChordBinding::Key(parse_key_stroke(binding).unwrap()));
    }
}

#[test]
fn invalid_pitch_classes_do_not_shift_later_pad_bindings() {
    let (mut instrument, layout) = bundled();
    instrument.chords[1].pitch_classes = vec![2];
    instrument.chords[3].pitch_classes = vec![0, 5, 12];
    let mapping = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    let expected = [(0, "q"), (2, "e"), (4, "t"), (5, "y"), (6, "u")];
    assert_eq!(mapping.chord_pads().len(), expected.len());
    for (pad, (index, binding)) in mapping.chord_pads().iter().zip(expected) {
        assert_eq!(pad.pitch_classes, instrument.chords[index].pitch_classes);
        assert_eq!(pad.binding, amp_core::input::mapping::ChordBinding::Key(parse_key_stroke(binding).unwrap()));
    }
}

#[test]
fn panic_release_keys_include_pad_only_keys_and_modifiers() {
    let (instrument, mut layout) = bundled();
    layout.chord_keys = Some(vec!["^A".into(), "^Space".into()]);
    let original = melody_mapping(&instrument, &layout);
    assert!(!original.all_keys().contains(&OutputKey::Control));
    assert!(!original.all_keys().contains(&OutputKey::Shift));
    assert!(!original.all_keys().contains(&OutputKey::Space));
    let mapping = original.clone().with_chord_pads(&instrument, &layout);
    let all_keys = mapping.all_keys();
    for key in original.all_keys().into_iter().chain([
        OutputKey::Control, OutputKey::Shift, OutputKey::Space,
    ]) {
        assert!(all_keys.contains(&key), "Missing panic-release key: {key:?}");
    }
    let cleared = mapping.with_chord_bindings(Vec::new());
    assert!(!cleared.all_keys().contains(&OutputKey::Control));
    assert!(!cleared.all_keys().contains(&OutputKey::Shift));
    assert!(!cleared.all_keys().contains(&OutputKey::Space));
}

#[test]
fn nte_wheel_maps_all_pages_without_acquiring_piano_keys() {
    use amp_core::input::mapping::ChordBinding;
    let instrument: InstrumentConfigJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/Games/Neverness to Everness/Instruments/Strings/Guitar.json"
    )).unwrap();
    let layout: KeyboardLayoutJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Chord_Wheel.json"
    )).unwrap();
    let mapping = NoteKeyMapping::from_pairs(KeyboardLayout::Chromatic36, &[]).with_chord_pads(&instrument, &layout);
    assert_eq!(mapping.chord_pads().len(), 24);
    assert_eq!(mapping.pitch_range(), (52, 87));
    assert_eq!(mapping.playable_notes().len(), 36);
    assert!(mapping.all_keys().is_empty());
    for (index, pad) in mapping.chord_pads().iter().enumerate() {
        assert_eq!(pad.binding, ChordBinding::Wheel(index));
        assert_eq!(pad.pitch_classes, instrument.chords[index].pitch_classes);
    }
    assert!(mapping.chord_pads()[12].pitch_classes.len() == 2);
    assert_eq!(mapping.chord_pads()[18].bass_pitch_class, Some(11));
    assert!(instrument.notes.iter().all(|pitch| mapping.get_stroke(*pitch).is_none()));
}

#[test]
fn configured_chord_mode_selects_existing_pad_or_wheel_bindings() {
    use amp_core::input::{config::ChordMode, mapping::ChordBinding};
    let (mut instrument, mut layout) = bundled();
    let wheel: KeyboardLayoutJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Chord_Wheel.json"
    )).unwrap();
    layout.chord_wheel = wheel.chord_wheel;
    let pads = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    assert!(pads.chord_wheel().is_none());
    assert!(matches!(pads.chord_pads()[0].binding, ChordBinding::Key(_)));
    assert!(pads.get_stroke(60).is_some());
    instrument.chord_mode = ChordMode::Wheel;
    let radial = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    assert!(radial.chord_wheel().is_some());
    assert!(matches!(radial.chord_pads()[0].binding, ChordBinding::Wheel(0)));
    assert!(radial.get_stroke(60).is_none());
    layout.chord_wheel = None;
    let invalid = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    assert!(invalid.chord_pads().is_empty(), "wheel mode must not fall back to pad keys");
}
