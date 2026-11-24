use std::collections::{HashMap, HashSet, VecDeque};
use std::sync::{Arc, Mutex};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::time::{Duration, Instant};
use crossbeam_channel::{unbounded, Sender, Receiver};
use spin_sleep::SpinSleeper;

use crate::midi::{
    ParsedMidi, TransposeMode, ControlEventKind,
    process_note_pitch,
    detect::KeyChangeMarker,
};
use crate::input::{NoteKeyMapping, KeySimulator};
use crate::input::mapping::ChordBinding;
use crate::audio::{AudioSynth, SynthMode};
use enigo::Key;
use crate::diagnostics::ServiceStatus;
#[cfg(not(windows))]
use crate::diagnostics::ServiceState;
use super::live_input::{LiveInput, LiveInputSnapshot, LiveSettings, MidiMessage};
use crate::input::simulator::KeyboardOutput;
use super::note_timing::{is_tap, note_durations, pending_note_owners, KeyReleases, KeyPressAction, OverlapBehavior};
use super::note_processing::{NoteProcessingSettings, ChordNote, merge_notes, detect_chords, plan_guitar, plan_pad_accompaniment, GuitarPlanStart};

#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum PlaybackStatus {
    Stopped,
    Playing,
    Paused,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ActiveTrackNote {
    pub pitch: u8,
    pub track_index: usize,
}

/// MIDI pedal activity is tracked independently on each channel and combined for the UI.
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PedalState {
    pub sustain: bool,
    pub sostenuto: bool,
    pub una_corda: bool,
}

#[derive(Default)]
struct PedalTracker {
    channels: [[bool; 3]; 16],
}

impl PedalTracker {
    fn apply(&mut self, channel: u8, kind: &ControlEventKind) {
        let Some(pedals) = self.channels.get_mut(channel as usize) else { return };
        if let ControlEventKind::ControlChange { controller, value } = *kind {
            match controller {
                64 => pedals[0] = value >= 64,
                66 => pedals[1] = value >= 64,
                67 => pedals[2] = value >= 64,
                121 => *pedals = [false; 3], // Reset All Controllers
                _ => {}
            }
        }
    }

    fn state(&self) -> PedalState {
        PedalState {
            sustain: self.channels.iter().any(|p| p[0]),
            sostenuto: self.channels.iter().any(|p| p[1]),
            una_corda: self.channels.iter().any(|p| p[2]),
        }
    }

    fn restore(&mut self, song: &ParsedMidi, control_idx: usize) {
        *self = Self::default();
        for event in &song.control_events[..control_idx] {
            self.apply(event.channel, &event.kind);
        }
    }
}

fn sync_game_pedals(simulator: &mut KeySimulator, mapping: &NoteKeyMapping, state: PedalState, enabled: bool) {
    simulator.sync_pedals(mapping.pedal_bindings(),
        [state.sustain, state.sostenuto, state.una_corda], enabled);
}

struct LiveNote {
    pitch: Option<u8>,
    key: Option<Key>,
    track_index: usize,
    synth_note: Option<(u8, u8)>,
    started: Instant,
    duration_us: Option<u64>,
    deadline: Option<Instant>,
    adapted_timing: bool,
    chord_pad: bool,
}

fn next_note_deadline(keys: &KeyReleases, notes: &HashMap<usize, LiveNote>) -> Option<Instant> {
    keys.next_deadline().into_iter().chain(notes.values().filter_map(|note| note.deadline)).min()
}

fn finish_live_note(
    owner: usize, notes: &mut HashMap<usize, LiveNote>, synth: &Option<AudioSynth>,
    callback: &Arc<dyn Fn(PlayerEvent) + Send + Sync>,
) -> bool {
    let Some(note) = notes.remove(&owner) else { return false };
    if let Some((channel, pitch)) = note.synth_note {
        if !notes.values().any(|note| note.synth_note == Some((channel, pitch))) {
            if let Some(synth) = synth { synth.note_off(channel, pitch); }
        }
    }
    if let Some(pitch) = note.pitch {
        callback(PlayerEvent::NoteHit { note: pitch, is_on: false });
    }
    true
}

fn publish_live_notes(
    notes: &HashMap<usize, LiveNote>, active: &mut HashSet<u8>, tracks: &mut HashSet<ActiveTrackNote>,
    callback: &Arc<dyn Fn(PlayerEvent) + Send + Sync>,
) {
    let next_active: HashSet<_> = notes.values().filter_map(|note| note.pitch).collect();
    let next_tracks: HashSet<_> = notes.values().filter_map(|note| note.pitch.map(|pitch| ActiveTrackNote {
        pitch, track_index: note.track_index,
    })).collect();
    if *active != next_active || *tracks != next_tracks {
        *active = next_active;
        *tracks = next_tracks;
        callback(PlayerEvent::ActiveNotes(active.iter().copied().collect()));
        callback(PlayerEvent::ActiveTrackNotes(tracks.iter().copied().collect()));
    }
}

#[derive(Debug, Default, Clone, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ArrangementState {
    pub mode: Option<&'static str>,
    pub slot: Option<usize>,
    pub strings: u8,
    pub page: Option<usize>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub strum: Option<&'static str>,
    pub omitted_melody: usize,
    pub omitted_accompaniment: usize,
}

#[derive(Debug, Clone, Copy)]
struct WheelAttack {
    owner: usize,
    at: u64,
    slot: usize,
    fret: bool,
    members: usize,
}

#[derive(Debug, Clone)]
pub enum PlayerEvent {
    TimeUpdate { time_us: u64, seek_id: u64 },
    ActiveNotes(Vec<u8>),
    PedalsChanged(PedalState),
    ActiveTrackNotes(Vec<ActiveTrackNote>),
    NoteHit { note: u8, is_on: bool },
    StateChanged(PlaybackStatus),
    Finished,
    ChordWheelChanged(crate::input::chord_wheel::ChordWheelState),
    Arrangement(ArrangementState),
}

enum Command {
    GetChordWheelState(Sender<crate::input::chord_wheel::ChordWheelState>),
    SetChordWheelPage(usize, bool, Sender<Result<(), String>>),
    SyncGuitarMode(bool, Sender<Result<(), String>>),
    SwitchGuitarMode(bool, crate::input::mapping::KeyStroke, Sender<Result<(), String>>),
    GetGuitarMode(Sender<Option<bool>>),
    ObserveKeyboard(ServiceStatus),
    Fence(Sender<()>),
    MidiStart(u64),
    MidiStop,
    MidiMessage(u64, MidiMessage),
    MidiGate(bool),
    PrepareKeyboard(Sender<Result<(), String>>),
    SetKeyboard(Option<Box<dyn KeyboardOutput>>),
    Play(Sender<Result<(), String>>),
    Pause,
    Stop,
    Seek(u64, u64),
    SetSpeed(f64),
    SetKeyOffset(i8),
    SetTransposeMode(TransposeMode),
    SetTrackMuted(usize, bool),
    SetFps(u32),
    SetHoldDelayMs(u64),
    SetOverlapBehavior(OverlapBehavior),
    SetRepressGapMs(u64),
    SetNoteTiming { hold_notes: bool, tap_threshold_ms: u64 },
    SetNoteProcessing(NoteProcessingSettings),
    SetNoteLogging(bool),
    SetGamePreviewFollowNoteTiming(bool),
    SetPedalsEnabled(bool),
    SetSendInput(bool),
    LoadSong(ParsedMidi),
    Panic,
    Shutdown,
    // Phase 4: Audio Synthesizer & Listen Mode commands
    SetListenMode(bool),
    SetSynthMode(SynthMode),
    SetMasterVolume(f32),
    SetVolumeNormalization(bool),
    SetTrackVolume(usize, f32),
    SetAudioSynth(Option<AudioSynth>),
    SetMapping(NoteKeyMapping),
    TapNote { pitch: u8, listen_mode: bool },
    // Phase 10: Key Change Modulation markers
    SetKeyMarkers(Vec<KeyChangeMarker>),
    SetKeyMarkersEnabled(bool),
}

pub struct PlaybackEngine {
    cmd_tx: Sender<Command>,
    status: Arc<Mutex<PlaybackStatus>>,
    current_time_us: Arc<Mutex<u64>>,
    config_available: Arc<AtomicBool>,
    worker_handle: Option<std::thread::JoinHandle<()>>,
    midi_snapshot: Arc<Mutex<LiveInputSnapshot>>,
    keyboard_status: Arc<Mutex<ServiceStatus>>,
    midi_queued: Arc<AtomicUsize>,
    midi_overflow: Arc<AtomicBool>,
}

impl PlaybackEngine {
    pub fn guitar_mode(&self) -> Result<Option<bool>, String> {
        let (tx, rx) = crossbeam_channel::bounded(1);
        self.cmd_tx.send(Command::GetGuitarMode(tx)).map_err(|e| e.to_string())?;
        rx.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())
    }
    pub fn sync_guitar_mode(&self, fret: bool) -> Result<(), String> {
        let (tx, rx) = crossbeam_channel::bounded(1);
        self.cmd_tx.send(Command::SyncGuitarMode(fret, tx)).map_err(|e| e.to_string())?;
        rx.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())?
    }
    pub fn switch_guitar_mode(&self, fret: bool, key: crate::input::mapping::KeyStroke) -> Result<(), String> {
        let (tx, rx) = crossbeam_channel::bounded(1);
        self.cmd_tx.send(Command::SwitchGuitarMode(fret, key, tx)).map_err(|e| e.to_string())?;
        rx.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())?
    }
    pub fn chord_wheel_state(&self) -> Result<crate::input::chord_wheel::ChordWheelState, String> {
        let (tx, rx) = crossbeam_channel::bounded(1);
        self.cmd_tx.send(Command::GetChordWheelState(tx)).map_err(|e| e.to_string())?;
        rx.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())
    }
    pub fn set_chord_wheel_page(&self, page: usize, send: bool) -> Result<(), String> {
        let (tx, rx) = crossbeam_channel::bounded(1);
        self.cmd_tx.send(Command::SetChordWheelPage(page, send, tx)).map_err(|e| e.to_string())?;
        rx.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())?
    }
    pub fn new<F>(
        initial_song: Option<ParsedMidi>,
        mapping: NoteKeyMapping,
        send_input_enabled: bool,
        audio_synth: Option<AudioSynth>,
        event_callback: F,
    ) -> Self
    where
        F: Fn(PlayerEvent) + Send + Sync + 'static,
    {
        let (cmd_tx, cmd_rx) = unbounded::<Command>();
        let status = Arc::new(Mutex::new(PlaybackStatus::Stopped));
        let current_time_us = Arc::new(Mutex::new(0u64));
        let config_available = Arc::new(AtomicBool::new(true));
        let config_available_clone = Arc::clone(&config_available);

        let status_clone = Arc::clone(&status);
        let time_clone = Arc::clone(&current_time_us);
        let cb = Arc::new(event_callback);
        let midi_snapshot = Arc::new(Mutex::new(LiveInputSnapshot::default()));
        let worker_snapshot = Arc::clone(&midi_snapshot);
        let keyboard_status = Arc::new(Mutex::new(ServiceStatus::default()));
        let worker_keyboard_status = Arc::clone(&keyboard_status);
        let midi_queued = Arc::new(AtomicUsize::new(0));
        let midi_overflow = Arc::new(AtomicBool::new(false));
        let worker_queued = Arc::clone(&midi_queued);
        let worker_overflow = Arc::clone(&midi_overflow);

        let worker_handle = std::thread::spawn(move || {
            run_playback_loop(
                initial_song,
                mapping,
                send_input_enabled,
                audio_synth,
                cmd_rx,
                status_clone,
                time_clone,
                config_available_clone,
                cb,
                worker_snapshot,
                worker_keyboard_status,
                worker_queued,
                worker_overflow,
            );
        });

        Self {
            cmd_tx,
            status,
            current_time_us,
            config_available,
            worker_handle: Some(worker_handle),
            midi_snapshot,
            keyboard_status,
            midi_queued,
            midi_overflow,
        }
    }

    pub fn keyboard_status(&self) -> ServiceStatus { self.keyboard_status.lock().unwrap_or_else(|e| e.into_inner()).clone() }
    pub fn observe_keyboard(&self, status: ServiceStatus) { let _ = self.cmd_tx.send(Command::ObserveKeyboard(status)); }
    pub fn midi_start(&self, generation: u64) { let _ = self.cmd_tx.send(Command::MidiStart(generation)); }
    /// Wait for previously submitted worker commands without involving the UI thread.
    pub fn flush(&self) -> Result<(), String> {
        let (tx, rx) = crossbeam_channel::bounded(1);
        self.cmd_tx.send(Command::Fence(tx)).map_err(|e| e.to_string())?;
        rx.recv_timeout(Duration::from_secs(5)).map_err(|e| e.to_string())
    }
    pub fn midi_stop(&self) { let _ = self.cmd_tx.send(Command::MidiStop); }
    pub fn midi_message(&self, generation: u64, message: MidiMessage) {
        let mut queued = self.midi_queued.load(Ordering::Acquire);
        loop {
            if queued >= 4096 { self.midi_overflow.store(true, Ordering::Release); return; }
            match self.midi_queued.compare_exchange_weak(queued, queued + 1, Ordering::AcqRel, Ordering::Acquire) {
                Ok(_) => break,
                Err(current) => queued = current,
            }
        }
        if self.cmd_tx.send(Command::MidiMessage(generation, message)).is_err() {
            self.midi_queued.fetch_sub(1, Ordering::AcqRel);
        }
    }
    pub fn midi_gate(&self, allowed: bool) { let _ = self.cmd_tx.send(Command::MidiGate(allowed)); }
    pub fn midi_snapshot(&self) -> LiveInputSnapshot { self.midi_snapshot.lock().unwrap_or_else(|e| e.into_inner()).clone() }
    pub fn prepare_keyboard(&self) -> Result<(), String> {
        let (tx, rx) = crossbeam_channel::bounded(1);
        self.cmd_tx.send(Command::PrepareKeyboard(tx)).map_err(|e| e.to_string())?;
        rx.recv_timeout(Duration::from_secs(5)).map_err(|e| e.to_string())?
    }
    pub fn set_keyboard_output(&self, output: Option<Box<dyn KeyboardOutput>>) { let _ = self.cmd_tx.send(Command::SetKeyboard(output)); }

    pub fn play(&self) {
        self.request_play();
    }

    /// The worker acknowledges the request after checking the current song and mapping.
    pub fn request_play(&self) -> Receiver<Result<(), String>> {
        let (reply, result) = crossbeam_channel::bounded(1);
        let _ = self.cmd_tx.send(Command::Play(reply));
        result
    }

    pub fn pause(&self) {
        let _ = self.cmd_tx.send(Command::Pause);
    }

    pub fn stop(&self) {
        let _ = self.cmd_tx.send(Command::Stop);
    }

    /// Block every playback source (including native hotkeys) while its config is unavailable.
    pub fn set_config_available(&self, available: bool) {
        self.config_available.store(available, Ordering::Release);
        if !available { self.stop(); }
    }

    pub fn seek(&self, time_us: u64, seek_id: u64) {
        let _ = self.cmd_tx.send(Command::Seek(time_us, seek_id));
    }

    pub fn set_speed(&self, speed: f64) {
        let _ = self.cmd_tx.send(Command::SetSpeed(speed));
    }

    pub fn set_key_offset(&self, offset: i8) {
        let _ = self.cmd_tx.send(Command::SetKeyOffset(offset));
    }

    pub fn set_transpose_mode(&self, mode: TransposeMode) {
        let _ = self.cmd_tx.send(Command::SetTransposeMode(mode));
    }

    pub fn set_track_muted(&self, track_index: usize, muted: bool) {
        let _ = self.cmd_tx.send(Command::SetTrackMuted(track_index, muted));
    }

    pub fn set_fps(&self, fps: u32) {
        let _ = self.cmd_tx.send(Command::SetFps(fps));
    }

    pub fn set_hold_delay_ms(&self, ms: u64) {
        let _ = self.cmd_tx.send(Command::SetHoldDelayMs(ms));
    }

    pub fn set_overlap_behavior(&self, behavior: OverlapBehavior) {
        let _ = self.cmd_tx.send(Command::SetOverlapBehavior(behavior));
    }

    pub fn set_repress_gap_ms(&self, ms: u64) {
        let _ = self.cmd_tx.send(Command::SetRepressGapMs(ms));
    }

    pub fn set_note_timing(&self, hold_notes: bool, tap_threshold_ms: u64) {
        let _ = self.cmd_tx.send(Command::SetNoteTiming { hold_notes, tap_threshold_ms });
    }

    pub fn set_note_processing(&self, settings: NoteProcessingSettings) {
        let _ = self.cmd_tx.send(Command::SetNoteProcessing(settings));
    }

    pub fn set_note_logging(&self, enabled: bool) {
        let _ = self.cmd_tx.send(Command::SetNoteLogging(enabled));
    }

    pub fn set_game_preview_follow_note_timing(&self, enabled: bool) {
        let _ = self.cmd_tx.send(Command::SetGamePreviewFollowNoteTiming(enabled));
    }

    pub fn set_pedals_enabled(&self, enabled: bool) {
        let _ = self.cmd_tx.send(Command::SetPedalsEnabled(enabled));
    }

    pub fn set_send_input(&self, enabled: bool) {
        let _ = self.cmd_tx.send(Command::SetSendInput(enabled));
    }

    pub fn load_song(&self, song: ParsedMidi) {
        let _ = self.cmd_tx.send(Command::LoadSong(song));
    }

    pub fn panic(&self) {
        let _ = self.cmd_tx.send(Command::Panic);
    }

    pub fn set_listen_mode(&self, enabled: bool) {
        let _ = self.cmd_tx.send(Command::SetListenMode(enabled));
    }

    pub fn set_synth_mode(&self, mode: SynthMode) {
        let _ = self.cmd_tx.send(Command::SetSynthMode(mode));
    }

    pub fn set_master_volume(&self, volume: f32) {
        let _ = self.cmd_tx.send(Command::SetMasterVolume(volume));
    }

    pub fn set_volume_normalization(&self, enabled: bool) {
        let _ = self.cmd_tx.send(Command::SetVolumeNormalization(enabled));
    }

    pub fn set_track_volume(&self, track_index: usize, volume: f32) {
        let _ = self.cmd_tx.send(Command::SetTrackVolume(track_index, volume));
    }

    pub fn set_audio_synth(&self, synth: Option<AudioSynth>) {
        let _ = self.cmd_tx.send(Command::SetAudioSynth(synth));
    }

    pub fn set_mapping(&self, mapping: NoteKeyMapping) {
        let _ = self.cmd_tx.send(Command::SetMapping(mapping));
    }

    pub fn tap_note(&self, pitch: u8, listen_mode: bool) {
        let _ = self.cmd_tx.send(Command::TapNote { pitch, listen_mode });
    }

    pub fn set_key_markers(&self, markers: Vec<KeyChangeMarker>) {
        let _ = self.cmd_tx.send(Command::SetKeyMarkers(markers));
    }

    pub fn set_key_markers_enabled(&self, enabled: bool) {
        let _ = self.cmd_tx.send(Command::SetKeyMarkersEnabled(enabled));
    }

    pub fn get_status(&self) -> PlaybackStatus {
        *self.status.lock().unwrap()
    }

    pub fn get_current_time_us(&self) -> u64 {
        *self.current_time_us.lock().unwrap()
    }
}

impl Drop for PlaybackEngine {
    fn drop(&mut self) {
        let _ = self.cmd_tx.send(Command::Shutdown);
        if let Some(handle) = self.worker_handle.take() {
            let _ = handle.join();
        }
    }
}

fn get_active_marker(markers: &[KeyChangeMarker], target_ms: u64) -> Option<&KeyChangeMarker> {
    if markers.is_empty() {
        return None;
    }
    let mut low = 0;
    let mut high = markers.len() as isize - 1;
    let mut best_idx = -1isize;
    while low <= high {
        let mid = low + ((high - low) / 2);
        if markers[mid as usize].time_ms <= target_ms {
            best_idx = mid;
            low = mid + 1;
        } else {
            high = mid - 1;
        }
    }
    if best_idx >= 0 {
        Some(&markers[best_idx as usize])
    } else {
        None
    }
}

fn playback_sleeper() -> SpinSleeper {
    #[cfg(target_os = "macos")]
    {
        // Avoid YieldThread on macOS (yield_now invokes XNU swtch_pri priority
        // depression) and keep the spin tail short so low-core VMs are not starved.
        SpinSleeper::new(250_000).with_spin_strategy(spin_sleep::SpinStrategy::SpinLoopHint)
    }
    #[cfg(not(target_os = "macos"))]
    {
        SpinSleeper::default()
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum WaitPlan {
    Channel(u64),
    Sleep(u64),
    Precise(u64),
}

#[allow(dead_code)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum WaitProfile {
    Windows,
    MacOs,
    Other,
}

impl WaitProfile {
    const fn current() -> Self {
        #[cfg(windows)]
        {
            Self::Windows
        }
        #[cfg(target_os = "macos")]
        {
            Self::MacOs
        }
        #[cfg(not(any(windows, target_os = "macos")))]
        {
            Self::Other
        }
    }
}

fn wait_step(
    cmd_rx: &Receiver<Command>,
    sleeper: SpinSleeper,
    plan: WaitPlan,
) -> Option<Command> {
    match plan {
        WaitPlan::Channel(0) | WaitPlan::Sleep(0) | WaitPlan::Precise(0) => None,
        WaitPlan::Channel(us) => cmd_rx.recv_timeout(Duration::from_micros(us)).ok(),
        WaitPlan::Sleep(us) => {
            std::thread::sleep(Duration::from_micros(us));
            None
        }
        WaitPlan::Precise(us) => {
            sleeper.sleep(Duration::from_micros(us));
            None
        }
    }
}

fn schedule_step(next_boundary_us: u64) -> WaitPlan {
    schedule_step_for(next_boundary_us, WaitProfile::current())
}

fn schedule_step_for(next_boundary_us: u64, profile: WaitProfile) -> WaitPlan {
    match profile {
        WaitProfile::Windows => {
            let chunk_us = next_boundary_us.min(10_000);
            if next_boundary_us <= 25_000 {
                WaitPlan::Precise(chunk_us)
            } else {
                WaitPlan::Channel(chunk_us)
            }
        }
        WaitProfile::MacOs => {
            // macOS pthread_cond_timedwait (recv_timeout) can coalesce 10ms waits by
            // 50-150ms on virtualized runners. Use 1ms nanosleep outside the final 15ms
            // window (zero spin, <= 1ms command polling) and 1ms SpinSleeper slices inside it.
            if next_boundary_us <= 15_000 {
                WaitPlan::Precise(next_boundary_us.min(1_000))
            } else {
                WaitPlan::Sleep(1_000)
            }
        }
        WaitProfile::Other => {
            if next_boundary_us <= 10_000 {
                WaitPlan::Precise(next_boundary_us)
            } else {
                WaitPlan::Channel((next_boundary_us - 10_000).clamp(1_000, 10_000))
            }
        }
    }
}

fn wheel_step(key_sim: &KeySimulator, now: Instant) -> WaitPlan {
    let remaining_us = key_sim
        .wheel_next_deadline(now)
        .map_or(1_000, |deadline| deadline.saturating_duration_since(now).as_micros() as u64);
    schedule_step(remaining_us)
}

fn run_playback_loop(
    initial_song: Option<ParsedMidi>,
    mapping: NoteKeyMapping,
    mut send_input: bool,
    mut audio_synth: Option<AudioSynth>,
    cmd_rx: Receiver<Command>,
    status_arc: Arc<Mutex<PlaybackStatus>>,
    time_arc: Arc<Mutex<u64>>,
    config_available: Arc<AtomicBool>,
    callback: Arc<dyn Fn(PlayerEvent) + Send + Sync>,
    midi_snapshot: Arc<Mutex<LiveInputSnapshot>>,
    keyboard_status: Arc<Mutex<ServiceStatus>>,
    midi_queued: Arc<AtomicUsize>,
    midi_overflow: Arc<AtomicBool>,
) {
    let mut key_sim = KeySimulator::new();
    let mut manual_mode_switch: Option<(bool, Sender<Result<(), String>>)> = None;
    let mut wheel_contexts = [mapping.wheel_context_for_mode(false), mapping.wheel_context_for_mode(true)];
    if !mapping.is_mixed() && mapping.chord_wheel().is_some() {
        key_sim.declare_mode(Some(mapping.chord_pads().is_empty() && mapping.frets().is_some()));
    }
    key_sim.set_wheel_context(mapping.wheel_context());
    let mut log_played_notes = false;
    let mut midi = LiveInput::default();
    // Keyboard failures can outlive an inactive MIDI connection. Keep them
    // separate so recovering an output never discards a MIDI overflow error.
    #[cfg(not(windows))]
    let mut keyboard_error = None;
    let mut repress_gap_ms = crate::settings::defaults::value("/repressGapMs");
    let mut pending_command = None;
    let mut preview_audio = HashMap::<u8, Instant>::new();
    let mut preview_keys = HashMap::<Key, Instant>::new();
    let sleeper = playback_sleeper();

    let mut durations = initial_song.as_ref().map(|song| note_durations(&song.events)).unwrap_or_default();
    // Retain the untouched MIDI so live toggles never accumulate transformations.
    let mut source_song = initial_song.map(Arc::new);
    let mut current_song = source_song.clone();
    let mut note_processing = NoteProcessingSettings::default();
    let mut rebuild_notes = note_processing.merge_notes;
    let mut rebuild_chords = true;
    let mut chord_notes: Vec<Option<ChordNote>> = Vec::new();
    let mut triggered_chords = HashSet::new();
    let mut hold_notes = crate::settings::defaults::value("/defaultHoldNotes");
    let mut tap_threshold_ms = crate::settings::defaults::value("/defaultTapThresholdMs");
    let mut tap_duration_ms = crate::settings::defaults::value("/keyPressDelayMs");
    let mut game_preview_follow_note_timing = crate::settings::defaults::value("/gamePreviewFollowNoteTiming");
    let mut overlap_behavior = OverlapBehavior::default();
    let mut key_releases = KeyReleases::default();
    let mut event_idx = 0usize;
    let mut restore_notes = false;
    let mut control_idx = 0usize;
    let mut pedals = PedalTracker::default();
    let mut pedals_enabled = crate::settings::defaults::value("/ui/pedalsEnabled");
    let mut current_us = 0u64;
    let mut speed = crate::settings::defaults::value::<f64>("/defaultSpeed");
    let mut key_offset = crate::settings::defaults::value::<i8>("/defaultKeyOffset");
    let mut transpose_mode = TransposeMode::default();
    let mut sync_interval_us = 16_666u64; // Default: ~60 FPS
    let mut muted_tracks = HashSet::<usize>::new();
    let mut track_volumes = HashMap::<usize, f32>::new();
    let mut active_notes = HashSet::<u8>::new();
    let mut active_track_notes = HashSet::<ActiveTrackNote>::new();
    let mut live_notes = HashMap::<usize, LiveNote>::new();
    let mut key_markers = Vec::<KeyChangeMarker>::new();
    let mut key_markers_enabled = crate::settings::defaults::value("/defaultEnableKeyChangeMarkers");
    let mut active_held_notes = HashMap::<(usize, u8, u8), VecDeque<(Option<u8>, Option<Key>, usize)>>::new();
    let mut status = PlaybackStatus::Stopped;
    let mut last_instant = Instant::now();
    let mut last_sync_us = 0u64;
    let mut seek_id = 0u64;

    // Phase 4 audio states
    let mut listen_mode = crate::settings::defaults::value("/ui/playback/isListenMode");
    let mut synth_mode = SynthMode::default();
    let mut volume_normalization = crate::settings::defaults::value("/volumeNormalization");

    let mut mapping = mapping;
    let mut all_keys = mapping.all_keys();
    let (mut min_pitch, mut max_pitch) = mapping.pitch_range();
    let mut playable_notes = mapping.playable_notes().to_vec();
    let mut wheel_attacks: Vec<WheelAttack> = Vec::new();
    let mut arrangement = ArrangementState::default();
    let mut arrangement_clear_at = None;
    let mut wheel_starting = true;
    let mut wheel_state = key_sim.wheel_state();

    let update_status = |new_status: PlaybackStatus, status_arc: &Arc<Mutex<PlaybackStatus>>| {
        *status_arc.lock().unwrap() = new_status;
        callback(PlayerEvent::StateChanged(new_status));
    };

    loop {
        #[cfg(not(windows))]
        let mut fence_reply = None;
        #[cfg(not(windows))]
        let mut keyboard_reply = None;
        // 1. Process pending commands
        // A noisy MIDI port must not starve note deadlines or UI snapshots.
        for _ in 0..256 {
            let Some(cmd) = pending_command.take().or_else(|| cmd_rx.try_recv().ok()) else { break };
            match cmd {
                Command::GetChordWheelState(reply) => { let _ = reply.send(key_sim.wheel_state()); }
                Command::GetGuitarMode(reply) => { let _ = reply.send(key_sim.fret_mode()); }
                Command::SwitchGuitarMode(fret, key, reply) => {
                    let result = if status == PlaybackStatus::Playing || key_sim.wheel_active() || manual_mode_switch.is_some() {
                        Err("Pause playback and wait for pending guitar input.".into())
                    } else if !config_available.load(Ordering::Acquire) || mapping.chord_wheel().is_none() {
                        Err("Select a guitar with a valid wheel layout.".into())
                    } else if key_sim.fret_mode().is_none() {
                        Err("Sync the current game mode in Instrument View before changing modes.".into())
                    } else if key_sim.fret_mode() == Some(fret) {
                        Ok(())
                    } else {
                        // Fixed modes install the destination mapping after this command completes.
                        let context = wheel_contexts[fret as usize].as_ref().or(wheel_contexts[!fret as usize].as_ref()).unwrap();
                        key_sim.begin_mode_switch(fret, context, key, mapping.chord_wheel().unwrap(), mapping.game_process_names());
                        manual_mode_switch = Some((fret, reply));
                        continue;
                    };
                    let _ = reply.send(result);
                }
                Command::SyncGuitarMode(fret, reply) => {
                    let result = if status == PlaybackStatus::Playing || key_sim.wheel_active() {
                        Err("Pause playback and wait for pending input before synchronizing the mode.".into())
                    } else if wheel_contexts[fret as usize].is_none() {
                        Err("Select that guitar mode in Instrument View first.".into())
                    } else {
                        key_sim.declare_mode(Some(fret));
                        key_sim.set_wheel_context(wheel_contexts[fret as usize].clone());
                        rebuild_chords = true;
                        Ok(())
                    };
                    let _ = reply.send(result);
                }
                Command::SetChordWheelPage(page, send, reply) => {
                    let result = if !config_available.load(Ordering::Acquire) { Err("The instrument configuration is unavailable.".into()) }
                        else if status == PlaybackStatus::Playing { Err("Pause playback before changing the page manually.".into()) }
                        else if mapping.is_mixed() && key_sim.fret_mode().is_none() { Err("Synchronize the current guitar mode before changing its page.".into()) }
                        else if let Some(config) = mapping.wheel_for_mode(key_sim.fret_mode().unwrap_or(false)) { key_sim.set_wheel_page(config, mapping.game_process_names(), page, send) }
                        else { Err("Select an instrument with a chord wheel first.".into()) };
                    if result.is_ok() && !send { rebuild_chords = true; }
                    let _ = reply.send(result);
                }
                Command::ObserveKeyboard(status) => key_sim.observe(status),
                Command::Fence(reply) => {
                    #[cfg(windows)]
                    { let _ = reply.send(()); }
                    #[cfg(not(windows))]
                    { fence_reply = Some(reply); break; }
                }
                Command::MidiStart(generation) => midi.start(generation, &mut key_sim, &audio_synth),
                Command::MidiStop => midi.stop(&mut key_sim, &audio_synth),
                Command::MidiGate(allowed) => midi.gate(allowed && send_input && config_available.load(Ordering::Acquire), &mut key_sim, &audio_synth),
                Command::MidiMessage(generation, message) => {
                    midi_queued.fetch_sub(1, Ordering::AcqRel);
                    if midi_overflow.swap(false, Ordering::AcqRel) {
                        midi.stop(&mut key_sim, &audio_synth);
                        midi.snapshot.error = Some("MIDI input overflow. Start input again after checking the device.".into());
                    }
                    midi.message(generation, message, Instant::now(), &LiveSettings {
                        mapping: &mapping, listen: listen_mode, synth_mode,
                        hold: hold_notes, tap_ms: tap_duration_ms, preview_timing: game_preview_follow_note_timing,
                        overlap: overlap_behavior, pedals_enabled,
                    }, &mut key_sim, &audio_synth);
                }
                Command::PrepareKeyboard(reply) => {
                    #[cfg(windows)]
                    { let _ = reply.send(key_sim.prepare()); }
                    #[cfg(not(windows))]
                    {
                        let result = key_sim.prepare();
                        if result.is_ok() { keyboard_error = None; }
                        keyboard_reply = Some((reply, result));
                        break;
                    }
                }
                Command::SetKeyboard(output) => {
                    if !listen_mode { midi.clear(&mut key_sim, &audio_synth); }
                    key_sim.set_output(output);
                    #[cfg(not(windows))]
                    if key_sim.is_ready() { keyboard_error = None; }
                }
                Command::Play(reply) => {
                    if status != PlaybackStatus::Playing && key_sim.wheel_active() {
                        let _ = reply.send(Err("Wait for the guitar controls to finish changing.".into()));
                        continue;
                    }
                    if !config_available.load(Ordering::Acquire) {
                        let _ = reply.send(Err("Choose an instrument with a valid layout in Instrument View.".into()));
                        continue;
                    }
                    if current_song.is_none() {
                        let _ = reply.send(Err("Choose a song to play.".into()));
                        continue;
                    }
                    if send_input && !listen_mode && mapping.chord_wheel().is_some()
                        && key_sim.fret_mode().and_then(|fret| mapping.wheel_for_mode(fret)).is_none() {
                        let _ = reply.send(Err("Synchronize the current guitar mode in Instrument View before playback.".into()));
                        continue;
                    }
                    if status != PlaybackStatus::Playing {
                        if current_song.as_ref().is_some_and(|song| event_idx >= song.events.len() && control_idx >= song.control_events.len()) {
                            rebuild_chords = true;
                            triggered_chords.clear();
                            current_us = 0;
                            event_idx = 0;
                            control_idx = 0;
                            last_sync_us = 0;
                            *time_arc.lock().unwrap() = 0;
                            if let Some(ref synth) = audio_synth {
                                synth.all_notes_off();
                                synth.reset();
                            }
                            callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
                        }
                        if let Some(ref song) = current_song {
                            pedals.restore(song, control_idx);
                            callback(PlayerEvent::PedalsChanged(pedals.state()));
                        }
                        status = PlaybackStatus::Playing;
                        wheel_starting = true;
                        restore_notes = true;
                        last_instant = Instant::now();
                        update_status(status, &status_arc);
                    }
                    let _ = reply.send(Ok(()));
                }
                Command::Pause => {
                    if status == PlaybackStatus::Playing {
                        status = PlaybackStatus::Paused;
                        key_sim.release_all(&all_keys);
                        if let Some(ref synth) = audio_synth {
                            synth.all_notes_off();
                        }
                        active_notes.clear();
                        active_track_notes.clear();
                        active_held_notes.clear();
                        live_notes.clear();
                        key_releases.clear();
                        callback(PlayerEvent::ActiveNotes(Vec::new()));
                        callback(PlayerEvent::ActiveTrackNotes(Vec::new()));
                        pedals = PedalTracker::default();
                        callback(PlayerEvent::PedalsChanged(pedals.state()));
                        update_status(status, &status_arc);
                    }
                }
                Command::Stop => {
                    rebuild_chords = true;
                    triggered_chords.clear();
                    status = PlaybackStatus::Stopped;
                    current_us = 0;
                    last_sync_us = 0;
                    event_idx = 0;
                    control_idx = 0;
                    *time_arc.lock().unwrap() = 0;
                    key_sim.release_all(&all_keys);
                    if let Some(ref synth) = audio_synth {
                        synth.all_notes_off();
                        synth.reset();
                    }
                    active_notes.clear();
                    active_track_notes.clear();
                    active_held_notes.clear();
                    live_notes.clear();
                    key_releases.clear();
                    callback(PlayerEvent::ActiveNotes(Vec::new()));
                    callback(PlayerEvent::ActiveTrackNotes(Vec::new()));
                    pedals = PedalTracker::default();
                    callback(PlayerEvent::PedalsChanged(pedals.state()));
                    update_status(status, &status_arc);
                }
                Command::Seek(target_us, request_id) => {
                    wheel_starting = true;
                    if request_id < seek_id {
                        continue;
                    }
                    seek_id = request_id;
                    rebuild_chords = true;
                    triggered_chords.clear();
                    if let Some(ref song) = current_song {
                        current_us = target_us.min(song.duration_us);
                        *time_arc.lock().unwrap() = current_us;
                        // All events at the target must still run, including chords.
                        event_idx = song.events.partition_point(|e| e.time_us < current_us);
                        restore_notes = status == PlaybackStatus::Playing;
                        control_idx = song.control_events.partition_point(|e| e.time_us < current_us);
                        key_sim.release_all(&all_keys);
                        if let Some(ref synth) = audio_synth {
                            synth.all_notes_off();
                            synth.reset();
                            for cev in &song.control_events[..control_idx] {
                                match cev.kind {
                                    ControlEventKind::ControlChange { controller, value } => {
                                        synth.control_change(cev.channel, controller, value);
                                    }
                                    ControlEventKind::ProgramChange { program } => {
                                        synth.program_change(cev.channel, program);
                                    }
                                }
                            }
                        }
                        active_notes.clear();
                        active_track_notes.clear();
                        active_held_notes.clear();
                        live_notes.clear();
                        key_releases.clear();
                        callback(PlayerEvent::ActiveNotes(Vec::new()));
                        callback(PlayerEvent::ActiveTrackNotes(Vec::new()));
                        pedals.restore(song, control_idx);
                        callback(PlayerEvent::PedalsChanged(pedals.state()));
                        last_instant = Instant::now();
                        last_sync_us = current_us;
                        callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
                    } else {
                        callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
                    }
                }
                Command::SetSpeed(new_speed) => {
                    rebuild_chords |= mapping.chord_wheel().is_some() && speed != new_speed.clamp(0.1, 5.0);
                    speed = new_speed.clamp(0.1, 5.0);
                }
                Command::SetKeyOffset(offset) => {
                    key_offset = offset;
                    rebuild_chords = true;
                }
                Command::SetTransposeMode(mode) => {
                    transpose_mode = mode;
                    rebuild_chords = true;
                }
                Command::SetTrackMuted(track_idx, muted) => {
                    rebuild_chords = true;
                    if status == PlaybackStatus::Playing { key_sim.cancel_wheel(); }
                    if muted {
                        muted_tracks.insert(track_idx);
                        let owners: Vec<_> = live_notes.iter()
                            .filter_map(|(&owner, note)| (note.track_index == track_idx).then_some(owner)).collect();
                        for owner in owners {
                            if let Some(key) = live_notes[&owner].key {
                                if key_releases.cancel_owner(key, owner) {
                                    key_sim.send_key(key, true);
                                }
                            }
                            finish_live_note(owner, &mut live_notes, &audio_synth, &callback);
                        }
                        // Keep the source FIFO entries until their NoteOff arrives,
                        // so an old release cannot consume a newly enabled note.
                        publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);
                    } else {
                        muted_tracks.remove(&track_idx);
                    }
                    if let Some(ref synth) = audio_synth {
                        synth.set_track_muted(track_idx, muted);
                    }
                }
                Command::SetNoteLogging(enabled) => { log_played_notes = enabled; }
                Command::SetFps(fps) => {
                    if fps == 0 {
                        sync_interval_us = 4_000; // ~240 FPS / uncapped
                    } else {
                        let clamped = fps.clamp(10, 240);
                        sync_interval_us = 1_000_000 / (clamped as u64);
                    }
                }
                Command::SetHoldDelayMs(ms) => {
                    rebuild_chords |= mapping.chord_wheel().is_some() && tap_duration_ms != ms.min(1000);
                    if tap_duration_ms != ms.min(1000) { midi.clear(&mut key_sim, &audio_synth); }
                    tap_duration_ms = ms.min(1000);
                    key_sim.set_hold_delay_ms(ms);
                }
                Command::SetOverlapBehavior(behavior) => {
                    overlap_behavior = behavior;
                }
                Command::SetRepressGapMs(ms) => {
                    repress_gap_ms = ms;
                    key_releases.set_repress_gap_ms(ms);
                }
                Command::SetNoteTiming { hold_notes: enabled, tap_threshold_ms: threshold } => {
                    if hold_notes != enabled { midi.clear(&mut key_sim, &audio_synth); }
                    hold_notes = enabled;
                    tap_threshold_ms = threshold.min(10_000);
                }
                Command::SetNoteProcessing(settings) => {
                    if note_processing != settings {
                        rebuild_notes |= note_processing.merge_notes != settings.merge_notes
                            || ((note_processing.merge_notes || settings.merge_notes)
                                && note_processing.merge_tolerance_ms != settings.merge_tolerance_ms);
                        rebuild_chords = true;
                        if status == PlaybackStatus::Playing { key_sim.cancel_wheel(); }
                        note_processing = settings;
                    }
                }
                Command::SetGamePreviewFollowNoteTiming(enabled) => {
                    if game_preview_follow_note_timing != enabled { midi.clear(&mut key_sim, &audio_synth); }
                    game_preview_follow_note_timing = enabled;
                    if let Some(ref synth) = audio_synth {
                        synth.set_enforce_note_durations(listen_mode && synth_mode == SynthMode::Adapted && enabled);
                    }
                    if listen_mode && synth_mode == SynthMode::Adapted {
                        for (&owner, note) in &mut live_notes {
                            note.adapted_timing = enabled;
                            if !enabled {
                                // Taps may outlive their source NoteOff. Finish those
                                // now; notes still held return to their MIDI release.
                                let source_ended = current_song.as_ref().is_some_and(|song|
                                    note.duration_us.is_some_and(|duration|
                                        song.events[owner].time_us.saturating_add(duration) <= current_us));
                                note.deadline = source_ended.then(Instant::now);
                            }
                        }
                    }
                }
                Command::SetPedalsEnabled(enabled) => {
                    if pedals_enabled != enabled { midi.clear(&mut key_sim, &audio_synth); }
                    pedals_enabled = enabled;
                }
                Command::SetSendInput(enabled) => {
                    send_input = enabled;
                    if !enabled {
                        midi.gate(false, &mut key_sim, &audio_synth);
                        key_sim.release_all(&all_keys);
                        key_releases.clear();
                    }
                }
                Command::LoadSong(new_song) => {
                    wheel_starting = true;
                    chord_notes.clear();
                    triggered_chords.clear();
                    rebuild_notes = note_processing.merge_notes;
                    rebuild_chords = true;
                    durations = note_durations(&new_song.events);
                    let new_song = Arc::new(new_song);
                    source_song = Some(Arc::clone(&new_song));
                    current_song = Some(new_song);
                    current_us = 0;
                    last_sync_us = 0;
                    event_idx = 0;
                    control_idx = 0;
                    *time_arc.lock().unwrap() = 0;
                    active_notes.clear();
                    active_track_notes.clear();
                    active_held_notes.clear();
                    live_notes.clear();
                    key_releases.clear();
                    key_markers.clear();
                    callback(PlayerEvent::ActiveNotes(Vec::new()));
                    callback(PlayerEvent::ActiveTrackNotes(Vec::new()));
                    pedals = PedalTracker::default();
                    callback(PlayerEvent::PedalsChanged(pedals.state()));
                    key_sim.release_all(&all_keys);
                    if let Some(ref synth) = audio_synth {
                        synth.all_notes_off();
                        synth.reset();
                    }
                    if status == PlaybackStatus::Playing {
                        last_instant = Instant::now();
                    }
                    callback(PlayerEvent::TimeUpdate { time_us: 0, seek_id });
                }
                Command::Panic => {
                    triggered_chords.clear();
                    rebuild_chords = true;
                    midi.stop(&mut key_sim, &audio_synth);
                    key_sim.release_everything();
                    if let Some(synth) = &audio_synth { synth.for_source(2).all_notes_off(); }
                    preview_audio.clear(); preview_keys.clear();
                    status = PlaybackStatus::Stopped;
                    key_sim.release_all(&all_keys);
                    if let Some(ref synth) = audio_synth {
                        synth.all_notes_off();
                        synth.reset();
                    }
                    active_notes.clear();
                    active_track_notes.clear();
                    active_held_notes.clear();
                    live_notes.clear();
                    key_releases.clear();
                    callback(PlayerEvent::ActiveNotes(Vec::new()));
                    callback(PlayerEvent::ActiveTrackNotes(Vec::new()));
                    pedals = PedalTracker::default();
                    callback(PlayerEvent::PedalsChanged(pedals.state()));
                    update_status(status, &status_arc);
                }
                Command::Shutdown => {
                    midi.stop(&mut key_sim, &audio_synth);
                    key_sim.release_everything();
                    if let Some(synth) = &audio_synth { synth.for_source(2).all_notes_off(); }
                    preview_audio.clear(); preview_keys.clear();
                    key_sim.release_all(&all_keys);
                    active_held_notes.clear();
                    live_notes.clear();
                    key_releases.clear();
                    if let Some(ref synth) = audio_synth {
                        synth.all_notes_off();
                    }
                    return;
                }
                Command::SetListenMode(enabled) => {
                    wheel_starting = true;
                    if listen_mode != enabled { midi.clear(&mut key_sim, &audio_synth); }
                    listen_mode = enabled;
                    if let Some(ref synth) = audio_synth {
                        synth.set_enforce_note_durations(listen_mode && synth_mode == SynthMode::Adapted && game_preview_follow_note_timing);
                    }
                    active_held_notes.clear();
                    live_notes.clear();
                    publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);
                    key_releases.clear();
                    if listen_mode {
                        key_sim.release_all(&all_keys);
                        if let (Some(ref song), Some(ref synth)) = (&current_song, &audio_synth) {
                            synth.reset();
                            for cev in &song.control_events[..control_idx] {
                                match cev.kind {
                                    ControlEventKind::ControlChange { controller, value } => {
                                        synth.control_change(cev.channel, controller, value);
                                    }
                                    ControlEventKind::ProgramChange { program } => {
                                        synth.program_change(cev.channel, program);
                                    }
                                }
                            }
                        }
                    } else if let Some(ref synth) = audio_synth {
                        synth.all_notes_off();
                    }
                }
                Command::SetSynthMode(mode) => {
                    if synth_mode != mode { midi.clear(&mut key_sim, &audio_synth); }
                    synth_mode = mode;
                    if let Some(ref synth) = audio_synth {
                        synth.set_enforce_note_durations(listen_mode && synth_mode == SynthMode::Adapted && game_preview_follow_note_timing);
                    }
                }
                Command::SetMasterVolume(vol) => {
                    if let Some(ref synth) = audio_synth {
                        synth.set_master_volume(vol);
                    }
                }
                Command::SetVolumeNormalization(enabled) => {
                    volume_normalization = enabled;
                    if let Some(ref synth) = audio_synth {
                        synth.set_volume_normalization(enabled);
                    }
                }
                Command::SetTrackVolume(track_idx, vol) => {
                    rebuild_chords = true;
                    if status == PlaybackStatus::Playing { key_sim.cancel_wheel(); }
                    track_volumes.insert(track_idx, vol.clamp(0.0, 1.0));
                    if let Some(ref synth) = audio_synth {
                        synth.set_track_volume(track_idx, vol);
                    }
                }
                Command::SetAudioSynth(s) => {
                    audio_synth = s;
                    if let Some(ref synth) = audio_synth {
                        synth.set_volume_normalization(volume_normalization);
                        synth.set_enforce_note_durations(listen_mode && synth_mode == SynthMode::Adapted && game_preview_follow_note_timing);
                    }
                }
                Command::SetMapping(new_mapping) => {
                    wheel_starting = true;
                    rebuild_chords = true;
                    midi.clear(&mut key_sim, &audio_synth);
                    key_sim.release_all(&all_keys);
                    let next_contexts = [new_mapping.wheel_context_for_mode(false), new_mapping.wheel_context_for_mode(true)];
                    if !new_mapping.is_mixed() {
                        key_sim.declare_mode(new_mapping.chord_wheel().map(|_| new_mapping.chord_pads().is_empty() && new_mapping.frets().is_some()));
                    } else if key_sim.fret_mode().is_some_and(|fret| wheel_contexts[fret as usize] != next_contexts[fret as usize]) {
                        key_sim.declare_mode(None);
                    }
                    wheel_contexts = next_contexts;
                    key_sim.set_wheel_context(wheel_contexts[key_sim.fret_mode().unwrap_or(false) as usize].clone());
                    key_releases.clear();
                    active_held_notes.clear();
                    live_notes.clear();
                    publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);
                    if let Some(ref synth) = audio_synth { synth.all_notes_off(); }
                    all_keys = new_mapping.all_keys();
                    let (new_min, new_max) = new_mapping.pitch_range();
                    min_pitch = new_min;
                    max_pitch = new_max;
                    playable_notes = new_mapping.playable_notes().to_vec();
                    mapping = new_mapping;
                }
                Command::TapNote { pitch, listen_mode: tap_listen } => {
                    let now = Instant::now();
                    if tap_listen {
                        if let Some(synth) = &audio_synth {
                            synth.for_source(2).note_on(0, pitch, 100);
                            preview_audio.insert(pitch, now + Duration::from_millis(150));
                        }
                    } else if send_input {
                        if let Some(stroke) = mapping.get_stroke(pitch) {
                            for (key, up) in stroke.press_events() { key_sim.send_owned_key(4, key, up); }
                            preview_keys.insert(stroke.key, now + Duration::from_millis(tap_duration_ms));
                        }
                    }
                }
                Command::SetKeyMarkers(mut markers) => {
                    rebuild_chords = true;
                    markers.sort_by_key(|m| m.time_ms);
                    key_markers = markers;
                }
                Command::SetKeyMarkersEnabled(enabled) => {
                    rebuild_chords = true;
                    key_markers_enabled = enabled;
                }
            }
        }

        if rebuild_notes {
            rebuild_notes = false;
            if let Some(source) = &source_song {
                let song = if note_processing.merge_notes {
                    let mut merged = (**source).clone();
                    merged.events = merge_notes(&source.events, note_processing.merge_tolerance_ms);
                    Arc::new(merged)
                } else { Arc::clone(source) };
                durations = note_durations(&song.events);
                chord_notes.clear();
                triggered_chords.clear();
                event_idx = song.events.partition_point(|event| event.time_us < current_us);
                current_song = Some(song);
                // Only release the backing song; physical MIDI owns separate outputs.
                key_sim.release_all(&all_keys);
                if let Some(synth) = &audio_synth {
                    synth.all_notes_off();
                    // all_notes_off clears this source's pedals. Restore the
                    // MIDI controller/program state before chasing held notes.
                    if listen_mode {
                        for event in &source.control_events[..control_idx] {
                            match event.kind {
                                ControlEventKind::ControlChange { controller, value } =>
                                    synth.control_change(event.channel, controller, value),
                                ControlEventKind::ProgramChange { program } =>
                                    synth.program_change(event.channel, program),
                            }
                        }
                    }
                }
                active_held_notes.clear();
                live_notes.clear();
                key_releases.clear();
                publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);
                restore_notes = status == PlaybackStatus::Playing;
                last_instant = Instant::now();
                rebuild_chords = true;
            }
        }
        if rebuild_chords {
            if status == PlaybackStatus::Playing { key_sim.cancel_wheel(); }
            rebuild_chords = false;
            if chord_notes.is_empty() && event_idx > 0 && mapping.chord_wheel().is_some() {
                if let Some(song) = current_song.as_ref() {
                    let enabled = |_: usize, event: &crate::midi::TimedNoteEvent| {
                        !muted_tracks.contains(&event.track_index)
                            && ((event.velocity as f32)
                                * track_volumes.get(&event.track_index).copied().unwrap_or(1.0))
                            .round()
                                > 0.0
                    };
                    let transform = |pitch, time_us: u64| {
                        let marker = key_markers_enabled
                            .then(|| get_active_marker(&key_markers, (time_us + 10_000) / 1000))
                            .flatten();
                        let offset = marker.map(|m| m.key_offset).unwrap_or(key_offset);
                        process_note_pitch(
                            pitch,
                            offset,
                            marker.and_then(|m| m.transpose).unwrap_or(transpose_mode),
                            min_pitch,
                            max_pitch,
                            &playable_notes,
                        )
                    };
                    let start = GuitarPlanStart {
                        from_index: 0,
                        initial_fret_mode: key_sim.fret_mode().unwrap_or(false),
                        initial_chord_page: key_sim
                            .page_for_context(wheel_contexts[0].as_deref())
                            .unwrap_or(0),
                        initial_fret_page: key_sim
                            .page_for_context(wheel_contexts[1].as_deref())
                            .unwrap_or(0),
                        speed,
                        hold_ms: tap_duration_ms,
                    };
                    chord_notes = plan_guitar(
                        song,
                        &mapping,
                        start,
                        note_processing.chord_detection_milliseconds,
                        enabled,
                        transform,
                        |time_us| {
                            key_markers_enabled
                                .then(|| get_active_marker(&key_markers, (time_us + 10_000) / 1000))
                                .flatten()
                                .map(|m| m.key_offset)
                                .unwrap_or(key_offset)
                        },
                    )
                    .notes;
                }
            }
            // A pad attack replaces the whole chord. Keep its remaining members
            // suppressed if controls change between their staggered onsets, and
            // preserve historical guitar attacks (< event_idx) for backtracking.
            let past_chords: HashSet<usize> = if mapping.chord_wheel().is_some() {
                chord_notes
                    .iter()
                    .take(event_idx)
                    .flatten()
                    .map(|note| note.chord_id)
                    .collect()
            } else {
                HashSet::new()
            };
            let frozen: Vec<_> = chord_notes
                .iter()
                .map(|note| {
                    note.filter(|note| {
                        triggered_chords.contains(&note.chord_id)
                            || past_chords.contains(&note.chord_id)
                    })
                })
                .collect();
            let next_chord_id = frozen.iter().flatten().map(|note| note.chord_id + 1).max().unwrap_or(0);
            let mut omissions = (0, 0);
            let mut planned = current_song.as_ref().map(|song| {
                let enabled = |index: usize, event: &crate::midi::TimedNoteEvent| frozen.get(index).is_none_or(Option::is_none)
                        && !muted_tracks.contains(&event.track_index)
                        && ((event.velocity as f32) * track_volumes.get(&event.track_index).copied().unwrap_or(1.0)).round() > 0.0;
                let pad_only = !mapping.chord_pads().is_empty() && mapping.chord_wheel().is_none() && mapping.is_chord_only();
                let transform = |pitch, time_us: u64| {
                    let marker = key_markers_enabled.then(|| get_active_marker(&key_markers, (time_us + 10_000) / 1000)).flatten();
                    let offset = marker.map(|m| m.key_offset).unwrap_or(key_offset);
                    if pad_only {
                        // A pad plays a harmony independently of the melody keyboard's range.
                        return Some(crate::midi::transpose::fold_note_by_octaves(i32::from(pitch) + i32::from(offset), 0, 127) as u8);
                    }
                    process_note_pitch(pitch, offset, marker.and_then(|m| m.transpose).unwrap_or(transpose_mode),
                        min_pitch, max_pitch, &playable_notes)
                };
                if mapping.chord_wheel().is_some() {
                    let start = GuitarPlanStart {
                        from_index: event_idx,
                        initial_fret_mode: key_sim.fret_mode().unwrap_or(false),
                        initial_chord_page: key_sim.page_for_context(wheel_contexts[0].as_deref()).unwrap_or(0),
                        initial_fret_page: key_sim.page_for_context(wheel_contexts[1].as_deref()).unwrap_or(0),
                        speed, hold_ms: tap_duration_ms,
                    };
                    let plan = plan_guitar(
                        song,
                        &mapping,
                        start,
                        note_processing.chord_detection_milliseconds,
                        enabled,
                        transform,
                        |time_us| {
                            key_markers_enabled
                                .then(|| get_active_marker(&key_markers, (time_us + 10_000) / 1000))
                                .flatten()
                                .map(|m| m.key_offset)
                                .unwrap_or(key_offset)
                        },
                    );
                    omissions = (plan.omitted_melody, plan.omitted_accompaniment);
                    plan.notes
                } else if pad_only {
                    plan_pad_accompaniment(song, mapping.chord_pads(), note_processing.chord_detection_milliseconds,
                        |index, event| event.channel != 9 && enabled(index, event), transform)
                } else if !mapping.chord_pads().is_empty() {
                    detect_chords(&song.events, mapping.chord_pads(), note_processing.chord_detection_milliseconds, enabled, transform)
                } else { Vec::new() }
            }).unwrap_or_default();
            for note in planned.iter_mut().flatten() { note.chord_id += next_chord_id; }
            if frozen.iter().any(Option::is_some) {
                planned.resize(frozen.len(), None);
                for (index, note) in frozen.into_iter().enumerate() {
                    if note.is_some() { planned[index] = note; }
                }
            }
            chord_notes = planned;
            let previous = arrangement.clone();
            arrangement = ArrangementState { omitted_melody: omissions.0, omitted_accompaniment: omissions.1, ..Default::default() };
            arrangement_clear_at = None;
            if previous != arrangement { callback(PlayerEvent::Arrangement(arrangement.clone())); }
            wheel_attacks.clear();
            if let Some(song) = current_song.as_ref().filter(|_| mapping.chord_wheel().is_some()) {
                let mut members = HashMap::new();
                for (event, note) in song.events.iter().zip(&chord_notes) {
                    if let Some(note) = note.filter(|_| event.is_note_on && event.velocity > 0) {
                        *members.entry(note.chord_id).or_insert(0usize) += 1;
                    }
                }
                for (owner, note) in chord_notes.iter().enumerate() {
                    let Some(note) = note.filter(|n| n.trigger) else { continue };
                    let (slot, fret) = match note.binding {
                        ChordBinding::Wheel(slot)
                        | ChordBinding::WheelUp(slot)
                        | ChordBinding::Pluck { slot, .. } => (slot, false),
                        ChordBinding::Frets { slot, .. } => (slot, true),
                        ChordBinding::Key(_) => continue,
                    };
                    wheel_attacks.push(WheelAttack { owner, at: song.events[owner].time_us, slot, fret, members: members[&note.chord_id] });
                }
            }
        }

        midi.snapshot.listen_mode = listen_mode;
        #[cfg(windows)]
        { midi.snapshot.audio_available = audio_synth.is_some(); }
        #[cfg(not(windows))]
        { midi.snapshot.audio_available = audio_synth.as_ref()
            .is_some_and(|synth| synth.output_status().state == ServiceState::Ready); }
        if !config_available.load(Ordering::Acquire) { midi.gate(false, &mut key_sim, &audio_synth); }
        midi.tick(Instant::now(), &mut key_sim, &audio_synth, repress_gap_ms);
        if manual_mode_switch.is_some() && !config_available.load(Ordering::Acquire) {
            key_sim.cancel_wheel();
        }
        key_sim.tick_wheel();
        if let Some(error) = key_sim.take_error() {
            if let Some((_, reply)) = manual_mode_switch.take() { let _ = reply.send(Err(error.clone())); }
            if mapping.chord_wheel().is_some() {
                let mut state = key_sim.wheel_state();
                state.error = Some(error.clone());
                callback(PlayerEvent::ChordWheelChanged(state));
            }
            midi.stop(&mut key_sim, &audio_synth);
            if (cfg!(not(windows)) || mapping.chord_wheel().is_some())
                && send_input && !listen_mode && status == PlaybackStatus::Playing {
                // A missing keyboard backend cannot play the remaining game
                // notes. Keep the current position available for permission
                // recovery instead of silently advancing through the song.
                status = PlaybackStatus::Paused;
                key_sim.release_all(&all_keys);
                if let Some(ref synth) = audio_synth { synth.all_notes_off(); }
                active_notes.clear();
                active_track_notes.clear();
                active_held_notes.clear();
                live_notes.clear();
                key_releases.clear();
                restore_notes = false;
                callback(PlayerEvent::ActiveNotes(Vec::new()));
                callback(PlayerEvent::ActiveTrackNotes(Vec::new()));
                pedals = PedalTracker::default();
                callback(PlayerEvent::PedalsChanged(pedals.state()));
                *time_arc.lock().unwrap() = current_us;
                last_sync_us = current_us;
                callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
                update_status(status, &status_arc);
            }
            // Retire a failed backend so permission failures cannot keep
            // stopping a later Listen Mode session.
            key_sim.set_output(None);
            #[cfg(windows)]
            { midi.snapshot.error = Some(error); }
            #[cfg(not(windows))]
            { keyboard_error = Some(error); }
        }
        if !key_sim.wheel_active() {
            if let Some((fret, reply)) = manual_mode_switch.take() {
                let result = if key_sim.fret_mode() == Some(fret) { Ok(()) }
                    else { Err("The guitar mode change was interrupted. Sync the current game mode.".into()) };
                let _ = reply.send(result);
            }
        }
        let next_wheel_state = key_sim.wheel_state();
        if status != PlaybackStatus::Playing && wheel_state.busy && !next_wheel_state.busy {
            // A manual page move changes the starting position of the arrangement.
            rebuild_chords |= mapping.chord_wheel().is_some();
        }
        if wheel_state != next_wheel_state {
            wheel_state = next_wheel_state;
            callback(PlayerEvent::ChordWheelChanged(wheel_state.clone()));
        }
        #[cfg(windows)]
        { midi.snapshot.keyboard_available = key_sim.is_ready() && midi.snapshot.error.is_none(); }
        #[cfg(not(windows))]
        {
            midi.snapshot.keyboard_available = key_sim.is_ready();
            if midi.snapshot.keyboard_available { keyboard_error = None; }
        }
        *keyboard_status.lock().unwrap_or_else(|e| e.into_inner()) = key_sim.status();
        #[cfg(windows)]
        { *midi_snapshot.lock().unwrap_or_else(|e| e.into_inner()) = midi.snapshot.clone(); }
        #[cfg(not(windows))]
        {
            let mut published = midi.snapshot.clone();
            if published.error.is_none() { published.error = keyboard_error.clone(); }
            *midi_snapshot.lock().unwrap_or_else(|e| e.into_inner()) = published;
            // A barrier must not expose the state from the previous worker cycle.
            if let Some(reply) = fence_reply { let _ = reply.send(()); }
            if let Some((reply, result)) = keyboard_reply { let _ = reply.send(result); }
        }
        let now_preview = Instant::now();
        preview_audio.retain(|&pitch, deadline| {
            if *deadline > now_preview { return true; }
            if let Some(synth) = &audio_synth { synth.for_source(2).note_off(0, pitch); }
            false
        });
        preview_keys.retain(|&key, deadline| {
            if *deadline > now_preview { return true; }
            key_sim.send_owned_key(4, key, true); false
        });

        // Reconcile after commands as well as CC events: toggles, resumes,
        // seeks and output/layout changes must update held game keys promptly.
        sync_game_pedals(&mut key_sim, &mapping, pedals.state(),
            pedals_enabled && send_input && !listen_mode && status == PlaybackStatus::Playing
                && config_available.load(Ordering::Acquire));

        let now = Instant::now();
        if arrangement_clear_at.is_some_and(|deadline| now >= deadline || status != PlaybackStatus::Playing || !send_input || listen_mode) {
            arrangement.slot = None;
            arrangement.strings = 0;
            arrangement.strum = None;
            arrangement_clear_at = None;
            callback(PlayerEvent::Arrangement(arrangement.clone()));
        }
        // A threshold/hold/speed change also applies to notes currently held.
        for (&owner, note) in &mut live_notes {
            if note.adapted_timing && note.deadline.is_none()
                && is_tap(hold_notes, tap_threshold_ms, note.duration_us, speed) {
                let deadline = note.started + Duration::from_millis(tap_duration_ms);
                note.deadline = Some(deadline);
                if !note.chord_pad {
                    if let Some(key) = note.key { key_releases.retime(key, owner, deadline); }
                }
            }
        }
        for (stroke, owners) in key_releases.take_presses(now) {
            key_sim.press_stroke(stroke);
            for owner in owners {
                if let Some(chord) = chord_notes.get(owner).copied().flatten().filter(|c| c.trigger && c.binding == ChordBinding::Key(stroke)) {
                    if let Some(slot) = mapping.chord_slot(chord.binding).filter(|_| key_sim.dispatch_succeeded()) {
                        arrangement.mode = Some("chords");
                        arrangement.slot = Some(slot);
                        arrangement.page = None;
                        arrangement.strings = 0;
                        arrangement.strum = None;
                        arrangement_clear_at = Some(now + Duration::from_millis(tap_duration_ms));
                        callback(PlayerEvent::Arrangement(arrangement.clone()));
                    }
                }
                if let Some(note) = live_notes.get_mut(&owner).filter(|note| !note.chord_pad) {
                    note.deadline = note.deadline.map(|at| now + at.saturating_duration_since(note.started));
                    note.started = now;
                }
            }
        }
        for key in key_releases.take_due(now) {
            key_sim.send_key(key, true);
        }
        let due: Vec<_> = live_notes.iter()
            .filter_map(|(&owner, note)| note.deadline.filter(|&at| at <= now).map(|_| owner)).collect();
        if !due.is_empty() {
            for owner in due { finish_live_note(owner, &mut live_notes, &audio_synth, &callback); }
            publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);
        }

        // 2. If not playing, sleep coarse and continue
        if status != PlaybackStatus::Playing {
            if key_sim.wheel_active() {
                let plan = wheel_step(&key_sim, Instant::now());
                pending_command = wait_step(&cmd_rx, sleeper, plan);
                continue;
            }
            let wait = key_releases.next_deadline()
                .map(|deadline| deadline.saturating_duration_since(Instant::now()))
                .unwrap_or(Duration::from_millis(15));
            pending_command = cmd_rx.recv_timeout(wait.min(Duration::from_millis(5))).ok();
            continue;
        }

        let song = match current_song {
            Some(ref s) => s,
            None => {
                status = PlaybackStatus::Stopped;
                update_status(status, &status_arc);
                sleeper.sleep(Duration::from_millis(15));
                continue;
            }
        };

        let mut wheel_prep_wait_us: Option<u64> = None;
        if send_input && !listen_mode && mapping.chord_wheel().is_some() {
            let next_pos = wheel_attacks.partition_point(|attack| attack.owner < event_idx);
            let next_attack = wheel_attacks.get(next_pos).copied();
            let prev_attack = next_pos.checked_sub(1).and_then(|i| wheel_attacks.get(i)).copied();
            let mut prepared_next = false;

            if let Some(attack) = next_attack {
                if let Some(config) = mapping.wheel_for_mode(attack.fret) {
                    let context = wheel_contexts[attack.fret as usize].as_deref();
                    let budget_us = key_sim.target_preparation_ms(config, attack.slot, attack.fret, context) * 1000;
                    let until_us = (attack.at.saturating_sub(current_us) as f64 / speed) as u64;
                    if until_us <= budget_us {
                        prepared_next = true;
                        if !mapping.is_mixed() || key_sim.fret_mode() == Some(attack.fret) {
                            key_sim.reconcile_wheel(config, attack.slot, Some(attack.owner));
                        }
                        if !key_sim.wheel_active() {
                            if mapping.is_mixed() && key_sim.fret_mode() != Some(attack.fret) {
                                if let (Some(_), Some(context), Some(key)) = (key_sim.fret_mode(), context, mapping.frets().and_then(|f| f.mode_key)) {
                                    key_sim.begin_mode_switch(attack.fret, context, key, config, mapping.game_process_names());
                                } else {
                                    key_sim.fail_wheel("Synchronize the current guitar mode before Mixed playback.");
                                }
                            } else {
                                key_sim.prepare_wheel(config, mapping.game_process_names(), attack.slot, attack.owner);
                            }
                        }
                        // Starting and seeking get preparation time before the song
                        // clock runs. Later page changes must fit between attacks.
                        if wheel_starting && !key_sim.wheel_ready(attack.owner) {
                            let plan = wheel_step(&key_sim, Instant::now());
                            pending_command = wait_step(&cmd_rx, sleeper, plan);
                            last_instant = Instant::now();
                            continue;
                        }
                    } else {
                        wheel_prep_wait_us = Some(until_us - budget_us);
                    }
                }
            }

            if !prepared_next {
                if let Some(prev) = prev_attack {
                    if let Some(config) = mapping.wheel_for_mode(prev.fret) {
                        let target_page = prev.slot / config.positions.len();
                        let out_of_sync = (mapping.is_mixed() && key_sim.fret_mode() != Some(prev.fret))
                            || key_sim.wheel_state().page != Some(target_page)
                            || key_sim.wheel_selected() != Some(prev.slot);
                        if out_of_sync || (wheel_starting && key_sim.wheel_active()) {
                            let prev_context = wheel_contexts[prev.fret as usize].as_deref();
                            let can_correct = wheel_starting
                                || next_attack.is_none_or(|next| next.fret == prev.fret && next.slot == prev.slot);
                            if can_correct {
                                if out_of_sync {
                                    if !mapping.is_mixed() || key_sim.fret_mode() == Some(prev.fret) {
                                        key_sim.reconcile_wheel(config, prev.slot, None);
                                    }
                                    if !key_sim.wheel_active() {
                                        if mapping.is_mixed() && key_sim.fret_mode() != Some(prev.fret) {
                                            if let (Some(_), Some(context), Some(key)) = (key_sim.fret_mode(), prev_context, mapping.frets().and_then(|f| f.mode_key)) {
                                                key_sim.begin_mode_switch(prev.fret, context, key, config, mapping.game_process_names());
                                            } else {
                                                key_sim.fail_wheel("Synchronize the current guitar mode before Mixed playback.");
                                            }
                                        } else {
                                            key_sim.sync_wheel_slot(config, mapping.game_process_names(), prev.slot);
                                        }
                                    }
                                }
                                if wheel_starting && (out_of_sync || key_sim.wheel_active()) {
                                    let plan = wheel_step(&key_sim, Instant::now());
                                    pending_command = wait_step(&cmd_rx, sleeper, plan);
                                    last_instant = Instant::now();
                                    continue;
                                }
                            }
                        }
                    }
                }
            }
        }
        wheel_starting = false;

        // Chase held source notes only once playback is running. A stopped or
        // paused seek remains silent, and ordinary ticks never scan old events.
        let mut restored_owners: VecDeque<_> = if restore_notes {
            restore_notes = false;
            pending_note_owners(&song.events, event_idx).into()
        } else { VecDeque::new() };

        // 3. Check if song finished
        if event_idx >= song.events.len() && control_idx >= song.control_events.len() {
            if key_sim.wheel_active() {
                let plan = wheel_step(&key_sim, Instant::now());
                pending_command = wait_step(&cmd_rx, sleeper, plan);
                continue;
            }
            // A tap may outlast its MIDI note, including the final note in the song.
            if let Some(deadline) = next_note_deadline(&key_releases, &live_notes) {
                pending_command = cmd_rx.recv_timeout(deadline.saturating_duration_since(Instant::now()).min(Duration::from_millis(10))).ok();
                continue;
            }
            status = PlaybackStatus::Stopped;
            key_sim.release_all(&all_keys);
            if let Some(ref synth) = audio_synth {
                synth.all_notes_off();
            }
            active_notes.clear();
            active_track_notes.clear();
            active_held_notes.clear();
            live_notes.clear();
            key_releases.clear();
            callback(PlayerEvent::ActiveNotes(Vec::new()));
            callback(PlayerEvent::ActiveTrackNotes(Vec::new()));
            pedals = PedalTracker::default();
            callback(PlayerEvent::PedalsChanged(pedals.state()));
            update_status(status, &status_arc);
            callback(PlayerEvent::Finished);
            continue;
        }

        let target_us = song.events.get(event_idx).map(|e| e.time_us)
            .into_iter()
            .chain(song.control_events.get(control_idx).map(|e| e.time_us))
            .min()
            .unwrap_or(current_us);

        // Calculate time until next note
        if restored_owners.is_empty() && target_us > current_us {
            let delta_us = target_us - current_us;
            let real_wait_us = ((delta_us as f64) / speed).round() as u64;

            if real_wait_us > 1_000 {
                let now_wait = Instant::now();
                let release_wait_us = next_note_deadline(&key_releases, &live_notes)
                    .map(|deadline| deadline.saturating_duration_since(now_wait).as_micros() as u64);
                let wheel_wait_us = key_sim
                    .wheel_next_deadline(now_wait)
                    .map(|deadline| deadline.saturating_duration_since(now_wait).as_micros() as u64);
                let next_boundary_us = real_wait_us
                    .min(wheel_prep_wait_us.unwrap_or(u64::MAX))
                    .min(wheel_wait_us.unwrap_or(u64::MAX))
                    .min(release_wait_us.unwrap_or(u64::MAX));
                let plan = schedule_step(next_boundary_us);
                pending_command = wait_step(&cmd_rx, sleeper, plan);
                let elapsed = last_instant.elapsed();
                last_instant = Instant::now();
                current_us += (elapsed.as_micros() as f64 * speed).round() as u64;
                *time_arc.lock().unwrap() = current_us;

                if current_us.saturating_sub(last_sync_us) >= sync_interval_us {
                    last_sync_us = current_us;
                    callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
                }
                continue;
            } else if real_wait_us > 0 {
                sleeper.sleep(Duration::from_micros(real_wait_us));
            }
        }

        // 4. Dispatch all events that occur at this current timestamp
        let elapsed = last_instant.elapsed();
        last_instant = Instant::now();
        current_us += (elapsed.as_micros() as f64 * speed).round() as u64;
        *time_arc.lock().unwrap() = current_us;

        // 4a. Process MIDI Control Events (Sustain CC 64, Volume CC 7, Program Changes)
        while control_idx < song.control_events.len() && song.control_events[control_idx].time_us <= current_us {
            let cev = &song.control_events[control_idx];
            let previous_pedals = pedals.state();
            pedals.apply(cev.channel, &cev.kind);
            if pedals.state() != previous_pedals {
                sync_game_pedals(&mut key_sim, &mapping, pedals.state(),
                    pedals_enabled && send_input && !listen_mode
                        && config_available.load(Ordering::Acquire));
                callback(PlayerEvent::PedalsChanged(pedals.state()));
            }
            if listen_mode {
                if let Some(ref synth) = audio_synth {
                    match cev.kind {
                        ControlEventKind::ControlChange { controller, value } => {
                            synth.control_change(cev.channel, controller, value);
                        }
                        ControlEventKind::ProgramChange { program } => {
                            synth.program_change(cev.channel, program);
                        }
                    }
                }
            }
            control_idx += 1;
        }

        // 4b. Process Note Events
        while !restored_owners.is_empty()
            || (event_idx < song.events.len() && song.events[event_idx].time_us <= current_us) {
            let restored = restored_owners.pop_front();
            let restoring = restored.is_some();
            let owner_index = restored.unwrap_or(event_idx);
            let ev = &song.events[owner_index];

            let track_vol = track_volumes.get(&ev.track_index).copied().unwrap_or(1.0);

            if ev.is_note_on && ev.velocity > 0 {
                let effective_vel = ((ev.velocity as f32) * track_vol).round().clamp(0.0, 127.0) as u8;

                // Look up dynamic key marker with 10ms lookahead (parity with WPF)
                let active_marker = if key_markers_enabled {
                    let target_ms = (ev.time_us + 10_000) / 1000;
                    get_active_marker(&key_markers, target_ms)
                } else {
                    None
                };

                let active_key_offset = active_marker.map(|m| m.key_offset).unwrap_or(key_offset);
                let active_transpose_mode = active_marker.and_then(|m| m.transpose).unwrap_or(transpose_mode);

                if !muted_tracks.contains(&ev.track_index) && effective_vel > 0 {
                    let adapted_timing = !listen_mode
                        || (synth_mode == SynthMode::Adapted && game_preview_follow_note_timing);
                    if restoring && (durations[owner_index].is_some_and(|duration|
                        ev.time_us.saturating_add(duration) <= current_us)
                        || (adapted_timing && is_tap(hold_notes, tap_threshold_ms, durations[owner_index], speed))) {
                        // Do not replay historical taps or notes ending exactly at
                        // the playhead, but reserve their FIFO NoteOff position.
                        active_held_notes.entry((ev.track_index, ev.channel, ev.note)).or_default()
                                .push_back((None, None, owner_index));
                        continue;
                    }
                    // Apply transposition and smart mode adaptation for NoteOn
                    let mapped_note = process_note_pitch(
                        ev.note,
                        active_key_offset,
                        active_transpose_mode,
                        min_pitch,
                        max_pitch,
                        &playable_notes,
                    );
                    let chord = (!listen_mode).then(|| chord_notes.get(owner_index).copied().flatten()).flatten();
                    let chord_pad = chord.is_some_and(|note| note.trigger && !restoring);
                    let mapped_stroke = match chord {
                        // Pads are one timed attack, never retriggered by a historical seek/resume.
                        Some(note) => match note.binding {
                            ChordBinding::Key(stroke) => (note.trigger && !restoring).then_some(stroke),
                            ChordBinding::Wheel(_)
                            | ChordBinding::WheelUp(_)
                            | ChordBinding::Pluck { .. }
                            | ChordBinding::Frets { .. } => None,
                        },
                        None => mapped_note.and_then(|n| mapping.get_stroke(n)),
                    };
                    let mapped_key = mapped_stroke.map(|stroke| stroke.key);

                    if log_played_notes {
                        log::info!(target: "amp_core::player::note_diagnostics",
                            "NOTE_ON track={} channel={} original={} mapped={:?} key={:?} velocity={} output={} restored={} chord_pad={}",
                            ev.track_index, ev.channel, ev.note, mapped_note, mapped_stroke, effective_vel,
                            if listen_mode { "synth" } else if send_input { "game" } else { "disabled" }, restoring, chord_pad);
                        log::info!(target: "amp_core::player::scheduler_diagnostics",
                            "DISPATCH scheduled_us={} actual_us={} drift_us={} speed={} track={} original={}",
                            ev.time_us, current_us, ((current_us.saturating_sub(ev.time_us) as f64) / speed).round() as u64,
                            speed, ev.track_index, ev.note);
                    }

                    active_held_notes.entry((ev.track_index, ev.channel, ev.note)).or_default()
                        .push_back((mapped_note, mapped_key, owner_index));

                    let started = Instant::now();
                    let deadline = (adapted_timing && is_tap(hold_notes, tap_threshold_ms, durations[owner_index], speed))
                        .then(|| started + Duration::from_millis(tap_duration_ms));
                    let synth_note = if listen_mode {
                        match synth_mode {
                            SynthMode::Adapted => mapped_note,
                            SynthMode::Raw => Some(ev.note),
                        }.map(|pitch| (ev.channel, pitch))
                    } else { None };
                    live_notes.insert(owner_index, LiveNote {
                        pitch: mapped_note, key: mapped_key, track_index: ev.track_index,
                        synth_note, started, duration_us: durations[owner_index], deadline, adapted_timing, chord_pad,
                    });

                    if listen_mode {
                        if let Some(ref synth) = audio_synth {
                            if let Some((channel, note)) = synth_note {
                                synth.note_on(channel, note, effective_vel);
                            }
                        }
                    } else if send_input {
                        if let Some(chord) = chord.filter(|note| note.trigger && !restoring) {
                            triggered_chords.insert(chord.chord_id);
                            if let Ok(index) = wheel_attacks.binary_search_by_key(&owner_index, |a| a.owner) {
                                let attack = wheel_attacks[index];
                                key_sim.tick_wheel();
                                if !key_sim.wheel_ready(owner_index) && (!mapping.is_mixed() || key_sim.fret_mode() == Some(attack.fret)) {
                                    if let Some(config) = mapping.wheel_for_mode(attack.fret) {
                                        key_sim.reconcile_wheel(config, attack.slot, Some(owner_index));
                                        let target_page = attack.slot / config.positions.len();
                                        if !key_sim.wheel_active()
                                            && key_sim.wheel_state().page == Some(target_page)
                                            && key_sim.wheel_selected() == Some(attack.slot)
                                        {
                                            key_sim.prepare_wheel(config, mapping.game_process_names(), attack.slot, owner_index);
                                        }
                                    }
                                }
                                if !key_sim.wheel_ready(owner_index) {
                                    // An overloaded scheduler must never turn a missed attack into a late one.
                                    key_sim.expire_wheel(owner_index);
                                    arrangement.omitted_melody += 1;
                                    arrangement.omitted_accompaniment += attack.members.saturating_sub(1);
                                    callback(PlayerEvent::Arrangement(arrangement.clone()));
                                } else {
                                    let strings = match chord.binding {
                                        ChordBinding::Pluck { strings, .. } | ChordBinding::Frets { strings, .. } => strings,
                                        _ => 0,
                                    };
                                    let strum_dir = if strings == 0 {
                                        let dir = chord
                                            .binding
                                            .strum_direction()
                                            .unwrap_or(crate::input::StrumDirection::Down);
                                        key_sim.strum_wheel_direction(owner_index, dir, tap_duration_ms);
                                        Some(dir.as_str())
                                    } else {
                                        if let Some(frets) = mapping.frets() {
                                            let strokes: Vec<_> = frets.string_strokes.iter().enumerate()
                                                .filter_map(|(index, &stroke)| (strings & (1 << index) != 0).then_some(stroke)).collect();
                                            key_sim.pluck_wheel(owner_index, &strokes, tap_duration_ms);
                                        }
                                        None
                                    };
                                    if key_sim.dispatch_succeeded() {
                                        arrangement.mode = Some(if attack.fret { "frets" } else { "chords" });
                                        arrangement.slot = Some(attack.slot);
                                        arrangement.page = key_sim.wheel_state().page;
                                        arrangement.strings = strings;
                                        arrangement.strum = strum_dir;
                                        arrangement_clear_at = Some(started + Duration::from_millis(tap_duration_ms));
                                        callback(PlayerEvent::Arrangement(arrangement.clone()));
                                    }
                                }
                            }
                        }
                        if let Some(stroke) = mapped_stroke {
                            if let Some(chord) = chord.filter(|note| note.trigger && !restoring) {
                                triggered_chords.insert(chord.chord_id);
                            }
                            let key = stroke.key;
                            let key_deadline = if chord_pad { Some(started + Duration::from_millis(tap_duration_ms)) } else { deadline };
                            let (action, press_at) = if chord_pad && mapping.is_chord_only() {
                                // Chord attacks carry their own musical onset. The overlap
                                // preference for melody keys must not suppress or defer a pad.
                                if key_releases.press(key, owner_index, key_deadline) {
                                    key_sim.send_key(key, true);
                                }
                                (KeyPressAction::Press, started)
                            } else {
                                key_releases.begin_stroke(stroke, owner_index, started, key_deadline, overlap_behavior)
                            };
                            if let Some(note) = live_notes.get_mut(&owner_index).filter(|note| !note.chord_pad) {
                                note.started = press_at;
                                note.deadline = deadline.map(|at| press_at + at.saturating_duration_since(started));
                            }
                            match action {
                                KeyPressAction::Press => key_sim.press_stroke(stroke),
                                KeyPressAction::Retrigger => key_sim.send_key(key, true),
                                KeyPressAction::Deferred | KeyPressAction::Ignore => {}
                            }
                            if chord_pad && matches!(action, KeyPressAction::Press) && key_sim.dispatch_succeeded() {
                                arrangement.mode = Some("chords");
                                arrangement.slot = mapping.chord_slot(ChordBinding::Key(stroke));
                                arrangement.page = None;
                                arrangement.strings = 0;
                                arrangement.strum = None;
                                arrangement_clear_at = Some(started + Duration::from_millis(tap_duration_ms));
                                callback(PlayerEvent::Arrangement(arrangement.clone()));
                            }
                        }
                    }

                    if let Some(note) = mapped_note {
                        callback(PlayerEvent::NoteHit {
                            note,
                            is_on: true,
                        });
                    }
                } else {
                    // Every source NoteOn reserves its FIFO slot, even when muted
                    // or scaled to zero velocity. Its NoteOff must not steal a
                    // later same-pitch note's owner after the track is enabled.
                    active_held_notes.entry((ev.track_index, ev.channel, ev.note)).or_default()
                        .push_back((None, None, owner_index));
                }
            } else {
                // NoteOff always runs, regardless of the current mute or volume.
                // Only release the exact output acquired by its matching NoteOn.
                if let Some((pitch, key, owner)) = active_held_notes
                    .get_mut(&(ev.track_index, ev.channel, ev.note))
                    .and_then(VecDeque::pop_front) {
                    if log_played_notes {
                        log::info!(target: "amp_core::player::note_diagnostics",
                            "NOTE_OFF track={} channel={} original={} mapped={:?} key={:?}",
                            ev.track_index, ev.channel, ev.note, pitch, key);
                    }
                    if let Some(key) = key {
                        if key_releases.note_off(key, owner) {
                            key_sim.send_key(key, true);
                        }
                    }
                    if live_notes.get(&owner).is_some_and(|note| note.deadline.is_none()) {
                        finish_live_note(owner, &mut live_notes, &audio_synth, &callback);
                    }
                }
            }

            if !restoring { event_idx += 1; }
        }

        publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);

        if current_us.saturating_sub(last_sync_us) >= sync_interval_us {
            last_sync_us = current_us;
            callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{schedule_step_for, WaitPlan, WaitProfile};

    #[test]
    fn schedule_step_uses_coarse_waits_until_final_deadline_window() {
        for (boundary_us, expected_windows, expected_macos, expected_other) in [
            (0, WaitPlan::Precise(0), WaitPlan::Precise(0), WaitPlan::Precise(0)),
            (500, WaitPlan::Precise(500), WaitPlan::Precise(500), WaitPlan::Precise(500)),
            (1_000, WaitPlan::Precise(1_000), WaitPlan::Precise(1_000), WaitPlan::Precise(1_000)),
            (5_000, WaitPlan::Precise(5_000), WaitPlan::Precise(1_000), WaitPlan::Precise(5_000)),
            (10_000, WaitPlan::Precise(10_000), WaitPlan::Precise(1_000), WaitPlan::Precise(10_000)),
            (10_500, WaitPlan::Precise(10_000), WaitPlan::Precise(1_000), WaitPlan::Channel(1_000)),
            (15_000, WaitPlan::Precise(10_000), WaitPlan::Precise(1_000), WaitPlan::Channel(5_000)),
            (15_001, WaitPlan::Precise(10_000), WaitPlan::Sleep(1_000), WaitPlan::Channel(5_001)),
            (25_000, WaitPlan::Precise(10_000), WaitPlan::Sleep(1_000), WaitPlan::Channel(10_000)),
            (25_001, WaitPlan::Channel(10_000), WaitPlan::Sleep(1_000), WaitPlan::Channel(10_000)),
            (145_000, WaitPlan::Channel(10_000), WaitPlan::Sleep(1_000), WaitPlan::Channel(10_000)),
        ] {
            assert_eq!(schedule_step_for(boundary_us, WaitProfile::Windows), expected_windows);
            assert_eq!(schedule_step_for(boundary_us, WaitProfile::MacOs), expected_macos);
            assert_eq!(schedule_step_for(boundary_us, WaitProfile::Other), expected_other);
        }
    }
}
