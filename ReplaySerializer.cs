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
    
    static void WriteStructureState(BinaryWriter bw, StructureState s)
    {
        bw.Write(s.position);
        bw.Write(s.rotation);
        bw.Write(s.active);
        bw.Write(s.grounded);
    }
    
    static void WritePlayerState(BinaryWriter bw, PlayerState p)
    {
        bw.Write(p.VRRigPos);
        bw.Write(p.VRRigRot);

        bw.Write(p.HeadPos);
        bw.Write(p.HeadRot);

        bw.Write(p.LHandPos);
        bw.Write(p.LHandRot);

        bw.Write(p.RHandPos);
        bw.Write(p.RHandRot);

        bw.Write(p.Health);
        bw.Write(p.active);
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
                    bool currExists = i < f.Structures.Length;
                    bool prevExists = i < lastStructureFrame.Length;

                    if (!currExists)
                    {
                        bw.Write((byte)0);
                        continue;
                    }

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
                    if (changed)
                        WriteStructureState(bw, curr);

                    if (i < lastStructureFrame.Length)
                        lastStructureFrame[i] = curr;
                }
                
                // Players

                for (int i = 0; i < playerCount; i++)
                {
                    bool currExists = i < f.Players.Length;
                    bool prevExists = i < lastPlayerFrame.Length;

                    if (!currExists)
                    {
                        bw.Write((byte)0);
                        continue;
                    }

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

                    if (changed)
                        WritePlayerState(bw, curr);
                    
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

            for (int s = 0; s < structureCount; s++)
            {
                byte changed = br.ReadByte();

                if (changed == 1)
                {
                    var state = ReadStructureState(br);
                    frame.Structures[s] = state;
                    lastStructureStates[s] = state;
                }
                else
                {
                    frame.Structures[s] = lastStructureStates[s];
                }
            }

            for (int p = 0; p < playerCount; p++)
            {
                byte changed = br.ReadByte();

                if (changed == 1)
                {
                    var state = ReadPlayerState(br);
                    frame.Players[p] = state;
                    lastPlayerStates[p] = state;
                }
                else
                {
                    frame.Players[p] = lastPlayerStates[p];
                }
            }

            frames[f] = frame;
        }

        return frames;
    }

    static PlayerState ReadPlayerState(BinaryReader br)
    {
        return new PlayerState
        {
            VRRigPos = br.ReadVector3(),
            VRRigRot = br.ReadQuaternion(),

            HeadPos = br.ReadVector3(),
            HeadRot = br.ReadQuaternion(),

            LHandPos = br.ReadVector3(),
            LHandRot = br.ReadQuaternion(),

            RHandPos = br.ReadVector3(),
            RHandRot = br.ReadQuaternion(),
            
            Health = br.ReadInt16(),
            active = br.ReadBoolean()
        };
    }

    static StructureState ReadStructureState(BinaryReader br)
    {
        var state = new StructureState();

        state.position = br.ReadVector3();
        state.rotation = br.ReadQuaternion();
        state.active = br.ReadBoolean();
        state.grounded = br.ReadBoolean();

        return state;
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

[Serializable]
public struct StructureInfo
{
    public StructureType Type;
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

[Serializable]
public struct Frame
{
    public float Time;
    public StructureState[] Structures;
    public PlayerState[] Players;
}

[Serializable]
public struct StructureState
{
    public Vector3 position;
    public Quaternion rotation;
    public bool active;
    public bool grounded;
}

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

    public short Health;
    public bool active;
}

