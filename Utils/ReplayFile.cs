using System.Text;
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
        string hostPlayer = null;
        
        var orderedPlayers = Calls.Players.GetPlayersInActorNoOrder();
        if (orderedPlayers.Count > 0)
            hostPlayer = orderedPlayers[0].Data.GeneralData.PublicUsername;

        var header = new ReplayHeader
        {
            Scene = Utilities.GetFriendlySceneName(),
            Date = DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss"),

            LocalPlayer = localPlayer,
            opponent = opponentName,

            HostPlayer = hostPlayer,

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

    public static ReplayHeader GetHeaderFromFile(string path, FileStream stream, BinaryReader reader)
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

    public static (Dictionary<string, PlayerReplayState>, List<List<StructureFrame>>) GetReplayFromFile(string path)
    {
        var players = new Dictionary<string, PlayerReplayState>();
        var structureFrames = new List<List<StructureFrame>>();
        
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);
        
        var header = GetHeaderFromFile(path, stream, reader);

        MelonLogger.Msg($"[RumbleAnimator] Loaded replay from scene: {header.Scene}, date: {header.Date}");

        while (reader.BaseStream.Position < reader.BaseStream.Length)
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

                case FrameType.VisualData:
                    Codec.DecodeVisualData(data, players);
                    break;

                case FrameType.StructureUpdate:
                    Codec.DecodeStructureFrame(data, structureFrames);
                    break;

                default:
                    MelonLogger.Warning($"[RumbleAnimator] Unknown frame type: {frameType}");
                    break;
            }
        }

        return (players, structureFrames);
    }
}