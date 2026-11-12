//! Access to the application's user-facing Spanish messages.

/// Returns a translated message from the bundled resource catalog.
pub fn text(key: &str) -> &'static str {
    include_str!("../locales/spanish.properties")
        .lines()
        .filter_map(|line| line.split_once('='))
        .find_map(|(name, message)| (name == key).then_some(message))
        .expect("A studio localization key is missing")
}
