using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Il2CppRUMBLE.Players.Scaling;
using Newtonsoft.Json;
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

    public static async Task BuildReplayPackage(
        string outputPath,
        ReplayInfo replay,
        Dictionary<string, byte[]> voices = null
    )
    {
        try
        {
            byte[] rawReplay = SerializeReplayFile(replay);

            byte[] compressedReplay = await Task.Run(() => Compress(rawReplay));

            string manifestJson = JsonConvert.SerializeObject(
                replay.Header,
                Formatting.Indented
            );

            await using var fs = new FileStream(outputPath, FileMode.Create);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            var manifestEntry = zip.CreateEntry(
                "manifest.json",
                CompressionLevel.Optimal
            );

            await using (var writer = new StreamWriter(manifestEntry.Open()))
                await writer.WriteAsync(manifestJson);

            var replayEntry = zip.CreateEntry(
                "replay",
                CompressionLevel.NoCompression
            );

            await using (var stream = replayEntry.Open())
                await stream.WriteAsync(compressedReplay);
        }
        catch (Exception e)
        {
            Main.instance.LoggerInstance.Error($"Replay save failed: {e}");
        }
    }
    
    public static byte[] SerializeReplayFile(ReplayInfo replay)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        bw.Write(Encoding.ASCII.GetBytes("RPLY"));
        
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
                    bw.Write((byte)ChunkType.StructureState);
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

                    
                if (changed)
                    WriteStructureChunk(bw, curr);

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
                    bw.Write((byte)ChunkType.PlayerState);
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
                bw.Write((byte)ChunkType.PlayerState);
                if (changed)
                    WritePlayerChunk(bw, curr);
                    
                if (i < lastPlayerFrame.Length)
                    lastPlayerFrame[i] = curr;
            }
        }
        
        return ms.ToArray();
    }
    
    public static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.Optimal))
            brotli.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    public static byte[] Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    public static ReplayInfo LoadReplay(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        
        var manifestEntry = zip.GetEntry("manifest.json");
        if (manifestEntry == null)
        {
            Main.instance.LoggerInstance.Error("Missing manifest.json");
            return new ReplayInfo();
        }

        using var reader = new StreamReader(manifestEntry.Open());
        string manifestJson = reader.ReadToEnd();
        
        var header = JsonConvert.DeserializeObject<ReplayHeader>(manifestJson);
        
        var replayEntry = zip.GetEntry("replay");
        if (replayEntry == null)
        {
            Main.instance.LoggerInstance.Error("Missing replay");
            return new ReplayInfo();
        }

        using var ms = new MemoryStream();
        using var stream = replayEntry.Open();
        stream.CopyTo(ms);
        byte[] compressedReplay = ms.ToArray();
        
        byte[] replayData = Decompress(compressedReplay);
        
        using var memStream = new MemoryStream(replayData);
        using var br = new BinaryReader(memStream);
        
        var replayInfo = new ReplayInfo();
        replayInfo.Header = header;
        replayInfo.Frames = ReadFrames(
            br,
            header.FrameCount,
            header.StructureCount,
            header.Players.Length
        );

        return replayInfo;
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

