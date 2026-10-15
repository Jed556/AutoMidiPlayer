/// Represents a note on or note off event timed in microseconds from the start of the song.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TimedNoteEvent {
    /// Absolute timestamp in microseconds
    pub time_us: u64,
    /// MIDI note pitch (0..127). For example, 60 = Middle C (C4)
    pub note: u8,
    /// Velocity of the note (0..127)
    pub velocity: u8,
    /// True for Note On, False for Note Off
    pub is_note_on: bool,
    /// MIDI channel (0..15)
    pub channel: u8,
    /// Track index in the MIDI file
    pub track_index: usize,
}

impl PartialOrd for TimedNoteEvent {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for TimedNoteEvent {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        self.time_us.cmp(&other.time_us)
            .then_with(|| self.is_note_on.cmp(&other.is_note_on))
            .then_with(|| self.note.cmp(&other.note))
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ControlEventKind {
    ControlChange { controller: u8, value: u8 },
    ProgramChange { program: u8 },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TimedControlEvent {
    pub time_us: u64,
    pub channel: u8,
    pub kind: ControlEventKind,
}

impl PartialOrd for TimedControlEvent {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for TimedControlEvent {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        self.time_us.cmp(&other.time_us)
    }
}

