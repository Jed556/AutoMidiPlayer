# Contributing

Want to help improve AMP? Thank you! 💖 Please discuss your planned change in an issue or with me or the maintainers before starting, so we can agree on what it should do.

Please follow our [Code of Conduct](CODE_OF_CONDUCT.md) in all project interactions.

## Get Set Up

AMP uses Svelte 5, Tauri v2, and Rust, with development on **`main`**. Follow [Build from Source](README.md#build-from-source) for prerequisites and commands, or see the Wiki's [Getting Started](https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started) for platform details and configuration examples.

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

Release versions are managed by the project's scripts and workflows. You don't need to bump the version just to open a pull request unless a maintainer asks. Use the commit conventions below so the workflow can determine the appropriate version.

## Commit Messages and Versioning

Use the format `type: description` or `type(scope): description`. For squash merges, the final squash commit message determines the bump, so use the same format for your pull request title.

| Commit | Version change |
| --- | --- |
| A conventional commit with `!` before `:`, such as `feat!: replace the settings format`, or a `BREAKING CHANGE:` / `BREAKING-CHANGE:` line in its body | Major |
| `feat:` | Minor |
| `fix:`, `perf:`, `ref:`, `refactor:`, `ui:` | Patch |
| Other types, such as `docs:`, `chore:`, `ci:`, `build:`, `deps:`, and `test:`, or an unprefixed message | Inherit the current version; no increment unless a conventional commit declares a breaking change |

The [Bump Version workflow](.github/workflows/bump_version.yml) runs on pushes to `main`. Each eligible commit advances the version independently, including commits in the same push or on the same day. For example, starting at `8.0.0`:

```text
fix: release held notes on pause   -> 8.0.1
fix: handle device disconnection  -> 8.0.2
docs: clarify MIDI input setup   -> 8.0.2
feat: add a new instrument        -> 8.1.0
fix: correct its note mapping     -> 8.1.1
```

The workflow writes each commit's version into `package.json`, `src-tauri/tauri.conf.json`, `src-tauri/Cargo.toml`, `Cargo.lock`, and `src-tauri/Cargo.lock` when present. It amends those commits instead of adding a separate `chore(version)` commit. Commit messages, authors, and both author and committer dates, including timezone offsets, are preserved. Rewritten commits have new SHAs and lose existing commit signatures. Push and Actions-run times are separate from the dates stored in commits.

Commits marked `[skip ci]` or `[ci skip]`, and version/release automation commits, are ignored for version increments. Avoid skip markers on changes that need a bump. Version assignment does not publish a release; release packaging and tags are handled separately.

Preview the calculation locally without changing version files or rewriting commits:

```bash
pnpm version:preview
```

### Releases and Beta Pre-releases

The [Release workflow](.github/workflows/release.yml) publishes packages using the commit-bumped version already in `package.json` by default (`version: current`).

* **Stable release (`beta: false`):** Tags and publishes `vX.Y.Z` as the latest release.
* **Beta pre-release (`beta: true`):** Appends `-beta` (for example, `v8.19.3-beta`), marks the GitHub release as a pre-release, and leaves `main` at its commit-bumped version without adding a release commit to the branch. Because the Bump Version workflow already advances `X.Y.Z` on each eligible commit, the next beta uses the next bumped version (for example, `v8.19.4-beta`); if `vX.Y.Z-beta` already exists on another commit, push a version-bumping commit before cutting another beta.
* **Per-beta vs. cumulative stable notes:** Each beta pre-release lists only the changes since the previous release (stable or beta), while the next stable release combines all features, changes, and bug fixes across every intermediate beta since the previous stable release. SemVer ordering (`8.19.3-beta` < `8.19.4-beta` < `8.19.4`) ensures beta testers auto-update cleanly to newer betas and to the final stable release.

### Sync After an Automated Amendment

If you push directly to a managed branch, wait for the workflow to finish before continuing work on that branch. The bot pushes with a lease against the original tip, so it refuses to overwrite a newer push. Maintainers must configure branch rules to allow the workflow's force pushes.

Save the exact tip you are pushing on an unused backup branch. For example, on `main`:

```bash
git branch backup/before-version-amend
git push origin HEAD:main

# After the Bump Version workflow finishes:
git fetch origin
git rebase --onto origin/main backup/before-version-amend main
```

Commit or stash uncommitted edits before rebasing. This moves the local branch onto the amended history and replays only commits made after the saved push tip, if any. Rebasing those additional local commits can change their committer dates. Keep the backup until you have checked the result, and use a fresh backup name for each push. See Git's [upstream rebase recovery guidance](https://git-scm.com/docs/git-rebase#_recovering_from_upstream_rebase) for more detail.

Historical removal of old version-only bump commits is a separate maintainer operation using [backfill-versions.mjs](scripts/backfill-versions.mjs). That tool prepares and verifies a replacement history, saves the original history on a backup branch, and checks that non-version contents and existing staged/working edits are preserved before applying it. It does not push to a remote.

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
