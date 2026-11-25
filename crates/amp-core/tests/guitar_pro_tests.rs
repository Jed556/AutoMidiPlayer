use amp_core::input::chord_wheel::WheelButton;
use amp_core::input::simulator::KeyboardOutput;
use amp_core::player::engine::ArrangementState;
use amp_core::player::NoteProcessingSettings;
use amp_core::{
    extract_metadata, is_guitar_pro_path, is_supported_song_path, parse_midi_bytes,
    parse_midi_file, scan_folder, ControlEventKind, GuitarProBrush, InstrumentConfigJson,
    KeyboardLayout, KeyboardLayoutJson, LibraryDatabase, MidiError, NoteKeyMapping, OutputKey,
    PlaybackEngine, PlaybackStatus, PlayerEvent, StrumDirection,
};
use flate2::write::DeflateEncoder;
use flate2::Compression;
use std::io::Write;
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

struct TempDir(std::path::PathBuf);

impl TempDir {
    fn new(prefix: &str) -> Self {
        let dir = std::env::temp_dir().join(format!("{prefix}_{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&dir).unwrap();
        Self(dir)
    }

    fn path(&self) -> &Path {
        &self.0
    }
}

impl Drop for TempDir {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

struct ZipEntrySpec<'a> {
    name: &'a str,
    data: &'a [u8],
    deflate: bool,
    flags: u16,
}

fn build_zip(entries: &[ZipEntrySpec<'_>]) -> Vec<u8> {
    let mut out = Vec::new();
    struct CdRecord {
        name: Vec<u8>,
        flags: u16,
        method: u16,
        comp_size: u32,
        uncomp_size: u32,
        offset: u32,
    }
    let mut cd = Vec::new();

    for entry in entries {
        let offset = out.len() as u32;
        let (method, payload): (u16, Vec<u8>) = if entry.deflate {
            let mut enc = DeflateEncoder::new(Vec::new(), Compression::default());
            enc.write_all(entry.data).unwrap();
            (8, enc.finish().unwrap())
        } else {
            (0, entry.data.to_vec())
        };
        let name = entry.name.as_bytes().to_vec();
        let comp_size = payload.len() as u32;
        let uncomp_size = entry.data.len() as u32;

        out.extend_from_slice(b"PK\x03\x04");
        out.extend_from_slice(&20u16.to_le_bytes()); // version needed
        out.extend_from_slice(&entry.flags.to_le_bytes());
        out.extend_from_slice(&method.to_le_bytes());
        out.extend_from_slice(&0u16.to_le_bytes()); // mod time
        out.extend_from_slice(&0u16.to_le_bytes()); // mod date
        out.extend_from_slice(&0u32.to_le_bytes()); // crc32 (unused by reader)
        out.extend_from_slice(&comp_size.to_le_bytes());
        out.extend_from_slice(&uncomp_size.to_le_bytes());
        out.extend_from_slice(&(name.len() as u16).to_le_bytes());
        out.extend_from_slice(&0u16.to_le_bytes()); // extra len
        out.extend_from_slice(&name);
        out.extend_from_slice(&payload);

        cd.push(CdRecord {
            name,
            flags: entry.flags,
            method,
            comp_size,
            uncomp_size,
            offset,
        });
    }

    let cd_offset = out.len() as u32;
    for rec in &cd {
        out.extend_from_slice(b"PK\x01\x02");
        out.extend_from_slice(&20u16.to_le_bytes()); // version made by
        out.extend_from_slice(&20u16.to_le_bytes()); // version needed
        out.extend_from_slice(&rec.flags.to_le_bytes());
        out.extend_from_slice(&rec.method.to_le_bytes());
        out.extend_from_slice(&0u16.to_le_bytes()); // mod time
        out.extend_from_slice(&0u16.to_le_bytes()); // mod date
        out.extend_from_slice(&0u32.to_le_bytes()); // crc32
        out.extend_from_slice(&rec.comp_size.to_le_bytes());
        out.extend_from_slice(&rec.uncomp_size.to_le_bytes());
        out.extend_from_slice(&(rec.name.len() as u16).to_le_bytes());
        out.extend_from_slice(&0u16.to_le_bytes()); // extra
        out.extend_from_slice(&0u16.to_le_bytes()); // comment
        out.extend_from_slice(&0u16.to_le_bytes()); // disk start
        out.extend_from_slice(&0u16.to_le_bytes()); // int attrs
        out.extend_from_slice(&0u32.to_le_bytes()); // ext attrs
        out.extend_from_slice(&rec.offset.to_le_bytes());
        out.extend_from_slice(&rec.name);
    }
    let cd_size = (out.len() as u32) - cd_offset;

    out.extend_from_slice(b"PK\x05\x06");
    out.extend_from_slice(&0u16.to_le_bytes()); // disk num
    out.extend_from_slice(&0u16.to_le_bytes()); // cd disk
    out.extend_from_slice(&(cd.len() as u16).to_le_bytes());
    out.extend_from_slice(&(cd.len() as u16).to_le_bytes());
    out.extend_from_slice(&cd_size.to_le_bytes());
    out.extend_from_slice(&cd_offset.to_le_bytes());
    out.extend_from_slice(&0u16.to_le_bytes()); // comment len
    out
}

fn build_bcfs(files: &[(&str, &[u8])]) -> Vec<u8> {
    const SECTOR: usize = 0x1000;
    let mut image = Vec::new();
    image.extend_from_slice(b"BCFS");
    image.resize(4 + SECTOR, 0); // Sector 0 header padding

    for &(name, content) in files {
        let dir_sector = (image.len() - 4) / SECTOR;
        let data_sectors = content.len().div_ceil(SECTOR).max(1);
        image.resize(image.len() + (1 + data_sectors) * SECTOR, 0);

        let dir_off = 4 + dir_sector * SECTOR;
        image[dir_off..dir_off + 4].copy_from_slice(&2i32.to_le_bytes());
        let name_bytes = name.as_bytes();
        let copy_len = name_bytes.len().min(126);
        image[dir_off + 4..dir_off + 4 + copy_len].copy_from_slice(&name_bytes[..copy_len]);
        image[dir_off + 0x8c..dir_off + 0x90]
            .copy_from_slice(&(content.len() as i32).to_le_bytes());

        for s in 0..data_sectors {
            let target_sector = (dir_sector + 1 + s) as i32;
            let ptr_off = dir_off + 0x94 + s * 4;
            image[ptr_off..ptr_off + 4].copy_from_slice(&target_sector.to_le_bytes());
            let data_off = 4 + (target_sector as usize) * SECTOR;
            let src_start = s * SECTOR;
            let src_end = ((s + 1) * SECTOR).min(content.len());
            if src_start < src_end {
                image[data_off..data_off + (src_end - src_start)]
                    .copy_from_slice(&content[src_start..src_end]);
            }
        }
    }
    image
}

struct BitWriter {
    buf: Vec<u8>,
    current: u8,
    bit_pos: u8,
}

impl BitWriter {
    fn new() -> Self {
        Self {
            buf: Vec::new(),
            current: 0,
            bit_pos: 0,
        }
    }

    fn push_bit(&mut self, bit: u32) {
        self.current = (self.current << 1) | ((bit & 1) as u8);
        self.bit_pos += 1;
        if self.bit_pos == 8 {
            self.buf.push(self.current);
            self.current = 0;
            self.bit_pos = 0;
        }
    }

    fn push_bits(&mut self, val: u32, count: u8) {
        for i in (0..count).rev() {
            self.push_bit((val >> i) & 1);
        }
    }

    fn push_bits_reversed(&mut self, val: u32, count: u8) {
        for i in 0..count {
            self.push_bit((val >> i) & 1);
        }
    }

    fn push_byte(&mut self, byte: u8) {
        self.push_bits(u32::from(byte), 8);
    }

    fn finish(mut self) -> Vec<u8> {
        if self.bit_pos > 0 {
            self.current <<= 8 - self.bit_pos;
            self.buf.push(self.current);
        }
        self.buf
    }
}

fn build_bcfz(raw_bcfs: &[u8]) -> Vec<u8> {
    let mut bw = BitWriter::new();
    let mut i = 0usize;
    while i < raw_bcfs.len() {
        // Emit LZSS back-references when repeating zeros at >= 8 bytes into the output stream
        if i >= 8 && raw_bcfs[i..].starts_with(&[0; 8]) && raw_bcfs[i - 8..i] == [0; 8] {
            bw.push_bit(1);
            bw.push_bits(4, 4); // word_size = 4
            bw.push_bits_reversed(8, 4); // offset = 8
            bw.push_bits_reversed(8, 4); // size = 8
            i += 8;
        } else {
            let chunk = (raw_bcfs.len() - i).min(3);
            bw.push_bit(0);
            bw.push_bits_reversed(chunk as u32, 2);
            for b in &raw_bcfs[i..i + chunk] {
                bw.push_byte(*b);
            }
            i += chunk;
        }
    }

    let mut out = Vec::new();
    out.extend_from_slice(b"BCFZ");
    out.extend_from_slice(&(raw_bcfs.len() as i32).to_le_bytes());
    out.extend_from_slice(&bw.finish());
    out
}

fn push_gp_str_byte_len(buf: &mut Vec<u8>, s: &str, fixed_len: usize) {
    let bytes = s.as_bytes();
    let len = bytes.len().min(fixed_len);
    buf.push(len as u8);
    buf.extend_from_slice(&bytes[..len]);
    buf.resize(buf.len() + (fixed_len - len), 0);
}

fn push_gp_str_int_unused(buf: &mut Vec<u8>, s: &str) {
    let bytes = s.as_bytes();
    let len = bytes.len().min(255);
    buf.extend_from_slice(&((len + 1) as i32).to_le_bytes());
    buf.push(len as u8);
    buf.extend_from_slice(&bytes[..len]);
}

fn push_gp_str_int_byte(buf: &mut Vec<u8>, s: &str) {
    let bytes = s.as_bytes();
    let len = bytes.len().min(255);
    buf.extend_from_slice(&((len + 1) as i32).to_le_bytes());
    buf.push(len as u8);
    buf.extend_from_slice(&bytes[..len]);
}

struct GpLegacyBeatSpec<'a> {
    duration: i8,
    chord: Option<&'a str>,
    brush: Option<StrumDirection>,
    tempo_change: Option<i32>,
    strings: &'a [(usize, i8, bool, bool)], // (string_idx 0..6, fret, tie_dest, dead)
}

fn build_gp_legacy_binary(
    version: i32,
    title: &str,
    artist: &str,
    album: &str,
    tempo: i32,
    capo: i32,
    is_percussion: bool,
    beats: &[GpLegacyBeatSpec<'_>],
) -> Vec<u8> {
    let mut b = Vec::new();
    let ver_str = match version {
        300 => "FICHIER GUITAR PRO v3.00",
        406 => "FICHIER GUITAR PRO v4.06",
        500 => "FICHIER GUITAR PRO v5.00",
        510 => "FICHIER GUITAR PRO v5.10",
        _ => panic!("unsupported test version {version}"),
    };
    push_gp_str_byte_len(&mut b, ver_str, 30);
    push_gp_str_int_unused(&mut b, title);
    push_gp_str_int_unused(&mut b, ""); // subtitle
    push_gp_str_int_unused(&mut b, artist);
    push_gp_str_int_unused(&mut b, album);
    push_gp_str_int_unused(&mut b, ""); // words
    if version >= 500 {
        push_gp_str_int_unused(&mut b, ""); // music
    }
    push_gp_str_int_unused(&mut b, ""); // copyright
    push_gp_str_int_unused(&mut b, ""); // tab
    push_gp_str_int_unused(&mut b, ""); // instructions
    b.extend_from_slice(&0i32.to_le_bytes()); // notice lines

    if version < 500 {
        b.push(0); // global triplet feel
    }
    if version >= 400 {
        b.extend_from_slice(&1i32.to_le_bytes()); // lyrics track
        for _ in 0..5 {
            b.extend_from_slice(&0i32.to_le_bytes());
            b.extend_from_slice(&0i32.to_le_bytes());
        }
    }
    if version >= 510 {
        b.resize(b.len() + 19, 0);
    }
    if version >= 500 {
        b.resize(b.len() + 28, 0); // page setup
        b.extend_from_slice(&0i16.to_le_bytes()); // page flags
        for _ in 0..10 {
            push_gp_str_int_byte(&mut b, "");
        }
        push_gp_str_int_byte(&mut b, ""); // tempo label
    }

    b.extend_from_slice(&tempo.to_le_bytes());
    if version >= 510 {
        b.push(0); // hide tempo
    }
    b.extend_from_slice(&0i32.to_le_bytes()); // key_sig
    if version >= 400 {
        b.push(0); // octave
    }

    for ch in 0..64 {
        let prog = if ch == 0 { 25i32 } else { 0i32 };
        b.extend_from_slice(&prog.to_le_bytes());
        b.push(13); // vol (~103)
        b.push(8); // pan (~64)
        b.resize(b.len() + 6, 0);
    }

    if version >= 500 {
        for _ in 0..19 {
            b.extend_from_slice(&(-1i16).to_le_bytes());
        }
        b.resize(b.len() + 4, 0);
    }

    b.extend_from_slice(&1i32.to_le_bytes()); // bar_count = 1
    b.extend_from_slice(&1i32.to_le_bytes()); // track_count = 1

    // MasterBar 0
    b.push(0x01 | 0x02);
    b.push(4);
    b.push(4);
    if version >= 500 {
        b.resize(b.len() + 4, 0); // flags & 0x03 skip
        b.push(0); // alternate_endings
        b.push(0); // triplet_feel
        b.push(0);
    }

    // Track 0
    b.push(if is_percussion { 0x01 } else { 0x00 });
    push_gp_str_byte_len(&mut b, if is_percussion { "Drums" } else { "Guitar" }, 40);
    b.extend_from_slice(&6i32.to_le_bytes()); // 6 strings
    for &pitch in &[64i32, 59, 55, 50, 45, 40, 0] {
        b.extend_from_slice(&pitch.to_le_bytes());
    }
    b.extend_from_slice(&1i32.to_le_bytes()); // port
    b.extend_from_slice(&1i32.to_le_bytes()); // channel idx (1-based)
    b.extend_from_slice(&2i32.to_le_bytes()); // effect channel
    b.extend_from_slice(&24i32.to_le_bytes()); // fret count
    b.extend_from_slice(&capo.to_le_bytes());
    b.extend_from_slice(&0i32.to_le_bytes()); // RGBA
    if version >= 500 {
        b.resize(b.len() + 45, 0);
        if version >= 510 {
            b.resize(b.len() + 4, 0);
            push_gp_str_int_byte(&mut b, "");
            push_gp_str_int_byte(&mut b, "");
        }
    }

    // Bar 0 Track 0
    if version >= 500 {
        b.push(0);
    }
    b.extend_from_slice(&(beats.len() as i32).to_le_bytes());
    for beat in beats {
        let mut flags = 0u8;
        if beat.chord.is_some() {
            flags |= 0x02;
        }
        if beat.brush.is_some() {
            flags |= 0x08;
        }
        if beat.tempo_change.is_some() {
            flags |= 0x10;
        }
        b.push(flags);
        b.push(beat.duration as u8);

        if let Some(chord_name) = beat.chord {
            if version >= 500 {
                b.resize(b.len() + 17, 0);
                push_gp_str_byte_len(&mut b, chord_name, 21);
                b.resize(b.len() + 4 + 4 + 7 * 4 + 1 + 5 + 26, 0);
            } else {
                b.push(1);
                if version >= 400 {
                    b.resize(b.len() + 16, 0);
                    push_gp_str_byte_len(&mut b, chord_name, 21);
                    b.resize(b.len() + 4 + 4 + 7 * 4 + 1 + 5 + 26, 0);
                } else {
                    b.resize(b.len() + 25, 0);
                    push_gp_str_byte_len(&mut b, chord_name, 34);
                    b.resize(b.len() + 4 + 6 * 4 + 36, 0);
                }
            }
        }

        if let Some(dir) = beat.brush {
            b.push(0x40); // brush flag
            if version >= 400 {
                b.push(0); // flags2
            }
            if version < 500 {
                // pre-GP5: down then up
                match dir {
                    StrumDirection::Down => b.extend_from_slice(&[20, 0]),
                    StrumDirection::Up => b.extend_from_slice(&[0, 20]),
                }
            } else {
                // GP5: up then down
                match dir {
                    StrumDirection::Up => b.extend_from_slice(&[20, 0]),
                    StrumDirection::Down => b.extend_from_slice(&[0, 20]),
                }
            }
        }

        if let Some(new_tempo) = beat.tempo_change {
            b.push(-1i8 as u8); // instrument
            if version >= 500 {
                b.resize(b.len() + 16, 0);
            }
            b.extend_from_slice(&[-1i8 as u8; 6]); // vol, bal, ch, rev, pha, tre
            if version >= 500 {
                push_gp_str_int_byte(&mut b, "");
            }
            b.extend_from_slice(&new_tempo.to_le_bytes());
            b.push(0); // tempo dur
            if version >= 510 {
                b.push(0); // hide tempo
            }
            if version >= 400 {
                b.push(0); // flags
            }
            if version >= 500 {
                b.push(-1i8 as u8); // wah
            }
            if version >= 510 {
                push_gp_str_int_byte(&mut b, "");
                push_gp_str_int_byte(&mut b, "");
            }
        }

        let mut string_mask = 0u8;
        for &(s_idx, _, _, _) in beat.strings {
            if s_idx < 7 {
                string_mask |= 1u8 << (6 - s_idx);
            }
        }
        b.push(string_mask);

        for i in (0..=6).rev() {
            if (string_mask & (1 << i)) == 0 {
                continue;
            }
            let s_idx = 6 - i;
            let &(_, fret, tie_dest, dead) = beat
                .strings
                .iter()
                .find(|(idx, _, _, _)| *idx == s_idx)
                .unwrap();
            b.push(0x20 | 0x10); // note type/fret + velocity
            let typ = if tie_dest {
                2
            } else if dead {
                3
            } else {
                1
            };
            b.push(typ);
            b.push(6); // F velocity (95)
            b.push(fret as u8);
            if version >= 500 {
                b.push(0); // flags2
            }
        }

        if version >= 500 {
            b.extend_from_slice(&0i16.to_le_bytes()); // beat flags2
        }
    }

    if version >= 500 {
        b.extend_from_slice(&0i32.to_le_bytes()); // empty voice 1
    }

    b
}

fn minimal_gpif_xml() -> &'static str {
    r#"<?xml version="1.0" encoding="utf-8"?>
<GPIF>
  <Score>
    <Title>Campfire Waltz</Title>
    <Artist>Test Artist</Artist>
    <Album>Test Album</Album>
  </Score>
  <MasterTrack>
    <Tracks>0</Tracks>
    <Anacrusis/>
    <Automations>
      <Automation>
        <Type>Tempo</Type>
        <Bar>0</Bar>
        <Position>0</Position>
        <Value>120 2</Value>
      </Automation>
    </Automations>
  </MasterTrack>
  <Tracks>
    <Track id="0">
      <Name>Steel Guitar</Name>
      <Instrument ref="guitar-steel"/>
      <Sounds>
        <Sound>
          <Path>Stringed/Acoustic</Path>
          <Name>Steel</Name>
          <Role>User</Role>
          <MIDI><Program>25</Program></MIDI>
        </Sound>
      </Sounds>
      <Properties>
        <Property name="Tuning"><Pitches>40 45 50 55 59 64</Pitches></Property>
        <Property name="CapoFret"><Fret>2</Fret></Property>
        <Property name="DiagramCollection">
          <Items>
            <Item id="10" name="G"/>
            <Item id="11" name="Bm"/>
          </Items>
        </Property>
      </Properties>
    </Track>
  </Tracks>
  <MasterBars>
    <MasterBar>
      <Time>2/4</Time>
      <Bars>0</Bars>
    </MasterBar>
    <MasterBar>
      <Time>4/4</Time>
      <Repeat start="true"/>
      <Bars>1</Bars>
    </MasterBar>
    <MasterBar>
      <Time>4/4</Time>
      <AlternateEndings>1</AlternateEndings>
      <Repeat end="true" count="2"/>
      <Bars>2</Bars>
    </MasterBar>
    <MasterBar>
      <Time>4/4</Time>
      <AlternateEndings>2</AlternateEndings>
      <Directions><Target>Fine</Target></Directions>
      <Bars>3</Bars>
    </MasterBar>
    <MasterBar>
      <Time>4/4</Time>
      <Directions><Jump>DaCapoAlFine</Jump></Directions>
      <Bars>4</Bars>
    </MasterBar>
  </MasterBars>
  <Bars>
    <Bar id="0"><Voices>0 -1</Voices></Bar>
    <Bar id="1"><Voices>1 -1</Voices></Bar>
    <Bar id="2"><Voices>2 -1</Voices></Bar>
    <Bar id="3"><Voices>3 -1</Voices></Bar>
    <Bar id="4"><Voices>4 -1</Voices></Bar>
  </Bars>
  <Voices>
    <Voice id="0"><Beats>0</Beats></Voice>
    <Voice id="1"><Beats>1 2</Beats></Voice>
    <Voice id="2"><Beats>3</Beats></Voice>
    <Voice id="3"><Beats>4</Beats></Voice>
    <Voice id="4"><Beats>5</Beats></Voice>
  </Voices>
  <Beats>
    <Beat id="0">
      <Notes>0 1</Notes>
      <Rhythm ref="r-quarter"/>
      <Chord>10</Chord>
      <Properties><Property name="Brush"><Direction>Down</Direction></Property></Properties>
    </Beat>
    <Beat id="1">
      <Notes>2</Notes>
      <Rhythm ref="r-whole"/>
    </Beat>
    <Beat id="2">
      <Notes>3 4</Notes>
      <Rhythm ref="r-quarter"/>
      <Chord>11</Chord>
      <Properties><Property name="Brush"><Direction>Up</Direction></Property></Properties>
    </Beat>
    <Beat id="3">
      <Notes>5</Notes>
      <Rhythm ref="r-quarter"/>
    </Beat>
    <Beat id="4">
      <Notes>6</Notes>
      <Rhythm ref="r-quarter"/>
    </Beat>
    <Beat id="5">
      <Notes>7</Notes>
      <Rhythm ref="r-quarter"/>
    </Beat>
  </Beats>
  <Notes>
    <Note id="0"><Properties><Property name="String"><String>0</String></Property><Property name="Fret"><Fret>0</Fret></Property></Properties></Note>
    <Note id="1"><Properties><Property name="String"><String>5</String></Property><Property name="Fret"><Fret>3</Fret></Property></Properties></Note>
    <Note id="2">
      <Properties>
        <Property name="String"><String>2</String></Property>
        <Property name="Fret"><Fret>0</Fret></Property>
        <Property name="Bended"><Enable/></Property>
        <Property name="BendOriginValue"><Float>0</Float></Property>
        <Property name="BendOriginOffset"><Float>0</Float></Property>
        <Property name="BendDestinationValue"><Float>100</Float></Property>
        <Property name="BendDestinationOffset"><Float>50</Float></Property>
      </Properties>
    </Note>
    <Note id="3"><Tie origin="true"/><Properties><Property name="String"><String>0</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
    <Note id="4"><Properties><Property name="String"><String>1</String></Property><Property name="Fret"><Fret>5</Fret></Property><Property name="Muted"><Enable/></Property></Properties></Note>
    <Note id="5"><Tie destination="true"/><Properties><Property name="String"><String>0</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
    <Note id="6"><Properties><Property name="String"><String>3</String></Property><Property name="Fret"><Fret>2</Fret></Property></Properties></Note>
    <Note id="7"><Properties><Property name="String"><String>4</String></Property><Property name="Fret"><Fret>0</Fret></Property></Properties></Note>
  </Notes>
  <Rhythms>
    <Rhythm id="r-quarter"><NoteValue>Quarter</NoteValue></Rhythm>
    <Rhythm id="r-whole"><NoteValue>Whole</NoteValue></Rhythm>
  </Rhythms>
</GPIF>"#
}

#[test]
fn gp_zip_store_and_deflate_parse_metadata_capo_navigation_ties_dead_notes_and_bends() {
    let xml = minimal_gpif_xml().as_bytes();
    let extra = br#"{"ignored":true}"#;

    let gp_store = build_zip(&[
        ZipEntrySpec {
            name: "Content/Preferences.json",
            data: extra,
            deflate: false,
            flags: 0,
        },
        ZipEntrySpec {
            name: "Content/score.gpif",
            data: xml,
            deflate: false,
            flags: 0,
        },
    ]);
    let gp_deflate = build_zip(&[ZipEntrySpec {
        name: "Content/score.gpif",
        data: xml,
        deflate: true,
        flags: 0,
    }]);

    let parsed_store = parse_midi_bytes(&gp_store).unwrap();
    let parsed_deflate = parse_midi_bytes(&gp_deflate).unwrap();
    assert_eq!(parsed_store.title, "Campfire Waltz");
    assert_eq!(parsed_store.duration_us, parsed_deflate.duration_us);
    assert_eq!(parsed_store.events.len(), parsed_deflate.events.len());

    let gp = parsed_store.guitar_pro.as_ref().unwrap();
    assert_eq!(gp.artist.as_deref(), Some("Test Artist"));
    assert_eq!(gp.album.as_deref(), Some("Test Album"));

    // Beat 0: Anacrusis bar 0 has 1 quarter beat in 2/4 bar (= 500,000 us at 120 BPM).
    // Notes: String 0 (low E 40) + capo 2 + fret 0 = 42, String 5 (high E 64) + capo 2 + fret 3 = 69.
    let first_pitches: Vec<u8> = parsed_store
        .events
        .iter()
        .filter(|e| e.is_note_on && e.time_us == 0)
        .map(|e| e.note)
        .collect();
    assert!(first_pitches.contains(&42) && first_pitches.contains(&69), "{first_pitches:?}");
    assert_eq!(
        gp.brushes,
        vec![
            GuitarProBrush {
                time_us: 0,
                track_index: 0,
                direction: StrumDirection::Down,
                strings: (1 << 0) | (1 << 5),
            },
            GuitarProBrush {
                time_us: 10_500_000,
                track_index: 0,
                direction: StrumDirection::Down,
                strings: (1 << 0) | (1 << 5),
            }
        ]
    );
    assert!(gp.chord_marks.iter().any(|c| c.name == "G" && c.time_us == 0 && c.capo == 2));

    // Beat 1 starts at bar 1 = 500,000 us and holds a whole note on string 2 (D 50 + capo 2 = 52) bent +2 semitones to 54 at halfway.
    let note_ons: Vec<(u64, u8)> = parsed_store
        .events
        .iter()
        .filter(|e| e.is_note_on)
        .map(|e| (e.time_us, e.note))
        .collect();
    assert!(note_ons.iter().any(|&(t, n)| t == 500_000 && n == 52), "{note_ons:?}");
    assert!(note_ons.iter().any(|&(_, n)| n == 54), "discrete bend peak 54 expected: {note_ons:?}");

    // Dead note on Beat 2 (raw string 1 = A 45 + capo 2 + fret 5 = 52) must be omitted at 2,500,000 us.
    assert!(
        !note_ons.iter().any(|&(t, n)| t == 2_500_000 && n == 52),
        "dead muted note on beat 2 should not emit MIDI note-on: {note_ons:?}"
    );
    // Bar 1 Beat 2 (47 = low E 40 + capo 2 + fret 5) plays on initial pass (2,500,000), repeat pass (6,500,000),
    // and DaCapoAlFine pass (13,000,000), without re-attacking on Bar 2's tie destination.
    let tie_restarts: Vec<u64> = parsed_store
        .events
        .iter()
        .filter(|e| e.is_note_on && e.note == 47)
        .map(|e| e.time_us)
        .collect();
    assert_eq!(tie_restarts, vec![2_500_000, 6_500_000, 13_000_000]);

    // DaCapoAlFine stops at Fine (bar 3), so bar 4 (pitch string 4 = 59+2 = 61? GPIF raw 4 -> string_idx 1 -> 59+2=61, Wait raw 4 in reverse 40 45 50 55 59 64: raw 4 is 59) occurs ONLY once during initial pass (not after Fine!).
    let after_fine_count = parsed_store
        .events
        .iter()
        .filter(|e| e.is_note_on && e.note == 61)
        .count();
    assert_eq!(after_fine_count, 1, "bar after Fine should play only before DaCapo jump");
}

#[test]
fn gpx_bcfs_and_bcfz_with_lzss_backrefs_roundtrip_identically() {
    let xml = minimal_gpif_xml().as_bytes();
    let bcfs = build_bcfs(&[("Preview.txt", b"ignored"), ("Content/score.gpif", xml)]);
    let bcfz = build_bcfz(&bcfs);

    let from_bcfs = parse_midi_bytes(&bcfs).unwrap();
    let from_bcfz = parse_midi_bytes(&bcfz).unwrap();

    assert_eq!(from_bcfs.title, "Campfire Waltz");
    assert_eq!(from_bcfs.guitar_pro, from_bcfz.guitar_pro);
    assert_eq!(from_bcfs.duration_us, from_bcfz.duration_us);
    assert_eq!(from_bcfs.events.len(), from_bcfz.events.len());
}

#[test]
fn gp3_gp4_and_gp5_binary_versions_parse_brushes_tempos_and_percussion() {
    for (ver, expected_dir_1, expected_dir_2) in [
        (300, StrumDirection::Down, StrumDirection::Up),
        (406, StrumDirection::Down, StrumDirection::Up),
        (500, StrumDirection::Up, StrumDirection::Down),
        (510, StrumDirection::Up, StrumDirection::Down),
    ] {
        let gp_bytes = build_gp_legacy_binary(
            ver,
            "Legacy Tab",
            "Legacy Author",
            "Legacy LP",
            120,
            2,
            false,
            &[
                GpLegacyBeatSpec {
                    duration: 0,
                    chord: Some("G"),
                    brush: Some(expected_dir_1),
                    tempo_change: None,
                    strings: &[(0, 3, false, false), (5, 3, false, false)],
                },
                GpLegacyBeatSpec {
                    duration: 0,
                    chord: None,
                    brush: Some(expected_dir_2),
                    tempo_change: Some(150),
                    strings: &[(0, 3, true, false), (1, 2, false, true), (5, 0, false, false)],
                },
            ],
        );
        let parsed = parse_midi_bytes(&gp_bytes).unwrap();
        assert_eq!(parsed.title, "Legacy Tab");
        assert_eq!(parsed.bpm, 120.0);
        let meta = parsed.guitar_pro.as_ref().unwrap();
        assert_eq!(meta.artist.as_deref(), Some("Legacy Author"));
        assert_eq!(meta.album.as_deref(), Some("Legacy LP"));
        assert!(meta.chord_marks.iter().any(|c| c.name == "G" && c.capo == 2));
        // Beat 1 had a tie destination (`brushed_has_tie == true`), so only Beat 0 registers in `brushes`.
        assert_eq!(meta.brushes.len(), 1);
        assert_eq!(meta.brushes[0].direction, expected_dir_1);

        // Verify reverse brush direction when Beat 0 uses `expected_dir_2` without ties.
        let gp_rev = build_gp_legacy_binary(
            ver,
            "Legacy Tab 2",
            "",
            "",
            100,
            0,
            false,
            &[GpLegacyBeatSpec {
                duration: 0,
                chord: None,
                brush: Some(expected_dir_2),
                tempo_change: Some(140),
                strings: &[(0, 0, false, false), (1, 0, false, false)],
            }],
        );
        let parsed_rev = parse_midi_bytes(&gp_rev).unwrap();
        assert_eq!(
            parsed_rev.guitar_pro.as_ref().unwrap().brushes[0].direction,
            expected_dir_2
        );
    }

    // Percussion flag tracks channel 9 and Drum Kit track metadata.
    let drums = build_gp_legacy_binary(
        500,
        "Drum Groove",
        "Drummer",
        "",
        120,
        0,
        true,
        &[GpLegacyBeatSpec {
            duration: 0,
            chord: None,
            brush: None,
            tempo_change: None,
            strings: &[(0, 36, false, false), (1, 38, false, false)],
        }],
    );
    let parsed_drums = parse_midi_bytes(&drums).unwrap();
    assert!(parsed_drums
        .events
        .iter()
        .all(|e| e.channel == 9 && (!e.is_note_on || e.note == 36 || e.note == 38)));
    assert!(parsed_drums
        .control_events
        .iter()
        .all(|c| !matches!(c.kind, ControlEventKind::ProgramChange { .. })));
}

#[test]
fn corrupt_or_invalid_guitar_pro_inputs_return_guitar_pro_errors() {
    assert!(matches!(
        parse_midi_bytes(b"BCFZ\x10\x00\x00\x00\xFF\xFF"),
        Err(MidiError::GuitarPro(_))
    ));
    let missing_gpif_zip = build_zip(&[ZipEntrySpec {
        name: "Content/Other.xml",
        data: b"<root/>",
        deflate: false,
        flags: 0,
    }]);
    assert!(matches!(
        parse_midi_bytes(&missing_gpif_zip),
        Err(MidiError::GuitarPro(_))
    ));
    let encrypted_zip = build_zip(&[ZipEntrySpec {
        name: "Content/score.gpif",
        data: minimal_gpif_xml().as_bytes(),
        deflate: false,
        flags: 0x0001,
    }]);
    assert!(matches!(
        parse_midi_bytes(&encrypted_zip),
        Err(MidiError::GuitarPro(_))
    ));
    let truncated_gp5 = &build_gp_legacy_binary(
        500,
        "Trunc",
        "",
        "",
        120,
        0,
        false,
        &[],
    )[..40];
    assert!(matches!(
        parse_midi_bytes(truncated_gp5),
        Err(MidiError::GuitarPro(_))
    ));
}

#[derive(Clone, Debug, PartialEq, Eq)]
enum WheelInput {
    Key(OutputKey, bool),
    Move([u16; 2]),
    Button(WheelButton, bool),
}

struct MockOutput {
    events: Arc<Mutex<Vec<WheelInput>>>,
    focused: Arc<AtomicBool>,
}

impl KeyboardOutput for MockOutput {
    fn key(&mut self, key: OutputKey, up: bool) -> Result<(), String> {
        self.events.lock().unwrap().push(WheelInput::Key(key, up));
        Ok(())
    }

    fn wheel_move(&mut self, _processes: &[String], point: [u16; 2]) -> Result<(), String> {
        self.events.lock().unwrap().push(WheelInput::Move(point));
        Ok(())
    }

    fn wheel_button(&mut self, button: WheelButton, up: bool) -> Result<(), String> {
        self.events
            .lock()
            .unwrap()
            .push(WheelInput::Button(button, up));
        Ok(())
    }

    fn wheel_focused(&self) -> bool {
        self.focused.load(Ordering::Acquire)
    }
}

fn nte_guitar_mapping(with_frets: bool) -> (NoteKeyMapping, KeyboardLayoutJson) {
    let instrument: InstrumentConfigJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/Games/Neverness to Everness/Instruments/Strings/Guitar.json"
    ))
    .unwrap();
    let chord_layout: KeyboardLayoutJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Chord_Wheel.json"
    ))
    .unwrap();
    let fret_layout: KeyboardLayoutJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Fret_Wheel.json"
    ))
    .unwrap();
    let game: amp_core::GameConfig = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/Games/Neverness to Everness/game.json"
    ))
    .unwrap();
    let base = NoteKeyMapping::from_pairs(KeyboardLayout::Chromatic36, &[]);
    let mapping = if with_frets {
        base.with_guitar_chords(&instrument, &chord_layout, &fret_layout, true)
    } else {
        base.with_chord_pads(&instrument, &chord_layout)
    }
    .with_game_target(&game);
    (mapping, chord_layout)
}

fn build_gp_for_xml(xml: &str) -> Vec<u8> {
    build_zip(&[ZipEntrySpec {
        name: "Content/score.gpif",
        data: xml.as_bytes(),
        deflate: true,
        flags: 0,
    }])
}

#[test]
fn nte_guitar_dispatches_right_and_left_strums_and_protects_bm_unknown_string() {
    // Score 1: Down-brushed G chord at beat 0, Up-brushed G chord at beat 2 (1,000,000 us).
    let gp_strums = parse_midi_bytes(&build_gp_for_xml(
        r#"<GPIF>
  <Score><Title>Directional Strums</Title></Score>
  <MasterTrack><Tracks>0</Tracks><Automations><Automation><Type>Tempo</Type><Bar>0</Bar><Position>0</Position><Value>120 2</Value></Automation></Automations></MasterTrack>
  <Tracks><Track id="0"><Properties>
    <Property name="Tuning"><Pitches>40 45 50 55 59 64</Pitches></Property>
    <Property name="DiagramCollection"><Items><Item id="1" name="G"/></Items></Property>
  </Properties></Track></Tracks>
  <MasterBars><MasterBar><Time>4/4</Time><Bars>0</Bars></MasterBar></MasterBars>
  <Bars><Bar id="0"><Voices>0 -1</Voices></Bar></Bars>
  <Voices><Voice id="0"><Beats>0 1 2</Beats></Voice></Voices>
  <Beats>
    <Beat id="0"><Notes>0 1 2</Notes><Rhythm ref="q"/><Chord>1</Chord><Properties><Property name="Brush"><Direction>Down</Direction></Property></Properties></Beat>
    <Beat id="1"><Rhythm ref="q"/></Beat>
    <Beat id="2"><Notes>0 1 2</Notes><Rhythm ref="q"/><Chord>1</Chord><Properties><Property name="Brush"><Direction>Up</Direction></Property></Properties></Beat>
  </Beats>
  <Notes>
    <Note id="0"><Properties><Property name="String"><String>0</String></Property><Property name="Fret"><Fret>3</Fret></Property></Properties></Note>
    <Note id="1"><Properties><Property name="String"><String>2</String></Property><Property name="Fret"><Fret>0</Fret></Property></Properties></Note>
    <Note id="2"><Properties><Property name="String"><String>5</String></Property><Property name="Fret"><Fret>3</Fret></Property></Properties></Note>
  </Notes>
  <Rhythms><Rhythm id="q"><NoteValue>Quarter</NoteValue></Rhythm></Rhythms>
</GPIF>"#,
    ))
    .unwrap();

    let (mapping, chord_layout) = nte_guitar_mapping(false);
    let strum_pos = chord_layout.chord_wheel.as_ref().unwrap().strum_position;
    let events = Arc::new(Mutex::new(Vec::new()));
    let arrangements = Arc::new(Mutex::new(Vec::<ArrangementState>::new()));
    let cap_arr = arrangements.clone();
    let engine = PlaybackEngine::new(Some(gp_strums), mapping, true, None, move |ev| {
        if let PlayerEvent::Arrangement(state) = ev {
            cap_arr.lock().unwrap().push(state);
        }
    });
    engine.set_keyboard_output(Some(Box::new(MockOutput {
        events: events.clone(),
        focused: Arc::new(AtomicBool::new(true)),
    })));
    engine.set_listen_mode(false);
    engine.set_hold_delay_ms(20);
    engine.set_note_processing(NoteProcessingSettings {
        merge_notes: false,
        merge_tolerance_ms: 25,
        chord_detection_milliseconds: 30,
    });
    engine.flush().unwrap();
    engine.request_play().recv_timeout(Duration::from_secs(5)).unwrap().unwrap();
    let deadline = Instant::now() + Duration::from_secs(5);
    while engine.get_status() != PlaybackStatus::Stopped {
        assert!(Instant::now() < deadline, "timed out playing directional strums");
        std::thread::sleep(Duration::from_millis(5));
    }

    let recorded = events.lock().unwrap().clone();
    assert!(
        recorded
            .windows(2)
            .any(|w| w[0] == WheelInput::Move(strum_pos) && w[1] == WheelInput::Button(WheelButton::Right, false)),
        "expected down-strum (Right button) at strum position: {recorded:?}"
    );
    assert!(
        recorded
            .windows(2)
            .any(|w| w[0] == WheelInput::Move(strum_pos) && w[1] == WheelInput::Button(WheelButton::Left, false)),
        "expected up-strum (Left button) at strum position: {recorded:?}"
    );
    let strums: Vec<Option<&'static str>> = arrangements
        .lock()
        .unwrap()
        .iter()
        .filter(|a| a.slot.is_some())
        .map(|a| a.strum)
        .collect();
    assert_eq!(strums, vec![Some("down"), Some("up")]);

    // Score 2: Bm triad [47, 62, 66] (B2, D4, F#4) at Fret 7 with Bm chord mark and Brush Down.
    // Because NTE Guitar.json Bm slot 2 has string 2 set to null (missing D4), with Frets enabled
    // the player skips full-strumming Bm and cleanly plays all three notes on Fret 7 instead.
    let gp_bm_common_fret = parse_midi_bytes(&build_gp_for_xml(
        r#"<GPIF>
  <Score><Title>Bm Common Fret</Title></Score>
  <MasterTrack><Tracks>0</Tracks><Automations><Automation><Type>Tempo</Type><Bar>0</Bar><Position>0</Position><Value>120 2</Value></Automation></Automations></MasterTrack>
  <Tracks><Track id="0"><Properties>
    <Property name="Tuning"><Pitches>40 45 50 55 59 64</Pitches></Property>
    <Property name="DiagramCollection"><Items><Item id="2" name="Bm"/></Items></Property>
  </Properties></Track></Tracks>
  <MasterBars><MasterBar><Time>4/4</Time><Bars>0</Bars></MasterBar></MasterBars>
  <Bars><Bar id="0"><Voices>0 -1</Voices></Bar></Bars>
  <Voices><Voice id="0"><Beats>0</Beats></Voice></Voices>
  <Beats>
    <Beat id="0"><Notes>0 1 2</Notes><Rhythm ref="q"/><Chord>2</Chord><Properties><Property name="Brush"><Direction>Down</Direction></Property></Properties></Beat>
  </Beats>
  <Notes>
    <Note id="0"><Properties><Property name="String"><String>0</String></Property><Property name="Fret"><Fret>7</Fret></Property></Properties></Note>
    <Note id="1"><Properties><Property name="String"><String>3</String></Property><Property name="Fret"><Fret>7</Fret></Property></Properties></Note>
    <Note id="2"><Properties><Property name="String"><String>4</String></Property><Property name="Fret"><Fret>7</Fret></Property></Properties></Note>
  </Notes>
  <Rhythms><Rhythm id="q"><NoteValue>Quarter</NoteValue></Rhythm></Rhythms>
</GPIF>"#,
    ))
    .unwrap();

    // With Frets enabled: Bm slot 2 has `chord_has_unknown_string(2) == true`, so full-strum of Bm is skipped and Fret 7 plucks [47, 62, 66]!
    let (mixed_mapping, _) = nte_guitar_mapping(true);
    let mixed_arr = Arc::new(Mutex::new(Vec::<ArrangementState>::new()));
    let cap_mixed = mixed_arr.clone();
    let engine_mixed = PlaybackEngine::new(
        Some(gp_bm_common_fret.clone()),
        mixed_mapping,
        true,
        None,
        move |ev| {
            if let PlayerEvent::Arrangement(s) = ev {
                cap_mixed.lock().unwrap().push(s);
            }
        },
    );
    engine_mixed.set_keyboard_output(Some(Box::new(MockOutput {
        events: Arc::new(Mutex::new(Vec::new())),
        focused: Arc::new(AtomicBool::new(true)),
    })));
    engine_mixed.set_listen_mode(false);
    engine_mixed.sync_guitar_mode(false).unwrap();
    engine_mixed.flush().unwrap();
    engine_mixed.request_play().recv_timeout(Duration::from_secs(5)).unwrap().unwrap();
    while engine_mixed.get_status() != PlaybackStatus::Stopped {
        std::thread::sleep(Duration::from_millis(5));
    }
    let mixed_played: Vec<ArrangementState> = mixed_arr
        .lock()
        .unwrap()
        .iter()
        .filter(|s| s.slot.is_some())
        .cloned()
        .collect();
    assert_eq!(mixed_played.len(), 1);
    assert_eq!(mixed_played[0].mode, Some("frets"));
    assert_ne!(mixed_played[0].slot, Some(2));

    // Without Frets (`chords`-only mode): Bm chord mark still falls back to slot 2 so notes aren't dropped!
    let (chords_only, _) = nte_guitar_mapping(false);
    let chords_arr = Arc::new(Mutex::new(Vec::<ArrangementState>::new()));
    let cap_chords = chords_arr.clone();
    let engine_chords = PlaybackEngine::new(Some(gp_bm_common_fret), chords_only, true, None, move |ev| {
        if let PlayerEvent::Arrangement(s) = ev {
            cap_chords.lock().unwrap().push(s);
        }
    });
    engine_chords.set_keyboard_output(Some(Box::new(MockOutput {
        events: Arc::new(Mutex::new(Vec::new())),
        focused: Arc::new(AtomicBool::new(true)),
    })));
    engine_chords.set_listen_mode(false);
    engine_chords.flush().unwrap();
    engine_chords.request_play().recv_timeout(Duration::from_secs(5)).unwrap().unwrap();
    while engine_chords.get_status() != PlaybackStatus::Stopped {
        std::thread::sleep(Duration::from_millis(5));
    }
    let chords_played: Vec<ArrangementState> = chords_arr
        .lock()
        .unwrap()
        .iter()
        .filter(|s| s.slot.is_some())
        .cloned()
        .collect();
    assert_eq!(chords_played.len(), 1);
    assert_eq!(chords_played[0].mode, Some("chords"));
    assert_eq!(chords_played[0].slot, Some(2));
}

#[test]
fn nte_guitar_dispatches_fret_mode_brushes_and_program_timbre_switches() {
    let gp_fret_brushes = parse_midi_bytes(&build_gp_for_xml(
        r#"<GPIF>
  <Score><Title>Fret Strums and Timbre</Title></Score>
  <MasterTrack><Tracks>0</Tracks><Automations><Automation><Type>Tempo</Type><Bar>0</Bar><Position>0</Position><Value>120 2</Value></Automation></Automations></MasterTrack>
  <Tracks><Track id="0">
    <GeneralMidi><Program>27</Program><Volume>100</Volume><Balance>64</Balance></GeneralMidi>
    <Properties><Property name="Tuning"><Pitches>40 45 50 55 59 64</Pitches></Property></Properties>
  </Track></Tracks>
  <MasterBars><MasterBar><Time>4/4</Time><Bars>0</Bars></MasterBar><MasterBar><Time>4/4</Time><Bars>1</Bars></MasterBar></MasterBars>
  <Bars><Bar id="0"><Voices>0 -1</Voices></Bar><Bar id="1"><Voices>1 -1</Voices></Bar></Bars>
  <Voices><Voice id="0"><Beats>0 1</Beats></Voice><Voice id="1"><Beats>2</Beats></Voice></Voices>
  <Beats>
    <Beat id="0"><Notes>0 1 2 3 4 5</Notes><Rhythm ref="h"/><Properties><Property name="Brush"><Direction>Down</Direction></Property></Properties></Beat>
    <Beat id="1"><Rhythm ref="h"/></Beat>
    <Beat id="2"><Notes>0 1 2 3 4 5</Notes><Rhythm ref="q"/><Properties><Property name="Brush"><Direction>Up</Direction></Property></Properties></Beat>
  </Beats>
  <Notes>
    <Note id="0"><Properties><Property name="String"><String>0</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
    <Note id="1"><Properties><Property name="String"><String>1</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
    <Note id="2"><Properties><Property name="String"><String>2</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
    <Note id="3"><Properties><Property name="String"><String>3</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
    <Note id="4"><Properties><Property name="String"><String>4</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
    <Note id="5"><Properties><Property name="String"><String>5</String></Property><Property name="Fret"><Fret>5</Fret></Property></Properties></Note>
  </Notes>
  <Rhythms><Rhythm id="q"><NoteValue>Quarter</NoteValue></Rhythm><Rhythm id="h"><NoteValue>Half</NoteValue></Rhythm></Rhythms>
</GPIF>"#,
    ))
    .unwrap();

    let (mut gp_with_distortion, mut chord_layout, fret_layout) = (
        gp_fret_brushes,
        serde_json::from_str::<KeyboardLayoutJson>(include_str!(
            "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Chord_Wheel.json"
        ))
        .unwrap(),
        serde_json::from_str::<KeyboardLayoutJson>(include_str!(
            "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Fret_Wheel.json"
        ))
        .unwrap(),
    );
    gp_with_distortion.control_events.push(amp_core::TimedControlEvent {
        time_us: 1_200_000,
        channel: 0,
        kind: amp_core::ControlEventKind::ProgramChange { program: 30 },
    });
    let instrument: InstrumentConfigJson = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/Games/Neverness to Everness/Instruments/Strings/Guitar.json"
    ))
    .unwrap();
    let game: amp_core::GameConfig = serde_json::from_str(include_str!(
        "../../../src-tauri/resources/defaults/Games/Neverness to Everness/game.json"
    ))
    .unwrap();
    chord_layout.chord_wheel.as_mut().unwrap().timbre_key = Some("v".into());
    let mapping = NoteKeyMapping::from_pairs(KeyboardLayout::Chromatic36, &[])
        .with_frets(&instrument, &fret_layout, chord_layout.chord_wheel.as_ref().unwrap())
        .with_game_target(&game);

    let strum_pos = chord_layout.chord_wheel.as_ref().unwrap().strum_position;
    let events = Arc::new(Mutex::new(Vec::new()));
    let arrangements = Arc::new(Mutex::new(Vec::<ArrangementState>::new()));
    let cap_arr = arrangements.clone();
    let engine = PlaybackEngine::new(Some(gp_with_distortion), mapping, true, None, move |ev| {
        if let PlayerEvent::Arrangement(state) = ev {
            cap_arr.lock().unwrap().push(state);
        }
    });
    engine.set_keyboard_output(Some(Box::new(MockOutput {
        events: events.clone(),
        focused: Arc::new(AtomicBool::new(true)),
    })));
    engine.set_listen_mode(false);
    engine.set_hold_delay_ms(20);
    engine.set_note_processing(NoteProcessingSettings {
        merge_notes: false,
        merge_tolerance_ms: 25,
        chord_detection_milliseconds: 30,
    });
    engine.flush().unwrap();
    engine.request_play().recv_timeout(Duration::from_secs(5)).unwrap().unwrap();
    let deadline = Instant::now() + Duration::from_secs(5);
    while engine.get_status() != PlaybackStatus::Stopped {
        assert!(Instant::now() < deadline, "timed out playing fret brushes");
        std::thread::sleep(Duration::from_millis(5));
    }

    let recorded = events.lock().unwrap().clone();
    assert!(recorded.windows(2).any(|w| w[0] == WheelInput::Move(strum_pos) && w[1] == WheelInput::Button(WheelButton::Right, false)));
    assert!(recorded.windows(2).any(|w| w[0] == WheelInput::Move(strum_pos) && w[1] == WheelInput::Button(WheelButton::Left, false)));
    let v_key = amp_core::parse_key_stroke("v").unwrap().key;
    assert_eq!(recorded.iter().filter(|e| **e == WheelInput::Key(v_key, false)).count(), 1);
    let attacks: Vec<_> = arrangements
        .lock()
        .unwrap()
        .iter()
        .filter(|a| a.slot.is_some())
        .map(|a| (a.mode, a.slot, a.strum, a.timbre))
        .collect();
    assert_eq!(
        attacks,
        vec![
            (Some("frets"), Some(4), Some("down"), Some("clean")),
            (Some("frets"), Some(4), Some("up"), Some("distortion")),
        ]
    );
}

#[test]
fn library_scanner_and_metadata_support_guitar_pro_files_alongside_midi() {
    let tmp = TempDir::new("amp_gp_scan");
    let gp_path = tmp.path().join("campfire_tab.gp");
    let gp5_path = tmp.path().join("legacy_tab.GP5");
    let txt_path = tmp.path().join("notes.txt");

    std::fs::write(
        &gp_path,
        build_zip(&[ZipEntrySpec {
            name: "Content/score.gpif",
            data: minimal_gpif_xml().as_bytes(),
            deflate: true,
            flags: 0,
        }]),
    )
    .unwrap();
    std::fs::write(
        &gp5_path,
        build_gp_legacy_binary(
            500,
            "Legacy Anthem",
            "Composer",
            "",
            120,
            0,
            false,
            &[GpLegacyBeatSpec {
                duration: 0,
                chord: None,
                brush: None,
                tempo_change: None,
                strings: &[(0, 0, false, false)],
            }],
        ),
    )
    .unwrap();
    std::fs::write(&txt_path, b"not a song").unwrap();

    assert!(is_guitar_pro_path(&gp_path));
    assert!(is_guitar_pro_path(&gp5_path));
    assert!(is_supported_song_path(&gp_path));
    assert!(!is_supported_song_path(&txt_path));

    let parsed = parse_midi_file(&gp_path).unwrap();
    assert_eq!(parsed.title, "Campfire Waltz");

    let meta = extract_metadata(&gp_path).unwrap();
    assert_eq!(meta.title, "Campfire Waltz");
    assert_eq!(meta.artist.as_deref(), Some("Test Artist"));

    let db_path = tmp.path().join("library.db");
    let db = LibraryDatabase::new(Some(db_path)).unwrap();
    let summary = scan_folder(&db, tmp.path(), None).unwrap();
    assert_eq!(summary.added, 2);
    assert_eq!(summary.skipped, 0);

    let songs = db.get_all_songs().unwrap();
    assert_eq!(songs.len(), 2);
    let titles: Vec<Option<String>> = songs.iter().map(|s| s.title.clone()).collect();
    assert!(titles.contains(&Some("Campfire Waltz".to_string())), "{titles:?}");
    assert!(titles.contains(&Some("Legacy Anthem".to_string())), "{titles:?}");
}
