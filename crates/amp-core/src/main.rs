use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;
use amp_core::{
    parse_midi_file, KeyboardLayout, NoteKeyMapping, PlaybackEngine, PlayerEvent, PlaybackStatus,
};

fn main() {
    env_logger::init();

    println!("========================================");
    println!("  Auto MIDI Player (AMP) - Rust Core CLI");
    println!("========================================");

    // 1. Locate MIDI file argument or use default test MIDI
    let args: Vec<String> = std::env::args().collect();
    let midi_path = if args.len() > 1 {
        PathBuf::from(&args[1])
    } else {
        // Fallback to test.mid in the workspace
        #[cfg(windows)]
        {
            let default_path = PathBuf::from(r"..\MIDIs\test.mid");
            if default_path.exists() { default_path } else {
                let alt_path = PathBuf::from(r"C:\Users\Jed556\CodeProjects\GitHub\Games\Auto MIDI Player\MIDIs\test.mid");
                if alt_path.exists() { alt_path } else {
                    eprintln!("Usage: amp-cli <path-to-midi-file>");
                    std::process::exit(1);
                }
            }
        }
        #[cfg(not(windows))]
        {
        let candidates = [
            PathBuf::from("..").join("MIDIs").join("test.mid"),
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../MIDIs/test.mid"),
        ];
        candidates.into_iter().find(|path| path.is_file()).unwrap_or_else(|| {
            eprintln!("Usage: amp-cli <path-to-midi-file>");
            std::process::exit(1);
        })
        }
    };

    println!("\nLoading MIDI: {}", midi_path.display());
    let parsed = match parse_midi_file(&midi_path) {
        Ok(m) => m,
        Err(e) => {
            eprintln!("Failed to parse MIDI: {e}");
            std::process::exit(1);
        }
    };

    println!("\n[MIDI Metadata]");
    println!("  Title:       {}", parsed.title);
    println!("  Duration:    {:.2}s", (parsed.duration_us as f64) / 1_000_000.0);
    println!("  BPM:         {:.1}", parsed.bpm);
    println!("  PPQN:        {}", parsed.ppqn);
    println!("  Total Notes: {}", parsed.events.len());
    println!("  Tracks ({}):", parsed.tracks.len());
    for t in &parsed.tracks {
        println!("    - [#{}] {} (Notes: {}, Channel: {})", t.index + 1, t.name, t.note_count, t.channel);
    }

    // 2. Setup mapping
    let mapping = NoteKeyMapping::new(KeyboardLayout::Diatonic21);

    // 3. Prepare countdown
    println!("\n------------------------------------------------");
    println!("READY TO PLAY!");
    #[cfg(windows)]
    println!("Switch to Notepad (or in-game instrument) now!");
    #[cfg(not(windows))]
    println!("Switch to your in-game instrument or a text editor now!");
    println!("------------------------------------------------");

    for i in (1..=3).rev() {
        println!("Starting in {i}...");
        std::thread::sleep(Duration::from_secs(1));
    }
    println!("PLAYING NOW! (Press Ctrl+C to abort)\n");

    let is_running = Arc::new(AtomicBool::new(true));
    let is_running_clone = Arc::clone(&is_running);

    let ctrl_c_running = Arc::clone(&is_running);
    let _ = ctrlc::set_handler(move || {
        println!("\nStopping playback...");
        ctrl_c_running.store(false, Ordering::SeqCst);
    });

    let engine = PlaybackEngine::new(
        Some(parsed),
        mapping,
        true, // Enable native keyboard simulation
        None,
        move |event| match event {
            PlayerEvent::NoteHit { note, is_on } => {
                if is_on {
                    if let Some(key) = NoteKeyMapping::new(KeyboardLayout::Diatonic21).get_key(note) {
                        let char_key = NoteKeyMapping::key_to_char(key);
                        print!("[Note {note} -> '{char_key}'] ");
                    }
                }
            }
            PlayerEvent::Finished => {
                println!("\n\nPlayback complete!");
                is_running_clone.store(false, Ordering::SeqCst);
            }
            PlayerEvent::StateChanged(status) => {
                #[cfg(not(windows))]
                if status == PlaybackStatus::Paused {
                    eprintln!("Playback paused. Check keyboard output permissions.");
                    is_running_clone.store(false, Ordering::SeqCst);
                }
                if status == PlaybackStatus::Stopped {
                    is_running_clone.store(false, Ordering::SeqCst);
                }
            }
            _ => {}
        },
    );

    #[cfg(not(windows))]
    if let Err(error) = engine.prepare_keyboard() {
        eprintln!("Keyboard output is unavailable: {error}");
        eprintln!("On Wayland, use the desktop app to authorize keyboard access.");
        return;
    }
    engine.play();

    while is_running.load(Ordering::SeqCst) {
        std::thread::sleep(Duration::from_millis(100));
    }

    engine.stop();
    println!("Engine stopped.");
}
