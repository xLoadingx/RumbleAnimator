namespace RumbleAnimator.Recording;

[Serializable]
public enum FrameType
{
    PlayerUpdate = 0,
    StackEvent = 1,
    StructureUpdate = 2,
    StructureData = 3,
    PlayerData = 4
}

[Serializable]
public struct FrameData
{
    public FramePoseData positions;
    public FrameRotationData rotations;
    public float timestamp;
}

[Serializable]
public struct StackEvent
{
    public float timestamp;
    public string stack;
}

[Serializable]
public struct StructureFrame
{
    public float timestamp;
    public SVector3 position;
    public SQuaternion rotation;
}

[Serializable]
public class StructureReplayData
{
    public string type;
    public bool existInScene;
    public List<StructureFrame> frames = new();
    public int CurrentFrameIndex = 0;
    public bool isActive;
}

[Serializable]
public struct FramePoseData
{
    public SVector3 lHandPos;
    public SVector3 rHandPos;
    public SVector3 headPos;

    public float visualsY;
    public SVector3 vrPos;
    public SVector3 controllerPos;
}

[Serializable]
public struct FrameRotationData
{
    public SQuaternion lHandRot;
    public SQuaternion rHandRot;

    public SQuaternion headRot;
    public SQuaternion vrRot;
    public SQuaternion controllerRot;
}