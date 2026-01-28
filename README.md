<div align="center">
  <br>
  <h1>
    Auto MIDI Player【AMP】
  </h1>
  <p>
    <a href="https://github.com/Jed556/AutoMidiPlayer/releases"><img alt="GitHub release (latest by date including pre-releases)" src="https://img.shields.io/github/v/release/Jed556/AutoMidiPlayer?include_prereleases&color=35566D&logo=github&logoColor=white&label=latest"></a>
    <a href="https://github.com/Jed556/AutoMidiPlayer/releases/latest"><img alt="GitHub downloads" src="https://img.shields.io/github/downloads/Jed556/AutoMidiPlayer/total?label=downloads&logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAA2klEQVQ4jZ2SMWpCQRCGv5WHWKQIHsAj5Ah2IR7ByhvYpUiVxkqipPCE5gKKBB5Y+KXIIzzXWX3mh2FhZ/5vZ3YXAqkzdavumtiqs6g2MvfV2kvVaj+v7wWMChgE+4MmdxMQ7RVz14r/Dbirg7+Z1BHw2ERJT+oe2KeUvs4y6ntw8yUtLtAq6rqDeaPG/XWAlM0Z5KOzWZ2owwCybJk/c7M6VCf4+0XHhU5e1bfoZHWs1hVwInjflBLA6vrAnCrgADyrxwZGa83Va60vwCGpU2ADPNw4Ldc3MP8Bk60okvXOxJoAAAAASUVORK5CYII="></a>
  </p>
</div>

A MIDI to key player for in-game instruments made using C# and WPF with Windows Mica design. This project is originally forked from **[sabihoshi/GenshinLyreMidiPlayer][GenshinLyreMidiPlayer]** and was later detached into its own repository to enable multi-game support and introduce features that don’t fit the original Genshin Impact–only use design.

<div align="center">
  <i>If you liked this project, consider <a href="https://github.com/Jed556/AutoMidiPlayer?tab=contributing-ov-file">contributing</a> or giving a 🌟 star. Thank you~</i>
</div>

### Supported Games and Instruments
- **Genshin Impact** - Windsong Lyre, Floral Zither, Vintage Lyre
- **Heartopia** - Piano

*Image: Main player showing playlist and playback controls.*

## How to use

1. [Download][latest] the program and then run, no need for installation.
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
* **Per-song Settings** - Key, speed, and transpose settings are saved per song
  - **Track Management** - Enable/disable individual MIDI tracks with detailed statistics
  - **Transposition** - Change the key with automatic note transposition
  - **Speed Control** - Adjust playback speed from 0.1x to 4.0x
  - **BPM Control** - Set a custom BPM for the song
* Written in C# WPF with Windows Mica design

### Playback
* Play multiple tracks of a MIDI file simultaneously
* Test MIDI files through speakers before playing in-game
* Change keyboard layouts (QWERTY, QWERTZ, AZERTY, DVORAK, etc.)
* Auto-play at a scheduled time
* Find songs using the search box

### Piano Sheet
The Piano Sheet allows you to easily share songs to other people, or for yourself to try. You can change the delimiter as well as the split size, and spacing. This will use the current keyboard layout that you have chosen.

*GIF: Piano Sheet example demonstrating delimiter, split size, and spacing options.*

### Play using your own MIDI Input Device
If you have your own MIDI instrument, this will let you play directly to the in-game instrument. This lets you play directly without using a MIDI file.

### Playlist Controls
A playlist allows you to play songs without having to open or delete a song or file.

*Screenshot: Playlist and history panel showing song queue.*

### Hold notes & Merge nearby notes
  - You can set the player to hold sustained notes (does not really make a difference. Off by default.)
* Some songs sound better when nearby notes are merged see [#4](https://github.com/Jed556/AutoMidiPlayer/issues/4) for an example

### Theming
You can set the player to light mode/dark mode and change its accent color.

*Image: Theming examples.*

## About

### What are MIDI files?
MIDI files (.mid) is a set of instructions that play various instruments on what are called tracks. You can enable specific tracks that you want it to play. It converts the notes on the track into keyboard inputs for the game. Currently it is tuned to C major.

### Can this get me banned?
The short answer is that it's uncertain. Use it at your own risk. Do not play songs that will spam the keyboard, listen to the MIDI file first and make sure to play only one instrument so that the tool doesn't spam keyboard inputs. For Genshin Impact, [here is miHoYo's response](https://genshin.mihoyo.com/en/news/detail/5763) to using 3rd party tools.

## Pull Request Process

1. Do not include the build itself where the project is cleaned using `dotnet clean`.
2. Update the README.md with details of changes to the project, new features, and others that are applicable.
3. Increase the version number of the project and the README.md to the new version that this
   Pull Request would represent. The versioning scheme we use is [SemVer](http://semver.org).
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
* This project is inspired by and revamped from **[sabihoshi/GenshinLyreMidiPlayer][GenshinLyreMidiPlayer]**. Huge thanks for the original work!
* **[ianespana/ShawzinBot](https://github.com/ianespana/ShawzinBot)** - Original inspiration for the concept *`~GenshinLyreMidiPlayer`*
* **[yoroshikun/flutter_genshin_lyre_player](https://github.com/yoroshikun/flutter_genshin_lyre_player)** - Ideas for history and fluent design *`~GenshinLyreMidiPlayer`*
* **[Lantua](https://github.com/lantua)** - Music theory guidance (octaves, transposition, keys, scales) *`~GenshinLyreMidiPlayer`*

# License
* This project is under the [MIT](https://github.com/Jed556/AutoMidiPlayer?tab=MIT-1-ov-file) license.
* Originally created by [sabihoshi][GenshinLyreMidiPlayer]. Modified by [Jed556](https://github.com/Jed556) for multi-game support and modernization.
* All rights reserved by © miHoYo Co., Ltd. and © XD Inc. This project is not affiliated nor endorsed by miHoYo or XD. Genshin Impact™, Heartopia™, and other properties belong to their respective owners.
* This project uses third-party libraries or other resources that may be
distributed under [different licenses](THIRD-PARTY-NOTICES.md).

[latest]: https://github.com/Jed556/AutoMidiPlayer/releases/latest
[GenshinLyreMidiPlayer]: https://github.com/sabihoshi/GenshinLyreMidiPlayer
