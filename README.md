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
</div>

A MIDI to key player for in-game instruments made using C# and WPF with Windows Mica design. This project is originally forked from **[sabihoshi/GenshinLyreMidiPlayer][GenshinLyreMidiPlayer]** and was later detached into its own repository to enable multi-game support and introduce features that don’t fit the original Genshin Impact–only use design.

<div align="center">
  <i>If you liked this project, consider <a href="CONTRIBUTING.md">contributing</a> or giving a 🌟 star. Thank you~</i>
</div>
<br/>

https://github.com/user-attachments/assets/8e7d8dec-33c4-4d2b-a268-4abd1dbac405

### Supported Games and Instruments
- **Genshin Impact** - Windsong Lyre, Floral Zither, Vintage Lyre
- **Heartopia** - Piano (All variations), 15-key instruments (e.g. lyre, wooden bass, violin, etc.)
- **Roblox** - Piano (61-key)
- **Sky: Children of the Light** - All available Sky instruments as of Feb 2026
- **NTE (Neverness to Everness)** - Piano (21-Key)

See the [Support wiki page][wiki-support] for details on supported games, instruments, and keyboard layouts.

## Quick Start

1. [Download][latest] the app and then run, no need for installation.
2. Open a .mid file by pressing the **+** button at the top left.
3. Enable the tracks that you want to be played back.
4. Press play and it will automatically switch to the target game window.
5. Automatically stops playing if you switch to a different window.

> [!NOTE]
> If you get a SmartScreen popup, click on "More info" and then "Run anyway"
> The reason this appears is because the application is not signed. Signing costs money which can get very expensive.

## Features

### Core Features
* **Multi-game support** - Play on Genshin Impact, Sky, Roblox and Heartopia
* **Spotify-style UI** - Modern player interface with fixed bottom controls
* **Per-song Settings** - Key offset, transpose, speed, and BPM settings are saved for each song

### Instrument Playback
* Test MIDI files through speakers before playing in-game
* Change keyboard layouts (QWERTY, QWERTZ, AZERTY, DVORAK, etc.)
* Hold and merge nearby notes. _Some songs sound better when merged ([#4](https://github.com/sabihoshi/GenshinLyreMidiPlayer/issues/4))_
* Play using your own MIDI Input Device

https://github.com/user-attachments/assets/e10a31d2-419c-4f41-bc1d-3f12cee36c0d

### MIDI Track Management
* Play multiple tracks of a MIDI file simultaneously
* Turn on/off tracks in realtime

https://github.com/user-attachments/assets/2519cab3-521f-4862-9af7-8404a1656582

### Piano Sheet
The Piano Sheet allows you to easily share songs to other people, or for yourself to try. You can change the delimiter as well as the split size, and spacing. This will use the current keyboard layout that you have chosen.

> No preview yet

### Queue
A queue allows you to play songs without having to open or delete a song or file.

https://github.com/user-attachments/assets/e23776fa-2191-455e-bc6b-5518a969943b

### Theming
You can set the player to light mode/dark mode and change its accent color.

https://github.com/user-attachments/assets/f249be17-566c-4a4f-856b-9b03f55592ef

## About

### What are MIDI files?
MIDI files (.mid) are a set of instructions that play various instruments on what are called tracks. You can enable specific tracks that you want it to play. It converts the notes on the track into keyboard inputs for the game. Currently it is tuned to C major.

### Can this get me banned?
The short answer is that it's uncertain. Use it at your own risk. Do not play songs that will spam the keyboard, listen to the MIDI file first and make sure to play only one instrument so that the tool doesn't spam keyboard inputs.
* For Genshin Impact, here is [miHoYo's response](https://genshin.mihoyo.com/en/news/detail/5763) to using 3rd party tools.
* For Heartopia, here is their [Official Discord message](https://discord.com/channels/1128257488375005215/1460985755529773301/1465702188700405986) about using 3rd party tools.
* For Sky, see their policy on [third-party apps](https://thatgamecompany.helpshift.com/hc/en/17-sky-children-of-the-light/faq/1250-can-i-use-or-create-third-party-applications-like-mods-or-bots/).
* For Roblox, refer to their [third-party services Terms of Use](https://en.help.roblox.com/hc/en-us/articles/115004647846-Roblox-Terms-of-Use#third-party-services).


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

If you just want to run the app, download the latest [release][latest].

### Requirements
* [Git](https://git-scm.com)
* [.NET 8.0](https://dotnet.microsoft.com/download) SDK or later

### Build and run
```bat
git clone https://github.com/Jed556/AutoMidiPlayer.git
cd AutoMidiPlayer

dotnet build
dotnet run --project AutoMidiPlayer.WPF
```

For publish options and advanced setup, see [Getting Started][wiki-getting-started].

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md).  
For adding notes, instruments, games, or keyboard mappings, see [Getting Started][wiki-getting-started]

# Special Thanks
This project is inspired by and built on top of **[sabihoshi/GenshinLyreMidiPlayer][GenshinLyreMidiPlayer]** [v4.0.5](https://github.com/sabihoshi/GenshinLyreMidiPlayer/releases/tag/v4.0.5). Huge thanks for the original work!
#### Projects that inspired GenshinLyreMidiPlayer
* **[ianespana/ShawzinBot](https://github.com/ianespana/ShawzinBot)** - Original inspiration for the concept
* **[yoroshikun/flutter_genshin_lyre_player](https://github.com/yoroshikun/flutter_genshin_lyre_player)** - Ideas for history and fluent design
* **[Lantua](https://github.com/lantua)** - Music theory guidance (octaves, transposition, keys, scales)

# License
* This project is under the [MIT](LICENSE.md) license.
* Originally created by [sabihoshi][GenshinLyreMidiPlayer]. Modified by [Jed556](https://github.com/Jed556) for multi-game support and modernization.
* All rights reserved by © miHoYo Co., Ltd., © XD Inc., © thatgamecompany, Inc., and © Roblox Corporation. This project is not affiliated nor endorsed by miHoYo, XD, thatgamecompany, or Roblox. Genshin Impact™, Heartopia™, Sky: Children of the Light™, Roblox™, and other properties belong to their respective owners.
* This project uses third-party libraries or other resources that may be distributed under [different licenses](THIRD-PARTY-NOTICES.md).

<br/>

> Demo videos and screenshots are for illustrative purposes only and may not reflect the latest version of the application. Actual features, UI, and supported games/instruments may vary. Please refer to the [latest release][latest] and Wiki for up-to-date information.

> [!NOTE]
> I don't have knowledge about music theory, if you find any issues with note mappings or transpositions, please open an issue or PR. Thank you! 💖

[latest]: https://github.com/Jed556/AutoMidiPlayer/releases/latest
[GenshinLyreMidiPlayer]: https://github.com/sabihoshi/GenshinLyreMidiPlayer
[wiki-home]: https://github.com/Jed556/AutoMidiPlayer/wiki
[wiki-getting-started]: https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started
[wiki-how-to-use]: https://github.com/Jed556/AutoMidiPlayer/wiki/How-to-Use
[wiki-how-it-works]: https://github.com/Jed556/AutoMidiPlayer/wiki/How-it-Works
[wiki-support]: https://github.com/Jed556/AutoMidiPlayer/wiki/Support
[wiki-faq]: https://github.com/Jed556/AutoMidiPlayer/wiki/FAQ-General
[wiki-disclaimer]: https://github.com/Jed556/AutoMidiPlayer/wiki/Disclaimer
