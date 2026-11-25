<div align="center">
  <br>
  <p>
    <a href="https://github.com/Jed556/AutoMidiPlayer"><img src="https://raw.githubusercontent.com/wiki/Jed556/AutoMidiPlayer/Assets/Branding.png" width="500" alt="Auto MIDI Player【AMP】" /></a> 
  </p>
  <p>
    <a href="https://github.com/Jed556/AutoMidiPlayer/releases"><img alt="Beta" src="https://img.shields.io/github/v/release/Jed556/AutoMidiPlayer?include_prereleases&color=35566D&label=Beta&logo=data:image/svg+xml;base64,PCFET0NUWVBFIHN2ZyBQVUJMSUMgIi0vL1czQy8vRFREIFNWRyAxLjEvL0VOIiAiaHR0cDovL3d3dy53My5vcmcvR3JhcGhpY3MvU1ZHLzEuMS9EVEQvc3ZnMTEuZHRkIj4KDTwhLS0gVXBsb2FkZWQgdG86IFNWRyBSZXBvLCB3d3cuc3ZncmVwby5jb20sIFRyYW5zZm9ybWVkIGJ5OiBTVkcgUmVwbyBNaXhlciBUb29scyAtLT4KPHN2ZyB3aWR0aD0iODAwcHgiIGhlaWdodD0iODAwcHgiIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiBzdHJva2U9IiNmZmZmZmYiPgoNPGcgaWQ9IlNWR1JlcG9fYmdDYXJyaWVyIiBzdHJva2Utd2lkdGg9IjAiLz4KDTxnIGlkPSJTVkdSZXBvX3RyYWNlckNhcnJpZXIiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPgoNPGcgaWQ9IlNWR1JlcG9faWNvbkNhcnJpZXIiPiA8cGF0aCBkPSJNNyA4TDMgMTEuNjkyM0w3IDE2TTE3IDhMMjEgMTEuNjkyM0wxNyAxNk0xNCA0TDEwIDIwIiBzdHJva2U9IiNmZmZmZmYiIHN0cm9rZS13aWR0aD0iMiIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+IDwvZz4KDTwvc3ZnPg=="></a>
    <a href="https://github.com/Jed556/AutoMidiPlayer/releases/latest"><img alt="Stable" src="https://img.shields.io/github/v/release/Jed556/AutoMidiPlayer?color=35566D&label=Stable&logo=data:image/svg+xml;base64,PCFET0NUWVBFIHN2ZyBQVUJMSUMgIi0vL1czQy8vRFREIFNWRyAxLjEvL0VOIiAiaHR0cDovL3d3dy53My5vcmcvR3JhcGhpY3MvU1ZHLzEuMS9EVEQvc3ZnMTEuZHRkIj4KDTwhLS0gVXBsb2FkZWQgdG86IFNWRyBSZXBvLCB3d3cuc3ZncmVwby5jb20sIFRyYW5zZm9ybWVkIGJ5OiBTVkcgUmVwbyBNaXhlciBUb29scyAtLT4KPHN2ZyB3aWR0aD0iODAwcHgiIGhlaWdodD0iODAwcHgiIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiBzdHJva2U9IiNmZmZmZmYiPgoNPGcgaWQ9IlNWR1JlcG9fYmdDYXJyaWVyIiBzdHJva2Utd2lkdGg9IjAiLz4KDTxnIGlkPSJTVkdSZXBvX3RyYWNlckNhcnJpZXIiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPgoNPGcgaWQ9IlNWR1JlcG9faWNvbkNhcnJpZXIiPiA8cGF0aCBkPSJNMjAuNSA3LjI3NzgzTDEyIDEyLjAwMDFNMTIgMTIuMDAwMUwzLjQ5OTk3IDcuMjc3ODNNMTIgMTIuMDAwMUwxMiAyMS41MDAxTTE0IDIwLjg4OUwxMi43NzcgMjEuNTY4NEMxMi40OTM0IDIxLjcyNiAxMi4zNTE2IDIxLjgwNDcgMTIuMjAxNSAyMS44MzU2QzEyLjA2ODUgMjEuODYzIDExLjkzMTUgMjEuODYzIDExLjc5ODYgMjEuODM1NkMxMS42NDg0IDIxLjgwNDcgMTEuNTA2NiAyMS43MjYgMTEuMjIzIDIxLjU2ODRMMy44MjI5NyAxNy40NTczQzMuNTIzNDYgMTcuMjkwOSAzLjM3MzY4IDE3LjIwNzcgMy4yNjQ2MyAxNy4wODkzQzMuMTY4MTYgMTYuOTg0NyAzLjA5NTE1IDE2Ljg2MDYgMy4wNTA0OCAxNi43MjU0QzMgMTYuNTcyNiAzIDE2LjQwMTMgMyAxNi4wNTg2VjcuOTQxNTNDMyA3LjU5ODg5IDMgNy40Mjc1NyAzLjA1MDQ4IDcuMjc0NzdDMy4wOTUxNSA3LjEzOTU5IDMuMTY4MTYgNy4wMTU1MSAzLjI2NDYzIDYuOTEwODJDMy4zNzM2OCA2Ljc5MjQ4IDMuNTIzNDUgNi43MDkyOCAzLjgyMjk3IDYuNTQyODhMMTEuMjIzIDIuNDMxNzdDMTEuNTA2NiAyLjI3NDIxIDExLjY0ODQgMi4xOTU0MyAxMS43OTg2IDIuMTY0NTRDMTEuOTMxNSAyLjEzNzIxIDEyLjA2ODUgMi4xMzcyMSAxMi4yMDE1IDIuMTY0NTRDMTIuMzUxNiAyLjE5NTQzIDEyLjQ5MzQgMi4yNzQyMSAxMi43NzcgMi40MzE3N0wyMC4xNzcgNi41NDI4OEMyMC40NzY2IDYuNzA5MjggMjAuNjI2MyA2Ljc5MjQ4IDIwLjczNTQgNi45MTA4MkMyMC44MzE4IDcuMDE1NTEgMjAuOTA0OSA3LjEzOTU5IDIwLjk0OTUgNy4yNzQ3N0MyMSA3LjQyNzU3IDIxIDcuNTk4ODkgMjEgNy45NDE1M0wyMSAxMi41MDAxTTcuNSA0LjUwMDA4TDE2LjUgOS41MDAwOE0xNiAxOC4wMDAxTDE4IDIwLjAwMDFMMjIgMTYuMDAwMSIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjIiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPiA8L2c+Cg08L3N2Zz4="></a>
    <a href="https://github.com/Jed556/AutoMidiPlayer/releases/latest"><img alt="Downloads" src="https://img.shields.io/github/downloads/Jed556/AutoMidiPlayer/total?label=Downloads&logo=data:image/svg+xml;base64,PCFET0NUWVBFIHN2ZyBQVUJMSUMgIi0vL1czQy8vRFREIFNWRyAxLjEvL0VOIiAiaHR0cDovL3d3dy53My5vcmcvR3JhcGhpY3MvU1ZHLzEuMS9EVEQvc3ZnMTEuZHRkIj4KDTwhLS0gVXBsb2FkZWQgdG86IFNWRyBSZXBvLCB3d3cuc3ZncmVwby5jb20sIFRyYW5zZm9ybWVkIGJ5OiBTVkcgUmVwbyBNaXhlciBUb29scyAtLT4KPHN2ZyB3aWR0aD0iODAwcHgiIGhlaWdodD0iODAwcHgiIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KDTxnIGlkPSJTVkdSZXBvX2JnQ2FycmllciIgc3Ryb2tlLXdpZHRoPSIwIi8+Cg08ZyBpZD0iU1ZHUmVwb190cmFjZXJDYXJyaWVyIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz4KDTxnIGlkPSJTVkdSZXBvX2ljb25DYXJyaWVyIj4gPHBhdGggZD0iTTExIDMuMDEyNTRDMTAuOTk4MyAyLjQ2MDI2IDExLjQ0NDYgMi4wMTExNCAxMS45OTY5IDIuMDA5NDFDMTIuNTQ5MiAyLjAwNzY4IDEyLjk5ODMgMi40NTM5OSAxMyAzLjAwNjI3TDExIDMuMDEyNTRaIiBmaWxsPSIjZmZmZmZmIi8+IDxwYXRoIGQ9Ik0xNC4zMTU4IDEwLjI5NTFMMTMuMDI2OSAxMS41OTJMMTMgMy4wMDYyN0wxMSAzLjAxMjU0TDExLjAyNjkgMTEuNTk4M0w5LjczMDAzIDEwLjMwOTVDOS4zMzgyOCA5LjkyMDE4IDguNzA1MSA5LjkyMjE0IDguMzE1OCAxMC4zMTM5QzcuOTI2NSAxMC43MDU2IDcuOTI4NDkgMTEuMzM4OCA4LjMyMDI0IDExLjcyODFMOC4zMjI3NSAxMS43MzA2TDguMzIzNzQgMTEuNzMxNkwxMi4wMzkgMTUuNDIzNkwxNS43MjA2IDExLjcxODdMMTUuNzI2MiAxMS43MTMxTDE1LjcyNyAxMS43MTIzTDE1LjcyNzggMTEuNzExNUwxNS43MzM3IDExLjcwNTZMMTUuNzM0NCAxMS43MDQ5TDE0LjMxNTggMTAuMjk1MVoiIGZpbGw9IiNmZmZmZmYiLz4gPHBhdGggZD0iTTE1LjczNDQgMTEuNzA0OUMxNi4xMjM3IDExLjMxMzEgMTYuMTIxNyAxMC42Nzk5IDE1LjczIDEwLjI5MDZDMTUuMzM4MiA5LjkwMTM0IDE0LjcwNSA5LjkwMzM1IDE0LjMxNTggMTAuMjk1MUwxNS43MzQ0IDExLjcwNDlaIiBmaWxsPSIjZmZmZmZmIi8+IDxwYXRoIGQ9Ik00IDEyQzQgMTAuODk1NCA0Ljg5NTQzIDEwIDYgMTBDNi41NTIyOCAxMCA3IDkuNTUyMjggNyA5QzcgOC40NDc3MSA2LjU1MjI4IDggNiA4QzMuNzkwODYgOCAyIDkuNzkwODYgMiAxMlYxOEMyIDIwLjIwOTEgMy43OTA4NiAyMiA2IDIySDE3QzE5Ljc2MTQgMjIgMjIgMTkuNzYxNCAyMiAxN1YxMkMyMiA5Ljc5MDg2IDIwLjIwOTEgOCAxOCA4QzE3LjQ0NzcgOCAxNyA4LjQ0NzcxIDE3IDlDMTcgOS41NTIyOCAxNy40NDc3IDEwIDE4IDEwQzE5LjEwNDYgMTAgMjAgMTAuODk1NCAyMCAxMlYxN0MyMCAxOC42NTY5IDE4LjY1NjkgMjAgMTcgMjBINkM0Ljg5NTQzIDIwIDQgMTkuMTA0NiA0IDE4VjEyWiIgZmlsbD0iI2ZmZmZmZiIvPiA8L2c+Cg08L3N2Zz4="></a>
  </p>
  <p>
    <a href="https://github.com/zzstar101/awesome-nte"><img align="top" height="26" alt="Listed in Awesome NTE" src="https://raw.githubusercontent.com/zzstar101/awesome-nte/main/assets/badge-listed.svg"></a>
  </p>
</div>

<div align="center">
  <p>A feature-rich MIDI and Guitar Pro player for in-game instruments, built with Svelte 5, Tauri v2, and Rust.</p>
  <i>If you liked this project, consider <a href="CONTRIBUTING.md">contributing</a> or giving a 🌟 star. Thank you~</i>
</div>
<br/>

---


### Supported Games and Instruments

| Game | Bundled instruments |
| --- | --- |
| **Genshin Impact** | 11 instrument entries, including lyres, zithers, pianos, percussion, Nightwind Horn, plus Ukulele and Lingering Euphonia with Keyboard and pad-only Chords modes |
| **Wuthering Waves** | 21-key instrument layout |
| **Neverness to Everness (NTE)** | Piano (21-key and 36-key), Guitar (Keyboard, Chords, Frets, and Mixed modes) |
| **Where Winds Meet** | Guqin |
| **Blue Protocol: Star Resonance** | Piano (61-key and 88-key) |
| **Sky: Children of the Light** | String, keyboard, wind, and percussion instruments |
| **Roblox** | Piano (61-key and 88-key) |
| **Heartopia** | Piano variations, plus string, wind, and percussion instruments in two-row and three-row layouts |
| **Core Keeper** | Pocket Piano, Drumkit, Cello, Harp, Flute, and Ocarina |
| **Harry Potter: Magic Awakened (HPMA)** | Piano (14-key and 36-key) |

See the [Support wiki page][wiki-support] for details on supported games, instruments, and keyboard layouts.

## Quick Start

1. Launch the desktop app and finish setup. If an old AMP profile is found, you can choose to import it.
2. Choose your game, instrument, input mode (such as **Keyboard**, **Chords**, **Frets**, or **Mixed** where supported), and layout. Make sure they match what you have open in the game.
3. Add your **MIDI** (`.mid`, `.midi`) or **Guitar Pro** (`.gp`, `.gpx`, `.gp5`, `.gp4`, `.gp3`) files with the **+** button (**Import MIDI / Guitar Pro files**) in Songs, drag and drop files into the app, or set a MIDI folder in Settings.
4. Open **Tracks** and keep the parts you want to play. A melody track is a good place to start!
5. Turn on **Listen Mode** to hear the song through your speakers first. Adjust the key, transpose, speed, and note settings as needed.
6. To play in-game, open the game's instrument, turn Listen Mode off, and press **Play**. AMP tries to focus the selected game on Windows, macOS, and X11; losing game focus pauses playback.

On **Wayland**, game output requires permission through the desktop's Remote Desktop portal, independently of physical MIDI input. After allowing keyboard access, switch to the game within 30 seconds; song playback waits until AMP loses focus. It sends keys to the focused app and cannot identify the game. Global hotkeys use the desktop's Global Shortcuts portal and show the desktop-approved bindings in Settings. Desktops without either portal report the unavailable feature. See [Getting Started][wiki-getting-started] for platform permissions and troubleshooting.

On **macOS**, the app isn't notarized, so the first launch is blocked by Gatekeeper. Open it once, then click **Open Anyway** in **System Settings → Privacy & Security** (or run `xattr -cr "/Applications/Auto MIDI Player.app"`). If game keys stop working after an update, re-enable AMP under **Privacy & Security → Accessibility**. See [First launch on macOS][wiki-macos-first-launch].

> [!NOTE]
> macOS and Linux support should work in theory, however it isn't tested due to my system's limitations, so help in testing would be appreciated!

Here's where the main controls live. The annotated screenshots use the current interface with **AMP Demo** data and no connected MIDI device; your library and available instruments may look different.

![Annotated Instrument page showing instrument selection, pedals, scheduling, and MIDI input](https://raw.githubusercontent.com/wiki/Jed556/AutoMidiPlayer/Assets/Screenshots/rewrite-instrument-annotated.svg)

## Features

### Make a Song Fit Your Instrument

* **Per-song tuning** - Save key offsets, transpose, speed, BPM, and note settings for the selected instrument, the whole game, or all games. Instrument settings take priority, so the same song can suit different instruments without setting everything up again.
* **Smart transpose and Auto Correct** - Help notes fit instruments with limited ranges or missing sharps and flats. You can also choose Up, Down, or Ignore transposition.
* **Hold Notes and Tap Threshold** - Choose between short key taps and held notes, or tap short notes while holding longer ones.
* **Merge Notes** - Join repeated notes of the same pitch when they overlap or fall within your chosen gap. Try it on songs where repeated taps sound too busy.
* **Guitar Pro & MIDI support** - Import and scan both standard MIDI (`.mid`, `.midi`) and Guitar Pro tabs (`.gp`, `.gpx`, `.gp5`, `.gp4`, `.gp3`). Guitar Pro files expand repeats, alternate endings, and Da Capo / Dal Segno / Coda jumps, and supply written chord marks and brush/strum directions to the guitar arranger.
* **Chord & Fret Instruments** - Switch between **Keyboard** and pad-only **Chords** modes on Genshin's Ukulele and Lingering Euphonia, or use **Keyboard**, **Chords**, **Frets**, and **Mixed** modes on NTE's Guitar.
* **Default configuration updates** - Add missing bundled layouts and instruments on launch. A setup screen reviews changed JSON and duplicate IDs before the player initializes, with opt-in replacement and backed-up removal of older duplicates. See [custom configurations][wiki-config].
* **Pedals and layouts** - Use sustain, sostenuto, and soft pedals where supported. Edit pedal bindings for the selected keyboard layout, and choose from QWERTY, QWERTZ, AZERTY, Dvorak, and other available mappings.

Every game's instrument behaves a little differently. Preview your changes, then try a short section in-game to find what sounds best.

For setup, page/mode synchronization, and limitations on NTE's **Guitar** (**Keyboard**, **Chords**, **Frets**, and **Mixed** modes) and pad-only **Chords** instruments (Genshin Impact's **Ukulele** and **Lingering Euphonia**), see the [Guitar Modes][wiki-guitar], [Chord Pads Mode][wiki-chords], and [custom configurations][wiki-config] guides on the wiki.

### Listen Before You Play

Listen Mode plays through your speakers using SoundFonts instead of sending game keys. Pick **Game Preview (Adapted)** to hear notes fitted to the selected instrument, or **Full Chromatic (Raw)** to hear the original MIDI pitches.

* **Play In-Game Durations** makes the adapted preview follow Hold Notes, Tap Threshold, and Key Press Delay. Leave it off to keep the MIDI's note lengths.
* Use the bundled **GeneralUser GS** and **Roland GM** SoundFonts, or add your own `.sf2` files.
* Turn on **Automatic gap filling** and arrange SoundFonts in priority order. Missing instruments and notes fall back to the next font in the list.
* Custom SoundFonts stay at their original locations. Removing one from AMP removes its registration and leaves your file alone.
* **Volume Normalization** balances the combined SoundFont audio, including overlapping notes, so quiet and loud passages are easier to listen to.

![Annotated audio settings showing synthesis mode, in-game durations, SoundFont priority, gap filling, and volume normalization](https://raw.githubusercontent.com/wiki/Jed556/AutoMidiPlayer/Assets/Screenshots/rewrite-audio-annotated.svg)

### Play Your Own MIDI Keyboard

Choose a device from **MIDI Input** on the Instrument page and start playing-selecting it connects automatically. AMP remembers the device and tries to reconnect to it at launch when it is available. After unplugging and reconnecting it, use **Refresh** to reconnect.

Live input works with Listen Mode or game output, including note velocity and supported pedals. You can play along with a MIDI song, and the connection stays active while you browse other pages or minimize to the tray. Select **None** to disconnect. See the [MIDI input guide][wiki-how-to-use] for details and platform permissions.

### Tracks, Studio, and Piano Sheet

* **Tracks** - Play several MIDI tracks together, mute parts you don't need, or solo a part to hear it clearly.
* **Studio** - Follow a live piano roll with note highlighting, a keyboard guide, timeline scrubbing, zoom, and playhead tracking. Adjust tracks and song settings from the side panel.
* **Key change markers** - Add mid-song key offsets in Studio or detect key changes from the MIDI, then edit or disable the markers as needed.
* **Piano Sheet** - Turn the selected song into keyboard notation using your current layout. Adjust the delimiter, grouping, and spacing, and check the legend when a symbol is unfamiliar.

![Annotated Studio page showing key markers, song settings, and the timeline scrubber](https://raw.githubusercontent.com/wiki/Jed556/AutoMidiPlayer/Assets/Screenshots/rewrite-studio-annotated.svg)

### Your Library, Playlists, and Queue

* Import MIDI and Guitar Pro files (or drag and drop them in), scan a library folder, search your library, and sort by title, artist, duration, or date added.
* Organize favorites into **Playlists** and **Liked Songs**, or switch to custom order to drag songs into the order you want. AMP remembers your ordering and scroll positions.
* Add songs to the queue, reorder what's coming next, and use shuffle or repeat for longer listening sessions.
* Schedule a play or pause action from the Instrument page. The timer keeps running while you change pages; keep AMP open for it to fire.

### Desktop Conveniences

* **System tray** - Choose whether minimizing or closing hides AMP to the tray. Use the tray menu to play/pause, skip ahead, reopen the window, or exit.
* **Playback shortcuts** - Use configurable hotkeys and native media controls, including while the window is minimized. Availability depends on your desktop and its permissions.
* **Make it yours** - Choose light, dark, or system theme, change the accent color, and switch between English and Simplified Chinese.
* **Migration** - Import an existing legacy library and settings during setup when detected. Old-data cleanup is optional.
* **System and diagnostics** - View system information, open logs, and adjust diagnostic settings from Settings when you need help with an issue.

### Still in Progress

**Discover (online MIDI)** is not implemented yet. Discover is available in the legacy app.

## About

### What are MIDI and Guitar Pro files?
MIDI files (`.mid`, `.midi`) and Guitar Pro tabs (`.gp`, `.gpx`, `.gp5`, `.gp4`, `.gp3`) store notes, timing, and instrument tracks rather than recorded audio. AMP lets you choose which tracks to play, then turns their notes into game keys, chord pads, guitar wheel actions, or SoundFont audio. For Guitar Pro files, AMP also uses written chord marks and strum directions to improve guitar playback. The selected instrument, mode, layout, and tuning settings decide which notes fit.

### Can this get me banned?
There is no guarantee that a game permits automated input or that using AMP will avoid account restrictions. Check the game's current rules and use it at your own risk. See the [Disclaimer][wiki-disclaimer] for more information.

## Documentation

For setup details, feature walkthroughs, safety notes, and troubleshooting, use the wiki links below.

- [Wiki Home][wiki-home]
- [Disclaimer][wiki-disclaimer]
- [Getting Started][wiki-getting-started]
- [How To Use][wiki-how-to-use]
- [How it Works][wiki-how-it-works]
- [Support (Supported Games, Instruments, and Keyboards)][wiki-support]
- [FAQ General][wiki-faq]

## Build from Source

### Requirements

* [Git](https://git-scm.com)
* [Node.js](https://nodejs.org) (v24+) & [pnpm](https://pnpm.io)
* [Rust](https://www.rust-lang.org) (MSVC toolchain on Windows, native toolchain on Linux/macOS)
* [Tauri platform prerequisites](https://v2.tauri.app/start/prerequisites/)

### Build and run

```bash
git clone --branch main https://github.com/Jed556/AutoMidiPlayer.git
cd AutoMidiPlayer

# Install frontend dependencies
pnpm install --frozen-lockfile

# Start Tauri desktop app in dev mode (Vite + Rust)
pnpm dev

# Build native packages for the current platform
pnpm build
```

`pnpm dev` starts both the frontend and native backend. `pnpm dev:frontend` starts Vite alone for frontend work; the app's library, settings, and playback require Tauri and do not work as a standalone browser app.

For platform prerequisites, package formats, and custom game or instrument definitions, see [Getting Started][wiki-getting-started]. Windows, macOS, X11, and Wayland have different permission and focus requirements; a successful build does not verify physical MIDI or in-game behavior on every platform.

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md).  
For adding notes, instruments, games, or keyboard mappings, see [Getting Started][wiki-getting-started].

Use conventional commit messages so the versioning workflow can assign a version to each eligible commit on `main`. It amends the version files into those commits, preserving author and committer dates while changing commit SHAs. See [Commit Messages and Versioning](CONTRIBUTING.md#commit-messages-and-versioning) for bump rules, previews, and how to sync your local branch after the bot runs.

# Special Thanks

This project was originally forked from **[sabihoshi/GenshinLyreMidiPlayer][GenshinLyreMidiPlayer]** [v4.0.5](https://github.com/sabihoshi/GenshinLyreMidiPlayer/releases/tag/v4.0.5) and later detached into its own repository to enable multi-game support and features that do not fit the original Genshin Impact-only design. Huge thanks for the original work! 🔥

Special thanks to **[Lunaferlone](https://github.com/Lunaferlone)** for helping with QA and testing throughout the rewrite development process! 💖

# License

* This project’s source code is licensed under the [GNU General Public License v3.0](LICENSE).
* This project is based on [sabihoshi/GenshinLyreMidiPlayer][GenshinLyreMidiPlayer]. Original attribution and upstream notices are preserved in [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).
* "**Auto MIDI Player**" and its logo are trademarks of Jed556. Trademark and branding use are governed by [TRADEMARK.md](TRADEMARK.md).
* This project is not affiliated with or endorsed by miHoYo, KURO GAMES, XD, thatgamecompany, Roblox Corporation, Hotta Studio, Pugstorm, Everstone Studio, or any other rights holder. Genshin Impact™, Wuthering Waves™, Heartopia™, Sky: Children of the Light™, Roblox™, Neverness to Everness™, Core Keeper™, Where Winds Meet™, and other game titles and properties belong to their respective owners.
* Third-party libraries and other resources used by this project may be distributed under different licenses and are listed in [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).

<br/>

> [!IMPORTANT]
> Older releases use the legacy C#/WPF app and have different features and setup steps.

> [!NOTE]
> I don't have knowledge about music theory, if you find any issues with note mappings or transpositions, please open an issue or PR. Thank you! 💖

[latest]: https://github.com/Jed556/AutoMidiPlayer/releases/latest
[releases]: https://github.com/Jed556/AutoMidiPlayer/releases
[GenshinLyreMidiPlayer]: https://github.com/sabihoshi/GenshinLyreMidiPlayer
[wiki-home]: https://github.com/Jed556/AutoMidiPlayer/wiki
[wiki-getting-started]: https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started
[wiki-config]: https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started#addding
[wiki-macos-first-launch]: https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started#macos-first-launch
[wiki-how-to-use]: https://github.com/Jed556/AutoMidiPlayer/wiki/How-to-Use
[wiki-chords]: https://github.com/Jed556/AutoMidiPlayer/wiki/How-to-Use#play-chords
[wiki-guitar]: https://github.com/Jed556/AutoMidiPlayer/wiki/How-to-Use#play-guitar
[wiki-how-it-works]: https://github.com/Jed556/AutoMidiPlayer/wiki/How-it-Works
[wiki-support]: https://github.com/Jed556/AutoMidiPlayer/wiki/Support
[wiki-faq]: https://github.com/Jed556/AutoMidiPlayer/wiki/FAQ-General
[wiki-disclaimer]: https://github.com/Jed556/AutoMidiPlayer/wiki/Disclaimer
