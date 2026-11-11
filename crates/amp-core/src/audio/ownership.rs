use std::collections::{HashMap, HashSet};

/// Synth voices are shared by song, physical MIDI and local preview handles.
/// Ownership is a lease (the caller pairs overlapping attacks itself), not a
/// count of NoteOn calls: song playback may retrigger a shared voice.
#[derive(Default)]
pub(super) struct AudioOwnership {
    pub notes: HashMap<(u8, u8), HashSet<u8>>,
    pedals: HashMap<(u8, u8, u8), u8>,
}

impl AudioOwnership {
    pub fn on(&mut self, source: u8, channel: u8, note: u8) {
        self.notes
            .entry((channel, note))
            .or_default()
            .insert(source);
    }
    pub fn off(&mut self, source: u8, channel: u8, note: u8) -> bool {
        let Some(owners) = self.notes.get_mut(&(channel, note)) else {
            return false;
        };
        if !owners.remove(&source) || !owners.is_empty() {
            return false;
        }
        self.notes.remove(&(channel, note));
        true
    }
    pub fn pedal(&mut self, source: u8, channel: u8, cc: u8, value: u8) -> u8 {
        self.pedals.insert((source, channel, cc), value);
        self.pedals
            .iter()
            .filter(|((_, ch, c), _)| *ch == channel && *c == cc)
            .map(|(_, &v)| v)
            .max()
            .unwrap_or(0)
    }
    pub fn has_other(&self, source: u8) -> bool {
        self.notes
            .values()
            .any(|owners| owners.iter().any(|&s| s != source))
            || self
                .pedals
                .iter()
                .any(|((s, _, _), &v)| *s != source && v > 0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn song_release_cannot_release_live_or_preview_voice() {
        let mut o = AudioOwnership::default();
        for source in 0..3 {
            o.on(source, 0, 60);
        }
        assert!(!o.off(0, 0, 60));
        assert!(!o.off(2, 0, 60));
        assert!(o.off(1, 0, 60));
        assert!(!o.off(1, 0, 60));
    }
    #[test]
    fn pedals_combine_sources_without_cross_releasing_channels() {
        let mut o = AudioOwnership::default();
        assert_eq!(o.pedal(0, 0, 64, 127), 127);
        assert_eq!(o.pedal(1, 0, 64, 100), 127);
        assert_eq!(o.pedal(0, 0, 64, 0), 100);
        assert_eq!(o.pedal(1, 1, 64, 0), 0);
        assert_eq!(o.pedal(1, 0, 64, 0), 0);
    }
}
