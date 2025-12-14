# RumbleAnimator

RumbleAnimator is a replay and animation system for RUMBLE VR that records scenes into a binary format and allows them to be replayed.

It is primarily intended for:
- Match analysis
- Animation and cinematic capture
- Debugging gameplay behavior
- External tooling (via the replay format)

---

## Features
- Records player and structure transforms
- Delta-compressed frame storage
- Brotli-compressed replay stream
- ZIP-based container with manifest metadata
- Forward and backward compatible, field-based binary format.
- ImHex pattern file for inspecting replay data

---

## Planned Work

### Playback
- Add automatic legacy camera for each player POV
- Keybinds for frame seeking
- Add in-game menu for frame controls and replay selection
- Fix playback framerate not matching recording framerate
- Convert pause into a timescale system (timescale = 0 for pausing)
- Ensure all VFX repsect replay timescale
- Allow recording while a replay is playing (recording recordings)
- Support non-deterministic playback (structures follow live stacks instead of teleporting)
  
- Replay table for browsing, selecting, and entering replays

### Recording
- Auto-record toggle for matches (matchmaker)
- Auto-record toggle for parks (park board)
- Custom hand gesture to start/stop recording
- Add SFX for recording, playback, controls, and UI interactions

### Visuals
- Add VFX to structures (hold, flick, explode, etc.)
- Fix legs not being pulled up while in the air
- Fix client jittering
- Fix structures accidentally hitting players mid-flight

### Game States
- Add pedestals for matches
- Fix players who joined later in the recording being added incorrectly
- Fix replays in parks not loading correctly due to networking

---

## Replay Format
The replay format is documented [here](ReplayFormat).

- [Binary format spec](ReplayFormat/README.md): `ReplayFormat/README.md`
- [ImHex pattern](ReplayFormat/ReplayFile.hexpat): `ReplayFormat/ReplayFile.hexpat`

External tools can read replays using the documented format.
