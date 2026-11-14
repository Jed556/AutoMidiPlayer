use std::collections::VecDeque;

const LOOKAHEAD: usize = 256; // 5.8 ms at the synth's 44.1 kHz sample rate.

/// Bounded automatic gain for the complete SoundFont mix, before user volume.
/// Stereo channels share gain and peak protection so pan positions stay intact.
pub(super) struct VolumeNormalizer {
    enabled: bool,
    blend: f32,
    blend_step: f32,
    gain: f32,
    limiter: PeakLimiter,
    gain_attack: f32,
    gain_release: f32,
    level_power: f64,
    level_release: f64,
}

impl VolumeNormalizer {
    const INITIAL_GAIN: f32 = 1.0;

    // Code-only tuning, measured before Master Volume (linear stereo RMS).
    // Raise QUIET_THRESHOLD_RMS for more lift; raise LOUD_THRESHOLD_RMS to
    // preserve louder passages. Levels between the two keep their original gain.
    const QUIET_THRESHOLD_RMS: f32 = 0.125; // About -18 dBFS.
    const LOUD_THRESHOLD_RMS: f32 = 0.20; // About -14 dBFS.
    const MAX_GAIN: f32 = 32.0; // +30 dB; bounds lift of unusually quiet fonts.
    const GAIN_RISE_SECONDS: f32 = 0.25; // Lift short notes without a long fade-in.
    const SILENCE_RMS: f32 = 0.000001; // Ignore numerical noise, not quiet held notes.
    const CEILING: f32 = 0.95;

    pub fn new(sample_rate: u32, enabled: bool) -> Self {
        let coefficient = |seconds: f32| 1.0 - (-1.0 / (seconds * sample_rate as f32)).exp();
        Self {
            enabled,
            blend: if enabled { 1.0 } else { 0.0 },
            blend_step: 1.0 / (0.02 * sample_rate as f32),
            gain: Self::INITIAL_GAIN,
            limiter: PeakLimiter::new(coefficient(0.1)),
            gain_attack: coefficient(0.005),
            gain_release: coefficient(Self::GAIN_RISE_SECONDS),
            level_power: 0.0,
            level_release: (-1.0 / (0.2 * sample_rate as f64)).exp(),
        }
    }

    pub fn set_enabled(&mut self, enabled: bool) {
        self.enabled = enabled;
    }

    pub fn reset(&mut self) {
        self.gain = Self::INITIAL_GAIN;
        self.level_power = 0.0;
        self.limiter.reset();
    }

    /// No render-time allocations. Both wet and dry signals share the short
    /// lookahead delay, keeping toggle crossfades aligned and bypass sample-exact.
    pub fn process(
        &mut self,
        left: &[f32],
        right: &[f32],
        master_volume: f32,
        notes_held: bool,
        out: &mut Vec<f32>,
    ) {
        debug_assert_eq!(left.len(), right.len());
        if left.is_empty() {
            return;
        }
        let energy: f64 = left
            .iter()
            .chain(right.iter())
            .map(|v| (*v as f64).powi(2))
            .sum();
        let power = energy / (2 * left.len()) as f64;
        // Catch a dense onset immediately, but hold its measured level briefly
        // so waveform troughs and gaps cannot keep turning the gain back up.
        let decay = self.level_release.powf(left.len() as f64);
        self.level_power = power.max(power + decay * (self.level_power - power));
        let rms = self.level_power.sqrt() as f32;
        let mut target = self.gain;
        if rms >= Self::SILENCE_RMS {
            // Summed voices can need far more than 12 dB of attenuation.
            // Only upward gain is capped; peak protection handles transients.
            target = if rms < Self::QUIET_THRESHOLD_RMS {
                (Self::QUIET_THRESHOLD_RMS / rms).min(Self::MAX_GAIN)
            } else if rms > Self::LOUD_THRESHOLD_RMS {
                Self::LOUD_THRESHOLD_RMS / rms
            } else {
                Self::INITIAL_GAIN
            };
            // Let release envelopes decay naturally instead of turning up their tails.
            if !notes_held {
                target = target.min(self.gain);
            }
        }
        let smoothing = if target < self.gain {
            self.gain_attack
        } else {
            self.gain_release
        };
        for (&l, &r) in left.iter().zip(right.iter()) {
            self.gain += smoothing * (target - self.gain);
            let l = l * master_volume;
            let r = r * master_volume;
            let frame = self.limiter.process([
                l * self.gain,
                r * self.gain,
                l.clamp(-1.0, 1.0),
                r.clamp(-1.0, 1.0),
            ]);
            // Crossfade the complete protected signal, including limiter gain,
            // so bypass cannot jump when the limiter is still recovering.
            self.blend = if self.enabled {
                (self.blend + self.blend_step).min(1.0)
            } else {
                (self.blend - self.blend_step).max(0.0)
            };
            out.push(frame[2] * (1.0 - self.blend) + frame[0] * self.blend);
            out.push(frame[3] * (1.0 - self.blend) + frame[1] * self.blend);
        }
    }
}

/// Sliding peak hold followed by a moving-average gain envelope. Every delayed
/// sample's peak appears in all the windows averaged for its gain, so smoothing
/// cannot let a transient exceed the ceiling. A slow release avoids pumping.
struct PeakLimiter {
    frames: [[f32; 4]; LOOKAHEAD],
    gains: [f32; LOOKAHEAD],
    gain_sum: f64,
    envelope: f32,
    release: f32,
    position: usize,
    index: u64,
    peaks: VecDeque<(u64, f32)>,
}

impl PeakLimiter {
    fn new(release: f32) -> Self {
        Self {
            frames: [[0.0; 4]; LOOKAHEAD],
            gains: [1.0; LOOKAHEAD],
            gain_sum: LOOKAHEAD as f64,
            envelope: 1.0,
            release,
            position: 0,
            index: 0,
            peaks: VecDeque::with_capacity(LOOKAHEAD + 1),
        }
    }

    fn reset(&mut self) {
        self.frames.fill([0.0; 4]);
        self.gains.fill(1.0);
        self.gain_sum = LOOKAHEAD as f64;
        self.envelope = 1.0;
        self.position = 0;
        self.index = 0;
        self.peaks.clear();
    }

    fn process(&mut self, incoming: [f32; 4]) -> [f32; 4] {
        let oldest = self.index.saturating_sub(LOOKAHEAD as u64);
        while self.peaks.front().is_some_and(|&(index, _)| index < oldest) {
            self.peaks.pop_front();
        }
        let peak = incoming[0].abs().max(incoming[1].abs());
        while self.peaks.back().is_some_and(|&(_, value)| value <= peak) {
            self.peaks.pop_back();
        }
        self.peaks.push_back((self.index, peak));
        let peak = self.peaks.front().unwrap().1;
        let required = if peak > VolumeNormalizer::CEILING {
            VolumeNormalizer::CEILING / peak
        } else {
            1.0
        };
        self.envelope = required.min(self.envelope + self.release * (1.0 - self.envelope));
        self.gain_sum += self.envelope as f64 - self.gains[self.position] as f64;
        self.gains[self.position] = self.envelope;
        let gain = (self.gain_sum / LOOKAHEAD as f64).clamp(0.0, 1.0) as f32;
        let mut output = self.frames[self.position];
        self.frames[self.position] = incoming;
        self.position = (self.position + 1) % LOOKAHEAD;
        self.index += 1;
        output[0] *= gain;
        output[1] *= gain;
        output
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn block(normalizer: &mut VolumeNormalizer, level: f32, volume: f32, held: bool) -> Vec<f32> {
        let mut left = [level; 256];
        let mut right = [level * 0.5; 256];
        let mut out = Vec::with_capacity(512);
        normalizer.process(&mut left, &mut right, volume, held, &mut out);
        out
    }

    #[test]
    fn quiet_audio_is_lifted_with_bounded_gain_and_stereo_balance() {
        let mut normalizer = VolumeNormalizer::new(44100, true);
        let mut out = Vec::new();
        for _ in 0..600 {
            out = block(&mut normalizer, 0.02, 1.0, true);
        }
        assert!((rms(&out) - 0.125).abs() < 0.001);
        for frame in out.chunks_exact(2) {
            assert_eq!(frame[0] * 0.5, frame[1]);
        }
        assert!(normalizer.gain <= VolumeNormalizer::MAX_GAIN);
    }

    fn rms(samples: &[f32]) -> f64 {
        (samples.iter().map(|v| (*v as f64).powi(2)).sum::<f64>() / samples.len() as f64).sqrt()
    }

    #[test]
    fn quiet_and_dense_mixes_stay_in_range_and_master_remains_a_multiplier() {
        let mut levels = Vec::new();
        for volume in [0.5, 1.0, 2.0] {
            let mut single = VolumeNormalizer::new(44100, true);
            let mut stacked = VolumeNormalizer::new(44100, true);
            let mut single_out = Vec::new();
            let mut stacked_out = Vec::new();
            for _ in 0..600 {
                single_out = block(&mut single, 0.1, volume, true);
                // Identical voices at the same velocity add coherently.
                stacked_out = block(&mut stacked, 0.1 * 64.0, volume, true);
            }
            let solo = rms(&single_out);
            let dense = rms(&stacked_out);
            assert!(
                (solo / volume as f64 - 0.125).abs() < 0.002,
                "quiet audio should reach the lower comfortable level: {solo}"
            );
            assert!(
                (dense / volume as f64 - 0.20).abs() < 0.002,
                "64 equal-velocity voices should stay at the upper comfortable level: {dense}"
            );
            levels.push((solo, dense));
        }
        for pair in levels.windows(2) {
            assert!((pair[1].0 / pair[0].0 - 2.0).abs() < 0.001);
            assert!((pair[1].1 / pair[0].1 - 2.0).abs() < 0.001);
        }
    }

    #[test]
    fn ordinary_levels_keep_unity_gain_including_at_double_master_volume() {
        for volume in [0.5, 1.0, 2.0] {
            for level in [0.16, 0.20, 0.24] {
                let mut normalizer = VolumeNormalizer::new(44100, true);
                for _ in 0..30 {
                    block(&mut normalizer, level, volume, true);
                }
                let output = block(&mut normalizer, level, volume, true);
                assert_eq!(
                    normalizer.gain, 1.0,
                    "ordinary levels must not be turned down"
                );
                assert_eq!(output, [level * volume, level * 0.5 * volume].repeat(256));
            }
        }
    }

    #[test]
    fn quiet_short_notes_get_lift_before_they_end_without_inflating_their_tails() {
        let mut normalizer = VolumeNormalizer::new(44100, true);
        block(&mut normalizer, 0.01, 1.0, true); // Fill lookahead.
        let mut attack = Vec::new();
        for _ in 0..17 {
            attack.extend(block(&mut normalizer, 0.01, 1.0, true));
        }
        assert!(
            rms(&attack) > 0.02,
            "a quiet 100 ms note needs useful lift during its attack"
        );
        let gain_at_release = normalizer.gain;
        let mut tail = Vec::new();
        for _ in 0..30 {
            tail = block(&mut normalizer, 0.001, 1.0, false);
        }
        assert_eq!(
            normalizer.gain, gain_at_release,
            "released notes must decay instead of getting turned up"
        );
        assert!(rms(&tail) < rms(&attack) * 0.25);
    }

    #[test]
    fn sudden_dense_mix_is_reduced_in_average_level_as_well_as_peak_level() {
        let mut normalizer = VolumeNormalizer::new(44100, true);
        for _ in 0..300 {
            block(&mut normalizer, 0.1, 1.0, true);
        }
        let mut onset = Vec::new();
        for _ in 0..18 {
            onset.extend(block(&mut normalizer, 3.2, 1.0, true));
        }
        assert!(
            rms(&onset) < 0.30,
            "first 100 ms of stacked audio must be leveled, not just peak-limited"
        );
        for _ in 0..100 {
            block(&mut normalizer, 3.2, 1.0, true);
        }
        assert!(rms(&block(&mut normalizer, 3.2, 1.0, true)) < 0.205);
        let reduced_gain = normalizer.gain;
        let sparse = block(&mut normalizer, 0.1, 1.0, true);
        assert!(
            rms(&sparse) < 0.205,
            "removing the stack must not cause an upward surge"
        );
        assert!(
            normalizer.gain < reduced_gain * 1.1,
            "gain must recover gradually"
        );
    }

    #[test]
    fn very_quiet_held_notes_are_lifted_without_lifting_released_tails() {
        let mut held = VolumeNormalizer::new(44100, true);
        let mut released = VolumeNormalizer::new(44100, true);
        for _ in 0..800 {
            block(&mut held, 0.0002, 1.0, true);
            block(&mut released, 0.0002, 1.0, false);
        }
        assert!(
            rms(&block(&mut held, 0.0002, 1.0, true))
                > rms(&block(&mut released, 0.0002, 1.0, false)) * 3.5
        );
        assert_eq!(released.gain, VolumeNormalizer::INITIAL_GAIN);
    }

    #[test]
    fn sudden_stacked_peaks_and_double_volume_stay_below_ceiling() {
        let mut normalizer = VolumeNormalizer::new(44100, true);
        for _ in 0..400 {
            block(&mut normalizer, 0.02, 1.0, true);
        }
        let out = block(&mut normalizer, -3.0, 2.0, true);
        assert!(out.iter().all(|v| v.is_finite() && v.abs() <= 0.950001));
        let out = block(&mut normalizer, -3.0, 2.0, true);
        assert!(out.iter().all(|v| v.is_finite() && v.abs() <= 0.950001));
        assert!(out.iter().any(|v| v.abs() > 0.9));
    }

    #[test]
    fn silence_release_tails_and_mute_do_not_drive_gain_up() {
        let mut normalizer = VolumeNormalizer::new(44100, true);
        for _ in 0..1000 {
            assert!(block(&mut normalizer, 0.0, 1.0, false)
                .iter()
                .all(|v| *v == 0.0));
        }
        assert_eq!(normalizer.gain, VolumeNormalizer::INITIAL_GAIN);
        for _ in 0..200 {
            block(&mut normalizer, 0.01, 1.0, false);
        }
        assert_eq!(normalizer.gain, VolumeNormalizer::INITIAL_GAIN);
        block(&mut normalizer, 0.02, 0.0, true);
        assert!(block(&mut normalizer, 0.02, 0.0, true)
            .iter()
            .all(|v| *v == 0.0));
    }

    #[test]
    fn master_volume_still_scales_output_and_bypass_restores_original_samples() {
        let mut full = VolumeNormalizer::new(44100, true);
        let mut half = VolumeNormalizer::new(44100, true);
        block(&mut full, 0.02, 1.0, true);
        block(&mut half, 0.02, 0.5, true);
        for (a, b) in block(&mut full, 0.02, 1.0, true)
            .iter()
            .zip(block(&mut half, 0.02, 0.5, true))
        {
            assert_eq!(*a * 0.5, b);
        }
        let previous = block(&mut full, 0.02, 1.0, true)[510];
        full.set_enabled(false);
        let first = block(&mut full, 0.02, 1.0, true);
        assert!(
            (first[0] - previous).abs() < 0.001,
            "toggle must ramp, not jump"
        );
        for _ in 0..4 {
            block(&mut full, 0.02, 1.0, true);
        }
        block(&mut full, 0.02, 0.5, true);
        assert_eq!(block(&mut full, 0.02, 0.5, true), [0.01, 0.005].repeat(256));
        block(&mut full, 0.02, 1.0, true);
        full.set_enabled(true);
        assert!(block(&mut full, 0.02, 1.0, true)[0] > 0.02);
    }

    #[test]
    fn disabling_under_peak_reduction_crossfades_the_limiter_too() {
        let mut normalizer = VolumeNormalizer::new(44100, true);
        block(&mut normalizer, 3.0, 1.0, true);
        block(&mut normalizer, 0.2, 1.0, true);
        block(&mut normalizer, 0.2, 1.0, true);
        normalizer.set_enabled(false);
        let mut previous = block(&mut normalizer, 0.2, 1.0, true)[510];
        for _ in 0..8 {
            for frame in block(&mut normalizer, 0.2, 1.0, true).chunks_exact(2) {
                assert!(
                    (frame[0] - previous).abs() < 0.01,
                    "bypass must not jump out of limiting"
                );
                previous = frame[0];
            }
        }
        assert_eq!(previous, 0.2);
    }

    #[test]
    fn lookahead_limits_irregular_transients_and_smooths_gain_between_blocks() {
        let mut limiter = PeakLimiter::new(1.0 - (-1.0_f32 / 4410.0).exp());
        let mut previous = 0.2;
        for _ in 0..LOOKAHEAD * 2 {
            limiter.process([0.2, 0.1, 0.2, 0.1]);
        }
        // A late-block peak should produce a smooth gain ramp on the preceding bed.
        for i in 0..LOOKAHEAD * 2 {
            let peak = if i == LOOKAHEAD - 1 { 8.0 } else { 0.2 };
            let frame = limiter.process([peak, peak * 0.5, peak, peak * 0.5]);
            assert!(frame[0].abs() <= 0.950001);
            if i < LOOKAHEAD * 2 - 1 {
                assert!(
                    (frame[0] - previous).abs() < 0.002,
                    "gain must ramp before the peak"
                );
            }
            previous = frame[0];
        }
        limiter.reset();
        let mut seed = 17_u32;
        for _ in 0..44100 * 4 {
            seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
            let sample = (seed as f64 / u32::MAX as f64 * 16.0 - 8.0) as f32;
            let frame = limiter.process([sample, -sample * 0.5, 0.0, 0.0]);
            assert!(frame[0].abs() <= 0.950001 && frame[1].abs() <= 0.950001);
            assert_eq!(frame[0] * -0.5, frame[1]);
        }
        limiter.reset();
        for _ in 0..LOOKAHEAD {
            assert_eq!(limiter.process([0.0; 4]), [0.0; 4]);
        }
    }
}
