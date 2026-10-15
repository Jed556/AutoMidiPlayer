/// An entry in the tempo map denoting when tempo changes occur.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct TempoChange {
    pub tick: u64,
    pub us_at_tick: u64,
    /// Microseconds per quarter note (e.g. 500,000 for 120 BPM)
    pub us_per_quarter_note: u32,
}

#[derive(Debug, Clone)]
pub struct TempoMap {
    pub ppqn: u16,
    changes: Vec<TempoChange>,
}

impl TempoMap {
    pub fn new(ppqn: u16, raw_changes: &[(u64, u32)]) -> Self {
        let ppqn_f = ppqn.max(1) as f64;
        let mut sorted = raw_changes.to_vec();
        sorted.sort_by_key(|&(tick, _)| tick);

        let mut changes: Vec<TempoChange> = Vec::new();
        let mut cur_tick = 0u64;
        let mut cur_us = 0.0f64;
        let mut cur_tempo = 500_000u32; // Default MIDI tempo: 120 BPM

        if sorted.is_empty() || sorted[0].0 > 0 {
            changes.push(TempoChange {
                tick: 0,
                us_at_tick: 0,
                us_per_quarter_note: cur_tempo,
            });
        }

        for (tick, tempo) in sorted {
            if tick > cur_tick {
                let delta_ticks = tick - cur_tick;
                cur_us += (delta_ticks as f64 * cur_tempo as f64) / ppqn_f;
                cur_tick = tick;
            }
            cur_tempo = tempo;

            // If a change already exists at this exact tick, replace it
            if let Some(last) = changes.last_mut() {
                if last.tick == tick {
                    last.us_per_quarter_note = tempo;
                    continue;
                }
            }

            changes.push(TempoChange {
                tick,
                us_at_tick: cur_us.round() as u64,
                us_per_quarter_note: tempo,
            });
        }

        Self { ppqn, changes }
    }

    /// Converts a MIDI tick value to microseconds.
    pub fn tick_to_us(&self, tick: u64) -> u64 {
        if self.changes.is_empty() {
            return 0;
        }

        let idx = match self.changes.binary_search_by_key(&tick, |c| c.tick) {
            Ok(exact) => exact,
            Err(insertion) => insertion.saturating_sub(1),
        };

        let change = &self.changes[idx];
        let delta_ticks = tick.saturating_sub(change.tick);
        let us_offset = (delta_ticks as f64 * change.us_per_quarter_note as f64) / (self.ppqn as f64);
        change.us_at_tick + (us_offset.round() as u64)
    }

    /// Converts microseconds to MIDI ticks.
    pub fn us_to_tick(&self, us: u64) -> u64 {
        if self.changes.is_empty() {
            return 0;
        }

        let idx = match self.changes.binary_search_by_key(&us, |c| c.us_at_tick) {
            Ok(exact) => exact,
            Err(insertion) => insertion.saturating_sub(1),
        };

        let change = &self.changes[idx];
        let delta_us = us.saturating_sub(change.us_at_tick);
        let ticks_offset = (delta_us as f64 * self.ppqn as f64) / (change.us_per_quarter_note as f64);
        change.tick + (ticks_offset.round() as u64)
    }

    /// Returns the BPM active at the specified timestamp.
    pub fn bpm_at_us(&self, us: u64) -> f64 {
        if self.changes.is_empty() {
            return 120.0;
        }

        let idx = match self.changes.binary_search_by_key(&us, |c| c.us_at_tick) {
            Ok(exact) => exact,
            Err(insertion) => insertion.saturating_sub(1),
        };

        let tempo = self.changes[idx].us_per_quarter_note.max(1);
        60_000_000.0 / (tempo as f64)
    }

    /// Returns the initial BPM at the start of the song.
    pub fn initial_bpm(&self) -> f64 {
        self.bpm_at_us(0)
    }
}
