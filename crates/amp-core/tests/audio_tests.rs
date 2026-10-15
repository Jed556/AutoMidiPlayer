use std::fs::File;
use std::sync::Arc;
use amp_core::audio::AudioDevice;
use rustysynth::SoundFont;

#[test]
fn test_soundfont_load_and_synth() {
    let sf_path = concat!(env!("CARGO_MANIFEST_DIR"), "/../../src-tauri/resources/default.sf2");
    if let Ok(mut file) = File::open(sf_path) {
        let sf = SoundFont::new(&mut file).expect("Failed to parse default.sf2");
        let sf_arc = Arc::new(sf);

        // Test AudioDevice initialization
        if let Ok(audio_dev) = AudioDevice::new(Some(sf_arc.clone())) {
            let synth = audio_dev.synth();
            // Test sending note on / off / CC
            synth.note_on(0, 60, 100);
            synth.control_change(0, 64, 127); // Sustain pedal on
            synth.set_master_volume(0.8);
            synth.set_track_volume(0, 0.9);
            synth.set_track_muted(1, true);
            synth.note_off(0, 60);
            synth.control_change(0, 64, 0); // Sustain pedal off
            synth.all_notes_off();
            synth.reset();
        }
    }
}
