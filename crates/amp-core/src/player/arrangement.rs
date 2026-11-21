//! Arrangement of source attacks for layouts with chord pads or physical guitar strings.
use super::{detect_chords, partners, ChordNote};
use crate::input::chord_wheel::wheel_transition_ms;
use crate::input::mapping::{ChordBinding, ChordPad, NoteKeyMapping};
use crate::midi::{ParsedMidi, TimedNoteEvent};
use std::collections::{BTreeMap, HashMap};

#[derive(Clone, Copy)]
struct SourceNote {
    index: usize,
    off: usize,
    start: u64,
    end: u64,
    original: u8,
    pitch: Option<u8>,
}

fn source_notes(
    song: &ParsedMidi,
    enabled: &impl Fn(usize, &TimedNoteEvent) -> bool,
    transform: &impl Fn(u8, u64) -> Option<u8>,
) -> Vec<SourceNote> {
    let paired = partners(&song.events);
    song.events
        .iter()
        .enumerate()
        .filter_map(|(index, event)| {
            if !event.is_note_on
                || event.velocity == 0
                || event.channel == 9
                || !enabled(index, event)
            {
                return None;
            }
            let off = paired[index]?;
            Some(SourceNote {
                index,
                off,
                start: event.time_us,
                end: song.events[off].time_us,
                original: event.note,
                pitch: transform(event.note, event.time_us),
            })
        })
        .collect()
}

fn assign<'a>(
    notes: &mut [Option<ChordNote>],
    members: impl Iterator<Item = &'a SourceNote>,
    binding: ChordBinding,
    chord_id: usize,
) {
    for (pos, note) in members.enumerate() {
        notes[note.index] = Some(ChordNote {
            binding,
            chord_id,
            trigger: pos == 0,
        });
        notes[note.off] = Some(ChordNote {
            binding,
            chord_id,
            trigger: false,
        });
    }
}

#[derive(Default)]
struct Beat {
    number: u64,
    attacks: Vec<SourceNote>,
    weight: [u64; 12],
    bass_weight: [u64; 12],
    written: Option<ChordBinding>,
}

// Sweep releases as well as attacks so a sustained bass influences subsequent
// occupied beats. No empty beat or note-on is manufactured in a long rest.
fn weigh_beats(song: &ParsedMidi, beats: &mut [Beat], source: &[SourceNote]) {
    let mut edges = Vec::with_capacity(source.len() * 2);
    for note in source {
        if let Some(pitch) = note.pitch {
            if note.end > note.start {
                edges.push((note.start, true, note.original, pitch));
                edges.push((note.end, false, note.original, pitch));
            }
        }
    }
    edges.sort_unstable();
    let mut sounding = BTreeMap::<(u8, u8), u64>::new();
    let mut classes = [0u64; 12];
    let mut cursor = 0;
    for beat in beats {
        let ppqn = u64::from(song.ppqn.max(1));
        let from = song.tempo_map.tick_to_us(beat.number * ppqn);
        let until = song.tempo_map.tick_to_us((beat.number + 1) * ppqn);
        let mut at = from;
        loop {
            let next = edges.get(cursor).map_or(until, |edge| edge.0.min(until));
            if next > at {
                for (weight, voices) in beat.weight.iter_mut().zip(classes) {
                    *weight += (next - at) * voices;
                }
                if let Some(((_, bass), _)) = sounding.first_key_value() {
                    beat.bass_weight[usize::from(bass % 12)] += next - at;
                }
                at = next;
            }
            if next >= until {
                break;
            }
            let (_, on, original, pitch) = edges[cursor];
            let class = &mut classes[usize::from(pitch % 12)];
            if on {
                *sounding.entry((original, pitch)).or_default() += 1;
                *class += 1;
            } else {
                let count = sounding
                    .get_mut(&(original, pitch))
                    .expect("paired arrangement release");
                *count -= 1;
                if *count == 0 {
                    sounding.remove(&(original, pitch));
                }
                *class -= 1;
            }
            cursor += 1;
        }
    }
}

fn coverage(weights: &[u64; 12], mask: u16) -> u64 {
    weights
        .iter()
        .enumerate()
        .filter(|(pc, _)| mask & (1 << pc) != 0)
        .map(|(_, weight)| weight)
        .sum()
}

fn root_class(mask: u16) -> Option<u8> {
    (0..12u8).find(|&root| {
        let has = |interval: u8| mask & (1 << ((root + interval) % 12)) != 0;
        has(0) && has(7) && (has(3) || has(4) || mask.count_ones() == 2)
    })
}

pub(in crate::player) fn plan_pad_accompaniment(
    song: &ParsedMidi,
    pads: &[ChordPad],
    tolerance_ms: u64,
    enabled: impl Fn(usize, &TimedNoteEvent) -> bool,
    transform: impl Fn(u8, u64) -> Option<u8>,
) -> Vec<Option<ChordNote>> {
    let mut result = detect_chords(
        &song.events,
        pads,
        tolerance_ms,
        |index, event| event.channel != 9 && enabled(index, event),
        &transform,
    );
    if pads.is_empty() {
        return result;
    }
    let source = source_notes(song, &enabled, &transform);
    let mut beats = Vec::<Beat>::new();
    for note in &source {
        if note.pitch.is_none() || result[note.index].is_some_and(|chord| !chord.trigger) {
            continue;
        }
        let number = song.tempo_map.us_to_tick(note.start) / u64::from(song.ppqn.max(1));
        if beats.last().is_none_or(|beat| beat.number != number) {
            beats.push(Beat {
                number,
                ..Beat::default()
            });
        }
        let beat = beats.last_mut().unwrap();
        if let Some(exact) = result[note.index] {
            beat.written = Some(exact.binding);
        }
        beat.attacks.push(*note);
    }
    weigh_beats(song, &mut beats, &source);
    let palette: Vec<_> = pads
        .iter()
        .map(|pad| {
            let mask = super::pitch_classes(pad.pitch_classes.iter().copied());
            (pad, mask, root_class(mask))
        })
        .collect();
    let mut previous = None;
    let mut id = result
        .iter()
        .flatten()
        .map(|note| note.chord_id + 1)
        .max()
        .unwrap_or(0);
    for (i, beat) in beats.iter().enumerate() {
        if let Some(written) = beat.written {
            previous = Some(written);
            continue;
        }
        let anchor = beat.attacks[0].pitch.unwrap() % 12;
        let bass = (0..12).max_by_key(|&pc| beat.bass_weight[pc]).unwrap() as u8;
        let mut context = [0; 12];
        for other in [
            i.checked_sub(1).and_then(|n| beats.get(n)),
            beats.get(i + 1),
        ]
        .into_iter()
        .flatten()
        .filter(|other| beat.number.abs_diff(other.number) == 1)
        {
            // Each neighboring beat gets an equal vote even across tempo changes.
            let total = other.weight.iter().sum::<u64>().max(1);
            for (out, weight) in context.iter_mut().zip(other.weight) {
                *out += weight.saturating_mul(1000) / total;
            }
        }
        let best = palette
            .iter()
            .enumerate()
            .filter(|(_, (pad, _, _))| pad.bass_pitch_class.is_none_or(|p| p == bass))
            .max_by_key(|(order, (pad, mask, root))| {
                let covered = coverage(&beat.weight, *mask);
                let bass_score = root.map_or(0, |pc| beat.bass_weight[usize::from(pc)]);
                let distance = pad
                    .pitch_classes
                    .iter()
                    .map(|pc| {
                        let delta = anchor.abs_diff(*pc);
                        delta.min(12 - delta)
                    })
                    .min()
                    .unwrap_or(12);
                (
                    covered > 0,
                    covered * 8 + bass_score * 3,
                    std::cmp::Reverse(distance),
                    std::cmp::Reverse(mask.count_ones()),
                    coverage(&context, *mask),
                    previous == Some(pad.binding),
                    std::cmp::Reverse(*order),
                )
            });
        if let Some((_, (pad, _, _))) = best {
            assign(&mut result, beat.attacks.iter(), pad.binding, id);
            id += 1;
            previous = Some(pad.binding);
        }
    }
    result
}

#[derive(Clone, Copy)]
pub(in crate::player) struct GuitarPlanStart {
    pub from_index: usize,
    pub initial_fret_mode: bool,
    pub initial_chord_page: usize,
    pub initial_fret_page: usize,
    pub speed: f64,
    pub hold_ms: u64,
}

pub(in crate::player) struct GuitarPlanResult {
    pub notes: Vec<Option<ChordNote>>,
    pub omitted_melody: usize,
    pub omitted_accompaniment: usize,
}

#[derive(Clone, Copy)]
struct Choice {
    binding: ChordBinding,
    // Exact voiced pitches, including unisons across source tracks.
    pitches: u128,
    kept: usize,
}

struct Onset<'a> {
    at: u64,
    notes: &'a [SourceNote],
    choices: Vec<Choice>,
}

fn choose_strings(
    strings: &[Option<u8>],
    pitches: &[SourceNote],
    lead: u8,
    slot: usize,
    fret: bool,
) -> Option<Choice> {
    if !strings.contains(&Some(lead)) {
        return None;
    }
    let mut voiced = 0u128;
    let mut selected = 0u8;
    let mut kept = 0;
    for note in pitches {
        let Some(pitch) = note.pitch else { continue };
        if let Some(string) = strings.iter().position(|&value| value == Some(pitch)) {
            voiced |= 1u128 << pitch;
            selected |= 1 << string;
            kept += 1;
        }
    }
    let binding = if fret {
        ChordBinding::Frets {
            slot,
            strings: selected,
        }
    } else {
        ChordBinding::Pluck {
            slot,
            strings: selected,
        }
    };
    Some(Choice {
        binding,
        pitches: voiced,
        kept,
    })
}

fn selector(binding: ChordBinding) -> (bool, usize) {
    match binding {
        ChordBinding::Wheel(slot) | ChordBinding::Pluck { slot, .. } => (false, slot),
        ChordBinding::Frets { slot, .. } => (true, slot),
        ChordBinding::Key(_) => unreachable!("guitar wheel action"),
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
struct Position {
    fret: bool,
    pages: [usize; 2],
    selected: Option<usize>,
    last_at: Option<u64>,
}

// Bounded lookahead keeps long songs linear in length. Each route shares its
// prefix by index; physical state includes the inactive wheel's remembered page.
#[derive(Clone, Copy)]
struct Route {
    position: Position,
    loss: [u64; 4], // missed melody, missed backing, missed written strums, physical movement
    parent: usize,
    choice: Option<usize>,
}

fn moved(
    mapping: &NoteKeyMapping,
    start: GuitarPlanStart,
    at: u64,
    pos: Position,
    binding: ChordBinding,
) -> Option<(Position, u64)> {
    let (fret, slot) = selector(binding);
    let wheel = mapping.wheel_for_mode(fret)?;
    let page = slot / wheel.positions.len();
    let old = pos.pages[usize::from(fret)];
    if old >= wheel.page_count || page >= wheel.page_count {
        return None;
    }
    let distance = old.abs_diff(page);
    let moves = distance.min(wheel.page_count - distance) as u64;
    let switch = pos.fret != fret;
    let select = switch || pos.selected != Some(slot);
    let needed_ms = start.hold_ms + wheel_transition_ms(wheel, slot, Some(old), !select, switch);
    if pos.last_at.is_some_and(|last| {
        ((at.saturating_sub(last) as f64) / start.speed) < (needed_ms * 1000) as f64
    }) {
        return None;
    }
    let mut pages = pos.pages;
    pages[usize::from(fret)] = page;
    Some((
        Position {
            fret,
            selected: Some(slot),
            pages,
            last_at: Some(at),
        },
        8 * u64::from(switch) + 2 * moves + u64::from(select),
    ))
}

const LOOKAHEAD: usize = 24;
const BEAM: usize = 24;

fn route_phrase(
    mapping: &NoteKeyMapping,
    start: GuitarPlanStart,
    position: Position,
    phrase: &[Onset<'_>],
    commit: usize,
) -> (Position, Vec<Option<usize>>) {
    let mut layers = vec![vec![Route {
        position,
        loss: [0; 4],
        parent: 0,
        choice: None,
    }]];
    for onset in phrase {
        let mut best = HashMap::<Position, Route>::new();
        let written_strum = onset
            .choices
            .first()
            .is_some_and(|option| matches!(option.binding, ChordBinding::Wheel(_)));
        let mut insert = |route: Route| {
            let entry = best.entry(route.position).or_insert(route);
            if route.loss < entry.loss {
                *entry = route;
            }
        };
        for (parent, route) in layers.last().unwrap().iter().enumerate() {
            let mut missed = route.loss;
            missed[0] += 1;
            missed[1] += (onset.notes.len() - 1) as u64;
            insert(Route {
                position: route.position,
                loss: missed,
                parent,
                choice: None,
            });
            for (i, option) in onset.choices.iter().enumerate() {
                let Some((position, cost)) =
                    moved(mapping, start, onset.at, route.position, option.binding)
                else {
                    continue;
                };
                let mut loss = route.loss;
                loss[1] += (onset.notes.len() - option.kept) as u64;
                loss[2] +=
                    u64::from(written_strum && !matches!(option.binding, ChordBinding::Wheel(_)));
                loss[3] += cost;
                insert(Route {
                    position,
                    loss,
                    parent,
                    choice: Some(i),
                });
            }
        }
        let mut routes: Vec<_> = best.into_values().collect();
        let rank = |r: &Route| (r.loss, r.parent, r.choice, r.position);
        if routes.len() > BEAM {
            routes.select_nth_unstable_by_key(BEAM, rank);
            routes.truncate(BEAM);
        }
        routes.sort_unstable_by_key(rank);
        layers.push(routes);
    }
    let mut next = 0;
    let mut committed = position;
    let mut selected = Vec::with_capacity(phrase.len());
    for depth in (1..layers.len()).rev() {
        let route = layers[depth][next];
        if depth == commit {
            committed = route.position;
        }
        if depth <= commit {
            selected.push(route.choice);
        }
        next = route.parent;
    }
    selected.reverse();
    (committed, selected)
}

pub(in crate::player) fn plan_guitar(
    song: &ParsedMidi,
    mapping: &NoteKeyMapping,
    start: GuitarPlanStart,
    tolerance_ms: u64,
    enabled: impl Fn(usize, &TimedNoteEvent) -> bool,
    transform: impl Fn(u8, u64) -> Option<u8>,
) -> GuitarPlanResult {
    let source = source_notes(song, &enabled, &transform);
    let pads = mapping.chord_pads();
    let exact = detect_chords(
        &song.events,
        pads,
        tolerance_ms,
        |index, event| event.channel != 9 && enabled(index, event),
        &transform,
    );
    let mut positions = Vec::<(bool, usize, Vec<Option<u8>>)>::new();
    let string_count = mapping
        .frets()
        .map_or(0, |frets| frets.string_strokes.len());
    for pad in pads {
        if let ChordBinding::Wheel(slot) = pad.binding {
            let pitches = mapping
                .string_pitches(slot)
                .iter()
                .take(string_count)
                .copied()
                .collect();
            positions.push((false, slot, pitches));
        }
    }
    if mapping.is_mixed() || pads.is_empty() {
        if let (Some(frets), Some(wheel)) = (mapping.frets(), mapping.wheel_for_mode(true)) {
            let slots = wheel.positions.len();
            for fret in 0..=frets.fret_count {
                let slot = if fret == 0 {
                    usize::from(frets.fret_count).div_ceil(slots) * slots
                } else {
                    usize::from(fret - 1)
                };
                if slot / slots < wheel.page_count {
                    positions.push((
                        true,
                        slot,
                        frets
                            .tuning
                            .iter()
                            .map(|open| open.checked_add(fret))
                            .collect(),
                    ));
                }
            }
        }
    }
    let mut result = GuitarPlanResult {
        notes: vec![None; song.events.len()],
        omitted_melody: 0,
        omitted_accompaniment: 0,
    };
    let mut position = Position {
        fret: start.initial_fret_mode,
        pages: [start.initial_chord_page, start.initial_fret_page],
        selected: None,
        last_at: None,
    };
    // Plan past the commit point, so an attack just before a buffer boundary
    // still anticipates the next page or mode switch.
    let mut onsets = source
        .chunk_by(|a, b| match (exact[a.index], exact[b.index]) {
            (Some(a), Some(b)) => a.chord_id == b.chord_id,
            (None, None) => a.start == b.start,
            _ => false,
        })
        .skip_while(|notes| notes[0].index < start.from_index)
        .map(|notes| {
            let at = notes[0].start;
            let mut choices = Vec::new();
            if let Some(chord) = exact[notes[0].index] {
                choices.push(Choice {
                    binding: chord.binding,
                    kept: notes.len(),
                    pitches: u128::MAX,
                });
            }
            // A strum may combine staggered attacks inside the detection tolerance.
            // Plucking that whole set at once would move the later notes off time.
            if choices.is_empty() || (mapping.is_mixed() && notes.last().unwrap().start == at) {
                if let Some(lead) = notes
                    .iter()
                    .max_by_key(|note| note.original)
                    .and_then(|note| note.pitch)
                {
                    choices.extend(positions.iter().filter_map(|(fret, slot, pitches)| {
                        choose_strings(pitches, notes, lead, *slot, *fret)
                    }));
                }
            }
            Onset { at, notes, choices }
        });
    let mut phrase = Vec::with_capacity(LOOKAHEAD);
    loop {
        phrase.extend(onsets.by_ref().take(LOOKAHEAD - phrase.len()));
        if phrase.is_empty() {
            break;
        }
        let commit = if phrase.len() < LOOKAHEAD {
            phrase.len()
        } else {
            LOOKAHEAD / 2
        };
        let (last, choices) = route_phrase(mapping, start, position, &phrase, commit);
        position = last;
        for (onset, choice) in phrase.drain(..commit).zip(choices) {
            if let Some(i) = choice {
                let option = onset.choices[i];
                let members = onset.notes.iter().filter(|n| {
                    matches!(option.binding, ChordBinding::Wheel(_))
                        || n.pitch
                            .is_some_and(|pitch| option.pitches & (1u128 << pitch) != 0)
                });
                assign(
                    &mut result.notes,
                    members,
                    option.binding,
                    onset.notes[0].index,
                );
                result.omitted_accompaniment += onset.notes.len() - option.kept;
            } else {
                result.omitted_melody += 1;
                result.omitted_accompaniment += onset.notes.len() - 1;
            }
        }
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{InstrumentConfigJson, KeyboardLayout, KeyboardLayoutJson, TempoMap};

    fn song(notes: &[(u64, u64, u8)]) -> ParsedMidi {
        let mut events: Vec<_> = notes
            .iter()
            .flat_map(|&(start, end, note)| {
                [
                    TimedNoteEvent {
                        time_us: start,
                        track_index: 0,
                        channel: 0,
                        note,
                        velocity: 92,
                        is_note_on: true,
                    },
                    TimedNoteEvent {
                        time_us: end,
                        track_index: 0,
                        channel: 0,
                        note,
                        velocity: 0,
                        is_note_on: false,
                    },
                ]
            })
            .collect();
        events.sort();
        ParsedMidi {
            title: "Arrangement".into(),
            duration_us: events.last().unwrap().time_us,
            bpm: 120.0,
            ppqn: 480,
            tracks: vec![],
            events,
            control_events: vec![],
            tempo_map: TempoMap::new(480, &[]),
            key_signatures: vec![],
        }
    }

    fn pad_plan(instrument_file: &str, source: &ParsedMidi) -> Vec<(u64, usize)> {
        let instrument: InstrumentConfigJson = serde_json::from_str(instrument_file).unwrap();
        let layout = serde_json::from_str(include_str!(
            "../../../../src-tauri/resources/defaults/KeyboardLayouts/Genshin_QWERTY_Chords.json"
        ))
        .unwrap();
        let mapping = NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[])
            .with_chord_pads(&instrument, &layout);
        plan_pad_accompaniment(
            source,
            mapping.chord_pads(),
            30,
            |_, _| true,
            |p, _| Some(p),
        )
        .iter()
        .zip(&source.events)
        .filter_map(|(planned, event)| {
            let note = planned.filter(|note| note.trigger)?;
            Some((
                event.time_us,
                mapping
                    .chord_pads()
                    .iter()
                    .position(|pad| pad.binding == note.binding)
                    .unwrap(),
            ))
        })
        .collect()
    }

    const UKULELE: &str = include_str!("../../../../src-tauri/resources/defaults/Games/Genshin Impact/Instruments/Strings/Ukulele.json");
    const EUPHONIA: &str = include_str!("../../../../src-tauri/resources/defaults/Games/Genshin Impact/Instruments/Strings/LingeringEuphonia.json");

    #[test]
    fn pads_fill_seven_melody_beats_and_follow_arpeggiated_harmony() {
        let scale: Vec<_> = [60, 62, 64, 65, 67, 69, 71]
            .into_iter()
            .enumerate()
            .map(|(i, pitch)| (i as u64 * 500_000, i as u64 * 500_000 + 125_000, pitch))
            .collect();
        let arpeggios = [[60, 64, 67], [57, 60, 64], [53, 57, 60], [55, 59, 62]];
        let mut notes = vec![];
        for (i, pitches) in arpeggios.iter().enumerate() {
            for (j, &pitch) in pitches.iter().enumerate() {
                let at = i as u64 * 500_000 + j as u64 * 125_000;
                notes.push((at, at + 100_000, pitch));
            }
        }
        for inst in [UKULELE, EUPHONIA] {
            let planned = pad_plan(inst, &song(&scale));
            assert_eq!(
                planned.iter().map(|(_, pad)| *pad).collect::<Vec<_>>(),
                [0, 1, 2, 3, 4, 5, 2]
            );
            assert_eq!(
                planned.iter().map(|(at, _)| *at).collect::<Vec<_>>(),
                scale.iter().map(|(at, _, _)| *at).collect::<Vec<_>>()
            );
            assert_eq!(
                pad_plan(inst, &song(&notes)),
                [(0, 0), (500_000, 5), (1_000_000, 3), (1_500_000, 4)]
            );
        }
    }

    #[test]
    fn sustained_bass_influences_same_melody_and_rests_do_not_strum() {
        let source = song(&[
            (0, 900_000, 48),
            (500_000, 850_000, 64),
            (1_500_000, 2_400_000, 45),
            (2_000_000, 2_350_000, 64),
        ]);
        for inst in [UKULELE, EUPHONIA] {
            assert_eq!(
                pad_plan(inst, &source),
                [(0, 0), (500_000, 0), (1_500_000, 5), (2_000_000, 5)]
            );
        }
    }

    #[test]
    fn neighboring_beat_resolves_ambiguous_melody_harmony() {
        // B belongs to Em and G. The following E favors Em while D favors G.
        for inst in [UKULELE, EUPHONIA] {
            for (next, expected) in [(64, 2), (62, 4)] {
                let source = song(&[(75_000, 200_000, 71), (600_000, 900_000, next)]);
                assert_eq!(pad_plan(inst, &source)[0], (75_000, expected));
            }
        }
    }

    #[test]
    fn percussion_and_disabled_tracks_cannot_supply_a_pad_attack() {
        let instrument: InstrumentConfigJson = serde_json::from_str(UKULELE).unwrap();
        let layout = serde_json::from_str(include_str!(
            "../../../../src-tauri/resources/defaults/KeyboardLayouts/Genshin_QWERTY_Chords.json"
        ))
        .unwrap();
        let mapping = NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[])
            .with_chord_pads(&instrument, &layout);
        let mut source = song(&[
            (0, 120_000, 60),
            (500_000, 600_000, 64),
            (1_000_000, 1_100_000, 69),
        ]);
        for event in &mut source.events {
            if event.note == 60 {
                event.channel = 9;
            }
            if event.note == 64 {
                event.track_index = 1;
            }
        }
        let planned = plan_pad_accompaniment(
            &source,
            mapping.chord_pads(),
            30,
            |_, e| e.track_index == 0,
            |p, _| Some(p),
        );
        let attacks: Vec<_> = planned
            .iter()
            .zip(&source.events)
            .filter(|(n, _)| n.is_some_and(|c| c.trigger))
            .map(|(_, e)| e.time_us)
            .collect();
        assert_eq!(attacks, [1_000_000]);
    }

    #[test]
    fn impossible_transition_omits_its_attack_and_keeps_later_actions_on_time() {
        let result = plan(
            &guitar(false, true, true),
            &[
                (0, 10_000, 88),
                (12_000, 80_000, 40),
                (1_000_000, 1_100_000, 40),
            ],
        );
        assert_eq!(
            (result.omitted_melody, result.omitted_accompaniment),
            (1, 0)
        );
        assert_eq!(bindings(&result).len(), 2);
        assert!(result.notes[4].is_some_and(|n| n.trigger));
    }

    #[test]
    fn exact_chords_and_tempo_changes_keep_source_attacks() {
        let mut source = song(&[
            (0, 100_000, 60),
            (5_000, 100_000, 64),
            (10_000, 100_000, 67),
            (250_000, 400_000, 62),
            (250_000, 400_000, 65),
            (250_000, 400_000, 69),
            (550_000, 630_000, 67),
            (830_000, 940_000, 67),
            (1_000_000, 1_100_000, 67),
            (1_500_000, 1_550_000, 67),
        ]);
        source.tempo_map = TempoMap::new(480, &[(480, 1_000_000)]);
        for inst in [UKULELE, EUPHONIA] {
            assert_eq!(
                pad_plan(inst, &source),
                [(0, 0), (250_000, 1), (550_000, 4), (1_500_000, 4)]
            );
        }
    }

    fn guitar(mixed: bool, fret_only: bool, paged: bool) -> NoteKeyMapping {
        let mut instrument: InstrumentConfigJson = serde_json::from_str(include_str!(
            "../../../../src-tauri/resources/defaults/Games/Neverness to Everness/Instruments/Strings/Guitar.json")).unwrap();
        instrument.paged_frets = Some(paged);
        instrument.paged_chords = Some(paged);
        let chord_layout: KeyboardLayoutJson = serde_json::from_str(include_str!(
            "../../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Chord_Wheel.json"
        ))
        .unwrap();
        let fret_layout = serde_json::from_str(include_str!(
            "../../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Fret_Wheel.json"
        ))
        .unwrap();
        let base = NoteKeyMapping::from_pairs(KeyboardLayout::Chromatic36, &[]);
        if fret_only {
            base.with_frets(
                &instrument,
                &fret_layout,
                chord_layout.chord_wheel.as_ref().unwrap(),
            )
        } else {
            base.with_guitar_chords(&instrument, &chord_layout, &fret_layout, mixed)
        }
    }

    fn plan(mapping: &NoteKeyMapping, notes: &[(u64, u64, u8)]) -> GuitarPlanResult {
        let start = GuitarPlanStart {
            from_index: 0,
            initial_fret_mode: mapping.chord_pads().is_empty(),
            initial_chord_page: 0,
            initial_fret_page: 0,
            speed: 1.0,
            hold_ms: 20,
        };
        plan_guitar(
            &song(notes),
            mapping,
            start,
            30,
            |_, _| true,
            |pitch, _| Some(pitch),
        )
    }

    fn bindings(result: &GuitarPlanResult) -> Vec<ChordBinding> {
        result
            .notes
            .iter()
            .flatten()
            .filter(|note| note.trigger)
            .map(|note| note.binding)
            .collect()
    }

    #[test]
    fn upcoming_low_e_guides_earlier_string_selection_and_avoids_page_travel() {
        let mapped = guitar(false, false, true);
        let result = plan(&mapped, &[(0, 40_000, 59), (52_000, 100_000, 40)]);
        assert_eq!(result.omitted_melody, 0);
        assert_eq!(
            bindings(&result),
            [
                ChordBinding::Pluck {
                    slot: 5,
                    strings: 2
                },
                ChordBinding::Pluck {
                    slot: 5,
                    strings: 32
                }
            ]
        );
    }

    #[test]
    fn mixed_strums_plucks_and_returns_to_chord_mode() {
        // Cmaj7 needs chord page 2. The 400 ms return allows a mode switch and
        // selection, but resetting the remembered chord page would miss it.
        let notes = [
            (0, 100_000, 48),
            (0, 100_000, 52),
            (0, 100_000, 55),
            (0, 100_000, 59),
            (1_000_000, 1_100_000, 88),
            (1_400_000, 1_500_000, 48),
            (1_400_000, 1_500_000, 52),
            (1_400_000, 1_500_000, 55),
            (1_400_000, 1_500_000, 59),
        ];
        let result = plan(&guitar(true, false, true), &notes);
        assert_eq!(
            bindings(&result),
            [
                ChordBinding::Wheel(6),
                ChordBinding::Frets {
                    slot: 23,
                    strings: 1
                },
                ChordBinding::Wheel(6)
            ]
        );
        assert_eq!(
            (result.omitted_melody, result.omitted_accompaniment),
            (0, 0)
        );
    }

    #[test]
    fn shared_fret_keeps_melody_and_counts_omitted_backing() {
        let result = plan(
            &guitar(false, true, true),
            &[(0, 100_000, 60), (0, 100_000, 61), (900_000, 1_000_000, 40)],
        );
        assert_eq!(
            (result.omitted_melody, result.omitted_accompaniment),
            (0, 1)
        );
        assert!(matches!(
            bindings(&result)[1],
            ChordBinding::Frets {
                slot: 24,
                strings: 32
            }
        ));
        let single_page = plan(
            &guitar(false, true, false),
            &[(0, 100_000, 40), (500_000, 600_000, 88)],
        );
        assert!(bindings(&single_page).is_empty());
        assert_eq!(single_page.omitted_melody, 2);
    }
}
