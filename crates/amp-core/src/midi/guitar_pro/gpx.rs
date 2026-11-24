use super::MAX_GPIF_BYTES;

const SECTOR_SIZE: usize = 0x1000;
const MAX_DECOMPRESSED_GPX: usize = 96 * 1024 * 1024;

struct BitReader<'a> {
    data: &'a [u8],
    byte_idx: usize,
    bit_pos: u8,
    current: u8,
}

impl<'a> BitReader<'a> {
    fn new(data: &'a [u8]) -> Self {
        Self {
            data,
            byte_idx: 0,
            bit_pos: 8,
            current: 0,
        }
    }

    fn read_bit(&mut self) -> Option<u32> {
        if self.bit_pos >= 8 {
            if self.byte_idx >= self.data.len() {
                return None;
            }
            self.current = self.data[self.byte_idx];
            self.byte_idx += 1;
            self.bit_pos = 0;
        }
        let bit = u32::from((self.current >> (7 - self.bit_pos)) & 0x01);
        self.bit_pos += 1;
        Some(bit)
    }

    fn read_bits(&mut self, count: u8) -> Option<u32> {
        let mut val = 0u32;
        for _ in 0..count {
            val = (val << 1) | self.read_bit()?;
        }
        Some(val)
    }

    fn read_bits_reversed(&mut self, count: u8) -> Option<u32> {
        let mut val = 0u32;
        for i in 0..count {
            val |= self.read_bit()? << i;
        }
        Some(val)
    }

    fn read_byte(&mut self) -> Option<u8> {
        self.read_bits(8).map(|v| v as u8)
    }
}

fn decompress_bcfz(payload: &[u8]) -> Result<Vec<u8>, String> {
    if payload.len() < 4 {
        return Err("Truncated BCFZ header in .gpx file".into());
    }
    let expected_len = i32::from_le_bytes(payload[0..4].try_into().unwrap());
    if expected_len <= 4 || expected_len as usize > MAX_DECOMPRESSED_GPX {
        return Err("Invalid or oversized BCFZ payload length in .gpx file".into());
    }
    let expected_len = expected_len as usize;
    let mut reader = BitReader::new(&payload[4..]);
    let mut out = Vec::with_capacity(expected_len.min(1024 * 1024));

    while out.len() < expected_len {
        let Some(flag) = reader.read_bits(1) else {
            break;
        };
        if flag == 1 {
            let Some(word_size) = reader.read_bits(4) else {
                break;
            };
            let word_size = word_size as u8;
            let (Some(offset), Some(size)) = (
                reader.read_bits_reversed(word_size),
                reader.read_bits_reversed(word_size),
            ) else {
                break;
            };
            let offset = offset as usize;
            let size = size as usize;
            if offset == 0 || offset > out.len() {
                return Err("Corrupt LZSS back-reference in .gpx BCFZ stream".into());
            }
            let src_pos = out.len() - offset;
            let to_read = offset.min(size);
            if out.len() + to_read > MAX_DECOMPRESSED_GPX {
                return Err("Decompressed .gpx payload exceeds safety limit".into());
            }
            for i in 0..to_read {
                let b = out[src_pos + i];
                out.push(b);
            }
        } else {
            let Some(raw_len) = reader.read_bits_reversed(2) else {
                break;
            };
            for _ in 0..raw_len {
                let Some(byte) = reader.read_byte() else {
                    return Ok(strip_bcfs_header(out)?);
                };
                if out.len() + 1 > MAX_DECOMPRESSED_GPX {
                    return Err("Decompressed .gpx payload exceeds safety limit".into());
                }
                out.push(byte);
            }
        }
    }

    strip_bcfs_header(out)
}

fn strip_bcfs_header(mut bytes: Vec<u8>) -> Result<Vec<u8>, String> {
    if bytes.len() < 4 || &bytes[0..4] != b"BCFS" {
        return Err("Decompressed .gpx container does not start with BCFS".into());
    }
    bytes.drain(0..4);
    Ok(bytes)
}

fn read_bcfs_score_gpif(data: &[u8]) -> Result<String, String> {
    let mut offset = SECTOR_SIZE;
    let mut found: Option<Vec<u8>> = None;

    while offset + 4 <= data.len() {
        let entry_type = i32::from_le_bytes(data[offset..offset + 4].try_into().unwrap());
        if entry_type == 2 {
            if offset + 0x94 > data.len() {
                return Err("Truncated BCFS directory entry in .gpx file".into());
            }
            let raw_name = &data[offset + 0x04..offset + 0x04 + 127];
            let zero_pos = raw_name.iter().position(|&b| b == 0).unwrap_or(raw_name.len());
            let file_name = String::from_utf8_lossy(&raw_name[..zero_pos]);
            let file_size = i32::from_le_bytes(
                data[offset + 0x8c..offset + 0x90].try_into().unwrap(),
            );
            let is_target = file_name
                .replace('\\', "/")
                .rsplit('/')
                .next()
                .is_some_and(|base| base.eq_ignore_ascii_case("score.gpif"));

            if is_target {
                if file_size < 0 || file_size as usize > MAX_GPIF_BYTES {
                    return Err("Guitar Pro score.gpif in .gpx exceeds 12 MB limit".into());
                }
            }

            let pointer_base = offset + 0x94;
            let mut sector_idx = 0usize;
            let mut file_bytes = if is_target {
                Vec::with_capacity((file_size.max(0) as usize).min(MAX_GPIF_BYTES))
            } else {
                Vec::new()
            };

            loop {
                let ptr_at = pointer_base + 4 * sector_idx;
                sector_idx += 1;
                if ptr_at + 4 > data.len() {
                    break;
                }
                let sector = i32::from_le_bytes(data[ptr_at..ptr_at + 4].try_into().unwrap());
                if sector == 0 {
                    break;
                }
                if sector < 0 {
                    return Err("Negative sector pointer in .gpx BCFS filesystem".into());
                }
                offset = (sector as usize).saturating_mul(SECTOR_SIZE);
                if is_target && offset < data.len() {
                    let end = (offset + SECTOR_SIZE).min(data.len());
                    file_bytes.extend_from_slice(&data[offset..end]);
                    if file_bytes.len() > MAX_GPIF_BYTES + SECTOR_SIZE {
                        return Err("Guitar Pro score.gpif in .gpx exceeds 12 MB limit".into());
                    }
                }
            }

            if is_target {
                if found.is_some() {
                    return Err("Multiple score.gpif entries found in .gpx container".into());
                }
                let trimmed = file_bytes.len().min(file_size.max(0) as usize);
                file_bytes.truncate(trimmed);
                found = Some(file_bytes);
            }
        }
        offset += SECTOR_SIZE;
    }

    let raw = found.ok_or("Missing score.gpif inside .gpx container")?;
    String::from_utf8(raw).map_err(|e| format!("score.gpif in .gpx is not valid UTF-8: {e}"))
}

pub(super) fn extract_score_gpif_from_gpx(data: &[u8]) -> Result<String, String> {
    if data.len() < 4 {
        return Err("Invalid .gpx file (too small)".into());
    }
    if &data[0..4] == b"BCFZ" {
        let block = decompress_bcfz(&data[4..])?;
        read_bcfs_score_gpif(&block)
    } else if &data[0..4] == b"BCFS" {
        read_bcfs_score_gpif(&data[4..])
    } else {
        Err("Unrecognized .gpx container header (expected BCFZ or BCFS)".into())
    }
}
