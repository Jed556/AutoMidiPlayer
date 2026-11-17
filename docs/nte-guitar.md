# NTE guitar keyboard, chord, and fret playback

On Windows, select **Neverness to Everness → Guitar**, then choose **Keyboard**, **Chords**, or **Frets** in Instrument view. Each input group has its own layout selection. Chords uses **Chord Wheel** with **Auto Chord** enabled; Frets uses **Fret Wheel** and does not require Auto Chord. The lower **Frets** toggle switches between chord and fret controls.

Open the matching guitar controls in NTE before playback. Selecting Keyboard uses the game's chromatic piano mode; entering or leaving that mode still requires the game's mode button. Switching directly between Chords and Frets while NTE is running focuses the game and sends its configured `Z` binding. Selecting a mode while the game is closed changes AMP's configuration without sending input. Pause playback before switching modes.

The existing Auto Chord detector handles both chord output types. The instrument JSON selects `chordMode`: `"pads"` uses the selected layout's `chordKeys` (the default for Genshin's Ukulele), while `"wheel"` uses its `chordWheel` selector/page/strum bindings. Musical definitions are stored in `chords`. The separate `keyboardLayouts`, `chordLayouts`, and `fretLayouts` lists declare the input groups available for the instrument. See the [instrument configuration guide](instrument-configuration.md) for the schema and examples.

Existing app-data JSON files are preserved when updating the app. To use the new bundled Guitar definition with an older configuration, restore the bundled game/layout defaults or add its chord settings to your existing JSON.

Use **Sync game page** to declare the page currently visible in NTE. It changes AMP's page tracking without sending input. AMP initially assumes page 1. Synchronize again after changing pages directly in the game, reopening its instrument, or an interrupted page change.

Chord and fret pages are tracked separately. On first entering an unfamiliar layout, set its page before playing. Returning to a previously synchronized layout restores its tracked page; AMP cannot detect changes made directly in the game.

**Previous** and **Next** focus the selected game and send its configured `R` / `T` bindings, wrapping at the first/last page. Pause playback before using these buttons. During MIDI playback, AMP takes the shorter wrapped route to the required page and reuses the selected chord for repeated strums, following the reference player's interaction.

| Page | Clockwise from the top |
| --- | --- |
| 1 | G, Am, Bm, C, D, Em |
| 2 | Cmaj7, C7, Cm7, G7, C9, Cm9 |
| 3 | C5, D5, G5, A5, Cmaj9, Cadd9 |
| 4 | G/B, Am7/G, D/F#, F/A, A, Dm |

The detector matches the pitch classes of note starts inside the Auto Chord tolerance. Power chords can match two distinct pitches. Slash chords also require the indicated lowest pitch class. Unsupported groups and individual melody notes are silent with the wheel layout; choose Guitar's keyboard layout to play chromatic melodies. Listen Mode still previews MIDI notes through the synthesizer.

## Fret playback and guitar view

Fret mode selects one fret shared by all six strings, then plucks the configured string keys. It supports frets 1–24 across four pages and open strings on the fifth page. String 1 is high E, bound to key `6`; string 6 is low E, bound to key `1`. The playable fret range comes from the instrument's tuning and fret count (E2–E6 for the bundled NTE guitar), independently of its piano-mode keyboard range.

Notes at the same MIDI onset must have an exact voicing using distinct strings at one common fret. Unsupported groups stay silent; the player does not approximate their pitches or spread them into an arpeggio. Separate onsets retain their own timing. Selection/page preparation and held string-key cleanup use the same transport as wheel chords.

`layoutStyle: "guitar"` adds a six-string fretboard, current chord names, and a string/fret readout. The board scrolls horizontally in a narrow window. This is a visualization of the current notes, not a TAB editor or imported score. An unsupported simultaneous fret group is indicated in the view.

AMP prepares the page and selector click ahead of the musical onset, then sends a right-button down-strum at the note's scheduled time. The first attack, seeks, and resumes may need a brief preparation interval while the playhead remains stationary. Page changes must fit between attacks at the chosen playback speed. If preparation cannot finish in time, playback pauses with an error instead of delaying that chord. Reduce speed or adjust timing for dense passages.

Pausing, stopping, switching instruments, disabling output, or losing focus cancels pending mouse actions and releases held inputs. A partially interrupted page key leaves the current page unknown; use Sync game page before resuming. There is no automatic visual recognition of the in-game mode or page.

## Positions and timing

The app-data layout file `KeyboardLayouts/NTE_Chord_Wheel.json` defines six selector positions, a separate strum position, page keys, and timing. `NTE_Fret_Wheel.json` references those controls through `fretboard.wheelLayout` and adds its string/mode bindings. Coordinates are thousandths of the foreground game's client width and height, excluding window borders. Defaults follow the supplied 16:9 screenshot; adjust them for a different UI arrangement or aspect ratio. For example, `[693, 255]` means 69.3% across and 25.5% down the game area. Changes reload through the existing configuration watcher.

The game process must match the selected game's `processNames` from `game.json` or its configured executable preference before any selector movement or mouse press. Layouts contain input controls only. Mouse output is currently Windows-only. Input holds and waits are configurable host timings; they are not measurements of NTE's response.

The page order, fret/string controls, and click/strum interaction were checked against the [reference player's game profile](https://github.com/DIEtype/Guitarplayer-For-NTE/blob/aa0ce3d183ef0745e6960a7bdc031bc3e2bb52e3/guitar_player/game_profile.py). This implementation does not import that application's code. TAB/Guitar Pro import, timbre switching, and live MIDI chord/fret arrangement are outside this change.

Mock backend tests cover page keys, selection, strums, scheduling, cancellation, focus loss, and cleanup failures. Actual NTE input acceptance, chord voicings, cursor calibration, and timing still require in-game testing.
