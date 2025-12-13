#include "std/mem.pat"
#pragma pattern_limit 9999999

struct Vector3 {
    float x, y, z;
} [[static]];

struct Quaternion {
    float x, y, z, w;
} [[static]];

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
};

enum StructureField : u8 {
    Position,
    Rotation,
    Active,
    Grounded
};

struct StructureFieldEntry {
    StructureField id;
    u8 size;

    match (id) {
        (StructureField::Position): Vector3 position;
        (StructureField::Rotation): Quaternion rotation;
        (StructureField::Active): bool active;
        (StructureField::Grounded): bool grounded;
    }
};

struct PlayerStateChunk {
    u32 length [[hidden]];
    u64 dataStart = $;

    PlayerFieldEntry fields[
        while ($ < dataStart + length)
    ];
};

struct StructureStateChunk {
    u32 length [[hidden]];
    u64 dataStart = $;

    StructureFieldEntry fields[
        while ($ < dataStart + length)
    ];
};

enum ChunkType : u8 {
    PlayerState,
    StructureState
};

struct FrameEntry {
    ChunkType type;
    u32 index;

    match (type) {
        (ChunkType::PlayerState): PlayerStateChunk player;
        (ChunkType::StructureState): StructureStateChunk structure;
    }
};

struct Frame {
    u32 frameLength [[hidden]];
    float Time;
    u32 entryCount [[hidden]];

    FrameEntry entries[entryCount];
} [[fixed_size(sizeof(u32) + frameLength)]];

char magic[4] @ 0x0;
Frame Replay[while(!std::mem::eof())] @ sizeof(magic);