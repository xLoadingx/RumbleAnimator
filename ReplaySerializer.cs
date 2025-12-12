using System;
using System.IO;
using System.IO.Compression;
using Il2CppRUMBLE.Players.Scaling;
using UnityEngine;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using MemoryStream = System.IO.MemoryStream;

namespace RumbleAnimator;

public static class BinaryExtensions
{
    public static void Write(this BinaryWriter bw, Vector3 v)
    {
        bw.Write(v.x);
        bw.Write(v.y);
        bw.Write(v.z);
    }

    public static void Write(this BinaryWriter bw, Quaternion q)
    {
        bw.Write(q.x);
        bw.Write(q.y);
        bw.Write(q.z);
        bw.Write(q.w);
    }

    public static Vector3 ReadVector3(this BinaryReader br)
    {
        return new Vector3(
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle()
        );
    }

    public static Quaternion ReadQuaternion(this BinaryReader br)
    {
        return new Quaternion(
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle()
        );
    }
}

public class ReplaySerializer
{
    public static string FileName { get; set; }
    
    const float EPS = 0.005f;
    const float ROT_EPS_DOT = 0.9995f;

    [Serializable]
    public class ReplayHeader
    {
        public string Version;
        public string Scene;
        public string DateUTC;

        public int FrameCount;
        public int StructureCount;
        public int FPS;

        public PlayerInfo[] Players;
        public StructureInfo[] Structures;
    }

    private static void WriteHeader(BinaryWriter bw, ReplayHeader h)
    {
        bw.Write(h.Version);
        bw.Write(h.Scene);
        bw.Write(h.DateUTC);

        bw.Write(h.FrameCount);
        bw.Write(h.StructureCount);
        bw.Write(h.FPS);

        bw.Write(h.Players.Length);
        foreach (var p in h.Players)
        {
            bw.Write(p.ActorId);
            bw.Write(p.MasterId);
            bw.Write(p.Name);
            bw.Write(p.BattlePoints);
            bw.Write(p.VisualData);

            bw.Write(p.EquippedShiftStones[0]);
            bw.Write(p.EquippedShiftStones[1]);

            bw.Write(p.Measurement.Length);
            bw.Write(p.Measurement.ArmSpan);
            
            bw.Write(p.WasHost);
        }

        bw.Write(h.Structures.Length);
        foreach (var s in h.Structures)
        {
            bw.Write((byte)s.Type);
        }
    }
    
    static void WriteStructureChunk(BinaryWriter bw, StructureState s)
    {
        using var ms = new MemoryStream();
        using var temp = new BinaryWriter(ms);
        
        temp.Write((byte)StructureField.position);
        temp.Write(s.position);

        temp.Write((byte)StructureField.rotation);
        temp.Write(s.rotation);
        
        temp.Write((byte)StructureField.active);
        temp.Write(s.active);
        
        temp.Write((byte)StructureField.grounded);
        temp.Write(s.grounded);

        byte[] chunk = ms.ToArray();
        bw.Write(chunk.Length);
        bw.Write(chunk);
    }
    
    static void WritePlayerChunk(BinaryWriter bw, PlayerState p)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((byte)PlayerField.VRRigPos);
        w.Write(p.VRRigPos);

        w.Write((byte)PlayerField.VRRigRot);
        w.Write(p.VRRigRot);

        w.Write((byte)PlayerField.LHandPos);
        w.Write(p.LHandPos);

        w.Write((byte)PlayerField.LHandRot);
        w.Write(p.LHandRot);

        w.Write((byte)PlayerField.RHandPos);
        w.Write(p.RHandPos);

        w.Write((byte)PlayerField.RHandRot);
        w.Write(p.RHandRot);

        w.Write((byte)PlayerField.HeadPos);
        w.Write(p.HeadPos);

        w.Write((byte)PlayerField.HeadRot);
        w.Write(p.HeadRot);

        w.Write((byte)PlayerField.currentStack);
        w.Write(p.currentStack);

        w.Write((byte)PlayerField.Health);
        w.Write(p.Health);

        w.Write((byte)PlayerField.active);
        w.Write(p.active);

        var chunk = ms.ToArray();
        bw.Write(chunk.Length);
        bw.Write(chunk);
    }

    static bool PosChanged(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude > EPS * EPS;
    }
    
    static bool RotChanged(Quaternion a, Quaternion b)
    {
        return Quaternion.Dot(a, b) < ROT_EPS_DOT;
    }

    public static Byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal))
            gzip.Write(data, 0, data.Length);
        
        return ms.ToArray();
    }
    
    public static void WriteReplayToFile(string path, ReplayInfo replay)
    {
        MemoryStream rawStream = new MemoryStream();
        using (BinaryWriter bw = new BinaryWriter(rawStream))
        {
            bw.Write("REPLAY".ToCharArray());
            WriteHeader(bw, replay.Header);
            
            StructureState[] lastStructureFrame = null;
            PlayerState[] lastPlayerFrame = null;

            foreach (var f in Main.Frames)
            {
                bw.Write(f.Time);

                int structureCount = replay.Header.StructureCount;
                int playerCount = replay.Header.Players.Length;
                
                lastStructureFrame ??= new StructureState[structureCount];
                lastPlayerFrame ??= new PlayerState[playerCount];

                // Structures
                
                for (int i = 0; i < structureCount; i++)
                {
                    bool prevExists = i < lastStructureFrame.Length;

                    var curr = f.Structures[i];
                    var prev = prevExists ? lastStructureFrame[i] : default;

                    bool changed;

                    if (!prevExists)
                    {
                        changed = true;
                    }
                    else
                    {
                        changed =
                            prev.active != curr.active ||
                            prev.grounded != curr.grounded ||
                            PosChanged(prev.position, curr.position) ||
                            RotChanged(prev.rotation, curr.rotation);
                    }

                    bw.Write((byte)(changed ? 1 : 0));
                    bw.Write((byte)ChunkType.StructureState);
                    if (changed)
                        WriteStructureChunk(bw, curr);

                    if (i < lastStructureFrame.Length)
                        lastStructureFrame[i] = curr;
                }
                
                // Players

                for (int i = 0; i < playerCount; i++)
                {
                    bool prevExists = i < lastPlayerFrame.Length;

                    var curr = f.Players[i];
                    var prev = prevExists ? lastPlayerFrame[i] : default;

                    bool changed;

                    if (!prevExists)
                    {
                        changed = true;
                    }
                    else
                    {
                        changed =
                            PosChanged(prev.VRRigPos, curr.VRRigPos) ||
                            RotChanged(prev.VRRigRot, curr.VRRigRot) ||
                            PosChanged(prev.LHandPos, curr.LHandPos) ||
                            RotChanged(prev.LHandRot, curr.LHandRot) ||
                            PosChanged(prev.RHandPos, curr.RHandPos) ||
                            RotChanged(prev.RHandRot, curr.RHandRot) ||
                            PosChanged(prev.HeadPos, curr.HeadPos) ||
                            RotChanged(prev.HeadRot, curr.HeadRot) ||
                            prev.active != curr.active ||
                            prev.Health != curr.Health;
                    }

                    bw.Write((byte)(changed ? 1 : 0));
                    bw.Write((byte)ChunkType.PlayerState);
                    if (changed)
                        WritePlayerChunk(bw, curr);
                    
                    if (i < lastPlayerFrame.Length)
                        lastPlayerFrame[i] = curr;
                }
            }
        }

        byte[] uncompressed = rawStream.ToArray();
        byte[] compressed = Compress(uncompressed);

        using (FileStream fs = new FileStream(path, FileMode.Create))
        using (BinaryWriter bw2 = new BinaryWriter(fs))
        {
            bw2.Write("RGZP".ToCharArray()); 
            bw2.Write(uncompressed.Length); 
            bw2.Write(compressed.Length); 
            bw2.Write(compressed); 
        }
    }

    public static byte[] Decompress(byte[] compressed, int originalSize)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(originalSize);

        gzip.CopyTo(output);
        return output.ToArray();
    }

    public static ReplayInfo DeserializeReplay(byte[] data)
    {
        ReplayInfo replay = new ReplayInfo();

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        string magic = new string(br.ReadChars(6));
        if (magic != "REPLAY")
        {
            Main.instance.LoggerInstance.Error("Invalid replay magic");
            return replay;
        }

        replay.Header = ReadHeader(br);

        replay.Frames = ReadFrames(br, replay.Header.FrameCount, replay.Header.StructureCount, replay.Header.Players.Length);

        return replay;
    }

    private static ReplayHeader ReadHeader(BinaryReader br)
    {
        var header = new ReplayHeader
        {
            Version = br.ReadString(),
            Scene = br.ReadString(),
            DateUTC = br.ReadString(),
            FrameCount = br.ReadInt32(),
            StructureCount = br.ReadInt32(),
            FPS = br.ReadInt32()
        };

        int playerCount = br.ReadInt32();
        header.Players = new PlayerInfo[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            header.Players[i].ActorId = br.ReadByte();
            header.Players[i].MasterId = br.ReadString();
            header.Players[i].Name = br.ReadString();
            header.Players[i].BattlePoints = br.ReadInt32();
            header.Players[i].VisualData = br.ReadString();

            short s0 = br.ReadInt16();
            short s1 = br.ReadInt16();
            header.Players[i].EquippedShiftStones = new[] { s0, s1 };

            float length = br.ReadSingle();
            float armSpan = br.ReadSingle();
            header.Players[i].Measurement = new PlayerMeasurement(length, armSpan);
            
            header.Players[i].WasHost = br.ReadBoolean();
        }
        
        int structureCount = br.ReadInt32();
        header.Structures = new StructureInfo[structureCount];
        for (int i = 0; i < header.Structures.Length; i++)
            header.Structures[i].Type = (StructureType)br.ReadByte();

        return header;
    }

    private static Frame[] ReadFrames(BinaryReader br, int frameCount, int structureCount, int playerCount)
    {
        Frame[] frames = new Frame[frameCount];

        StructureState[] lastStructureStates = new StructureState[structureCount];
        PlayerState[] lastPlayerStates = new PlayerState[playerCount];
        
        for (int f = 0; f < frameCount; f++)
        {
            Frame frame = new Frame();
            frame.Time = br.ReadSingle();
            
            frame.Structures = new StructureState[structureCount];
            frame.Players = new PlayerState[playerCount];
            
            int structureIndex = 0;
            int playerIndex = 0;
            
            int total = structureCount + playerCount;
            
            for (int i = 0; i < total; i++)
            {
                byte changed = br.ReadByte();
                ChunkType type = (ChunkType)br.ReadByte();

                switch (type)
                {
                    case ChunkType.StructureState:
                    {
                        if (changed == 1)
                        {
                            var s = ReadStructureChunk(br);
                            frame.Structures[structureIndex] = s;
                            lastStructureStates[structureIndex] = s;
                        }
                        else
                        {
                            frame.Structures[structureIndex] = lastStructureStates[structureIndex];
                        }
                        
                        structureIndex++;
                        break;
                    }

                    case ChunkType.PlayerState:
                    {
                        if (changed == 1)
                        {
                            var p = ReadPlayerChunk(br);
                            frame.Players[playerIndex] = p;
                            lastPlayerStates[playerIndex] = p;
                        }
                        else
                        {
                            frame.Players[playerIndex] = lastPlayerStates[playerIndex];
                        }

                        playerIndex++;
                        break;
                    }
                    default:
                    {
                        int len = br.ReadInt32();
                        br.BaseStream.Position += len;
                        break;
                    }
                }
            }

            frames[f] = frame;
        }

        return frames;
    }

    static PlayerState ReadPlayerChunk(BinaryReader br)
    {
        int len = br.ReadInt32();
        long end = br.BaseStream.Position + len;

        PlayerState p = new();

        while (br.BaseStream.Position < end)
        {
            PlayerField id = (PlayerField)br.ReadByte();

            switch (id)
            {
                case PlayerField.VRRigPos: p.VRRigPos = br.ReadVector3(); break;
                case PlayerField.VRRigRot: p.VRRigRot = br.ReadQuaternion(); break;
                case PlayerField.LHandPos: p.LHandPos = br.ReadVector3(); break;
                case PlayerField.LHandRot: p.LHandRot = br.ReadQuaternion(); break;
                case PlayerField.RHandPos: p.RHandPos = br.ReadVector3(); break;
                case PlayerField.RHandRot: p.RHandRot = br.ReadQuaternion(); break;
                case PlayerField.HeadPos: p.HeadPos = br.ReadVector3(); break;
                case PlayerField.HeadRot: p.HeadRot = br.ReadQuaternion(); break;
                case PlayerField.currentStack: p.currentStack = br.ReadInt16(); break;
                case PlayerField.Health: p.Health = br.ReadInt16(); break;
                case PlayerField.active: p.active = br.ReadBoolean(); break;
                default:
                    br.BaseStream.Position = end;
                    break;
            }
        }

        return p;
    }

    static StructureState ReadStructureChunk(BinaryReader br)
    {
        int len = br.ReadInt32();
        long end = br.BaseStream.Position + len;

        StructureState s = new();

        while (br.BaseStream.Position < end)
        {
            StructureField id = (StructureField)br.ReadByte();

            switch (id)
            {
                case StructureField.position: s.position = br.ReadVector3(); break;
                case StructureField.rotation: s.rotation = br.ReadQuaternion(); break;
                case StructureField.active: s.active = br.ReadBoolean(); break;
                case StructureField.grounded: s.grounded = br.ReadBoolean(); break;
                default:
                    br.BaseStream.Position = end;
                    break;
            }
        }

        return s;
    }

    public static ReplayInfo LoadReplay(string path)
    {
        using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using BinaryReader br = new BinaryReader(fs);

        string wrapperMagic = new(br.ReadChars(4));
        if (wrapperMagic != "RGZP")
        {
            Main.instance.LoggerInstance.Error("Invalid replay file (missing RGZP)");
            return new ReplayInfo();
        }

        int originalSize = br.ReadInt32();
        int compressedSize = br.ReadInt32();
        
        byte[] compressed = br.ReadBytes(compressedSize);
        byte[] decompressed = Decompress(compressed, originalSize);
        
        return DeserializeReplay(decompressed);
    }
}

[Serializable]
public struct ReplayInfo
{
    public ReplaySerializer.ReplayHeader Header;
    public Frame[] Frames;
}

[Serializable]
public struct Frame
{
    public float Time;
    public StructureState[] Structures;
    public PlayerState[] Players;
}

// ------- Structure State -------

[Serializable]
public struct StructureState
{
    public Vector3 position;
    public Quaternion rotation;
    public bool active;
    public bool grounded;
}

[Serializable]
public struct StructureInfo
{
    public StructureType Type;
}

public enum StructureField
{
    position,
    rotation,
    active,
    grounded
}

public enum StructureType : byte
{
    Cube,
    Pillar,
    Wall,
    Disc,
    Ball,
    CagedBall,
    LargeRock,
    SmallRock
}

// ------- Player State -------

[Serializable]
public struct PlayerState
{
    public Vector3 VRRigPos;
    public Quaternion VRRigRot;

    public Vector3 LHandPos;
    public Quaternion LHandRot;

    public Vector3 RHandPos;
    public Quaternion RHandRot;

    public Vector3 HeadPos;
    public Quaternion HeadRot;

    public short currentStack;

    public short Health;
    public bool active;
}

[Serializable]
public struct PlayerInfo
{
    public byte ActorId;
    public string MasterId;
    
    public string Name;
    public int BattlePoints;
    public string VisualData;
    public short[] EquippedShiftStones;
    public PlayerMeasurement Measurement;

    public bool WasHost;
}

public enum PlayerField : byte {
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
}

public enum StackType : short {
    None,
    Dash,
    Jump,
    Flick,
    Parry,
    HoldLeft,
    HoldRight,
    Ground,
    Straight,
    Uppercut,
    Kick,
    Explode
}

// ------------------------

public enum ChunkType : short
{
    PlayerState,
    StructureState
}

