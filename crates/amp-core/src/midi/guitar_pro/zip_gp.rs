use std::io::Read;
use flate2::read::DeflateDecoder;
use super::MAX_GPIF_BYTES;

const LOCAL_FILE_HEADER_SIG: [u8; 4] = *b"PK\x03\x04";
const CENTRAL_DIR_HEADER_SIG: [u8; 4] = *b"PK\x01\x02";
const EOCD_SIG: [u8; 4] = *b"PK\x05\x06";

fn read_u16(data: &[u8], at: usize) -> Option<u16> {
    data.get(at..at + 2)?.try_into().ok().map(u16::from_le_bytes)
}

fn read_u32(data: &[u8], at: usize) -> Option<u32> {
    data.get(at..at + 4)?.try_into().ok().map(u32::from_le_bytes)
}

fn find_eocd(data: &[u8]) -> Result<usize, String> {
    if data.len() < 22 {
        return Err("Invalid .gp ZIP archive (too small)".into());
    }
    let search_start = data.len().saturating_sub(22 + 65_535);
    for pos in (search_start..=data.len() - 22).rev() {
        if data[pos..pos + 4] == EOCD_SIG {
            let comment_len = read_u16(data, pos + 20).unwrap_or(0) as usize;
            if pos + 22 + comment_len == data.len() {
                return Ok(pos);
            }
        }
    }
    Err("Missing ZIP End of Central Directory in .gp file".into())
}

fn is_score_gpif_name(raw_name: &[u8]) -> bool {
    let name = String::from_utf8_lossy(raw_name);
    let normalized = name.replace('\\', "/");
    normalized
        .rsplit('/')
        .next()
        .is_some_and(|base| base.eq_ignore_ascii_case("score.gpif"))
}

struct ZipScoreEntry {
    method: u16,
    flags: u16,
    compressed_size: usize,
    uncompressed_size: usize,
    local_header_offset: usize,
}

pub(super) fn extract_score_gpif_from_zip(data: &[u8]) -> Result<String, String> {
    let eocd = find_eocd(data)?;
    let total_entries = read_u16(data, eocd + 10)
        .ok_or("Invalid ZIP central directory count")? as usize;
    let cd_size = read_u32(data, eocd + 12)
        .ok_or("Invalid ZIP central directory size")? as usize;
    let cd_offset = read_u32(data, eocd + 16)
        .ok_or("Invalid ZIP central directory offset")? as usize;

    let cd_end = cd_offset
        .checked_add(cd_size)
        .filter(|&end| end <= data.len())
        .ok_or("Invalid ZIP central directory bounds in .gp file")?;

    let mut cursor = cd_offset;
    let mut matches = Vec::new();
    for _ in 0..total_entries {
        if cursor + 46 > cd_end || data[cursor..cursor + 4] != CENTRAL_DIR_HEADER_SIG {
            return Err("Corrupt ZIP central directory entry in .gp file".into());
        }
        let flags = read_u16(data, cursor + 8).ok_or("Invalid ZIP flags")?;
        let method = read_u16(data, cursor + 10).ok_or("Invalid ZIP method")?;
        let compressed_size = read_u32(data, cursor + 20).ok_or("Invalid ZIP comp size")?;
        let uncompressed_size = read_u32(data, cursor + 24).ok_or("Invalid ZIP uncomp size")?;
        let fname_len = read_u16(data, cursor + 28).ok_or("Invalid ZIP name length")? as usize;
        let extra_len = read_u16(data, cursor + 30).ok_or("Invalid ZIP extra length")? as usize;
        let comment_len = read_u16(data, cursor + 32).ok_or("Invalid ZIP comment length")? as usize;
        let local_header_offset = read_u32(data, cursor + 42).ok_or("Invalid ZIP local offset")?;

        let name_start = cursor + 46;
        let next = name_start
            .checked_add(fname_len)
            .and_then(|v| v.checked_add(extra_len))
            .and_then(|v| v.checked_add(comment_len))
            .filter(|&end| end <= cd_end)
            .ok_or("Corrupt ZIP central directory bounds")?;

        let raw_name = &data[name_start..name_start + fname_len];
        if is_score_gpif_name(raw_name) {
            if compressed_size == u32::MAX
                || uncompressed_size == u32::MAX
                || local_header_offset == u32::MAX
            {
                return Err("Unsupported ZIP64 score entry in .gp file".into());
            }
            matches.push(ZipScoreEntry {
                method,
                flags,
                compressed_size: compressed_size as usize,
                uncompressed_size: uncompressed_size as usize,
                local_header_offset: local_header_offset as usize,
            });
        }
        cursor = next;
    }

    if matches.len() != 1 {
        return Err("Missing unique score.gpif notation entry inside .gp archive".into());
    }
    let entry = matches.pop().unwrap();
    if entry.flags & 0x0001 != 0 {
        return Err("Encrypted .gp archives are not supported".into());
    }
    if entry.uncompressed_size > MAX_GPIF_BYTES {
        return Err("Guitar Pro score.gpif exceeds 12 MB limit".into());
    }

    let lfh = entry.local_header_offset;
    if lfh + 30 > data.len() || data[lfh..lfh + 4] != LOCAL_FILE_HEADER_SIG {
        return Err("Invalid ZIP local file header for score.gpif".into());
    }
    let local_name_len = read_u16(data, lfh + 26).ok_or("Invalid local header name len")? as usize;
    let local_extra_len = read_u16(data, lfh + 28).ok_or("Invalid local header extra len")? as usize;
    let payload_start = lfh
        .checked_add(30)
        .and_then(|v| v.checked_add(local_name_len))
        .and_then(|v| v.checked_add(local_extra_len))
        .ok_or("Invalid score.gpif data offset")?;
    let payload_end = payload_start
        .checked_add(entry.compressed_size)
        .filter(|&end| end <= data.len())
        .ok_or("Truncated score.gpif payload in .gp archive")?;
    let compressed = &data[payload_start..payload_end];

    let raw_xml = match entry.method {
        0 => {
            if compressed.len() > MAX_GPIF_BYTES {
                return Err("Guitar Pro score.gpif exceeds 12 MB limit".into());
            }
            compressed.to_vec()
        }
        8 => {
            let mut out = Vec::with_capacity(entry.uncompressed_size.min(MAX_GPIF_BYTES));
            let decoder = DeflateDecoder::new(compressed);
            decoder
                .take((MAX_GPIF_BYTES + 1) as u64)
                .read_to_end(&mut out)
                .map_err(|e| format!("Failed to inflate score.gpif from .gp archive: {e}"))?;
            if out.len() > MAX_GPIF_BYTES {
                return Err("Guitar Pro score.gpif exceeds 12 MB limit".into());
            }
            out
        }
        other => {
            return Err(format!("Unsupported ZIP compression method ({other}) in .gp archive"));
        }
    };

    String::from_utf8(raw_xml)
        .map_err(|e| format!("score.gpif in .gp archive is not valid UTF-8: {e}"))
}
