use amp_core::{
    parse_key_str, scan_games, scan_keyboard_layouts,
};
use enigo::Key;
use std::fs;

#[test]
fn test_parse_key_str() {
    assert_eq!(parse_key_str("space"), Some(Key::Space));
    assert_eq!(parse_key_str("SPACE"), Some(Key::Space));
    assert_eq!(parse_key_str("enter"), Some(Key::Return));
    assert_eq!(parse_key_str("tab"), Some(Key::Tab));
    assert_eq!(parse_key_str("a"), Some(Key::A));
    assert_eq!(parse_key_str("A"), Some(Key::A));
    assert_eq!(parse_key_str("1"), Some(Key::Num1));
    assert_eq!(parse_key_str("^q"), Some(Key::Q));
    assert_eq!(parse_key_str("!"), Some(Key::Unicode('!')));
    assert_eq!(parse_key_str(","), Some(Key::Unicode(',')));
}

#[test]
fn matrix_configs_preserve_rows_and_normalize_legacy_playback_fields() {
    use amp_core::{InstrumentConfigJson, KeyboardLayoutJson};
    let instrument: InstrumentConfigJson = serde_json::from_str(r#"{
        "id":"piano", "name":"Piano", "layoutStyle":"heartopia",
        "keys":[[72,null,73],[60,61]], "supportedLayouts":["qwerty"]
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
        "supportedLayouts": ["genshin_qwerty"]
    }"#;
    fs::write(instruments_pianos.join("LeapingSpiritPiano.json"), piano_json).unwrap();

    // Instrument in Percussion subfolder
    let drum_json = r#"{
        "id": "djem-djem-drum",
        "name": "Djem Djem Drum",
        "type": "drum8",
        "notes": [60, 62, 64, 65, 67, 69, 71, 72],
        "supportedLayouts": ["genshin_qwerty_drums"]
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
