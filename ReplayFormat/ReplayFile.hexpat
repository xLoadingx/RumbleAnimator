#include "std/mem.pat"
#pragma pattern_limit 9999999

struct Vector3 { float x, y, z; } [[static]];
struct Quaternion { float x, y, z, w; } [[static]];
struct String { u32 length; char value[length]; } [[static]];

// -------------------- Player --------------------

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
    active
};

struct PlayerFieldEntry {
    PlayerField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (PlayerField::VRRigPos): Vector3 VRRigPos;
        (PlayerField::VRRigRot): Quaternion VRRigRot;
        (PlayerField::LHandPos): Vector3 LHandPos;
        (PlayerField::LHandRot): Quaternion LHandRot;
        (PlayerField::RHandPos): Vector3 RHandPos;
        (PlayerField::RHandRot): Quaternion RHandRot;
        (PlayerField::HeadPos): Vector3 HeadPos;
        (PlayerField::HeadRot): Quaternion HeadRot;
        (PlayerField::currentStack): s16 stack;
        (PlayerField::Health): s16 health;
        (PlayerField::active): bool active;
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

// -------------------- Structure --------------------

enum StructureField : u8 {
    Position,
    Rotation,
    Active,
    Grounded,
    IsHeld,
    IsFlicked,
    IsShaking
};

struct StructureFieldEntry {
    StructureField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (StructureField::Position): Vector3 position;
        (StructureField::Rotation): Quaternion rotation;
        (StructureField::Active): bool active;
        (StructureField::Grounded): bool grounded;
        (StructureField::IsHeld): bool isHeld;
        (StructureField::IsFlicked): bool isFlicked;
        (StructureField::IsShaking): bool isShaking;
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

// -------------------- Pedestal --------------------

enum PedestalField : u8 {
    Position,
    Active
};

struct PedestalFieldEntry {
    PedestalField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (PedestalField::Position): Vector3 position;
        (PedestalField::Active): bool active;
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

enum EventField : u8 {
    Type,
    Position,
    Rotation,
    PlayerId,
    Length,
    ArmSpan
};

struct EventFieldEntry {
    EventField id;
    u8 size;
    u64 valueStart = $;

    match (id) {
        (EventField::Type): u8 type;
        (EventField::Position): Vector3 position;
        (EventField::Rotation): Quaternion rotation;
        (EventField::PlayerId): String playerId;
        (EventField::Length): float length;
        (EventField::ArmSpan): float armSpan;
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

// -------------------- Frame entries + frames --------------------

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

char magic[4] @ 0x0;
Frame Replay[while(!std::mem::eof())] @ sizeof(magic);