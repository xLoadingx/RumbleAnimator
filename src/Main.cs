using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Networking;
using Il2CppRUMBLE.Networking.MatchFlow;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppSystem.IO;
using Il2CppSystem.Text;
using Il2CppSystem.Text.RegularExpressions;
using MelonLoader;
using MelonLoader.Utils;
using RumbleModdingAPI;
using RumbleModUI;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using PlayerState = RumbleAnimator.PlayerState;

[assembly: MelonInfo(typeof(RumbleAnimator.Main), RumbleAnimator.BuildInfo.Name, RumbleAnimator.BuildInfo.Version, RumbleAnimator.BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace RumbleAnimator
{
    public static class BuildInfo
    {
        public const string Name = "RumbleReplay";
        public const string Author = "ERROR";
        public const string Version = "1.0.0";
    }

    internal static class ReplayCache
    {
        public static Dictionary<string, StackType> NameToStackType = new (StringComparer.OrdinalIgnoreCase)
        {
            { "RockSlide", StackType.Dash },
            { "Jump", StackType.Jump },
            { "Flick", StackType.Flick },
            { "Parry", StackType.Parry },
            { "HoldLeft", StackType.HoldLeft },
            { "HoldRight", StackType.HoldRight },
            { "Stomp", StackType.Ground },
            { "Straight", StackType.Straight },
            { "Uppercut", StackType.Uppercut },
            { "Kick", StackType.Kick },
            { "Explode", StackType.Explode }
        };
        
        public static Dictionary<StructureType, Pool<PooledMonoBehaviour>> structurePools;
        public static Dictionary<string, AudioCall> structureSpawnSFX;
        
        public static void BuildCacheTables()
        {
            structurePools = new();
            structureSpawnSFX = new();

            var processor = PlayerManager.instance.localPlayer.Controller
                .GetComponent<PlayerStackProcessor>();

            // Pool Cache (Structures + VFX)
            foreach (var pool in PoolManager.instance.availablePools)
            {
                var name = pool.poolItem.resourceName;

                // Structure pools
                if (name.Contains("RockCube")) structurePools[StructureType.Cube] = pool;
                else if (name.Contains("Pillar")) structurePools[StructureType.Pillar] = pool;
                else if (name.Contains("Disc")) structurePools[StructureType.Disc] = pool;
                else if (name.Contains("Wall")) structurePools[StructureType.Wall] = pool;
                else if (name == "Ball") structurePools[StructureType.Ball] = pool;
                else if (name.Contains("LargeRock")) structurePools[StructureType.LargeRock] = pool;
                else if (name.Contains("SmallRock")) structurePools[StructureType.SmallRock] = pool;
                else if (name.Contains("BoulderBall")) structurePools[StructureType.CagedBall] = pool;
            }

            // Structure Spawn Audio Cache
            foreach (var stack in processor.availableStacks)
            {
                var processes = stack.processes;
                if (processes == null || processes.Count == 0) continue;

                var proc = processes.ToArray()[0];

                string prefabName = null;

                var grounded = proc.structureSpawnModifier?.TryCast<SpawnStructureGroundedModifier>();
                var nonGrounded = proc.structureSpawnModifier?.TryCast<SpawnStructureNonGroundedModifier>();

                if (grounded != null) prefabName = grounded.StructurePrefabName;
                else if (nonGrounded != null) prefabName = nonGrounded.StructurePrefabName;

                if (prefabName == null)
                    continue;

                PlayAudioModifer playMod = null;

                foreach (var binding in proc.bindings)
                {
                    playMod = binding.TryCast<PlayAudioModifer>();
                    if (playMod != null) break;
                }

                if (playMod == null) continue;

                structureSpawnSFX[prefabName] = playMod.AudioCall;
            }
        }
    }
    
    public static class Utilities
    {
        private static GameObject customMultiplayerMaps;
        
        public static string GetFriendlySceneName(string scene)
        {
            return scene switch
            {
                "Map0" => "Ring",
                "Map1" => "Pit",
                _ => scene
            };
        }

        public static string GetActiveCustomMapName()
        {
            customMultiplayerMaps ??= GameObject.Find("CustomMultiplayerMaps");

            if (customMultiplayerMaps == null)
                return null;

            for (int i = 0; i < customMultiplayerMaps.transform.childCount; i++)
            {
                var child = customMultiplayerMaps.transform.GetChild(i);
                if (child.gameObject.activeInHierarchy)
                    return child.name;
            }

            return null;
        }

        public static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";

            string s = Regex.Replace(name, "<.*?>", string.Empty);

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (!char.IsSurrogate(ch))
                    sb.Append(ch);
            }

            s = sb.ToString();

            foreach (var c in Path.GetInvalidPathChars())
                s = s.Replace(c.ToString(), "");

            s = Regex.Replace(s, @"\s+", " ").Trim();

            return string.IsNullOrEmpty(s) ? "Unknown" : s;
        }

        public static string GetReplayName(ReplayInfo replayInfo)
        {
            string sceneName = GetFriendlySceneName(replayInfo.Header.Scene);
            string customMapName = GetActiveCustomMapName();
            
            var localPlayer = PlayerManager.instance.localPlayer;
            string localPlayerName = CleanName(localPlayer.Data.GeneralData.PublicUsername);
            
            string opponent = replayInfo.Header.Players.FirstOrDefault(p => p.MasterId != localPlayer.Data.GeneralData.PlayFabMasterId).Name;
            string opponentName = !string.IsNullOrEmpty(opponent)
                ? CleanName(opponent)
                : "Unknown";

            string timestamp = replayInfo.Header.DateUTC;
            
            string matchFormat = $"{localPlayerName}-vs-{opponentName}_on_{sceneName}_{timestamp}.replay";
            
            if (!string.IsNullOrEmpty(customMapName))
                return $"{localPlayerName}-vs-{opponentName}_on_{customMapName}_{timestamp}.replay";

            return sceneName switch
            {
                "Ring" or "Pit" => matchFormat,
                "Park" => $"Replay_{timestamp}_{sceneName}_{replayInfo.Header.Players.Length}P_{localPlayerName}.replay",
                _ => $"Replay_{timestamp}_{sceneName}_{localPlayerName}.replay"
            };
        }
    }

    public class Main : MelonMod
    {
        public string currentScene = "Loader";

        // Recording
        public bool isRecording = false;
        
        public float elapsedRecordingTime = 0f;
        private float lastRecordedTime = 0f;
        
        public List<Structure> Structures = new();

        public List<Player> RecordedPlayers = new();
        public Dictionary<string, int> MasterIdToIndex = new();
        
        public List<Frame> Frames = new();
        
        // Playback
        public ReplayInfo currentReplay;
        
        public bool isPlaying = false;
        public bool isPaused = false;

        public float elapsedPlaybackTime = 0f;
        public int currentPlaybackFrame = 0;
        public int lastEventFrame = -1;
        
        public GameObject replayStructures;
        public GameObject[] PlaybackStructures;
        public HashSet<Structure> HiddenStructures = new();

        public GameObject replayPlayers;
        public Clone[] PlaybackPlayers;

        // Settings
        
        private Mod rumbleAnimatorMod = new();

        private ModSetting<bool> AutoRecordMatches = new();
        private ModSetting<bool> AutoRecordParks = new();

        private ModSetting<int> RecordFPS = new();

        public static Main instance;

        public Main() { instance = this; }

        public override void OnLateInitializeMelon()
        {
            UI.instance.UI_Initialized += OnUIInitialized;
            Calls.onMatchEnded += () => { if (isRecording) StopRecording(); };
            Calls.onMapInitialized += OnMapInitialized;

            Directory.CreateDirectory(
                Path.Combine(
                    MelonEnvironment.UserDataDirectory,
                    "MatchReplays"
                )
            );
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            currentScene = sceneName;
            
            if (isRecording)
                StopRecording();
        }

        public void OnMapInitialized()
        {
            if ((currentScene is "Map0" or "Map1" && (bool)AutoRecordMatches.SavedValue && PlayerManager.instance.AllPlayers.Count > 1) || (currentScene == "Park" && (bool)AutoRecordParks.SavedValue))
                StartRecording();

            if ((ReplayCache.structureSpawnSFX == null || ReplayCache.structurePools == null) && currentScene != "Loader")
                ReplayCache.BuildCacheTables();
        }

        public void OnUIInitialized()
        {
            rumbleAnimatorMod.ModName = BuildInfo.Name;
            rumbleAnimatorMod.ModVersion = BuildInfo.Version;

            rumbleAnimatorMod.SetFolder("MatchReplays");
            RecordFPS = rumbleAnimatorMod.AddToList("Recording FPS", 50, "The FPS that recordings record in.", new Tags());
            AutoRecordMatches = rumbleAnimatorMod.AddToList("Auto Record Matches", false, 0, "Automatically start recordings in matches", new Tags());
            
            rumbleAnimatorMod.GetFromFile();
            
            UI.instance.AddMod(rumbleAnimatorMod);
        }

        public void StartRecording()
        {
            Frames.Clear();
            Structures.Clear();
            RecordedPlayers.Clear();
            MasterIdToIndex.Clear();

            foreach (var structure in CombatManager.instance.structures)
            {
                if (structure == null ||
                    !structure.TryGetComponent(out Structure _) ||
                    structure.name.StartsWith("Static Target") ||
                    structure.name.StartsWith("Moving Target"))
                    continue;
                
                Structures.Add(structure);
            }

            foreach (var player in PlayerManager.instance.AllPlayers)
            {
                if (player == null)
                    continue;
                
                RecordedPlayers.Add(player);
            }
            
            elapsedRecordingTime = 0f;
            lastRecordedTime = 0f;

            isRecording = true;
        }

        public void StopRecording()
        {
            isRecording = false;

            var playerInfo = new List<PlayerInfo>();

            foreach (var player in PlayerManager.instance.AllPlayers)
            {
                PlayerInfo info = new PlayerInfo();

                info.ActorId = (byte)player.Data.GeneralData.ActorNo;
                info.MasterId = player.Data.GeneralData.PlayFabMasterId;
                info.Name = player.Data.GeneralData.PublicUsername;
                info.BattlePoints = player.Data.GeneralData.BattlePoints;
                info.VisualData = player.Data.VisualData.ToPlayfabDataString();
                info.EquippedShiftStones = player.Data.EquipedShiftStones.ToArray();
                info.Measurement = player.Data.PlayerMeasurement;
                
                info.WasHost = (info.ActorId == PhotonNetwork.MasterClient?.ActorNumber);

                playerInfo.Add(info);
            }
            
            var validStructures = new List<StructureInfo>();
            
            foreach (var s in Structures)
            {
                if (s == null) continue;

                var name = s.resourceName;
                
                validStructures.Add(new StructureInfo
                {
                    Type = 
                        name switch
                        {
                            "RockCube" => StructureType.Cube,
                            "Pillar" => StructureType.Pillar,
                            "Disc" => StructureType.Disc,
                            "Wall" => StructureType.Wall,
                            "Ball" => StructureType.Ball,
                            "LargeRock" => StructureType.LargeRock,
                            "SmallRock" => StructureType.SmallRock,
                            "BoulderBall" => StructureType.CagedBall,
                            _ => StructureType.Cube
                        }
                });
            }
            
            var replayInfo = new ReplayInfo
            {
                Header = new ReplaySerializer.ReplayHeader
                {
                    Version = BuildInfo.Version,
                    DateUTC = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    FPS = (int)RecordFPS.SavedValue,
                    Scene = currentScene,
                    FrameCount = Frames.Count,
                    StructureCount = validStructures.Count,
                    Players = playerInfo.ToArray(),
                    Structures = validStructures.ToArray()
                },
                Frames = Frames.ToArray()
            };
            
            MelonCoroutines.Start(
                ReplaySerializer.BuildReplayPackage(
                    $"{MelonEnvironment.UserDataDirectory}/{Utilities.GetReplayName(replayInfo)}", 
                    replayInfo
                )
            );
        }
        
        public void LoadReplay(string path)
        {
            if (currentScene == "Park" && (PhotonNetwork.CurrentRoom.PlayerCount > 1 || PhotonNetwork.CurrentRoom.IsVisible)) // Temporarily 
                return;
            
            currentReplay = ReplaySerializer.LoadReplay(path);
            
            elapsedPlaybackTime = 0f;
            currentPlaybackFrame = 0;
            
            // Structures
            
            if (replayStructures != null) GameObject.Destroy(replayStructures);
            HiddenStructures.Clear();
            PlaybackStructures = null;
            foreach (var structure in CombatManager.instance.structures)
            {
                if (structure == null) continue; 
                
                structure.gameObject.SetActive(false);
                HiddenStructures.Add(structure);
            }

            PlaybackStructures = new GameObject[currentReplay.Header.StructureCount];
            replayStructures = new GameObject("Replay Structures");
            
            for (int i = 0; i < PlaybackStructures.Length; i++)
            {
                var type = currentReplay.Header.Structures[i].Type;
                PlaybackStructures[i] = ReplayCache.structurePools.GetValueOrDefault(type).FetchFromPool().gameObject;
                
                Structure structure = PlaybackStructures[i].GetComponent<Structure>();
                structure.indistructable = true;
                
                PlaybackStructures[i].GetComponent<Rigidbody>().isKinematic = true;
                
                if (PlaybackStructures[i].TryGetComponent<NetworkGameObject>(out var networkGameObject))
                    GameObject.Destroy(networkGameObject);
                
                PlaybackStructures[i].SetActive(false);
                PlaybackStructures[i].transform.SetParent(replayStructures.transform);
            }
            
            // Players

            if (replayPlayers != null) GameObject.Destroy(replayPlayers);

            if (PlaybackPlayers != null)
            {
                foreach (var player in PlaybackPlayers)
                    PlayerManager.instance.AllPlayers.Remove(player.Controller.assignedPlayer);
            }
            
            PlaybackPlayers = null;

            if (currentReplay.Header.Scene is "Map0" or "Map1")
            {
                MelonCoroutines.Start(DoMatchStart());

                IEnumerator DoMatchStart()
                {
                    MatchHandler.instance.DoStartCountdown();
                    yield return new WaitForSeconds(10f);
                    MatchHandler.instance.FadeInCombatMusic();
                }
            }
            
            MelonCoroutines.Start(SpawnClones(() =>
            {
                isPlaying = true;
            }));
        }

        private IEnumerator SpawnClones(Action done = null)
        {
            PlaybackPlayers = new Clone[currentReplay.Header.Players.Length];
            replayPlayers = new GameObject("Replay Players");

            for (int i = 0; i < PlaybackPlayers.Length; i++)
            {
                Clone temp = null;
                yield return BuildClone(currentReplay.Header.Players[i], c => temp = c);

                PlaybackPlayers[i] = temp;
                temp.Controller.transform.SetParent(replayPlayers.transform);
            }
            
            done?.Invoke();
        }

        public static IEnumerator BuildClone(PlayerInfo pInfo, Action<Clone> Callback, Vector3 initialPosition = default)
        {
            var visualData = string.IsNullOrEmpty(pInfo.VisualData) ? PlayerVisualData.DefaultMale : PlayerVisualData.FromPlayfabDataString(pInfo.VisualData);
            var randomID = Guid.NewGuid().ToString();
            
            pInfo.Name = string.IsNullOrEmpty(pInfo.Name) ? $"Player_{pInfo.MasterId}" : pInfo.Name;
            
            pInfo.EquippedShiftStones ??= new short[] { -1, -1 };
            var shiftstones = new Il2CppStructArray<short>(2);
            
            for (int i = 0; i < 2; i++)
                shiftstones[i] = pInfo.EquippedShiftStones[i];
            
            PlayerData data = new PlayerData(
                new GeneralData
                {
                    PlayFabMasterId = pInfo.MasterId,
                    PlayFabTitleId = randomID,
                    BattlePoints = pInfo.BattlePoints,
                    PublicUsername = pInfo.Name
                },
                PlayerManager.instance.localPlayer.Data.RedeemedMoves,
                PlayerManager.instance.localPlayer.Data.EconomyData,
                shiftstones,
                visualData
            );
            
            data.PlayerMeasurement = pInfo.Measurement.Length != 0 ? pInfo.Measurement : PlayerManager.instance.localPlayer.Data.PlayerMeasurement;

            Player newPlayer = Player.CreateRemotePlayer(data);
            PlayerManager.instance.AllPlayers.Add(newPlayer);
            PlayerManager.instance.SpawnPlayerController(newPlayer, initialPosition, Quaternion.identity);

            yield return null;
            yield return null;

            GameObject body = newPlayer.Controller.gameObject;
            body.name = $"Player_{pInfo.MasterId}";
            
            GameObject Overall = body.transform.GetChild(2).gameObject;
            GameObject LHand = Overall.transform.GetChild(1).gameObject;
            GameObject RHand = Overall.transform.GetChild(2).gameObject;
            GameObject Head = Overall.transform.GetChild(0).GetChild(0).gameObject;

            GameObject.Destroy(Overall.GetComponent<NetworkGameObject>());
            Overall.GetComponent<Rigidbody>().isKinematic = true;
            
            var localTransform = PlayerManager.instance.localPlayer.Controller.transform;
            newPlayer.Controller.transform.position = localTransform.position;
            newPlayer.Controller.transform.rotation = localTransform.rotation;

            var physics = body.transform.GetChild(5);
            var lControllerPhysics = physics.GetChild(2).GetComponent<ConfigurableJoint>();
            lControllerPhysics.xMotion = 0;
            lControllerPhysics.yMotion = 0;
            lControllerPhysics.zMotion = 0;

            var rControllerPhysics = physics.GetChild(3).GetComponent<ConfigurableJoint>();
            rControllerPhysics.xMotion = 0;
            rControllerPhysics.yMotion = 0;
            rControllerPhysics.zMotion = 0;
            
            foreach (var driver in body.GetComponentsInChildren<TrackedPoseDriver>())
                driver.enabled = false;
            
            body.transform.GetChild(10).gameObject.SetActive(false);

            Callback.Invoke(new Clone
            {
                VRRig = Overall,
                LeftHand = LHand,
                RightHand = RHand,
                Head = Head,
                Controller = newPlayer.Controller
            });
        }

        public void StopReplay()
        {
            if (currentReplay.Frames == null) return;
            if (replayStructures != null) GameObject.Destroy(replayStructures);
            if (replayPlayers != null) GameObject.Destroy(replayPlayers);
            
            foreach (var structure in HiddenStructures) structure.gameObject.SetActive(true);

            if (PlaybackPlayers != null)
            {
                foreach (var player in PlaybackPlayers)
                    PlayerManager.instance.AllPlayers.Remove(player.Controller.assignedPlayer);
            }
            
            if (currentReplay.Header.Scene is "Map0" or "Map1")
                MatchHandler.instance.FadeOutCombatMusic();
            
            PlaybackPlayers = null;
            replayStructures = null;
            HiddenStructures.Clear();

            elapsedPlaybackTime = 0;
            currentPlaybackFrame = 0;

            isPlaying = false;
            isPaused = false;
            
            CombatManager.instance.CleanStructureList();
        }

        public override void OnUpdate()
        {
            HandleRecording();
            HandlePlayback();
        }

        public void HandleRecording()
        {
            if (!isRecording) return;
            
            elapsedRecordingTime += Time.deltaTime;
            
            if (elapsedRecordingTime - lastRecordedTime < (1f / 50f))
                return;

            lastRecordedTime = elapsedRecordingTime;
            
            // Structures
            
            var structureStates = new List<StructureState>();
            
            foreach (var structure in Structures)
            {
                if (structure == null ||
                    !structure.TryGetComponent(out Structure _) ||
                    structure.name.StartsWith("Static Target") ||
                    structure.name.StartsWith("Moving Target"))
                    continue;

                if (isPlaying && HiddenStructures.Contains(structure))
                    continue;
                
                
                structureStates.Add(new StructureState
                {
                    position = structure.transform.position,
                    rotation = structure.transform.rotation,
                    active = structure.gameObject.activeSelf,
                    grounded = structure.IsGrounded
                });
            }
            
            // Players

            var playerStates = new PlayerState[RecordedPlayers.Count];

            for (int i = 0; i < RecordedPlayers.Count; i++)
            {
                var p = RecordedPlayers[i];

                if (p == null)
                {
                    playerStates[i] = default;
                    playerStates[i].active = false;
                    continue;
                }

                Transform VRRig = p.Controller.transform.GetChild(2);
                Transform LHand = VRRig.transform.GetChild(1);
                Transform RHand = VRRig.transform.GetChild(2);
                Transform Head = VRRig.transform.GetChild(0).GetChild(0);

                playerStates[i] = new PlayerState
                {
                    VRRigPos = VRRig.position,
                    VRRigRot = VRRig.rotation,
                    HeadPos = Head.localPosition,
                    HeadRot = Head.localRotation,
                    LHandPos = LHand.localPosition,
                    LHandRot = LHand.localRotation,
                    RHandPos = RHand.localPosition,
                    RHandRot = RHand.localRotation,
                    Health = p.Data.HealthPoints,
                    currentStack = Patches.activations.FirstOrDefault(s => s.playerId == p.Data.GeneralData.PlayFabMasterId).stackId,
                    active = true
                };
            }

            var frame = new Frame
            {
                Time = elapsedRecordingTime, 
                Structures = structureStates.ToArray(),
                Players = playerStates
            };
            Frames.Add(frame);

            Patches.activations.Clear();
        }

        public void HandlePlayback()
        {
            if (!isPlaying || isPaused) return;

            if (currentPlaybackFrame >= currentReplay.Frames.Length - 1)
            {
                StopReplay();
                return;
            }

            float timePerFrame = 1f / currentReplay.Header.FPS;
            
            elapsedPlaybackTime += Time.deltaTime;
            
            while (elapsedPlaybackTime >= timePerFrame)
            {
                elapsedPlaybackTime -= timePerFrame;
                currentPlaybackFrame++;
            }

            float t = elapsedPlaybackTime / timePerFrame;

            ApplyInterpolatedFrame(currentPlaybackFrame, t);
        }

        public void ApplyInterpolatedFrame(int frameIndex, float t)
        {
            var frames = currentReplay.Frames;

            if (frameIndex < 0 || frameIndex >= frames.Length - 1)
                return;

            Frame a = frames[frameIndex];
            Frame b = frames[frameIndex + 1];

            for (int i = 0; i < PlaybackStructures.Length; i++)
            {
                var playbackStructure = PlaybackStructures[i];
                var sa = a.Structures[i];
                var sb = b.Structures[i];
                var poolManager = PoolManager.instance;

                bool justSpawned = false;
                
                if (!playbackStructure.GetComponent<Rigidbody>().isKinematic)
                    playbackStructure.GetComponent<Rigidbody>().isKinematic = true;
                
                if (playbackStructure.GetComponentInChildren<MeshRenderer>().material.GetFloat("_shake") == 1)
                    playbackStructure.GetComponentInChildren<MeshRenderer>().material.SetFloat("_shake", 1f);
                
                // Event checks

                if (currentPlaybackFrame != lastEventFrame)
                {
                    // Structure Broke
                    if (sa.active && !sb.active)
                    {
                        var pool = poolManager.GetPool(
                            playbackStructure.name == "Disc" 
                                ? "DustBreakDISC_VFX"
                                : "DustBreak_VFX"
                        );
                    
                        pool.FetchFromPool(playbackStructure.transform.position, playbackStructure.transform.rotation);
                        AudioManager.instance.Play(
                            playbackStructure.GetComponent<Structure>().onDeathAudio, 
                            playbackStructure.transform.position
                        );
                    }

                    // Structure Spawned
                    if (!sa.active && sb.active)
                    {
                        justSpawned = true;
                        
                        var pool = poolManager.GetPool("DustSpawn_VFX");

                        var offset = playbackStructure.name is "Ball" or "Disc" ? Vector3.zero : new Vector3(0, 0.5f, 0);
                        if (playbackStructure.name is "Wall")
                            offset = new Vector3(0, -0.5f, 0);
                        
                        pool.FetchFromPool(sb.position + offset, Quaternion.identity);
                    
                        if (ReplayCache.structureSpawnSFX.TryGetValue(playbackStructure.name, out var audioCall))
                            AudioManager.instance.Play(audioCall, playbackStructure.transform.position);
                    }
                    
                    // Grounded
                    if (!sa.grounded && sb.grounded && sa.active)
                    {
                        var pool = poolManager.GetPool("Ground_VFX");
                        
                        var offset = playbackStructure.name is "Ball" or "Disc" ? Vector3.zero : new Vector3(0, -0.5f, 0);
                        pool.FetchFromPool(sb.position + offset, Quaternion.identity);

                        var structure = playbackStructure.GetComponent<Structure>();
                        structure.processableComponent.SetCurrentState(structure.groundedState);
                    }

                    // Ungrounded
                    if (sa.grounded && !sb.grounded)
                    {
                        var structure = playbackStructure.GetComponent<Structure>();
                        structure.processableComponent.SetCurrentState(structure.freeState);
                    }
                }

                if (!sb.active)
                {
                    playbackStructure.SetActive(false);
                    continue;
                }
                
                if (!playbackStructure.activeSelf)
                    playbackStructure.SetActive(true);

                Vector3 pos = Vector3.Lerp(sa.position, sb.position, t);
                Quaternion rot = Quaternion.Slerp(sa.rotation, sb.rotation, t);

                if (!justSpawned)
                    playbackStructure.transform.SetPositionAndRotation(pos, rot);
                else
                    playbackStructure.transform.SetPositionAndRotation(sb.position, sb.rotation);
            }
            
            lastEventFrame = currentPlaybackFrame;

            for (int i = 0; i < PlaybackPlayers.Length; i++)
            {
                var playbackPlayer = PlaybackPlayers[i];
                var pa = a.Players[i];
                var pb = b.Players[i];

                if (!pa.active && !pb.active)
                {
                    playbackPlayer.Controller.gameObject.SetActive(false);
                    continue;
                }

                if (pa.Health != pb.Health)
                {
                    playbackPlayer.Controller.GetSubsystem<PlayerHealth>().SetHealth(pb.Health, pa.Health);
                    
                    // Hit (not death)
                    if (pb.Health < pa.Health && pb.Health != 0)
                    {
                        var hitmarker = PoolManager.instance.GetPool("PlayerHitmarker")
                            .FetchFromPool(playbackPlayer.VRRig.transform.position + new Vector3(0, 0.5f, 0), Quaternion.identity)
                            .Cast<PlayerHitmarker>();

                        hitmarker.SetDamage(pa.Health - pb.Health);
                        hitmarker.gameObject.SetActive(true);
                        hitmarker.Play();
                    }
                }

                if (pa.currentStack != (short)StackType.None && pa.currentStack != pb.currentStack)
                {
                    var key = ReplayCache.NameToStackType.FirstOrDefault(s => s.Value == (StackType)pa.currentStack);

                    var stack = playbackPlayer.Controller
                        .GetSubsystem<PlayerStackProcessor>()
                        .availableStacks.ToArray()
                        .FirstOrDefault(s => s.CachedName == key.Key);

                    playbackPlayer.Controller
                        .GetSubsystem<PlayerStackProcessor>()
                        .Execute(stack);
                }
                
                if (!playbackPlayer.Controller.gameObject.activeSelf)
                    playbackPlayer.Controller.gameObject.SetActive(true);

                playbackPlayer.ApplyInterpolatedPose(pa, pb, t);
            }
        }
    }
}

public class Clone
{
    public GameObject VRRig;
    public GameObject LeftHand;
    public GameObject RightHand;
    public GameObject Head;
    public PlayerController Controller;
    
    public void ApplyInterpolatedPose(PlayerState a, PlayerState b, float t)
    {
        VRRig.transform.position = Vector3.Lerp(a.VRRigPos, b.VRRigPos, t);
        VRRig.transform.rotation = Quaternion.Slerp(a.VRRigRot, b.VRRigRot, t);
        
        Head.transform.localPosition = Vector3.Lerp(a.HeadPos, b.HeadPos, t);
        Head.transform.localRotation = Quaternion.Slerp(a.HeadRot, b.HeadRot, t);
        
        LeftHand.transform.localPosition = Vector3.Lerp(a.LHandPos, b.LHandPos, t);
        LeftHand.transform.localRotation = Quaternion.Slerp(a.LHandRot, b.LHandRot, t);
        
        RightHand.transform.localPosition = Vector3.Lerp(a.RHandPos, b.RHandPos, t);
        RightHand.transform.localRotation = Quaternion.Slerp(a.RHandRot, b.RHandRot, t);
    }
}