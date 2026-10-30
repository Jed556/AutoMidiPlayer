use std::path::PathBuf;
use amp_core::{
    parse_midi_file, TempoMap, NoteKeyMapping, KeyboardLayout, TransposeMode,
    detect_best_scale, build_scale_pitch_classes, process_note_pitch, TimedNoteEvent,
};

#[test]
fn test_tempo_map_basic() {
    let map = TempoMap::new(480, &[]);
    assert_eq!(map.ppqn, 480);
    assert_eq!(map.tick_to_us(0), 0);
    assert_eq!(map.tick_to_us(480), 500_000);
    assert_eq!(map.tick_to_us(960), 1_000_000);

    assert_eq!(map.us_to_tick(0), 0);
    assert_eq!(map.us_to_tick(500_000), 480);
    assert_eq!(map.us_to_tick(1_000_000), 960);
    assert!((map.initial_bpm() - 120.0).abs() < 0.01);
}

#[test]
fn test_tempo_map_with_changes() {
    let map = TempoMap::new(480, &[(0, 500_000), (480, 1_000_000)]);
    assert_eq!(map.tick_to_us(0), 0);
    assert_eq!(map.tick_to_us(480), 500_000);
    assert_eq!(map.tick_to_us(960), 1_500_000);
}

#[test]
fn test_key_mapping() {
    let mapping = NoteKeyMapping::new(KeyboardLayout::Diatonic21);
    // C4 (Middle C) -> 'a'
    assert_eq!(mapping.get_key(60), Some(enigo::Key::A));
    // D4 -> 's'
    assert_eq!(mapping.get_key(62), Some(enigo::Key::S));
    // C3 -> 'z'
    assert_eq!(mapping.get_key(48), Some(enigo::Key::Z));
    // C5 -> 'q'
    assert_eq!(mapping.get_key(72), Some(enigo::Key::Q));
}

#[test]
fn test_diatonic15_and_drum8_layouts() {
    let sky_mapping = NoteKeyMapping::new(KeyboardLayout::Diatonic15);
    assert_eq!(sky_mapping.get_key(60), Some(enigo::Key::Q));
    assert_eq!(sky_mapping.get_key(62), Some(enigo::Key::W));
    assert_eq!(sky_mapping.get_key(84), Some(enigo::Key::G));
    assert_eq!(sky_mapping.playable_notes().len(), 15);

    let drum_mapping = NoteKeyMapping::new(KeyboardLayout::Drum8);
    assert_eq!(drum_mapping.get_key(36), Some(enigo::Key::Num1));
    assert_eq!(drum_mapping.get_key(38), Some(enigo::Key::Num2));
    assert_eq!(drum_mapping.playable_notes().len(), 8);
}

#[test]
fn test_custom_key_mapping_from_pairs() {
    let custom = NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[
        (60, 'z'),
        (62, 'x'),
        (64, 'c'),
    ]);
    assert_eq!(custom.get_key(60), Some(enigo::Key::Z));
    assert_eq!(custom.get_key(62), Some(enigo::Key::X));
    assert_eq!(custom.get_key(64), Some(enigo::Key::C));
}

#[test]
fn test_smart_mode_detection() {
    // Generate C Major diatonic notes (C4, D4, E4, F4, G4, A4, B4)
    let notes = vec![
        TimedNoteEvent { time_us: 0, note: 60, velocity: 80, is_note_on: true, channel: 0, track_index: 0 },
        TimedNoteEvent { time_us: 100, note: 62, velocity: 80, is_note_on: true, channel: 0, track_index: 0 },
        TimedNoteEvent { time_us: 200, note: 64, velocity: 80, is_note_on: true, channel: 0, track_index: 0 },
        TimedNoteEvent { time_us: 300, note: 65, velocity: 80, is_note_on: true, channel: 0, track_index: 0 },
        TimedNoteEvent { time_us: 400, note: 67, velocity: 80, is_note_on: true, channel: 0, track_index: 0 },
        TimedNoteEvent { time_us: 500, note: 69, velocity: 80, is_note_on: true, channel: 0, track_index: 0 },
        TimedNoteEvent { time_us: 600, note: 71, velocity: 80, is_note_on: true, channel: 0, track_index: 0 },
    ];

    let (tonic, mode_idx) = detect_best_scale(&notes, 60);
    // Should detect C (0) Ionian (0)
    assert_eq!(tonic, 0);
    assert_eq!(mode_idx, 0);

    assert_eq!(build_scale_pitch_classes(tonic, mode_idx).len(), 7);
    let mapping = NoteKeyMapping::new(KeyboardLayout::Diatonic21);
    let playable = mapping.playable_notes();

    // In-scale note (C4=60) returns 60 directly
    let mapped = process_note_pitch(60, 0, TransposeMode::Smart, 48, 83, playable);
    assert_eq!(mapped, Some(60));

    // Legacy resolves this equal-distance accidental to the lower playable note.
    let mapped_accidental = process_note_pitch(61, 0, TransposeMode::Smart, 48, 83, playable);
    assert_eq!(mapped_accidental, Some(60));

    // Out-of-range low note (C2=36) folds up by octaves to C3=48 or C4=60
    let mapped_folded = process_note_pitch(36, 0, TransposeMode::Smart, 48, 83, playable);
    assert_eq!(mapped_folded, Some(48));
}

#[test]
fn smart_preserves_still_with_you_melody_dip_like_legacy() {
    let mapping = NoteKeyMapping::new(KeyboardLayout::Diatonic21);
    let (min, max) = mapping.pitch_range();
    // Lead notes around 3.77–4.97 s in jungkook-still-with-you.mid.
    // C#5, C#5, C5, C5, C#5 at -4 must retain the dip to G4.
    let mapped: Vec<_> = [73, 73, 72, 72, 73].into_iter().map(|pitch|
        process_note_pitch(pitch, -4, TransposeMode::Smart, min, max, mapping.playable_notes())
    ).collect();
    assert_eq!(mapped, vec![Some(69), Some(69), Some(67), Some(67), Some(69)]);
}

#[test]
fn smart_uses_current_instrument_and_preserves_playable_pitches() {
    let diatonic = NoteKeyMapping::new(KeyboardLayout::Diatonic21);
    let (min, max) = diatonic.pitch_range();
    for &pitch in diatonic.playable_notes() {
        assert_eq!(process_note_pitch(pitch, 0, TransposeMode::Smart, min, max,
            diatonic.playable_notes()), Some(pitch));
    }
    assert_eq!(process_note_pitch(68, 0, TransposeMode::Smart, min, max,
        diatonic.playable_notes()), Some(67));
    let chromatic: Vec<_> = (48..=83).collect();
    assert_eq!(process_note_pitch(68, 0, TransposeMode::Smart, 48, 83, &chromatic), Some(68));
    assert_eq!(process_note_pitch(80, 0, TransposeMode::Smart, 60, 71,
        &[60, 62, 64, 65, 67, 69, 71]), Some(67));
    assert_eq!(process_note_pitch(68, 0, TransposeMode::Smart, 65, 65, &[65]), Some(65));
    assert_eq!(process_note_pitch(68, 0, TransposeMode::Smart, 0, 127, &[]), None);
}

#[test]
fn test_parse_real_midi() {
    let path = PathBuf::from(r"..\..\MIDIs\test.mid");
    if path.exists() {
        let midi = parse_midi_file(&path).expect("Failed to parse test.mid");
        assert!(midi.duration_us > 0, "Duration should be positive");
        assert!(!midi.events.is_empty(), "Events should not be empty");
        assert!(!midi.tracks.is_empty(), "Tracks should not be empty");
    }
}

#[test]
fn test_gm_instrument_names() {
    use amp_core::get_gm_instrument_name;
    assert_eq!(get_gm_instrument_name(0), "Acoustic Grand Piano");
    assert_eq!(get_gm_instrument_name(4), "Electric Piano 1");
    assert_eq!(get_gm_instrument_name(24), "Acoustic Guitar (nylon)");
    assert_eq!(get_gm_instrument_name(40), "Violin");
    assert_eq!(get_gm_instrument_name(56), "Trumpet");
    assert_eq!(get_gm_instrument_name(73), "Flute");
}
