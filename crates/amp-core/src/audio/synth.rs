use std::collections::{HashMap, HashSet};
use std::num::{NonZeroU16, NonZeroU32};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use crossbeam_channel::{bounded, unbounded, Receiver, RecvTimeoutError, Sender};
use rodio::cpal::{self, traits::{DeviceTrait, HostTrait}};
use rodio::{DeviceSinkBuilder, MixerDeviceSink, Player, Source};
use rustysynth::{SoundFont, SynthesizerSettings};
use super::font_stack::FontStack;
use super::font_loader::{FontLoader, FontInput, PreparedSoundFont};

const SAMPLE_RATE: u32 = 44100;
const BLOCK_SIZE: usize = 256;

#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum SynthMode {
    Adapted,
    Raw,
}

impl Default for SynthMode {
    fn default() -> Self {
        match crate::settings::defaults::value::<String>("/ui/playback/synthMode").as_str() {
            "adapted" => Self::Adapted,
            "raw" => Self::Raw,
            value => panic!("Invalid default synth mode: {value}"),
        }
    }
}

#[derive(Debug, thiserror::Error)]
pub enum AudioError {
    #[error("Failed to initialize audio output stream: {0}")]
    StreamInit(String),
    #[error("Failed to initialize audio sink: {0}")]
    SinkInit(String),
    #[error("Failed to parse SoundFont: {0}")]
    SoundFontParse(String),
    #[error("SoundFont IO error: {0}")]
    Io(#[from] std::io::Error),
}

pub enum AudioCommand {
    NoteOn { channel: u8, note: u8, velocity: u8 },
    NoteOff { channel: u8, note: u8 },
    ControlChange { channel: u8, controller: u8, value: u8 },
    ProgramChange { channel: u8, program: u8 },
    SetEnforceNoteDurations(bool),
    SetMasterVolume(f32),
    SetTrackMuted { track_index: usize, muted: bool },
    SetTrackVolume { track_index: usize, volume: f32 },
    InstallSoundFont(Box<PreparedSoundFont>),
    ClearSoundFonts,
    AllNotesOff,
    Reset,
}

/// Synthesizer state shared across output streams.
///
/// The OS output stream may need to be torn down and rebuilt (e.g. when the user switches
/// the system default audio device). Keeping the synth state here, behind a shared handle,
/// lets a fresh stream pick up exactly where the previous one left off (loaded SoundFont,
/// volumes, programs, sounding notes) instead of resetting everything.
struct RetiringStack { stack: Box<FontStack>, quiet_samples: u32 }

struct SynthEngine {
    synth: Option<Box<FontStack>>,
    retiring: Vec<RetiringStack>,
    reclaim_tx: Sender<Box<FontStack>>,
    tail_left: Vec<f32>,
    tail_right: Vec<f32>,
    cmd_rx: Receiver<AudioCommand>,
    sample_rate: u32,
    master_volume: f32,
    track_volumes: HashMap<usize, f32>,
    muted_tracks: HashSet<usize>,
    channel_programs: [u8; 16],
    channel_controllers: [HashMap<u8, u8>; 16],
    enforce_note_durations: bool,
    left_buf: Vec<f32>,
    right_buf: Vec<f32>,
}

impl SynthEngine {
    fn new(initial_soundfont: Option<Arc<SoundFont>>, cmd_rx: Receiver<AudioCommand>) -> Self {
        let synth = initial_soundfont.and_then(|sf| {
            let settings = SynthesizerSettings::new(SAMPLE_RATE as i32);
            FontStack::new(vec![sf], &settings).ok().map(Box::new)
        });

        let (reclaim_tx, reclaim_rx) = unbounded::<Box<FontStack>>();
        std::thread::Builder::new().name("soundfont-reclaimer".into()).spawn(move || {
            while let Ok(stack) = reclaim_rx.recv() { drop(stack); }
        }).expect("start SoundFont reclaimer");
        Self {
            synth,
            retiring: Vec::with_capacity(16), reclaim_tx,
            tail_left: vec![0.0; BLOCK_SIZE], tail_right: vec![0.0; BLOCK_SIZE],
            cmd_rx,
            sample_rate: SAMPLE_RATE,
            master_volume: crate::settings::defaults::value("/ui/playback/volume"),
            track_volumes: HashMap::new(),
            muted_tracks: HashSet::new(),
            channel_programs: [0; 16],
            channel_controllers: std::array::from_fn(|_| HashMap::new()),
            enforce_note_durations: false,
            left_buf: vec![0.0f32; BLOCK_SIZE],
            right_buf: vec![0.0f32; BLOCK_SIZE],
        }
    }

    /// Drains pending commands and renders one interleaved stereo block into `out`.
    fn render_block(&mut self, out: &mut Vec<f32>) {
        // SynthSource reuses this Vec. Keeping previous blocks would replay
        // their samples and grow playback latency on every render.
        out.clear();

        // 1. Drain pending commands
        self.process_pending_commands();

        self.left_buf.fill(0.0); self.right_buf.fill(0.0);
        if let Some(s) = &mut self.synth { s.render(&mut self.left_buf, &mut self.right_buf); }
        let mut i = 0;
        while i < self.retiring.len() {
            let tail = &mut self.retiring[i];
            tail.stack.render(&mut self.tail_left, &mut self.tail_right);
            let quiet = self.tail_left.iter().chain(&self.tail_right).all(|v| v.abs() < 0.0000316227766);
            if quiet && !tail.stack.has_pending_notes() { tail.quiet_samples += BLOCK_SIZE as u32; }
            else { tail.quiet_samples = 0; }
            for j in 0..BLOCK_SIZE { self.left_buf[j] += self.tail_left[j]; self.right_buf[j] += self.tail_right[j]; }
            if tail.quiet_samples >= self.sample_rate / 4 {
                let tail = self.retiring.swap_remove(i);
                let _ = self.reclaim_tx.send(tail.stack);
            } else { i += 1; }
        }
        for i in 0..BLOCK_SIZE {
            out.push((self.left_buf[i] * self.master_volume).clamp(-1.0, 1.0));
            out.push((self.right_buf[i] * self.master_volume).clamp(-1.0, 1.0));
        }
    }

    fn clear_retiring(&mut self) {
        for tail in self.retiring.drain(..) { let _ = self.reclaim_tx.send(tail.stack); }
    }

    /// Panic/stop also clears retiring voices and effect tails.
    fn silence_all(&mut self) {
        for map in &mut self.channel_controllers { map.remove(&64); }
        if let Some(mut s) = self.synth.take() {
            s.reset();
            self.restore_channels(&mut s);
            self.synth = Some(s);
        }
        self.clear_retiring();
    }

    fn process_pending_commands(&mut self) {
        while let Ok(cmd) = self.cmd_rx.try_recv() {
            match cmd {
                AudioCommand::NoteOn { channel, note, velocity } => {
                    if let Some(ref mut s) = self.synth {
                        s.note_on(channel as i32, note as i32, velocity as i32);
                    }
                    if velocity == 0 {
                        for tail in &mut self.retiring { tail.stack.note_off(channel as i32, note as i32); }
                    }
                }
                AudioCommand::NoteOff { channel, note } => {
                    if let Some(ref mut s) = self.synth {
                        s.note_off(channel as i32, note as i32);
                    }
                    for tail in &mut self.retiring { tail.stack.note_off(channel as i32, note as i32); }
                }
                AudioCommand::ControlChange { channel, controller, value } => {
                    let ch = channel as usize;
                    if ch < 16 {
                        if controller == 121 {
                            // Preserve bank, volume and pan; clear only controllers reset by CC121.
                            self.channel_controllers[ch].retain(|cc, _| ![1, 33, 11, 43, 64, 98, 99, 100, 101].contains(cc));
                        } else if controller < 120 {
                            self.channel_controllers[ch].insert(controller, value);
                        }
                    }
                    self.send_control_change(channel, controller, value);
                }
                AudioCommand::SetEnforceNoteDurations(enabled) => {
                    if self.enforce_note_durations != enabled {
                        self.enforce_note_durations = enabled;
                        // Clear an already-held pedal immediately, or restore the
                        // MIDI pedal state when returning to source durations.
                        for channel in 0..16 {
                            for controller in [64, 66] {
                                let value = self.channel_controllers[channel].get(&controller).copied().unwrap_or(0);
                                self.send_control_change(channel as u8, controller, value);
                            }
                        }
                    }
                }
                AudioCommand::ProgramChange { channel, program } => {
                    let ch = channel as usize;
                    if ch < 16 {
                        self.channel_programs[ch] = program;
                    }
                    if let Some(ref mut s) = self.synth {
                        s.process_midi_message(channel as i32, 0xC0, program as i32, 0);
                    }
                    for tail in &mut self.retiring { tail.stack.process_midi_message(channel as i32, 0xC0, program as i32, 0); }
                }
                AudioCommand::SetMasterVolume(vol) => {
                    self.master_volume = vol.clamp(0.0, 2.0);
                }
                AudioCommand::SetTrackMuted { track_index, muted } => {
                    if muted {
                        self.muted_tracks.insert(track_index);
                    } else {
                        self.muted_tracks.remove(&track_index);
                    }
                }
                AudioCommand::SetTrackVolume { track_index, volume } => {
                    self.track_volumes.insert(track_index, volume.clamp(0.0, 1.0));
                }
                AudioCommand::InstallSoundFont(prepared) => {
                    if !prepared.is_current() {
                        let _ = prepared.reply.send(Err("SoundFont change superseded".into()));
                        let _ = self.reclaim_tx.send(prepared.stack);
                    } else {
                        self.replace_fonts(prepared.stack);
                        let _ = prepared.reply.send(Ok(()));
                    }
                }
                AudioCommand::AllNotesOff => self.silence_all(),
                AudioCommand::ClearSoundFonts => {
                    if let Some(stack) = self.synth.take() { let _ = self.reclaim_tx.send(stack); }
                    self.clear_retiring();
                }
                AudioCommand::Reset => {
                    self.channel_programs = [0; 16];
                    for map in &mut self.channel_controllers { map.clear(); }
                    if let Some(ref mut s) = self.synth { s.reset(); }
                    self.clear_retiring();
                }
            }
        }
    }

    fn replace_fonts(&mut self, mut new_synth: Box<FontStack>) {
        self.restore_channels(&mut new_synth);
        if let Some(old) = self.synth.replace(new_synth) {
            self.retiring.push(RetiringStack { stack: old, quiet_samples: 0 });
        }
    }

    fn effective_controller_value(&self, controller: u8, value: u8) -> u8 {
        // Sustain/sostenuto must not extend explicitly scheduled preview releases.
        if self.enforce_note_durations && matches!(controller, 64 | 66) { 0 } else { value }
    }

    fn send_control_change(&mut self, channel: u8, controller: u8, value: u8) {
        let value = self.effective_controller_value(controller, value);
        if let Some(ref mut s) = self.synth {
            s.process_midi_message(channel as i32, 0xB0, controller as i32, value as i32);
        }
        for tail in &mut self.retiring {
            tail.stack.process_midi_message(channel as i32, 0xB0, controller as i32, value as i32);
        }
    }

    fn restore_channels(&self, new_synth: &mut FontStack) {
        // Re-apply cached channel state (Bank Selects, Program Changes, Continuous Controllers)
        // so changing SoundFonts during playback doesn't reset all channels to Grand Piano (Program 0)
        // or default volume/expression/pan.
        for ch in 0..16 {
            // 1. Bank Select MSB (CC 0) and LSB (CC 32)
            if let Some(&bank_msb) = self.channel_controllers[ch].get(&0) {
                new_synth.process_midi_message(ch as i32, 0xB0, 0, bank_msb as i32);
            }
            if let Some(&bank_lsb) = self.channel_controllers[ch].get(&32) {
                new_synth.process_midi_message(ch as i32, 0xB0, 32, bank_lsb as i32);
            }

            // 2. Program Change
            let prog = self.channel_programs[ch];
            new_synth.process_midi_message(ch as i32, 0xC0, prog as i32, 0);

            // 3. Continuous Controllers (volume CC 7, pan CC 10, expression CC 11, sustain CC 64, etc.)
            for (&cc, &val) in &self.channel_controllers[ch] {
                if cc != 0 && cc != 32 {
                    new_synth.process_midi_message(ch as i32, 0xB0, cc as i32,
                        self.effective_controller_value(cc, val) as i32);
                }
            }
        }
    }

}

/// Rodio source that pulls audio blocks from the shared [`SynthEngine`].
struct SynthSource {
    engine: Arc<Mutex<SynthEngine>>,
    sample_rate: u32,
    block_buffer: Vec<f32>,
    block_pos: usize,
}

impl SynthSource {
    fn new(engine: Arc<Mutex<SynthEngine>>) -> Self {
        let sample_rate = lock_engine(&engine).sample_rate;
        Self {
            engine,
            sample_rate,
            block_buffer: Vec::with_capacity(BLOCK_SIZE * 2),
            block_pos: 0,
        }
    }

    fn render_next_block(&mut self) {
        lock_engine(&self.engine).render_block(&mut self.block_buffer);
        self.block_pos = 0;
    }
}

fn lock_engine(engine: &Mutex<SynthEngine>) -> std::sync::MutexGuard<'_, SynthEngine> {
    // A panic while rendering must not permanently silence audio.
    engine.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

impl Source for SynthSource {
    fn current_span_len(&self) -> Option<usize> {
        None
    }

    fn channels(&self) -> NonZeroU16 {
        NonZeroU16::new(2).expect("stereo channel count is non-zero")
    }

    fn sample_rate(&self) -> NonZeroU32 {
        NonZeroU32::new(self.sample_rate).expect("synth sample rate is non-zero")
    }

    fn total_duration(&self) -> Option<Duration> {
        None
    }
}

impl Iterator for SynthSource {
    type Item = f32;

    #[inline]
    fn next(&mut self) -> Option<Self::Item> {
        if self.block_pos >= self.block_buffer.len() {
            self.render_next_block();
        }

        let sample = self.block_buffer[self.block_pos];
        self.block_pos += 1;
        Some(sample)
    }
}

/// How often to check whether the system default output device changed. This is a fallback
/// for backends that don't report the change through the stream error callback.
const DEVICE_POLL_INTERVAL: Duration = Duration::from_secs(2);
/// How often to retry opening an output while no device is available.
const DEVICE_RETRY_INTERVAL: Duration = Duration::from_secs(1);
/// The OS fires several notifications in a burst while switching devices; wait for it to settle.
const DEVICE_SWITCH_DEBOUNCE: Duration = Duration::from_millis(250);

enum OutputControl {
    /// The current stream is no longer usable (device switched / removed); reopen on the default device.
    Rebuild,
    Shutdown,
}

/// An open OS output stream bound to one physical device.
struct OutputSink {
    _stream: MixerDeviceSink,
    _player: Player,
    /// Id of the system default output device when this sink was opened.
    device_id: Option<cpal::DeviceId>,
}

fn current_default_device_id() -> Option<cpal::DeviceId> {
    cpal::default_host()
        .default_output_device()
        .and_then(|device| device.id().ok())
}

impl OutputSink {
    fn open(
        engine: &Arc<Mutex<SynthEngine>>,
        control_tx: &Sender<OutputControl>,
    ) -> Result<Self, AudioError> {
        let device = cpal::default_host()
            .default_output_device()
            .ok_or_else(|| AudioError::StreamInit("No default output device available".into()))?;
        let device_id = device.id().ok();

        // cpal reports a default-device switch (WASAPI) or a disconnect as a stream error;
        // the stream itself stays bound to the old device, so it has to be rebuilt.
        let error_tx = control_tx.clone();
        let error_callback = move |err: cpal::Error| match err.kind() {
            cpal::ErrorKind::DeviceNotAvailable | cpal::ErrorKind::StreamInvalidated => {
                log::info!("Audio output stream invalidated ({err}); reopening on default device");
                let _ = error_tx.send(OutputControl::Rebuild);
            }
            cpal::ErrorKind::Xrun => {}
            _ => log::warn!("Audio output stream error: {err}"),
        };

        let mut stream = DeviceSinkBuilder::from_device(device)
            .and_then(|builder| builder.with_error_callback(error_callback).open_sink_or_fallback())
            .or_else(|err| {
                log::warn!("Failed to open default audio output ({err}); trying other devices");
                DeviceSinkBuilder::open_default_sink()
            })
            .map_err(|e| AudioError::StreamInit(e.to_string()))?;
        stream.log_on_drop(false);

        let player = Player::connect_new(stream.mixer());
        player.append(SynthSource::new(engine.clone()));

        Ok(Self {
            _stream: stream,
            _player: player,
            device_id,
        })
    }

    fn default_device_changed(&self) -> bool {
        current_default_device_id() != self.device_id
    }
}

/// Keeps an output stream open on the current system default device, rebuilding it whenever
/// the device is switched, removed, or becomes available again.
fn run_output_supervisor(
    mut current: Option<OutputSink>,
    engine: Arc<Mutex<SynthEngine>>,
    control_tx: Sender<OutputControl>,
    control_rx: Receiver<OutputControl>,
) {
    loop {
        if current.is_none() { lock_engine(&engine).process_pending_commands(); }
        let timeout = if current.is_some() { DEVICE_POLL_INTERVAL } else { DEVICE_RETRY_INTERVAL };
        let rebuild = match control_rx.recv_timeout(timeout) {
            Ok(OutputControl::Shutdown) | Err(RecvTimeoutError::Disconnected) => return,
            Ok(OutputControl::Rebuild) => true,
            Err(RecvTimeoutError::Timeout) => match &current {
                Some(sink) => sink.default_device_changed(),
                None => true,
            },
        };
        if !rebuild {
            continue;
        }

        std::thread::sleep(DEVICE_SWITCH_DEBOUNCE);
        while let Ok(msg) = control_rx.try_recv() {
            if matches!(msg, OutputControl::Shutdown) {
                return;
            }
        }

        let recovering_from_outage = current.is_none();
        // Release the old device before opening the new one.
        drop(current.take());

        match OutputSink::open(&engine, &control_tx) {
            Ok(sink) => {
                if recovering_from_outage {
                    // Notes queued while no device was available would otherwise all fire at once.
                    let mut engine = lock_engine(&engine);
                    engine.process_pending_commands();
                    engine.silence_all();
                }
                log::info!("Audio output switched to device {:?}", sink.device_id);
                current = Some(sink);
            }
            Err(err) => log::warn!("Audio output unavailable, will retry: {err}"),
        }
    }
}

/// Manages the OS audio stream lifecycle. Keep this held on the host thread or state.
///
/// The output stream follows the system default device: when the user switches audio devices
/// (or the current one is unplugged), the stream is transparently reopened on the new default
/// device while the synthesizer state is preserved.
pub struct AudioDevice {
    synth: AudioSynth,
    control_tx: Sender<OutputControl>,
    supervisor: Option<std::thread::JoinHandle<()>>,
}

impl AudioDevice {
    pub fn new(initial_soundfont: Option<Arc<SoundFont>>) -> Result<Self, AudioError> {
        let (cmd_tx, cmd_rx) = unbounded::<AudioCommand>();
        let engine = Arc::new(Mutex::new(SynthEngine::new(initial_soundfont, cmd_rx)));
        let (control_tx, control_rx) = unbounded::<OutputControl>();
        let (init_tx, init_rx) = bounded::<Result<(), AudioError>>(1);

        // The stream is created and dropped on this thread only, so it never has to be Send.
        let thread_control_tx = control_tx.clone();
        let supervisor = std::thread::Builder::new()
            .name("audio-output".into())
            .spawn(move || match OutputSink::open(&engine, &thread_control_tx) {
                Ok(sink) => {
                    let _ = init_tx.send(Ok(()));
                    run_output_supervisor(Some(sink), engine, thread_control_tx, control_rx);
                }
                Err(err) => {
                    let _ = init_tx.send(Err(err));
                }
            })
            .map_err(|e| AudioError::StreamInit(e.to_string()))?;

        match init_rx.recv() {
            Ok(Ok(())) => Ok(Self {
                synth: AudioSynth { loader: Arc::new(FontLoader::new(cmd_tx.clone(), SAMPLE_RATE)), cmd_tx },
                control_tx,
                supervisor: Some(supervisor),
            }),
            Ok(Err(err)) => {
                let _ = supervisor.join();
                Err(err)
            }
            Err(_) => {
                let _ = supervisor.join();
                Err(AudioError::StreamInit("Audio output thread exited unexpectedly".into()))
            }
        }
    }

    pub fn synth(&self) -> AudioSynth {
        self.synth.clone()
    }
}

impl Drop for AudioDevice {
    fn drop(&mut self) {
        let _ = self.control_tx.send(OutputControl::Shutdown);
        if let Some(handle) = self.supervisor.take() {
            let _ = handle.join();
        }
    }
}

/// Send + Sync handle to send audio events from any playback thread without locking.
#[derive(Clone)]
pub struct AudioSynth {
    loader: Arc<FontLoader>,
    cmd_tx: Sender<AudioCommand>,
}

impl AudioSynth {
    pub fn note_on(&self, channel: u8, note: u8, velocity: u8) {
        let _ = self.cmd_tx.send(AudioCommand::NoteOn { channel, note, velocity });
    }

    pub fn note_off(&self, channel: u8, note: u8) {
        let _ = self.cmd_tx.send(AudioCommand::NoteOff { channel, note });
    }

    pub fn control_change(&self, channel: u8, controller: u8, value: u8) {
        let _ = self.cmd_tx.send(AudioCommand::ControlChange { channel, controller, value });
    }

    pub fn program_change(&self, channel: u8, program: u8) {
        let _ = self.cmd_tx.send(AudioCommand::ProgramChange { channel, program });
    }

    pub fn set_enforce_note_durations(&self, enabled: bool) {
        let _ = self.cmd_tx.send(AudioCommand::SetEnforceNoteDurations(enabled));
    }

    pub fn set_master_volume(&self, volume: f32) {
        let _ = self.cmd_tx.send(AudioCommand::SetMasterVolume(volume));
    }

    pub fn set_track_muted(&self, track_index: usize, muted: bool) {
        let _ = self.cmd_tx.send(AudioCommand::SetTrackMuted { track_index, muted });
    }

    pub fn set_track_volume(&self, track_index: usize, volume: f32) {
        let _ = self.cmd_tx.send(AudioCommand::SetTrackVolume { track_index, volume });
    }

    pub fn load_soundfont(&self, soundfont: Arc<SoundFont>) -> Result<(), String> {
        self.load_soundfont_stack(vec![soundfont])
    }

    pub fn load_soundfont_stack(&self, fonts: Vec<Arc<SoundFont>>) -> Result<(), String> {
        self.loader.load(FontInput::Fonts(fonts))
    }

    /// Disk IO and synth preparation run on the loader; success means handoff completed.
    pub fn load_soundfont_paths(&self, paths: Vec<std::path::PathBuf>) -> Result<(), String> {
        self.loader.load(FontInput::Paths(paths))
    }

    pub fn all_notes_off(&self) {
        let _ = self.cmd_tx.send(AudioCommand::AllNotesOff);
    }

    pub fn clear_soundfonts(&self) {
        self.loader.cancel_pending();
        let _ = self.cmd_tx.send(AudioCommand::ClearSoundFonts);
    }

    pub fn reset(&self) {
        let _ = self.cmd_tx.send(AudioCommand::Reset);
    }
}

#[cfg(test)]
#[path = "switch_tests.rs"]
mod switch_tests;
