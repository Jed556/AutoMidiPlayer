# Physical MIDI input

Select an input device on the Instrument page and press **Start**. Selection is
remembered; active sessions are not restored at launch. Navigation, minimization
and closing to the tray do not stop an active session. **Stop**, Panic, device
disconnection and app exit release its notes and pedals. Reconnection requires
Start again.

Listen Mode routes MIDI to the SoundFont synthesizer. Raw mode is chromatic;
adapted mode and game output use the selected instrument's exact pitches, without
the song's key offset, key markers or Smart transpose. Game keys use the current
Hold Notes setting: taps use Keypress Delay; holds end on MIDI Note Off. The tap
threshold is not applied because live note lengths are unknown. Adapted Listen
Mode follows this timing only when Follow Note Timing is enabled.

Notes, velocity, sustain, sostenuto, soft pedal, reset controllers, and channel
all-notes/all-sounds-off are supported. Program changes, pitch bend, aftertouch,
other controllers and SysEx are ignored. Backing songs and local previews have
independent output ownership; stopping one source must not silence the others.

## Platform requirements

- Windows: the game must be in the foreground. A game running at elevated
  integrity may require running AMP at the same integrity level.
- macOS: grant AMP access in **System Settings → Privacy & Security →
  Accessibility** when requested. Denial leaves MIDI discovery and Listen Mode
  available. The selected game's process/window profile must match the native game.
  Profiles that require a window title can also need Screen Recording permission
  for macOS to expose that title; title filters are never bypassed when it is missing.
- Linux X11: requires ALSA MIDI and a working X11 input connection. The selected
  game must be in the foreground.
- Linux Wayland: requires a desktop implementing the XDG RemoteDesktop keyboard
  portal. Start requests keyboard permission only. No screen capture or root
  access is requested. Keys go to the focused application; the portal cannot
  verify game identity. Returning to any AMP window suspends game keys. Stop or
  revoking the desktop session ends output. Switch to Listen Mode or X11 if the
  desktop does not implement the portal. No XWayland focus inference is used.
  Returning to AMP or ending the shared keyboard session also pauses backing
  game playback; it cannot continue sending keys without that authorization.

## Desktop acceptance checks

CI compiles/tests Windows, Linux and both macOS architectures without sending
physical input. These checks do not establish hardware or compositor behavior.
Before claiming runtime verification, run this matrix with a keyboard or virtual
MIDI sender: Windows, macOS, X11, GNOME Wayland, KDE Wayland.

1. Start without a song. Select a device; verify that selection alone is silent.
   Start in Listen Mode and verify polyphony, velocity, Note Off, zero-velocity
   Note Off, raw/adapted pitch handling and pedal release.
2. Switch to game output. Verify exact mapped keys, Shift/Ctrl bindings, taps,
   holds and simultaneous chords. On Windows/macOS/X11, switching away from the
   selected game must release keys and prevent new output.
3. Navigate to another page, minimize, and close to tray while sending MIDI.
   Input must remain active. Reopen the Instrument page and verify the same
   selection, connection and active note display.
4. Play a backing song sharing a held pitch/key with live input. Pause, seek,
   stop and finish the song; live holds must remain. Release the live note while
   the song holds it; the song must remain. Repeat with a local preview.
5. Change device, mapping, Listen Mode and Hold Notes while holding notes.
   Confirm release before the change and no replay until a fresh Note On.
6. Disconnect a held device, reconnect, then Start. Confirm release on detection,
   retained selection, no automatic restart, and no stale callback notes.
7. On macOS deny, then grant Accessibility access and retry. On both Wayland
   desktops deny/allow the portal, cancel a pending Start with Stop, revoke an
   active session, and return to AMP while holding notes. Confirm cleanup and
   useful errors. Denial must leave Listen Mode usable.
8. Test Panic, Clear Data, quitting and relaunching. Confirm no held keys or
   sustained voices survive and that relaunch never automatically starts input.

Record OS/compositor versions, device or virtual-port name, and pass/fail per
scenario. Keep untested entries explicitly unverified.
