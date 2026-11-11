use super::SettingsStore;
use std::path::Path;

/// Custom fonts remain at their original paths, including while temporarily missing.
pub fn paths(settings: &SettingsStore) -> Result<Vec<String>, String> {
    Ok(settings.get()?.get("customSoundFontPaths")
        .and_then(|value| value.as_array())
        .map(|values| values.iter().filter_map(|value| value.as_str().map(str::to_owned)).collect())
        .unwrap_or_default())
}

pub fn add(settings: &SettingsStore, path: &Path) -> Result<String, String> {
    if !path.is_absolute() || !path.extension().is_some_and(|ext| ext.eq_ignore_ascii_case("sf2")) {
        return Err("Choose an absolute path to an .sf2 file".into());
    }
    let path = path.canonicalize().map_err(|e| e.to_string())?;
    let mut file = std::fs::File::open(&path).map_err(|e| e.to_string())?;
    rustysynth::SoundFont::new(&mut file).map_err(|e| format!("Failed to parse SoundFont: {e:?}"))?;
    let path = path.to_string_lossy().into_owned();
    let mut registered = paths(settings)?;
    if !registered.contains(&path) {
        if registered.len() >= 32 { return Err("Choose at most 32 custom SoundFonts".into()); }
        registered.push(path.clone());
        settings.update(&serde_json::json!({"customSoundFontPaths": registered}))?;
    }
    Ok(path)
}

/// Remove the registration only. The source file always belongs to the user.
pub fn remove(settings: &SettingsStore, path: &str) -> Result<(), String> {
    let mut registered = paths(settings)?;
    registered.retain(|entry| entry != path);
    settings.update(&serde_json::json!({"customSoundFontPaths": registered}))
}
