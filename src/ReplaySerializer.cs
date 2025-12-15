using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Il2CppRUMBLE.Players.Scaling;
using Newtonsoft.Json;
using UnityEngine;
using System.Threading.Tasks;
using Utilities = RumbleAnimator.ReplayGlobals.Utilities;
using ReplayFiles = RumbleAnimator.ReplayGlobals.ReplayFiles;
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

    public static void Write<TField>(this BinaryWriter bw, TField field, Vector3 v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((byte)12);
        bw.Write(v);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, Quaternion q) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((byte)16);
        bw.Write(q);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, short v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((byte)2);
        bw.Write(v);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, bool v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((byte)1);
        bw.Write(v);
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
        public string Title;
        public string CustomMap;
        public string Version;
        public string Scene;
        public string DateUTC;

        public int FrameCount;
        public int StructureCount;
        public int FPS;

        public PlayerInfo[] Players;
        public StructureInfo[] Structures;
    }
    
    static void WriteStructureChunk(BinaryWriter bw, StructureState s)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        
        w.Write(StructureField.position, s.position);
        w.Write(StructureField.rotation, s.rotation);
        w.Write(StructureField.grounded, s.grounded);
        w.Write(StructureField.active, s.active);
        w.Write(StructureField.isFlicked, s.isFlicked);
        w.Write(StructureField.isHeld, s.isHeld);

        byte[] chunk = ms.ToArray();
        bw.Write(chunk.Length);
        bw.Write(chunk);
    }
    
    static void WritePlayerChunk(BinaryWriter bw, PlayerState p)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(PlayerField.VRRigPos, p.VRRigPos);
        w.Write(PlayerField.VRRigRot, p.VRRigRot);
        w.Write(PlayerField.LHandPos, p.LHandPos);
        w.Write(PlayerField.LHandRot, p.LHandRot);
        w.Write(PlayerField.RHandPos, p.RHandPos);
        w.Write(PlayerField.RHandRot, p.RHandRot);
        w.Write(PlayerField.HeadPos, p.HeadPos);
        w.Write(PlayerField.HeadRot, p.HeadRot);
        w.Write(PlayerField.currentStack, p.currentStack);
        w.Write(PlayerField.Health, p.Health);
        w.Write(PlayerField.active, p.active);

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

    public static void BuildReplayPackage(
        string outputPath,
        ReplayInfo replay,
        Dictionary<string, byte[]> voices = null,
        Action done = null
    )
    {
        byte[] rawReplay = SerializeReplayFile(replay);

        string manifestJson = JsonConvert.SerializeObject(
            replay.Header,
            Formatting.Indented
        );

        Task.Run(() =>
        {
            byte[] compressedReplay = Compress(rawReplay);

            using (var fs = new FileStream(outputPath, FileMode.Create))
            {
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var manifestEntry = zip.CreateEntry(
                        "manifest.json",
                        CompressionLevel.Optimal
                    );

                    using (var writer = new StreamWriter(manifestEntry.Open()))
                        writer.Write(manifestJson);

                    var replayEntry = zip.CreateEntry(
                        "replay",
                        CompressionLevel.NoCompression
                    );

                    using (var stream = replayEntry.Open())
                        stream.Write(compressedReplay);
                };
            };

            done?.Invoke();
        });
    }
    
    public static byte[] SerializeReplayFile(ReplayInfo replay)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        bw.Write(Encoding.ASCII.GetBytes("RPLY"));
        
        StructureState[] lastStructureFrame = null;
        PlayerState[] lastPlayerFrame = null;

        foreach (var f in Main.instance.Frames)
        {
            using var frameMs = new MemoryStream();
            using var frameBw = new BinaryWriter(frameMs);
            
            frameBw.Write(f.Time);

            using var entriesMs = new MemoryStream();
            using var entriesBw = new BinaryWriter(entriesMs);
            
            int entryCount = 0;

            int structureCount = replay.Header.StructureCount;
            int playerCount = replay.Header.Players.Length;
                
            lastStructureFrame ??= new StructureState[structureCount];
            lastPlayerFrame ??= new PlayerState[playerCount];

            // Structures
                
            for (int i = 0; i < structureCount; i++)
            {
                if (i >= f.Structures.Length || i >= lastStructureFrame.Length)
                    continue;
                
                var curr = f.Structures[i];
                var prev = lastStructureFrame[i];

                bool changed =
                    prev.active != curr.active ||
                    prev.grounded != curr.grounded ||
                    prev.isFlicked != curr.isFlicked ||
                    prev.isHeld != curr.isHeld ||
                    PosChanged(prev.position, curr.position) ||
                    RotChanged(prev.rotation, curr.rotation);

                if (!changed)
                    continue;
                
                entriesBw.Write((byte)ChunkType.StructureState);
                entriesBw.Write(i);
                WriteStructureChunk(entriesBw, curr);

                lastStructureFrame[i] = curr;
                entryCount++;
            }
                
            // Players

            for (int i = 0; i < playerCount; i++)
            {
                if (i >= f.Players.Length || i >= lastPlayerFrame.Length)
                    continue;
                
                var curr = f.Players[i];
                var prev = lastPlayerFrame[i];

                bool changed =
                    PosChanged(prev.VRRigPos, curr.VRRigPos) ||
                    RotChanged(prev.VRRigRot, curr.VRRigRot) ||
                    PosChanged(prev.LHandPos, curr.LHandPos) ||
                    RotChanged(prev.LHandRot, curr.LHandRot) ||
                    PosChanged(prev.RHandPos, curr.RHandPos) ||
                    RotChanged(prev.RHandRot, curr.RHandRot) ||
                    PosChanged(prev.HeadPos, curr.HeadPos) ||
                    RotChanged(prev.HeadRot, curr.HeadRot) ||
                    prev.active != curr.active ||
                    prev.Health != curr.Health ||
                    prev.currentStack != curr.currentStack;

                if (!changed)
                    continue;

                entriesBw.Write((byte)ChunkType.PlayerState);
                entriesBw.Write(i);
                WritePlayerChunk(entriesBw, curr);

                lastPlayerFrame[i] = curr;
                entryCount++;
            }

            frameBw.Write(entryCount);
            frameBw.Write(entriesMs.ToArray());

            byte[] frameData = frameMs.ToArray();

            bw.Write(frameData.Length);
            bw.Write(frameData);
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

    public static string BuildTitle(ReplayHeader header)
    {
        string scene = Utilities.GetFriendlySceneName(header.Scene);
        string customMap = header.CustomMap;

        string finalScene = string.IsNullOrEmpty(customMap) ? scene : customMap;

        return header.Players?.Length switch
        {
            2 when scene != "Park" => $"{header.Players[0].Name}<#1A0D07> vs {header.Players[1].Name}<#1A0D07> - {finalScene}",
            > 0 when scene == "Park" => $"{header.Players[0].Name}<#1A0D07> - {scene}\n<scale=85%>{header.Players.Length} player{(header.Players.Length > 1 ? "s" : "")}",
            1 => $"{header.Players[0]} - {finalScene}",
            _ => $"{finalScene} - {header.DateUTC}"
        };
    }

    public static ReplayHeader GetManifest(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        
        var manifestEntry = zip.GetEntry("manifest.json");
        if (manifestEntry == null)
            throw new Exception("Replay does not have valid manifest.json");

        using var stream = manifestEntry.Open();
        using var reader = new StreamReader(stream);

        string json = reader.ReadToEnd();
        return JsonConvert.DeserializeObject<ReplayHeader>(json);
    }

    public static void WriteManifest(string replayPath, ReplayHeader header)
    {
        ReplayFiles.suppressWatcher = true;
        
        using var fs = new FileStream(replayPath, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        
        var manifestEntry = zip.GetEntry("manifest.json");

        manifestEntry?.Delete();

        var newEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        
        using var writer = new StreamWriter(newEntry.Open());
        writer.Write(JsonConvert.SerializeObject(
            header,
            Formatting.Indented
        ));
        
        ReplayFiles.suppressWatcher = false;
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
        
        byte[] magic = br.ReadBytes(4);
        string magicStr = Encoding.ASCII.GetString(magic);

        if (magicStr != "RPLY")
        {
            Main.instance.LoggerInstance.Error($"Invalid replay file (magic={magicStr}");
            return new ReplayInfo();
        }
        
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

    private static Frame[] ReadFrames(
        BinaryReader br,
        int frameCount,
        int structureCount,
        int playerCount
    )
    {
        Frame[] frames = new Frame[frameCount];

        StructureState[] lastStructures = new StructureState[structureCount];
        PlayerState[] lastPlayers = new PlayerState[playerCount];
        
        for (int f = 0; f < frameCount; f++)
        {
            int frameSize = br.ReadInt32();
            
            long frameEnd = br.BaseStream.Position + frameSize;
            
            Frame frame = new Frame();
            frame.Time = br.ReadSingle();
            
            frame.Structures = new StructureState[structureCount];
            frame.Players = new PlayerState[playerCount];

            Array.Copy(lastStructures, frame.Structures, structureCount);
            Array.Copy(lastPlayers, frame.Players, playerCount);
            
            int entryCount = br.ReadInt32();

            for (int e = 0; e < entryCount; e++)
            {
                ChunkType type = (ChunkType)br.ReadByte();
                int index = br.ReadInt32();

                switch (type)
                {
                    case ChunkType.StructureState:
                    {
                        var s = ReadStructureChunk(br);
                        frame.Structures[index] = s;
                        lastStructures[index] = s;
                        break;
                    }

                    case ChunkType.PlayerState:
                    {
                        var p = ReadPlayerChunk(br);
                        frame.Players[index] = p;
                        lastPlayers[index] = p;
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
            
            br.BaseStream.Position = frameEnd;

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
            byte size = br.ReadByte();
            
            long fieldEnd = br.BaseStream.Position + size;

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
            }
            
            br.BaseStream.Position = fieldEnd;
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
            byte size = br.ReadByte();
            
            long fieldEnd = br.BaseStream.Position + size;

            switch (id)
            {
                case StructureField.position: s.position = br.ReadVector3(); break;
                case StructureField.rotation: s.rotation = br.ReadQuaternion(); break;
                case StructureField.active: s.active = br.ReadBoolean(); break;
                case StructureField.grounded: s.grounded = br.ReadBoolean(); break;
                case StructureField.isFlicked: s.isFlicked = br.ReadBoolean(); break;
                case StructureField.isHeld: s.isHeld = br.ReadBoolean(); break;
            }
            
            br.BaseStream.Position = fieldEnd;
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
    public bool isHeld;
    public bool isFlicked;
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
    grounded,
    isHeld,
    isFlicked
}

public enum StructureType
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

public enum PlayerField {
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

public enum StackType {
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

public enum ChunkType
{
    PlayerState,
    StructureState
}

