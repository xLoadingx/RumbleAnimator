using System;
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
using ReplayVoices = RumbleAnimator.ReplayGlobals.ReplayVoices;

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

        public int PedestalCount;

        public PlayerInfo[] Players;
        public StructureInfo[] Structures;

        public VoiceTrackInfo[] Voices;
    }
    
    static bool WriteStructureDiff(BinaryWriter w, StructureState prev, StructureState curr)
    {
        bool any = false;

        any |= WriteIf(
            PosChanged(prev.position, curr.position),
            () => w.Write(StructureField.position, curr.position)
        );

        any |= WriteIf(
            RotChanged(prev.rotation, curr.rotation),
            () => w.Write(StructureField.rotation, curr.rotation)
        );

        any |= WriteIf(
            prev.active != curr.active,
            () => w.Write(StructureField.active, curr.active)
        );

        any |= WriteIf(
            prev.grounded != curr.grounded,
            () => w.Write(StructureField.grounded, curr.grounded)
        );

        any |= WriteIf(
            prev.isHeld != curr.isHeld,
            () => w.Write(StructureField.isHeld, curr.isHeld)
        );

        any |= WriteIf(
            prev.isFlicked != curr.isFlicked,
            () => w.Write(StructureField.isFlicked, curr.isFlicked)
        );

        any |= WriteIf(
            prev.isShaking != curr.isShaking,
            () => w.Write(StructureField.isShaking, curr.isShaking)
        );

        return any;
    }
    
    static bool WritePlayerDiff(BinaryWriter w, PlayerState prev, PlayerState curr)
    {
        bool any = false;

        any |= WriteIf(
            PosChanged(prev.VRRigPos, curr.VRRigPos),
            () => w.Write(PlayerField.VRRigPos, curr.VRRigPos)
        );

        any |= WriteIf(
            RotChanged(prev.VRRigRot, curr.VRRigRot),
            () => w.Write(PlayerField.VRRigRot, curr.VRRigRot)
        );

        any |= WriteIf(
            PosChanged(prev.LHandPos, curr.LHandPos),
            () => w.Write(PlayerField.LHandPos, curr.LHandPos)
        );

        any |= WriteIf(
            RotChanged(prev.LHandRot, curr.LHandRot),
            () => w.Write(PlayerField.LHandRot, curr.LHandRot)
        );

        any |= WriteIf(
            PosChanged(prev.RHandPos, curr.RHandPos),
            () => w.Write(PlayerField.RHandPos, curr.RHandPos)
        );

        any |= WriteIf(
            RotChanged(prev.RHandRot, curr.RHandRot),
            () => w.Write(PlayerField.RHandRot, curr.RHandRot)
        );

        any |= WriteIf(
            PosChanged(prev.HeadPos, curr.HeadPos),
            () => w.Write(PlayerField.HeadPos, curr.HeadPos)
        );

        any |= WriteIf(
            RotChanged(prev.HeadRot, curr.HeadRot),
            () => w.Write(PlayerField.HeadRot, curr.HeadRot)
        );

        any |= WriteIf(
            prev.currentStack != curr.currentStack,
            () => w.Write(PlayerField.currentStack, curr.currentStack)
        );

        any |= WriteIf(
            prev.Health != curr.Health,
            () => w.Write(PlayerField.Health, curr.Health)
        );

        any |= WriteIf(
            prev.active != curr.active,
            () => w.Write(PlayerField.active, curr.active)
        );

        return any;
    }

    static bool WritePedestalDiff(BinaryWriter w, PedestalState prev, PedestalState curr)
    {
        bool any = false;

        any |= WriteIf(
            PosChanged(prev.position, curr.position),
            () => w.Write(PedestalField.position, curr.position)
        );

        any |= WriteIf(
            prev.active != curr.active,
            () => w.Write(PedestalField.active, curr.active)
        );

        return any;
    }

    static bool PosChanged(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude > EPS * EPS;
    }
    
    static bool RotChanged(Quaternion a, Quaternion b)
    {
        return Quaternion.Dot(a, b) < ROT_EPS_DOT;
    }

    static bool WriteIf(
        bool condition,
        Action write
    )
    {
        if (!condition)
            return false;

        write();
        return true;
    }

    public static async Task BuildReplayPackage(
        string outputPath,
        ReplayInfo replay,
        Action done = null
    )
    {
        byte[] rawReplay = SerializeReplayFile(replay);

        string manifestJson = JsonConvert.SerializeObject(
            replay.Header,
            Formatting.Indented
        );

        await Task.Run(() =>
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
        });
        
        done?.Invoke();
    }
    
    public static byte[] SerializeReplayFile(ReplayInfo replay)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        bw.Write(Encoding.ASCII.GetBytes("RPLY"));
        
        StructureState[] lastStructureFrame = null;
        PlayerState[] lastPlayerFrame = null;
        PedestalState[] lastPedestalFrame = null;

        foreach (var f in Main.instance.Frames)
        {
            using var frameMs = new MemoryStream();
            using var frameBw = new BinaryWriter(frameMs);
            
            frameBw.Write(f.Time);

            using var entriesMs = new MemoryStream();
            using var entriesBw = new BinaryWriter(entriesMs);
            
            int entryCount = 0;

            int structureCount = replay.Header.Structures.Length;
            int playerCount = replay.Header.Players.Length;
            int pedestalCount = replay.Header.PedestalCount;
            
            lastStructureFrame ??= Utilities.NewArray<StructureState>(structureCount);
            lastPlayerFrame ??= Utilities.NewArray<PlayerState>(playerCount);
            lastPedestalFrame ??= Utilities.NewArray<PedestalState>(pedestalCount);

            // Structures
                
            for (int i = 0; i < structureCount; i++)
            {
                if (i >= f.Structures.Length || i >= lastStructureFrame.Length)
                    continue;
                
                var curr = f.Structures[i];
                var prev = lastStructureFrame[i];

                using var chunkMs = new MemoryStream();
                using var w = new BinaryWriter(chunkMs);

                if (!WriteStructureDiff(w, prev, curr))
                    continue;

                entriesBw.Write((byte)ChunkType.StructureState);
                entriesBw.Write(i);
                entriesBw.Write((int)chunkMs.Length);
                entriesBw.Write(chunkMs.ToArray());

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

                using var chunkMs = new MemoryStream();
                using var w = new BinaryWriter(chunkMs);

                if (!WritePlayerDiff(w, prev, curr))
                    continue;
                
                entriesBw.Write((byte)ChunkType.PlayerState);
                entriesBw.Write(i);
                entriesBw.Write((int)chunkMs.Length);
                entriesBw.Write(chunkMs.ToArray());
                
                lastPlayerFrame[i] = curr;
                entryCount++;
            }

            for (int i = 0; i < pedestalCount; i++)
            {
                if (i >= f.Pedestals.Length || i >= lastPedestalFrame.Length)
                    continue;

                var curr = f.Pedestals[i];
                var prev = lastPedestalFrame[i];

                using var chunkMs = new MemoryStream();
                using var w = new BinaryWriter(chunkMs);
                
                if (!WritePedestalDiff(w, prev, curr))
                    continue;
                
                entriesBw.Write((byte)ChunkType.PedestalState);
                entriesBw.Write(i);
                entriesBw.Write((int)chunkMs.Length);
                entriesBw.Write(chunkMs.ToArray());
                
                lastPedestalFrame[i] = curr;
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
            2 when scene != "Park" => $"{header.Players[0].Name}<#FFF> vs {header.Players[1].Name}<#FFF> - {finalScene}",
            > 0 when scene == "Park" => $"{header.Players[0].Name}<#FFF> - {scene}\n<scale=85%>{header.Players.Length} player{(header.Players.Length > 1 ? "s" : "")}",
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
            header.Structures.Length,
            header.Players.Length,
            header.PedestalCount
        );

        return replayInfo;
    }

    private static Frame[] ReadFrames(
        BinaryReader br,
        int frameCount,
        int structureCount,
        int playerCount,
        int pedestalCount
    )
    {
        Frame[] frames = new Frame[frameCount];

        StructureState[] lastStructures = Utilities.NewArray<StructureState>(structureCount);
        PlayerState[] lastPlayers = Utilities.NewArray<PlayerState>(playerCount);
        PedestalState[] lastPedestals = Utilities.NewArray<PedestalState>(pedestalCount);
        
        for (int f = 0; f < frameCount; f++)
        {
            int frameSize = br.ReadInt32();
            
            long frameEnd = br.BaseStream.Position + frameSize;
            
            Frame frame = new Frame();
            frame.Time = br.ReadSingle();
            
            frame.Structures = Utilities.NewArray(structureCount, lastStructures);
            frame.Players = Utilities.NewArray(playerCount, lastPlayers);
            frame.Pedestals = Utilities.NewArray(pedestalCount, lastPedestals);
            
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

                    case ChunkType.PedestalState:
                    {
                        var p = ReadPedestalChunk(br);
                        frame.Pedestals[index] = p;
                        lastPedestals[index] = p;
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

    static T ReadChunk<T, TField>(
        BinaryReader br,
        Func<T> ctor,
        Action<T, TField, BinaryReader> readField
    )
        where TField : Enum
    {
        int len = br.ReadInt32();
        long end = br.BaseStream.Position + len;

        T state = ctor();

        while (br.BaseStream.Position < end)
        {
            byte raw = br.ReadByte();
            TField id = (TField)Enum.ToObject(typeof(TField), raw);
            
            byte size = br.ReadByte();
            long fieldEnd = br.BaseStream.Position + size;

            if (!Enum.IsDefined(typeof(TField), id))
            {
                br.BaseStream.Position = fieldEnd;
                continue;
            }

            readField(state, id, br);
            
            br.BaseStream.Position = fieldEnd;
        }

        return state;
    }

    static PlayerState ReadPlayerChunk(BinaryReader br)
    {
        return ReadChunk<PlayerState, PlayerField>(
            br,
            () => new PlayerState(),
            (p, id, r) =>
            {
                switch (id)
                {
                    case PlayerField.VRRigPos: p.VRRigPos = r.ReadVector3(); break;
                    case PlayerField.VRRigRot: p.VRRigRot = r.ReadQuaternion(); break;
                    case PlayerField.LHandPos: p.LHandPos = r.ReadVector3(); break;
                    case PlayerField.LHandRot: p.LHandRot = r.ReadQuaternion(); break;
                    case PlayerField.RHandPos: p.RHandPos = r.ReadVector3(); break;
                    case PlayerField.RHandRot: p.RHandRot = r.ReadQuaternion(); break;
                    case PlayerField.HeadPos: p.HeadPos = r.ReadVector3(); break;
                    case PlayerField.HeadRot: p.HeadRot = r.ReadQuaternion(); break;
                    case PlayerField.currentStack: p.currentStack = r.ReadInt16(); break;
                    case PlayerField.Health: p.Health = r.ReadInt16(); break;
                    case PlayerField.active: p.active = r.ReadBoolean(); break;
                }
            }
        );
    }

    static StructureState ReadStructureChunk(BinaryReader br)
    {
        return ReadChunk<StructureState, StructureField>(
            br,
            () => new StructureState(),
            (s, id, r) =>
            {
                switch (id)
                {
                    case StructureField.position: s.position = r.ReadVector3(); break;
                    case StructureField.rotation: s.rotation = r.ReadQuaternion(); break;
                    case StructureField.active: s.active = r.ReadBoolean(); break;
                    case StructureField.grounded: s.grounded = r.ReadBoolean(); break;
                    case StructureField.isFlicked: s.isFlicked = r.ReadBoolean(); break;
                    case StructureField.isHeld: s.isHeld = r.ReadBoolean(); break;
                    case StructureField.isShaking: s.isShaking = r.ReadBoolean(); break;
                }
            }
        );
    }

    static PedestalState ReadPedestalChunk(BinaryReader br)
    {
        return ReadChunk<PedestalState, PedestalField>(
            br,
            () => new PedestalState(),
            (p, id, r) =>
            {
                switch (id)
                {
                    case PedestalField.position: p.position = r.ReadVector3(); break;
                    case PedestalField.active: p.active = r.ReadBoolean(); break;
                }
            }
        );
    }
}

[Serializable]
public class ReplayInfo
{
    public ReplaySerializer.ReplayHeader Header;
    public Frame[] Frames;
}

[Serializable]
public class Frame
{
    public float Time;
    public StructureState[] Structures;
    public PlayerState[] Players;
    public PedestalState[] Pedestals;
}

// ------- Structure State -------

[Serializable]
public class StructureState
{
    public Vector3 position;
    public Quaternion rotation;
    public bool active;
    public bool grounded;
    public bool isHeld;
    public bool isFlicked;
    public bool isShaking;
}

[Serializable]
public class StructureInfo
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
    isFlicked,
    isShaking
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
public class PlayerState
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
public class PlayerInfo
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

[Serializable]
public class VoiceTrackInfo
{
    public int ActorId;
    public string FileName;
    public float StartTime;
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

// ------- Pedestal State -------

[Serializable]
public class PedestalState
{
    public Vector3 position;
    
    public bool active;
}

public enum PedestalField
{
    position,
    active
}

// ------------------------

public enum ChunkType
{
    PlayerState,
    StructureState,
    PedestalState
}