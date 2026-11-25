<!-- release-prepared: v8.19.4-beta -->

### ✨ What's New

- Extended guitar support & add config checking ([5155e9c](https://github.com/Jed556/AutoMidiPlayer/commit/5155e9cfef0a0e13cb37988fe6e36a62fb7e148d))
- Animate song row context menu and add Liked Songs action ([274cdbd](https://github.com/Jed556/AutoMidiPlayer/commit/274cdbdc304a519e8c568a9cea35597fe0bc41e8))
- Integrate liked songs ([2d2c6ec](https://github.com/Jed556/AutoMidiPlayer/commit/2d2c6ec042e9aa51877c68dd52f5d3e384026cd3))
- Implement support for playlists ([4080587](https://github.com/Jed556/AutoMidiPlayer/commit/408058784e7c67d3c943216b8dd6e24638068483))
- Add NTE layout & adjust engine ([6fd5500](https://github.com/Jed556/AutoMidiPlayer/commit/6fd55008474f6a1780c2be9d45eac2052c7cbfba))
- Add Guitar Pro support ([f1539d7](https://github.com/Jed556/AutoMidiPlayer/commit/f1539d7a2e0407d3bcf3a4616b7c93bf6efe6d3b))

### 🛠️ Changes

- Adjust Sentry logging ([9007e3f](https://github.com/Jed556/AutoMidiPlayer/commit/9007e3f685b94c7c91d038eeab546ad225169255))
- Fix volume icon inf scaling ([6cb0d4d](https://github.com/Jed556/AutoMidiPlayer/commit/6cb0d4d6d19934d49eadfb7b1b00a2d24e419417))
- Handle album icon in media controls ([21a7067](https://github.com/Jed556/AutoMidiPlayer/commit/21a7067481b21eb91aeeba60d01b6bfece3da3e9))
- Adjust config, UI and chord/fret handling ([0eadb42](https://github.com/Jed556/AutoMidiPlayer/commit/0eadb428b0fe44b8f1820aee51c5be06c273549f))
- Move Sentry metrics out of issues ([7b53a26](https://github.com/Jed556/AutoMidiPlayer/commit/7b53a26ba1c1b330a0d57310488569aabdde0a4b))
- Minor guitar wheel adjustments ([a6cdee9](https://github.com/Jed556/AutoMidiPlayer/commit/a6cdee97932492194fdf996d3c52af8a326701af))

### 🐛 Bug Fixes

- Deduplicate file issues by path to prevent dialog crash ([9734fac](https://github.com/Jed556/AutoMidiPlayer/commit/9734facf76dd8e8833ca3a7a0d1de876cf3773de))
- Disable native inspect context menu on left navigation and UI chrome ([da0e643](https://github.com/Jed556/AutoMidiPlayer/commit/da0e643b9d1d98c37d95f67d16e74dce95eef1cc))
- Prevent TypeError when clearing BPM input in EditDialog ([e720b13](https://github.com/Jed556/AutoMidiPlayer/commit/e720b13520ee1772342acca138ce4966b75a3120))
- Iteratively strip HTML comments in markdown parser ([dd07e2f](https://github.com/Jed556/AutoMidiPlayer/commit/dd07e2f28f2f9e12b5ca893dc1b82f88aa4e2e69))
- Scheduling and transition-budget fix for macOS ([fb28dd0](https://github.com/Jed556/AutoMidiPlayer/commit/fb28dd052427f826e2e5607f4c4fc55923aedb17))

---

### 📥 Downloads & Installation

| Platform | Package | Notes |
| :--- | :--- | :--- |
| **Windows x64** | [`AutoMidiPlayer_v8.19.4-beta_windows-x64-setup.exe`](https://github.com/Jed556/AutoMidiPlayer/releases/download/v8.19.4-beta/AutoMidiPlayer_v8.19.4-beta_windows-x64-setup.exe) | Standard silent-updating installer |
| **Windows x64** | [`AutoMidiPlayer_v8.19.4-beta_windows-x64-portable.zip`](https://github.com/Jed556/AutoMidiPlayer/releases/download/v8.19.4-beta/AutoMidiPlayer_v8.19.4-beta_windows-x64-portable.zip) | Extract and run `Auto MIDI Player.exe` (no install required) |
| **macOS (Apple Silicon)** | [`AutoMidiPlayer_v8.19.4-beta_macos-arm64.dmg`](https://github.com/Jed556/AutoMidiPlayer/releases/download/v8.19.4-beta/AutoMidiPlayer_v8.19.4-beta_macos-arm64.dmg) | Drag to `/Applications` (first launch needs **Open Anyway** — see [macOS launch notes](https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started#macos-first-launch)) |
| **macOS (Intel)** | [`AutoMidiPlayer_v8.19.4-beta_macos-x64.dmg`](https://github.com/Jed556/AutoMidiPlayer/releases/download/v8.19.4-beta/AutoMidiPlayer_v8.19.4-beta_macos-x64.dmg) | Drag to `/Applications` (first launch needs **Open Anyway** — see [macOS launch notes](https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started#macos-first-launch)) |
| **Linux (AppImage)** | [`AutoMidiPlayer_v8.19.4-beta_linux-x64.AppImage`](https://github.com/Jed556/AutoMidiPlayer/releases/download/v8.19.4-beta/AutoMidiPlayer_v8.19.4-beta_linux-x64.AppImage) | Standalone AppImage |
| **Linux (Debian / Ubuntu)** | [`AutoMidiPlayer_v8.19.4-beta_linux-x64.deb`](https://github.com/Jed556/AutoMidiPlayer/releases/download/v8.19.4-beta/AutoMidiPlayer_v8.19.4-beta_linux-x64.deb) | Debian package |

> [!NOTE]
> macOS and Linux support should work in theory, however it isn't tested due to my system's limitations, so help in testing would be appreciated!

---

### 📖 Documentation & Wiki

- Check out the [Wiki & Getting Started Guide](https://github.com/Jed556/AutoMidiPlayer/wiki) for detailed setup and usage tutorials.
- Found a bug or have a suggestion? Open an issue on our [GitHub Issues](https://github.com/Jed556/AutoMidiPlayer/issues) page!

**Full Changelog**: [v8.13.9...v8.19.4-beta](https://github.com/Jed556/AutoMidiPlayer/compare/v8.13.9...v8.19.4-beta)
