//! Household size inference shared by tank and septic designs.

use crate::document::{Document, PlumbingPoint};

/// Uses an explicit occupant count, two people per bedroom, or the original four-person fallback.
pub(super) fn household_size(document: &Document, point: &PlumbingPoint) -> i32 {
    if point.occupants > 0 {
        return point.occupants;
    }
    let bedrooms = document
        .rooms
        .iter()
        .filter(|room| {
            let name = room.name.to_lowercase();
            include_str!("../../data/bedroom-terms.txt").lines().any(|term| name.contains(term))
        })
        .count();
    if bedrooms > 0 { (bedrooms * 2) as i32 } else { 4 }
}

#[cfg(test)]
mod tests {
    use super::household_size;
    use crate::document::{Document, Room};

    /// Keeps explicit counts, legacy room names, and the no-bedroom fallback consistent with the original application.
    #[test]
    fn infers_occupants_from_explicit_values_and_bedroom_names() {
        let mut document = Document::parse(include_bytes!("../../tests/fixtures/water-storage.gr")).expect("valid water-storage fixture");
        assert_eq!(household_size(&document, &document.plumbing_points[2]), 6);
        assert_eq!(household_size(&document, &document.plumbing_points[3]), 3);

        document.rooms.clear();
        assert_eq!(household_size(&document, &document.plumbing_points[2]), 4);
        for term in include_str!("../../data/bedroom-terms.txt").lines() {
            document.rooms.push(Room { name: term.to_uppercase() });
        }
        assert_eq!(household_size(&document, &document.plumbing_points[2]), 6);
    }
}
