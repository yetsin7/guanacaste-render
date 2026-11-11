//! The native project container shared by `.gr` and `.g3d` files, as written by `NativeDocumentStore` in the C#
//! application.
//!
//! ```text
//! "G3DNATV1"            8 bytes
//! container version     i32 (= 1)
//! kind                  u8  (1 project, 2 family, 3 template)
//! schema version        i32
//! document id           16 bytes
//! uncompressed length   i64
//! compressed length     i64
//! SHA-256 of the JSON   32 bytes
//! gzip of the JSON      the rest of the file
//! ```
//!
//! Little endian. The checksum is verified, so a damaged file is refused rather than half-read.

use sha2::{Digest, Sha256};
use std::fmt;
use std::io::Read;

const SIGNATURE: &[u8; 8] = b"G3DNATV1";
const HEADER_BYTES: usize = 8 + 4 + 1 + 4 + 16 + 8 + 8 + 32;
const MAXIMUM_PAYLOAD_BYTES: u64 = 256 * 1024 * 1024;

#[derive(Debug)]
pub enum ContainerError {
    Io(std::io::Error),
    /// Not a native container, or one this reader does not understand.
    Unsupported(&'static str),
    /// A native container whose contents do not match its own header.
    Damaged(&'static str),
}

impl fmt::Display for ContainerError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ContainerError::Io(error) => write!(f, "cannot read the project file: {error}"),
            ContainerError::Unsupported(reason) => write!(f, "unsupported project file: {reason}"),
            ContainerError::Damaged(reason) => write!(f, "damaged project file: {reason}"),
        }
    }
}

impl std::error::Error for ContainerError {}

impl From<std::io::Error> for ContainerError {
    fn from(error: std::io::Error) -> Self {
        ContainerError::Io(error)
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum DocumentKind {
    Project,
    Family,
    Template,
}

/// A verified container: its header and the JSON it carried.
#[derive(Debug)]
pub struct Container {
    pub kind: DocumentKind,
    pub schema_version: i32,
    pub json: Vec<u8>,
}

impl Container {
    pub fn read(path: impl AsRef<std::path::Path>) -> Result<Container, ContainerError> {
        Container::parse(&std::fs::read(path)?)
    }

    pub fn parse(bytes: &[u8]) -> Result<Container, ContainerError> {
        if bytes.len() < HEADER_BYTES || &bytes[..8] != SIGNATURE {
            return Err(ContainerError::Unsupported("the G3DNATV1 signature is missing"));
        }
        let i32_at = |at: usize| i32::from_le_bytes([bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]]);
        let i64_at = |at: usize| {
            let mut value = [0u8; 8];
            value.copy_from_slice(&bytes[at..at + 8]);
            i64::from_le_bytes(value)
        };
        if i32_at(8) != 1 {
            return Err(ContainerError::Unsupported("unknown container version"));
        }
        let kind = match bytes[12] {
            1 => DocumentKind::Project,
            2 => DocumentKind::Family,
            3 => DocumentKind::Template,
            _ => return Err(ContainerError::Damaged("the document kind is invalid")),
        };
        let schema_version = i32_at(13);
        let (uncompressed, compressed) = (i64_at(33), i64_at(41));
        let expected_hash = &bytes[49..81];
        if schema_version < 1
            || uncompressed <= 0
            || uncompressed as u64 > MAXIMUM_PAYLOAD_BYTES
            || compressed <= 0
            || compressed as usize != bytes.len() - HEADER_BYTES
        {
            return Err(ContainerError::Damaged("the header lengths are invalid"));
        }

        // Read one byte past the declared length: a longer stream is a mismatch, not something to keep inflating.
        let mut json = Vec::with_capacity(uncompressed as usize);
        flate2::read::GzDecoder::new(&bytes[HEADER_BYTES..])
            .take(uncompressed as u64 + 1)
            .read_to_end(&mut json)
            .map_err(|_| ContainerError::Damaged("the compressed payload is invalid"))?;
        if json.len() as i64 != uncompressed {
            return Err(ContainerError::Damaged("the decompressed length does not match the header"));
        }
        if Sha256::digest(&json).as_slice() != expected_hash {
            return Err(ContainerError::Damaged("the integrity checksum does not match"));
        }
        Ok(Container { kind, schema_version, json })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const HOUSE: &[u8] = include_bytes!("../tests/fixtures/demo-house.gr");

    #[test]
    fn reads_a_project_written_by_the_application() {
        let container = Container::parse(HOUSE).expect("the fixture is a valid project");
        assert_eq!(container.kind, DocumentKind::Project);
        assert_eq!(container.schema_version, 15);
        assert!(container.json.starts_with(b"{"));
    }

    #[test]
    fn refuses_foreign_truncated_and_altered_files() {
        assert!(matches!(Container::parse(b"PK\x03\x04"), Err(ContainerError::Unsupported(_))));
        assert!(matches!(Container::parse(&HOUSE[..HOUSE.len() - 40]), Err(ContainerError::Damaged(_))));
        let mut altered = HOUSE.to_vec();
        altered[60] ^= 0x01; // one bit of the stored checksum
        assert!(matches!(Container::parse(&altered), Err(ContainerError::Damaged("the integrity checksum does not match"))));
    }
}
