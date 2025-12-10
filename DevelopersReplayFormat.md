# **RumbleAnimator Replay File Format (External Tools Spec)**

This describes the binary layout of `.replay` files generated.  
It is intended for developers who want to parse replays in external software such as Blender.

## **1. Container Format**

Every replay file uses a two-layer structure:

### **Outer Wrapper (compressed container)**

```
4 bytes: "RGZP"
int32  originalUncompressedSize
int32  compressedSize
bytes  GZIP-compressed replay data
```

Only the inner replay data is documented below.

## **2. Inner Replay Format**

The inner data always begins with the magic string:

```
6 bytes: "REPLAY"
```

After that, all content is written with `BinaryWriter` defaults (UTF-8 strings, little-endian numbers).

## **3. Header Layout**

### **Strings**

```
string  Version
string  Scene
string  DateUTC

```

### **Integers**

```
int32 FrameCount
int32 StructureCount
int32 FPS

```

### **Players**

```
int32 PlayerCount
PlayerInfo (per Player):
    byte    ActorId
    string  MasterId
    string  Name
    int32   BattlePoints
    string  VisualData
    int16   Shiftstone0
    int16   Shiftstone1
    float   BodyLength
    float   ArmSpan
    bool    WasHost

```

### **Structures**

```
int32 StructureCount
StructureInfo (per structure):
    byte StructureType
    
StructureType enum:
		0 = Cube
		1 = Pillar
		2 = Wall
		3 = Disc
		4 = Ball
		5 = CagedBall
		6 = LargeRock (Boulder)
		7 = SmallRock
```

## **4. Frames**

There are `FrameCount` frames.  
Each frame begins with:

```
float Time
```

### **4.1 Structure Updates**

For each of the `StructureCount` structures:

```
byte changedFlag   // 1 = values follow, 0 = reuse previous
if changedFlag == 1:
    Vector3 position   (12 bytes)
    Quaternion rot     (16 bytes)
    bool active
    bool grounded
```

Must cache the _last known_ state per structure and reuse it when `changedFlag == 0`.

----------

### **4.2 Player Updates**

For each of the `PlayerCount` players:

```
byte changedFlag   // 1 = values follow, 0 = reuse previous
if changedFlag == 1:
    Vector3 VRRigPos
    Quaternion VRRigRot
    Vector3 HeadPos
    Quaternion HeadRot
    Vector3 LHandPos
    Quaternion LHandRot
    Vector3 RHandPos
    Quaternion RHandRot
    int16 Health
    bool active

```

Must reuse last-frame values if `changedFlag == 0`.

## **5. Notes**

-   Inner replay data is written raw, then GZIP-compressed.
    
-   Quaternions and vectors are stored as raw floats.
    
-   BinaryWriter/Reader default encodings apply.

> Future versions of RumbleAnimator may append new fields to the header or add new per-frame data. Readers should key off the `Version` string and ignore unknown trailing data.