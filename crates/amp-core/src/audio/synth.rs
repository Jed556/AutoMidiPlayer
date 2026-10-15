use std::collections::{HashMap, HashSet};
use std::sync::Arc;
use std::time::Duration;
use crossbeam_channel::{unbounded, Receiver, Sender};
use rodio::{OutputStream, Sink, Source};
use rustysynth::{SoundFont, Synthesizer, SynthesizerSettings};

const SAMPLE_RATE: u32 = 44100;
const BLOCK_SIZE: usize = 256;

#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize, Default)]
pub enum SynthMode {
    #[default]
    Adapted,
    Raw,
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
    SetMasterVolume(f32),
    SetTrackMuted { track_index: usize, muted: bool },
    SetTrackVolume { track_index: usize, volume: f32 },
    LoadSoundFont(Arc<SoundFont>),
    AllNotesOff,
    Reset,
}

struct SynthSource {
    synth: Option<Synthesizer>,
    cmd_rx: Receiver<AudioCommand>,
    sample_rate: u32,
    master_volume: f32,
    track_volumes: HashMap<usize, f32>,
    muted_tracks: HashSet<usize>,
    left_buf: Vec<f32>,
    right_buf: Vec<f32>,
    block_buffer: Vec<f32>,
    block_pos: usize,
}

impl SynthSource {
    fn new(initial_soundfont: Option<Arc<SoundFont>>, cmd_rx: Receiver<AudioCommand>) -> Self {
        let synth = initial_soundfont.and_then(|sf| {
            let settings = SynthesizerSettings::new(SAMPLE_RATE as i32);
            Synthesizer::new(&sf, &settings).ok()
        });

        Self {
            synth,
            cmd_rx,
            sample_rate: SAMPLE_RATE,
            master_volume: 1.0,
            track_volumes: HashMap::new(),
            muted_tracks: HashSet::new(),
            left_buf: vec![0.0f32; BLOCK_SIZE],
            right_buf: vec![0.0f32; BLOCK_SIZE],
            block_buffer: Vec::with_capacity(BLOCK_SIZE * 2),
            block_pos: 0,
        }
    }

    fn render_next_block(&mut self) {
        // 1. Drain pending commands
        while let Ok(cmd) = self.cmd_rx.try_recv() {
            match cmd {
                AudioCommand::NoteOn { channel, note, velocity } => {
                    if let Some(ref mut s) = self.synth {
                        s.note_on(channel as i32, note as i32, velocity as i32);
                    }
                }
                AudioCommand::NoteOff { channel, note } => {
                    if let Some(ref mut s) = self.synth {
                        s.note_off(channel as i32, note as i32);
                    }
                }
                AudioCommand::ControlChange { channel, controller, value } => {
                    if let Some(ref mut s) = self.synth {
                        s.process_midi_message(channel as i32, 0xB0, controller as i32, value as i32);
                    }
                }
                AudioCommand::ProgramChange { channel, program } => {
                    if let Some(ref mut s) = self.synth {
                        s.process_midi_message(channel as i32, 0xC0, program as i32, 0);
                    }
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
                AudioCommand::LoadSoundFont(sf) => {
                    let settings = SynthesizerSettings::new(self.sample_rate as i32);
                    if let Ok(new_synth) = Synthesizer::new(&sf, &settings) {
                        self.synth = Some(new_synth);
                    }
                }
                AudioCommand::AllNotesOff => {
                    if let Some(ref mut s) = self.synth {
                        for ch in 0..16 {
                            s.process_midi_message(ch, 0xB0, 123, 0); // All Notes Off
                            s.process_midi_message(ch, 0xB0, 120, 0); // All Sound Off
                            s.process_midi_message(ch, 0xB0, 64, 0);  // Release Damper / Sustain
                        }
                    }
                }
                AudioCommand::Reset => {
                    if let Some(ref mut s) = self.synth {
                        s.reset();
                    }
                }
            }
        }

        // 2. Render samples from synthesizer
        self.block_buffer.clear();

        if let Some(ref mut s) = self.synth {
            self.left_buf.fill(0.0);
            self.right_buf.fill(0.0);
            s.render(&mut self.left_buf, &mut self.right_buf);

            let vol = self.master_volume;
            for i in 0..BLOCK_SIZE {
                let l = (self.left_buf[i] * vol).clamp(-1.0, 1.0);
                let r = (self.right_buf[i] * vol).clamp(-1.0, 1.0);
                self.block_buffer.push(l);
                self.block_buffer.push(r);
            }
        } else {
            // Silence
            self.block_buffer.resize(BLOCK_SIZE * 2, 0.0);
        }

        self.block_pos = 0;
    }
}

impl Source for SynthSource {
    fn current_frame_len(&self) -> Option<usize> {
        None
    }

    fn channels(&self) -> u16 {
        2 // Stereo
    }

    fn sample_rate(&self) -> u32 {
        self.sample_rate
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

/// Manages the OS audio stream lifecycle. Keep this held on the host thread or state.
pub struct AudioDevice {
    _stream: OutputStream,
    _sink: Sink,
    synth: AudioSynth,
}

impl AudioDevice {
    pub fn new(initial_soundfont: Option<Arc<SoundFont>>) -> Result<Self, AudioError> {
        let (stream, stream_handle) = OutputStream::try_default()
            .map_err(|e| AudioError::StreamInit(e.to_string()))?;

        let sink = Sink::try_new(&stream_handle)
            .map_err(|e| AudioError::SinkInit(e.to_string()))?;

        let (cmd_tx, cmd_rx) = unbounded::<AudioCommand>();
        let source = SynthSource::new(initial_soundfont, cmd_rx);

        sink.append(source);
        sink.play();

        let synth = AudioSynth { cmd_tx };

        Ok(Self {
            _stream: stream,
            _sink: sink,
            synth,
        })
    }

    pub fn synth(&self) -> AudioSynth {
        self.synth.clone()
    }
}

/// Send + Sync handle to send audio events from any playback thread without locking.
#[derive(Clone)]
pub struct AudioSynth {
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

    pub fn set_master_volume(&self, volume: f32) {
        let _ = self.cmd_tx.send(AudioCommand::SetMasterVolume(volume));
    }

    pub fn set_track_muted(&self, track_index: usize, muted: bool) {
        let _ = self.cmd_tx.send(AudioCommand::SetTrackMuted { track_index, muted });
    }

    pub fn set_track_volume(&self, track_index: usize, volume: f32) {
        let _ = self.cmd_tx.send(AudioCommand::SetTrackVolume { track_index, volume });
    }

    pub fn load_soundfont(&self, soundfont: Arc<SoundFont>) {
        let _ = self.cmd_tx.send(AudioCommand::LoadSoundFont(soundfont));
    }

    pub fn all_notes_off(&self) {
        let _ = self.cmd_tx.send(AudioCommand::AllNotesOff);
    }

    pub fn reset(&self) {
        let _ = self.cmd_tx.send(AudioCommand::Reset);
    }
}
