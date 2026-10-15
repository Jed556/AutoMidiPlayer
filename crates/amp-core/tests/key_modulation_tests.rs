use amp_core::midi::{
    calculate_key_signature_offset, normalize_detected_key_offset,
    KeyChangeMarker, TransposeMode,
};

#[test]
fn test_circle_of_fifths_offsets() {
    // C Major (0 sharps/flats): tonic = C (0), offset to C = 0
    let (offset_c, label_c) = calculate_key_signature_offset(0, false);
    assert_eq!(offset_c, 0);
    assert_eq!(label_c, "C maj");

    // G Major (1 sharp): tonic = G (7), transposition to C = -7 => 5 (or -7 mod 12 is 5)
    let (offset_g, label_g) = calculate_key_signature_offset(1, false);
    assert_eq!(offset_g, 5);
    assert_eq!(label_g, "G maj");

    // D Major (2 sharps): tonic = D (2), transposition to C = -2
    let (offset_d, label_d) = calculate_key_signature_offset(2, false);
    assert_eq!(offset_d, -2);
    assert_eq!(label_d, "D maj");

    // F Major (1 flat = -1): tonic = F (5), transposition to C = -5
    let (offset_f, label_f) = calculate_key_signature_offset(-1, false);
    assert_eq!(offset_f, -5);
    assert_eq!(label_f, "F maj");

    // A Minor (0 sharps/flats, minor): tonic = A (9), transposition to C = -9 => +3
    let (offset_am, label_am) = calculate_key_signature_offset(0, true);
    assert_eq!(offset_am, 3);
    assert_eq!(label_am, "A min");
}

#[test]
fn test_normalize_detected_key_offset() {
    assert_eq!(normalize_detected_key_offset(0), 0);   // C -> 0
    assert_eq!(normalize_detected_key_offset(2), -2);  // D -> -2
    assert_eq!(normalize_detected_key_offset(4), -4);  // E -> -4
    assert_eq!(normalize_detected_key_offset(5), -5);  // F -> -5
    assert_eq!(normalize_detected_key_offset(7), 5);   // G -> +5
    assert_eq!(normalize_detected_key_offset(9), 3);   // A -> +3
    assert_eq!(normalize_detected_key_offset(11), 1);  // B -> +1
}

#[test]
fn test_key_change_marker_serde() {
    let marker = KeyChangeMarker {
        time_ms: 1250,
        key_offset: -3,
        transpose: Some(TransposeMode::Smart),
        label: "Eb maj".to_string(),
    };

    let json = serde_json::to_string(&marker).expect("Failed to serialize marker");
    assert!(json.contains("\"timeMs\":1250"));
    assert!(json.contains("\"keyOffset\":-3"));
    assert!(json.contains("\"transpose\":\"Smart\""));
    assert!(json.contains("\"label\":\"Eb maj\""));

    let deserialized: KeyChangeMarker = serde_json::from_str(&json).expect("Failed to deserialize marker");
    assert_eq!(deserialized, marker);
}

#[test]
fn test_detect_from_meta_key_signatures() {
    use amp_core::midi::{detect_key_changes, ParsedMidi, RawKeySignature, TempoMap};

    let midi = ParsedMidi {
        title: "Meta Signature Test".to_string(),
        bpm: 120.0,
        ppqn: 480,
        tracks: vec![],
        events: vec![],
        control_events: vec![],
        duration_us: 1_000_000,
        tempo_map: TempoMap::new(480, &[]),
        key_signatures: vec![
            RawKeySignature {
                tick: 0,
                time_us: 0,
                time_ms: 0,
                sf: 2,
                is_minor: false,
                key_offset: -2,
                label: "D maj".to_string(),
            },
            RawKeySignature {
                tick: 480,
                time_us: 500_000,
                time_ms: 500,
                sf: 0,
                is_minor: false,
                key_offset: 0,
                label: "C maj".to_string(),
            },
        ],
    };

    let markers = detect_key_changes(&midi);
    assert_eq!(markers.len(), 2);
    assert_eq!(markers[0].time_ms, 0);
    assert_eq!(markers[0].key_offset, -2);
    assert_eq!(markers[0].label, "D maj");

    assert_eq!(markers[1].time_ms, 500);
    assert_eq!(markers[1].key_offset, 0);
    assert_eq!(markers[1].label, "C maj");
}

#[test]
fn test_detect_from_pitch_profile_fallback() {
    use amp_core::midi::{detect_key_changes, ParsedMidi, TimedNoteEvent, TempoMap};

    // Play C Major triad multiple times (C=60, E=64, G=67)
    let mut events = Vec::new();
    for i in 0..10 {
        let t = i * 100_000;
        events.push(TimedNoteEvent {
            time_us: t,
            track_index: 0,
            channel: 0,
            note: 60, // C
            velocity: 100,
            is_note_on: true,
        });
        events.push(TimedNoteEvent {
            time_us: t + 20_000,
            track_index: 0,
            channel: 0,
            note: 64, // E
            velocity: 100,
            is_note_on: true,
        });
        events.push(TimedNoteEvent {
            time_us: t + 40_000,
            track_index: 0,
            channel: 0,
            note: 67, // G
            velocity: 100,
            is_note_on: true,
        });
    }

    let midi = ParsedMidi {
        title: "Fallback Profile Test".to_string(),
        bpm: 120.0,
        ppqn: 480,
        tracks: vec![],
        events,
        control_events: vec![],
        duration_us: 1_000_000,
        tempo_map: TempoMap::new(480, &[]),
        key_signatures: vec![], // No meta events -> triggers fallback profile
    };

    let markers = detect_key_changes(&midi);
    assert_eq!(markers.len(), 1);
    assert_eq!(markers[0].time_ms, 0);
    assert_eq!(markers[0].key_offset, 0);
    assert_eq!(markers[0].label, "C maj");
}
