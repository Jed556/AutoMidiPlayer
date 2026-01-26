# 【 Auto MIDI Player 】

A MIDI to key player for in-game instruments made using C# and WPF with Windows Mica design.

### Supported Games
- **Genshin Impact** - Windsong Lyre, Floral Zither, Vintage Lyre
- **Heartopia** - 15 keys, 37 keys

If you enjoyed this project, consider [contributing](#contributing) or 🌟 starring the repository. Thank you~

## **[Download latest version][latest]** [![GitHub all releases](https://img.shields.io/github/downloads/Jed556/AutoMidiPlayer/total?style=social)][latest] [![GitHub release (latest by date)](https://img.shields.io/github/v/release/Jed556/AutoMidiPlayer)][latest]

Screenshot: Main player showing playlist and playback controls (landscape UI with bottom player bar).

## How to use

1. [Download the program][latest] and then run, no need for installation.
2. Open a .mid file by pressing the open file button at the top left.
3. Enable the tracks that you want to be played back.
4. Press play and it will automatically switch to the target game window.
5. Automatically stops playing if you switch to a different window.

> If you get a SmartScreen popup, click on "More info" and then "Run anyway"
> The reason this appears is because the application is not signed. Signing costs money which can get very expensive.

## Features

### Core Features
* **Multi-game support** - Play on Genshin Impact (Lyre, Zither, Vintage Lyre) and Heartopia (15-key, 22-key, 37-key)
* **Spotify-style UI** - Modern player interface with fixed bottom controls
* **Track Management** - Enable/disable individual MIDI tracks with detailed statistics (note count, black key ratio, avg duration)
* **Per-song Settings** - Key, speed, and transpose settings are saved per song
* **Transposition** - Change the key with automatic note transposition
* **Speed Control** - Adjust playback speed from 0.1x to 4.0x
* Written in C# WPF with Windows 11 Mica design

### Playback
* Play multiple tracks of a MIDI file simultaneously
* Test MIDI files through speakers before playing in-game
* Change keyboard layouts (QWERTY, QWERTZ, AZERTY, DVORAK, etc.)
* Auto-play at a scheduled time
* Filter tracks using the search box

# Piano Sheet [![](https://img.shields.io/badge/v2.1.0.1-New!-yellow)](https://github.com/Jed556/AutoMidiPlayer/releases/tag/v2.1.0.1)
The first version of the Piano Sheet has been added, this allows you to easily share songs to other people, or for yourself to try. You can change the delimiter as well as the split size, and spacing. This will use the current keyboard layout that you have chosen.

Animation: Piano Sheet example demonstrating delimiter, split size, and spacing options.

### Media Controls
You can now control the Lyre natively by using your media controls that some keyboards have as special function keys. This integrates with other music applications as well.

Illustration: Media control integration with keyboard function keys.

### Play using your own MIDI Input Device
If you have your own MIDI instrument, this will let you play directly to the in-game instrument. This lets you play directly without using a MIDI file.

### Playlist Controls & History
A playlist allows you to play songs continuously without having to open a new file after a song has finished.

Screenshot: Playlist and history panel showing song queue.

### Hold notes & Merge nearby notes
  - You can set the player to hold sustained notes (does not really make a difference. Off by default.)
* Some songs sound better when nearby notes are merged see [#4](https://github.com/Jed556/AutoMidiPlayer/issues/4) for an example

### Light Mode
You can set the player to light mode/dark mode (uses your system's theme by default.)

Screenshot: Light and dark theme examples.

### Mini Mode
You can resize the player as small as you want and it should close the panels accordingly.

Screenshot: Mini mode showing compact UI.

### Drag and Drop
Drag and drop MIDI files directly into the player window to add them to your playlist.

## About

### What are MIDI files?
MIDI files (.mid) is a set of instructions that play various instruments on what are called tracks. You can enable specific tracks that you want it to play. It converts the notes on the track into keyboard inputs for the game. Currently it is tuned to C major.

### Can this get me banned?
The short answer is that it's uncertain. Use it at your own risk. Do not play songs that will spam the keyboard, listen to the MIDI file first and make sure to play only one instrument so that the tool doesn't spam keyboard inputs. For Genshin Impact, [here is miHoYo's response](https://genshin.mihoyo.com/en/news/detail/5763) to using 3rd party tools.

## Pull Request Process

1. Do not include the build itself where the project is cleaned using `dotnet clean`.
2. Update the README.md with details of changes to the project, new features, and others that are applicable.
3. Increase the version number of the project and the README.md to the new version that this
   Pull Request would represent. The versioning scheme we use is [SemVer](http://semver.org/).
4. You may merge the Pull Request in once you have the the approval of the maintainers.

## Build
If you just want to run the program, there are precompiled releases that can be found in [here](https://github.com/Jed556/AutoMidiPlayer/releases).
### Requirements
* [Git](https://git-scm.com) for cloning the project
* [.NET 8.0](https://dotnet.microsoft.com/download) SDK or later

#### Publish a single binary for Windows
```bat
git clone https://github.com/Jed556/AutoMidiPlayer.git
cd AutoMidiPlayer

dotnet publish AutoMidiPlayer.WPF -r win-x64 ^
               -c Release --self-contained false -p:PublishSingleFile=true
```
> For other runtimes, visit the [RID Catalog](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog) and change the runtime value.

#### Build the project (not necessary if you published)
```bat
git clone https://github.com/Jed556/AutoMidiPlayer.git
cd AutoMidiPlayer

dotnet build
```

#### Publish the project using defaults
```bat
git clone https://github.com/Jed556/AutoMidiPlayer.git
cd AutoMidiPlayer

dotnet publish
```

# Special Thanks
* This project is inspired by and revamped from **[sabihoshi/GenshinLyreMidiPlayer](https://github.com/sabihoshi/GenshinLyreMidiPlayer)**. Huge thanks for the original work!
* **[ianespana/ShawzinBot](https://github.com/ianespana/ShawzinBot)** - Original inspiration for the concept
* **[yoroshikun/flutter_genshin_lyre_player](https://github.com/yoroshikun/flutter_genshin_lyre_player)** - Ideas for history and fluent design
* **[Lantua](https://github.com/lantua)** - Music theory guidance (octaves, transposition, keys, scales)

# License
* This project is under the [MIT](LICENSE.md) license.
* Originally created by [sabihoshi](https://github.com/sabihoshi/GenshinLyreMidiPlayer). Modified by [Jed556](https://github.com/Jed556) for multi-game support.
* All rights reserved by © miHoYo Co., Ltd. and © XD. This project is not affiliated nor endorsed by miHoYo or XD. Genshin Impact™, Heartopia™, and other properties belong to their respective owners.
* This project uses third-party libraries or other resources that may be
distributed under [different licenses](THIRD-PARTY-NOTICES.md).

[latest]: https://github.com/Jed556/AutoMidiPlayer/releases/latest
