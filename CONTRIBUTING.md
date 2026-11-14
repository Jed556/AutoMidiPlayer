# Contributing

Want to help improve AMP? Thank you! 💖 Please discuss your planned change in an issue or with me or the maintainers before starting, so we can agree on what it should do.

Please follow our [Code of Conduct](CODE_OF_CONDUCT.md) in all project interactions.

## Get Set Up

The current desktop rewrite lives on **`rewrite-experimental`** and uses Svelte 5, Tauri v2, and Rust. Follow [Build from Source](README.md#build-from-source) for prerequisites and commands, or see the Wiki's [Getting Started](https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started) for platform details and configuration examples.

The main pieces are:

* `src/renderer` - the Svelte interface, with shared types and defaults in `src/shared`.
* `src-tauri` - desktop integration, including MIDI devices, game detection, media controls, and the system tray.
* `crates/amp-core` - MIDI parsing, playback, synthesis, note mapping, and library storage.

Adding a game, instrument, or keyboard layout? Edit the bundled JSON under `src-tauri/resources/defaults/`. You can try custom mappings in the installed app first; the Wiki explains where to put them.

## Pull Request Process

1. Keep the change focused and explain the problem it solves. Include a small example or reproduction steps when useful.
2. Update the README
3. Run the checks relevant to your change and mention the results. For UI changes, screenshots help; for playback or input changes, say which game, OS, or MIDI device you tried.
4. Keep generated builds, caches, personal settings, MIDI collections, and personal SoundFonts out of the pull request.

Release versions are managed by the project's scripts and workflows. You don't need to bump the version just to open a pull request unless a maintainer asks.

## Check Your Changes

These are the usual checks; pick the ones that cover what you changed:

```bash
# Compile the frontend
pnpm build:frontend

# Run frontend/store and integration regression tests
node --test tests/*.test.mjs

# Run the Rust library tests
cargo test --workspace --lib --locked
```

You can run an individual test file while working on one feature. Use `pnpm dev` to check the desktop app, and `pnpm build` when you need to verify native packaging. A browser capture or automated test doesn't verify physical MIDI input or in-game playback, so include a short manual check when your change depends on those.
