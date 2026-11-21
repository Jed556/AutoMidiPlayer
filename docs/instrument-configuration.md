# Instrument and input layouts

Game definitions own process/window names and default selections. Instrument definitions own pitches, chords, guitar tuning, supported input groups, and their visual style. Layout definitions own keyboard/mouse bindings and calibration.

## Layout groups

An instrument declares three separate lists:

```json
{
  "keyboardLayouts": ["NTE_QWERTY_Guitar"],
  "chordLayouts": ["NTE_Chord_Wheel"],
  "fretLayouts": ["NTE_Fret_Wheel"]
}
```

| Field | Purpose |
| --- | --- |
| `keyboardLayouts` | Chromatic/diatonic note keys, including the existing NTE piano-style guitar controls |
| `chordLayouts` | Chord pad keys or a calibrated chord wheel, selected by the instrument's `chordMode` |
| `fretLayouts` | Guitar string bindings and a fret selector |

An empty list means that input group is unavailable. A layout may appear in more than one list: Genshin's melody layout provides both individual note keys and chord pad keys. Instrument view retains a layout choice for each available input group.

## Chords

Use `chords` for the instrument's musical definitions:

```json
{
  "chordMode": "wheel",
  "chords": [
    { "name": "G", "pitchClasses": [2, 7, 11] },
    { "name": "G/B", "pitchClasses": [2, 7, 11], "bassPitchClass": 11 }
  ]
}
```

Pitch classes use C = 0 through B = 11. `bassPitchClass` distinguishes slash-chord inversions. The existing Auto Chord detector is shared by `"pads"` (the default) and `"wheel"` modes. Pad layouts supply `chordKeys`; wheel layouts supply `chordWheel`. The musical definitions are independent of those physical controls.

## Guitar geometry and display

```json
{
  "layoutStyle": "guitar",
  "guitar": {
    "tuning": [64, 59, 55, 50, 45, 40],
    "fretCount": 24
  }
}
```

Tuning is ordered from string 1 (high E) to string 6 (low E), using MIDI pitches for the open strings. A fretted pitch is its open-string pitch plus the fret number. The guitar style presents chord and string/fret information without treating the guitar controls as a piano grid. A string/fret display is a visualization of current notes; it does not add TAB or Guitar Pro file import.

The instrument's existing `keys` matrix remains the pitch set for keyboard playback. Fret playback derives its playable range from `guitar.tuning` and `guitar.fretCount`.

## Fret bindings

```json
{
  "id": "NTE_Fret_Wheel",
  "name": "Fret Wheel",
  "keys": [],
  "fretboard": {
    "stringKeys": ["6", "5", "4", "3", "2", "1"],
    "wheelLayout": "NTE_Chord_Wheel",
    "modeKey": "z"
  }
}
```

`stringKeys` follows the same high-to-low string order as tuning. `wheelLayout` references the existing calibrated selector positions, page keys, and timing instead of copying them. `modeKey` switches the game's chord/fret sub-mode. NTE's frets 1–24 occupy four six-slot pages; open strings use the fifth page's Zero selector.

## Existing configurations

Bundled instruments use the new names. Older `supportedLayouts` and `chordPads` fields can still be read for existing user configurations; native serialization emits the canonical layout groups and `chords`. Existing app-data JSON is preserved during app updates. Restore bundled defaults or edit your current definitions to adopt newly added bindings.

See [NTE guitar setup](nte-guitar.md) for playback controls, mode/page synchronization, calibration, and limitations.
