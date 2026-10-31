use serde::de::DeserializeOwned;
use serde_json::Value;
use std::sync::LazyLock;

static DEFAULTS: LazyLock<Value> = LazyLock::new(|| {
    serde_json::from_str(include_str!("defaults.json")).expect("Invalid bundled settings defaults")
});

pub fn document() -> &'static Value { &DEFAULTS }

/// Bundled defaults are validated by tests. A missing or mistyped entry is a
/// programming error rather than a second hardcoded fallback.
pub fn value<T: DeserializeOwned>(pointer: &str) -> T {
    serde_json::from_value(DEFAULTS.pointer(pointer)
        .unwrap_or_else(|| panic!("Missing bundled default: {pointer}")).clone())
        .unwrap_or_else(|error| panic!("Invalid bundled default {pointer}: {error}"))
}
