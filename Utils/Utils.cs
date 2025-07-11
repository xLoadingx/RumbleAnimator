using System.Collections;
using Il2CppRootMotion.FinalIK;
using Il2CppRUMBLE.Interactions;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Slabs.Forms;
using Il2CppSystem.Text;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleModdingAPI;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using RumbleAnimator.Recording;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace RumbleAnimator.Utils;

public static class Utilities
{
    public static Dictionary<string, string> SceneNameMap { get; } = new()
    {
        { "Gym", "Gym" },
        { "Park", "Park" },
        { "Map0", "Ring" },
        { "Map1", "Pit" }
    };

    public static Il2CppAssetBundle LoadBundle(string name)
    {
        using var stream = Main.ModAssembly.GetManifestResourceStream($"RumbleAnimator.AssetBundles.{name}");

        byte[] bundleBytes = new byte[stream.Length];
        stream.Read(bundleBytes, 0, bundleBytes.Length);

        return Il2CppAssetBundleManager.LoadFromMemory(bundleBytes);
    }

    public static string GetFriendlySceneName()
    {
        string sceneKey = Main.currentScene?.Trim();
        return SceneNameMap.TryGetValue(sceneKey, out var friendly) ? friendly : sceneKey;
    }

    public static string TrimPlayerName(string playerName)
    {
        if (string.IsNullOrEmpty(playerName))
            return "Unknown";

        var result = new StringBuilder();
        bool inTag = false;

        foreach (char c in playerName)
        {
            if (c == '<')
            {
                inTag = true;
                continue;
            }

            if (c == '>')
            {
                inTag = false;
                continue;
            }

            if (inTag) continue;

            if (char.IsLetterOrDigit(c) || c == '_' || c == ' ')
                result.Append(c);
        }

        return result.ToString();
    }

    public static bool HasTransformChanged(
        Vector3 posA, Quaternion rotA,
        Vector3 posB, Quaternion rotB,
        float posTolerance = 0.001f,
        float rotTolerance = 0.01f)
    {
        return Vector3.Distance(posA, posB) > posTolerance
               || Quaternion.Angle(rotA, rotB) > rotTolerance;
    }
}

public static class SlabBuilder
{
    public static GameObject slabPrefab;
    public static Transform spawnTransform;
    public static InteractionButton slabButton;
    public static List<TextMeshPro> fileNameTexts = new();
    public static GameObject previousPageButton;
    public static GameObject nextPageButton;
    
    public static Dictionary<int, string[]> paginatedFiles = new();
    private static int currentPage = 0;

    public static bool IsBuilt;
    public static float Distance = 0.4f;
    public static float HeightOffset = 2.0f;

    public static void BuildSlab()
    {
        slabPrefab = GameObject.Instantiate(Calls.GameObjects.Gym.Logic.School.GetGameObject());
        slabPrefab.name = "ReplaySlab";
        slabPrefab.transform.localScale = Vector3.one * 0.8f;
        slabPrefab.transform.position = new Vector3(0, -100, 0);
        GameObject.DontDestroyOnLoad(slabPrefab);

        Transform slab = slabPrefab.transform.GetChild(0);
        slab.transform.localScale = Vector3.one;
        slab.localPosition = Vector3.zero;
        slab.GetChild(0).GetChild(0).localPosition = Vector3.zero;

        slabButton = slabPrefab.transform.GetChild(1).GetChild(1).GetChild(0).GetComponent<InteractionButton>();
        spawnTransform = slab.GetChild(3);

        Transform slabCanvas = slab.GetChild(0).GetChild(0).GetChild(0).GetChild(1);
        for (int i = 0; i < slabCanvas.childCount; i++)
            GameObject.Destroy(slabCanvas.GetChild(i).gameObject);

        GameObject slabMesh = slab.GetChild(0).GetChild(0).GetChild(0).GetChild(0).GetChild(0).gameObject;
        GameObject tutorialSlab = Calls.GameObjects.Gym.Tutorial.StaticTutorials.RUMBLEStarterGuide
            .BaseSlab.SlabRock.GetGameObject();

        slabMesh.GetComponent<MeshFilter>().mesh = tutorialSlab.GetComponent<MeshFilter>().mesh;
        slabMesh.GetComponent<MeshRenderer>().material = tutorialSlab.GetComponent<MeshRenderer>().material;
        slabMesh.transform.localRotation *= Quaternion.Euler(0, 180, 0);

        var bundle = Utilities.LoadBundle("replaysettingmeshes");
        var meshes = GameObject.Instantiate(bundle.LoadAsset<GameObject>("ReplayMenuMeshes"));
        meshes.name = "Buttons";
        meshes.transform.SetParent(slabMesh.transform);
        meshes.transform.localRotation = Quaternion.Euler(270, 180, 0);
        meshes.transform.localScale = Vector3.one * 118.1141f;
        meshes.transform.localPosition = new Vector3(-0.0651f, 0.2347f, 0.1563f);

        var replayFilesText =
            Calls.Create.NewText("Replay Files", 0.0023f, Color.yellow, Vector3.zero, Quaternion.identity);
        replayFilesText.name = "ReplayFilesText";
        replayFilesText.transform.SetParent(meshes.transform);
        replayFilesText.transform.localPosition = new Vector3(0.0003f, 0.00001f, 0.00275f);
        replayFilesText.transform.localRotation = Quaternion.Euler(90, 0, 0);

        var controls = Calls.GameObjects.Gym.Scene.GymProduction.DressingRoom.ControlPanel
            .GetGameObject().transform.GetChild(0).GetChild(0);

        meshes.GetComponent<MeshRenderer>().material = new Material(
            controls.GetChild(0).GetComponent<MeshRenderer>().material
        );

        nextPageButton = Calls.Create.NewButton(() =>
        {
            currentPage = (currentPage + 1) % 4;
            ShowPage(currentPage);
        });
        nextPageButton.transform.SetParent(slabMesh.transform);
        nextPageButton.transform.localRotation = Quaternion.Euler(0, 90, 90);
        nextPageButton.transform.localPosition = new Vector3(-0.3095f, -0.1884f, 0.1305f);
        nextPageButton.name = "NextPageButton";

        previousPageButton = Calls.Create.NewButton(() =>
        {
            currentPage = (currentPage - 1 + 4) % 4;
            ShowPage(currentPage);
        });
        previousPageButton.transform.SetParent(slabMesh.transform);
        previousPageButton.transform.localRotation = Quaternion.Euler(0, 270, 270);
        previousPageButton.transform.localPosition = new Vector3(0.0935f, -0.1884f, 0.1305f);
        previousPageButton.name = "PreviousPageButton";

        for (int i = 0; i < meshes.transform.childCount; i++)
        {
            GameObject buttonFrame = meshes.transform.GetChild(i).gameObject;
            if (buttonFrame.GetComponent<RectTransform>())
                continue;

            buttonFrame.GetComponent<MeshRenderer>().material = new Material(
                controls.GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material
            );

            buttonFrame.transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = new Material(
                controls.GetChild(1).GetChild(0).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material
            );
            
            Transform interactionButton = buttonFrame.transform.GetChild(0);

            var button = interactionButton.gameObject.AddComponent<InteractionButton>();
            
            var filter = interactionButton.GetChild(0).GetComponent<MeshFilter>();
            var collider = interactionButton.gameObject.AddComponent<BoxCollider>();
            collider.center = filter.sharedMesh.bounds.center;
            collider.size = filter.sharedMesh.bounds.size;
            
            GameObject fileNameTxt = Calls.Create.NewText(
                "FileName", 1f, Color.yellow, Vector3.zero, Quaternion.identity
            );
            fileNameTxt.transform.SetParent(buttonFrame.transform);
            fileNameTxt.name = "FileName";
            fileNameTxt.transform.localRotation = Quaternion.Euler(90, 0, 0);
            fileNameTxt.transform.localPosition = new Vector3(-0.00013f, -0.00103f, 0.0038f);
            fileNameTxt.transform.localScale = Vector3.one * 0.0025f;
            fileNameTxt.GetComponent<TextMeshPro>().enableWordWrapping = false;
            fileNameTxt.GetComponent<TextMeshPro>().overflowMode =  TextOverflowModes.Overflow;
            fileNameTxt.GetComponent<TextMeshPro>().enableWordWrapping = false;
            
            fileNameTexts.Add(fileNameTxt.GetComponent<TextMeshPro>());
        }

        GameObject replayText = Calls.Create.NewText(
            "Replays", 2f, new Color(0.102f, 0.0667f, 0.051f, 1),
            new Vector3(-0.0982f, 0.7627f, 0.1681f), Quaternion.identity
        );
        replayText.name = "ReplaysText";
        replayText.transform.SetParent(slabMesh.transform);
        replayText.transform.localRotation = Quaternion.Euler(0, 180, 0);
        replayText.transform.localPosition = new Vector3(-0.2619f, 0.7336f, 0.1516f);
        
        GameObject nextPageText = Calls.Create.NewText("Next Page", 0.7f, new Color(0.102f, 0.0667f, 0.051f, 1), Vector3.zero, Quaternion.identity);
        nextPageText.transform.SetParent(nextPageButton.transform);
        nextPageText.transform.localRotation = Quaternion.Euler(90, 90, 0);
        nextPageText.transform.localPosition = new Vector3(-0.1065f, 0.0204f, -0.2475f);
        nextPageText.name = "NextPageText";

        GameObject previousPageText = Calls.Create.NewText("Previous Page", 0.7f, new Color(0.102f, 0.0667f, 0.051f, 1), Vector3.zero, Quaternion.identity);
        previousPageText.transform.SetParent(previousPageButton.transform);
        previousPageText.transform.localRotation = Quaternion.Euler(90, 270, 0);
        previousPageText.transform.localPosition = new Vector3(0.1021f, 0.0204f, 0.1656f);
        previousPageText.name = "PreviousPageText";

        var infoSlab = slab.GetChild(0).GetChild(0).GetChild(0);
        GameObject.Destroy(infoSlab.GetComponent<DisposableObject>());
        GameObject.Destroy(infoSlab.GetComponent<CollisionHandler>());
        
        GameObject disposableGO = infoSlab.GetChild(2).gameObject;
        var disableOnHit = disposableGO.AddComponent<Components.DisableOnHit>();
        disableOnHit.destroyVFX = infoSlab.GetChild(4).GetComponent<VisualEffect>();
        disableOnHit.disableObject = infoSlab.GetChild(0).gameObject;

        IsBuilt = true;
    }

    public static void ShowPage(int page)
    {
        if (!paginatedFiles.TryGetValue(page, out var files))
            return;

        for (int i = 0; i < fileNameTexts.Count; i++)
        {
            if (i >= files.Length)
            {
                fileNameTexts[i].transform.parent.gameObject.SetActive(false);
                continue;
            }
            
            using var stream = new FileStream(files[i], FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);
            var header = ReplayFile.GetHeaderFromFile(files[i], stream, reader);

            string display = string.Empty;
            if (header.Scene is "Gym")
                display = $"Gym ({header.LocalPlayer}) | {header.Date}";
            else if (header.Scene is "Park")
                display = $"Park ({header.LocalPlayer}) | {header.Date} {header.playerCount} {(header.playerCount > 1 ? "Players" : "Player")}";
            else if (header.Scene is "Ring" or "Pit")
                display = $"{header.LocalPlayer} vs {header.opponent} on {header.Scene} | {header.Date}";
            
            fileNameTexts[i].transform.parent.gameObject.SetActive(true);
            fileNameTexts[i].text = display;
        }
    }

    public static void ShowSlab()
    {
        if (!IsBuilt) BuildSlab();

        Transform headsetTr = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(1).GetChild(0).GetChild(0);
        Vector3 flatForward = headsetTr.forward;
        flatForward.y = 0f;
        flatForward.Normalize();

        Vector3 spawnPos = headsetTr.position + flatForward * Distance;
        spawnPos.y -= HeightOffset;

        spawnTransform.position = spawnPos;
        
        Vector3 direction = (spawnTransform.position - headsetTr.position).normalized;
        float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        spawnTransform.rotation = Quaternion.Euler(0f, angle, 0f);

        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays");
        int itemsPerPage = 4;
        string[] replayFiles = Directory.GetFiles(directory, "*.replay");
        int totalPages = (int)Math.Ceiling(replayFiles.Length / (float)itemsPerPage);

        for (int page = 0; page < 4; page++)
        {
            paginatedFiles[page] = replayFiles
                .Skip(page * itemsPerPage)
                .Take(itemsPerPage)
                .ToArray();
        }

        currentPage = 0;
        ShowPage(currentPage);
        
        slabPrefab.transform.GetChild(0).GetChild(0).GetChild(0).GetChild(0).GetChild(0).gameObject.SetActive(true);
        
        slabButton.RPC_OnPressed();
    }
}

public static class ReplayFile
{
    public static BinaryWriter ReplayWriter { get; set; }
    public static string FileName { get; set; }

    [Serializable]
    public class ReplayHeader
    {
        public readonly string Version = BuildInfo.Version;
        public string Scene = "Unknown";
        public string Date = DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss");

        public string LocalPlayer =
            Utilities.TrimPlayerName(Calls.Players.GetLocalPlayer().Data.GeneralData.PublicUsername);

        public string opponent;

        public int playerCount = Calls.Players.GetAllPlayers().Count;
    }

    public static string GenerateReplayFormat(int playerCount, string opponent = null)
    {
        string timestamp = DateTime.Now.ToString("MMMdd_hhmmtt").ToUpper();
        string localName = Utilities.TrimPlayerName(Calls.Players.GetLocalPlayer().Data.GeneralData.PublicUsername);
        opponent = Utilities.TrimPlayerName(opponent);

        string sceneName = Utilities.GetFriendlySceneName();
        string matchFormat = $"{localName}-vs-{opponent}_On_{sceneName}_{timestamp}.replay";

        return sceneName switch
        {
            "Ring" =>
                matchFormat,

            "Pit" =>
                matchFormat,

            "Park" =>
                $"Replay_{timestamp}_{sceneName}_{playerCount}P_{localName}.replay",

            // Gym and default are the same format
            _ =>
                $"Replay_{timestamp}_{sceneName}_{localName}.replay"
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
        if (Calls.Players.GetAllPlayers().Count > 1)
            opponentName =
                Utilities.TrimPlayerName(Calls.Players.GetEnemyPlayers().FirstOrDefault().Data.GeneralData
                    .PublicUsername);
        FileName = GenerateReplayFormat(Calls.Players.GetAllPlayers().Count, opponentName);

        string path = Path.Combine(directory, FileName);

        var file = new FileStream(path, FileMode.Create);
        ReplayWriter = new BinaryWriter(file);

        byte[] magicBytes = Encoding.ASCII.GetBytes("REPLAY");
        ReplayWriter.Write(magicBytes);

        var header = new ReplayHeader { Scene = Main.currentScene, opponent = opponentName };
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

public static class Codec
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

    public static void DecodeVisualData(byte[] data, Dictionary<string, PlayerReplayState> players)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        string masterID = br.ReadString();
        string visualData = br.ReadString();

        if (!players.TryGetValue(masterID, out var state))
            state = new PlayerReplayState(masterID, visualData);
        else
            state.Data.VisualData = visualData;

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
            state = new PlayerReplayState(masterID, "");
            players[masterID] = state;
        }

        state.Data.Frames.Add(frameData);
    }

    public static void EncodeStackEvent(BinaryWriter bw, string masterID, StackEvent stackEvent)
    {
        bw.Write(masterID);
        bw.Write(stackEvent.timestamp);
        bw.Write(stackEvent.stack);

        MelonLogger.Msg($"[Read] StackEvent: time={stackEvent.timestamp}, name='{stackEvent.stack}");
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
            state = new PlayerReplayState(masterID, "");
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

    public static void DecodeStructureFrame(byte[] data, List<List<StructureFrame>> frames)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        int index = br.ReadInt32();
        float timestamp = br.ReadSingle();

        var frame = new StructureFrame
        {
            timestamp = timestamp,
            position = ReadVec3(br),
            rotation = ReadQuat(br),
        };

        while (frames.Count <= index)
            frames.Add(new List<StructureFrame>());

        frames[index].Add(frame);
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

public class Debouncer
{
    private bool lastValue = false;

    public bool JustPressed(bool current)
    {
        bool result = current && !lastValue;
        lastValue = current;
        return result;
    }
}

public static class CloneBuilder
{
    public class CloneInfo
    {
        public GameObject RootObject;
        public GameObject VRRig;
        public GameObject LeftHand;
        public GameObject RightHand;
        public GameObject Head;

        public GameObject BodyDouble;
        public GameObject dVRRig;
        public GameObject DLeftHand;
        public GameObject DRightHand;
        public GameObject DHead;

        public GameObject PhysicsRightHand;
        public GameObject PhysicsLeftHand;
        public GameObject PhysicsHead;

        public PlayerStackProcessor StackProcessor;
        public PlayerController Controller;
    }

    public static CloneInfo BuildClone(Vector3 initialPosition, string visualDataString, string masterID)
    {
        // PlayerVisualData visualData = PlayerVisualData.FromPlayfabDataString(visualDataString);

        Player localPlayer = Calls.Players.GetLocalPlayer();

        GameObject clone = Object.Instantiate(Calls.Managers.GetPlayerManager().PlayerControllerPrefab.gameObject);
        PlayerController cloneController = clone.GetComponent<PlayerController>();

        clone.transform.position = initialPosition;

        Transform vr = clone.transform.GetChild(1);
        GameObject rHand = vr.GetChild(1).gameObject;
        GameObject lHand = vr.GetChild(2).gameObject;
        GameObject head = vr.GetChild(0).GetChild(0).gameObject;

        clone.transform.GetChild(9).gameObject.SetActive(false); // LIV

        clone.AddComponent<ReplayClone>();

        var vrik = clone.GetComponentInChildren<VRIK>();
        if (vrik != null)
            vrik.enabled = true;

        var psp = clone.GetComponent<PlayerStackProcessor>();
        psp.Initialize(cloneController);
        psp.activeStackHandles = localPlayer.Controller.GetComponent<PlayerStackProcessor>().activeStackHandles;

        PlayerMeasurement playerMeasurement = Calls.Players.GetLocalPlayer().Data.PlayerMeasurement;
        cloneController.assignedPlayer.Data = localPlayer.Data;
        cloneController.assignedPlayer.Data.SetMeasurement(playerMeasurement, true);
        cloneController.assignedPlayer.Data.visualData = localPlayer.Data.visualData;
        cloneController.Initialize(cloneController.AssignedPlayer);

        foreach (var driver in clone.GetComponentsInChildren<TrackedPoseDriver>())
            driver.enabled = false;

        head.GetComponent<Camera>().enabled = false;
        head.GetComponent<AudioListener>().enabled = false;

        clone.GetComponent<PlayerPoseSystem>().currentInputPoses.Clear();
        MelonLogger.Msg("Clone poses cleared");

        GameObject bodyDouble = Object.Instantiate(localPlayer.Controller.gameObject);
        bodyDouble.name = "BodyDouble";

        GameObject dRHand = bodyDouble.transform.GetChild(1).GetChild(1).gameObject;
        GameObject dLHand = bodyDouble.transform.GetChild(1).GetChild(2).gameObject;
        GameObject dHead = bodyDouble.transform.GetChild(1).GetChild(0).GetChild(0).gameObject;
        GameObject dOverall = bodyDouble.transform.GetChild(1).gameObject;

        bodyDouble.transform.GetChild(4).gameObject.SetActive(false);

        foreach (var driver in bodyDouble.GetComponentsInChildren<TrackedPoseDriver>())
            driver.enabled = false;

        bodyDouble.transform.GetChild(9).gameObject.SetActive(false); // LIV

        GameObject health = GameObject.Find("Health");
        if (health != null)
            health.transform.SetParent(clone.transform);

        clone.transform.GetChild(1).gameObject.SetActive(false);
        clone.transform.GetChild(8).gameObject.SetActive(true);

        GameObject clonePhysics = clone.transform.GetChild(4).gameObject;
        GameObject physicsLHand = clonePhysics.transform.GetChild(2).gameObject;
        GameObject physicsRHand = clonePhysics.transform.GetChild(2).gameObject;

        physicsLHand.SetActive(false);
        physicsRHand.SetActive(false);
        clonePhysics.SetActive(false);

        clone.transform.GetChild(5).gameObject.SetActive(false); // Hitboxes
        clone.GetComponent<PlayerMovement>().enabled = false;

        MelonCoroutines.Start(VisualReskin(bodyDouble.transform.GetChild(0).GetChild(0).gameObject
            .GetComponent<SkinnedMeshRenderer>()));

        return new CloneInfo
        {
            RootObject = clone,
            VRRig = vr.gameObject,
            LeftHand = lHand,
            RightHand = rHand,
            Head = head,
            Controller = cloneController,
            StackProcessor = psp,

            BodyDouble = bodyDouble,
            dVRRig = dOverall,
            DLeftHand = dLHand,
            DRightHand = dRHand,
            DHead = dHead,

            PhysicsLeftHand = physicsLHand,
            PhysicsRightHand = physicsRHand,
            PhysicsHead = physicsRHand
        };
    }

    private static IEnumerator VisualReskin(SkinnedMeshRenderer renderer)
    {
        yield return new WaitForSeconds(0.1f);

        renderer.sharedMesh = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(0).GetChild(0)
            .GetComponent<SkinnedMeshRenderer>().sharedMesh;
        renderer.material = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(0)
            .GetComponent<PlayerVisuals>().NonHeadClippedMaterial;
        renderer.updateWhenOffscreen = true;

        MelonLogger.Msg("Rebinding SkinnedMeshRenderer with delayed fix");
    }
}