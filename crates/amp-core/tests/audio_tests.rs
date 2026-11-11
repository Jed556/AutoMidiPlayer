use std::fs::File;
use std::sync::Arc;
use amp_core::audio::AudioDevice;
use rustysynth::SoundFont;

#[test]
fn test_soundfont_load_and_synth() {
    let sf_path = concat!(env!("CARGO_MANIFEST_DIR"), "/../../src-tauri/resources/soundfonts/GeneralUser GS.sf2");
    if let Ok(mut file) = File::open(sf_path) {
        let sf = SoundFont::new(&mut file).expect("Failed to parse GeneralUser GS.sf2");
        let sf_arc = Arc::new(sf);

        // Test AudioDevice initialization
        if let Ok(audio_dev) = AudioDevice::new(Some(sf_arc.clone())) {
            let synth = audio_dev.synth();
            // Test sending note on / off / CC
            synth.program_change(1, 40);
            synth.control_change(1, 7, 90);
            synth.control_change(1, 10, 32);
            synth.note_on(0, 60, 100);
            synth.control_change(0, 64, 127); // Sustain pedal on
            synth.set_master_volume(0.8);
            synth.set_track_volume(0, 0.9);
            synth.set_track_muted(1, true);
            synth.note_off(0, 60);
            synth.control_change(0, 64, 0); // Sustain pedal off

            // Test hot-swapping SoundFont while programs and CCs are active
            let legacy_path = concat!(env!("CARGO_MANIFEST_DIR"), "/../../src-tauri/resources/soundfonts/Roland GM.sf2");
            if let Ok(mut leg_file) = File::open(legacy_path) {
                if let Ok(leg_sf) = SoundFont::new(&mut leg_file) {
                    synth.load_soundfont(Arc::new(leg_sf)).unwrap();
                    synth.load_soundfont(sf_arc.clone()).unwrap();
                }
            }

            synth.all_notes_off();
            synth.reset();
        }
    }

    let legacy_sf_path = concat!(env!("CARGO_MANIFEST_DIR"), "/../../src-tauri/resources/soundfonts/Roland GM.sf2");
    if let Ok(mut file) = File::open(legacy_sf_path) {
        let sf = SoundFont::new(&mut file).expect("Failed to parse Roland GM.sf2");
        assert!(sf.get_presets().len() > 0);
    }
}
