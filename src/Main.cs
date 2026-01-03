using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Environment.MatchFlow;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Networking;
using Il2CppRUMBLE.Networking.MatchFlow;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppRUMBLE.Utilities;
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
using ReplayCrystals = RumbleAnimator.ReplayGlobals.ReplayCrystals;

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
        // Runtime
        public static Main instance;
        public string currentScene = "Loader";
        
        // Replay control
        public static ReplayInfo currentReplay;
        public static bool isPlaying = false;
        public static float playbackSpeed = 1f;
        
        // Local player transforms
        public Transform leftHand;
        public Transform rightHand;
        public Transform head;
        
        
        
        // ------ Recording ------
        public static bool isRecording = false;
        
        public static float elapsedRecordingTime = 0f;
        private static float lastRecordedFrameTime = 0f;
        
        public List<Frame> Frames = new();
        public List<EventChunk> Events = new();
        
        // World
        public List<Structure> Structures = new();
        public List<GameObject> Pedestals = new();

        // Renderer state (recording)
        public List<(MeshRenderer renderer, MaterialPropertyBlock mpb)> structureRenderers = new();

        // Players
        public List<Player> RecordedPlayers = new();
        public Dictionary<string, int> MasterIdToIndex = new();
        public List<PlayerInfo> PlayerInfos = new();
        
        // Recording FX / timers
        public GameObject clapperboardVFX;
        public bool hasPlayed;
        public float heldTime = 0f;
        public float soundTimer = 0f;
        
        // Replay Buffer
        public Queue<Frame> replayBuffer = new();
        
        public static bool isBuffering = false;
        
        public static float elapsedBufferTime = 0f;
        private static float lastBufferFrameTime = 0f;

        public float lastTriggerTime = 0f;
        
        // ------ Playback ------
        
        public static float elapsedPlaybackTime = 0f;
        public static int currentPlaybackFrame = 0;
        
        // Roots
        public GameObject ReplayRoot;
        public GameObject replayStructures;
        public GameObject replayPlayers;
        public GameObject pedestalsParent;
        public GameObject VFXParent;
        
        // Structures
        public GameObject[] PlaybackStructures;
        public HashSet<Structure> HiddenStructures = new();
        public (MeshRenderer renderer, MaterialPropertyBlock mpb)[] playbackStructureRenderers;
        public PlaybackStructureState[] playbackStructureStates;
        
        // Players
        public Clone[] PlaybackPlayers;
        public PlaybackPlayerState[] playbackPlayerStates;
        public Clone povPlayer;

        // Pedestals
        public List<GameObject> replayPedestals = new();
        public PlaybackPedestalState[] playbackPedestalStates;
        
        // Events
        public int lastEventFrame = -1;
        
        // UI
        public ReplayTable replayTable;
        
        // Controls
        public GameObject playbackControls;
        public Material timelineMaterial;
        public TextMeshPro totalDuration;
        public TextMeshPro currentDuration;
        
        // ------ Settings ------
        
        public const float errorsArmspan = 1.2744f;
        
        private Mod rumbleAnimatorMod = new();

        public ModSetting<int> TargetRecordingFPS = new();

        public ModSetting<bool> AutoRecordMatches = new();
        public ModSetting<bool> AutoRecordParks = new();
        public ModSetting<float> tableOffset = new();

        public ModSetting<bool> ReplayBufferEnabled = new();
        public ModSetting<int> ReplayBufferDuration = new();

        public ModSetting<string> ReplayBufferTriggerSide = new();

        public ModSetting<bool> EnableHaptics = new();
        
        // ------------
        
        

        public Main() { instance = this; }

        public override void OnLateInitializeMelon()
        {
            UI.instance.UI_Initialized += OnUIInitialized;
            Calls.onMatchEnded += () => { if (isRecording) StopRecording(); };
            Calls.onMapInitialized += OnMapInitialized;

            ReplayFiles.Init();
        }

        public override void OnApplicationQuit()
        {
            if (isRecording)
                StopRecording();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            currentScene = sceneName;
            
            if (isRecording)
                StopRecording();

            if (isPlaying)
                StopReplay();

            replayBuffer.Clear();
            elapsedBufferTime = 0f;
            lastBufferFrameTime = 0f;
        }

        public void OnMapInitialized()
        {
            if ((currentScene is "Map0" or "Map1" && (bool)AutoRecordMatches.SavedValue && PlayerManager.instance.AllPlayers.Count > 1) || (currentScene == "Park" && (bool)AutoRecordParks.SavedValue))
                StartRecording();

            if ((ReplayCache.SFX == null || ReplayCache.structurePools == null) && currentScene != "Loader")
                ReplayCache.BuildCacheTables();

            if (replayTable == null && clapperboardVFX == null)
                LoadReplayObjects();

            if (ReplayCrystals.crystalParent == null)
                ReplayCrystals.crystalParent = new GameObject("Crystals");
            
            if (currentScene == "Gym")
            {
                replayTable.TableRoot.SetActive(true);
                replayTable.metadataText.gameObject.SetActive(true);
                
                ReplayCrystals.LoadCrystals();
                ReplayFiles.ReloadReplays();
            }
            else
            {
                replayTable.TableRoot.SetActive(false);
                replayTable.metadataText.gameObject.SetActive(false);
            }
            
            if (currentScene != "Loader")
                StartBuffering();

            var vr = PlayerManager.instance.localPlayer.Controller.transform.GetChild(2);
            replayTable.metadataText.GetComponent<LookAtPlayer>().lockX = true;
            replayTable.metadataText.GetComponent<LookAtPlayer>().lockZ = true;

            leftHand = vr.GetChild(1);
            rightHand = vr.GetChild(2);
            head = vr.GetChild(0).GetChild(0);
        }

        public void OnUIInitialized()
        {
            rumbleAnimatorMod.ModName = BuildInfo.Name;
            rumbleAnimatorMod.ModVersion = BuildInfo.Version;

            rumbleAnimatorMod.SetFolder("MatchReplays");
            rumbleAnimatorMod.AddDescription("Description", "", "A mod that records scenes into a 3d file that you can replay", new Tags { IsSummary = true });

            TargetRecordingFPS = rumbleAnimatorMod.AddToList("Target Recording FPS", 50, "The target FPS that it tries to record at.\nThis is limited by how fast the game runs.", new Tags());
            
            AutoRecordMatches = rumbleAnimatorMod.AddToList("Auto Record Matches", true, 0, "Automatically start recordings in matches.", new Tags());
            AutoRecordParks = rumbleAnimatorMod.AddToList("Auto Record Parks", false, 0, "Automatically start recordings when you join a park.", new Tags());

            tableOffset = rumbleAnimatorMod.AddToList("Table Offset", 0f, "Table offset in meters.\nThe table does move with your scale, but the default might feel too low for some people.", new Tags());

            ReplayBufferEnabled = rumbleAnimatorMod.AddToList("Enable Replay Buffer", false, 0, "Toggles the replay buffer.", new Tags());
            ReplayBufferDuration = rumbleAnimatorMod.AddToList("Replay Buffer Duration", 30, "The length of the replay buffer in seconds.", new Tags());

            ReplayBufferTriggerSide = rumbleAnimatorMod.AddToList("Replay Buffer Trigger Side", "Disabled",
                "Which controller should trigger the replay buffer save when both buttons are pressed.\n" +
                "Possible values:\n" +
                "- Left\n" +
                "- Right\n" +
                "- Disabled", new Tags());

            ReplayBufferEnabled.SavedValueChanged += (obj, sender) =>
            {
                if ((bool)ReplayBufferEnabled.SavedValue && !isBuffering)
                    StartBuffering();
                
                isBuffering = (bool)ReplayBufferEnabled.SavedValue;
            };
            
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

            var tableFloat = ReplayTable.AddComponent<TableFloat>();
            tableFloat.speed = (2 * PI) / 10f;
            tableFloat.amplitude = 0.01f;
            
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
            textTableFloat.stopRadius = 0f;
            
            var metadataTMP = metadataText.GetComponent<TextMeshPro>();
            metadataTMP.m_HorizontalAlignment = HorizontalAlignmentOptions.Center;

            var lookAt = metadataText.AddComponent<LookAtPlayer>();
            lookAt.lockX = true;
            lookAt.lockZ = true;
            
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
            
            loadReplayButtonComp.OnPressed.AddListener((UnityAction)(() => { LoadSelectedReplay(); }));
            
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

            GameObject vfx = GameObject.Instantiate(PoolManager.instance.GetPool("Stubbornstone_VFX").poolItem.gameObject, ReplayTable.transform);

            vfx.name = "Crystalize VFX";
            vfx.transform.localScale = Vector3.one * 0.2f;
            vfx.transform.localPosition = new Vector3(0, 0, 0.3045f);
            vfx.SetActive(true);

            VisualEffect vfxComp = vfx.GetComponent<VisualEffect>();
            vfxComp.playRate = 0.6f;
            
            ReplayCrystals.crystalizeVFX = vfxComp;

            GameObject crystalPrefab = GameObject.Instantiate(bundle.LoadAsset<GameObject>("Crystal"));
            
            crystalPrefab.transform.localScale *= 0.5f;
            crystalPrefab.GetComponent<Renderer>().material = PoolManager.instance.GetPool("FlowStone").PoolItem.transform.GetChild(0).GetComponent<Renderer>().material;
            crystalPrefab.SetActive(false);

            ReplayCrystals.crystalPrefab = crystalPrefab;

            var crystalizeButton = GameObject.Instantiate(loadReplayButton, ReplayTable.transform);

            crystalizeButton.name = "CrystalizeReplay";
            crystalizeButton.transform.localPosition = new Vector3(0.21f, -0.4484f, -0.1325f);
            crystalizeButton.transform.localScale = Vector3.one * 1.1f;
            crystalizeButton.transform.localRotation = Quaternion.Euler(303.8364f, 249f, 108.4483f);
            
            crystalizeButton.transform.GetChild(1).transform.localScale = Vector3.one * 0.1f;
            
            var crystalizeButtonComp = crystalizeButton.transform.GetChild(0).GetComponent<InteractionButton>();
            crystalizeButtonComp.enabled = true;
            crystalizeButtonComp.OnPressed.RemoveAllListeners();
            
            crystalizeButtonComp.onPressedAudioCall = loadReplayButtonComp.onPressedAudioCall;
            
            ReplayCrystals.crystalParent = new GameObject("Crystals");
            
            crystalizeButtonComp.OnPressed.AddListener((UnityAction)(() =>
            {
                if (!ReplayCrystals.Crystals.Any(c => c != null && c.ReplayPath == ReplayFiles.currentReplayPath) && ReplayFiles.currentHeader != null)
                {
                    AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_Bake_Part"], crystalizeButton.transform.position);
                    
                    var header = ReplayFiles.currentHeader;
                    ReplayCrystals.CreateCrystal(replayTable.transform.position + new Vector3(0, 0.3f, 0), header, true);
                }
                else
                {
                    AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Failure"], crystalizeButton.transform.position);
                }
            }));
            
            // Playback Controls

            playbackControls = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.School.LogoSlab.NotificationSlab.SlabbuddyInfovariant.InfoForm.GetGameObject());
            playbackControls.name = "Playback Controls";
            playbackControls.transform.localScale = Vector3.one * 1.3f;
            
            for (int i = 0; i < playbackControls.transform.GetChild(1).childCount; i++)
                GameObject.Destroy(playbackControls.transform.GetChild(1).GetChild(i).gameObject);

            GameObject timeline = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Gearmarket.Itemhighlightwindow.StatusBar.GetGameObject(), 
                playbackControls.transform.GetChild(1));
            timelineMaterial = timeline.GetComponent<MeshRenderer>().material;
            timelineMaterial.SetFloat("_Has_BP_Requirement", 1f);
            timelineMaterial.SetFloat("_Has_RC_Requirement", 0f);

            timeline.name = "Timeline";
            timeline.transform.localPosition = new Vector3(0, -0.1091f, 0);
            timeline.transform.localScale = new Vector3(1.0715f, 0.0434f, 1);
            timeline.transform.localRotation = Quaternion.identity;
            timeline.SetActive(true);

            currentDuration = Calls.Create.NewText(":3", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
            totalDuration = Calls.Create.NewText(":3", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
            
            currentDuration.transform.SetParent(playbackControls.transform.GetChild(1));
            currentDuration.name = "Current Duration";

            currentDuration.transform.localScale = Vector3.one * 0.8f;
            currentDuration.transform.localPosition = new Vector3(-0.2633f, -0.1792f, 0);
            currentDuration.transform.localRotation = Quaternion.identity;
            
            totalDuration.transform.SetParent(playbackControls.transform.GetChild(1));
            totalDuration.name = "Total Duration";

            totalDuration.transform.localScale = Vector3.one * 0.8f;
            totalDuration.transform.localPosition = new Vector3(0.696f, -0.1792f, 0);
            totalDuration.transform.localRotation = Quaternion.identity;
            
            GameObject exitMatchButton = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.CyclePoseButton.GetGameObject(), 
                playbackControls.transform.GetChild(1));
            
            exitMatchButton.name = "Exit Match Button";
            exitMatchButton.transform.localScale = Vector3.one * 2f;
            exitMatchButton.transform.localPosition = new Vector3(0, -0.633f, 0);
            exitMatchButton.transform.localRotation = Quaternion.identity;
            
            exitMatchButton.transform.GetChild(1).GetComponent<TextMeshPro>().text = "Exit Replay";
            exitMatchButton.transform.GetChild(1).GetComponent<TextMeshPro>().ForceMeshUpdate();
            
            InteractionButton button = exitMatchButton.transform.GetChild(0).GetComponent<InteractionButton>();
            button.OnPressed.RemoveAllListeners();
            button.OnPressed.AddListener((UnityAction)(() => { MelonCoroutines.Start(Utilities.LoadMap(1)); }));
            
            GameObject.DontDestroyOnLoad(ReplayTable);
            GameObject.DontDestroyOnLoad(crystalPrefab);
            GameObject.DontDestroyOnLoad(playbackControls);
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

        public void SetupRecordingData()
        {
            RecordedPlayers.Clear();
            Structures.Clear();
            MasterIdToIndex.Clear();
            PlayerInfos.Clear();
            Pedestals.Clear();
            structureRenderers.Clear();
            
            foreach (var structure in CombatManager.instance.structures)
            {
                if (structure == null || !structure.TryGetComponent(out Structure _) ||
                    structure.name.StartsWith("Static Target") || structure.name.StartsWith("Moving Target"))
                    continue;

                Structures.Add(structure);
                var renderer = structure.GetComponentInChildren<MeshRenderer>();
                var mpb = new MaterialPropertyBlock();
                structureRenderers.Add((renderer, mpb));
            }

            foreach (var player in PlayerManager.instance.AllPlayers)
            {
                if (player == null) continue;

                RecordedPlayers.Add(player);

                var info = new PlayerInfo
                {
                    ActorId = (byte)player.Data.GeneralData.ActorNo,
                    MasterId = player.Data.GeneralData.PlayFabMasterId,
                    Name = player.Data.GeneralData.PublicUsername,
                    BattlePoints = player.Data.GeneralData.BattlePoints,
                    VisualData = player.Data.VisualData.ToPlayfabDataString(),
                    EquippedShiftStones = player.Data.EquipedShiftStones.ToArray(),
                    Measurement = player.Data.PlayerMeasurement,
                    WasHost = (player.Data.GeneralData.ActorNo == PhotonNetwork.MasterClient?.ActorNumber)
                };

                PlayerInfos.Add(info);
            }

            Pedestals.AddRange(Utilities.EnumerateMatchPedestals());
        }

        public void StartRecording()
        {
            SetupRecordingData();
            Frames.Clear();
            Events.Clear();
            elapsedRecordingTime = 0f;
            lastRecordedFrameTime = 0f;
            isRecording = true;
        }

        public void StartBuffering()
        {
            SetupRecordingData();
            replayBuffer.Clear();
            elapsedBufferTime = 0f;
            lastBufferFrameTime = 0f;
            isBuffering = true;
        }
        
        public void SaveReplay(Frame[] frames, string logPrefix, bool isBufferClip = false)
        {
            if (frames.Length == 0)
            {
                LoggerInstance.Warning($"{logPrefix} stopped, but no frames were captured. Replay was not saved.");
                return;
            }

            float duration = frames[^1].Time - frames[0].Time;

            var validStructures = new List<StructureInfo>();

            foreach (var s in Structures)
            {
                if (s == null) continue;

                var name = s.resourceName;

                validStructures.Add(new StructureInfo
                {
                    Type = name switch
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

            string customMap = Utilities.GetActiveCustomMapName();
            string sceneName = string.IsNullOrWhiteSpace(customMap)
                ? Utilities.GetFriendlySceneName(currentScene)
                : customMap;

            var replayInfo = new ReplayInfo
            {
                Header = new ReplaySerializer.ReplayHeader
                {
                    Version = BuildInfo.Version,
                    Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Scene = currentScene,
                    Duration = duration,
                    CustomMap = customMap,
                    FrameCount = frames.Length,
                    PedestalCount = Pedestals.Count,
                    TargetFPS = (int)TargetRecordingFPS.SavedValue,
                    Players = PlayerInfos.ToArray(),
                    Structures = validStructures.ToArray(),
                },
                Frames = frames
            };
            
            string pattern = sceneName switch
            {
                "Gym" => ReplayFiles.LoadFormatFile("AutoNameFormats/gym"),
                "Park" => ReplayFiles.LoadFormatFile("AutoNameFormats/park"),
                "Pit" or "Ring" => ReplayFiles.LoadFormatFile("AutoNameFormats/match"),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(pattern))
            {
                LoggerInstance.Error($"{logPrefix} was not saved due to missing name format file.");
                return;
            }

            replayInfo.Header.Title = ReplaySerializer.FormatReplayString(pattern, replayInfo.Header);
            
            LoggerInstance.Msg($"{logPrefix} saved after {duration:F2}s ({frames.Length} frames, avg FPS: {(frames.Length / duration):F2})");

            string path = $"{ReplayFiles.replayFolder}/{(isBufferClip ? "Clip_" : "")}{Utilities.GetReplayName(replayInfo)}";
            ReplaySerializer.BuildReplayPackage(
                path,
                replayInfo,
                () =>
                {
                    AudioManager.instance.Play(ReplayCache.SFX["Call_PoseGhost_PosePerformed"], 
                        PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerVR>().transform.position);
                    LoggerInstance.Msg($"{logPrefix} saved to disk: '{path}'");

                    ReplayFiles.ReloadReplays();
                }
            );
        }

        public void StopRecording()
        {
            isRecording = false;
            SaveReplay(Frames.ToArray(), "Recording");
        }

        public void SaveReplayBuffer()
        {
            var frames = replayBuffer.ToArray();
            float offsetTime = frames[0].Time;

            foreach (var t in frames)
                t.Time -= offsetTime;

            SaveReplay(frames, "Replay Buffer", true);
        }

        public void LoadSelectedReplay()
        {
            if (ReplayFiles.currentIndex != -1)
            {
                if ((ReplayFiles.currentHeader.Scene == "Map0" && currentScene != "Map0") || (ReplayFiles.currentHeader.Scene == "Map1" && currentScene != "Map1"))
                {
                    GameObject customMaps = GameObject.Find("CustomMultiplayerMaps");
                    bool isCustomMap = !string.IsNullOrEmpty(ReplayFiles.currentHeader.CustomMap);

                    GameObject replayCustomMap = null;

                    if (isCustomMap)
                    {
                        if (customMaps == null)
                        {
                            AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Failure"], replayTable.loadButton.transform.position);
                            LoggerInstance.Error("Selected replay uses a custom map, but custom maps are not installed. Please make sure you have the custom maps mod before loading this replay.");
                            return;
                        }

                        replayCustomMap = customMaps.transform.Find(ReplayFiles.currentHeader.CustomMap)?.gameObject;

                        if (replayCustomMap == null)
                        {
                            AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Failure"], replayTable.loadButton.transform.position);
                            LoggerInstance.Error($"Could not find the custom map '{ReplayFiles.currentHeader.CustomMap}'. Please check whether it's installed.");
                            return;
                        }
                    }
                    
                    ReplayFiles.HideMetadata();
                    MelonCoroutines.Start(Utilities.LoadMap(ReplayFiles.currentHeader.Scene == "Map0" ? 3 : 4, 2.5f, () =>
                    {
                        LoadReplay(ReplayFiles.currentReplayPath);
                        ReplayFiles.ShowMetadata();

                        if (ReplayFiles.currentHeader.Scene == "Map1")
                            Calls.GameObjects.Map1.Logic.SceneProcessors.GetGameObject().SetActive(false);

                        if (isCustomMap && replayCustomMap != null)
                        {
                            replayCustomMap.SetActive(true);
                            
                            if (ReplayFiles.currentHeader.Scene == "Map0")
                                Calls.GameObjects.Map0.Map0production.GetGameObject().SetActive(false);
                            else if (ReplayFiles.currentHeader.Scene == "Map1")
                                Calls.GameObjects.Map1.Map1production.GetGameObject().SetActive(false);
                        }
                    }));
                }
                else if (ReplayFiles.currentHeader.Scene != "Park")
                {
                    LoadReplay(ReplayFiles.currentReplayPath);
                }
            }
            else
            {
                AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Failure"], replayTable.loadButton.transform.position);
                LoggerInstance.Error("Could not find file");
            }
        }
        
        public void LoadReplay(string path)
        {
            if (currentScene == "Park" && (PhotonNetwork.CurrentRoom?.PlayerCount > 1 || (PhotonNetwork.CurrentRoom?.IsVisible ?? false))) // Temporarily 
                return;
            
            currentReplay = ReplaySerializer.LoadReplay(path);
            
            ReplayRoot = new GameObject("Replay Root");
            pedestalsParent = new GameObject("Pedestals");
            replayPlayers = new GameObject("Replay Players");
            
            VFXParent = new GameObject("Replay VFX");
            VFXParent.transform.SetParent(ReplayRoot.transform);
            
            elapsedPlaybackTime = 0f;
            currentPlaybackFrame = 0;
            
            // ------ Structures ------
            
            if (replayStructures != null) GameObject.Destroy(replayStructures);
            HiddenStructures.Clear();
            PlaybackStructures = null;
            foreach (var structure in CombatManager.instance.structures)
            {
                if (structure == null) continue; 
                
                structure.gameObject.SetActive(false);
                HiddenStructures.Add(structure);
            }

            PlaybackStructures = new GameObject[currentReplay.Header.Structures.Length];
            playbackStructureRenderers = new (MeshRenderer renderer, MaterialPropertyBlock mpb)[PlaybackStructures.Length];
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

                playbackStructureRenderers[i] = (PlaybackStructures[i].GetComponentInChildren<MeshRenderer>(), new MaterialPropertyBlock());
            }

            playbackStructureStates = new PlaybackStructureState[PlaybackStructures.Length];

            for (int i = 0; i < PlaybackStructures.Length; i++)
            {
                var structure = PlaybackStructures[i];
                var name = structure.name;

                bool grounded = name switch
                {
                    "Ball" => false,
                    "Disc" => false,
                    _ => true
                };

                playbackStructureStates[i] = new PlaybackStructureState
                {
                    grounded = grounded
                };
            }
            
            // ------ Players ------

            if (PlaybackPlayers != null)
            {
                foreach (var player in PlaybackPlayers)
                    PlayerManager.instance.AllPlayers.Remove(player.Controller.assignedPlayer);
            }
            
            PlaybackPlayers = null;
            
            // ------ Pedestals ------
            
            replayPedestals.Clear();
            
            foreach (var pedestal in Utilities.EnumerateMatchPedestals())
            {
                pedestal.transform.SetParent(pedestalsParent.transform);
                replayPedestals.Add(pedestal);
                
                if (currentReplay.Header.PedestalCount == 0)
                    pedestal.SetActive(false);
            }
            
            playbackPedestalStates = new PlaybackPedestalState[replayPedestals.Count];
            
            // --------------
            
            MelonCoroutines.Start(SpawnClones(() =>
            {
                replayStructures.transform.SetParent(ReplayRoot.transform);
                replayPlayers.transform.SetParent(ReplayRoot.transform);
                pedestalsParent.transform.SetParent(ReplayRoot.transform);
                
                playbackPlayerStates = new PlaybackPlayerState[PlaybackPlayers.Length];
                
                ReorderPlayers();
                
                isPlaying = true;
            }));

            if (currentReplay.Header.Scene is "Map0" or "Map1")
            {
                MelonCoroutines.Start(DoMatchStart());

                IEnumerator DoMatchStart()
                {
                    MatchHandler.instance.DoStartCountdown();

                    float elapsed = 0f;
                    const float duration = 10f;

                    while (elapsed < duration)
                    {
                        elapsed += Time.deltaTime * playbackSpeed;
                        yield return null;
                    }
                    
                    foreach (var player in PlaybackPlayers)
                        player.Controller.GetSubsystem<PlayerNameTag>().FadePlayerNameTag(false);
                    
                    // MatchHandler.instance.FadeInCombatMusic();
                }
            }
            
            // Playback Controls
            timelineMaterial.SetFloat("_BP_Target", currentReplay.Header.Duration * 1000f);
            timelineMaterial.SetFloat("_BP_Current", 0f);

            TimeSpan t = TimeSpan.FromSeconds(currentReplay.Header.Duration);
            totalDuration.text = $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
            currentDuration.text = "0:00";
        }

        public void ReorderPlayers()
        {
            var all = PlayerManager.instance.AllPlayers;
            if (all == null || all.Count == 0)
                return;

            var wasHostById = new Dictionary<string, bool>();
            foreach (var p in currentReplay.Header.Players)
                wasHostById[p.MasterId] = p.WasHost;

            Player host = null;
            Player local = PlayerManager.instance.localPlayer;
            var middle = new List<Player>();

            foreach (var p in all)
            {
                if (p == local)
                    continue;

                if (wasHostById.TryGetValue(p.Data.GeneralData.PlayFabMasterId, out bool wasHost) && wasHost)
                    host = p;
                else
                    middle.Add(p);
            }

            all.Clear();

            if (host != null)
                all.Add(host);

            foreach (var p in middle)
                all.Add(p);

            if (local != null)
                all.Add(local);
        }
        
        private IEnumerator SpawnClones(Action done = null)
        {
            PlaybackPlayers = new Clone[currentReplay.Header.Players.Length];

            for (int i = 0; i < PlaybackPlayers.Length; i++)
            {
                Clone temp = null;
                yield return BuildClone(currentReplay.Header.Players[i], c => temp = c);

                while (temp == null)
                    yield return null;

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

            var clone = newPlayer.Controller.gameObject.AddComponent<Clone>();

            clone.VRRig = Overall;
            clone.LeftHand = LHand;
            clone.RightHand = RHand;
            clone.Head = Head;
            clone.Controller = newPlayer.Controller;
            
            Callback.Invoke(clone);
        }

        // We want to be very defensive here
        // even if anything is null, we want to stop what we can
        public void StopReplay()
        {
            isPlaying = false;

            foreach (var structure in PlaybackStructures)
            {
                if (structure == null)
                    continue;

                var comp = structure.GetComponent<Structure>();
                comp.indistructable = false;
                
                foreach (var effect in structure.GetComponentsInChildren<VisualEffect>())
                    GameObject.Destroy(effect.gameObject);
            }

            if (replayStructures != null)
                GameObject.Destroy(replayStructures);

            if (replayPlayers != null)
                GameObject.Destroy(replayPlayers);

            if (pedestalsParent != null)
                GameObject.Destroy(pedestalsParent);

            if (VFXParent != null)
                GameObject.Destroy(VFXParent);

            if (HiddenStructures != null)
            {
                foreach (var structure in HiddenStructures)
                {
                    if (structure != null)
                        structure.gameObject.SetActive(true);
                }

                HiddenStructures.Clear();
            }

            if (PlaybackPlayers != null && PlayerManager.instance != null)
            {
                foreach (var player in PlaybackPlayers)
                {
                    var assigned = player?.Controller?.assignedPlayer;
                    if (assigned != null)
                        PlayerManager.instance.AllPlayers.Remove(assigned);
                }
            }

            if (currentReplay?.Header?.Scene is "Map0" or "Map1")
                MatchHandler.instance?.FadeOutCombatMusic();

            CombatManager.instance?.CleanStructureList();

            UpdateReplayCameraPOV(-1);

            PlaybackPlayers = null;
            PlaybackStructures = null;
            replayStructures = null;
            replayPlayers = null;
            pedestalsParent = null;

            elapsedPlaybackTime = 0f;
            currentPlaybackFrame = 0;
        }

        public override void OnUpdate()
        {
            HandleReplayPose();
            
            if (currentScene == "Gym")
                ReplayCrystals.HandleCrystals();
        }

        public override void OnLateUpdate()
        {
            HandleRecording();
            HandlePlayback();
        }
        

        public override void OnFixedUpdate()
        {
            if (currentScene == "Loader") return;
            
            string triggerSetting = (string)ReplayBufferTriggerSide.SavedValue;
            bool hapticsEnabled = (bool)EnableHaptics.SavedValue;
            
            if (triggerSetting.Equals("Left", StringComparison.OrdinalIgnoreCase) &&
                Calls.ControllerMap.LeftController.GetPrimary() > 0 &&
                Calls.ControllerMap.LeftController.GetSecondary() > 0 &&
                Time.time - lastTriggerTime > 1f)
            {
                lastTriggerTime = Time.time;
                SaveReplayBuffer();

                if (hapticsEnabled)
                    PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(0.6f, 0.15f, 0f, 0f);
            }
            else if (triggerSetting.Equals("Right", StringComparison.OrdinalIgnoreCase) &&
                 Calls.ControllerMap.RightController.GetPrimary() > 0 &&
                 Calls.ControllerMap.RightController.GetSecondary() > 0 &&
                 Time.time - lastTriggerTime > 1f)
            {
                lastTriggerTime = Time.time;
                SaveReplayBuffer();

                if (hapticsEnabled)
                    PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(0f, 0f, 0.6f, 0.15f);
            }
        }

        public void HandleReplayPose()
        {
            if (currentScene == "Loader")
                return;

            if (leftHand == null || rightHand == null || head == null)
                return;

            bool pose = IsFramePose(leftHand, rightHand);

            if (pose &&
                Calls.ControllerMap.LeftController.GetGrip() > 0.8f &&
                Calls.ControllerMap.RightController.GetGrip() > 0.8f &&
                Calls.ControllerMap.LeftController.GetTrigger() < 0.5f &&
                Calls.ControllerMap.RightController.GetTrigger() < 0.5f)
            {
                heldTime += Time.deltaTime;
                soundTimer += Time.deltaTime;

                if (soundTimer >= 0.5f && !hasPlayed)
                {
                    soundTimer -= 0.5f;
                    AudioManager.instance.Play(
                        ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"],
                        head.position
                    );
                }

                if (heldTime >= 2f && !hasPlayed)
                {
                    hasPlayed = true;

                    PlayClapperboardVFX(
                        head.position + head.forward * 1.2f + new Vector3(0f, -0.1f, 0f),
                        Quaternion.Euler(270f, head.eulerAngles.y + 180f, 0f)
                    );

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

        bool IsFramePose(Transform handA, Transform handB)
        {
            Vector3 headRight = head.right;

            Vector3 fingerA = handA.forward;
            Vector3 thumbA  = handA.up;
            Vector3 palmA   = handA.right;

            Vector3 fingerB = handB.forward;
            Vector3 thumbB  = handB.up;
            Vector3 palmB   = -handB.right;

            Vector3 toHeadA = (head.position - handA.position).normalized;
            Vector3 toHeadB = (head.position - handB.position).normalized;

            float dotSide = Vector3.Dot(fingerA, headRight);
            bool A_pointsSideways = Abs(dotSide) > 0.7f;

            float dotPalmHeadA = Vector3.Dot(palmA, toHeadA);
            bool A_palmToHead = dotPalmHeadA > 0.6f;

            float dotThumbUpA = Vector3.Dot(thumbA, Vector3.up);
            bool A_thumbUp = dotThumbUpA > 0.6f;

            float dotOpposite = Vector3.Dot(fingerA, fingerB);
            bool B_pointsOpposite = dotOpposite < -0.7f;

            float dotPalmHeadB = Vector3.Dot(palmB, toHeadB);
            bool B_palmAwayFromHead = dotPalmHeadB < -0.6f;

            float dotThumbDownB = Vector3.Dot(thumbB, Vector3.up);
            bool B_thumbDown = dotThumbDownB < -0.6f;

            float dist = Vector3.Distance(handA.position, handB.position);
            float maxDist =
                PlayerManager.instance.localPlayer.Data.PlayerMeasurement.ArmSpan
                * (0.30f / errorsArmspan);

            bool closeEnough = dist < maxDist;

            return
                A_pointsSideways &&
                A_palmToHead &&
                A_thumbUp &&
                B_pointsOpposite &&
                B_palmAwayFromHead &&
                B_thumbDown &&
                closeEnough;
        }
        
        public void HandleRecording()
        {
            if (!isRecording && !isBuffering) return;
            
            if (isRecording)
                elapsedRecordingTime += Time.deltaTime;
            
            if (isBuffering)
                elapsedBufferTime += Time.deltaTime;

            var frame = CaptureFrame();
            
            // Recording
            if (isRecording && (elapsedRecordingTime - lastRecordedFrameTime >= 1f / (int)TargetRecordingFPS.SavedValue))
            {
                lastRecordedFrameTime = elapsedRecordingTime;

                frame.Time = elapsedRecordingTime;
                Frames.Add(frame);
            }

            // Buffering
            if (isBuffering && (elapsedBufferTime - lastBufferFrameTime >= 1f / (int)TargetRecordingFPS.SavedValue))
            {
                lastBufferFrameTime = elapsedBufferTime;

                frame.Time = elapsedBufferTime;
                replayBuffer.Enqueue(frame);

                float cutoffTime = frame.Time - (int)ReplayBufferDuration.SavedValue;

                while (replayBuffer.Count > 0 && replayBuffer.Peek().Time < cutoffTime)
                    replayBuffer.Dequeue();
            }

            Patches.activations.Clear();
            Events.Clear();
        }

        Frame CaptureFrame()
        {
            var heldStructures = new HashSet<GameObject>();
            var flickedStructures = new HashSet<GameObject>();

            // Players
            var playerStates = Utilities.NewArray<PlayerState>(RecordedPlayers.Count);

            for (int i = 0; i < RecordedPlayers.Count; i++)
            {
                var p = RecordedPlayers[i];
                if (p == null) continue;

                var stackProc = p.Controller.GetSubsystem<PlayerStackProcessor>();

                Stack flickStack = null;
                Stack holdStack = null;

                foreach (var stack in stackProc.availableStacks)
                {
                    switch (stack.cachedName)
                    {
                        case "Flick": flickStack = stack; break;
                        case "HoldLeft":
                        case "HoldRight": holdStack = stack; break;
                    }

                    if (flickStack?.runningExecutions != null)
                    {
                        foreach (var exec in flickStack.runningExecutions)
                        {
                            var proc = exec.TargetProcessable.TryCast<ProcessableComponent>();
                            if (proc?.gameObject != null)
                                flickedStructures.Add(proc.gameObject);
                        }
                    }
                    
                    if (holdStack?.runningExecutions != null)
                    {
                        foreach (var exec in holdStack.runningExecutions)
                        {
                            var proc = exec.TargetProcessable.TryCast<ProcessableComponent>();
                            if (proc?.gameObject != null)
                                heldStructures.Add(proc.gameObject);
                        }
                    }
                    
                    Transform VRRig = p.Controller.transform.GetChild(2);
                    Transform LHand = VRRig.GetChild(1);
                    Transform RHand = VRRig.GetChild(2);
                    Transform Head = VRRig.GetChild(0).GetChild(0);

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
            }

            // Structures
            var structureStates = Utilities.NewArray<StructureState>(Structures.Count);
            
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

                var structureRenderer = structureRenderers[i];
                structureRenderer.renderer.GetPropertyBlock(structureRenderer.mpb);

                bool isShaking = structureRenderer.mpb.GetFloat("_shake") > 0f;

                structureStates[i] = new StructureState
                {
                    position = structure.transform.position,
                    rotation = structure.transform.rotation,
                    active = structure.gameObject.activeSelf,
                    grounded = structure.IsGrounded,
                    isHeld = heldStructures.Contains(structure.gameObject),
                    isFlicked = flickedStructures.Contains(structure.gameObject),
                    isShaking = isShaking
                };
            }

            // Pedestals
            var pedestalStates = Utilities.NewArray<PedestalState>(Pedestals.Count);

            for (int i = 0; i < Pedestals.Count; i++)
            {
                var pedestal = Pedestals[i];
                if (pedestal == null) continue;
                
                pedestalStates[i] = new PedestalState
                {
                    position = pedestal.transform.position,
                    active = pedestal.GetComponent<Pedestal>().enabled
                };
            }
            
            return new Frame
            {
                Structures = structureStates,
                Players = playerStates,
                Pedestals = pedestalStates,
                Events = Events.ToArray()
            };
        }

        public void SetPlaybackSpeed(float newSpeed)
        {
            playbackSpeed = newSpeed;

            foreach (var structure in PlaybackStructures)
            {
                foreach (var vfx in structure.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = Abs(newSpeed);
            }

            for (int i = 0; i < VFXParent.transform.childCount; i++)
            {
                var vfx = VFXParent.transform.GetChild(i);
                vfx.GetComponent<VisualEffect>().playRate = Abs(newSpeed);
            }

            foreach (var pedestal in Pedestals)
            {
                foreach (var vfx in pedestal.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = Abs(newSpeed);
            }
        }
        
        public void HandlePlayback()
        {
            if (!isPlaying) return;

            if (currentPlaybackFrame >= currentReplay.Frames.Length - 3)
            {
                StopReplay();
                return;
            }

            elapsedPlaybackTime += Time.deltaTime * playbackSpeed;
            SetPlaybackTime(elapsedPlaybackTime);

            timelineMaterial?.SetFloat("_BP_Current", elapsedPlaybackTime * 1000f);

            if (currentDuration != null)
            {
                TimeSpan t = TimeSpan.FromSeconds(elapsedPlaybackTime);
                currentDuration.text = $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
            }
        }

        public void SetPlaybackTime(float time)
        {
            elapsedPlaybackTime = Clamp(time, 0f, currentReplay.Frames[^1].Time);

            for (int i = 0; i < currentReplay.Frames.Length - 2; i++)
            {
                if (currentReplay.Frames[i + 1].Time > elapsedPlaybackTime)
                {
                    currentPlaybackFrame = i;
                    break;
                }
            }

            Frame a = currentReplay.Frames[currentPlaybackFrame];
            Frame b = currentReplay.Frames[currentPlaybackFrame + 1];

            float span = b.Time - a.Time;
            float t = span > 0f
                ? (elapsedPlaybackTime - a.Time) / span
                : 1f;

            ApplyInterpolatedFrame(currentPlaybackFrame, Clamp01(t));
        }

        public void SetPlaybackFrame(int frame)
        {
            int clampedFrame = Clamp(frame, 0, currentReplay.Frames.Length - 2);
            float time = currentReplay.Frames[clampedFrame].Time;
            SetPlaybackTime(time);
        }

        public void UpdateReplayCameraPOV(int playerIndex, bool hideLocalPlayer = false)
        {
            RecordingCamera cam = Calls.GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<RecordingCamera>(); 
            
            var localController = PlayerManager.instance.localPlayer.Controller.transform;
            localController.GetChild(1).gameObject.SetActive(!hideLocalPlayer);
            localController.GetChild(6).gameObject.SetActive(!hideLocalPlayer);
            
            var povHead = povPlayer?.Controller.GetSubsystem<PlayerIK>().VrIK.references.head;
            if (povHead != null)
            {
                if (povPlayer != null)
                {
                    povHead.transform.localScale = Vector3.one;
                    povPlayer.Controller.transform.GetChild(6).gameObject.SetActive(true);
                }
                
                if (playerIndex < 0 || playerIndex >= PlaybackPlayers.Length)
                {
                    cam.localPlayerVR = Calls.Players.GetLocalPlayer().Controller.GetSubsystem<PlayerVR>();
                    povHead.transform.localScale = Vector3.one;
                    return;
                }
            }

            if (playerIndex > -1)
            {
                povPlayer = PlaybackPlayers[playerIndex];
                povHead = povPlayer.Controller.GetSubsystem<PlayerIK>().VrIK.references.head;
                povHead.transform.localScale = Vector3.zero;
                povPlayer.Controller.transform.GetChild(6).gameObject.SetActive(false);
                
                cam.localPlayerVR = povPlayer.Controller.GetSubsystem<PlayerVR>();
            }
        }

        public void ApplyInterpolatedFrame(int frameIndex, float t)
        {
            var frames = currentReplay.Frames;

            if (frameIndex < 0 || frameIndex >= frames.Length - 1)
                return;

            Frame a = frames[frameIndex];
            Frame b = frames[frameIndex + 1];

            // ------ Structures ------
            
            for (int i = 0; i < PlaybackStructures.Length; i++)
            {
                var playbackStructure = PlaybackStructures[i];
                var structureComp = playbackStructure.GetComponent<Structure>();
                var sa = a.Structures[i];
                var sb = b.Structures[i];
                var poolManager = PoolManager.instance;

                ref var state = ref playbackStructureStates[i];

                var vfxSize = playbackStructure.name switch
                {
                    "Disc" or "Ball" => 1f,
                    "RockCube" => 1.5f,
                    "Wall" or "Pillar" => 2.5f,
                    
                    "LargeRock" => 2.7f,

                    _ => 1f
                };
                
                var rb = playbackStructure.GetComponent<Rigidbody>();
                if (!rb.isKinematic)
                    rb.isKinematic = true;
                
                // ------ State Event Checks ------
                
                // Structure Broke
                if (state.active && !sb.active)
                {
                    var pool = poolManager.GetPool(
                        playbackStructure.name == "Disc" 
                            ? "DustBreakDISC_VFX"
                            : "DustBreak_VFX"
                    );
                
                    PooledMonoBehaviour effect = GameObject.Instantiate(pool.poolItem, VFXParent.transform);
                    effect.transform.position = sa.position;
                    effect.transform.rotation = Quaternion.identity;
                    effect.gameObject.AddComponent<ReplayTag>();
                    
                    AudioManager.instance.Play(
                        playbackStructure.GetComponent<Structure>().onDeathAudio, 
                        playbackStructure.transform.position
                    );

                    playbackStructure.GetComponent<Structure>().onStructureDestroyed?.Invoke();
                }

                // Structure Spawned
                if (!state.active && sb.active)
                {
                    var pool = poolManager.GetPool("DustSpawn_VFX");

                    var offset = playbackStructure.name == "Disc" ? Vector3.zero : new Vector3(0, 0.5f, 0);

                    PooledMonoBehaviour effect = GameObject.Instantiate(pool.poolItem, VFXParent.transform);
                    effect.transform.position = sb.position + offset;
                    effect.transform.rotation = Quaternion.identity;
                    effect.GetComponent<VisualEffect>().playRate = playbackSpeed;
                    effect.gameObject.AddComponent<ReplayTag>();

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
                
                state.active = sb.active;
                playbackStructure.SetActive(state.active);

                // Shaking
                if (state.isShaking != sb.isShaking)
                {
                    state.isShaking = sb.isShaking;
                    
                    var pr = playbackStructureRenderers[i];
                    
                    pr.renderer.GetPropertyBlock(pr.mpb);

                    pr.mpb.SetFloat("_shake", sb.isShaking ? 1f : 0f);
                    pr.renderer.SetPropertyBlock(pr.mpb);
                }
                
                // Grounded state
                if (state.grounded != sb.grounded)
                {
                    structureComp.processableComponent.SetCurrentState(
                        sb.grounded ? structureComp.groundedState
                            : structureComp.freeState
                    );

                    if (sb.grounded)
                    {
                        var collider = structureComp.GetComponentInChildren<Collider>();

                        Vector3 origin = collider.bounds.center;

                        if (Physics.Raycast(origin, Vector3.down, out var hit, 5f, 768))
                        {
                            var pool = poolManager.GetPool("Ground_VFX");
                            var effect = GameObject.Instantiate(pool.poolItem.gameObject, VFXParent.transform);
                            
                            effect.transform.SetPositionAndRotation(
                                hit.point,
                                Quaternion.identity
                            );
                            
                            effect.transform.localScale = Vector3.one;
                            effect.GetComponent<VisualEffect>().playRate = playbackSpeed;
                            effect.AddComponent<ReplayTag>();
                        }
                    }
                }
                
                state.grounded = structureComp.IsGrounded;
                
                // Hold started
                if (!state.isHeld && sb.isHeld)
                {
                    var pool = poolManager.GetPool("Hold_VFX");
                    var effect = GameObject.Instantiate(pool.poolItem.gameObject, playbackStructure.transform);
                    
                    effect.transform.localPosition = Vector3.zero;
                    effect.transform.localRotation = Quaternion.identity;
                    effect.transform.localScale = Vector3.one * vfxSize;
                    effect.GetComponent<VisualEffect>().playRate = playbackSpeed;
                    effect.AddComponent<ReplayTag>();

                    AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Hold"], sa.position);
                }
                
                // Hold ended
                if (state.isHeld && !sb.isHeld)
                {
                    foreach (var vfx in playbackStructure.GetComponentsInChildren<ReplayTag>())
                    {
                        if (vfx == null)
                            continue;

                        if (vfx.name.Contains("Hold_VFX") && vfx.transform.parent == playbackStructure.transform)
                        {
                            GameObject.Destroy(vfx.gameObject);
                            break;
                        }
                    }
                }
                
                state.isHeld = sb.isHeld;

                // Flick started
                if (!state.isFlicked && sb.isFlicked)
                {
                    var pool = poolManager.GetPool("Flick_VFX");
                    var effect = GameObject.Instantiate(pool.poolItem.gameObject, playbackStructure.transform);

                    effect.transform.localPosition = Vector3.zero;
                    effect.transform.localRotation = Quaternion.identity;
                    effect.transform.localScale = Vector3.one * vfxSize;
                    effect.GetComponent<VisualEffect>().playRate = playbackSpeed;
                    effect.AddComponent<ReplayTag>();
                    
                    AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Flick"], sa.position);
                }

                // Flick ended
                if (state.isFlicked && !sb.isFlicked)
                {
                    foreach (var vfx in playbackStructure.GetComponentsInChildren<ReplayTag>())
                    {
                        if (vfx == null)
                            continue;

                        if (vfx.name.Contains("Flick_VFX") && vfx.transform.parent == playbackStructure.transform)
                        {
                            GameObject.Destroy(vfx.gameObject);
                            break;
                        }
                    }
                }
                
                state.isFlicked = sb.isFlicked;
                
                // ------------

                if (sa.active && sb.active)
                {
                    Vector3 pos = Vector3.Lerp(sa.position, sb.position, t);
                    Quaternion rot = Quaternion.Slerp(sa.rotation, sb.rotation, t);
                    playbackStructure.transform.SetPositionAndRotation(pos, rot);
                }
                else
                {
                    playbackStructure.transform.SetPositionAndRotation(sb.position, sb.rotation);
                }

                foreach (var vfx in playbackStructure.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = playbackSpeed;
            }

            // ------ Players ------
            
            for (int i = 0; i < PlaybackPlayers.Length; i++)
            {
                var playbackPlayer = PlaybackPlayers[i];
                var pa = a.Players[i];
                var pb = b.Players[i];
                
                ref var state = ref playbackPlayerStates[i];

                if (state.active != pb.active)
                {
                    state.active = pb.active;
                    
                    playbackPlayer.Controller.gameObject.SetActive(pb.active);
                    continue;
                }

                if (state.health != pb.Health)
                {
                    playbackPlayer.Controller.GetSubsystem<PlayerHealth>().SetHealth(pb.Health, (short)state.health);
                    
                    if (pb.Health < pa.Health && pb.Health != 0)
                    {
                        var pool = PoolManager.instance.GetPool("PlayerHitmarker");
                        var effect = GameObject.Instantiate(pool.poolItem.gameObject, VFXParent.transform);

                        effect.transform.localPosition = playbackPlayer.Head.transform.position - new Vector3(0, 0.5f, 0);
                        effect.transform.localRotation = Quaternion.identity;
                        effect.GetComponent<VisualEffect>().playRate = playbackSpeed;
                        effect.AddComponent<ReplayTag>();

                        var hitmarker = effect.GetComponent<PlayerHitmarker>();
                        hitmarker.SetDamage(state.health - pb.Health);
                        hitmarker.gameObject.SetActive(true);
                        hitmarker.Play();
                        hitmarker.GetComponent<VisualEffect>().playRate = playbackSpeed;
                    }
                }
                
                state.health = playbackPlayer.Controller.assignedPlayer.Data.HealthPoints;
                
                if (state.currentStack != pb.currentStack)
                {
                    state.currentStack = pb.currentStack;
                    
                    if (pb.currentStack != (short)StackType.Flick
                        && pb.currentStack != (short)StackType.HoldLeft
                        && pb.currentStack != (short)StackType.HoldRight
                        && pb.currentStack != (short)StackType.None)
                    {
                        var key = ReplayCache.NameToStackType
                            .FirstOrDefault(s => s.Value == (StackType)pb.currentStack);

                        var stack = playbackPlayer.Controller
                            .GetSubsystem<PlayerStackProcessor>()
                            .availableStacks
                            .ToArray()
                            .FirstOrDefault(s => s.CachedName == key.Key);

                        if (stack != null)
                        {
                            playbackPlayer.Controller
                                .GetSubsystem<PlayerStackProcessor>()
                                .Execute(stack);
                        }
                    }
                }

                playbackPlayer.ApplyInterpolatedPose(pa, pb, t);
                
                foreach (var vfx in playbackPlayer.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = playbackSpeed;
            }
            
            // ------ Pedestals ------

            for (int i = 0; i < currentReplay.Header.PedestalCount; i++)
            {
                var playbackPedestal = replayPedestals[i];
                var pa = a.Pedestals[i];
                var pb = b.Pedestals[i];
                
                ref var state = ref playbackPedestalStates[i];
            
                if (state.active != pb.active)
                {
                    state.active = pb.active;
                    playbackPedestal.SetActive(pb.active);
                }
                
                Vector3 pos = Vector3.Lerp(pa.position, pb.position, t);
                playbackPedestal.transform.position = pos;
            }
            
            // ------ Events 

            var events = a.Events;
            if (events == null || lastEventFrame == currentPlaybackFrame)
                return;
            
            lastEventFrame = currentPlaybackFrame;

            foreach (var evt in events)
            {
                switch (evt.type)
                {
                    
                }
            }
        }
    }

    public struct PlaybackStructureState
    {
        public bool active;
        public bool grounded;
        public bool isHeld;
        public bool isFlicked;
        public bool isShaking;
    }

    public struct PlaybackPlayerState
    {
        public bool active;
        public int health;
        public short currentStack;
    }

    public struct PlaybackPedestalState
    {
        public bool active;
    }
}

[RegisterTypeInIl2Cpp]
public class Clone : MonoBehaviour
{
    public GameObject VRRig;
    public GameObject LeftHand;
    public GameObject RightHand;
    public GameObject Head;
    public PlayerController Controller;

    public PlayerAnimator pa;
    public PlayerMovement pm;

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

    public void Update()
    {
        if (Controller == null)
            return;

        if (pa == null || pm == null)
        {
            pa = Controller.GetSubsystem<PlayerAnimator>();
            pm = Controller.GetSubsystem<PlayerMovement>();
        }

        int state = pm.IsGrounded() ? 1 : 2;

        pa.animator.SetInteger(pa.movementStateAnimatorHash, state);
    }
}

[RegisterTypeInIl2Cpp]
public class TableFloat : MonoBehaviour
{
    public float amplitude = 0.25f;
    public float speed = 1f;

    public float stopRadius = 2f;
    public float resumeSpeed = 6f;

    private float floatTime;
    private float timeScale = 1f;

    public Vector3 startPos;
    public float targetY;

    void Awake()
    {
        startPos = transform.localPosition;
    }

    void Update()
    {
        if (Main.instance.head == null)
            return;
        
        startPos.y = Lerp(
            startPos.y, 
            targetY + (float)Main.instance.tableOffset.SavedValue, 
            Time.deltaTime * 4f
        );
        
        float d = (Main.instance.head.position - transform.position).magnitude;
        bool shouldStop = d < stopRadius;

        timeScale = Lerp(
            timeScale,
            shouldStop ? 0f : 1f,
            Time.deltaTime * resumeSpeed
        );

        floatTime += Time.deltaTime * speed * timeScale;
        
        float y = Sin(floatTime) * amplitude;
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

    public bool isReadingCrystal = false;

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
            
            ReplayCrystals.Crystal target = ReplayCrystals.FindClosestCrystal(
                transform.position + new Vector3(0, 0.4f, 0),
                0.5f
            );

            if (target == null && !SceneManager.instance.IsLoadingScene)
            {
                if (ReplayFiles.metadataHidden && ReplayFiles.currentIndex != -1)
                    ReplayFiles.ShowMetadata();

                isReadingCrystal = false;
            }
            else
            {
                if (!ReplayFiles.metadataHidden)
                    ReplayFiles.HideMetadata();

                if (!isReadingCrystal && target != null && !target.isGrabbed && target.hasLeftTable)
                {
                    isReadingCrystal = true;
                    target.isAnimation = true;
                    MelonCoroutines.Start(ReplayCrystals.ReadCrystal(target));
                }
            }
        }
    }
}

[RegisterTypeInIl2Cpp]
public class LookAtPlayer : MonoBehaviour
{
    public bool lockX;
    public bool lockY;
    public bool lockZ;

    void Update()
    {
        transform.rotation = Quaternion.Euler(0, 270, 0);
        
        var cam = Camera.main;
        if (!cam)
            return;

        Vector3 toCam = cam.transform.position - transform.position;
        if (toCam.sqrMagnitude < 0.0001f)
            return;

        Quaternion target = Quaternion.LookRotation(toCam, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);

        Vector3 targetEuler = target.eulerAngles;
        Vector3 currentEuler = transform.rotation.eulerAngles;

        if (lockX) targetEuler.x = currentEuler.x;
        if (lockY) targetEuler.y = currentEuler.y;
        if (lockZ) targetEuler.z = currentEuler.z;

        transform.rotation = Quaternion.Euler(targetEuler);
    }
}

[RegisterTypeInIl2Cpp]
public class ReplayTag : MonoBehaviour { }