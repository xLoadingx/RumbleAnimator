using System.Collections;
using System.IO.Compression;
using System.Text;
using Il2CppPhoton.Pun;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleAnimator.Recording;
using RumbleModdingAPI;

namespace RumbleAnimator.Utils;

public class ReplayFile
{
    public static BinaryWriter ReplayWriter { get; set; }
    public static string FileName { get; set; }

    [Serializable]
    public class ReplayHeader
    {
        public readonly string Version = BuildInfo.Version;
        
        public string Scene;
        public string Date;

        public string LocalPlayer;
        public string opponent;

        public string HostPlayer;

        public int playerCount;
        public Dictionary<string, object> Meta = new();
    }

    public static Task CompressAsync(string inputPath, string outputPath)
    {
        return Task.Run(() =>
        {
            using var input = File.OpenRead(inputPath);
            using var output = File.Create(outputPath);
            using var brotli = new BrotliStream(output, CompressionLevel.Optimal);
            input.CopyTo(brotli);
        });
    }

    public static MemoryStream DecompressToMemory(string path)
    {
        using var input = File.OpenRead(path);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var output = new MemoryStream();
        brotli.CopyTo(output);
        output.Position = 0;
        return output;
    }

    public static BinaryReader GetDecompressedReader(string path)
    {
        var memStream = DecompressToMemory(path);
        return new BinaryReader(memStream);
    }

    public static async Task CompressAndSaveAsync(string name)
    {
        string replayPath = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays", name);
        string compressedPath = replayPath + ".br";

        await ReplayFile.CompressAsync(replayPath, compressedPath);

        if (File.Exists(compressedPath))
        {
            File.Delete(replayPath);
            MelonLogger.Msg($"[RumbleAnimator] Compressed and deleted original. Final file: {Path.GetFileName(compressedPath)}");
        }
        else
        {
            MelonLogger.Warning("[RumbleAnimator] Compression failed - original replay kept.");
        }
    }

    public static IEnumerator TryCompressAllReplays()
    {
        string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays");
        var files = Directory.GetFiles(dir, "*.replay");

        List<Task> activeTasks = new();
        int maxConcurrency = 4;

        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            var task = ReplayFile.CompressAndSaveAsync(fileName);
            activeTasks.Add(task);

            if (activeTasks.Count >= maxConcurrency)
            {
                while (activeTasks.Any(t => !t.IsCompleted))
                    yield return null;

                activeTasks.RemoveAll(t => t.IsCompleted);
            }
        }

        while (activeTasks.Any(t => !t.IsCompleted))
            yield return null;

        MelonLogger.Msg($"[RumbleAnimator] Finished compressing all replays.");
    }

    public static string GenerateReplayFormat(int playerCount, string opponent = null)
    {
        string timestamp = DateTime.Now.ToString("MMMdd_hhmmtt").ToUpper();
        string localName = Utilities.TrimPlayerName(Calls.Players.GetLocalPlayer().Data.GeneralData.PublicUsername);
        opponent = Utilities.TrimPlayerName(opponent);

        string sceneName = Utilities.TryGetActiveCustomMap() ?? Utilities.GetFriendlySceneName();
        string matchFormat = $"{localName}-vs-{opponent}_On_{sceneName}_{timestamp}.replay";

        return sceneName switch
        {
            "Gym" =>
                $"Replay_{timestamp}_{sceneName}_{localName}.replay",

            "Park" =>
                $"Replay_{timestamp}_{sceneName}_{playerCount}P_{localName}.replay",

            // Custom maps and matches
            _ =>
                matchFormat
        };
    }

    public static void WriteFramedData(byte[] data, int frame, FrameType type)
    {
        Main._writeBuffer.AddRange(BitConverter.GetBytes((short)data.Length));
        Main._writeBuffer.AddRange(BitConverter.GetBytes(frame));
        Main._writeBuffer.Add((byte)type);
        Main._writeBuffer.AddRange(data);
    }

    public static void InitializeReplayFile()
    {
        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays");
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        string sceneName = Utilities.GetFriendlySceneName();
        string opponentName = null;
        string opponentNameTrimmed = null;
        if (Calls.Players.GetAllPlayers().Count > 1)
        {
            opponentName = Calls.Players.GetEnemyPlayers().FirstOrDefault().Data.GeneralData.PublicUsername;
            opponentNameTrimmed = Utilities.TrimPlayerName(opponentName);
        }
            
        FileName = GenerateReplayFormat(Calls.Players.GetAllPlayers().Count, opponentNameTrimmed);

        string path = Path.Combine(directory, FileName);

        var file = new FileStream(path, FileMode.Create);
        ReplayWriter = new BinaryWriter(file);

        byte[] magicBytes = Encoding.ASCII.GetBytes("REPLAY");
        ReplayWriter.Write(magicBytes);

        string localPlayer = Calls.Players.GetLocalPlayer().Data.GeneralData.PublicUsername;
        string hostPlayer = Calls.Players.GetPlayerByActorNo(PhotonNetwork.MasterClient?.ActorNumber ?? -1)?.Data?.GeneralData?.PublicUsername;

        var header = new ReplayHeader
        {
            Scene = Utilities.GetFriendlySceneName(),
            Date = DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss"),

            LocalPlayer = Utilities.FixColorTags(localPlayer),
            opponent = Utilities.FixColorTags(opponentName),

            HostPlayer = Utilities.FixColorTags(hostPlayer),

            playerCount = Calls.Players.GetAllPlayers().Count
        };
        
        var customMapName = Utilities.TryGetActiveCustomMap();
        if (customMapName is not null)
        {
            header.Scene = "Custom";
            header.Meta["customMapName"] = customMapName;
        }
        
        string jsonHeader = JsonConvert.SerializeObject(header);
        short headerLength = (short)Encoding.UTF8.GetByteCount(jsonHeader);

        ReplayWriter.Write(headerLength);
        ReplayWriter.Write(Encoding.UTF8.GetBytes(jsonHeader));

        MelonLogger.Msg($"[RumbleAnimator] Replay file created at {path}");
    }

    public static ReplayHeader GetHeaderFromFile(string path, BinaryReader reader)
    {
        if (!File.Exists(path))
        {
            MelonLogger.Msg($"[RumbleAnimator] Replay file not found: {path}");
            return null;
        }

        var magic = reader.ReadBytes(6);
        if (Encoding.ASCII.GetString(magic) != "REPLAY")
        {
            MelonLogger.Error("[RumbleAnimator] Invalid replay file format.");
            return null;
        }

        short headerLength = reader.ReadInt16();
        string jsonheader = Encoding.UTF8.GetString(reader.ReadBytes(headerLength));
        return JsonConvert.DeserializeObject<ReplayHeader>(jsonheader);
    }
    
    public static void WithReplayReader(string path, Action<BinaryReader> readAction)
    {
        if (!File.Exists(path))
        {
            MelonLogger.Warning($"[ReplayIO] File not found: {path}");
            return;
        }

        if (path.EndsWith(".br"))
        {
            using var file   = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var brotli = new BrotliStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(brotli);
            readAction(reader);
        }
        else
        {
            using var file   = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(file);
            readAction(reader);
        }
    }

    public static ReplayHeader ParseReplay(
        BinaryReader reader,
        string path,
        Dictionary<string, PlayerReplayState> players,
        List<StructureReplayData> structureFrames)
    {
        var header = GetHeaderFromFile(path, reader);

        MelonLogger.Msg($"[RumbleAnimator] Loaded replay from scene: {header.Scene}, date: {header.Date}");

        while (true)
        {
            try
            {
                short dataLength = reader.ReadInt16();
                int frameID = reader.ReadInt32();
                FrameType frameType = (FrameType)reader.ReadByte();
                byte[] data = reader.ReadBytes(dataLength);

                switch (frameType)
                {
                    case FrameType.PlayerUpdate:
                        Codec.DecodePlayerFrame(data, players);
                        break;

                    case FrameType.StackEvent:
                        Codec.DecodeStackEvent(data, players);
                        break;

                    case FrameType.PlayerData:
                        Codec.DecodePlayerData(data, players);
                        break;

                    case FrameType.StructureUpdate:
                        Codec.DecodeStructureFrame(data, structureFrames);
                        break;
                    
                    case FrameType.StructureData:
                        Codec.DecodeStructureData(data, structureFrames);
                        break;

                    default:
                        MelonLogger.Warning($"[RumbleAnimator] Unknown frame type: {frameType}");
                        break;
                }
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }

        return header;
    }

    public static (Dictionary<string, PlayerReplayState>, List<StructureReplayData>, ReplayHeader header) GetReplayFromFile(string path)
    {
        if (!path.EndsWith(".br") && File.Exists(path + ".br"))
            path += ".br";
        
        var players = new Dictionary<string, PlayerReplayState>();
        var structureFrames = new List<StructureReplayData>();
        ReplayHeader header = null;

        WithReplayReader(path, reader =>
        {
            header = ParseReplay(reader, path, players, structureFrames);
        });

        return (players, structureFrames, header);
    }

    public static void DebugDump(string path)
    {
        WithReplayReader(path, reader =>
        {
            var header = GetHeaderFromFile(path, reader);

            MelonLogger.Msg($"[RumbleAnimator] Loaded replay from scene: {header.Scene}, date: {header.Date}");

            while (true)
            {
                try
                {
                    short dataLength = reader.ReadInt16();
                    int frameID = reader.ReadInt32();
                    FrameType frameType = (FrameType)reader.ReadByte();
                    byte[] data = reader.ReadBytes(dataLength);
                    
                    MelonLogger.Msg($"[Dump] Frame {frameID} | Type: {frameType} | Size: {dataLength}");
                }
                catch (EndOfStreamException)
                {
                    break;
                }
            }
        });
    }
}