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

- `Version` (string)
- `Scene` (string)
- `DateUTC` (string)

- `FrameCount` (int)
- `StructureCount` (int)
- `FPS` (int)

- `Players` (array)
- `Structures` (array)

Though the game does not always record exactly at what the stated FPS is.  
I'd recommend using the `Time` value of each chunk compared to the static FPS number.

---

### Players

`Players` is an array of player definitions.  
The order of this array defines the player indexing used throughout the replay.

Each player object contains:

- `ActorId` (byte)
- `MasterId` (string)
- `Name` (string)
- `BattlePoints` (int)
- `VisualData` (string)
- `EquippedShiftStones` (array of 2 int16 values)
- `Measurement.Length` (float)
- `Measurement.ArmSpan` (float)
- `WasHost` (bool)

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

1. Frame timestamp
2. State Entries

### Timestamp

    float Time

Time is expressed in seconds since the start of the replay.

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

`Index` refers to the index in the corresponding manifest array (`Players`, `Structures`, etc.)

---

## State Chunks

Each state entry is immediately followed by its chunk data:

    int32 ChunkLength
    bytes ChunkData

`ChunkLength` specifies the exact number of bytes in `ChunkData`.

`ChunkData` contains a sequence of tagged fields.
Each field is encoded as:

    byte FieldId
    field value

Fields may appear in any order, but keep the defined field values the same order as described below for each state.
---

## StructureState Chunks

Structure state chunks describe the transform and status of a structure.

Defined structure fields:

- position -> Vector3 (3 floats)
- rotation -> Quaternion (4 floats)
- active -> bool
- grounded -> bool

---

## PlayerState Chunks

Player state chunks describe the pose and gameplay state of a player.

Defined player fields:

- VRRigPos -> Vector3
- VRRigRot -> Quaternion
- LHandPos -> Vector3
- LHandRot -> Quaternion
- RHandPos -> Vector3
- RHandRot -> Quaternion
- HeadPos -> Vector3
- HeadRot -> Quaternion
- currentStack -> int16
- Health -> int16
- active -> bool

---

## State Indexing

- Structure index `i` always refers to `Structures[i]` from the manifest
- Player index `i` always refers to `Players[i]` from the manifest
- et cetera for each state array defined in `manifest.json`

If a state for a given index does not appear in a frame, the state from the previous frame is reused.
