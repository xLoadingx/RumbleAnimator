using System.Collections;
using System.Reflection;
using Il2Cpp;
using Il2CppExitGames.Client.Photon;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppRUMBLE.Managers;
using MelonLoader;
using UnityEngine;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Networking;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using MelonLoader.Utils;
using RumbleModdingAPI;
using RumbleModUI;
using Input = UnityEngine.Input;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;
using RumbleAnimator.Recording;
using RumbleAnimator.Utils;
using UnityEngine.Events;
using UnityEngine.VFX;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;

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
        public static Dictionary<GameObject, UnityAction> destructionHooks = new();
        public static bool initializedStructureTracking = false;

        private HashSet<string> recordedVisuals = new();

        public static List<byte> _writeBuffer = new();

        public static float recordingStartTime;
        public static float playbackStartTime;

        public static int currentRecordingFrame = 0;
        public static int currentPlaybackFrame = 0;

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
            MelonCoroutines.Start(WaitForPhoton());

            ModAssembly = MelonAssembly.Assembly;
        }
        
        private IEnumerator WaitForPhoton() 
        {
            while (PhotonNetwork.NetworkingClient is null)
                yield return null;
            
            MelonLogger.Msg($"[RumbleAnimator] PhotonNetwork is ready. Hooking EventReceived.");
            PhotonNetwork.NetworkingClient.EventReceived += (Action<EventData>)OnEvent;
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
                Hashtable props = new()
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
                MelonLogger.Msg($"Scene: {Utilities.GetFriendlySceneName()}");
                if (Calls.Players.GetEnemyPlayers().FirstOrDefault() is not null)
                    MelonLogger.Msg($"Opponent: {Utilities.TrimPlayerName(Calls.Players.GetEnemyPlayers().FirstOrDefault().Data.GeneralData.PublicUsername)}");
                MelonLogger.Msg($"Time: {DateTime.UtcNow:yyyy/MM/dd HH:mm:ss}");
                MelonLogger.Msg($"Player Count: {Calls.Players.GetAllPlayers().Count}");

                onRecordingStarted?.Invoke(this, null);

                MelonCoroutines.Start(Utilities.PlaySound("Slab_Textchange.wav"));

                if (SlabBuilder.IsShown)
                    SlabBuilder.mainSlabDestroy.Disable();

                players.Clear();
                structures.Clear();
                structureDataList.Clear();
                recordedVisuals.Clear();
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

                _ = ReplayFile.CompressAndSaveAsync(ReplayFile.FileName);
                isRecording = false;
            }
        }

        public override void OnUpdate()
        {
            // TODO:
            // Make Vfx frame and make it parent to the structure, if it had one (maybe)
            // Make sound work.
            
            if (currentScene is "Loader")
                return;

            if (Input.GetKeyDown(KeyCode.F))
                CloneBuilder.BuildClone(Vector3.zero,
                    "0:e1,e35,e49,e97,e73,e114,e135,e148,e166,e197,e197,e187,e187,e187,e66,e66,e251,e66,e66:l1,d4,a2,a3,f0,f3,g2,b0,b1,i93,i77,i74,i71,i69,i64,i90:i98,i113,i98,i101,i102,i107,i108:i15,i19,i19,i19,i19,i55,i55,i55,i55,i55:9,0,0,0,0,1,1,0,0,0:i34,m44,m6",
                    Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId,
                    Calls.Players.GetLocalPlayer().Data.GeneralData.BattlePoints,
                    Calls.Players.GetLocalPlayer().Data.PlayerMeasurement);

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

        public static void OnEvent(EventData eventData)
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

                foreach (var existing in GameObject.FindObjectsOfType<Structure>())
                {
                    if (!existing.isInPool)
                        existing.Kill(playSFX: false, playVFX: false);
                }
                structures.Clear();

                bool hasStructureData = structureDataList.Any(s => s.type != null);

                if (!hasStructureData)
                {
                    MelonLogger.Error($"[RumbleAnimator] File '{path}' does not have any structure data.");
                    return;
                }

                for (int i = 0; i < structureDataList.Count; i++)
                {
                    var replayData = structureDataList[i];

                    if (string.IsNullOrEmpty(replayData.type))
                    {
                        MelonLogger.Warning($"[Replay] Missing type for structure index {i}, skipping instantiation.");
                        continue;
                    }

                    var pool = PoolManager.Instance.availablePools.FirstOrDefault(name =>
                        name.poolItem.resourceName == replayData.type);

                    if (pool is null)
                    {
                        MelonLogger.Warning($"[Replay] Could not find pool for type '{replayData.type}'");
                        continue;
                    }
                    
                    PooledMonoBehaviour structure = pool.FetchFromPool(new Vector3(0, 1, 0), Quaternion.identity);
                    GameObject.Destroy(structure.GetComponent<NetworkGameObject>());
                    structure.GetComponent<Structure>().OnFetchFromPool();
                    structure.GetComponent<Structure>().indistructable = true;
                    structure.GetComponent<Structure>().isInPool = false;
                    structure.GetComponent<Rigidbody>().isKinematic = true;
                    structure.gameObject.transform.position = new Vector3(0, -1f, 0);

                    structures.Add(structure.gameObject);
                }
                
                MelonLogger.Msg($"StructureDataList contains {structureDataList.Count} structures");

                MelonLogger.Msg($"[Replay] Playing file {Path.GetFileNameWithoutExtension(path)}");
                
                playbackStartTime = Time.time;

                onPlaybackStarted?.Invoke(path);
                isPlaying = true;
            }
            
            // Private park
            if (currentScene is "Park" && !PhotonNetwork.CurrentRoom.IsVisible && header.Scene is not "Park")
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
                
                // if (header.Scene != "Custom")
                // {
                //     MelonCoroutines.Start(SceneOverlayManager.LoadSceneForReplay(
                //         header.Scene,
                //         new Vector3(0, 2, 0),
                //         StartPlayback
                //     ));
                //     return;
                // }
                //
                // if (header.Meta.TryGetValue("customMapName", out var mapName))
                // {
                //     SceneOverlayManager.LoadCustomMap(mapName.ToString(), new Vector3(0, 2, 0));
                //     StartPlayback();
                //     return;
                // }
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
                        state = new PlayerReplayState(masterID, player.Data.VisualData.ToPlayfabDataString(), player.Data.GeneralData.BattlePoints, player.Data.PlayerMeasurement);
                        players[masterID] = state;
                    }

                    string visual = player.Data.VisualData.ToPlayfabDataString();
                    if (!recordedVisuals.Contains(masterID) && !string.IsNullOrEmpty(visual))
                    {
                        recordedVisuals.Add(masterID);

                        using var ms = new MemoryStream();
                        using var bw = new BinaryWriter(ms);
                        bw.Write(masterID);
                        bw.Write(visual);
                        bw.Write(state.Data.BattlePoints);
                        bw.Write(state.Data.Measurement.Length);
                        bw.Write(state.Data.Measurement.ArmSpan);
                        ReplayFile.WriteFramedData(ms.ToArray(), currentRecordingFrame, FrameType.PlayerData);
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

                        var pooled = go.GetComponent<PooledMonoBehaviour>();
                        var replayData = new StructureReplayData
                        {
                            type = pooled.resourceName,
                            existInScene = true
                        };

                        structures.Add(go);

                        while (structureDataList.Count <= structures.IndexOf(go))
                            structureDataList.Add(null);

                        structureDataList[structures.IndexOf(go)] = replayData;

                        var data = Codec.EncodeStructureData(pooled.resourceName, true, structures.IndexOf(go));
                        ReplayFile.WriteFramedData(data, currentRecordingFrame, FrameType.StructureData);

                        MelonLogger.Msg($"[Replay] Registered pre-existing structure: {go.name} with type {pooled.resourceName}");
                    }

                    initializedStructureTracking = true;
                }

                for (int i = 0; i < structures.Count; i++)
                {
                    var structure = structures[i];

                    if (structure == null ||
                        !structure.TryGetComponent(out Structure component) ||
                        structure.name.StartsWith("Static Target") ||
                        structure.name.StartsWith("Moving Target"))
                        continue;
                    
                    while (structureDataList.Count <= i)
                    {
                        var pooled = structure.GetComponent<PooledMonoBehaviour>();
                        var type = pooled.resourceName;

                        structureDataList.Add(new StructureReplayData
                        {
                            type = type
                        });

                        var typeData = Codec.EncodeStructureData(type, false, i);
                        ReplayFile.WriteFramedData(typeData, currentRecordingFrame, FrameType.StructureData);
                    }

                    var replayData = structureDataList[i];
                    var frames = replayData.frames ??= new List<StructureFrame>();
                    
                    if (frames.Count > 0)
                    {
                        var prevFrame = frames[^1];

                        if (!component.isInPool && !replayData.isActive)
                        {
                            replayData.isActive = true;
                            MelonLogger.Msg($"Structure {replayData.type} new lifetime at frame {currentRecordingFrame}");
                        }

                        if (component.isInPool && replayData.isActive)
                        {
                            replayData.isActive = false;
                            
                            MelonLogger.Msg($"Structure {replayData.type} destroyed at frame {currentRecordingFrame}");
                            
                            var destroyedTimestamp = Time.time - recordingStartTime;
                            var hiddenFrame = new StructureFrame
                            {
                                timestamp = destroyedTimestamp,
                                position = new SVector3(new Vector3(0, -100f, 0)),
                                rotation = new SQuaternion(Quaternion.identity)
                            };
                            frames.Add(hiddenFrame);
                            
                            var hiddenData = Codec.EncodeStructureFrame(hiddenFrame, i, destroyedTimestamp);
                            ReplayFile.WriteFramedData(hiddenData, currentRecordingFrame, FrameType.StructureUpdate);
                            continue;
                        }
                        
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

                currentRecordingFrame++;
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

                    if (state.Clone is null)
                    {
                        Utilities.TeleportPlayer(Calls.Players.GetLocalPlayer(), Vector3.zero);
                        state.Clone = CloneBuilder.BuildClone(
                            state.Data.Frames[0].positions.controllerPos.ToVector3(), state.Data.VisualData, masterID, state.Data.BattlePoints, state.Data.Measurement);
                    }
                    

                    var currentClone = state.Clone;
                    int currentPlayerFrame = state.CurrentPlayerFrameIndex;
                    int currentStackFrame = state.CurrentStackFrameIndex;

                    if (currentPlayerFrame >= state.Data.Frames.Count)
                    {
                        StopPlaybackFor(masterID);

                        if (players.Count is 0)
                        {
                            isPlaying = false;
                            isRecording = false;

                            foreach (var structure in structures)
                                GameObject.Destroy(structure);

                            structures.Clear();
                            structureDataList.Clear();
                            players.Clear();
                            destructionHooks.Clear();
                            initializedStructureTracking = false;
                            playbackStartTime = 0f;
                            currentRecordingFrame = 0;
                            currentPlaybackFrame = 0;
                            
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
                                if (stackFrame.stack.Contains("Spawn") || stackFrame.stack is "Disc")
                                    break;
                                
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
                    if (i >= structureDataList.Count)
                    {
                        MelonLogger.Warning($"[Replay] structureDataList missing index {i}");
                        continue;
                    }
                
                    var structure = structures[i];
                    if (structure == null)
                    {
                        MelonLogger.Warning($"[Replay] structure[{i}] is null");
                        continue;
                    }
                
                    var replayData = structureDataList[i];
                    if (replayData == null || replayData.frames == null || replayData.frames.Count == 0)
                    {
                        MelonLogger.Warning($"[Replay] replayData[{i}] is null or has no frames");
                        continue;
                    }

                    var rigid = structure.GetComponent<Rigidbody>();
                    if (rigid is not null) 
                    {
                        if (!rigid.isKinematic)
                            rigid.isKinematic = true;
                    }
                
                    var frames = replayData.frames;
                    StructureFrame? frame = frames.LastOrDefault(f => f.timestamp <= time);
                
                    if (frame.HasValue)
                    {
                        if (!structure.activeSelf && frame.Value.position.y > -99f)
                            structure.SetActive(true);

                        if (replayData.CurrentFrameIndex + 1 < frames.Count)
                        {
                            var nextFrame = frames[replayData.CurrentFrameIndex + 1];
                            if (nextFrame.position.y < -99f)
                            {
                                structure.GetComponent<Structure>().Kill();
                                structure.GetComponent<Structure>().OnFetchFromPool();
                                structure.GetComponent<Structure>().isInPool = false;
                            }
                                
                        }
                
                        var tr = structure.transform;
                        tr.SetPositionAndRotation(
                            frame.Value.position.ToVector3(),
                            frame.Value.rotation.ToQuaternion()
                        );

                        replayData.CurrentFrameIndex++;
                    }
                }

                currentPlaybackFrame++;
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

    public class PlayerReplayState
    {
        public PlayerReplayData Data;
        public CloneBuilder.CloneInfo Clone;
        public int CurrentPlayerFrameIndex = 0;
        public int CurrentStackFrameIndex = 0;

        public PlayerReplayState(string masterID, string visualData, int battlePoints, PlayerMeasurement measurement)
        {
            Data = new PlayerReplayData(masterID, visualData, battlePoints, measurement);
        }
    }

    [Serializable]
    public class PlayerReplayData
    {
        public string MasterID;
        public List<FrameData> Frames = new();
        public List<StackEvent> StackEvents = new();
        public string VisualData;
        public int BattlePoints;
        public PlayerMeasurement Measurement;

        public PlayerReplayData(string MasterID, string VisualData, int battlePoints, PlayerMeasurement measurement)
        {
            this.MasterID = MasterID;
            this.VisualData = VisualData;
            this.BattlePoints = battlePoints;
            this.Measurement = measurement;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class ReplayClone : MonoBehaviour
    {
    }
}