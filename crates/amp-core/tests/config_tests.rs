use amp_core::{
    char_to_key, parse_key_str, scan_games, scan_keyboard_layouts,
};
use enigo::Key;
use std::fs;

fn key(c: char) -> Key { char_to_key(c).unwrap() }

#[test]
fn test_parse_key_str() {
    assert_eq!(parse_key_str("space"), Some(Key::Space));
    assert_eq!(parse_key_str("SPACE"), Some(Key::Space));
    assert_eq!(parse_key_str("enter"), Some(Key::Return));
    assert_eq!(parse_key_str("tab"), Some(Key::Tab));
    assert_eq!(parse_key_str("a"), Some(key('a')));
    assert_eq!(parse_key_str("A"), Some(key('a')));
    assert_eq!(parse_key_str("1"), Some(key('1')));
    assert_eq!(parse_key_str("^q"), Some(key('q')));
    assert_eq!(parse_key_str("!"), Some(Key::Unicode('!')));
    assert_eq!(parse_key_str(","), Some(Key::Unicode(',')));
}

#[test]
fn matrix_configs_preserve_rows_and_normalize_legacy_playback_fields() {
    use amp_core::{InstrumentConfigJson, KeyboardLayoutJson};
    let instrument: InstrumentConfigJson = serde_json::from_str(r#"{
        "id":"piano", "name":"Piano", "layoutStyle":"heartopia",
        "keys":[[72,null,73],[60,61]], "keyboardLayouts":["qwerty"]
    }"#).unwrap();
    assert_eq!(instrument.notes, [72, 73, 60, 61]);
    assert_eq!(instrument.keys.as_ref().unwrap()[0], [Some(72), None, Some(73)]);
    assert_eq!(serde_json::to_value(&instrument).unwrap()["layoutStyle"], "heartopia");
    let layout: KeyboardLayoutJson = serde_json::from_str(r#"{
        "id":"qwerty", "name":"QWERTY", "keys":[["q",null,"Q"],["a","A"]]
    }"#).unwrap();
    assert_eq!(layout.keys, ["q", "Q", "a", "A"]);
    assert_eq!(layout.key_rows.as_ref().unwrap()[0][1], None);
    let roundtrip: KeyboardLayoutJson = serde_json::from_value(serde_json::to_value(&layout).unwrap()).unwrap();
    assert_eq!(roundtrip.keys, layout.keys);
    assert_eq!(roundtrip.key_rows, layout.key_rows);
    assert!(serde_json::from_str::<InstrumentConfigJson>(r#"{"id":"x","name":"X","layoutStyle":"unknown"}"#).is_err());
}

#[test]
fn instrument_layout_groups_and_chords_serialize_canonically() {
    use amp_core::InstrumentConfigJson;
    let instrument: InstrumentConfigJson = serde_json::from_str(r#"{
        "id":"guitar", "name":"Guitar", "layoutStyle":"guitar", "notes":[60],
        "keyboardLayouts":["keyboard"], "chordLayouts":["wheel"], "fretLayouts":["frets"],
        "chords":[{"name":"C","pitchClasses":[0,4,7]}],
        "guitar":{"tuning":[64,59,55,50,45,40],"fretCount":24}
    }"#).unwrap();
    assert!(instrument.supports_keyboard_layout("KEYBOARD"));
    assert!(instrument.supports_chord_layout("wheel"));
    assert!(instrument.supports_fret_layout("frets"));
    assert!(!instrument.supports_chord_layout("keyboard"));
    assert_eq!(instrument.all_layouts().map(String::as_str).collect::<Vec<_>>(), ["keyboard", "wheel", "frets"]);
    assert!(instrument.guitar.as_ref().unwrap().is_valid());
    let json = serde_json::to_value(instrument).unwrap();
    assert_eq!(json["layoutStyle"], "guitar");
    assert_eq!(json["chords"][0]["name"], "C");
    assert!(json.get("supportedLayouts").is_none());
    assert!(json.get("chordPads").is_none());
}

#[test]
fn legacy_chord_definitions_remain_readable_but_explicit_layout_groups_win() {
    use amp_core::InstrumentConfigJson;
    let legacy: InstrumentConfigJson = serde_json::from_str(r#"{
        "id":"ukulele","name":"Ukulele", "supportedLayouts":["qwerty"],
        "chordPads":[{"name":"C","pitchClasses":[0,4,7]}]
    }"#).unwrap();
    assert_eq!(legacy.keyboard_layouts, ["qwerty"]);
    assert_eq!(legacy.chord_layouts, ["qwerty"]);
    assert_eq!(legacy.chords.len(), 1);
    let explicit: InstrumentConfigJson = serde_json::from_str(r#"{
        "id":"ukulele","name":"Ukulele", "supportedLayouts":["old"],
        "keyboardLayouts":["new"], "chordLayouts":[],
        "chords":[{"name":"C","pitchClasses":[0,4,7]}]
    }"#).unwrap();
    assert_eq!(explicit.keyboard_layouts, ["new"]);
    assert!(explicit.chord_layouts.is_empty());
}

#[test]
fn guitar_pitch_and_binding_configuration_rejects_unplayable_input() {
    use amp_core::{FretLayoutJson, GuitarConfigJson};
    let mut guitar = GuitarConfigJson { tuning: vec![64, 59, 55, 50, 45, 40], fret_count: 24 };
    assert!(guitar.is_valid());
    guitar.tuning[0] = 120;
    assert!(!guitar.is_valid());
    let mut layout = FretLayoutJson { string_keys: vec!["6".into(), "5".into(), "4".into(), "3".into(), "2".into(), "1".into()],
        wheel_layout: "NTE_Chord_Wheel".into(), mode_key: "z".into() };
    assert!(layout.is_valid());
    layout.string_keys[0] = "NotAKey".into();
    assert!(!layout.is_valid());
}

#[test]
fn test_scan_keyboard_layouts() {
    let temp_dir = std::env::temp_dir().join(format!("amp_cfg_test_{}", uuid::Uuid::new_v4()));
    let layouts_dir = temp_dir.join("KeyboardLayouts");
    fs::create_dir_all(&layouts_dir).unwrap();

    let qwerty_json = r#"{
        "id": "genshin_qwerty",
        "name": "QWERTY",
        "keys": ["q", "w", "e", "r", "t", "y", "u", "a", "s", "d", "f", "g", "h", "j", "z", "x", "c", "v", "b", "n", "m"],
        "chordKeys": ["1", "2", "3", "4", "5", "6", "7"],
        "sustainKey": "space"
    }"#;
    fs::write(layouts_dir.join("Genshin_QWERTY.json"), qwerty_json).unwrap();

    let layouts = scan_keyboard_layouts(&layouts_dir);
    assert!(layouts.contains_key("genshin_qwerty"));
    let layout = layouts.get("genshin_qwerty").unwrap();
    assert_eq!(layout.name, "QWERTY");
    assert_eq!(layout.keys.len(), 21);
    assert_eq!(layout.sustain_key.as_deref(), Some("space"));
    let _ = fs::remove_dir_all(&temp_dir);
}

#[test]
fn test_scan_games_recursive_instruments() {
    let temp_dir = std::env::temp_dir().join(format!("amp_cfg_test_{}", uuid::Uuid::new_v4()));
    let games_dir = temp_dir.join("Games");
    let genshin_dir = games_dir.join("Genshin Impact");
    let instruments_pianos = genshin_dir.join("Instruments").join("Pianos");
    let instruments_drums = genshin_dir.join("Instruments").join("Percussion");

    fs::create_dir_all(&instruments_pianos).unwrap();
    fs::create_dir_all(&instruments_drums).unwrap();

    // game.json
    let game_json = r#"{
        "id": "genshin-impact",
        "displayName": "Genshin Impact",
        "shortName": "Genshin",
        "processNames": ["GenshinImpact", "YuanShen"],
        "windowNames": ["Genshin Impact"],
        "icon": "icon.png",
        "defaultInstrument": "leaping-spirit-piano",
        "defaultLayout": "genshin_qwerty"
    }"#;
    fs::write(genshin_dir.join("game.json"), game_json).unwrap();
    fs::write(genshin_dir.join("icon.png"), b"fake_png").unwrap();

    // Instrument in Pianos subfolder
    let piano_json = r#"{
        "id": "leaping-spirit-piano",
        "name": "Leaping Spirit Piano",
        "type": "diatonic21",
        "notes": [72, 74, 76, 77, 79, 81, 83, 60, 62, 64, 65, 67, 69, 71, 48, 50, 52, 53, 55, 57, 59],
        "keyboardLayouts": ["genshin_qwerty"]
    }"#;
    fs::write(instruments_pianos.join("LeapingSpiritPiano.json"), piano_json).unwrap();

    // Instrument in Percussion subfolder
    let drum_json = r#"{
        "id": "djem-djem-drum",
        "name": "Djem Djem Drum",
        "type": "drum8",
        "notes": [60, 62, 64, 65, 67, 69, 71, 72],
        "keyboardLayouts": ["genshin_qwerty_drums"]
    }"#;
    fs::write(instruments_drums.join("DjemDjemDrum.json"), drum_json).unwrap();

    let games = scan_games(&games_dir);
    assert_eq!(games.len(), 1);

    let bundle = &games[0];
    assert_eq!(bundle.config.id, "genshin-impact");
    assert_eq!(bundle.config.short_name.as_deref(), Some("Genshin"));
    assert!(bundle.icon_path.is_some());
    assert_eq!(bundle.instruments.len(), 2);

    // Verify subfolder categorization
    let piano = bundle.instruments.iter().find(|i| i.id == "leaping-spirit-piano").unwrap();
    assert_eq!(piano.subfolder.as_deref(), Some("Pianos"));
    assert_eq!(piano.category.as_deref(), Some("Pianos"));

    let drum = bundle.instruments.iter().find(|i| i.id == "djem-djem-drum").unwrap();
    assert_eq!(drum.subfolder.as_deref(), Some("Percussion"));
    assert_eq!(drum.category.as_deref(), Some("Percussion"));

    let _ = fs::remove_dir_all(&temp_dir);
}
