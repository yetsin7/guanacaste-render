//! Room information required to reproduce household service sizing.

use serde::Deserialize;

/// A room name is used to infer household size when no explicit occupant count is stored.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Room {
    pub name: String,
}
