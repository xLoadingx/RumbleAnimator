using System.Reflection;
using Il2CppExitGames.Client.Photon;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using MelonLoader;
using UnityEngine;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppSystem.Collections;
using MelonLoader.Utils;
using RumbleModdingAPI;
using RumbleModUI;
using Input = UnityEngine.Input;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;
using RumbleAnimator.Recording;
using RumbleAnimator.Utils;
using UnityEngine.Events;

[assembly:
    MelonInfo(typeof(RumbleAnimator.Main), RumbleAnimator.BuildInfo.Name, RumbleAnimator.BuildInfo.Version,
        RumbleAnimator.BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace RumbleAnimator
{
    // If you're reading this, welcome to binary hek.
    // Good luck, have fun. I sure as heck didn't - ERROR

    // Thanks to Blank and SaveForth for their binary wizardry and
    // clonebending techniques, respectively.

    public static class BuildInfo
    {
        public const string Name = "RumbleAnimator";
        public const string Author = "ERROR";
        public const string Version = "1.0.0";
    }

    public class Main : MelonMod
    {
        public static string currentScene = "Loader";

        public static bool isRecording = false;
        public static bool isPlaying = false;

        private static Debouncer recordDebounce = new();
        private static Debouncer playbackDebounce = new();

        public static Dictionary<string, PlayerReplayState> players = new();

        public static List<GameObject> structures = new();
        public static List<StructureReplayData> structureDataList = new();
        public Dictionary<GameObject, UnityAction> destructionHooks = new();
        public static bool initializedStructureTracking = false;

        private HashSet<string> recordedVisuals = new();

        public static List<byte> _writeBuffer = new();

        public static float recordingStartTime;
        public static float playbackStartTime;

        public static int currentRecordingFrame = 0;

        private GameObject recordingRing;

        public static event EventHandler onRecordingStarted;
        public static event EventHandler onRecordingStopped;

        public static event EventDelegates.File onPlaybackStarted;
        public static event EventHandler onPlaybackPaused;
        public static event EventHandler onPlaybackResumed;
        public static event EventHandler onPlaybackStopped;

        private Mod rumbleAnimatorMod = new();
        private ModSetting<float> SlabDistance = new();
        private ModSetting<float> HeightOffset = new();
        private ModSetting<bool> RecordingRingHand = new();
        private ModSetting<bool> TryCompressAllFiles = new();

        private ModSetting<bool> AutoRecordMatches = new();
        private ModSetting<bool> AutoRecordParks = new();

        public static Assembly ModAssembly;

        public override void OnLateInitializeMelon()
        {
            HarmonyInstance.PatchAll();
            UI.instance.UI_Initialized += OnUIInit;
            Calls.onMapInitialized += Initialize;
            PhotonNetwork.NetworkingClient.EventReceived += (Action<EventData>)OnEvent;

            ModAssembly = MelonAssembly.Assembly;
        }

        public void Initialize()
        {
            if (!SlabBuilder.IsBuilt)
                SlabBuilder.BuildSlab();

            SlabBuilder.mainSlabDestroy.Initialize();

            if (SlabBuilder.slabPrefab.activeInHierarchy)
                SlabBuilder.slabPrefab.SetActive(false);

            isRecording = false;
            isPlaying = false;

            if ((currentScene is "Park" && (bool)AutoRecordParks.SavedValue) ||
                (currentScene is "Map0" or "Map1" && (bool)AutoRecordMatches.SavedValue))
                RecordToggle();

            if (currentScene is "Park" && !(bool)AutoRecordParks.SavedValue)
            {
                Il2CppExitGames.Client.Photon.Hashtable props = new()
                {
                    ["hasReplayMod"] = true
                };

                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            }

            players?.Clear();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            currentScene = sceneName;

            if (isRecording)
            {
                try
                {
                    ReplayFile.ReplayWriter?.Write(_writeBuffer.ToArray());
                    ReplayFile.ReplayWriter?.Flush();
                    ReplayFile.ReplayWriter?.Close();
                }
                catch (Exception e)
                {
                    MelonLogger.Error($"[RumbleAnimator] Failed to write replay on scene load: {e}");
                }
                finally
                {
                    ReplayFile.ReplayWriter = null;
                    _writeBuffer.Clear();
                    isRecording = false;
                }
            }
        }

        public void RecordToggle()
        {
            if (!isRecording)
            {
                MelonLogger.Msg("[RumbleAnimator] Recording");

                onRecordingStarted?.Invoke(this, null);

                if (recordingRing == null)
                    InitializeRecordingBracelet();

                recordingRing?.SetActive(true);

                if (recordingRing != null &&
                    recordingRing.TryGetComponent(out Components.RecordingRingVisuals visuals))
                    visuals.shouldPulse = true;

                MelonCoroutines.Start(Utilities.PlaySound("Slab_Textchange.wav"));

                if (SlabBuilder.IsShown)
                    SlabBuilder.mainSlabDestroy.Disable();

                players.Clear();
                recordingStartTime = Time.time;
                ReplayFile.InitializeReplayFile();

                isRecording = true;
            }
            else
            {
                ReplayFile.ReplayWriter?.Write(_writeBuffer.ToArray());
                _writeBuffer.Clear();

                ReplayFile.ReplayWriter?.Close();
                ReplayFile.ReplayWriter = null;

                initializedStructureTracking = false;
                onRecordingStopped?.Invoke(this, null);

                MelonCoroutines.Start(Utilities.PlaySound("Slab_Dismiss.wav"));

                MelonLogger.Msg("[RumbleAnimator] Recording stopped and file saved.");

                if (recordingRing != null)
                {
                    recordingRing.GetComponent<Components.RecordingRingVisuals>().shouldPulse = false;
                    recordingRing?.SetActive(false);
                }

                _ = ReplayFile.CompressAndSaveAsync(ReplayFile.FileName);
                isRecording = false;
            }
        }

        public override void OnUpdate()
        {
            if (currentScene is "Loader")
                return;

            bool joystickR = Calls.ControllerMap.RightController.GetJoystickClick() is 1f;
            bool joystickL = Calls.ControllerMap.LeftController.GetJoystickClick() is 1f;
            bool clonebendingInstalled =
                Calls.Mods.findOwnMod("CloneBending", "ULVAK MAKE THE VERSION STRING OPTIONAL", false);

            bool canRecord =
                (recordDebounce.JustPressed(joystickR) || Input.GetKeyDown(KeyCode.R)) &&
                !(clonebendingInstalled && currentScene is "Gym");

            if (canRecord)
                RecordToggle();

            if (currentScene is "Map0" or "Map1" && Calls.Players.GetAllPlayers().Count > 1)
                return;

            bool canPlayback =
                (playbackDebounce.JustPressed(joystickL) || Input.GetKeyDown(KeyCode.L)) &&
                !isRecording;

            if (canPlayback)
                SlabBuilder.ShowSlab();
        }

        public void OnEvent(EventData eventData)
        {
            if (eventData.Code is not 59) return;

            if (eventData.CustomData is Il2CppSystem.String str)
            {
                string[] split = str.ToString().Split(new[] { "::" }, 2, StringSplitOptions.None);
                if (split.Length == 2 && split[0] == "StartReplay")
                {
                    string replayName = split[1];

                    string fullPath = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays", replayName);
                    if (File.Exists(fullPath))
                        PlayReplayFile(fullPath);
                    else
                        MelonLogger.Warning($"[Replay] Could not find replay file '{replayName}' to play.");
                }
            }
        }

        public static void PlayReplayFile(string path)
        {
            if (!File.Exists(path) || isPlaying || isRecording) return;
            
            var (loadedPlayerData, loadedStructureFrames, header) = ReplayFile.GetReplayFromFile(path);
            if (loadedPlayerData is null || loadedStructureFrames is null) return;

            void StartPlayback()
            {
                players = loadedPlayerData;
                structureDataList = loadedStructureFrames;

                MelonLogger.Msg($"[Replay] Playing file {Path.GetFileNameWithoutExtension(path)}");
                playbackStartTime = Time.time;

                onPlaybackStarted.Invoke(path);
                isPlaying = true;
            }
            
            // Private park
            if (currentScene is "Park" && !PhotonNetwork.CurrentRoom.IsVisible)
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    foreach (var kvp in PhotonNetwork.CurrentRoom.Players)
                    {
                        var player = kvp.Value;

                        if (!player.CustomProperties.TryGetValue("hasReplayMod", out var val) || !val.Unbox<bool>())
                        {
                            MelonLogger.Warning($"Could not start replay. Player {player.NickName} does not have the replay mod.");
                            return;
                        }
                    }

                    if (Calls.Players.GetAllPlayers().Count > 1)
                    {
                        string data = "StartReplay::" + Path.GetFileName(path);
                    
                        PhotonNetwork.RaiseEvent(
                            59,
                            data,
                            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                            SendOptions.SendReliable
                        );
                    }
                }
                
                if (header.Scene != "Custom")
                {
                    MelonCoroutines.Start(SceneOverlayManager.LoadSceneForReplay(
                        header.Scene,
                        new Vector3(0, 2, 0),
                        StartPlayback
                    ));
                    return;
                }
                
                if (header.Meta.TryGetValue("customMapName", out var mapName))
                {
                    SceneOverlayManager.LoadCustomMap(mapName.ToString(), new Vector3(0, 2, 0));
                    StartPlayback();
                    return;
                }
            }
            
            StartPlayback();
        }

        public override void OnFixedUpdate()
        {
            void SetPositionsAndRotations(FramePoseData pos, FrameRotationData rot, CloneBuilder.CloneInfo clonePlayer)
            {
                clonePlayer.LeftHand.transform.localPosition = pos.lHandPos.ToVector3();
                clonePlayer.LeftHand.transform.localRotation = rot.lHandRot.ToQuaternion();
                clonePlayer.DLeftHand.transform.localPosition = pos.rHandPos.ToVector3();
                clonePlayer.DLeftHand.transform.localRotation = rot.rHandRot.ToQuaternion();

                clonePlayer.RightHand.transform.localPosition = pos.rHandPos.ToVector3();
                clonePlayer.RightHand.transform.localRotation = rot.rHandRot.ToQuaternion();
                clonePlayer.DRightHand.transform.localPosition = pos.lHandPos.ToVector3();
                clonePlayer.DRightHand.transform.localRotation = rot.lHandRot.ToQuaternion();

                clonePlayer.Head.transform.localPosition = pos.headPos.ToVector3();
                clonePlayer.Head.transform.localRotation = rot.headRot.ToQuaternion();
                clonePlayer.DHead.transform.localPosition = pos.headPos.ToVector3();
                clonePlayer.DHead.transform.localRotation = rot.headRot.ToQuaternion();

                clonePlayer.VRRig.transform.position = pos.vrPos.ToVector3();
                clonePlayer.VRRig.transform.rotation = rot.vrRot.ToQuaternion();
                clonePlayer.dVRRig.transform.position = pos.vrPos.ToVector3();
                clonePlayer.dVRRig.transform.rotation = rot.vrRot.ToQuaternion();

                clonePlayer.RootObject.transform.position = pos.controllerPos.ToVector3();
                clonePlayer.RootObject.transform.rotation = rot.controllerRot.ToQuaternion();
                clonePlayer.BodyDouble.transform.position = pos.controllerPos.ToVector3();
                clonePlayer.BodyDouble.transform.rotation = rot.controllerRot.ToQuaternion();

                clonePlayer.BodyDouble.transform.position = new Vector3(
                    pos.controllerPos.x,
                    pos.visualsY,
                    pos.controllerPos.z
                );
            }

            if (isRecording)
            {
                // Player recording
                foreach (var player in Calls.Players.GetAllPlayers())
                {
                    var masterID = player.Data.GeneralData.PlayFabMasterId;

                    if (!players.TryGetValue(masterID, out var state))
                    {
                        state = new PlayerReplayState(masterID, player.Data.VisualData.ToPlayfabDataString());
                        players[masterID] = state;
                    }

                    if (recordedVisuals.Add(masterID))
                    {
                        using var ms = new MemoryStream();
                        using var bw = new BinaryWriter(ms);
                        bw.Write(masterID);
                        bw.Write(state.Data.VisualData);
                        ReplayFile.WriteFramedData(ms.ToArray(), currentRecordingFrame, FrameType.VisualData);
                    }

                    Transform playerController = player.Controller.transform;
                    Transform VR = playerController.GetChild(1);
                    Transform Visuals = playerController.GetChild(0);
                    Transform rController = VR.transform.GetChild(2);
                    Transform lController = VR.transform.GetChild(1);
                    Transform Headset = VR.transform.GetChild(0).GetChild(0);

                    var poseData = new FramePoseData
                    {
                        lHandPos = lController.localPosition.ToSerializable(),
                        rHandPos = rController.localPosition.ToSerializable(),
                        headPos = Headset.localPosition.ToSerializable(),
                        visualsY = Visuals.position.y,
                        vrPos = VR.position.ToSerializable(),
                        controllerPos = playerController.position.ToSerializable(),
                    };

                    var rotationData = new FrameRotationData
                    {
                        lHandRot = lController.localRotation.ToSerializable(),
                        rHandRot = rController.localRotation.ToSerializable(),
                        headRot = Headset.localRotation.ToSerializable(),
                        vrRot = VR.transform.rotation.ToSerializable(),
                        controllerRot = playerController.rotation.ToSerializable()
                    };

                    var frameData = new FrameData
                    {
                        positions = poseData,
                        rotations = rotationData
                    };

                    var timestamp = Time.time - recordingStartTime;
                    var data = Codec.EncodePlayerFrame(masterID, frameData, timestamp);
                    ReplayFile.WriteFramedData(data, currentRecordingFrame, FrameType.PlayerUpdate);
                }

                // Structure recording
                if (!initializedStructureTracking)
                {
                    foreach (var structure in GameObject.FindObjectsOfType<Structure>())
                    {
                        var go = structure.gameObject;

                        if (go.GetComponent<PooledMonoBehaviour>().isInPool || structures.Contains(go))
                            continue;

                        structures.Add(go);

                        while (structureDataList.Count <= structures.IndexOf(go))
                            structureDataList.Add(new StructureReplayData());

                        MelonLogger.Msg($"[Replay] Registered pre-existing structure: {go.name}");
                    }

                    initializedStructureTracking = true;
                }

                for (int i = 0; i < structures.Count; i++)
                {
                    var structure = structures[i];

                    if (structure == null ||
                        !structure.TryGetComponent(out Structure component) ||
                        structure.name.StartsWith("Static Target") ||
                        structure.name.StartsWith("Moving Target") ||
                        component.isSpawning)
                        continue;

                    while (structureDataList.Count <= i)
                        structureDataList.Add(new StructureReplayData());

                    var replayData = structureDataList[i];
                    var frames = replayData.frames ??= new List<StructureFrame>();

                    if (frames.Count > 0)
                    {
                        var prevFrame = frames[^1];
                        bool changed = Utilities.HasTransformChanged(
                            structure.transform.position, structure.transform.rotation,
                            prevFrame.position.ToVector3(), prevFrame.rotation.ToQuaternion());

                        if (!changed)
                            continue;
                    }

                    var timestamp = Time.time - recordingStartTime;
                    var newFrame = new StructureFrame
                    {
                        timestamp = timestamp,
                        position = new SVector3(structure.transform.position),
                        rotation = new SQuaternion(structure.transform.rotation)
                    };

                    frames.Add(newFrame);

                    var data = Codec.EncodeStructureFrame(newFrame, i, timestamp);
                    ReplayFile.WriteFramedData(data, currentRecordingFrame, FrameType.StructureUpdate);
                }

                if (_writeBuffer.Count >= 1024)
                {
                    ReplayFile.ReplayWriter.Write(_writeBuffer.ToArray());
                    _writeBuffer.Clear();
                }
            }

            if (isPlaying)
            {
                float time = Time.time - playbackStartTime;

                foreach (var player in players)
                {
                    string masterID = player.Value.Data.MasterID;

                    if (!players.TryGetValue(masterID, out var state))
                    {
                        MelonLogger.Warning($"[Replay] Tried to access missing player state for {masterID}");
                        return;
                    }

                    state.Clone ??= CloneBuilder.BuildClone(
                        state.Data.Frames[0].positions.controllerPos.ToVector3(), state.Data.VisualData, masterID);

                    var currentClone = state.Clone;
                    int currentPlayerFrame = state.CurrentPlayerFrameIndex;
                    int currentStackFrame = state.CurrentStackFrameIndex;

                    if (currentPlayerFrame >= state.Data.Frames.Count)
                    {
                        StopPlaybackFor(masterID);

                        if (players.Count is 0)
                        {
                            isPlaying = false;

                            foreach (var structure in structures)
                                StopPlaybackForStructure(structure, true);

                            structures.Clear();
                            structureDataList.Clear();
                            var playerController = Calls.Players.GetLocalPlayer().Controller.transform;
                            playerController.GetChild(0).GetComponent<PlayerVisuals>()
                                .Initialize(playerController.GetComponent<PlayerController>());

                            MelonCoroutines.Start(SceneOverlayManager.UnloadReplayScene());
                            onPlaybackStopped?.Invoke(this, null);
                        }

                        continue;
                    }

                    var playerFrame = state.Data.Frames[currentPlayerFrame];

                    if (time >= playerFrame.timestamp)
                    {
                        SetPositionsAndRotations(playerFrame.positions, playerFrame.rotations, state.Clone);

                        state.CurrentPlayerFrameIndex++;
                    }

                    if (state.CurrentStackFrameIndex < state.Data.StackEvents.Count)
                    {
                        var stackFrame = state.Data.StackEvents[currentStackFrame];

                        if (time >= stackFrame.timestamp)
                        {
                            Stack stack = null;

                            foreach (var availableStack in Calls.Players.GetLocalPlayer().Controller
                                         .GetComponent<PlayerStackProcessor>().availableStacks)
                            {
                                if (availableStack.CachedName == stackFrame.stack)
                                {
                                    stack = availableStack;
                                    break;
                                }
                            }

                            if (stack != null)
                                currentClone.StackProcessor.Execute(stack, null);

                            state.CurrentStackFrameIndex++;
                        }
                    }
                }

                for (int i = 0; i < structures.Count; i++)
                {
                    var structure = structures[i];
                    var replayData = structureDataList[i];
                    var frames = replayData.frames;

                    if (frames.Count is 0)
                        continue;

                    if (replayData.destroyedAtFrame.HasValue &&
                        replayData.CurrentFrameIndex >= replayData.destroyedAtFrame.Value &&
                        !structure.GetComponent<PooledMonoBehaviour>().IsInPool)
                    {
                        StopPlaybackForStructure(structure, true);
                        continue;
                    }

                    if (!structure.activeInHierarchy)
                    {
                        structure.SetActive(true);
                        var rb = structure.GetComponent<Rigidbody>();
                        if (rb != null)
                            rb.isKinematic = true;

                        foreach (var col in structure.GetComponentsInChildren<Collider>())
                            col.enabled = false;

                        MelonLogger.Msg($"[Replay] Spawned pre-existing structure: {structure.name}");
                    }

                    StructureFrame? frame = frames.LastOrDefault(f => f.timestamp <= time);

                    if (frame.HasValue)
                    {
                        var tr = structure?.transform;
                        if (tr is null)
                            continue;

                        structure.transform.SetPositionAndRotation(frame.Value.position.ToVector3(),
                            frame.Value.rotation.ToQuaternion());
                    }
                }
            }
        }

        private void StopPlaybackFor(string masterID)
        {
            var state = players[masterID];
            GameObject.Destroy(state.Clone.RootObject);
            GameObject.Destroy(state.Clone.BodyDouble);
            state.Clone = null;

            players.Remove(masterID);
            MelonLogger.Msg($"Replay ended for player {masterID}");
        }

        private void StopPlaybackForStructure(GameObject structure, bool killInsteadOfPool = false)
        {
            var pooled = structure.GetComponent<PooledMonoBehaviour>();
            var rigid = structure.GetComponent<Rigidbody>();

            if (killInsteadOfPool)
                structure.GetComponent<Structure>().Kill();
            else
                pooled.ReturnToPool();

            if (rigid != null)
                rigid.isKinematic = false;

            foreach (var collider in structure.GetComponentsInChildren<Collider>())
                collider.enabled = true;
        }

        private void InitializeRecordingBracelet()
        {
            if (recordingRing is null)
            {
                recordingRing = Utilities.LoadObjectFromBundle<GameObject>("bracelet", "Bracelet");
                var braceletMat = Utilities.LoadObjectFromBundle<Material>("bracelet", "RecordingMat");

                braceletMat.shader = Shader.Find("Shader Graphs/RUMBLE_Prop");
                braceletMat.SetTexture("_Albedo",
                    Utilities.LoadObjectFromBundle<Texture2D>("bracelet", "LP_Cube_AlbedoTransparency", false));
                recordingRing.GetComponent<MeshRenderer>().material = braceletMat;

                var visuals = recordingRing.AddComponent<Components.RecordingRingVisuals>();
                visuals.Initialize();
                visuals.SetPulsing(true);
            }

            recordingRing.SetActive(false);

            int hand = (bool)RecordingRingHand.SavedValue ? 1 : 2;

            recordingRing.transform.SetParent(Calls.Players.GetLocalPlayer().Controller.transform.GetChild(0)
                .GetChild(1).GetChild(0).GetChild(4).GetChild(0).GetChild(hand).GetChild(0).GetChild(0).GetChild(0)
                .GetChild(3)); // Ring finger for each hand
            recordingRing.transform.localPosition = new Vector3(0, 0.0276f, 0);
            recordingRing.transform.localScale = new Vector3(1.47f, 1.47f, 0.2916f);
            recordingRing.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        public void OnUIInit()
        {
            rumbleAnimatorMod.ModName = "RumbleAnimator";
            rumbleAnimatorMod.ModVersion = "1.0.0";
            rumbleAnimatorMod.SetFolder("MatchReplays");

            AutoRecordMatches = rumbleAnimatorMod.AddToList(
                "Automatically Record Matches",
                true,
                0,
                "Toggles if you want to automatically start recording matches when they start.",
                new Tags()
            );

            AutoRecordParks = rumbleAnimatorMod.AddToList(
                "Automatically Record Parks",
                true,
                0,
                "Toggles if you want to automatically start recording parks when you join.",
                new Tags()
            );

            SlabDistance = rumbleAnimatorMod.AddToList(
                "Settings Distance",
                0.5f,
                "The distance of the replay settings slab appears from you.",
                new Tags()
            );

            HeightOffset = rumbleAnimatorMod.AddToList(
                "Settings Height Offset",
                -0.25f,
                "The difference in height the replay settings slab is from your headset.",
                new Tags()
            );

            RecordingRingHand = rumbleAnimatorMod.AddToList(
                "Ring Hand",
                false,
                1,
                "Changes which hand the ring appears on when recording.\nFalse is the right hand, true is the left hand.",
                new Tags()
            );

            TryCompressAllFiles = rumbleAnimatorMod.AddToList(
                "Try Compress All Replays",
                false,
                2,
                "When toggled on and saved, it will try to compress all the uncompressed '.replay' files in your UserData folder.\nWARNING: It may take a while, and you cannot record while doing so.\nIt will log in your console when it has finished.",
                new Tags());

            rumbleAnimatorMod.GetFromFile();
            rumbleAnimatorMod.ModSaved += ModSaved;
            ModSaved();

            UI.instance.AddMod(rumbleAnimatorMod);
        }

        private void ModSaved()
        {
            float previousDistance = SlabBuilder.Distance;
            float previousHeightOffset = SlabBuilder.HeightOffset;

            SlabBuilder.Distance = (float)SlabDistance.SavedValue;
            SlabBuilder.HeightOffset = (float)HeightOffset.SavedValue;

            if ((bool)TryCompressAllFiles.SavedValue && !isRecording && !isPlaying)
            {
                TryCompressAllFiles.SavedValue = false;
                UI.instance.ForceRefresh();

                InitializeRecordingBracelet();

                MelonCoroutines.Start(ReplayFile.TryCompressAllReplays());
            }

            // if (currentScene is not "Loader" && recordingRing.activeInHierarchy)
            // 	ParentRecordingRing();

            if (SlabBuilder.slabPrefab is not null &&
                SlabBuilder.slabPrefab.transform.GetChild(0).GetChild(0).GetChild(0).gameObject.activeSelf &&
                (previousDistance != SlabBuilder.Distance || previousHeightOffset != SlabBuilder.HeightOffset))
                SlabBuilder.ShowSlab();
        }
    }

    public class StructureReplayData
    {
        public List<StructureFrame> frames;
        public int? destroyedAtFrame = null;
        public int CurrentFrameIndex = 0;
    }

    public class PlayerReplayState
    {
        public PlayerReplayData Data;
        public CloneBuilder.CloneInfo Clone;
        public int CurrentPlayerFrameIndex = 0;
        public int CurrentStackFrameIndex = 0;

        public PlayerReplayState(string masterID, string visualData)
        {
            Data = new PlayerReplayData(masterID, visualData);
        }
    }

    [Serializable]
    public class PlayerReplayData
    {
        public string MasterID;
        public List<FrameData> Frames = new();
        public List<StackEvent> StackEvents = new();
        public string VisualData;

        public PlayerReplayData(string MasterID, string VisualData)
        {
            this.MasterID = MasterID;
            this.VisualData = VisualData;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class ReplayClone : MonoBehaviour
    {
    }
}