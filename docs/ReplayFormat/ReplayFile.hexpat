#include "std/mem.pat"
#pragma pattern_limit 9999999

// ---------- Common ----------

struct Vector3 { float x, y, z; } [[static]];

struct QuaternionST {
    u16 a;
    u16 b;
    u16 c;
    u8 dropped;
} [[static]];

struct String {
    u32 length;
    char value[length];
} [[static]];

// ---------- Player ----------

enum PlayerField : u8 {
    VRRigPos,
    VRRigRot,

    LHandPos,
    LHandRot,

    RHandPos,
    RHandRot,

    HeadPos,
    HeadRot,

    currentStack,
    Health,
    active,

    activeShiftstoneVFX,
    leftShiftstone,
    rightShiftstone,

    lgripInput,
    lindexInput,
    lthumbInput,

    rgripInput,
    rindexInput,
    rthumbInput,

    rockCamActive,
    rockCamPos,
    rockCamRot,

    armSpan,
    length
};

struct PlayerFieldEntry {
    PlayerField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (PlayerField::VRRigPos): Vector3 VRRigPos;
        (PlayerField::VRRigRot): QuaternionST VRRigRot;

        (PlayerField::LHandPos): Vector3 LHandPos;
        (PlayerField::LHandRot): QuaternionST LHandRot;

        (PlayerField::RHandPos): Vector3 RHandPos;
        (PlayerField::RHandRot): QuaternionST RHandRot;

        (PlayerField::HeadPos): Vector3 HeadPos;
        (PlayerField::HeadRot): QuaternionST HeadRot;

        (PlayerField::currentStack): s16 currentStack;
        (PlayerField::Health): s16 Health;
        (PlayerField::active): bool active;

        (PlayerField::activeShiftstoneVFX): u8 activeShiftstoneVFX;
        (PlayerField::leftShiftstone): u8 leftShiftstone;
        (PlayerField::rightShiftstone): u8 rightShiftstone;

        (PlayerField::lgripInput): float lgripInput;
        (PlayerField::lindexInput): float lindexInput;
        (PlayerField::lthumbInput): float lthumbInput;

        (PlayerField::rgripInput): float rgripInput;
        (PlayerField::rindexInput): float rindexInput;
        (PlayerField::rthumbInput): float rthumbInput;

        (PlayerField::rockCamActive): bool rockCamActive;
        (PlayerField::rockCamPos): Vector3 rockCamPos;
        (PlayerField::rockCamRot): QuaternionST rockCamRot;

        (PlayerField::armSpan): float armSpan;
        (PlayerField::length): float length;
    }

    u8 _extra[(valueStart + size) > $ ? (valueStart + size) - $ : 0] [[hidden]];
};

struct PlayerStateChunk {
    s32 length [[hidden]];
    u64 dataStart = $;

    PlayerFieldEntry fields[
        while ($ < dataStart + length)
    ];

    u8 _pad[(dataStart + length) > $ ? (dataStart + length) - $ : 0] [[hidden]];
};

// ---------- Structure ----------

enum StructureField : u8 {
    position,
    rotation,
    active,
    grounded,
    isLeftHeld,
    isRightHeld,
    isFlicked,
    currentState,
    isTargetDisk
};

struct StructureFieldEntry {
    StructureField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (StructureField::position): Vector3 position;
        (StructureField::rotation): QuaternionST rotation;
        (StructureField::active): bool active;
        (StructureField::grounded): bool grounded;
        (StructureField::isLeftHeld): bool isLeftHeld;
        (StructureField::isRightHeld): bool isRightHeld;
        (StructureField::isFlicked): bool isFlicked;
        (StructureField::currentState): u8 currentState;
        (StructureField::isTargetDisk): bool isTargetDisk;
    }

    u8 _extra[(valueStart + size) > $ ? (valueStart + size) - $ : 0] [[hidden]];
};

struct StructureStateChunk {
    s32 length [[hidden]];
    u64 dataStart = $;

    StructureFieldEntry fields[
        while ($ < dataStart + length)
    ];

    u8 _pad[(dataStart + length) > $ ? (dataStart + length) - $ : 0] [[hidden]];
};

// ---------- Pedestal ----------

enum PedestalField : u8 {
    position,
    active
};

struct PedestalFieldEntry {
    PedestalField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (PedestalField::position): Vector3 position;
        (PedestalField::active): bool active;
    }

    u8 _extra[(valueStart + size) > $ ? (valueStart + size) - $ : 0] [[hidden]];
};

struct PedestalStateChunk {
    s32 length [[hidden]];
    u64 dataStart = $;

    PedestalFieldEntry fields[
        while ($ < dataStart + length)
    ];

    u8 _pad[(dataStart + length) > $ ? (dataStart + length) - $ : 0] [[hidden]];
};

// ---------- Event ----------

enum EventField : u8 {
    type,
    position,
    rotation,
    masterId,
    markerType,
    playerIndex,
    damage,
    fxType
};

struct EventFieldEntry {
    EventField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (EventField::type): u8 type;
        (EventField::position): Vector3 position;
        (EventField::rotation): QuaternionST rotation;
        (EventField::masterId): String masterId;
        (EventField::markerType): u8 markerType;
        (EventField::playerIndex): s32 playerIndex;
        (EventField::damage): s32 damage;
        (EventField::fxType): u8 fxType;
    }

    u8 _extra[(valueStart + size) > $ ? (valueStart + size) - $ : 0] [[hidden]];
};

struct EventChunk {
    s32 length [[hidden]];
    u64 dataStart = $;

    EventFieldEntry fields[
        while ($ < dataStart + length)
    ];

    u8 _pad[(dataStart + length) > $ ? (dataStart + length) - $ : 0] [[hidden]];
};

// ---------- Frames ----------

enum ChunkType : u8 {
    PlayerState,
    StructureState,
    PedestalState,
    Event
};

struct FrameEntry {
    ChunkType type;
    s32 index;

    match (type) {
        (ChunkType::PlayerState): PlayerStateChunk player;
        (ChunkType::StructureState): StructureStateChunk structure;
        (ChunkType::PedestalState): PedestalStateChunk pedestal;
        (ChunkType::Event): EventChunk evt;
    }
};

struct Frame {
    s32 frameSize [[hidden]];
    u64 dataStart = $;

    float Time;
    s32 entryCount;

    FrameEntry entries[entryCount];

    u8 _pad[(dataStart + frameSize) > $ ? (dataStart + frameSize) - $ : 0] [[hidden]];
} [[fixed_size(sizeof(s32) + frameSize)]];

// ---------- File ----------

char magic[4] @ 0x0;
Frame Replay[while(!std::mem::eof())] @ sizeof(magic);