use std::collections::HashMap;
use crate::input::chord_wheel::StrumDirection;
use super::model::*;
use super::MAX_MASTER_BARS;

struct Cursor<'a> {
    data: &'a [u8],
    pos: usize,
}

impl<'a> Cursor<'a> {
    fn new(data: &'a [u8]) -> Self {
        Self { data, pos: 0 }
    }

    fn read_u8(&mut self) -> Result<u8, String> {
        let b = *self
            .data
            .get(self.pos)
            .ok_or_else(|| "Unexpected end of Guitar Pro 3-5 file".to_string())?;
        self.pos += 1;
        Ok(b)
    }

    fn read_i8(&mut self) -> Result<i8, String> {
        Ok(self.read_u8()? as i8)
    }

    fn read_bool(&mut self) -> Result<bool, String> {
        Ok(self.read_u8()? != 0)
    }

    fn read_i16_le(&mut self) -> Result<i16, String> {
        let slice = self
            .data
            .get(self.pos..self.pos + 2)
            .ok_or_else(|| "Unexpected end of Guitar Pro 3-5 file".to_string())?;
        self.pos += 2;
        Ok(i16::from_le_bytes(slice.try_into().unwrap()))
    }

    fn read_i32_le(&mut self) -> Result<i32, String> {
        let slice = self
            .data
            .get(self.pos..self.pos + 4)
            .ok_or_else(|| "Unexpected end of Guitar Pro 3-5 file".to_string())?;
        self.pos += 4;
        Ok(i32::from_le_bytes(slice.try_into().unwrap()))
    }

    fn read_f64_be(&mut self) -> Result<f64, String> {
        let slice = self
            .data
            .get(self.pos..self.pos + 8)
            .ok_or_else(|| "Unexpected end of Guitar Pro 3-5 file".to_string())?;
        self.pos += 8;
        Ok(f64::from_be_bytes(slice.try_into().unwrap()))
    }

    fn skip(&mut self, len: usize) -> Result<(), String> {
        let next = self
            .pos
            .checked_add(len)
            .filter(|&n| n <= self.data.len())
            .ok_or_else(|| "Unexpected end of Guitar Pro 3-5 file".to_string())?;
        self.pos = next;
        Ok(())
    }

    fn read_bytes(&mut self, len: usize) -> Result<&'a [u8], String> {
        let end = self
            .pos
            .checked_add(len)
            .filter(|&n| n <= self.data.len())
            .ok_or_else(|| "Unexpected end of Guitar Pro 3-5 file".to_string())?;
        let slice = &self.data[self.pos..end];
        self.pos = end;
        Ok(slice)
    }

    fn read_string(&mut self, len: usize) -> Result<String, String> {
        let bytes = self.read_bytes(len)?;
        Ok(decode_gp_text(bytes))
    }

    fn read_string_byte_length(&mut self, fixed_len: usize) -> Result<String, String> {
        let str_len = self.read_u8()? as usize;
        let actual = str_len.min(fixed_len);
        let text = self.read_string(actual)?;
        if str_len > fixed_len {
            let _ = self.skip(str_len - fixed_len);
        } else if actual < fixed_len {
            self.skip(fixed_len - actual)?;
        }
        Ok(text)
    }

    fn read_string_int_unused(&mut self) -> Result<String, String> {
        let _ = self.read_i32_le()?;
        let len = self.read_u8()? as usize;
        self.read_string(len)
    }

    fn read_string_int(&mut self) -> Result<String, String> {
        let len = self.read_i32_le()?;
        if len <= 0 {
            return Ok(String::new());
        }
        self.read_string(len as usize)
    }

    fn read_string_int_byte(&mut self) -> Result<String, String> {
        let len = self.read_i32_le()?.saturating_sub(1);
        let _ = self.read_u8()?;
        if len <= 0 {
            return Ok(String::new());
        }
        self.read_string(len as usize)
    }
}

fn decode_gp_text(bytes: &[u8]) -> String {
    let trimmed = bytes.split(|&b| b == 0).next().unwrap_or(bytes);
    if let Ok(s) = std::str::from_utf8(trimmed) {
        return s.trim_start_matches('\u{FEFF}').trim().to_string();
    }
    const WIN1252: [char; 32] = [
        '€', '\u{81}', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u{8D}', 'Ž',
        '\u{8F}', '\u{90}', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u{9D}',
        'ž', 'Ÿ',
    ];
    let mut out = String::with_capacity(trimmed.len());
    for &b in trimmed {
        if b >= 0x80 && b < 0xA0 {
            out.push(WIN1252[(b - 0x80) as usize]);
        } else {
            out.push(b as char);
        }
    }
    out.trim().to_string()
}

fn gp_dynamic_to_velocity(dynamic: i8) -> u8 {
    match dynamic {
        1 => 15,
        2 => 31,
        3 => 47,
        4 => 63,
        5 => 79,
        6 => 95,
        7 => 111,
        8 => 127,
        _ => 95,
    }
}

fn map_gp5_percussion(midi: i32) -> i32 {
    match midi {
        27 => 42,
        28 => 60,
        32 => 31,
        other => other,
    }
}

#[derive(Clone, Copy)]
struct PlaybackInfo {
    program: u8,
    volume: u8,
    balance: u8,
}

pub(super) fn parse_gp3_to_5(data: &[u8]) -> Result<GpScore, String> {
    let mut cur = Cursor::new(data);
    let version_str = cur.read_string_byte_length(30)?;
    let rest = version_str
        .strip_prefix("FICHIER GUITAR PRO ")
        .ok_or_else(|| "Unsupported Guitar Pro header".to_string())?;
    let ver_tail = rest.get(1..).unwrap_or(rest);
    let (maj_s, min_s) = ver_tail
        .split_once('.')
        .ok_or_else(|| "Invalid Guitar Pro version string".to_string())?;
    let maj = maj_s
        .trim()
        .parse::<i32>()
        .map_err(|_| "Invalid Guitar Pro major version".to_string())?;
    let min = min_s
        .chars()
        .take_while(|c| c.is_ascii_digit())
        .collect::<String>()
        .parse::<i32>()
        .unwrap_or(0);
    let version = maj * 100 + min;

    let mut score = GpScore::default();
    score.title = cur.read_string_int_unused()?;
    let _subtitle = cur.read_string_int_unused()?;
    score.artist = cur.read_string_int_unused()?;
    score.album = cur.read_string_int_unused()?;
    let _words = cur.read_string_int_unused()?;
    if version >= 500 {
        let _music = cur.read_string_int_unused()?;
    }
    let _copyright = cur.read_string_int_unused()?;
    let _tab = cur.read_string_int_unused()?;
    let _instructions = cur.read_string_int_unused()?;
    let notice_lines = cur.read_i32_le()?.max(0) as usize;
    for _ in 0..notice_lines.min(1024) {
        let _ = cur.read_string_int_unused()?;
    }

    let global_triplet_feel = if version < 500 {
        if cur.read_bool()? {
            GpTripletFeel::Triplet8th
        } else {
            GpTripletFeel::None
        }
    } else {
        GpTripletFeel::None
    };

    if version >= 400 {
        let _lyrics_track = cur.read_i32_le()?;
        for _ in 0..5 {
            let _start_bar = cur.read_i32_le()?;
            let _text = cur.read_string_int()?;
        }
    }

    if version >= 510 {
        cur.skip(19)?;
    }

    if version >= 500 {
        // Page setup (28 bytes) + flags (2) + 10 header/footer templates
        cur.skip(28)?;
        let _flags = cur.read_i16_le()?;
        for _ in 0..10 {
            let _ = cur.read_string_int_byte()?;
        }
        let _tempo_label = cur.read_string_int_byte()?;
    }

    let tempo = cur.read_i32_le()?;
    score.tempo_bpm = if tempo > 0 { f64::from(tempo) } else { 120.0 };
    if version >= 510 {
        let _hide_tempo = cur.read_bool()?;
    }

    let _key_sig = cur.read_i32_le()?;
    if version >= 400 {
        let _octave = cur.read_u8()?;
    }

    let mut playback_infos = Vec::with_capacity(64);
    for _ in 0..64 {
        let prog = cur.read_i32_le()?.clamp(0, 127) as u8;
        let vol_raw = cur.read_u8()?;
        let bal_raw = cur.read_u8()?;
        cur.skip(6)?;
        // GP stores 1..16 (step 8 or direct 0..16 * 8 - 1 -> 0..127); normalize if <= 16.
        let volume = if vol_raw <= 16 {
            ((u16::from(vol_raw) * 8).saturating_sub(1)).min(127) as u8
        } else {
            vol_raw.min(127)
        };
        let balance = if bal_raw <= 16 {
            ((u16::from(bal_raw) * 8).min(127)) as u8
        } else {
            bal_raw.min(127)
        };
        playback_infos.push(PlaybackInfo {
            program: prog,
            volume,
            balance,
        });
    }

    let mut direction_lookup: HashMap<usize, Vec<GpDirection>> = HashMap::new();
    if version >= 500 {
        const DIRS: [GpDirection; 19] = [
            GpDirection::TargetCoda,
            GpDirection::TargetDoubleCoda,
            GpDirection::TargetSegno,
            GpDirection::TargetSegnoSegno,
            GpDirection::TargetFine,
            GpDirection::JumpDaCapo,
            GpDirection::JumpDaCapoAlCoda,
            GpDirection::JumpDaCapoAlDoubleCoda,
            GpDirection::JumpDaCapoAlFine,
            GpDirection::JumpDalSegno,
            GpDirection::JumpDalSegnoAlCoda,
            GpDirection::JumpDalSegnoAlDoubleCoda,
            GpDirection::JumpDalSegnoAlFine,
            GpDirection::JumpDalSegnoSegno,
            GpDirection::JumpDalSegnoSegnoAlCoda,
            GpDirection::JumpDalSegnoSegnoAlDoubleCoda,
            GpDirection::JumpDalSegnoSegnoAlFine,
            GpDirection::JumpDaCoda,
            GpDirection::JumpDaDoubleCoda,
        ];
        for dir in DIRS {
            let idx = cur.read_i16_le()?;
            if idx >= 1 {
                direction_lookup
                    .entry((idx - 1) as usize)
                    .or_default()
                    .push(dir);
            }
        }
        cur.skip(4)?;
    }

    let bar_count = cur.read_i32_le()?;
    let track_count = cur.read_i32_le()?;
    if !(0..=MAX_MASTER_BARS as i32).contains(&bar_count) || !(1..=128).contains(&track_count) {
        return Err("Guitar Pro bar or track count is out of supported range".into());
    }
    let bar_count = bar_count as usize;
    let track_count = track_count as usize;

    // MasterBars
    for i in 0..bar_count {
        let prev = score.master_bars.last().cloned();
        let mut mb = GpMasterBar {
            num: prev.as_ref().map_or(4, |p| p.num),
            den: prev.as_ref().map_or(4, |p| p.den),
            repeat_count: 1,
            triplet_feel: global_triplet_feel,
            ..GpMasterBar::default()
        };
        if i == 0 {
            mb.tempo_automations.push(GpTempoAutomation {
                ratio_position: 0.0,
                bpm: score.tempo_bpm,
            });
        }
        let flags = cur.read_u8()?;
        if flags & 0x01 != 0 {
            mb.num = i32::from(cur.read_u8()?);
        }
        if flags & 0x02 != 0 {
            mb.den = i32::from(cur.read_u8()?);
        }
        mb.is_repeat_start = flags & 0x04 != 0;
        if flags & 0x08 != 0 {
            let cnt = i32::from(cur.read_u8()?);
            mb.repeat_count = (cnt + if version >= 500 { 0 } else { 1 }).max(1);
        }
        if flags & 0x10 != 0 && version < 500 {
            let mut existent_alts = 0u32;
            for (b_idx, b) in score.master_bars.iter().enumerate().rev() {
                if b_idx + 1 < i && b.repeat_count > 1 {
                    break;
                }
                if b.is_repeat_start {
                    break;
                }
                existent_alts |= b.alternate_endings;
            }
            let repeat_mask = i32::from(cur.read_u8()?);
            let mut alt = 0u32;
            for bit in 0..8 {
                let flag_bit = 1u32 << bit;
                if repeat_mask > bit && (existent_alts & flag_bit) == 0 {
                    alt |= flag_bit;
                }
            }
            mb.alternate_endings = alt;
        }
        if flags & 0x20 != 0 {
            let _marker = cur.read_string_int_byte()?;
            cur.skip(4)?; // RGBA
        }
        if flags & 0x40 != 0 {
            let sf = cur.read_i8()?.clamp(-7, 7);
            let is_minor = cur.read_u8()? != 0;
            mb.key_sig = Some((sf, is_minor));
        }
        if version >= 500 && flags & 0x03 != 0 {
            cur.skip(4)?;
        }
        if version >= 500 {
            mb.alternate_endings = u32::from(cur.read_u8()?);
            let tf = cur.read_u8()?;
            mb.triplet_feel = match tf {
                1 => GpTripletFeel::Triplet8th,
                2 => GpTripletFeel::Triplet16th,
                _ => GpTripletFeel::None,
            };
            let _ = cur.read_u8()?;
        }
        if let Some(dirs) = direction_lookup.remove(&i) {
            mb.directions = dirs;
        }
        score.master_bars.push(mb);
    }

    // Tracks
    for _ in 0..track_count {
        let flags = cur.read_u8()?;
        let name = cur.read_string_byte_length(40)?;
        let string_count = cur.read_i32_le()?.clamp(0, 7) as usize;
        let mut tuning = Vec::with_capacity(string_count);
        for s in 0..7 {
            let t = cur.read_i32_le()?;
            if s < string_count {
                tuning.push(t);
            }
        }
        let _port = cur.read_i32_le()?;
        let info_idx = cur.read_i32_le()? - 1;
        let _effect_ch = cur.read_i32_le()? - 1;
        let _fret_count = cur.read_i32_le()?;
        let capo = cur.read_i32_le()?;
        cur.skip(4)?; // Color RGBA

        let mut track = GpTrack {
            name,
            volume: 100,
            balance: 64,
            is_solo: flags & 0x10 != 0,
            is_mute: flags & 0x20 != 0,
            ..GpTrack::default()
        };
        if info_idx >= 0 && (info_idx as usize) < playback_infos.len() {
            let info = playback_infos[info_idx as usize];
            track.program = info.program;
            track.volume = info.volume;
            track.balance = info.balance;
        }
        if version >= 500 {
            let _staff_flags = cur.read_u8()?;
            let _midi_auto = cur.read_u8()?;
            let _auto_accent = cur.read_u8()?;
            let _bank = cur.read_u8()?;
            let _human = cur.read_u8()?;
            let _clef_mode = cur.read_i32_le()?;
            cur.skip(18)?; // 4 + 4 + 10
            cur.skip(2)?; // 255, 3
            cur.skip(16)?; // RSE bank
            if version >= 510 {
                cur.skip(4)?; // 3-band EQ + PRE
                let _fx_name = cur.read_string_int_byte()?;
                let _fx_cat = cur.read_string_int_byte()?;
            }
        }
        track.staves.push(GpStaff {
            is_percussion: flags & 0x01 != 0,
            tuning,
            capo,
            bars: Vec::with_capacity(bar_count),
        });
        score.tracks.push(track);
    }

    // Bars (row-major: for bar in 0..bar_count, for track in 0..track_count)
    for bar_idx in 0..bar_count {
        for track_idx in 0..track_count {
            let voice_count = if version >= 500 {
                let _ = cur.read_u8()?;
                2usize
            } else {
                1usize
            };
            let is_perc = score.tracks[track_idx].staves[0].is_percussion;
            let tuning_len = score.tracks[track_idx].staves[0].tuning.len();
            let mut bar = GpBar::default();
            for _ in 0..voice_count {
                let beat_count = cur.read_i32_le()?;
                if beat_count < 0 || beat_count > 4096 {
                    return Err("Invalid beat count in Guitar Pro bar".into());
                }
                if beat_count == 0 {
                    continue;
                }
                let mut voice = GpVoice::default();
                for _ in 0..beat_count {
                    let mut beat = read_gp3_to_5_beat(
                        &mut cur,
                        version,
                        is_perc,
                        tuning_len,
                        &mut score.master_bars[bar_idx],
                    )?;
                    // Keep chord mark & effects clean
                    if let Some(ref mut grace) = beat.0 {
                        voice.beats.push(std::mem::take(grace));
                    }
                    voice.beats.push(beat.1);
                }
                bar.voices.push(voice);
            }
            score.tracks[track_idx].staves[0].bars.push(bar);
        }
    }

    Ok(score)
}

/// Returns `(optional preceding grace beat, main beat)`.
fn read_gp3_to_5_beat(
    cur: &mut Cursor<'_>,
    version: i32,
    is_percussion: bool,
    tuning_len: usize,
    master_bar: &mut GpMasterBar,
) -> Result<(Option<GpBeat>, GpBeat), String> {
    let mut beat = GpBeat::default();
    let flags = cur.read_u8()?;
    if flags & 0x01 != 0 {
        beat.dots = 1;
    }
    if flags & 0x40 != 0 {
        let typ = cur.read_u8()?;
        beat.is_empty = (typ & 0x02) == 0;
    }
    beat.duration = match cur.read_i8()? {
        -2 => GpDuration::Whole,
        -1 => GpDuration::Half,
        0 => GpDuration::Quarter,
        1 => GpDuration::Eighth,
        2 => GpDuration::Sixteenth,
        3 => GpDuration::ThirtySecond,
        4 => GpDuration::SixtyFourth,
        _ => GpDuration::Quarter,
    };
    if flags & 0x20 != 0 {
        let num = cur.read_i32_le()?;
        beat.tuplet_num = num;
        beat.tuplet_den = match num {
            1 => 1,
            3 => 2,
            5..=7 => 4,
            9..=13 => 8,
            2 | 4 | 8 => num,
            _ => {
                beat.tuplet_num = 1;
                1
            }
        };
    }
    if flags & 0x02 != 0 {
        beat.chord_name = read_gp_chord(cur, version, tuning_len)?;
    }
    if flags & 0x04 != 0 {
        let _text = cur.read_string_int_unused()?;
    }
    if flags & 0x08 != 0 {
        read_beat_effects(cur, version, &mut beat)?;
    }
    if flags & 0x10 != 0 {
        read_mix_table_change(cur, version, &mut beat, master_bar)?;
    }

    let string_flags = cur.read_u8()?;
    let mut grace_notes = Vec::new();
    let mut grace_is_on_beat = false;

    for i in (0..=6).rev() {
        if (string_flags & (1 << i)) != 0 && (6 - i) < tuning_len {
            let string_idx = 6 - i;
            let (note, grace) = read_gp3_to_5_note(cur, version, is_percussion, string_idx)?;
            if let Some((g_note, on_beat)) = grace {
                grace_is_on_beat = on_beat;
                grace_notes.push(g_note);
            }
            beat.notes.push(note);
        }
    }

    if version >= 500 {
        let flags2 = cur.read_i16_le()?;
        if flags2 & 0x0800 != 0 {
            let _ = cur.read_u8()?;
        }
    }

    let grace_beat = if !grace_notes.is_empty() {
        Some(GpBeat {
            duration: GpDuration::ThirtySecond,
            grace_type: Some(if grace_is_on_beat {
                GpGraceType::OnBeat
            } else {
                GpGraceType::BeforeBeat
            }),
            notes: grace_notes,
            ..GpBeat::default()
        })
    } else {
        None
    };

    Ok((grace_beat, beat))
}

fn read_gp_chord(
    cur: &mut Cursor<'_>,
    version: i32,
    tuning_len: usize,
) -> Result<Option<String>, String> {
    let name = if version >= 500 {
        cur.skip(17)?;
        let name = cur.read_string_byte_length(21)?;
        cur.skip(4)?;
        let _first_fret = cur.read_i32_le()?;
        for _ in 0..7 {
            let _ = cur.read_i32_le()?;
        }
        let _barres = cur.read_u8()?;
        cur.skip(5)?;
        cur.skip(26)?;
        name
    } else if cur.read_u8()? != 0 {
        if version >= 400 {
            cur.skip(16)?;
            let name = cur.read_string_byte_length(21)?;
            cur.skip(4)?;
            let _first_fret = cur.read_i32_le()?;
            for _ in 0..7 {
                let _ = cur.read_i32_le()?;
            }
            let _barres = cur.read_u8()?;
            cur.skip(5)?;
            cur.skip(26)?;
            name
        } else {
            cur.skip(25)?;
            let name = cur.read_string_byte_length(34)?;
            let _first_fret = cur.read_i32_le()?;
            for _ in 0..6 {
                let _ = cur.read_i32_le()?;
            }
            let _ = tuning_len;
            cur.skip(36)?;
            name
        }
    } else {
        let strings = if version >= 406 { 7 } else { 6 };
        let name = cur.read_string_int_byte()?;
        let first_fret = cur.read_i32_le()?;
        if first_fret > 0 {
            for _ in 0..strings {
                let _ = cur.read_i32_le()?;
            }
        }
        name
    };
    Ok((!name.is_empty()).then_some(name))
}

fn read_beat_effects(
    cur: &mut Cursor<'_>,
    version: i32,
    beat: &mut GpBeat,
) -> Result<(), String> {
    let flags = cur.read_u8()?;
    let flags2 = if version >= 400 { cur.read_u8()? } else { 0 };
    if flags & 0x20 != 0 {
        let _slap_pop = cur.read_i8()?;
        if version < 400 {
            cur.skip(4)?;
        }
    }
    if flags2 & 0x04 != 0 {
        let _typ = cur.read_u8()?;
        let _val = cur.read_i32_le()?;
        let count = cur.read_i32_le()?.clamp(0, 256) as usize;
        for _ in 0..count {
            let _off = cur.read_i32_le()?;
            let _v = cur.read_i32_le()?;
            let _vib = cur.read_bool()?;
        }
    }
    if flags & 0x40 != 0 {
        let (stroke_up, stroke_down) = if version < 500 {
            let down = cur.read_u8()?;
            let up = cur.read_u8()?;
            (up, down)
        } else {
            let up = cur.read_u8()?;
            let down = cur.read_u8()?;
            (up, down)
        };
        if stroke_up > 0 {
            beat.brush = Some(StrumDirection::Up);
        } else if stroke_down > 0 {
            beat.brush = Some(StrumDirection::Down);
        }
    }
    if flags2 & 0x02 != 0 {
        let _pick_stroke = cur.read_i8()?;
    }
    Ok(())
}

fn read_mix_table_change(
    cur: &mut Cursor<'_>,
    version: i32,
    beat: &mut GpBeat,
    master_bar: &mut GpMasterBar,
) -> Result<(), String> {
    let instrument = cur.read_i8()?;
    if version >= 500 {
        cur.skip(16)?; // RSE bank
    }
    let volume = cur.read_i8()?;
    let balance = cur.read_i8()?;
    let chorus = cur.read_i8()?;
    let reverb = cur.read_i8()?;
    let phaser = cur.read_i8()?;
    let tremolo = cur.read_i8()?;
    if version >= 500 {
        let _tempo_name = cur.read_string_int_byte()?;
    }
    let tempo = cur.read_i32_le()?;

    for val in [volume, balance, chorus, reverb, phaser, tremolo] {
        if val >= 0 {
            let _dur = cur.read_u8()?;
        }
    }
    if tempo >= 0 {
        let _dur = cur.read_i8()?;
        if version >= 510 {
            let _hide = cur.read_u8()?;
        }
    }
    if version >= 400 {
        let _flags = cur.read_u8()?;
    }
    if version >= 500 {
        let _wah = cur.read_i8()?;
    }
    if version >= 510 {
        let _ = cur.read_string_int_byte()?;
        let _ = cur.read_string_int_byte()?;
    }

    if instrument >= 0 {
        beat.program_change = Some(instrument.clamp(0, 127) as u8);
    }
    if volume >= 0 {
        let vol = ((i32::from(volume) * 8).saturating_sub(1)).clamp(0, 127) as u8;
        beat.volume_change = Some(vol);
    }
    if balance >= 0 {
        let bal = (i32::from(balance) * 8).clamp(0, 127) as u8;
        beat.balance_change = Some(bal);
    }
    if tempo > 0 {
        let bpm = f64::from(tempo);
        beat.tempo_change = Some(bpm);
        if master_bar.tempo_automations.is_empty() {
            master_bar.tempo_automations.push(GpTempoAutomation {
                ratio_position: 0.0,
                bpm,
            });
        }
    }
    Ok(())
}

fn read_gp3_to_5_note(
    cur: &mut Cursor<'_>,
    version: i32,
    is_percussion: bool,
    string_idx: usize,
) -> Result<(GpNote, Option<(GpNote, bool)>), String> {
    let flags = cur.read_u8()?;
    let mut note = GpNote {
        string_idx: (!is_percussion).then_some(string_idx),
        velocity: 95,
        duration_percent: 1.0,
        octave: -1,
        tone: -1,
        is_ghost: flags & 0x04 != 0,
        ..GpNote::default()
    };

    if flags & 0x20 != 0 {
        match cur.read_u8()? {
            2 => note.is_tie_destination = true,
            3 => note.is_dead = true,
            _ => {}
        }
    }
    if flags & 0x01 != 0 && version < 500 {
        let _dur = cur.read_u8()?;
        let _tuplet = cur.read_u8()?;
    }
    if flags & 0x10 != 0 {
        note.velocity = gp_dynamic_to_velocity(cur.read_i8()?);
    }
    if flags & 0x20 != 0 {
        note.fret = i32::from(cur.read_i8()?);
    }
    if flags & 0x80 != 0 {
        let _lf = cur.read_i8()?;
        let _rf = cur.read_i8()?;
    }
    if version >= 500 {
        if flags & 0x01 != 0 {
            note.duration_percent = cur.read_f64_be()?.clamp(0.05, 1.0);
        }
        let _flags2 = cur.read_u8()?;
    }

    let mut grace = None;
    if flags & 0x08 != 0 {
        let ef1 = cur.read_u8()?;
        let ef2 = if version >= 400 { cur.read_u8()? } else { 0 };
        if ef1 & 0x01 != 0 {
            let _bend_type = cur.read_u8()?;
            let _bend_val = cur.read_i32_le()?;
            let count = cur.read_i32_le()?.clamp(0, 256) as usize;
            for _ in 0..count {
                let offset = cur.read_i32_le()?.clamp(0, 60) as u16;
                let value = (cur.read_i32_le()? / 25) as i16;
                let _vib = cur.read_bool()?;
                note.bends.push(GpBendPoint { offset, value });
            }
        }
        if ef1 & 0x10 != 0 {
            let g_fret = i32::from(cur.read_i8()?);
            let g_vel = gp_dynamic_to_velocity(cur.read_i8()?);
            let _trans = cur.read_i8()?;
            cur.skip(1)?; // duration
            let (g_dead, on_beat) = if version < 500 {
                (false, false)
            } else {
                let gf = cur.read_u8()?;
                (gf & 0x01 != 0, gf & 0x02 != 0)
            };
            grace = Some((
                GpNote {
                    string_idx: note.string_idx,
                    fret: g_fret,
                    percussion_articulation: is_percussion.then_some(map_gp5_percussion(g_fret)),
                    velocity: g_vel,
                    duration_percent: 1.0,
                    is_dead: g_dead,
                    ..GpNote::default()
                },
                on_beat,
            ));
        }
        if ef2 & 0x04 != 0 {
            let _tremolo_marks = cur.read_u8()?;
        }
        if ef2 & 0x08 != 0 {
            let _slide = cur.read_i8()?;
        }
        if ef2 & 0x10 != 0 {
            let harm_type = cur.read_u8()?;
            if version >= 500 {
                match harm_type {
                    2 => cur.skip(3)?,
                    3 => cur.skip(1)?,
                    _ => {}
                }
            }
        }
        if ef2 & 0x20 != 0 {
            let _trill_fret = cur.read_u8()?;
            let _trill_period = cur.read_u8()?;
        }
    }

    if is_percussion {
        note.percussion_articulation = Some(map_gp5_percussion(note.fret));
        note.string_idx = None;
    } else if note.is_ghost {
        note.velocity = (note.velocity / 2).max(1);
    }

    Ok((note, grace))
}
