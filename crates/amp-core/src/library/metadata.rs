use std::fs::File;
use std::io::Read;
use std::path::Path;
use sha2::{Digest, Sha256};
use regex::Regex;
use crate::midi::parser::parse_midi_bytes;

pub struct ExtractedMetadata {
    pub title: String,
    pub artist: Option<String>,
    pub duration_ms: i64,
    pub native_bpm: f64,
    pub file_hash: String,
    pub detected_key_offset: Option<i8>,
}

/// Computes SHA-256 hash of a file
pub fn compute_file_hash(path: &Path) -> Result<String, std::io::Error> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = [0u8; 8192];
    loop {
        let count = file.read(&mut buffer)?;
        if count == 0 {
            break;
        }
        hasher.update(&buffer[..count]);
    }
    Ok(format!("{:x}", hasher.finalize()))
}

/// Strips common noise tags from a song title or artist string
pub fn sanitize_tag(text: &str) -> String {
    let re = Regex::new(r"(?i)\s*[\(\[][^()\[\]]*(?:official|audio|video|lyrics?|karaoke|instrumental|midi|mid|mp3|hq|hd|remaster|slowed|reverb|nightcore|sped\s*up|ver\b|version|full)[^()\[\]]*[\)\]]").unwrap();
    let cleaned = re.replace_all(text, "").to_string();
    cleaned.trim().to_string()
}

/// Hybrid filename & embedded MIDI metadata extractor
pub fn extract_metadata(path: &Path) -> Result<ExtractedMetadata, String> {
    let hash = compute_file_hash(path).map_err(|e| format!("Failed to hash file {}: {}", path.display(), e))?;

    let file_stem = path
        .file_stem()
        .and_then(|s| s.to_str())
        .unwrap_or("Unknown Song");

    let clean_stem = sanitize_tag(file_stem);

    // Split on " - " for Title and Artist
    let (mut title, mut artist) = if let Some((part1, part2)) = clean_stem.split_once(" - ") {
        (sanitize_tag(part1), Some(sanitize_tag(part2)))
    } else {
        (clean_stem, None)
    };

    // Read bytes for MIDI inspection
    let bytes = std::fs::read(path).map_err(|e| format!("Failed to read file {}: {}", path.display(), e))?;
    let parsed = parse_midi_bytes(&bytes).map_err(|e| format!("Failed to parse MIDI {}: {}", path.display(), e))?;

    let duration_ms = (parsed.duration_us / 1000) as i64;
    let native_bpm = parsed.bpm;

    // Fall back to track name if title was blank or generic
    if title.is_empty() || title.eq_ignore_ascii_case("untitled") {
        if let Some(track) = parsed.tracks.first() {
            if !track.name.is_empty() {
                title = sanitize_tag(&track.name);
            }
        }
    }

    if title.is_empty() {
        title = "Unknown Song".to_string();
    }

    if let Some(ref a) = artist {
        if a.is_empty() {
            artist = None;
        }
    }

    let key_markers = crate::midi::detect::detect_key_changes(&parsed);
    let detected_key_offset = key_markers.first().map(|m| m.key_offset);

    Ok(ExtractedMetadata {
        title,
        artist,
        duration_ms,
        native_bpm,
        file_hash: hash,
        detected_key_offset,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_sanitize_tag() {
        assert_eq!(sanitize_tag("Cupid (Official Video)"), "Cupid");
        assert_eq!(sanitize_tag("Song Title [HQ Audio]"), "Song Title");
        assert_eq!(sanitize_tag("Track [Remastered] (Lyrics)"), "Track");
    }

    #[test]
    fn test_title_artist_split() {
        let input = "APT - Rosé, Bruno Mars";
        let (t, a) = input.split_once(" - ").unwrap();
        assert_eq!(t, "APT");
        assert_eq!(a, "Rosé, Bruno Mars");
    }
}
