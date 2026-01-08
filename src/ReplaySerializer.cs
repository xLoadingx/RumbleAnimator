using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppRUMBLE.Players.Scaling;
using Newtonsoft.Json;
using UnityEngine;
using System.Threading.Tasks;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Players;
using MelonLoader;
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

    public static void Write<TField>(this BinaryWriter bw, TField field, float f) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((byte)4);
        bw.Write(f);
    }

    public static void Write<TField>(this BinaryWriter bw, TField field, string s) where TField : Enum
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        
        bw.Write(Convert.ToByte(field));
        bw.Write((byte)bytes.Length);
        bw.Write(bytes);
    }
}

public class ReplaySerializer
{
    public static string FileName { get; set; }
    
    const float EPS = 0.00005f;
    const float ROT_EPS_ANGLE = 0.05f;

    [Serializable]
    public class ReplayHeader
    {
        public string Title;
        public string CustomMap;
        public string Version;
        public string Scene;
        public string Date;
        
        public float Duration;

        public int FrameCount;
        public int PedestalCount;
        public int TargetFPS;

        public PlayerInfo[] Players;
        public StructureInfo[] Structures;
    }
    
    
    // ----- Helpers -----
    
    static bool PosChanged(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude > EPS * EPS;
    }
    
    static bool RotChanged(Quaternion a, Quaternion b)
    {
        return Quaternion.Angle(a, b) > ROT_EPS_ANGLE;
    }
    
    
    // ----- Field-based diffs -----
    
    static bool WriteIf(bool condition, Action write)
    {
        if (!condition)
            return false;

        write();
        return true;
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

        any |= WriteIf(
            prev.activeShiftstoneVFX != curr.activeShiftstoneVFX,
            () => w.Write(PlayerField.activeShiftstoneVFX, (byte)curr.activeShiftstoneVFX)
        );

        any |= WriteIf(
            prev.leftShiftstone != curr.leftShiftstone,
            () => w.Write(PlayerField.leftShiftstone, (byte)curr.leftShiftstone)
        );
        
        any |= WriteIf(
            prev.rightShiftstone != curr.rightShiftstone,
            () => w.Write(PlayerField.rightShiftstone, (byte)curr.rightShiftstone)
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

    static bool WriteEvent(BinaryWriter w, EventChunk e)
    {
        bool any = false;

        any |= WriteIf(
            true,
            () => w.Write(EventField.type, (byte)e.type)
        );

        any |= WriteIf(
            e.position != default,
            () => w.Write(EventField.position, e.position)
        );

        any |= WriteIf(
            e.rotation != default,
            () => w.Write(EventField.rotation, e.rotation)
        );

        any |= WriteIf(
            !string.IsNullOrEmpty(e.masterId),
            () => w.Write(EventField.masterId, e.masterId)
        );

        any |= WriteIf(
            e.playerIndex > -1,
            () => w.Write(EventField.playerIndex, e.playerIndex)
        );

        any |= WriteIf(
            e.Length > 0,
            () => w.Write(EventField.length, e.Length)
        );
        
        any |= WriteIf(
            e.ArmSpan > 0,
            () => w.Write(EventField.armspan, e.ArmSpan)
        );

        any |= WriteIf(
            e.markerType != MarkerType.None,
            () => w.Write(EventField.markerType, (byte)e.markerType)
        );

        any |= WriteIf(
            e.damage != 0,
            () => w.Write(EventField.damage, (byte)e.damage)
        );

        return any;
    }
    
    
    // ----- Serialization -----
    
    public static async Task BuildReplayPackage(
        string outputPath, 
        ReplayInfo replay, 
        Action done = null
    )
    {
        byte[] rawReplay = SerializeReplayFile(replay);

        byte[] compressedReplay = await Task.Run(() => Compress(rawReplay));

        MelonCoroutines.Start(FinishOnMainThread(outputPath, replay, compressedReplay, done));
    }

    static IEnumerator FinishOnMainThread(
        string outputPath,
        ReplayInfo replay,
        byte[] compressedReplay,
        Action done
    )
    {
        yield return null;
        
        string manifestJson = JsonConvert.SerializeObject(
            replay.Header,
            Formatting.Indented
        );
        
        using (var fs = new FileStream(outputPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(manifestJson);

            var replayEntry = zip.CreateEntry("replay", CompressionLevel.NoCompression);
            using (var stream = replayEntry.Open())
                stream.Write(compressedReplay, 0, compressedReplay.Length);
        }

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

        foreach (var f in replay.Frames)
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
                
                if (WriteStructureDiff(w, prev, curr))
                {
                    entriesBw.Write((byte)ChunkType.StructureState);
                    entriesBw.Write(i);
                    entriesBw.Write((int)chunkMs.Length);
                    entriesBw.Write(chunkMs.ToArray());
                    entryCount++;
                }
                
                lastStructureFrame[i] = curr;
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

                if (WritePlayerDiff(w, prev, curr))
                {
                    entriesBw.Write((byte)ChunkType.PlayerState);
                    entriesBw.Write(i);
                    entriesBw.Write((int)chunkMs.Length);
                    entriesBw.Write(chunkMs.ToArray());
                    entryCount++;
                }
                
                lastPlayerFrame[i] = curr;
            }

            for (int i = 0; i < pedestalCount; i++)
            {
                if (i >= f.Pedestals.Length || i >= lastPedestalFrame.Length)
                    continue;

                var curr = f.Pedestals[i];
                var prev = lastPedestalFrame[i];

                using var chunkMs = new MemoryStream();
                using var w = new BinaryWriter(chunkMs);

                if (WritePedestalDiff(w, prev, curr))
                {
                    entriesBw.Write((byte)ChunkType.PedestalState);
                    entriesBw.Write(i);
                    entriesBw.Write((int)chunkMs.Length);
                    entriesBw.Write(chunkMs.ToArray());
                    entryCount++;
                }
                
                lastPedestalFrame[i] = curr;
            }

            for (int i = 0; i < f.Events.Length; i++)
            {
                using var chunkMs = new MemoryStream();
                using var w = new BinaryWriter(chunkMs);

                if (WriteEvent(w, f.Events[i]))
                {
                    entriesBw.Write((byte)ChunkType.Event);
                    entriesBw.Write(i);
                    entriesBw.Write((int)chunkMs.Length);
                    entriesBw.Write(chunkMs.ToArray());
                    entryCount++;
                }
            }

            frameBw.Write(entryCount);
            frameBw.Write(entriesMs.ToArray());

            byte[] frameData = frameMs.ToArray();

            bw.Write(frameData.Length);
            bw.Write(frameData);
        }
        
        return ms.ToArray();
    }
    
    
    // ----- Codec -----
    public static byte[] Compress(byte[] data)
    {
        using (var ms = new MemoryStream())
        {
            using (var brotli = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                brotli.Write(data, 0, data.Length);
            }

            return ms.ToArray();
        }
    }

    public static byte[] Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }
    
    
    public static string FormatReplayString(string pattern, ReplayHeader header)
    {
        var scene = Utilities.GetFriendlySceneName(header.Scene);
        var customMap = header.CustomMap;
        var finalScene = string.IsNullOrEmpty(customMap) ? scene : customMap;
        var parsedDate = string.IsNullOrEmpty(header.Date)
            ? DateTime.MinValue
            : DateTime.Parse(header.Date);
        var duration = TimeSpan.FromSeconds(header.Duration);
        
        string GetPlayer(int index) =>
            index >= 0 && index < header.Players?.Length ? $"<#FFF>{header.Players[index].Name}<#FFF>" : "";

        var values = new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = $"<#FFF>{header.Players?.FirstOrDefault(p => p.WasHost)?.Name ?? ""}<#FFF>",
            ["Client"] = $"<#FFF>{header.Players?.FirstOrDefault(p => !p.WasHost)?.Name ?? ""}<#FFF>",
            ["LocalPlayer"] = $"<#FFF>{header.Players?[0]?.Name}<#FFF>",
            ["Scene"] = finalScene,
            ["Map"] = finalScene,
            ["DateTime"] = parsedDate == DateTime.MinValue ? "Unknown Date" : parsedDate,
            ["PlayerCount"] = $"{header.Players?.Length ?? 0} Player{((header.Players?.Length ?? 0) == 1 ? "" : "s")}",
            ["Version"] = header.Version ?? "",
            ["StructureCount"] = (header.Structures?.Length.ToString() ?? "0") + " Structure" + ((header.Structures?.Length ?? 0) == 1 ? "" : "s"),
            ["Title"] = header.Title,
            ["Duration"] = $"{duration.Minutes}:{duration.Seconds:D2}",
            ["FPS"] = header.TargetFPS
        };

        for (int i = 0; i < (header.Players?.Length ?? 0); i++)
            values[$"Player{i + 1}"] = GetPlayer(i);

        var regex = new Regex(@"\{(\w+)(?::([^}]+))?\}");

        return regex.Replace(pattern, match =>
        {
            var key = match.Groups[1].Value;
            var param = match.Groups[2].Success ? match.Groups[2].Value : null;

            if (key.Equals("PlayerList", StringComparison.OrdinalIgnoreCase))
            {
                int count = 3;

                if (!string.IsNullOrEmpty(param) && int.TryParse(param, out int parsed))
                    count = parsed;

                if (header.Players != null)
                    return ReplayFiles.BuildPlayerLine(header.Players, count);
            }

            if (values.TryGetValue(key, out var val))
            {
                if (val is DateTime dateTime && param != null)
                    return dateTime.ToString(param);
                return val.ToString();
            }

            return match.Value;
        });
    }
    
    
    // ----- Manifest -----
    
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
    

    // ----- Deserialization -----
    
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
            replayInfo,
            header.FrameCount,
            header.Structures.Length,
            header.Players.Length,
            header.PedestalCount
        );

        return replayInfo;
    }
    
    private static Frame[] ReadFrames(
        BinaryReader br, 
        ReplayInfo info, 
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
            var events = new System.Collections.Generic.List<EventChunk>();
            
            int entryCount = br.ReadInt32();

            for (int e = 0; e < entryCount; e++)
            {
                ChunkType type = (ChunkType)br.ReadByte();
                int index = br.ReadInt32();

                switch (type)
                {
                    case ChunkType.StructureState:
                    {
                        var s = ReadStructureChunk(br, lastStructures[index].Clone());
                        frame.Structures[index] = s;
                        lastStructures[index] = s;
                        break;
                    }

                    case ChunkType.PlayerState:
                    {
                        var p = ReadPlayerChunk(br, lastPlayers[index].Clone());
                        frame.Players[index] = p;
                        lastPlayers[index] = p;
                        break;
                    }

                    case ChunkType.PedestalState:
                    {
                        var p = ReadPedestalChunk(br, lastPedestals[index].Clone());
                        frame.Pedestals[index] = p;
                        lastPedestals[index] = p;
                        break;
                    }

                    case ChunkType.Event:
                    {
                        var evt = ReadEventChunk(br);
                        events.Add(evt);
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

            frame.Events = events?.ToArray();
            
            br.BaseStream.Position = frameEnd;

            frames[f] = frame;
        }
        return frames;
        
    }
    
    
    // ----- Chunk Reading -----
    
    static T ReadChunk<T, TField>(
        BinaryReader br, 
        Func<T> ctor, 
        Action<T, TField, BinaryReader> readField
    ) where TField : Enum
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
    

    static PlayerState ReadPlayerChunk(BinaryReader br, PlayerState baseState)
    {
        return ReadChunk<PlayerState, PlayerField>(
            br,
            () => baseState,
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
                    case PlayerField.activeShiftstoneVFX: 
                        p.activeShiftstoneVFX = (PlayerShiftstoneVFX)r.ReadByte(); break;
                    case PlayerField.leftShiftstone: p.leftShiftstone = r.ReadByte(); break;
                    case PlayerField.rightShiftstone: p.rightShiftstone = r.ReadByte(); break;
                }
            }
        );
    }

    static StructureState ReadStructureChunk(BinaryReader br, StructureState baseState)
    {
        return ReadChunk<StructureState, StructureField>(
            br,
            () => baseState,
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

    static PedestalState ReadPedestalChunk(BinaryReader br, PedestalState baseState)
    {
        return ReadChunk<PedestalState, PedestalField>(
            br,
            () => baseState,
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

    static EventChunk ReadEventChunk(BinaryReader br)
    {
        return ReadChunk<EventChunk, EventField>(
            br,
            () => new EventChunk(),
            (e, id, r) =>
            {
                switch (id)
                {
                    case EventField.type: e.type = (EventType)r.ReadByte(); break;
                    case EventField.position: e.position = r.ReadVector3(); break;
                    case EventField.rotation: e.rotation = r.ReadQuaternion(); break;
                    case EventField.masterId: e.masterId = r.ReadString(); break;
                    case EventField.playerIndex: e.playerIndex = r.ReadInt32(); break;
                    case EventField.armspan: e.ArmSpan = r.ReadSingle(); break;
                    case EventField.length: e.Length = r.ReadSingle(); break;
                    case EventField.markerType: e.markerType = (MarkerType)r.ReadByte(); break;
                    case EventField.damage: e.damage = r.ReadInt32(); break;
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
    public EventChunk[] Events;

    public Frame Clone()
    {
        var frame = new Frame();
        frame.Time = Time;
        frame.Structures = Utilities.NewArray(Structures.Length, Structures);
        frame.Players = Utilities.NewArray(Players.Length, Players);
        frame.Pedestals = Utilities.NewArray(Pedestals.Length, Pedestals);
        frame.Events = Utilities.NewArray(Events.Length, Events);

        return frame;
    }
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

    public StructureState Clone()
    {
        return new StructureState
        {
            position = position,
            rotation = rotation,
            active = active,
            grounded = grounded,
            isHeld = isHeld,
            isFlicked = isFlicked,
            isShaking = isShaking
        };
    }
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

    public PlayerShiftstoneVFX activeShiftstoneVFX;
    
    public int leftShiftstone;
    public int rightShiftstone;

    public PlayerState Clone()
    {
        return new PlayerState
        {
            VRRigPos = VRRigPos,
            VRRigRot = VRRigRot,
            LHandPos = LHandPos,
            LHandRot = LHandRot,
            RHandPos = RHandPos,
            RHandRot = RHandRot,
            HeadPos = HeadPos,
            HeadRot = HeadRot,
            currentStack = currentStack,
            Health = Health,
            active = active,
            activeShiftstoneVFX = activeShiftstoneVFX,
            leftShiftstone = leftShiftstone,
            rightShiftstone = rightShiftstone
        };
    }
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

    public PlayerInfo(Player copyPlayer)
    {
        var player = copyPlayer.Data;

        ActorId = (byte)player.GeneralData.ActorNo;
        MasterId = player.GeneralData.PlayFabMasterId;
        Name = player.GeneralData.PublicUsername;
        BattlePoints = player.GeneralData.BattlePoints;
        VisualData = player.VisualData.ToPlayfabDataString();
        EquippedShiftStones = player.EquipedShiftStones.ToArray();
        Measurement = player.PlayerMeasurement;
        WasHost = (player.GeneralData.ActorNo == PhotonNetwork.MasterClient?.ActorNumber);
    }
    
    [JsonConstructor]
    public PlayerInfo() { }
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
    active,
    
    activeShiftstoneVFX,
    leftShiftstone,
    rightShiftstone
}

[Flags]
public enum PlayerShiftstoneVFX : byte
{
    None = 0,
    Charge = 1 << 0,
    Adamant = 1 << 1,
    Vigor = 1 << 2,
    Surge = 1 << 3
}

[Serializable]
public enum PlayerShiftstones : byte
{
    None,
    Vigor,
    Guard,
    Flow,
    Stubborn,
    Charge,
    Volatile,
    Surge,
    Adamant
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

    public PedestalState Clone()
    {
        return new PedestalState
        {
            position = position,
            active = active
        };
    }
}

public enum PedestalField
{
    position,
    active
}

// ------- Event -------

[Serializable]
public class EventChunk
{
    public EventType type;
    
    // General Info
    public Vector3 position;
    public Quaternion rotation;
    public string masterId;
    public int playerIndex;

    // Player measurement
    public float Length;
    public float ArmSpan;
    
    // Marker
    public MarkerType markerType;
    
    // Damage HitMarker
    public int damage;
}

public enum EventType : byte
{
    PlayerMeasurement,
    Marker,
    DamageHitmarker
}

public enum EventField
{
    type,
    position,
    rotation,
    masterId,
    length,
    armspan,
    markerType,
    playerIndex,
    damage
}

[Serializable]
public enum MarkerType : byte
{
    None,
    Manual,
    RoundEnd,
    MatchEnd,
    LargeDamage
}

// ------------------------

public enum ChunkType
{
    PlayerState,
    StructureState,
    PedestalState,
    Event
}