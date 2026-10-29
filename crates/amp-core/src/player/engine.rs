use std::collections::{HashMap, HashSet, VecDeque};
use std::sync::{Arc, Mutex};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};
use crossbeam_channel::{unbounded, Sender, Receiver};
use spin_sleep::SpinSleeper;

use crate::midi::{
    ParsedMidi, TransposeMode, ControlEventKind,
    detect_best_scale, build_scale_pitch_classes, process_note_pitch,
    detect::KeyChangeMarker,
};
use crate::input::{NoteKeyMapping, KeySimulator};
use crate::audio::{AudioSynth, SynthMode};
use enigo::Key;
use super::note_timing::{is_tap, note_durations, KeyReleases, KeyPressAction, OverlapBehavior};

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

#[derive(Debug, Clone)]
pub enum PlayerEvent {
    TimeUpdate { time_us: u64, seek_id: u64 },
    ActiveNotes(Vec<u8>),
    PedalsChanged(PedalState),
    ActiveTrackNotes(Vec<ActiveTrackNote>),
    NoteHit { note: u8, is_on: bool },
    StateChanged(PlaybackStatus),
    Finished,
}

enum Command {
    Play,
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
    SetNoteTiming { hold_notes: bool, tap_threshold_ms: u64 },
    SetPedalsEnabled(bool),
    SetSendInput(bool),
    LoadSong(ParsedMidi),
    Panic,
    Shutdown,
    // Phase 4: Audio Synthesizer & Listen Mode commands
    SetListenMode(bool),
    SetSynthMode(SynthMode),
    SetMasterVolume(f32),
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
}

impl PlaybackEngine {
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
            );
        });

        Self {
            cmd_tx,
            status,
            current_time_us,
            config_available,
            worker_handle: Some(worker_handle),
        }
    }

    pub fn play(&self) {
        let _ = self.cmd_tx.send(Command::Play);
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

    pub fn set_note_timing(&self, hold_notes: bool, tap_threshold_ms: u64) {
        let _ = self.cmd_tx.send(Command::SetNoteTiming { hold_notes, tap_threshold_ms });
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
) {
    let mut key_sim = KeySimulator::new();
    let sleeper = SpinSleeper::default();

    let mut durations = initial_song.as_ref().map(|song| note_durations(&song.events)).unwrap_or_default();
    let mut current_song = initial_song;
    let mut hold_notes = false;
    let mut tap_threshold_ms = 0;
    let mut tap_duration_ms = 20;
    let mut overlap_behavior = OverlapBehavior::default();
    let mut key_releases = KeyReleases::default();
    let mut event_idx = 0usize;
    let mut control_idx = 0usize;
    let mut pedals = PedalTracker::default();
    let mut pedals_enabled = true;
    let mut current_us = 0u64;
    let mut speed = 1.0f64;
    let mut key_offset = 0i8;
    let mut transpose_mode = TransposeMode::Smart;
    let mut sync_interval_us = 16_666u64; // Default: ~60 FPS
    let mut muted_tracks = HashSet::<usize>::new();
    let mut track_volumes = HashMap::<usize, f32>::new();
    let mut active_notes = HashSet::<u8>::new();
    let mut active_track_notes = HashSet::<ActiveTrackNote>::new();
    let mut live_notes = HashMap::<usize, LiveNote>::new();
    let mut key_markers = Vec::<KeyChangeMarker>::new();
    let mut key_markers_enabled = true;
    let mut active_held_notes = HashMap::<(usize, u8, u8), VecDeque<(Option<u8>, Option<Key>, usize)>>::new();
    let mut status = PlaybackStatus::Stopped;
    let mut last_instant = Instant::now();
    let mut last_sync_us = 0u64;
    let mut seek_id = 0u64;

    // Phase 4 audio states
    let mut listen_mode = false;
    let mut synth_mode = SynthMode::Adapted;

    let mut mapping = mapping;
    let mut all_keys = mapping.all_keys();
    let (mut min_pitch, mut max_pitch) = mapping.pitch_range();
    let mut playable_notes = mapping.playable_notes().to_vec();

    // Scale pitch classes for smart transposition
    let mut scale_pcs = if let Some(ref s) = current_song {
        let (tonic, mode_idx) = detect_best_scale(&s.events, 60);
        build_scale_pitch_classes(tonic, mode_idx)
    } else {
        build_scale_pitch_classes(0, 0)
    };

    let update_status = |new_status: PlaybackStatus, status_arc: &Arc<Mutex<PlaybackStatus>>| {
        *status_arc.lock().unwrap() = new_status;
        callback(PlayerEvent::StateChanged(new_status));
    };

    loop {
        // 1. Process pending commands
        while let Ok(cmd) = cmd_rx.try_recv() {
            match cmd {
                Command::Play => {
                    if config_available.load(Ordering::Acquire) && status != PlaybackStatus::Playing && current_song.is_some() {
                        if current_song.as_ref().is_some_and(|song| event_idx >= song.events.len() && control_idx >= song.control_events.len()) {
                            current_us = 0;
                            event_idx = 0;
                            control_idx = 0;
                            last_sync_us = 0;
                            *time_arc.lock().unwrap() = 0;
                            callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
                        }
                        if let Some(ref song) = current_song {
                            pedals.restore(song, control_idx);
                            callback(PlayerEvent::PedalsChanged(pedals.state()));
                        }
                        status = PlaybackStatus::Playing;
                        last_instant = Instant::now();
                        update_status(status, &status_arc);
                    }
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
                    status = PlaybackStatus::Stopped;
                    current_us = 0;
                    last_sync_us = 0;
                    event_idx = 0;
                    control_idx = 0;
                    *time_arc.lock().unwrap() = 0;
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
                Command::Seek(target_us, request_id) => {
                    if request_id < seek_id {
                        continue;
                    }
                    seek_id = request_id;
                    if let Some(ref song) = current_song {
                        current_us = target_us.min(song.duration_us);
                        *time_arc.lock().unwrap() = current_us;
                        event_idx = match song.events.binary_search_by_key(&current_us, |e| e.time_us) {
                            Ok(idx) => idx,
                            Err(idx) => idx,
                        };
                        control_idx = song.control_events.partition_point(|e| e.time_us < current_us);
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
                    speed = new_speed.clamp(0.1, 5.0);
                }
                Command::SetKeyOffset(offset) => {
                    key_offset = offset;
                }
                Command::SetTransposeMode(mode) => {
                    transpose_mode = mode;
                }
                Command::SetTrackMuted(track_idx, muted) => {
                    if muted {
                        muted_tracks.insert(track_idx);
                    } else {
                        muted_tracks.remove(&track_idx);
                    }
                    if let Some(ref synth) = audio_synth {
                        synth.set_track_muted(track_idx, muted);
                    }
                }
                Command::SetFps(fps) => {
                    if fps == 0 {
                        sync_interval_us = 4_000; // ~240 FPS / uncapped
                    } else {
                        let clamped = fps.clamp(10, 240);
                        sync_interval_us = 1_000_000 / (clamped as u64);
                    }
                }
                Command::SetHoldDelayMs(ms) => {
                    tap_duration_ms = ms.min(1000);
                    key_sim.set_hold_delay_ms(ms);
                }
                Command::SetOverlapBehavior(behavior) => {
                    overlap_behavior = behavior;
                }
                Command::SetNoteTiming { hold_notes: enabled, tap_threshold_ms: threshold } => {
                    hold_notes = enabled;
                    tap_threshold_ms = threshold.min(10_000);
                }
                Command::SetPedalsEnabled(enabled) => {
                    pedals_enabled = enabled;
                }
                Command::SetSendInput(enabled) => {
                    send_input = enabled;
                    if !enabled {
                        key_sim.release_all(&all_keys);
                        key_releases.clear();
                    }
                }
                Command::LoadSong(new_song) => {
                    let (tonic, mode_idx) = detect_best_scale(&new_song.events, 60);
                    scale_pcs = build_scale_pitch_classes(tonic, mode_idx);
                    durations = note_durations(&new_song.events);
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
                    }
                    if status == PlaybackStatus::Playing {
                        last_instant = Instant::now();
                    }
                    callback(PlayerEvent::TimeUpdate { time_us: 0, seek_id });
                }
                Command::Panic => {
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
                }
                Command::Shutdown => {
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
                    listen_mode = enabled;
                    active_held_notes.clear();
                    live_notes.clear();
                    publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);
                    key_releases.clear();
                    if listen_mode {
                        key_sim.release_all(&all_keys);
                    } else if let Some(ref synth) = audio_synth {
                        synth.all_notes_off();
                    }
                }
                Command::SetSynthMode(mode) => {
                    synth_mode = mode;
                }
                Command::SetMasterVolume(vol) => {
                    if let Some(ref synth) = audio_synth {
                        synth.set_master_volume(vol);
                    }
                }
                Command::SetTrackVolume(track_idx, vol) => {
                    track_volumes.insert(track_idx, vol.clamp(0.0, 1.0));
                    if let Some(ref synth) = audio_synth {
                        synth.set_track_volume(track_idx, vol);
                    }
                }
                Command::SetAudioSynth(s) => {
                    audio_synth = s;
                }
                Command::SetMapping(new_mapping) => {
                    key_sim.release_all(&all_keys);
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
                    if tap_listen {
                        if let Some(ref synth) = audio_synth {
                            synth.note_on(0, pitch, 100);
                            let s_clone = synth.clone();
                            std::thread::spawn(move || {
                                std::thread::sleep(Duration::from_millis(150));
                                s_clone.note_off(0, pitch);
                            });
                        }
                    } else if send_input {
                        if let Some(stroke) = mapping.get_stroke(pitch) {
                            let key = stroke.key;
                            let now = Instant::now();
                            let deadline = now + Duration::from_millis(tap_duration_ms);
                            match key_releases.begin_stroke(stroke, usize::MAX, now, Some(deadline), OverlapBehavior::Retrigger).0 {
                                KeyPressAction::Press => key_sim.press_stroke(stroke),
                                KeyPressAction::Retrigger => key_sim.send_key(key, true),
                                KeyPressAction::Deferred | KeyPressAction::Ignore => {}
                            }
                        }
                    }
                }
                Command::SetKeyMarkers(mut markers) => {
                    markers.sort_by_key(|m| m.time_ms);
                    key_markers = markers;
                }
                Command::SetKeyMarkersEnabled(enabled) => {
                    key_markers_enabled = enabled;
                }
            }
        }

        // Reconcile after commands as well as CC events: toggles, resumes,
        // seeks and output/layout changes must update held game keys promptly.
        sync_game_pedals(&mut key_sim, &mapping, pedals.state(),
            pedals_enabled && send_input && !listen_mode && status == PlaybackStatus::Playing
                && config_available.load(Ordering::Acquire));

        let now = Instant::now();
        // A threshold/hold/speed change also applies to notes currently held.
        for (&owner, note) in &mut live_notes {
            if note.adapted_timing && note.deadline.is_none()
                && is_tap(hold_notes, tap_threshold_ms, note.duration_us, speed) {
                let deadline = note.started + Duration::from_millis(tap_duration_ms);
                note.deadline = Some(deadline);
                if let Some(key) = note.key { key_releases.retime(key, owner, deadline); }
            }
        }
        for (stroke, owners) in key_releases.take_presses(now) {
            key_sim.press_stroke(stroke);
            for owner in owners {
                if let Some(note) = live_notes.get_mut(&owner) {
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
            let wait = key_releases.next_deadline()
                .map(|deadline| deadline.saturating_duration_since(Instant::now()))
                .unwrap_or(Duration::from_millis(15));
            sleeper.sleep(wait.min(Duration::from_millis(15)));
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

        // 3. Check if song finished
        if event_idx >= song.events.len() && control_idx >= song.control_events.len() {
            // A tap may outlast its MIDI note, including the final note in the song.
            if let Some(deadline) = next_note_deadline(&key_releases, &live_notes) {
                sleeper.sleep(deadline.saturating_duration_since(Instant::now()).min(Duration::from_millis(10)));
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
        if target_us > current_us {
            let delta_us = target_us - current_us;
            let real_wait_us = ((delta_us as f64) / speed).round() as u64;

            if real_wait_us > 1_000 {
                let release_wait = next_note_deadline(&key_releases, &live_notes)
                    .map(|deadline| deadline.saturating_duration_since(Instant::now()).as_micros() as u64)
                    .unwrap_or(10_000);
                let chunk_us = real_wait_us.min(10_000).min(release_wait);
                sleeper.sleep(Duration::from_micros(chunk_us));
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
        while event_idx < song.events.len() && song.events[event_idx].time_us <= current_us {
            let ev = &song.events[event_idx];

            let track_vol = track_volumes.get(&ev.track_index).copied().unwrap_or(1.0);

            if !muted_tracks.contains(&ev.track_index) && track_vol > 0.0 {
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

                if ev.is_note_on && effective_vel > 0 {
                    // Apply transposition and smart mode adaptation for NoteOn
                    let mapped_note = process_note_pitch(
                        ev.note,
                        active_key_offset,
                        active_transpose_mode,
                        &scale_pcs,
                        min_pitch,
                        max_pitch,
                        &playable_notes,
                    );
                    let mapped_stroke = mapped_note.and_then(|n| mapping.get_stroke(n));
                    let mapped_key = mapped_stroke.map(|stroke| stroke.key);

                    active_held_notes.entry((ev.track_index, ev.channel, ev.note)).or_default()
                        .push_back((mapped_note, mapped_key, event_idx));

                    let started = Instant::now();
                    let adapted_timing = !listen_mode || synth_mode == SynthMode::Adapted;
                    let deadline = (adapted_timing && is_tap(hold_notes, tap_threshold_ms, durations[event_idx], speed))
                        .then(|| started + Duration::from_millis(tap_duration_ms));
                    let synth_note = if listen_mode {
                        match synth_mode {
                            SynthMode::Adapted => mapped_note,
                            SynthMode::Raw => Some(ev.note),
                        }.map(|pitch| (ev.channel, pitch))
                    } else { None };
                    live_notes.insert(event_idx, LiveNote {
                        pitch: mapped_note, key: mapped_key, track_index: ev.track_index,
                        synth_note, started, duration_us: durations[event_idx], deadline, adapted_timing,
                    });

                    if listen_mode {
                        if let Some(ref synth) = audio_synth {
                            if let Some((channel, note)) = synth_note {
                                synth.note_on(channel, note, effective_vel);
                            }
                        }
                    } else if send_input {
                        if let Some(stroke) = mapped_stroke {
                            let key = stroke.key;
                            let (action, press_at) = key_releases.begin_stroke(stroke, event_idx, started, deadline, overlap_behavior);
                            if let Some(note) = live_notes.get_mut(&event_idx) {
                                note.started = press_at;
                                note.deadline = deadline.map(|at| press_at + at.saturating_duration_since(started));
                            }
                            match action {
                                KeyPressAction::Press => key_sim.press_stroke(stroke),
                                KeyPressAction::Retrigger => key_sim.send_key(key, true),
                                KeyPressAction::Deferred | KeyPressAction::Ignore => {}
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
                    // NoteOff: release the exact note and physical key that were previously held
                    let (_prev_mapped_note, prev_mapped_key, owner) = active_held_notes
                        .get_mut(&(ev.track_index, ev.channel, ev.note))
                        .and_then(VecDeque::pop_front)
                        .unwrap_or_else(|| {
                            let mn = process_note_pitch(
                                ev.note,
                                active_key_offset,
                                active_transpose_mode,
                                &scale_pcs,
                                min_pitch,
                                max_pitch,
                                &playable_notes,
                            );
                            let mk = mn.and_then(|n| mapping.get_key(n));
                            (mn, mk, usize::MAX)
                        });

                    if !listen_mode && send_input {
                        if let Some(key) = prev_mapped_key {
                            if key_releases.note_off(key, owner) {
                                key_sim.send_key(key, true);
                            }
                        }
                    }

                    if live_notes.get(&owner).is_some_and(|note| note.deadline.is_none()) {
                        finish_live_note(owner, &mut live_notes, &audio_synth, &callback);
                    }
                }
            }

            event_idx += 1;
        }

        publish_live_notes(&live_notes, &mut active_notes, &mut active_track_notes, &callback);

        if current_us.saturating_sub(last_sync_us) >= sync_interval_us {
            last_sync_us = current_us;
            callback(PlayerEvent::TimeUpdate { time_us: current_us, seek_id });
        }
    }
}
