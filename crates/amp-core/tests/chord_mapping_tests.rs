use amp_core::{
    parse_key_stroke, InstrumentConfigJson, KeyboardLayout, KeyboardLayoutJson,
    NoteKeyMapping, OutputKey,
};

fn bundled() -> (InstrumentConfigJson, KeyboardLayoutJson) {
    let instrument = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/Games/Genshin Impact/Instruments/Strings/Ukulele.json"
    )).unwrap();
    let layout = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/Genshin_QWERTY_Melody.json"
    )).unwrap();
    (instrument, layout)
}

fn melody_mapping(instrument: &InstrumentConfigJson, layout: &KeyboardLayoutJson) -> NoteKeyMapping {
    let pairs: Vec<_> = instrument.notes.iter().copied().zip(layout.keys.iter().cloned()).collect();
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
        assert_eq!(pad.stroke, parse_key_stroke(binding).unwrap());
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
        assert_eq!(pad.pitch_classes, instrument.chord_pads[index].pitch_classes);
        assert_eq!(pad.stroke, parse_key_stroke(binding).unwrap());
    }
}

#[test]
fn invalid_pitch_classes_do_not_shift_later_pad_bindings() {
    let (mut instrument, layout) = bundled();
    instrument.chord_pads[1].pitch_classes = vec![2, 5];
    instrument.chord_pads[3].pitch_classes = vec![0, 5, 12];
    let mapping = melody_mapping(&instrument, &layout).with_chord_pads(&instrument, &layout);
    let expected = [(0, "q"), (2, "e"), (4, "t"), (5, "y"), (6, "u")];
    assert_eq!(mapping.chord_pads().len(), expected.len());
    for (pad, (index, binding)) in mapping.chord_pads().iter().zip(expected) {
        assert_eq!(pad.pitch_classes, instrument.chord_pads[index].pitch_classes);
        assert_eq!(pad.stroke, parse_key_stroke(binding).unwrap());
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
