# Roblox Piano Keymap Support

## Overview
This implementation adds support for Roblox virtual pianos using the MIRP (MIDI Input to Roblox Piano) layout format. The system maps MIDI notes to keyboard characters, enabling automated MIDI playback on Roblox piano games.

## Features

### 61-Note Chromatic Piano
- **Full Range**: C2 (MIDI 36) to C7 (MIDI 96)
- **Chromatic Scale**: All notes including sharps/flats
- **Character-Based Input**: Maps notes to keyboard characters

### MIRP Layout
The default layout follows the MIRP standard from [GreatCorn/MIRP](https://github.com/GreatCorn/MIRP):

```
Row 1 (Top):    1 ! 2 @ 3 4 $ 5 % 6 ^ 7 8 * 9 ( 0
Row 2 (QWERTY): q Q w W e E r t T y Y u i I o O p P
Row 3 (ASDF):   a s S d D f g G h H j J k l L
Row 4 (ZXCV):   z Z x c C v V b B n m
```

### Shift Key Handling
The system automatically handles Shift key requirements:
- **Uppercase letters** (Q, W, E, etc.) → Shift + lowercase
- **Symbols** (!, @, #, $, etc.) → Shift + number
- **Lowercase letters** and numbers → No Shift

## Usage

### Selecting Roblox Piano
1. Open AutoMidiPlayer
2. In the instrument dropdown, select **"Roblox Piano"**
3. The MIRP layout will be automatically applied
4. Open your MIDI file and play

### How It Works
When a MIDI file plays:
1. Each MIDI note is mapped to its corresponding character
2. The system determines if Shift is required
3. Key presses are simulated with proper timing
4. For shifted characters: Shift+Key+Release Shift

### Note Mapping Example
```
MIDI 36 (C2)  → '1'
MIDI 37 (C#2) → '!' (Shift+1)
MIDI 38 (D2)  → '2'
MIDI 60 (C4)  → 'q'
MIDI 61 (C#4) → 'Q' (Shift+q)
```

## Custom Keymaps (.mlf files)

### File Format
MIRP Layout Files (`.mlf`) are plain text files with:
- Exactly 61 lines
- One character per line
- Each line maps to a sequential MIDI note (36-96)

### Creating Custom Layouts
1. Create a text file with 61 lines
2. Each line should contain one keyboard character
3. Save with `.mlf` extension
4. Use `KeymapService.LoadKeymapFromFile()` to load

### Example Custom Layout
```
a
A
b
B
c
...
(61 characters total)
```

## Technical Implementation

### Key Components

1. **KeymapLayout.cs** (`AutoMidiPlayer.Data/Models`)
   - Model for 61-character keymap layouts
   - Load/Save .mlf files
   - Get character for MIDI note

2. **KeymapService.cs** (`AutoMidiPlayer.Data/Services`)
   - Manage keymap layouts
   - Default MIRP layout
   - Validation

3. **RobloxKeyboardLayouts.cs** (`Core/Games/Roblox`)
   - Character to VirtualKeyCode mapping
   - Shift detection (133 characters mapped)
   - Layout configuration

4. **RobloxKeyboardPlayer.cs** (`Core/Games/Roblox`)
   - Character-based note playback
   - Shift key handling
   - Win32 SendInput API integration

5. **RobloxInstruments.cs** (`Core/Games/Roblox/Instruments`)
   - Piano instrument configuration
   - 61-note chromatic scale

### Integration with Existing System
The implementation integrates seamlessly with the existing architecture:
- Automatically discovered via reflection (no manual registration)
- Routes through existing `KeyboardPlayer` API
- Compatible with all existing features (transposition, speed control, etc.)

## Transposition Support
If MIDI notes fall outside the 61-note range:
- Notes are automatically transposed up/down by octaves
- Existing transpose settings apply normally
- User can adjust key/octave in settings

## Limitations
1. **Single Layout**: Currently uses default MIRP layout only
2. **Windows Only**: Uses Win32 SendInput API
3. **No Visual Keyboard**: No on-screen keyboard display (yet)
4. **Fixed Base Note**: Always uses C2 (MIDI 36) as base

## Future Enhancements
- [ ] UI for loading custom .mlf files
- [ ] Per-song keymap selection
- [ ] Visual keyboard overlay
- [ ] Keymap editor/designer
- [ ] Multiple Roblox piano variants
- [ ] Configurable base note

## References
- **MIRP Project**: https://github.com/GreatCorn/MIRP
- **Layout Format**: Based on default.mlf from MIRP
- **Documentation**: MIRP's LayoutDesigner.htm

## Example Usage (Code)

### Loading Default Layout
```csharp
var layout = KeymapService.GetDefaultRobloxLayout();
// layout.Notes contains 61 characters
```

### Loading Custom Layout
```csharp
var layout = KeymapService.LoadKeymapFromFile("custom.mlf");
if (KeymapService.ValidateKeymap(layout))
{
    // Use layout
}
```

### Playing a Note
```csharp
// Automatically handled by KeyboardPlayer
KeyboardPlayer.PlayNote(noteId: 60, layoutName: "MIRP Default", instrumentId: "Roblox Piano");
// Internally routes to RobloxKeyboardPlayer for character-based input
```

## Testing
Since the project is Windows-specific and requires Roblox to be running:
1. Build the project on Windows
2. Launch Roblox and open a piano game
3. Select "Roblox Piano" instrument
4. Play a MIDI file
5. Verify notes play correctly in Roblox

## Support
For issues or questions:
- Open an issue on GitHub
- Check the MIRP project for layout information
- Review the code comments for implementation details
