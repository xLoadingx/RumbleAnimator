using Il2CppRUMBLE.Players.Scaling;
using MelonLoader;
using RumbleAnimator.Recording;

namespace RumbleAnimator.Utils;

public class Codec
{
    public static void EncodeFrameData(BinaryWriter bw, FrameData frame)
    {
        WriteVec3(bw, frame.positions.lHandPos);
        WriteVec3(bw, frame.positions.rHandPos);
        WriteVec3(bw, frame.positions.headPos);
        bw.Write(frame.positions.visualsY);
        WriteVec3(bw, frame.positions.vrPos);
        WriteVec3(bw, frame.positions.controllerPos);

        WriteQuat(bw, frame.rotations.lHandRot);
        WriteQuat(bw, frame.rotations.rHandRot);
        WriteQuat(bw, frame.rotations.headRot);
        WriteQuat(bw, frame.rotations.vrRot);
        WriteQuat(bw, frame.rotations.controllerRot);
    }

    public static void DecodePlayerData(byte[] data, Dictionary<string, PlayerReplayState> players)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        string masterID = br.ReadString();
        string visualData = br.ReadString();
        int battlePoints = br.ReadInt32();

        float length = br.ReadSingle();
        float armSpan = br.ReadSingle();

        PlayerMeasurement measurement = new PlayerMeasurement(length, armSpan);

        if (!players.TryGetValue(masterID, out var state))
        {
            state = new PlayerReplayState(masterID, visualData, battlePoints, measurement);
        }
        else
        {
            state.Data.VisualData = visualData;
            state.Data.BattlePoints = battlePoints;
            state.Data.Measurement = measurement;
        }

        players[masterID] = state;
    }

    
    public static byte[] EncodePlayerFrame(string masterID, FrameData frameTypes, float timestamp)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(timestamp);
        bw.Write(masterID);

        Codec.EncodeFrameData(bw, frameTypes);

        return ms.ToArray();
    }

    public static void DecodePlayerFrame(byte[] data, Dictionary<string, PlayerReplayState> players)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        float timestamp = br.ReadSingle();
        string masterID = br.ReadString();

        var frameData = new FrameData
        {
            positions = new FramePoseData
            {
                lHandPos = ReadVec3(br),
                rHandPos = ReadVec3(br),
                headPos = ReadVec3(br),
                visualsY = br.ReadSingle(),
                vrPos = ReadVec3(br),
                controllerPos = ReadVec3(br)
            },
            rotations = new FrameRotationData
            {
                lHandRot = ReadQuat(br),
                rHandRot = ReadQuat(br),
                headRot = ReadQuat(br),
                vrRot = ReadQuat(br),
                controllerRot = ReadQuat(br)
            },
            timestamp = timestamp
        };

        if (!players.TryGetValue(masterID, out var state))
        {
            state = new PlayerReplayState(masterID, "", 0, PlayerMeasurement.Default);
            players[masterID] = state;
        }

        state.Data.Frames.Add(frameData);
    }

    public static void EncodeStackEvent(BinaryWriter bw, string masterID, StackEvent stackEvent)
    {
        bw.Write(masterID);
        bw.Write(stackEvent.timestamp);
        bw.Write(stackEvent.stack);
    }

    public static void DecodeStackEvent(byte[] data, Dictionary<string, PlayerReplayState> players)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        string masterID = br.ReadString();
        float timestamp = br.ReadSingle();
        string stack = br.ReadString();

        if (!players.TryGetValue(masterID, out var state))
        {
            state = new PlayerReplayState(masterID, "", 0, PlayerMeasurement.Default);
            players[masterID] = state;
        }

        state.Data.StackEvents.Add(new StackEvent
        {
            timestamp = timestamp,
            stack = stack
        });

        MelonLogger.Msg($"[DecodeStackEvent] time={timestamp}, name='{stack}");
    }

    public static byte[] EncodeStructureFrame(StructureFrame frame, int index, float timestamp)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(index);
        bw.Write(timestamp);

        WriteVec3(bw, frame.position);
        WriteQuat(bw, frame.rotation);

        return ms.ToArray();
    }

    public static void DecodeStructureFrame(byte[] data, List<StructureReplayData> structures)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        int index = br.ReadInt32();
        float timestamp = br.ReadSingle();
        var position = ReadVec3(br);
        var rotation = ReadQuat(br);

        var frame = new StructureFrame
        {
            timestamp = timestamp,
            position = position,
            rotation = rotation
        };
        
        while (structures.Count <= index)
            structures.Add(new StructureReplayData());

        structures[index].frames.Add(frame);
    }
    
    public static byte[] EncodeStructureData(string type, bool existInScene, int index)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(index);
        bw.Write(type);
        bw.Write(existInScene);

        return ms.ToArray();
    }

    public static void DecodeStructureData(byte[] data, List<StructureReplayData> structures)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        int index = br.ReadInt32();
        string type = br.ReadString();
        bool existInScene = br.ReadBoolean();
        
        while (structures.Count <= index)
            structures.Add(new StructureReplayData());

        structures[index].type = type;
        structures[index].existInScene = existInScene;
    }

    private static void WriteVec3(BinaryWriter bw, SVector3 vec3)
    {
        bw.Write(vec3.x);
        bw.Write(vec3.y);
        bw.Write(vec3.z);
    }

    private static SVector3 ReadVec3(BinaryReader br)
    {
        return new SVector3
        {
            x = br.ReadSingle(),
            y = br.ReadSingle(),
            z = br.ReadSingle()
        };
    }

    private static void WriteQuat(BinaryWriter bw, SQuaternion q)
    {
        bw.Write(q.x);
        bw.Write(q.y);
        bw.Write(q.z);
        bw.Write(q.w);
    }

    private static SQuaternion ReadQuat(BinaryReader br)
    {
        return new SQuaternion
        {
            x = br.ReadSingle(),
            y = br.ReadSingle(),
            z = br.ReadSingle(),
            w = br.ReadSingle()
        };
    }
}