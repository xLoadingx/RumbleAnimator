# RumbleAnimator Replay File Format

This document describes the structure of `.replay` files produced by RumbleAnimator.
It is intended for developers and technical users who want to read replay data outside
the game, such as for external tools or basic analysis.

---

## File Structure

A replay file is a ZIP archive containing two entries:

- `manifest.json`
- `replay`

## manifest.json

`manifest.json` is a UTF-8 encoded JSON file containing all metadata required to parse
the binary replay stream.

The manifest JSON contains the following fields:

| Field         | Type            | Description                                                                            |
|:--------------|:----------------|:---------------------------------------------------------------------------------------|
| Title         | string          | User-visible replay name when file starts with 'Replay'                                |
| CustomMap     | string          | Name of custom map, or custom map data if not base custom map, or empty string if none |
| Version       | string          | Format version string (e.g. `"1.0.3"`)                                                 |
| Scene         | string          | Scene name (e.g. `"Gym"`, `"Map0"`, `"Map1"`,`"Park"`)                                 |
| Date          | string          | ISO 8601 timestamp (local) of recording start.                                         |
| Duration      | float           | Total duration in seconds                                                              |
| FrameCount    | int             | Number of recorded frames                                                              |
| PedestalCount | int             | Number of pedestals recorded                                                           |
| MarkerCount   | int             | Number of timeline markers recorded                                                    |
| AvgPing       | int             | Average ping of local player (ms)                                                      |
| MaxPing       | int             | Maximum recorded ping of local player (ms)                                             |
| MinPing       | int             | Minimum recorded ping of local player (ms)                                             |
| TargetFPS     | int             | Target recording framerate                                                             |
| Players       | PlayerInfo[]    | List of players in the replay                                                          |
| Structures    | StructureInfo[] | List of all structures recorded in the replay                                          |
| Markers       | Marker[]        | List of timeline markers (manual or auto)                                              |
| Guid          | string          | Unique identifier for the replay                                                       |

Though the game does not always record exactly at what the stated FPS is.  
I'd recommend using the `Time` value of each chunk compared to the static FPS number.

---

### Players

`Players` is an array of player definitions.  
The order of this array defines the player indexing used throughout the replay.

Each player object contains:

| Field               | Type     | Description                               |
|---------------------|----------|-------------------------------------------|
| ActorId             | byte     | Photon actor number for the player        |
| MasterId            | string   | Player’s PlayFab master ID                |
| Name                | string   | Player’s display name                     |
| BattlePoints        | int      | Player battle points at time of recording |
| VisualData          | string   | Cosmetic data string                      |
| EquippedShiftStones | short[2] | IDs of equipped shiftstones               |
| Measurement.Length  | float    | Player length measurement                 |
| Measurement.ArmSpan | float    | Player arm span measurement               |
| WasHost             | bool     | True if this player was the host          |




---

### Structures

`Structures` is an array defining the structures present in the match.  
The order of this array defines the structure indexing used throughout the replay.

Each structure object contains:

- `Type` (enum value)

Defined structure types:

- Cube
- Pillar
- Wall
- Disc
- Ball
- CagedBall
- LargeRock
- SmallRock
- TetheredCagedBall

---

## replay (binary data)

The `replay` entry contains the binary replay stream, which contains all the actual data of the replay.  
It is compressed using Brotli and stored in the ZIP without additional ZIP compression.

## Replay Binary Stream

### Magic

The stream begins with:

- 4 bytes ASCII: `RPLY`

All values are written using BinaryWriter defaults:
- Little-endian numbers
- UTF-8 strings
- booleans are 1 byte

## Frames

The replay consists of `FrameCount` frames written sequentially.

Each frame is written in the following order:

1. Frame size
2. Frame timestamp
3. State entry count
4. State entries

### Frame Layout

    int32 FrameSize
    float Time
    int32 EntryCount
    StateEntry[EntryCount]

`FrameLength` specifies the number of bytes following it that belong to the frame.

Time is expressed in seconds since the start of the replay

---

## State Entries

Each frame contains only the states that changed relative to the previous frame.  
Unchanged states reuse the state from the previous frame.

Each state entry begins with:

    byte ChunkType
    int32 Index

`ChunkType` identifies what type of state is being updated:
    
    PlayerState
    StructureState
    PedestalState
    Event
    Extension

`Index` refers to the index in the corresponding manifest array (`Players`, `Structures`, etc.)  

`Index` for extensions refers to the ModID defined by the mod using the FNV hash algorithm

For `Event` entries, the index is unused and can be ignored on read.

---

## State Chunks

Each state entry is followed by its chunk data:

    int32 ChunkLength
    bytes ChunkData

`ChunkLength` specifies the exact number of bytes in `ChunkData`.

`ChunkData` contains a sequence of tagged fields.
Each field is encoded as:

    byte FieldId
    byte FieldSize
    bytes FieldValue[FieldSize]

Fields may appear in any order, but the defined field enum values must keep the same order as described below for each state.  
Unknown fields must be skipped using `FieldSize`

---

## StructureState Chunks

Structure state chunks describe the transform and status of a structure.

| Field        | Type        | Description                                     |
|--------------|-------------|-------------------------------------------------|
| position     | Vector3     | World-space position of the structure           |
| rotation     | Quaternion  | World-space rotation of the structure           |
| active       | bool        | Whether the structure is currently active       |
| grounded     | bool        | True if the structure is grounded               |
| isFlicked    | bool        | True if the structure is currently flicked      |
| isLeftHeld   | bool        | True if the structure is held by the left hand  |
| isRightHeld  | bool        | True if the structure is held by the right hand |
| currentState | byte (enum) | Current state                                   |
| isTargetDisk | bool        | True if this structure is a target disk         |

Structure States:

- Default: Default structure state
- Free: When the structure is *not* grounded and able to be moved freely
- Frozen: The structure is in hitstop
- FreeGrounded: Grounded movement during things like straight
- StableGrounded: The structure is grounded
- Float: The structure is parried
- Normal: The structure can be moved freely

---

## PlayerState Chunks

Player state chunks describe the pose and gameplay state of a player.

| Field       | Type       | Description                                  |
|-------------|------------|----------------------------------------------|
| VRRigPos    | Vector3    | Root player rig position                     |
| VRRigRot    | Quaternion | Root player rig rotation                     |
| LHandPos    | Vector3    | Left hand position                           |
| LHandRot    | Quaternion | Left hand rotation                           |
| RHandPos    | Vector3    | Right hand position                          |
| RHandRot    | Quaternion | Right hand rotation                          |
| HeadPos     | Vector3    | Headset position                             |
| HeadRot     | Quaternion | Headset rotation                             |
| Health      | int16      | Player’s current health                      |
| active      | bool       | If the player is active in this frame        |

---

## PedestalState Chunks
Pedestal state chunks describe the state of the pedestals in a match.

| Field     | Type     | Description                                  |
|-----------|----------|----------------------------------------------|
| position  | Vector3  | World-space pedestal position                |
| active    | bool     | Whether the pedestal is active in this frame |

---

## Event Chunks
Event chunks represent discrete one-time actions in the current frame of the replay.  
Unlike `PlayerState` or `StructureState` chunks, which describe continuous changes over time, events occur at a single moment and do not persist across frames.

| Field       | Type       |
|-------------|------------|
| type        | byte       | 
| position    | Vector3    | 
| rotation    | Quaternion |
| masterId    | string     | 
| playerIndex | int        | 
| damage      | int        |
| fxType      | byte       | 

The event types, along with their corresponding fields, are as followed:

- `OneShotFX`
  - `position`
  - `rotation`
  - `fxType`

Event types do not write to other fields than the ones specified above.

### FX One Shot Type

| Value              | Description                                  |
|--------------------|----------------------------------------------|
| None               | No effect                                    |
| StructureCollision | Structure collided with another structure    |
| Ricochet           | Structure slid against another structure     |
| Grounded           | Structure became grounded                    |
| GroundedSFX        | Grounded sound effect trigger                |
| Ungrounded         | Structure became ungrounded                  |
| DustImpact         | Structure broke                              |
| ImpactLight        | Light impact effect                          |
| ImpactMedium       | Medium impact effect                         |
| ImpactHeavy        | Heavy impact effect                          |
| ImpactMassive      | Very heavy impact effect                     |
| Spawn              | Structure spawn effect                       |
| Break              | Structure break effect                       |
| BreakDisc          | Disc break effect                            |
| RockCamSpawn       | RockCam camera spawn effect                  |
| RockCamDespawn     | RockCam camera despawn effect                |
| RockCamStick       | RockCam stick effect                         |
| Fistbump           | Fistbump effect                              |
| FistbumpGoin       | Extra Gear Coin gained at the end of a match |

---

## State Indexing

- Structure index `i` always refers to `Structures[i]` from the manifest
- Player index `i` always refers to `Players[i]` from the manifest
- Pedestal index `i` always refers to `Pedestals[i]` from the manifest

If a state for a given index does not appear in a frame, the state from the previous frame is reused.
