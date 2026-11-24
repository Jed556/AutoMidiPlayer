use std::collections::{HashMap, HashSet};

use super::model::*;
use super::{GP_PPQN, MAX_EXPANDED_BARS, MAX_MASTER_BARS, MAX_TOTAL_NOTES};
use crate::midi::detect::{calculate_key_signature_offset, RawKeySignature};
use crate::midi::events::{ControlEventKind, TimedControlEvent, TimedNoteEvent};
use crate::midi::parser::{get_gm_instrument_name, ParsedMidi, TrackInfo};
use crate::midi::tempo_map::TempoMap;

#[derive(Debug, Clone)]
struct RepeatGroup {
    opening: usize,
    closings: Vec<usize>,
    is_closed: bool,
}

fn build_repeat_groups(master_bars: &[GpMasterBar]) -> (Vec<RepeatGroup>, Vec<usize>) {
    let mut groups: Vec<RepeatGroup> = Vec::new();
    let mut bar_to_group = Vec::with_capacity(master_bars.len());

    for (idx, mb) in master_bars.iter().enumerate() {
        let need_new = groups.is_empty()
            || mb.is_repeat_start
            || (groups.last().unwrap().is_closed && mb.alternate_endings == 0);
        if need_new {
            let mut g = RepeatGroup {
                opening: idx,
                closings: Vec::new(),
                is_closed: false,
            };
            if groups.is_empty() && mb.is_repeat_start {
                g.opening = idx;
            }
            groups.push(g);
        }
        let g_idx = groups.len() - 1;
        if mb.repeat_count > 1 {
            groups[g_idx].closings.push(idx);
            groups[g_idx].is_closed = true;
        }
        bar_to_group.push(g_idx);
    }

    (groups, bar_to_group)
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum ControllerMode {
    Normal,
    Jumped,
    JumpedAlCoda,
    JumpedAlDoubleCoda,
    JumpedAlFine,
}

struct ActiveRepeat {
    group_idx: usize,
    iterations: Vec<i32>,
    closing_idx: usize,
}

fn expand_master_bar_order(master_bars: &[GpMasterBar]) -> Result<Vec<usize>, String> {
    if master_bars.is_empty() {
        return Ok(Vec::new());
    }
    if master_bars.len() > MAX_MASTER_BARS {
        return Err("Guitar Pro score exceeds 25,000 measures limit".into());
    }
    let has_jumps = master_bars
        .iter()
        .any(|mb| mb.repeat_count > 1 || mb.alternate_endings != 0 || !mb.directions.is_empty());
    if !has_jumps {
        return Ok((0..master_bars.len()).collect());
    }

    let (groups, bar_to_group) = build_repeat_groups(master_bars);
    let mut order = Vec::with_capacity(master_bars.len());
    let mut repeat_stack: Vec<ActiveRepeat> = Vec::new();
    let mut groups_on_stack: HashSet<usize> = HashSet::new();
    let mut prev_alt_endings: u32 = 0;
    let mut mode = ControllerMode::Normal;
    let mut index = 0usize;
    let mut steps = 0usize;

    while index < master_bars.len() {
        steps += 1;
        if steps > MAX_EXPANDED_BARS * 4 || order.len() >= MAX_EXPANDED_BARS {
            return Err("Expanded repeat playback order exceeds 25,000 measures limit".into());
        }

        let mb = &master_bars[index];
        let should_play = if mode == ControllerMode::Normal {
            let g_idx = bar_to_group[index];
            let group = &groups[g_idx];
            let mut alt = mb.alternate_endings;
            if alt == 0 {
                alt = prev_alt_endings;
            }
            if index == group.opening && group.is_closed && !groups_on_stack.contains(&g_idx) {
                repeat_stack.push(ActiveRepeat {
                    group_idx: g_idx,
                    iterations: vec![0; group.closings.len()],
                    closing_idx: 0,
                });
                groups_on_stack.insert(g_idx);
                prev_alt_endings = 0;
                alt = mb.alternate_endings;
            }

            if repeat_stack.is_empty() || alt == 0 {
                true
            } else {
                let rep = repeat_stack.last().unwrap();
                let iter_num = rep.iterations[rep.closing_idx];
                prev_alt_endings = alt;
                if iter_num < 0 || iter_num >= 32 {
                    true
                } else {
                    (alt & (1u32 << iter_num)) != 0
                }
            }
        } else {
            true
        };

        if should_play {
            order.push(index);
        }

        // Advance index via directions first, then normal repeats.
        if let Some(next_idx) = advance_with_directions(
            master_bars,
            index,
            &mut mode,
            &mut repeat_stack,
            &mut groups_on_stack,
            &mut prev_alt_endings,
        ) {
            index = next_idx;
            continue;
        }

        let repeat_target = (mb.repeat_count - 1).max(0);
        if !repeat_stack.is_empty() && repeat_target > 0 {
            let closing_idx = repeat_stack.last().unwrap().closing_idx;
            let iter_now = repeat_stack.last().unwrap().iterations[closing_idx];
            let g_idx = repeat_stack.last().unwrap().group_idx;
            if iter_now < repeat_target {
                let rep = repeat_stack.last_mut().unwrap();
                let opening = groups[g_idx].opening;
                rep.iterations[closing_idx] += 1;
                for prev in 0..closing_idx {
                    rep.iterations[prev] = 0;
                }
                rep.closing_idx = 0;
                prev_alt_endings = 0;
                index = opening;
            } else if closing_idx + 1 < groups[g_idx].closings.len() {
                repeat_stack.last_mut().unwrap().closing_idx += 1;
                index += 1;
            } else {
                let popped = repeat_stack.pop().unwrap();
                groups_on_stack.remove(&popped.group_idx);
                index += 1;
            }
        } else {
            index += 1;
        }
    }

    Ok(order)
}

fn find_jump_target(
    master_bars: &[GpMasterBar],
    target: GpDirection,
    from: usize,
    backwards_first: bool,
) -> Option<usize> {
    let search_back = || (0..=from).rev().find(|&i| master_bars[i].directions.contains(&target));
    let search_fwd =
        || (from..master_bars.len()).find(|&i| master_bars[i].directions.contains(&target));
    if backwards_first {
        search_back().or_else(search_fwd)
    } else {
        search_fwd().or_else(search_back)
    }
}

fn advance_with_directions(
    master_bars: &[GpMasterBar],
    index: usize,
    mode: &mut ControllerMode,
    repeat_stack: &mut Vec<ActiveRepeat>,
    groups_on_stack: &mut HashSet<usize>,
    prev_alt_endings: &mut u32,
) -> Option<usize> {
    let dirs = &master_bars[index].directions;
    if *mode == ControllerMode::Normal && dirs.is_empty() {
        return None;
    }
    if dirs.is_empty() {
        return Some(index + 1);
    }

    let reset = |rs: &mut Vec<ActiveRepeat>, gs: &mut HashSet<usize>, pa: &mut u32| {
        rs.clear();
        gs.clear();
        *pa = 0;
    };

    match *mode {
        ControllerMode::Normal => {
            for (jump, next_mode) in [
                (GpDirection::JumpDaCapo, ControllerMode::Jumped),
                (GpDirection::JumpDaCapoAlCoda, ControllerMode::JumpedAlCoda),
                (
                    GpDirection::JumpDaCapoAlDoubleCoda,
                    ControllerMode::JumpedAlDoubleCoda,
                ),
                (GpDirection::JumpDaCapoAlFine, ControllerMode::JumpedAlFine),
            ] {
                if dirs.contains(&jump) {
                    *mode = next_mode;
                    reset(repeat_stack, groups_on_stack, prev_alt_endings);
                    return Some(0);
                }
            }
            for (jump, next_mode, target) in [
                (
                    GpDirection::JumpDalSegno,
                    ControllerMode::Jumped,
                    GpDirection::TargetSegno,
                ),
                (
                    GpDirection::JumpDalSegnoAlCoda,
                    ControllerMode::JumpedAlCoda,
                    GpDirection::TargetSegno,
                ),
                (
                    GpDirection::JumpDalSegnoAlDoubleCoda,
                    ControllerMode::JumpedAlDoubleCoda,
                    GpDirection::TargetSegno,
                ),
                (
                    GpDirection::JumpDalSegnoAlFine,
                    ControllerMode::JumpedAlFine,
                    GpDirection::TargetSegno,
                ),
                (
                    GpDirection::JumpDalSegnoSegno,
                    ControllerMode::Jumped,
                    GpDirection::TargetSegnoSegno,
                ),
                (
                    GpDirection::JumpDalSegnoSegnoAlCoda,
                    ControllerMode::JumpedAlCoda,
                    GpDirection::TargetSegnoSegno,
                ),
                (
                    GpDirection::JumpDalSegnoSegnoAlDoubleCoda,
                    ControllerMode::JumpedAlDoubleCoda,
                    GpDirection::TargetSegnoSegno,
                ),
                (
                    GpDirection::JumpDalSegnoSegnoAlFine,
                    ControllerMode::JumpedAlFine,
                    GpDirection::TargetSegnoSegno,
                ),
            ] {
                if dirs.contains(&jump) {
                    if let Some(target_idx) = find_jump_target(master_bars, target, index, true) {
                        *mode = next_mode;
                        reset(repeat_stack, groups_on_stack, prev_alt_endings);
                        return Some(target_idx);
                    }
                    return None;
                }
            }
            None
        }
        ControllerMode::Jumped => Some(index + 1),
        ControllerMode::JumpedAlCoda => {
            if dirs.contains(&GpDirection::JumpDaCoda) {
                if let Some(coda) =
                    find_jump_target(master_bars, GpDirection::TargetCoda, index, false)
                {
                    *mode = ControllerMode::Normal;
                    return Some(coda);
                }
            }
            Some(index + 1)
        }
        ControllerMode::JumpedAlDoubleCoda => {
            if dirs.contains(&GpDirection::JumpDaDoubleCoda) {
                if let Some(coda) =
                    find_jump_target(master_bars, GpDirection::TargetDoubleCoda, index, false)
                {
                    *mode = ControllerMode::Normal;
                    return Some(coda);
                }
            }
            Some(index + 1)
        }
        ControllerMode::JumpedAlFine => {
            if dirs.contains(&GpDirection::TargetFine) {
                Some(master_bars.len())
            } else {
                Some(index + 1)
            }
        }
    }
}

fn bar_length_beats(score: &GpScore, bar_idx: usize) -> Frac {
    let mb = &score.master_bars[bar_idx];
    let nominal = Frac::new(4 * i128::from(mb.num.max(1)), i128::from(mb.den.max(1)));
    if !mb.is_anacrusis && !mb.is_free_time {
        return nominal;
    }
    let mut max_voice = Frac::ZERO;
    for track in &score.tracks {
        for staff in &track.staves {
            if let Some(bar) = staff.bars.get(bar_idx) {
                for voice in &bar.voices {
                    let sum = voice
                        .beats
                        .iter()
                        .filter(|b| b.grace_type.is_none())
                        .fold(Frac::ZERO, |acc, b| acc.add(b.duration_beats()));
                    if sum.cmp_val(max_voice) == std::cmp::Ordering::Greater {
                        max_voice = sum;
                    }
                }
            }
        }
    }
    if max_voice > Frac::ZERO {
        max_voice
    } else {
        nominal
    }
}

struct PlacedBeat<'a> {
    start_beats: Frac,
    dur_beats: Frac,
    beat: &'a GpBeat,
}

fn layout_voice_beats<'a>(voice: &'a GpVoice, triplet_feel: GpTripletFeel) -> Vec<PlacedBeat<'a>> {
    let mut raw: Vec<PlacedBeat<'a>> = Vec::with_capacity(voice.beats.len());
    let mut cursor = Frac::ZERO;
    let grace_dur = Frac::new(1, 8); // 32nd note (1/8 quarter beat)

    for (idx, beat) in voice.beats.iter().enumerate() {
        if let Some(grace) = beat.grace_type {
            let start = match grace {
                GpGraceType::BeforeBeat => cursor.sub(grace_dur),
                GpGraceType::OnBeat => cursor,
            };
            raw.push(PlacedBeat {
                start_beats: start,
                dur_beats: grace_dur,
                beat,
            });
            continue;
        }
        let mut dur = beat.duration_beats();
        let mut start = cursor;
        // If previous beat was an OnBeat grace note right at cursor, start slightly after it.
        if idx > 0 && matches!(voice.beats[idx - 1].grace_type, Some(GpGraceType::OnBeat)) {
            if dur.cmp_val(grace_dur) == std::cmp::Ordering::Greater {
                start = start.add(grace_dur);
                dur = dur.sub(grace_dur);
            }
        }
        raw.push(PlacedBeat {
            start_beats: start,
            dur_beats: dur,
            beat,
        });
        cursor = cursor.add(beat.duration_beats());
    }

    // Apply TripletFeel swing timing on non-grace beat pairs where appropriate.
    let (grid, first_scale, second_scale) = match triplet_feel {
        GpTripletFeel::Triplet8th => (Frac::new(1, 2), Frac::new(4, 3), Frac::new(2, 3)),
        GpTripletFeel::Dotted8th => (Frac::new(1, 2), Frac::new(3, 2), Frac::new(1, 2)),
        GpTripletFeel::Scottish8th => (Frac::new(1, 2), Frac::new(1, 2), Frac::new(3, 2)),
        GpTripletFeel::Triplet16th => (Frac::new(1, 4), Frac::new(4, 3), Frac::new(2, 3)),
        GpTripletFeel::Dotted16th => (Frac::new(1, 4), Frac::new(3, 2), Frac::new(1, 2)),
        GpTripletFeel::Scottish16th => (Frac::new(1, 4), Frac::new(1, 2), Frac::new(3, 2)),
        GpTripletFeel::None => return raw,
    };

    let main_indices: Vec<usize> = raw
        .iter()
        .enumerate()
        .filter(|(_, p)| p.beat.grace_type.is_none())
        .map(|(i, _)| i)
        .collect();
    for pair in main_indices.windows(2) {
        let (a, b) = (pair[0], pair[1]);
        if raw[a].dur_beats == grid && raw[b].dur_beats == grid {
            // Check whether `raw[a].start_beats` falls on an even multiple of `grid * 2`.
            let step2 = grid.mul(Frac::new(2, 1));
            let pos_in_steps = Frac::new(raw[a].start_beats.num * step2.den, raw[a].start_beats.den * step2.num);
            if pos_in_steps.den == 1 {
                let new_a_dur = grid.mul(first_scale);
                let new_b_dur = grid.mul(second_scale);
                raw[b].start_beats = raw[a].start_beats.add(new_a_dur);
                raw[a].dur_beats = new_a_dur;
                raw[b].dur_beats = new_b_dur;
            }
        }
    }

    raw
}

fn beats_to_ticks(beats: Frac) -> i64 {
    let val = (beats.num as f64) * f64::from(GP_PPQN) / (beats.den as f64);
    val.round() as i64
}

fn normalize_chord_name(raw: &str) -> Option<String> {
    let cleaned = raw.trim().replace('♯', "#").replace('♭', "b");
    if cleaned.is_empty() {
        None
    } else {
        Some(cleaned)
    }
}

fn resolve_percussion_midi(track: &GpTrack, art_val: i32) -> u8 {
    if art_val >= 0 {
        if let Some(a) = track.percussion_articulations.get(art_val as usize) {
            if (0..=127).contains(&a.output_midi) && a.output_midi > 0 {
                return a.output_midi as u8;
            }
        }
        if let Some(a) = track
            .percussion_articulations
            .iter()
            .find(|a| a.id == art_val)
        {
            if (0..=127).contains(&a.output_midi) && a.output_midi > 0 {
                return a.output_midi as u8;
            }
        }
    }
    match art_val {
        91 => 38,
        92 => 46,
        93 | 94 | 126 | 127 => 51,
        95 => 55,
        96 => 52,
        97 => 49,
        98 => 57,
        99 | 100 | 102 | 103 => 56,
        104..=110 => 62,
        111..=113 => 54,
        114 => 36,
        115 | 116 => 49,
        117 => 69,
        118..=120 => 70,
        122 => 82,
        123 => 84,
        v if (27..=87).contains(&v) => v as u8,
        _ => 38,
    }
}

#[derive(Clone)]
struct PendingNote {
    start_tick: u64,
    end_tick: u64,
    pitch: u8,
    velocity: u8,
    string_idx: Option<usize>,
    fret: Option<i32>,
    bends: Vec<GpBendPoint>,
}

fn apply_discrete_bends(notes: &mut Vec<PendingNote>, tempo_map: &TempoMap) {
    // Match DIEtype's _bend_attacks: convert single-note bends >= 1/2 beat with even
    // quarter-tone peaks into discrete fret steps when enough NTE timing gap exists.
    let mut start_counts: HashMap<u64, usize> = HashMap::new();
    for n in notes.iter() {
        *start_counts.entry(n.start_tick).or_insert(0) += 1;
    }
    let mut distinct_starts: Vec<u64> = start_counts.keys().copied().collect();
    distinct_starts.sort_unstable();

    let fret_page = |fret: i32| -> i32 {
        if fret <= 0 {
            4
        } else {
            (fret - 1) / 6
        }
    };

    let mut additions: Vec<PendingNote> = Vec::new();
    for note in notes.iter_mut() {
        if note.bends.len() < 2 || note.string_idx.is_none() {
            continue;
        }
        let Some(fret) = note.fret else {
            continue;
        };
        if start_counts.get(&note.start_tick).copied().unwrap_or(0) != 1 {
            continue;
        }
        let dur = note.end_tick.saturating_sub(note.start_tick);
        if dur < u64::from(GP_PPQN) / 2 {
            continue;
        }
        let Some(first) = note.bends.first() else {
            continue;
        };
        let Some((peak_at, peak)) = note
            .bends
            .iter()
            .max_by_key(|p| p.value)
            .map(|p| (p.offset, p.value))
        else {
            continue;
        };
        if first.value != 0 || peak < 2 || peak % 2 != 0 {
            continue;
        }
        let bend_semi = i32::from(peak / 2);
        if fret + bend_semi > 24 || i32::from(note.pitch) + bend_semi > 127 {
            continue;
        }
        let last_val = note.bends.last().unwrap().value;
        let peak_ratio = (f64::from(peak_at) / 60.0).min(0.75);
        let peak_tick = note.start_tick + ((dur as f64) * peak_ratio).round() as u64;
        let mut steps: Vec<(u64, u8, i32)> =
            vec![(peak_tick, (i32::from(note.pitch) + bend_semi) as u8, fret + bend_semi)];
        if last_val == 0 {
            let rel_off = note
                .bends
                .iter()
                .find(|p| p.offset > peak_at && p.value == 0)
                .map_or(60, |p| p.offset);
            let rel_tick = note.start_tick + ((dur as f64) * (f64::from(rel_off) / 60.0)).round() as u64;
            steps.push((rel_tick, note.pitch, fret));
        } else if last_val != peak {
            continue;
        }

        let next_pos = distinct_starts.partition_point(|&s| s <= note.start_tick);
        let has_following = next_pos < distinct_starts.len();
        let following = distinct_starts.get(next_pos).copied().unwrap_or(note.end_tick);
        if has_following {
            let follow_us = tempo_map.tick_to_us(following);
            let last_step_us = tempo_map.tick_to_us(steps.last().unwrap().0);
            if follow_us.saturating_sub(last_step_us) < 120_000 {
                continue;
            }
        }

        let orig_end = note.end_tick;
        let mut boundaries = Vec::with_capacity(steps.len() + 2);
        boundaries.push(note.start_tick);
        for &(t, _, _) in &steps {
            boundaries.push(t);
        }
        boundaries.push(orig_end.min(following));
        let mut fretted = Vec::with_capacity(steps.len() + 1);
        fretted.push(fret);
        for &(_, _, f) in &steps {
            fretted.push(f);
        }

        let mut valid = true;
        for (i, w) in boundaries.windows(2).enumerate() {
            if w[1] <= w[0] {
                valid = false;
                break;
            }
            let gap_us = tempo_map.tick_to_us(w[1]).saturating_sub(tempo_map.tick_to_us(w[0]));
            let mut min_us = 65_000u64;
            if i + 1 < fretted.len() {
                let d = fret_page(fretted[i]).abs_diff(fret_page(fretted[i + 1]));
                let page_steps = d.min(5 - d) as u64;
                min_us = 75_000 + page_steps * 55_000;
            }
            if gap_us < min_us {
                valid = false;
                break;
            }
        }
        if !valid {
            continue;
        }

        note.end_tick = steps[0].0;
        for i in 0..steps.len() {
            let (step_start, step_pitch, _) = steps[i];
            let step_end = if i + 1 < steps.len() {
                steps[i + 1].0
            } else {
                orig_end
            };
            additions.push(PendingNote {
                start_tick: step_start,
                end_tick: step_end.max(step_start + 1),
                pitch: step_pitch,
                velocity: note.velocity,
                string_idx: note.string_idx,
                fret: Some(steps[i].2),
                bends: Vec::new(),
            });
        }
    }

    if !additions.is_empty() && notes.len() + additions.len() <= MAX_TOTAL_NOTES {
        notes.extend(additions);
    }
}

pub(super) fn gp_score_to_parsed_midi(
    score: &GpScore,
    fallback_title: Option<&str>,
) -> Result<ParsedMidi, String> {
    if score.master_bars.is_empty() {
        return Err("Guitar Pro score contains no measures".into());
    }
    let order = expand_master_bar_order(&score.master_bars)?;
    let bar_lengths: Vec<Frac> = (0..score.master_bars.len())
        .map(|i| bar_length_beats(score, i))
        .collect();

    // Compute start beat positions of every expanded bar occurrence and gather tempos + key signatures.
    let mut bar_start_beats: Vec<Frac> = Vec::with_capacity(order.len());
    let mut raw_tempo_changes: Vec<(u64, u32)> = Vec::new();
    let mut raw_key_sigs: Vec<(u64, i8, bool)> = Vec::new();
    let mut last_key_sig: Option<(i8, bool)> = None;
    let mut cursor_beats = Frac::ZERO;

    for &orig_bar_idx in &order {
        bar_start_beats.push(cursor_beats);
        let mb = &score.master_bars[orig_bar_idx];
        let bar_len = bar_lengths[orig_bar_idx];
        let bar_start_tick = beats_to_ticks(cursor_beats).max(0) as u64;

        if let Some(ks) = mb.key_sig {
            if last_key_sig != Some(ks) {
                raw_key_sigs.push((bar_start_tick, ks.0, ks.1));
                last_key_sig = Some(ks);
            }
        }
        for auto in &mb.tempo_automations {
            if auto.bpm.is_finite() && auto.bpm > 0.0 {
                let bpm = auto.bpm.clamp(10.0, 999.0);
                let ratio_frac = Frac::from_f64_approx(auto.ratio_position.clamp(0.0, 1.0));
                let pos_beats = cursor_beats.add(bar_len.mul(ratio_frac));
                let tick = beats_to_ticks(pos_beats).max(0) as u64;
                let us_per_q = (60_000_000.0 / bpm).round().max(1.0) as u32;
                raw_tempo_changes.push((tick, us_per_q));
            }
        }

        cursor_beats = cursor_beats.add(bar_len);
    }

    if raw_tempo_changes.is_empty() && score.tempo_bpm > 0.0 {
        let us_per_q = (60_000_000.0 / score.tempo_bpm.clamp(10.0, 999.0)).round().max(1.0) as u32;
        raw_tempo_changes.push((0, us_per_q));
    }

    // Collect mid-bar GP3-5 mix table tempo changes as well before constructing TempoMap.
    for track in &score.tracks {
        for staff in &track.staves {
            for (bar_pos_idx, &orig_bar_idx) in order.iter().enumerate() {
                let Some(bar) = staff.bars.get(orig_bar_idx) else {
                    continue;
                };
                let bar_start = bar_start_beats[bar_pos_idx];
                let mb = &score.master_bars[orig_bar_idx];
                for voice in &bar.voices {
                    for placed in layout_voice_beats(voice, mb.triplet_feel) {
                        if let Some(bpm) = placed.beat.tempo_change {
                            if bpm > 0.0 {
                                let tick = beats_to_ticks(bar_start.add(placed.start_beats)).max(0) as u64;
                                let us_per_q = (60_000_000.0 / bpm.clamp(10.0, 999.0)).round().max(1.0) as u32;
                                raw_tempo_changes.push((tick, us_per_q));
                            }
                        }
                    }
                }
            }
        }
    }

    let tempo_map = TempoMap::new(GP_PPQN, &raw_tempo_changes);
    let initial_bpm = tempo_map.initial_bpm();

    const MELODIC_CHANNELS: [u8; 15] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15];
    let mut next_melodic_ch = 0usize;

    let mut all_note_events: Vec<TimedNoteEvent> = Vec::new();
    let mut control_events: Vec<TimedControlEvent> = Vec::new();
    let mut track_infos: Vec<TrackInfo> = Vec::new();
    let mut brushes: Vec<GuitarProBrush> = Vec::new();
    let mut chord_marks: Vec<GuitarProChordMark> = Vec::new();
    let mut total_note_count = 0usize;

    for track in &score.tracks {
        let staff_count = track.staves.len();
        for (staff_idx, staff) in track.staves.iter().enumerate() {
            let out_track_idx = track_infos.len();
            let channel = if staff.is_percussion {
                9u8
            } else {
                let ch = MELODIC_CHANNELS[next_melodic_ch % MELODIC_CHANNELS.len()];
                next_melodic_ch += 1;
                ch
            };

            let track_name = if track.name.is_empty() {
                format!("Track {}", out_track_idx + 1)
            } else if staff_count > 1 {
                format!("{} · Staff {}", track.name, staff_idx + 1)
            } else {
                track.name.clone()
            };

            control_events.push(TimedControlEvent {
                time_us: 0,
                channel,
                kind: ControlEventKind::ControlChange {
                    controller: 7,
                    value: track.volume.min(127),
                },
            });
            control_events.push(TimedControlEvent {
                time_us: 0,
                channel,
                kind: ControlEventKind::ControlChange {
                    controller: 10,
                    value: track.balance.min(127),
                },
            });
            if !staff.is_percussion {
                control_events.push(TimedControlEvent {
                    time_us: 0,
                    channel,
                    kind: ControlEventKind::ProgramChange {
                        program: track.program.min(127),
                    },
                });
            }

            let is_guitar_six = !staff.is_percussion && staff.tuning.len() == 6;
            let mut active_by_string: HashMap<usize, usize> = HashMap::new();
            let mut pending_notes: Vec<PendingNote> = Vec::new();

            // Gather raw beat events across the expanded score, preserving multi-voice order cleanly.
            struct RawBeatItem<'a> {
                start_tick: u64,
                end_tick: u64,
                beat: &'a GpBeat,
            }
            let mut beat_timeline: Vec<RawBeatItem<'_>> = Vec::new();
            for (bar_pos_idx, &orig_bar_idx) in order.iter().enumerate() {
                let Some(bar) = staff.bars.get(orig_bar_idx) else {
                    continue;
                };
                let bar_start = bar_start_beats[bar_pos_idx];
                let triplet = score.master_bars[orig_bar_idx].triplet_feel;
                for voice in &bar.voices {
                    for placed in layout_voice_beats(voice, triplet) {
                        let start_b = bar_start.add(placed.start_beats);
                        if start_b.num < 0 || placed.dur_beats.num <= 0 {
                            continue;
                        }
                        let start_tick = beats_to_ticks(start_b).max(0) as u64;
                        let end_tick = beats_to_ticks(start_b.add(placed.dur_beats))
                            .max(start_tick as i64 + 1) as u64;
                        beat_timeline.push(RawBeatItem {
                            start_tick,
                            end_tick,
                            beat: placed.beat,
                        });
                    }
                }
            }
            beat_timeline.sort_by_key(|b| b.start_tick);

            for item in beat_timeline {
                let time_us = tempo_map.tick_to_us(item.start_tick);
                if let Some(prog) = item.beat.program_change {
                    if !staff.is_percussion {
                        control_events.push(TimedControlEvent {
                            time_us,
                            channel,
                            kind: ControlEventKind::ProgramChange { program: prog.min(127) },
                        });
                    }
                }
                if let Some(vol) = item.beat.volume_change {
                    control_events.push(TimedControlEvent {
                        time_us,
                        channel,
                        kind: ControlEventKind::ControlChange {
                            controller: 7,
                            value: vol.min(127),
                        },
                    });
                }
                if let Some(bal) = item.beat.balance_change {
                    control_events.push(TimedControlEvent {
                        time_us,
                        channel,
                        kind: ControlEventKind::ControlChange {
                            controller: 10,
                            value: bal.min(127),
                        },
                    });
                }

                if let Some(raw_chord) = item.beat.chord_name.as_deref().and_then(normalize_chord_name) {
                    chord_marks.push(GuitarProChordMark {
                        time_us,
                        track_index: out_track_idx,
                        name: raw_chord,
                        capo: staff.capo,
                    });
                }

                let mut brushed_mask = 0u8;
                let mut brushed_count = 0usize;
                let mut brushed_has_tie = false;

                for note in &item.beat.notes {
                    if note.is_dead {
                        continue;
                    }
                    let scaled_dur = ((item.end_tick.saturating_sub(item.start_tick) as f64)
                        * note.duration_percent.clamp(0.05, 1.0))
                    .round()
                    .max(1.0) as u64;
                    let note_end = item.start_tick + scaled_dur;

                    if let Some(str_idx) = note.string_idx {
                        let Some(&open_pitch) = staff.tuning.get(str_idx) else {
                            continue;
                        };
                        let sounding = open_pitch + staff.capo + note.fret;
                        if !(0..=127).contains(&sounding) {
                            continue;
                        }
                        let pitch = sounding as u8;

                        if note.is_tie_destination {
                            brushed_has_tie = true;
                            if let Some(&prev_idx) = active_by_string.get(&str_idx) {
                                if pending_notes[prev_idx].end_tick.abs_diff(item.start_tick) <= 4
                                    && (pending_notes[prev_idx].pitch == pitch || note.fret == 0)
                                {
                                    pending_notes[prev_idx].end_tick =
                                        pending_notes[prev_idx].end_tick.max(note_end);
                                    continue;
                                }
                            }
                        }

                        if let Some(&prev_idx) = active_by_string.get(&str_idx) {
                            if pending_notes[prev_idx].start_tick == item.start_tick {
                                pending_notes[prev_idx].end_tick =
                                    pending_notes[prev_idx].end_tick.max(note_end);
                                continue;
                            }
                            if pending_notes[prev_idx].end_tick > item.start_tick {
                                pending_notes[prev_idx].end_tick = item.start_tick;
                            }
                        }

                        total_note_count += 1;
                        if total_note_count > MAX_TOTAL_NOTES {
                            return Err("Guitar Pro score exceeds 150,000 notes limit".into());
                        }
                        if is_guitar_six && str_idx < 6 {
                            brushed_mask |= 1u8 << str_idx;
                            brushed_count += 1;
                        }

                        let idx = pending_notes.len();
                        pending_notes.push(PendingNote {
                            start_tick: item.start_tick,
                            end_tick: note_end,
                            pitch,
                            velocity: note.velocity.max(1),
                            string_idx: Some(str_idx),
                            fret: Some(note.fret),
                            bends: note.bends.clone(),
                        });
                        active_by_string.insert(str_idx, idx);
                    } else if staff.is_percussion {
                        let drum = note
                            .percussion_articulation
                            .map(|a| resolve_percussion_midi(track, a))
                            .unwrap_or_else(|| note.fret.clamp(0, 127) as u8);
                        total_note_count += 1;
                        if total_note_count > MAX_TOTAL_NOTES {
                            return Err("Guitar Pro score exceeds 150,000 notes limit".into());
                        }
                        pending_notes.push(PendingNote {
                            start_tick: item.start_tick,
                            end_tick: note_end,
                            pitch: drum,
                            velocity: note.velocity.max(1),
                            string_idx: None,
                            fret: None,
                            bends: Vec::new(),
                        });
                    } else if note.octave >= 0 && note.tone >= 0 {
                        let pitch_val = note.octave * 12 + note.tone;
                        if (0..=127).contains(&pitch_val) {
                            total_note_count += 1;
                            if total_note_count > MAX_TOTAL_NOTES {
                                return Err("Guitar Pro score exceeds 150,000 notes limit".into());
                            }
                            pending_notes.push(PendingNote {
                                start_tick: item.start_tick,
                                end_tick: note_end,
                                pitch: pitch_val as u8,
                                velocity: note.velocity.max(1),
                                string_idx: None,
                                fret: None,
                                bends: Vec::new(),
                            });
                        }
                    }
                }

                if let Some(direction) = item.beat.brush {
                    if is_guitar_six && brushed_count >= 2 && !brushed_has_tie {
                        brushes.push(GuitarProBrush {
                            time_us,
                            track_index: out_track_idx,
                            direction,
                            strings: brushed_mask,
                        });
                    }
                }
            }

            if is_guitar_six {
                apply_discrete_bends(&mut pending_notes, &tempo_map);
            }

            let mut track_stats_notes: Vec<(u8, u64, u64)> =
                Vec::with_capacity(pending_notes.len());
            for pn in pending_notes {
                let start_us = tempo_map.tick_to_us(pn.start_tick);
                let end_us = tempo_map.tick_to_us(pn.end_tick).max(start_us + 1);
                track_stats_notes.push((pn.pitch, start_us, end_us));
                all_note_events.push(TimedNoteEvent {
                    time_us: start_us,
                    note: pn.pitch,
                    velocity: pn.velocity,
                    is_note_on: true,
                    channel,
                    track_index: out_track_idx,
                });
                all_note_events.push(TimedNoteEvent {
                    time_us: end_us,
                    note: pn.pitch,
                    velocity: 0,
                    is_note_on: false,
                    channel,
                    track_index: out_track_idx,
                });
            }

            let (
                min_pitch,
                max_pitch,
                avg_pitch,
                start_ms,
                duration_ms,
                black_key_ratio,
                frequent_notes_ratio,
                average_duration_ms,
            ) = if track_stats_notes.is_empty() {
                (None, None, None, 0.0, 0.0, 0.0, 0.0, 0.0)
            } else {
                let min_p = track_stats_notes.iter().map(|n| n.0).min();
                let max_p = track_stats_notes.iter().map(|n| n.0).max();
                let avg_p = Some(
                    (track_stats_notes.iter().map(|n| n.0 as u32).sum::<u32>()
                        / track_stats_notes.len() as u32) as u8,
                );
                let min_start = track_stats_notes.iter().map(|n| n.1).min().unwrap_or(0);
                let max_end = track_stats_notes
                    .iter()
                    .map(|n| n.2)
                    .max()
                    .unwrap_or(min_start);
                let s_ms = (min_start as f64) / 1000.0;
                let d_ms = ((max_end.saturating_sub(min_start)) as f64) / 1000.0;
                let black = track_stats_notes
                    .iter()
                    .filter(|n| matches!(n.0 % 12, 1 | 3 | 6 | 8 | 10))
                    .count();
                let bk_ratio = (black as f64 / track_stats_notes.len() as f64) * 100.0;
                let total_dur: u64 = track_stats_notes
                    .iter()
                    .map(|n| n.2.saturating_sub(n.1))
                    .sum();
                let avg_dur_ms =
                    (total_dur as f64 / track_stats_notes.len() as f64) / 1000.0;
                let mut counts = HashMap::new();
                for n in &track_stats_notes {
                    *counts.entry(n.0).or_insert(0usize) += 1;
                }
                let avg_occ = (track_stats_notes.len() as f64) / (counts.len() as f64);
                let freq_count: usize =
                    counts.values().filter(|&&c| (c as f64) > avg_occ).sum();
                let freq_ratio =
                    (freq_count as f64 / track_stats_notes.len() as f64) * 100.0;
                (
                    min_p,
                    max_p,
                    avg_p,
                    s_ms,
                    d_ms,
                    bk_ratio,
                    freq_ratio,
                    avg_dur_ms,
                )
            };

            let program_id = (!staff.is_percussion).then_some(track.program.min(127));
            let instrument_name = if staff.is_percussion {
                "Standard Drum Kit".to_string()
            } else {
                get_gm_instrument_name(track.program.min(127)).to_string()
            };

            track_infos.push(TrackInfo {
                index: out_track_idx,
                name: track_name,
                instrument_name,
                program_id,
                channel,
                note_count: track_stats_notes.len(),
                min_pitch,
                max_pitch,
                avg_pitch,
                start_ms,
                duration_ms,
                black_key_ratio,
                frequent_notes_ratio,
                average_duration_ms,
            });
        }
    }

    // Ensure NoteOff appears before NoteOn when timestamps match (re-pluck cutoff / repeat).
    all_note_events.sort_by(|a, b| {
        a.time_us
            .cmp(&b.time_us)
            .then_with(|| a.is_note_on.cmp(&b.is_note_on))
            .then_with(|| a.track_index.cmp(&b.track_index))
            .then_with(|| a.note.cmp(&b.note))
    });
    control_events.sort();
    brushes.sort_by_key(|b| (b.time_us, b.track_index));
    chord_marks.sort_by_key(|m| (m.time_us, m.track_index));

    let duration_us = all_note_events.iter().map(|e| e.time_us).max().unwrap_or(0);
    let mut key_signatures = Vec::with_capacity(raw_key_sigs.len());
    for (tick, sf, is_minor) in raw_key_sigs {
        let time_us = tempo_map.tick_to_us(tick);
        let time_ms = time_us / 1000;
        let (key_offset, label) = calculate_key_signature_offset(sf, is_minor);
        key_signatures.push(RawKeySignature {
            tick,
            time_us,
            time_ms,
            sf,
            is_minor,
            key_offset,
            label,
        });
    }
    key_signatures.sort_by_key(|k| k.time_us);

    let title = if !score.title.is_empty() {
        score.title.clone()
    } else if let Some(stem) = fallback_title.filter(|s| !s.is_empty()) {
        stem.to_string()
    } else {
        track_infos
            .iter()
            .find(|t| !t.name.is_empty())
            .map(|t| t.name.clone())
            .unwrap_or_else(|| "Untitled Guitar Pro".to_string())
    };

    Ok(ParsedMidi {
        title,
        duration_us,
        bpm: initial_bpm,
        ppqn: GP_PPQN,
        tracks: track_infos,
        events: all_note_events,
        control_events,
        tempo_map,
        key_signatures,
        guitar_pro: Some(GuitarProMeta {
            artist: (!score.artist.is_empty()).then(|| score.artist.clone()),
            album: (!score.album.is_empty()).then(|| score.album.clone()),
            brushes,
            chord_marks,
        }),
    })
}
