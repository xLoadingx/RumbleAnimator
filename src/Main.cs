using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Networking;
using Il2CppRUMBLE.Networking.MatchFlow;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppTMPro;
using MelonLoader;
using RumbleModdingAPI;
using RumbleModUI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.XR;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using InteractionButton = Il2CppRUMBLE.Interactions.InteractionBase.InteractionButton;
using Main = RumbleAnimator.Main;
using PlayerState = RumbleAnimator.PlayerState;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;
using Utilities = RumbleAnimator.ReplayGlobals.Utilities;
using ReplayFiles = RumbleAnimator.ReplayGlobals.ReplayFiles;
using ReplayCache = RumbleAnimator.ReplayGlobals.ReplayCache;
using ReplayVoices = RumbleAnimator.ReplayGlobals.ReplayVoices;

[assembly: MelonInfo(typeof(Main), RumbleAnimator.BuildInfo.Name, RumbleAnimator.BuildInfo.Version, RumbleAnimator.BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace RumbleAnimator
{
    public static class BuildInfo
    {
        public const string Name = "RumbleReplay";
        public const string Author = "ERROR";
        public const string Version = "1.0.0";
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
        public List<PlayerInfo> PlayerInfos = new();

        public GameObject clapperboardVFX;

        public bool hasPlayed;
        public float heldTime = 0f;
        public float soundTimer = 0f;
        public Transform leftHand;
        public Transform rightHand;
        public Transform head;
        
        public List<Frame> Frames = new();
        
        // Playback
        public static ReplayInfo currentReplay;
        
        public static bool isPlaying = false;
        public static float playbackSpeed = 1f;

        public float elapsedPlaybackTime = 0f;
        public int currentPlaybackFrame = 0;

        public enum ReplayEvent
        {
            StructureSpawn,
            StructureBreak,
            StructureGrounded,
            StructureUngrounded,
            HoldStart,
            HoldEnd,
            FlickStart,
            FlickEnd,
            PlayerHit,
            StackExit
        }

        public Dictionary<ReplayEvent, int> lastEventFrame = new();

        public GameObject ReplayRoot;
        
        public GameObject replayStructures;
        public GameObject[] PlaybackStructures;
        public HashSet<Structure> HiddenStructures = new();

        public HashSet<PooledVisualEffect> visualEffects = new();

        public GameObject replayPlayers;
        public Clone[] PlaybackPlayers;

        public ReplayTable replayTable;
        
        public const float errorsArmspan = 1.2744f;

        // Settings
        
        private Mod rumbleAnimatorMod = new();

        public ModSetting<bool> AutoRecordMatches = new();
        public ModSetting<bool> AutoRecordParks = new();

        public ModSetting<float> tableOffset = new();

        public static Main instance;

        public Main() { instance = this; }

        public override void OnLateInitializeMelon()
        {
            UI.instance.UI_Initialized += OnUIInitialized;
            Calls.onMatchEnded += () => { if (isRecording) StopRecording(); };
            Calls.onMapInitialized += OnMapInitialized;

            ReplayFiles.Init();
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

            if ((ReplayCache.SFX == null || ReplayCache.structurePools == null) && currentScene != "Loader")
                ReplayCache.BuildCacheTables();

            if (replayTable == null && clapperboardVFX == null)
                LoadReplayObjects();

            if (currentScene == "Gym")
            {
                replayTable.TableRoot.SetActive(true);
                replayTable.metadataText.gameObject.SetActive(true);
            }
            else
            {
                replayTable.TableRoot.SetActive(false);
                replayTable.metadataText.gameObject.SetActive(false);
            }

            ReplayVoices.Hook();

            var vr = PlayerManager.instance.localPlayer.Controller.transform.GetChild(2);
            replayTable.metadataText.GetComponent<LookAtPlayer>().playerHeadset = vr.GetChild(0).GetChild(0);

            leftHand = vr.GetChild(1);
            rightHand = vr.GetChild(2);
            head = vr.GetChild(0).GetChild(0);
        }

        public void OnUIInitialized()
        {
            rumbleAnimatorMod.ModName = BuildInfo.Name;
            rumbleAnimatorMod.ModVersion = BuildInfo.Version;

            rumbleAnimatorMod.SetFolder("MatchReplays");
            AutoRecordMatches = rumbleAnimatorMod.AddToList("Auto Record Matches", true, 0, "Automatically start recordings in matches.", new Tags());
            AutoRecordParks = rumbleAnimatorMod.AddToList("Auto Record Parks", false, 0, "Automatically start recordings when you join a park.", new Tags());

            tableOffset = rumbleAnimatorMod.AddToList("Table Offset", 0f, "Table offset in meters.\nThe table does move with your scale, but the default might feel too low for some people.", new Tags());
            
            rumbleAnimatorMod.GetFromFile();
            
            UI.instance.AddMod(rumbleAnimatorMod);
        }

        public void LoadReplayObjects()
        {
            GameObject ReplayTable = new GameObject("Replay Table");
            
            ReplayTable.transform.localPosition = new Vector3(5.9506f, 1.3564f, 4.1906f);
            ReplayTable.transform.localRotation = Quaternion.Euler(270f, 121.5819f, 0f);

            AssetBundle bundle = Calls.LoadAssetBundleFromStream(this, "RumbleAnimator.src.replayobjects");
            
            GameObject table = GameObject.Instantiate(bundle.LoadAsset<GameObject>("Table"), ReplayTable.transform);

            table.name = "Table";
            table.transform.localScale *= 0.5f;
            table.transform.localRotation = Quaternion.identity;
            
            Material tableMat = new Material(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Gearmarket.Stallframe.GetGameObject().GetComponent<Renderer>().material);
            tableMat.SetTexture("_Albedo", bundle.LoadAsset<Texture2D>("Texture"));
            table.GetComponent<Renderer>().material = tableMat;
            
            if (Calls.Mods.findOwnMod("Rumble Dark Mode", "Bleh", false))
                table.GetComponent<Renderer>().lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            GameObject levitateVFX = GameObject.Instantiate(
                Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Parkboard.PlayerRelocationTrigger.StandHere.GetGameObject(),
                ReplayTable.transform
            );

            levitateVFX.name = "LevitateVFX";
            levitateVFX.transform.localPosition = new Vector3(0, 0, -0.2764f);
            levitateVFX.transform.localRotation = Quaternion.Euler(270, 0, 0);
            levitateVFX.transform.localScale = Vector3.one * 0.8f;
            
            GameObject Next = GameObject.Instantiate(
                Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Telephone20REDUXspecialedition.FriendScreen.FriendScrollBar.ScrollUpButton.GetGameObject(),
                ReplayTable.transform
            );

            Next.name = "Next Replay";
            Next.transform.localPosition = new Vector3(0.2978f, -0.248f, -0.181f);
            Next.transform.localRotation = Quaternion.Euler(345.219f, 340.1203f, 234.8708f);
            Next.transform.localScale = Vector3.one * 1.8f;
            var nextButton = Next.transform.GetChild(0).GetComponent<InteractionButton>();
            nextButton.enabled = true;
            nextButton.onPressedAudioCall = ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"];
            nextButton.OnPressed.RemoveAllListeners();
            nextButton.OnPressed.AddListener((UnityAction)(() => { ReplayFiles.NextReplay(); }));

            GameObject Previous = GameObject.Instantiate(Next, ReplayTable.transform);
            
            Previous.name = "Previous Replay";
            Previous.transform.localPosition = new Vector3(0.3204f, 0.2192f, -0.1844f);
            Previous.transform.localRotation = Quaternion.Euler(10.6506f, 337.4582f, 296.0434f);
            Previous.transform.GetChild(0).GetChild(3).localRotation = Quaternion.Euler(90, 180, 0);
            var previousButton = Previous.transform.GetChild(0).GetComponent<InteractionButton>();
            previousButton.enabled = true;
            previousButton.onPressedAudioCall = ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardUnlocked"];
            previousButton.OnPressed.RemoveAllListeners();
            previousButton.OnPressed.AddListener((UnityAction)(() => { ReplayFiles.PreviousReplay(); }));

            var tableFloat = ReplayTable.AddComponent<TableFloat>();
            tableFloat.speed = (2 * PI) / 10;
            tableFloat.amplitude = 0.01f;

            var replayNameText = Calls.Create.NewText("No Replay Selected", 5f, new Color(0.102f, 0.051f, 0.0275f), Vector3.zero, Quaternion.identity);
            replayNameText.transform.SetParent(ReplayTable.transform);

            replayNameText.name = "Replay Name";
            replayNameText.transform.localScale = Vector3.one * 0.07f;
            replayNameText.transform.localPosition = new Vector3(0.446f, -0.0204f, -0.1297f);
            replayNameText.transform.localRotation = Quaternion.Euler(0, 249.3179f, 270f);
            
            var indexText = Calls.Create.NewText("(0 / 0)", 5f, new Color(0.102f, 0.051f, 0.0275f), Vector3.zero, Quaternion.identity);
            indexText.transform.SetParent(ReplayTable.transform);

            indexText.name = "Replay Index";
            indexText.transform.localScale = Vector3.one * 0.04f;
            indexText.transform.localPosition = new Vector3(0.4604f, -0.0204f, -0.1697f);
            indexText.transform.localRotation = Quaternion.Euler(0, 249.3179f, 270f);

            var metadataText = Calls.Create.NewText("", 5f, Color.white, Vector3.zero, Quaternion.identity);

            metadataText.name = "Metadata Text";
            metadataText.transform.position = new Vector3(5.9575f, 1.8514f, 4.2102f);
            metadataText.transform.localScale = Vector3.one * 0.25f;
            
            var textTableFloat = metadataText.AddComponent<TableFloat>();
            textTableFloat.speed = (2.5f * PI) / 10;
            textTableFloat.amplitude = 0.01f;
            
            var metadataTMP = metadataText.GetComponent<TextMeshPro>();
            metadataTMP.m_HorizontalAlignment = HorizontalAlignmentOptions.Center;

            metadataText.AddComponent<LookAtPlayer>().playerHeadset = PlayerManager.instance.localPlayer.Controller.transform.GetChild(2).GetChild(0).GetChild(0);
            
            GameObject.DontDestroyOnLoad(metadataText);

            var loadReplayButton = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.ResetFighterButton.GetGameObject(), ReplayTable.transform);
            
            loadReplayButton.name = "Load Replay";
            loadReplayButton.transform.localPosition = new Vector3(0.5187f, -0.0131f, -0.1956f);
            loadReplayButton.transform.localRotation = Quaternion.Euler(0, 198.4543f, 0);
            loadReplayButton.transform.localScale = new Vector3(0.3945f, 0.9128f, 0.6f);
            loadReplayButton.transform.GetChild(1).transform.localScale = new Vector3(0.23f, 0.1f, 0.1f);
            
            var loadReplayButtonComp = loadReplayButton.transform.GetChild(0).GetComponent<InteractionButton>();
            loadReplayButtonComp.enabled = true;
            loadReplayButtonComp.OnPressed.RemoveAllListeners();

            loadReplayButtonComp.onPressedAudioCall = loadReplayButtonComp.longPressAudioCall;

            replayTable = ReplayTable.AddComponent<ReplayTable>();
            replayTable.TableRoot = ReplayTable;
            
            replayTable.nextButton = nextButton;
            replayTable.previousButton = previousButton;
            replayTable.loadButton = loadReplayButtonComp;

            replayTable.replayNameText = replayNameText.GetComponent<TextMeshPro>();
            replayTable.indexText = indexText.GetComponent<TextMeshPro>();
            replayTable.metadataText = metadataTMP;
            
            loadReplayButtonComp.OnPressed.AddListener((UnityAction)(() =>
            {
                if (ReplayFiles.currentIndex != -1)
                {
                    if (ReplayFiles.currentHeader.Scene is "Map0" or "Map1")
                    {
                        ReplayFiles.HideMetadata();
                        MelonCoroutines.Start(Utilities.LoadMap(ReplayFiles.currentHeader.Scene == "Map0" ? 3 : 4, 2.5f, () =>
                        {
                            LoadReplay(ReplayFiles.currentReplayPath);
                            ReplayFiles.ShowMetadata();
                            isPlaying = true;
                        }));
                    }
                    else if (ReplayFiles.currentHeader.Scene != "Park")
                    {
                        LoadReplay(ReplayFiles.currentReplayPath);
                        isPlaying = true;
                    }
                }
                else
                {
                    AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Failure"], loadReplayButton.transform.position);
                }
            }));
            
            ReplayFiles.table = replayTable;
            ReplayFiles.HideMetadata();

            clapperboardVFX = GameObject.Instantiate(bundle.LoadAsset<GameObject>("Clapper"));
            clapperboardVFX.name = "ClapperboardVFX";
            clapperboardVFX.transform.localScale = Vector3.one * 5f;
            
            Material clapperboardMat = new Material(tableMat);
            clapperboardMat.SetTexture("_Albedo", bundle.LoadAsset<Texture2D>("ClapperTexture"));
            clapperboardVFX.GetComponent<Renderer>().material = clapperboardMat;
            clapperboardVFX.transform.GetChild(0).GetComponent<Renderer>().material = clapperboardMat;
            clapperboardVFX.SetActive(false);

            GameObject.DontDestroyOnLoad(ReplayTable);
            GameObject.DontDestroyOnLoad(clapperboardVFX);
            bundle.Unload(false);
        }

        public void PlayClapperboardVFX(Vector3 position, Quaternion rotation)
        {
            var clapperboard = GameObject.Instantiate(clapperboardVFX);
            clapperboard.SetActive(true);
            clapperboard.transform.localScale = Vector3.zero;
            clapperboard.transform.position = position;
            clapperboard.transform.rotation = rotation;

            var vfx = PoolManager.instance.GetPool("RockCamSpawn_VFX").FetchFromPool(position, rotation);
            vfx.transform.localPosition += new Vector3(0f, 0.1f, 0f);
            vfx.transform.localScale = Vector3.one * 0.9f;

            AudioManager.instance.Play(!isRecording ? ReplayCache.SFX["Call_RockCam_StartRecording"] : ReplayCache.SFX["Call_RockCam_StopRecording"], position);

            MelonCoroutines.Start(Utilities.LerpValue(
                () => clapperboard.transform.localScale,
                v => clapperboard.transform.localScale = v,
                Vector3.Lerp,
                Vector3.one * 5f,
                0.5f,
                Utilities.EaseInOut,
                () =>
                {
                    MelonCoroutines.Start(Utilities.LerpValue(
                        () => clapperboard.transform.localScale,
                        v => clapperboard.transform.localScale = v,
                        Vector3.Lerp,
                        Vector3.zero,
                        0.5f,
                        Utilities.EaseInOut,
                        () =>
                        {
                            GameObject.Destroy(clapperboard);
                            AudioManager.instance.Play(ReplayCache.SFX["Call_RockCam_Despawn"], position);
                        }
                    ));
                }
            ));
            
            MelonCoroutines.Start(Utilities.LerpValue(
                () => clapperboard.transform.localRotation,
                v => clapperboard.transform.localRotation = v,
                Quaternion.Slerp,
                rotation * Quaternion.Euler(0f, 17f, 0f),
                0.8f,
                Utilities.EaseInOut
            ));

            MelonCoroutines.Start(Utilities.LerpValue(
                () => clapperboard.transform.GetChild(0).localRotation,
                v => clapperboard.transform.GetChild(0).localRotation = v,
                Quaternion.Slerp,
                Quaternion.Euler(0f, 9.221f, 0f),
                0.5f,
                Utilities.EaseInOut,
                () =>
                {
                    MelonCoroutines.Start(Utilities.LerpValue(
                        () => clapperboard.transform.GetChild(0).localRotation,
                        v => clapperboard.transform.GetChild(0).localRotation = v,
                        Quaternion.Slerp,
                        Quaternion.Euler(0f, 347.9986f, 0f),
                        0.5f,
                        Utilities.EaseInOut
                    ));
                }
            ));
        }

        public void StartRecording()
        {
            Frames.Clear();
            Structures.Clear();
            RecordedPlayers.Clear();
            MasterIdToIndex.Clear();
            PlayerInfos.Clear();

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
                
                PlayerInfo info = new PlayerInfo();

                info.ActorId = (byte)player.Data.GeneralData.ActorNo;
                info.MasterId = player.Data.GeneralData.PlayFabMasterId;
                info.Name = player.Data.GeneralData.PublicUsername;
                info.BattlePoints = player.Data.GeneralData.BattlePoints;
                info.VisualData = player.Data.VisualData.ToPlayfabDataString();
                info.EquippedShiftStones = player.Data.EquipedShiftStones.ToArray();
                info.Measurement = player.Data.PlayerMeasurement;
                
                info.WasHost = (info.ActorId == PhotonNetwork.MasterClient?.ActorNumber);

                PlayerInfos.Add(info);
            }
            
            elapsedRecordingTime = 0f;
            lastRecordedTime = 0f;

            isRecording = true;
        }

        public void StopRecording()
        {
            isRecording = false;
            
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

            var hostPlayer = PlayerInfos.FirstOrDefault(p => p.WasHost);
            string hostName = hostPlayer.Name ?? PlayerInfos.FirstOrDefault().Name ?? "Unknown";

            string customMap = Utilities.GetActiveCustomMapName();
            string sceneName = string.IsNullOrWhiteSpace(customMap)
                ? Utilities.GetFriendlySceneName(currentScene)
                : customMap;

            string title = currentScene switch
            {
                "Park" => 
                    $"{hostName}<#FFF> - Park\n" + $"<size=85%>{PlayerInfos.Count} Player{(PlayerInfos.Count != 1 ? "s" : "")}",

                _ when PlayerInfos.Count == 2 && currentScene != "Gym" =>
                    $"{PlayerInfos[0].Name}<#FFF> vs {PlayerInfos[1].Name}<#FFF> - {sceneName}",

                _ =>
                    $"{hostName}<#FFF> - {sceneName}"
            };
            
            var replayInfo = new ReplayInfo
            {
                Header = new ReplaySerializer.ReplayHeader
                {
                    Title = title,
                    Version = BuildInfo.Version,
                    DateUTC = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    FPS = 50,
                    Scene = currentScene,
                    CustomMap = customMap,
                    FrameCount = Frames.Count,
                    StructureCount = validStructures.Count,
                    Players = PlayerInfos.ToArray(),
                    Structures = validStructures.ToArray()
                },
                Frames = Frames.ToArray()
            };

            ReplaySerializer.BuildReplayPackage(
                $"{ReplayFiles.replayFolder}/{Utilities.GetReplayName(replayInfo)}",
                replayInfo,
                done: () => { AudioManager.instance.Play(ReplayCache.SFX["Call_PoseGhost_PosePerformed"], PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerVR>().transform.position); }
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
                ReplayRoot = new GameObject("Replay Root");

                replayStructures.transform.SetParent(ReplayRoot.transform);
                replayPlayers.transform.SetParent(ReplayRoot.transform);
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

            foreach (var visualEffect in visualEffects)
                GameObject.Destroy(visualEffect);
            visualEffects.Clear();

            elapsedPlaybackTime = 0;
            currentPlaybackFrame = 0;

            isPlaying = false;
            
            CombatManager.instance.CleanStructureList();
        }

        public override void OnUpdate()
        {
            HandleRecording();
            HandlePlayback();

            HandleReplayPose();
        }

        public void HandleReplayPose()
        {
            if (currentScene != "Loader" && leftHand != null && rightHand != null && head != null)
            {
                bool LeftSideways = Abs(Vector3.Dot(leftHand.forward, head.forward)) < 0.4f;
                bool RightSideways = Abs(Vector3.Dot(rightHand.forward, head.forward)) < 0.4f;
                bool opposite = Vector3.Dot(leftHand.forward, rightHand.forward) < -0.7f;
                bool closeEnough = Vector3.Distance(leftHand.position, rightHand.position) < PlayerManager.instance.localPlayer.Data.PlayerMeasurement.ArmSpan * (0.30f / errorsArmspan);
                
                if (LeftSideways && RightSideways && opposite && closeEnough && Calls.ControllerMap.LeftController.GetGrip() > 0.8f && Calls.ControllerMap.RightController.GetGrip() > 0.8f)
                {
                    heldTime += Time.deltaTime;
                    soundTimer += Time.deltaTime;

                    if (soundTimer >= 0.5f && !hasPlayed)
                    {
                        soundTimer -= 0.5f;
                        AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"], head.position);
                    }
                    
                    if (heldTime >= 2f && !hasPlayed)
                    {
                        hasPlayed = true;
                        PlayClapperboardVFX(head.position + head.forward * 1.2f + new Vector3(0f, -0.1f, 0f), Quaternion.Euler(270f, head.eulerAngles.y + 180f, 0f));

                        if (isRecording)
                            StopRecording();
                        else
                            StartRecording();
                    }
                }
                else
                {
                    hasPlayed = false;
                    heldTime = 0f;
                    soundTimer = 0f;
                }
            }
        }

        public void HandleRecording()
        {
            if (!isRecording) return;
            
            elapsedRecordingTime += Time.deltaTime;
            
            if (elapsedRecordingTime - lastRecordedTime < (1f / 50f))
                return;

            lastRecordedTime = elapsedRecordingTime;
            
            // Players

            var heldStructures = new HashSet<GameObject>();
            var flickedStructures = new HashSet<GameObject>();
            
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

                var stackProc = p.Controller.GetSubsystem<PlayerStackProcessor>();

                Stack flickStack = null;
                Stack holdStack = null;

                foreach (var stack in stackProc.availableStacks)
                {
                    switch (stack.cachedName)
                    {
                        case "Flick":
                            flickStack = stack;
                            break;

                        case "HoldLeft":
                        case "HoldRight":
                            holdStack = stack;
                            break;
                    }

                    if (flickStack != null && holdStack != null)
                        break;
                }

                if (flickStack != null)
                {
                    foreach (var exec in flickStack.runningExecutions)
                    {
                        var proc = exec.TargetProcessable.TryCast<ProcessableComponent>();
                        if (proc == null)
                            continue;

                        var go = proc.gameObject;
                        if (go != null)
                            flickedStructures.Add(go);
                    }
                }
                
                if (holdStack != null)
                {
                    foreach (var exec in holdStack.runningExecutions)
                    {
                        var proc = exec.TargetProcessable.TryCast<ProcessableComponent>();
                        if (proc == null)
                            continue;

                        var go = proc.gameObject;
                        if (go != null)
                            heldStructures.Add(go);
                    }
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
            
            // Structures
            
            var structureStates = new StructureState[Structures.Count];
            
            for (int i = 0; i < Structures.Count; i++)
            {
                var structure = Structures[i];
                
                if (structure == null ||
                    !structure.TryGetComponent(out Structure _) ||
                    structure.name.StartsWith("Static Target") ||
                    structure.name.StartsWith("Moving Target"))
                    continue;

                if (isPlaying && HiddenStructures.Contains(structure))
                    continue;
                
                structureStates[i] = new StructureState
                {
                    position = structure.transform.position,
                    rotation = structure.transform.rotation,
                    active = structure.gameObject.activeSelf,
                    grounded = structure.IsGrounded,
                    
                    isHeld = heldStructures.Contains(structure.gameObject),
                    isFlicked = flickedStructures.Contains(structure.gameObject)
                };
            }

            var frame = new Frame
            {
                Time = elapsedRecordingTime, 
                Structures = structureStates,
                Players = playerStates
            };
            Frames.Add(frame);

            Patches.activations.Clear();
        }
        
        public void HandlePlayback()
        {
            if (!isPlaying) return;

            if (currentPlaybackFrame >= currentReplay.Frames.Length - 1)
            {
                StopReplay();
                return;
            }
            
            elapsedPlaybackTime += Time.deltaTime * playbackSpeed;

            while (currentPlaybackFrame < currentReplay.Frames.Length - 2 &&
                   currentReplay.Frames[currentPlaybackFrame + 1].Time <= elapsedPlaybackTime)
            {
                currentPlaybackFrame++;
            }
            
            Frame a = currentReplay.Frames[currentPlaybackFrame];
            Frame b = currentReplay.Frames[currentPlaybackFrame + 1];

            float span = b.Time - a.Time;
            float t = span > 0f
                ? (elapsedPlaybackTime - a.Time) / span
                : 1f;
            
            ApplyInterpolatedFrame(currentPlaybackFrame, Clamp01(t));

            foreach (var vfx in visualEffects)
            {
                if (vfx == null) continue;
                
                var ve = vfx.GetComponent<VisualEffect>();
                if (ve != null)
                    ve.playRate = playbackSpeed;
            }
        }

        bool TryFire(ReplayEvent evt, int frame)
        {
            if (lastEventFrame.TryGetValue(evt, out int last) && last == frame)
                return false;

            lastEventFrame[evt] = frame;
            return true;
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

                var vfxSize = playbackStructure.name switch
                {
                    "Disc" or "Ball" => 1f,
                    "RockCube" => 1.5f,
                    "Wall" or "Pillar" => 2.5f,
                    
                    "LargeRock" => 3f,

                    _ => 1f
                };
                
                var rb = playbackStructure.GetComponent<Rigidbody>();
                if (!rb.isKinematic)
                    rb.isKinematic = true;
                
                if (playbackStructure.GetComponentInChildren<MeshRenderer>().material.GetFloat("_shake") == 1)
                    playbackStructure.GetComponentInChildren<MeshRenderer>().material.SetFloat("_shake", 0f);
                
                // Event checks
                
                // Structure Broke
                if (sa.active && !sb.active && TryFire(ReplayEvent.StructureBreak, currentPlaybackFrame))
                {
                    var pool = poolManager.GetPool(
                        playbackStructure.name == "Disc" 
                            ? "DustBreakDISC_VFX"
                            : "DustBreak_VFX"
                    );
                
                    PooledMonoBehaviour effect = pool.FetchFromPool(playbackStructure.transform.position, playbackStructure.transform.rotation);
                    visualEffects.Add(effect.GetComponent<PooledVisualEffect>());
                    
                    AudioManager.instance.Play(
                        playbackStructure.GetComponent<Structure>().onDeathAudio, 
                        playbackStructure.transform.position
                    );
                }

                // Structure Spawned
                if (!sa.active && sb.active && TryFire(ReplayEvent.StructureSpawn, currentPlaybackFrame))
                {
                    var pool = poolManager.GetPool("DustSpawn_VFX");

                    var offset = playbackStructure.name is "Ball" or "Disc" ? Vector3.zero : new Vector3(0, 0.5f, 0);
                    if (playbackStructure.name is "Wall")
                        offset = new Vector3(0, -0.5f, 0);
                    
                    PooledMonoBehaviour effect = pool.FetchFromPool(sb.position + offset, Quaternion.identity);
                    visualEffects.Add(effect.GetComponent<PooledVisualEffect>());

                    string sfx = playbackStructure.name switch
                    {
                        "Wall" => "Call_Structure_Spawn_Heavy",
                        "Ball" or "Disc" => "Call_Structure_Spawn_Light",
                        "LargeRock" => "Call_Structure_Spawn_Massive",
                        _ => "Call_Structure_Spawn_Medium"
                    };
                    
                    if (ReplayCache.SFX.TryGetValue(sfx, out var audioCall))
                        AudioManager.instance.Play(audioCall, playbackStructure.transform.position);

                    foreach (var visualEffect in playbackStructure.GetComponentsInChildren<PooledVisualEffect>())
                        GameObject.Destroy(visualEffect.gameObject);
                }
                
                // Grounded
                if (!sa.grounded && sb.grounded && sa.active && TryFire(ReplayEvent.StructureGrounded, currentPlaybackFrame))
                {
                    var pool = poolManager.GetPool("Ground_VFX");
                    
                    var offset = playbackStructure.name is "Ball" or "Disc" ? Vector3.zero : new Vector3(0, -0.5f, 0);
                    PooledMonoBehaviour effect = pool.FetchFromPool(sb.position + offset, Quaternion.identity);
                    visualEffects.Add(effect.GetComponent<PooledVisualEffect>());
                    
                    var structure = playbackStructure.GetComponent<Structure>();
                    structure.processableComponent.SetCurrentState(structure.groundedState);
                }

                // Ungrounded
                if (sa.grounded && !sb.grounded && TryFire(ReplayEvent.StructureUngrounded, currentPlaybackFrame))
                {
                    var structure = playbackStructure.GetComponent<Structure>();
                    structure.processableComponent.SetCurrentState(structure.freeState);
                }
                
                // Hold started
                if (!sa.isHeld && sb.isHeld && TryFire(ReplayEvent.HoldStart, currentPlaybackFrame))
                {
                    var pool = poolManager.GetPool("Hold_VFX");
                    var effect = GameObject.Instantiate(pool.poolItem.gameObject, playbackStructure.transform);
                    
                    effect.transform.localPosition = Vector3.zero;
                    effect.transform.localRotation = Quaternion.identity;
                    effect.transform.localScale = Vector3.one * vfxSize;
                    visualEffects.Add(effect.GetComponent<PooledVisualEffect>());

                    AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Hold"], sb.position);
                }

                // Flick started
                if (!sa.isFlicked && sb.isFlicked && TryFire(ReplayEvent.FlickStart, currentPlaybackFrame))
                {
                    var pool = poolManager.GetPool("Flick_VFX");
                    var effect = GameObject.Instantiate(pool.poolItem.gameObject, playbackStructure.transform);

                    effect.transform.localPosition = Vector3.zero;
                    effect.transform.localRotation = Quaternion.identity;
                    effect.transform.localScale = Vector3.one * vfxSize;

                    visualEffects.Add(effect.GetComponent<PooledVisualEffect>());
                    
                    AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Flick"], sb.position);
                }

                // Hold ended
                if (sa.isHeld && !sb.isHeld && TryFire(ReplayEvent.HoldEnd, currentPlaybackFrame))
                {
                    var toRemove = new List<PooledVisualEffect>();

                    foreach (var vfx in visualEffects)
                    {
                        if (vfx == null)
                            continue;

                        if (vfx.name == "Hold_VFX" && vfx.transform.parent == playbackStructure.transform)
                            toRemove.Add(vfx);
                    }

                    foreach (var vfx in toRemove)
                    {
                        GameObject.Destroy(vfx);
                        visualEffects.Remove(vfx);
                    }
                }

                // Flick ended
                if (sa.isFlicked && !sb.isFlicked && TryFire(ReplayEvent.FlickEnd, currentPlaybackFrame))
                {
                    var toRemove = new List<PooledVisualEffect>();

                    foreach (var vfx in visualEffects)
                    {
                        if (vfx == null)
                            continue;

                        if (vfx.name == "Flick_VFX" && vfx.transform.parent == playbackStructure.transform)
                            toRemove.Add(vfx);
                    }

                    foreach (var vfx in toRemove)
                    {
                        GameObject.Destroy(vfx);
                        visualEffects.Remove(vfx);
                    }
                }

                foreach (var visualEffect in playbackStructure.GetComponentsInChildren<PooledVisualEffect>())
                    visualEffects.Add(visualEffect);

                if (!sb.active)
                {
                    playbackStructure.SetActive(false);
                    continue;
                }
                
                if (!playbackStructure.activeSelf)
                    playbackStructure.SetActive(true);

                Vector3 pos = Vector3.Lerp(sa.position, sb.position, t);
                Quaternion rot = Quaternion.Slerp(sa.rotation, sb.rotation, t);

                playbackStructure.transform.SetPositionAndRotation(pos, rot);
            }

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
                    if (pb.Health < pa.Health && pb.Health != 0 && TryFire(ReplayEvent.PlayerHit, currentPlaybackFrame))
                    {
                        var hitmarker = PoolManager.instance.GetPool("PlayerHitmarker")
                            .FetchFromPool(playbackPlayer.Head.transform.position - new Vector3(0, 0.5f, 0), Quaternion.identity)
                            .Cast<PlayerHitmarker>();

                        hitmarker.SetDamage(pa.Health - pb.Health);
                        hitmarker.gameObject.SetActive(true);
                        hitmarker.Play();
                        hitmarker.GetComponent<VisualEffect>().playRate = playbackSpeed;
                        
                        visualEffects.Add(hitmarker.GetComponent<PooledVisualEffect>());
                    }
                }
                
                if (pb.currentStack == (short)StackType.None && pa.currentStack != (short)StackType.None && TryFire(ReplayEvent.StackExit, currentPlaybackFrame))
                {
                    if (pa.currentStack != (short)StackType.Flick
                        && pa.currentStack != (short)StackType.HoldLeft
                        && pa.currentStack != (short)StackType.HoldRight)
                    {
                        var key = ReplayCache.NameToStackType
                            .FirstOrDefault(s => s.Value == (StackType)pa.currentStack);

                        var stack = playbackPlayer.Controller
                            .GetSubsystem<PlayerStackProcessor>()
                            .availableStacks
                            .ToArray()
                            .FirstOrDefault(s => s.CachedName == key.Key);

                        playbackPlayer.Controller
                            .GetSubsystem<PlayerStackProcessor>()
                            .Execute(stack);
                    }
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

[RegisterTypeInIl2Cpp]
public class TableFloat : MonoBehaviour
{
    public float amplitude = 0.25f;
    public float speed = 1f;

    public Vector3 startPos;
    public float targetY;

    void Awake()
    {
        startPos = transform.localPosition;
    }

    void Update()
    {
        startPos.y = Lerp(startPos.y, targetY + (float)Main.instance.tableOffset.SavedValue, Time.deltaTime * 4f);
        
        float y = Sin(Time.time * speed) * amplitude;
        transform.localPosition = startPos + Vector3.up * y;
    }
}

[RegisterTypeInIl2Cpp]
public class ReplayTable : MonoBehaviour
{
    public GameObject TableRoot;
    public TableFloat tableFloat;
    public TableFloat metadataTextFloat;

    public InteractionButton nextButton;
    public InteractionButton previousButton;
    public InteractionButton loadButton;

    public TextMeshPro replayNameText;
    public TextMeshPro indexText;
    public TextMeshPro metadataText;
    
    public float desiredTableHeight = 1.5481f;
    public float desiredMetadataTextHeight = 1.8513f;

    public void Start()
    {
        tableFloat = TableRoot.GetComponent<TableFloat>();
        metadataTextFloat = metadataText.GetComponent<TableFloat>();
    }

    public void Update()
    {
        if (Main.instance.currentScene != "Loader" && PlayerManager.instance.localPlayer != null)
        {
            float playerArmspan = PlayerManager.instance.localPlayer.Data.PlayerMeasurement.ArmSpan;
        
            if (tableFloat != null)
                tableFloat.targetY = playerArmspan * (desiredTableHeight / Main.errorsArmspan);
        
            if (metadataTextFloat != null)
                metadataTextFloat.targetY = playerArmspan * (desiredMetadataTextHeight / Main.errorsArmspan);
        }
    }
}

[RegisterTypeInIl2Cpp]
public class LookAtPlayer : MonoBehaviour
{
    public Transform playerHeadset;

    public void Update()
    {
        if (playerHeadset == null)
            return;
        
        var rotation = Quaternion.LookRotation(transform.position - playerHeadset.position);

        transform.localRotation = new Quaternion(transform.localRotation.x, rotation.y, transform.localRotation.z, transform.localRotation.w);
    }
}