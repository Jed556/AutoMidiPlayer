mod convert;
mod gp3_5;
mod gpif;
mod gpx;
pub mod model;
mod zip_gp;

use std::path::Path;

pub use model::{GuitarProBrush, GuitarProChordMark, GuitarProMeta};

use super::parser::{MidiError, ParsedMidi};

pub const MAX_INPUT_BYTES: usize = 64_000_000;
pub const MAX_GPIF_BYTES: usize = 12_000_000;
pub const MAX_MASTER_BARS: usize = 25_000;
pub const MAX_EXPANDED_BARS: usize = 25_000;
pub const MAX_TOTAL_NOTES: usize = 150_000;
pub const GP_PPQN: u16 = 960;

pub fn is_guitar_pro_extension(ext: &str) -> bool {
    matches!(
        ext.to_ascii_lowercase().as_str(),
        "gp" | "gpx" | "gp5" | "gp4" | "gp3"
    )
}

pub fn is_guitar_pro_bytes(bytes: &[u8]) -> bool {
    bytes.starts_with(b"BCFZ")
        || bytes.starts_with(b"BCFS")
        || bytes.starts_with(b"PK\x03\x04")
        || bytes
            .get(1..20)
            .is_some_and(|hdr| hdr.eq_ignore_ascii_case(b"FICHIER GUITAR PRO "))
}

pub fn parse_guitar_pro_bytes(
    bytes: &[u8],
    fallback_title: Option<&str>,
) -> Result<ParsedMidi, MidiError> {
    if bytes.len() > MAX_INPUT_BYTES {
        return Err(MidiError::GuitarPro(
            "Guitar Pro file exceeds the 64 MB size limit".into(),
        ));
    }

    let score = if bytes.starts_with(b"PK\x03\x04") {
        let xml = zip_gp::extract_score_gpif_from_zip(bytes).map_err(MidiError::GuitarPro)?;
        gpif::parse_gpif_xml(&xml).map_err(MidiError::GuitarPro)?
    } else if bytes.starts_with(b"BCFZ") || bytes.starts_with(b"BCFS") {
        let xml = gpx::extract_score_gpif_from_gpx(bytes).map_err(MidiError::GuitarPro)?;
        gpif::parse_gpif_xml(&xml).map_err(MidiError::GuitarPro)?
    } else if bytes
        .get(1..20)
        .is_some_and(|hdr| hdr.eq_ignore_ascii_case(b"FICHIER GUITAR PRO "))
    {
        gp3_5::parse_gp3_to_5(bytes).map_err(MidiError::GuitarPro)?
    } else {
        return Err(MidiError::GuitarPro(
            "Unsupported or corrupted Guitar Pro file header".into(),
        ));
    };

    convert::gp_score_to_parsed_midi(&score, fallback_title).map_err(MidiError::GuitarPro)
}

pub fn parse_guitar_pro_file(path: &Path) -> Result<ParsedMidi, MidiError> {
    let meta = std::fs::metadata(path)?;
    if meta.len() > MAX_INPUT_BYTES as u64 {
        return Err(MidiError::GuitarPro(
            "Guitar Pro file exceeds the 64 MB size limit".into(),
        ));
    }
    let bytes = std::fs::read(path)?;
    let stem = path.file_stem().and_then(|s| s.to_str());
    parse_guitar_pro_bytes(&bytes, stem)
}
