use super::{font_stack::FontStack, synth::AudioCommand};
use crossbeam_channel::{bounded, unbounded, Receiver, Sender};
use rustysynth::{SoundFont, SynthesizerSettings};
use std::{
    path::PathBuf,
    sync::{
        atomic::{AtomicU64, Ordering},
        Arc,
    },
};

pub(super) enum FontInput {
    Paths(Vec<PathBuf>),
    Fonts(Vec<Arc<SoundFont>>),
}
struct Request {
    id: u64,
    input: FontInput,
    reply: Sender<Result<(), String>>,
}

/// Constructed only by the preparation worker, consumed at an audio block boundary.
pub struct PreparedSoundFont {
    pub(super) stack: Box<FontStack>,
    pub(super) id: u64,
    pub(super) latest: Arc<AtomicU64>,
    pub(super) reply: Sender<Result<(), String>>,
}

impl PreparedSoundFont {
    pub(super) fn is_current(&self) -> bool {
        self.latest.load(Ordering::Acquire) == self.id
    }
}

pub(super) struct FontLoader {
    tx: Sender<Request>,
    latest: Arc<AtomicU64>,
}
impl FontLoader {
    pub fn new(commands: Sender<AudioCommand>, sample_rate: u32) -> Self {
        let (tx, rx) = unbounded::<Request>();
        let latest = Arc::new(AtomicU64::new(0));
        let revision = latest.clone();
        std::thread::Builder::new()
            .name("soundfont-loader".into())
            .spawn(move || {
                while let Ok(mut request) = rx.recv() {
                    // Pending selections do not each need a disk read or a new synth.
                    while let Ok(next) = rx.try_recv() {
                        if next.id > request.id {
                            let _ = request
                                .reply
                                .send(Err("SoundFont change superseded".into()));
                            request = next;
                        } else {
                            let _ = next.reply.send(Err("SoundFont change superseded".into()));
                        }
                    }
                    if revision.load(Ordering::Acquire) != request.id {
                        let _ = request
                            .reply
                            .send(Err("SoundFont change superseded".into()));
                        continue;
                    }
                    let result = prepare(request.input, sample_rate);
                    if revision.load(Ordering::Acquire) != request.id {
                        let _ = request
                            .reply
                            .send(Err("SoundFont change superseded".into()));
                        continue;
                    }
                    match result {
                        Ok(stack) => {
                            let prepared = PreparedSoundFont {
                                stack: Box::new(stack),
                                id: request.id,
                                latest: revision.clone(),
                                reply: request.reply,
                            };
                            if commands
                                .send(AudioCommand::InstallSoundFont(Box::new(prepared)))
                                .is_err()
                            {
                                break;
                            }
                        }
                        Err(error) => {
                            let _ = request.reply.send(Err(error));
                        }
                    }
                }
            })
            .expect("start SoundFont loader");
        Self { tx, latest }
    }

    pub fn submit(&self, input: FontInput) -> Result<Receiver<Result<(), String>>, String> {
        let id = self.latest.fetch_add(1, Ordering::AcqRel) + 1;
        let (reply, rx) = bounded(1);
        self.tx
            .send(Request { id, input, reply })
            .map_err(|_| "SoundFont loader stopped")?;
        Ok(rx)
    }

    pub fn load(&self, input: FontInput) -> Result<(), String> {
        self.submit(input)?
            .recv()
            .map_err(|_| "Audio output stopped before SoundFont handoff")?
    }
}

fn prepare(input: FontInput, sample_rate: u32) -> Result<FontStack, String> {
    let fonts = match input {
        FontInput::Fonts(fonts) => fonts,
        FontInput::Paths(paths) => paths
            .into_iter()
            .map(|path| {
                let mut file =
                    std::fs::File::open(&path).map_err(|e| format!("{}: {e}", path.display()))?;
                SoundFont::new(&mut file)
                    .map(Arc::new)
                    .map_err(|e| format!("{}: {e}", path.display()))
            })
            .collect::<Result<Vec<_>, _>>()?,
    };
    FontStack::new(fonts, &SynthesizerSettings::new(sample_rate as i32))
}
