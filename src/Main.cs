using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Environment.MatchFlow;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.Integrations.LIV;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Networking;
using Il2CppRUMBLE.Networking.MatchFlow;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppRUMBLE.Poses;
using Il2CppRUMBLE.Social.Phone;
using Il2CppRUMBLE.Utilities;
using Il2CppSystem.IO;
using Il2CppTMPro;
using MelonLoader;
using RumbleModdingAPI;
using RumbleModUI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using InteractionButton = Il2CppRUMBLE.Interactions.InteractionBase.InteractionButton;
using Mod = RumbleModUIPlus.Mod;
using Random = UnityEngine.Random;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;
using Tags = RumbleModUIPlus.Tags;

[assembly: MelonInfo(typeof(ReplayMod.Main), ReplayMod.BuildInfo.Name, ReplayMod.BuildInfo.Version, ReplayMod.BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("RumbleModdingAPI","RumbleModUIPlus")]

namespace ReplayMod;

public static class BuildInfo
{
    public const string Name = "ReplayMod";
    public const string Author = "ERROR";
    public const string Version = "1.0.0";
    public const string FormatVersion = "1.0.0";
}

public class Main : MelonMod
{
    // Runtime
    public static Main instance;
    public Main() => instance = this;
    public string currentScene = "Loader";
    
    // Replay control
    public static ReplayInfo currentReplay;
    public static bool isPlaying = false;
    public static float playbackSpeed = 1f;
    
    // Local player
    public static Player LocalPlayer => PlayerManager.instance.localPlayer;
    public Transform leftHand, rightHand, head;
    
    // ------ Recording ------
    public static bool isRecording = false;
    
    public static float lastSampleTime = 0f;

    public int pingSum;
    public int pingCount;
    public int pingMin = int.MaxValue;
    public int pingMax = 0;
    public static float pingTimer = 0f;

    public TextMeshPro recordingIcon;
    
    public List<Frame> Frames = new();
    public List<EventChunk> Events = new();
    
    public Queue<Marker> bufferMarkers = new();
    public List<Marker> recordingMarkers = new();
    
    // World
    public List<Structure> Structures = new();
    public List<GameObject> Pedestals = new();
    public string recordingSceneName;

    // Players
    public List<Player> RecordedPlayers = new();
    public Dictionary<string, PlayerInfo> PlayerInfos = new();
    public List<StructureInfo> StructureInfos = new();
    
    // Recording FX / timers
    public GameObject clapperboardVFX;
    public bool hasPlayed;
    public float heldTime, soundTimer = 0f;
    
    // Replay Buffer
    public Queue<Frame> replayBuffer = new();
    public static bool isBuffering = false;
    public float lastTriggerTime = 0f;
    
    // ------ Playback ------
    
    public static float elapsedPlaybackTime = 0f;
    public static int currentPlaybackFrame = 0;

    public bool isReplayScene;

    public bool hasPaused;
    public static bool isPaused = false;
    public static float previousPlaybackSpeed = 1f;
    
    // Roots
    public GameObject ReplayRoot;
    public GameObject replayStructures;
    public GameObject replayPlayers;
    public GameObject pedestalsParent;
    public GameObject VFXParent;
    
    // Structures
    public GameObject[] PlaybackStructures;
    public HashSet<Structure> HiddenStructures = new();
    public PlaybackStructureState[] playbackStructureStates;
    public bool disableBaseStructureSystems = true;
    
    // Players
    public Clone[] PlaybackPlayers;
    public PlaybackPlayerState[] playbackPlayerStates;
    public Player povPlayer;

    public GameObject highlightMaterial;

    // Pedestals
    public List<GameObject> replayPedestals = new();
    public PlaybackPedestalState[] playbackPedestalStates;
    
    // Events
    public int lastEventFrame = -1;
    
    // UI
    public ReplayTable replayTable;
    public GameObject flatLandRoot;
    public bool? lastFlatLandActive;

    public ReplaySettings replaySettings;
    public object crystalBreakCoroutine;
    
    // ------ Settings ------
    
    public const float errorsArmspan = 1.2744f;
    
    private Mod replayMod = new();

    // Recording
    public ModSetting<int> TargetRecordingFPS = new();
    public ModSetting<bool> AutoRecordMatches = new();
    public ModSetting<bool> AutoRecordParks = new();
    
    public ModSetting<bool> HandFingerRecording = new();
    public ModSetting<bool> CloseHandsOnPose = new();
    
    // Automatic Markers - Match End
    public ModSetting<bool> EnableMatchEndMarker = new();
    
    // Automatic Markers - Round End
    public ModSetting<bool> EnableRoundEndMarker = new();
    
    // Automatic Markers - Large Damage
    public ModSetting<bool> EnableLargeDamageMarker = new();
    public ModSetting<int> DamageThreshold = new();
    public ModSetting<float> DamageWindow = new();
    
    // Playback
    public ModSetting<bool> StopReplayWhenDone = new();
    public ModSetting<bool> PlaybackControlsFollow = new();
    public ModSetting<bool> DestroyControlsOnPunch = new();
    
    // Replay Buffer
    public ModSetting<bool> ReplayBufferEnabled = new();
    public ModSetting<int> ReplayBufferDuration = new();

    // Controls
    public ModSetting<string> LeftHandControls = new();
    public ModSetting<string> RightHandControls = new();

    public ModSetting<bool> EnableHaptics = new();
    
    // Other
    public ModSetting<float> tableOffset = new();
    
    // ------------

    public void ReplayError(string message = null, Vector3 position = default)
    {
        try
        {
            if (position == default && head != null)
                position = head.position;

            AudioManager.instance.Play(
                ReplayCache.SFX["Call_Measurement_Failure"],
                position
            );
        }
        catch { }
        
        if (!string.IsNullOrEmpty(message))
            LoggerInstance.Error(message);   
    }
    
    // ----- Init -----
    
    public override void OnLateInitializeMelon()
    {
        UI.instance.UI_Initialized += OnUIInitialized;
        Calls.onMapInitialized += OnMapInitialized;
        
        Calls.onRoundEnded += () =>
        {
            if (!(bool)EnableRoundEndMarker.SavedValue)
                return;

            AddMarker("core.roundEnded", new Color(0.7f, 0.6f, 0.85f));
        };

        Calls.onMatchEnded += () =>
        {
            if ((bool)EnableMatchEndMarker.SavedValue)
                AddMarker("core.matchEnded", Color.black);
            
            if (isRecording) StopRecording();
        };
        
        ReplayFiles.Init();
    }
    
    private IEnumerator ListenForFlatLand()
    {
        yield return new WaitForSeconds(1f);
        flatLandRoot = GameObject.Find("FlatLand");
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
        {
            LoggerInstance.Msg("Stopped replay [OnSceneWasLoaded]");
            StopReplay();
        }

        if (sceneName == "Gym")
            MelonCoroutines.Start(ListenForFlatLand());

        replayBuffer.Clear();
        lastSampleTime = 0f;

        if (currentScene == "Gym")
            isReplayScene = false;

        pingCount = 0;
        pingSum = 0;
        pingMin = int.MaxValue;
        pingMax = 0;
    }

    public void OnMapInitialized()
    {
        recordingIcon = Calls.Create.NewText().GetComponent<TextMeshPro>();
        recordingIcon.transform.SetParent(LocalPlayer.Controller.GetSubsystem<PlayerUI>().transform.GetChild(0));
        recordingIcon.name = "Replay Recording Icon";
        recordingIcon.color = new Color(0, 1, 0, 0);
        recordingIcon.text = "●";
        recordingIcon.ForceMeshUpdate();
        recordingIcon.transform.localPosition = new Vector3(0.2313f, 0.0233f, 0.9604f);
        recordingIcon.transform.localRotation = Quaternion.Euler(20.2549f, 18.8002f, 0);
        recordingIcon.transform.localScale = Vector3.one * 0.4f;
        
        if (((currentScene is "Map0" or "Map1" && (bool)AutoRecordMatches.SavedValue && PlayerManager.instance.AllPlayers.Count > 1) || (currentScene == "Park" && (bool)AutoRecordParks.SavedValue)) && !isReplayScene)
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
            
            if (replayTable.tableFloat != null)
                replayTable.tableFloat.startPos = new Vector3(5.9506f, 1.3564f, 4.1906f);
            
            if (replayTable.metadataTextFloat != null)
                replayTable.metadataTextFloat.startPos = new Vector3(5.9575f, 1.8514f, 4.2102f);
            
            replayTable.tableOffset = 0f;
            replayTable.transform.localRotation = Quaternion.Euler(270, 121.5819f, 0);
        }
        else if (currentScene == "Park")
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);
            
            replayTable.tableFloat.startPos = new Vector3(-28.9436f, -1.5689f, -6.9218f);
            replayTable.metadataTextFloat.startPos = new Vector3(-28.9499f, -2.0639f, -6.9414f);
            replayTable.tableOffset = -3.06f;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 0, 0);

            if (isReplayScene)
                MelonCoroutines.Start(DelayedParkLoad());

        } 
        else if (currentScene == "Map0" && PlayerManager.instance.AllPlayers.Count == 1)
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);

            replayTable.tableFloat.startPos = new Vector3(0.411f, -4.4577f, 18.312f);
            replayTable.metadataTextFloat.startPos = new Vector3(0.411f, -4.027f, 18.3094f);
            replayTable.tableOffset = -6;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 90f, 0f);
        } 
        else if (currentScene == "Map1" && PlayerManager.instance.AllPlayers.Count == 1)
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);

            replayTable.tableFloat.startPos = new Vector3(0.991f, 1.1872f, 9.8221f);
            replayTable.metadataTextFloat.startPos = new Vector3(0.991f, 1.1872f, 9.8221f);
            replayTable.tableOffset = -0.35f;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 90f, 0f);
        }
        else
        {
            replayTable.TableRoot.SetActive(false);
            replayTable.metadataText.gameObject.SetActive(false);
        }
        
        if (isReplayScene)
        {
            MatchHandler matchHandler = currentScene switch
            {
                "Map1" => Calls.GameObjects.Map1.Logic.MatchHandler.GetGameObject().GetComponent<MatchHandler>(),
                "Map0" => Calls.GameObjects.Map0.Logic.MatchHandler.GetGameObject().GetComponent<MatchHandler>(),
                _ => null
            };

            if (matchHandler != null)
            {
                matchHandler.CurrentMatchPhase = MatchHandler.MatchPhase.MatchStart;
                matchHandler.FadeIn();
            }
        }
        
        string[] spellings = { "Heisenhouser", "Heisenhowser", "Heisenhouwser", "Heisenhouwer" };
        replayTable.heisenhouserText.text = spellings[Random.Range(0, spellings.Length)];
        replayTable.heisenhouserText.ForceMeshUpdate();

        ReplayPlaybackControls.destroyOnPunch.leftHand = LocalPlayer.Controller.GetSubsystem<PlayerHandPresence>().leftInteractionHand;
        ReplayPlaybackControls.destroyOnPunch.rightHand = LocalPlayer.Controller.GetSubsystem<PlayerHandPresence>().rightInteractionHand;
        
        ReplayCrystals.LoadCrystals(currentScene);
        ReplayFiles.LoadReplays();
        
        ReplayPlaybackControls.Close();
        
        if (currentScene != "Loader" && (bool)ReplayBufferEnabled.SavedValue)
            StartBuffering();
        
        if (isRecording || isBuffering)
            SetupRecordingData();

        var vr = LocalPlayer.Controller.transform.GetChild(2);
        replayTable.metadataText.GetComponent<LookAtPlayer>().lockX = true;
        replayTable.metadataText.GetComponent<LookAtPlayer>().lockZ = true;

        leftHand = vr.GetChild(1);
        rightHand = vr.GetChild(2);
        head = vr.GetChild(0).GetChild(0);

        SetPlaybackSpeed(1f);
        isReplayScene = false;
    }

    IEnumerator DelayedParkLoad()
    {
        if ((bool)EnableHaptics.SavedValue)
            LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.1f, 1f, 0.1f);
        
        yield return new WaitForSeconds(2f);

        if (currentScene == "Park")
        {
            if (PhotonNetwork.CurrentRoom != null)
                PhotonNetwork.CurrentRoom.isVisible = false;
            
            Calls.GameObjects.Park.LOGIC.ParkInstance.GetGameObject().SetActive(false);
            PhotonNetwork.LeaveRoom();
            
            yield return new WaitForSeconds(1f);
        }

        LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
        SimpleScreenFadeInstance.Progress = 0f;
    }

    IEnumerator DelayedFlatLandLoad()
    {
        if (flatLandRoot == null) yield break;

        while (flatLandRoot.activeSelf)
            yield return null;
        
        yield return new WaitForSeconds(1f);

        LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
        SimpleScreenFadeInstance.Progress = 0f;
    }

    public void OnUIInitialized()
    {
        replayMod.ModName = BuildInfo.Name;
        replayMod.ModVersion = BuildInfo.Version;
        replayMod.ModFormatVersion = BuildInfo.Version;

        replayMod.SetFolder("ReplayMod/Settings");
        replayMod.AddDescription("Description", "", "A mod that records scenes into a 3d file that you can replay", new Tags { IsSummary = true });

        var recordingFolder = replayMod.AddFolder("Recording", "Controls how and when gameplay is recorded into replays.");

        TargetRecordingFPS = replayMod.AddToList("Recording FPS", 50, "The target frame rate used when recording replays.\nThis is limited by the game's actual frame rate.", new Tags());
        AutoRecordMatches = replayMod.AddToList("Automatically Record Matches", true, 0, "Automatically start recording when you join a match", new Tags());
        AutoRecordParks = replayMod.AddToList("Automatically Record Parks", false, 0, "Automatically start recording when you join a park", new Tags());
        
        HandFingerRecording = replayMod.AddToList("Finger Animation Recording", true, 0, "Controls whether finger input values are recorded into the replay.", new Tags());
        CloseHandsOnPose = replayMod.AddToList("Close Hands On Pose", true, 0, "Closes the hands of a clone when they do a pose.", new Tags());
        
        var automaticMarkersFolder = replayMod.AddFolder("Automatic Markers", "Automatically adds markers to replays when notable events occur.");

        var matchEndFolder = replayMod.AddFolder("Match End", "Settings for markers added when a match ends.");
        EnableMatchEndMarker = replayMod.AddToList("Enable Match End Marker", true, 0, "Automatically adds a marker at the end of a match.", new Tags());
        matchEndFolder.AddSetting(EnableMatchEndMarker);

        var roundEndFolder = replayMod.AddFolder("Round End", "Settings for markers added at the end of each round.");
        EnableRoundEndMarker = replayMod.AddToList("Enable Round End Marker", true, 0, "Automatically adds a marker at the end of a round.", new Tags());
        roundEndFolder.AddSetting(EnableRoundEndMarker);

        var largeDamageFolder = replayMod.AddFolder("Large Damage", "Settings for markers triggered by bursts of high damage.");
        EnableLargeDamageMarker = replayMod.AddToList("Enable Large Damage Marker", false, 0, "Automatically adds a marker when a player takes a large amount of damage in a short amount of time.", new Tags());
        DamageThreshold = replayMod.AddToList("Damage Threshold", 12, "The minimum total damage required to create a marker.", new Tags());
        DamageWindow = replayMod.AddToList("Damage Window (seconds)", 1f, "The time window (in seconds) during which damage is summed to determine whether a marker should be created.", new Tags());
        
        largeDamageFolder.AddSetting(EnableLargeDamageMarker)
            .AddSetting(DamageThreshold)
            .AddSetting(DamageWindow);

        automaticMarkersFolder.AddSetting(matchEndFolder)
            .AddSetting(roundEndFolder)
            .AddSetting(largeDamageFolder);
        
        recordingFolder.AddSetting(TargetRecordingFPS)
            .AddSetting(AutoRecordMatches)
            .AddSetting(AutoRecordParks)
            .AddSetting(HandFingerRecording)
            .AddSetting(CloseHandsOnPose)
            .AddSetting(automaticMarkersFolder);

        var playbackFolder = replayMod.AddFolder("Playback", "Settings for playing back replays.");
        
        StopReplayWhenDone = replayMod.AddToList("Stop Replay On Finished", false, 0, "Stops a replay when it reaches the end or beginning of its duration.", new Tags());
        PlaybackControlsFollow = replayMod.AddToList("Playback Controls Follow Player", false, 0, "Makes the playback controls menu follow you when opened.", new Tags());
        DestroyControlsOnPunch = replayMod.AddToList("Destroy Controls On Punch", true, 0, "Destroys the playback controls when you punch the slab hard enough.", new Tags());
        
        playbackFolder.AddSetting(StopReplayWhenDone);
        playbackFolder.AddSetting(PlaybackControlsFollow);
        playbackFolder.AddSetting(DestroyControlsOnPunch);
        
        var replayBufferFolder = replayMod.AddFolder("Replay Buffer", "Settings for the replay buffer used to save recent gameplay.");

        ReplayBufferEnabled = replayMod.AddToList("Enable Replay Buffer", false, 0, "Keeps a rolling buffer of recent gameplay that can be saved as a replay.", new Tags());
        ReplayBufferDuration = replayMod.AddToList("Replay Buffer Duration (seconds)", 30, "How much gameplay time (in seconds) is kept in the replay buffer.", new Tags());
        
        replayBufferFolder.AddSetting(ReplayBufferEnabled);
        replayBufferFolder.AddSetting(ReplayBufferDuration);
        
        var controlsFolder = replayMod.AddFolder("Controls", "Controller bindings and feedback settings for replay actions.");
        
        LeftHandControls = replayMod.AddToList("Left Controller Binding", "None",
            "Selects the action performed when both buttons on the left controller are pressed at the same time.\n" +
            "Possible values:\n" +
            "- Toggle Recording\n" +
            "- Save Replay Buffer\n" +
            "- Add Marker (adds an event marker at the current time in a recording)\n" +
            "- None\n" +
            "Actions can be seperated by a comma to include multiple actions on press.", new Tags());
        
        RightHandControls = replayMod.AddToList("Right Controller Binding", "None",
            "Selects the action performed when both buttons on the right controller are pressed at the same time.\n" +
            "Possible values:\n" +
            "- Toggle Recording\n" +
            "- Save Replay Buffer\n" +
            "- Add Marker (adds an event marker at the current time in a recording)\n" +
            "- None\n" +
            "Actions can be seperated by a comma to include multiple actions on press.", new Tags());

        EnableHaptics = replayMod.AddToList("Enable Haptics", true, 0, "Plays controller haptics when actions such as saving a replay or adding a marker are performed.", new Tags());

        controlsFolder.AddSetting(LeftHandControls)
            .AddSetting(RightHandControls)
            .AddSetting(EnableHaptics);
        
        var otherFolder = replayMod.AddFolder("Other", "Miscellaneous settings.");

        tableOffset = replayMod.AddToList("Replay Table Height Offset", 0f, "Adjusts the vertical offset of the replay table in meters.\nUseful if the table feels too high or too low.", new Tags());
        
        otherFolder.AddSetting(tableOffset);

        ReplayBufferEnabled.SavedValueChanged += (obj, sender) =>
        {
            if ((bool)ReplayBufferEnabled.SavedValue && !isBuffering)
                StartBuffering();
            
            isBuffering = (bool)ReplayBufferEnabled.SavedValue;
        };
        
        var allowedBindings = new[] { "Toggle Recording", "Save Replay Buffer", "Add Marker", "None" };

        bool IsValidBindingList(string input)
        {
            return input
                .Split(',')
                .Select(s => s.Trim())
                .All(binding => 
                    allowedBindings.Contains(binding, StringComparer.OrdinalIgnoreCase)
                );
        }
        
        LeftHandControls.SavedValueChanged += (obj, sender) =>
        {
            string value = (string)LeftHandControls.Value;
            if (!IsValidBindingList(value)) 
                ReplayError($"'{value}' is not a valid binding (Left Controller)");
        };

        RightHandControls.SavedValueChanged += (obj, sender) =>
        {
            string value = (string)RightHandControls.Value;
            if (!IsValidBindingList(value))
                ReplayError($"'{value}' is not a valid binding (Right Controller).");
        };
        
        replayMod.GetFromFile();
        
        UI.instance.AddMod(replayMod);
    }

    public void LoadReplayObjects()
    {
        GameObject ReplayTable = new GameObject("Replay Table");

        ReplayTable.transform.localPosition = new Vector3(5.9506f, 1.3564f, 4.1906f);
        ReplayTable.transform.localRotation = Quaternion.Euler(270f, 121.5819f, 0f);

        AssetBundle bundle = Calls.LoadAssetBundleFromStream(this, "ReplayMod.src.replayobjects2");

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

        table.layer = LayerMask.NameToLayer("LeanableEnvironment");
        table.AddComponent<MeshCollider>();

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
        loadReplayButton.transform.localPosition = new Vector3(0.5267f, -0.0131f, -0.1956f);
        loadReplayButton.transform.localRotation = Quaternion.Euler(0, 198.4543f, 0);
        loadReplayButton.transform.localScale = new Vector3(0.5129f, 1.1866f, 0.78f);
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
            LoadSelectedReplay();
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

        GameObject vfx = GameObject.Instantiate(PoolManager.instance.GetPool("Stubbornstone_VFX").poolItem.gameObject, ReplayTable.transform);

        vfx.name = "Crystalize VFX";
        vfx.transform.localScale = Vector3.one * 0.2f;
        vfx.transform.localPosition = new Vector3(0, 0, 0.3903f);
        vfx.SetActive(true);

        VisualEffect vfxComp = vfx.GetComponent<VisualEffect>();
        vfxComp.playRate = 0.6f;

        ReplayCrystals.crystalizeVFX = vfxComp;

        var crystalizeButton = GameObject.Instantiate(loadReplayButton, ReplayTable.transform);

        crystalizeButton.name = "CrystalizeReplay";
        crystalizeButton.transform.localPosition = new Vector3(0.21f, -0.4484f, -0.1325f);
        crystalizeButton.transform.localScale = Vector3.one * 1.1f;
        crystalizeButton.transform.localRotation = Quaternion.Euler(303.8364f, 249f, 108.4483f);

        crystalizeButton.transform.GetChild(1).transform.localScale = Vector3.one * 0.1f;

        var crystalizeButtonComp = crystalizeButton.transform.GetChild(0).GetComponent<InteractionButton>();
        crystalizeButtonComp.enabled = true;
        crystalizeButtonComp.OnPressed.RemoveAllListeners();

        replayTable.crystalizeButton = crystalizeButtonComp;

        GameObject crystalPrefab = GameObject.Instantiate(bundle.LoadAsset<GameObject>("Crystal"));

        crystalPrefab.transform.localScale *= 0.5f;

        var shiftstoneMat = PoolManager.instance.GetPool("FlowStone").PoolItem.transform.GetChild(0).GetComponent<Renderer>().material;
        foreach (var rend in crystalPrefab.GetComponentsInChildren<Renderer>(true))
            rend.material = shiftstoneMat;

        crystalPrefab.SetActive(false);
        crystalPrefab.transform.localRotation = Quaternion.Euler(-90, 0, 0);

        ReplayCrystals.crystalPrefab = crystalPrefab;

        var crystalizeIcon = new GameObject("CrystalizeIcon");
        crystalizeIcon.transform.SetParent(crystalizeButton.transform.GetChild(0));
        crystalizeIcon.transform.localPosition = new Vector3(0, 0.012f, 0);
        crystalizeIcon.transform.localRotation = Quaternion.Euler(270, 0, 0);
        crystalizeIcon.transform.localScale = Vector3.one * 0.07f;

        var srC = crystalizeIcon.AddComponent<SpriteRenderer>();
        var textureC = bundle.LoadAsset<Texture2D>("CrystalSprite");
        srC.sprite = Sprite.Create(
            textureC,
            new Rect(0, 0, textureC.width, textureC.height),
            new Vector2(0.5f, 0.5f),
            100f
        );

        var heisenhouserIcon = new GameObject("HeisenhouwserIcon");
        heisenhouserIcon.transform.SetParent(ReplayTable.transform);
        heisenhouserIcon.transform.localPosition = new Vector3(-0.2847f, 0.3901f, -0.1354f);
        heisenhouserIcon.transform.localRotation = Quaternion.Euler(51.2548f, 110.7607f, 107.2314f);
        heisenhouserIcon.transform.localScale = Vector3.one * 0.02f;

        var srH = heisenhouserIcon.AddComponent<SpriteRenderer>();
        var textureH = bundle.LoadAsset<Texture2D>("HeisenhowerSprite");
        srH.sprite = Sprite.Create(
            textureH,
            new Rect(0, 0, textureH.width, textureH.height),
            new Vector3(0.5f, 0.5f),
            100f
        );

        GameObject heisenhowuserText = Calls.Create.NewText("Heisenhouwser", 1f, Color.white, Vector3.zero, Quaternion.identity);

        heisenhowuserText.transform.SetParent(ReplayTable.transform);
        heisenhowuserText.name = "HeisenhouswerLogoText";

        heisenhowuserText.transform.localPosition = new Vector3(-0.2891f, 0.3969f, -0.1684f);
        heisenhowuserText.transform.localScale = Vector3.one * 0.0035f;
        heisenhowuserText.transform.localRotation = Quaternion.Euler(51.2551f, 110.4334f, 107.2313f);

        replayTable.heisenhouserText = heisenhowuserText.GetComponent<TextMeshPro>();

        replayTable.heisenhouserText.fontSizeMin = 1;
        replayTable.heisenhouserText.enableAutoSizing = true;

        crystalizeButtonComp.onPressedAudioCall = loadReplayButtonComp.onPressedAudioCall;

        ReplayCrystals.crystalParent = new GameObject("Crystals");

        crystalizeButtonComp.OnPressed.AddListener((UnityAction)(() =>
        {
            if (!ReplayCrystals.Crystals.Any(c => c != null && c.ReplayPath == ReplayFiles.explorer.CurrentReplayPath) && ReplayFiles.currentHeader != null && ReplayFiles.explorer.currentIndex != -1)
            {
                AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_Bake_Part"], crystalizeButton.transform.position);

                var header = ReplayFiles.currentHeader;
                ReplayCrystals.CreateCrystal(replayTable.transform.position + new Vector3(0, 0.3f, 0), header, ReplayFiles.explorer.CurrentReplayPath, true);
            }
            else
            {
                ReplayError();
            }
        }));

        var loadReplaySprite = new GameObject("LoadReplaySprite");

        loadReplaySprite.transform.SetParent(loadReplayButtonComp.transform);

        loadReplaySprite.transform.localPosition = new Vector3(0, 0.012f, 0);
        loadReplaySprite.transform.localRotation = Quaternion.Euler(270, 90, 0);
        loadReplaySprite.transform.localScale = new Vector3(0.0015f, 0.003f, 0.003f);

        var loadReplaySpriteComp = loadReplaySprite.AddComponent<SpriteRenderer>();
        loadReplaySpriteComp.sprite = nextButton.transform.GetChild(3).GetChild(0).GetComponent<Image>().sprite;
        loadReplaySpriteComp.color = new Color(0.4047f, 0.3279f, 0f);

        // Playback Controls

        var playbackControls = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.School.LogoSlab.NotificationSlab.SlabbuddyInfovariant.InfoForm.GetGameObject());
        playbackControls.name = "Playback Controls";
        playbackControls.transform.localScale = Vector3.one;

        GameObject destroyOnPunch = new GameObject("DestroyOnPunch");
        destroyOnPunch.layer = LayerMask.NameToLayer("InteractionBase");
        destroyOnPunch.transform.SetParent(playbackControls.transform);

        var destroyOnPunchComp = destroyOnPunch.AddComponent<DestroyOnPunch>();
        destroyOnPunchComp.onDestroy += ReplayPlaybackControls.Close;

        var boxCollider = destroyOnPunch.AddComponent<BoxCollider>();
        var playbackRenderer = playbackControls.transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>();
        boxCollider.center = playbackRenderer.localBounds.center;
        boxCollider.size = playbackRenderer.localBounds.size;

        GameObject.Destroy(playbackControls.transform.GetChild(2).gameObject);

        for (int i = 0; i < playbackControls.transform.GetChild(1).childCount; i++)
            GameObject.Destroy(playbackControls.transform.GetChild(1).GetChild(i).gameObject);

        var timeline = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Gearmarket.Itemhighlightwindow.StatusBar.GetGameObject(),
            playbackControls.transform.GetChild(1));
        Material timelineMaterial = timeline.GetComponent<MeshRenderer>().material;
        timelineMaterial.SetFloat("_Has_BP_Requirement", 1f);
        timelineMaterial.SetFloat("_Has_RC_Requirement", 0f);

        timeline.name = "Timeline";
        timeline.transform.localPosition = new Vector3(0, -0.1091f, 0);
        timeline.transform.localScale = new Vector3(1.0715f, 0.0434f, 1);
        timeline.transform.localRotation = Quaternion.identity;
        timeline.SetActive(true);

        var colliderObj = new GameObject("TimelineCollider");
        colliderObj.transform.SetParent(timeline.transform, false);
        colliderObj.layer = LayerMask.NameToLayer("InteractionBase");

        var col = colliderObj.AddComponent<BoxCollider>();
        col.center = timeline.GetComponent<MeshRenderer>().localBounds.center;
        col.size = timeline.GetComponent<MeshRenderer>().localBounds.size;

        colliderObj.AddComponent<TimelineScrubber>();

        var currentDuration = Calls.Create.NewText(":3", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
        var totalDuration = Calls.Create.NewText(":3", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
        var playbackTitle = Calls.Create.NewText("Colon Three", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
        var playbackSpeedText = Calls.Create.NewText("Vewy Fast!", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();

        currentDuration.transform.SetParent(playbackControls.transform.GetChild(1));
        currentDuration.name = "Current Duration";

        currentDuration.transform.localScale = Vector3.one * 0.8f;
        currentDuration.transform.localPosition = new Vector3(-0.2633f, -0.0412f, 0);
        currentDuration.transform.localRotation = Quaternion.identity;

        totalDuration.transform.SetParent(playbackControls.transform.GetChild(1));
        totalDuration.name = "Total Duration";

        totalDuration.transform.localScale = Vector3.one * 0.8f;
        totalDuration.transform.localPosition = new Vector3(0.696f, -0.0412f, 0);
        totalDuration.transform.localRotation = Quaternion.identity;

        playbackTitle.transform.SetParent(playbackControls.transform.GetChild(1));
        playbackTitle.name = "Playback Title";

        playbackTitle.transform.localScale = Vector3.one * 1.2f;
        playbackTitle.transform.localPosition = new Vector3(0, 0.5916f, 0);
        playbackTitle.transform.localRotation = Quaternion.identity;

        playbackTitle.horizontalAlignment = HorizontalAlignmentOptions.Center;

        playbackSpeedText.transform.SetParent(playbackControls.transform.GetChild(1));
        playbackSpeedText.name = "Playback Speed";

        playbackSpeedText.transform.localScale = Vector3.one * 1.3f;
        playbackSpeedText.transform.localPosition = new Vector3(0, 0.002f, 0);
        playbackSpeedText.transform.localRotation = Quaternion.identity;
        playbackSpeedText.horizontalAlignment = HorizontalAlignmentOptions.Center;

        var friendScrollBar = Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Telephone20REDUXspecialedition.FriendScreen.FriendScrollBar.GetGameObject();

        var p5x = GameObject.Instantiate(friendScrollBar.transform.GetChild(0).gameObject, playbackControls.transform.GetChild(1));
        var np5x = GameObject.Instantiate(friendScrollBar.transform.GetChild(1).gameObject, playbackControls.transform.GetChild(1));
        var p1x = GameObject.Instantiate(friendScrollBar.transform.GetChild(2).gameObject, playbackControls.transform.GetChild(1));
        var np1x = GameObject.Instantiate(friendScrollBar.transform.GetChild(3).gameObject, playbackControls.transform.GetChild(1));
        var playButton = GameObject.Instantiate(friendScrollBar.transform.GetChild(0).gameObject, playbackControls.transform.GetChild(1));

        var speedUpTexture = bundle.LoadAsset<Texture2D>("SpeedUp");
        var speedUpSprite = Sprite.Create(
            speedUpTexture,
            new Rect(0, 0, speedUpTexture.width, speedUpTexture.height),
            new Vector3(0.5f, 0.5f),
            100f
        );

        var compp5x = p5x.transform.GetChild(0).GetComponent<InteractionButton>();
        compp5x.enabled = true;
        compp5x.onPressed.RemoveAllListeners();
        compp5x.onPressed.AddListener((UnityAction)(() => { AddPlaybackSpeed(0.1f); }));
        compp5x.transform.GetChild(3).GetChild(0).GetComponent<Image>().sprite = speedUpSprite;

        p5x.name = "+0.1 Speed";
        p5x.transform.localScale = Vector3.one * 1.8f;
        p5x.transform.localPosition = new Vector3(0.1598f, -0.2665f, 0.096f);
        p5x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compnp5x = np5x.transform.GetChild(0).GetComponent<InteractionButton>();
        compnp5x.enabled = true;
        compnp5x.onPressed.RemoveAllListeners();
        compnp5x.onPressed.AddListener((UnityAction)(() => { AddPlaybackSpeed(-0.1f); }));
        compnp5x.transform.GetChild(3).GetChild(0).GetComponent<Image>().sprite = speedUpSprite;

        np5x.name = "-0.1 Speed";
        np5x.transform.localScale = Vector3.one * 1.8f;
        np5x.transform.localPosition = new Vector3(-0.1598f, -0.2665f, 0.096f);
        np5x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compp1x = p1x.transform.GetChild(0).GetComponent<InteractionButton>();
        compp1x.enabled = true;
        compp1x.onPressed.RemoveAllListeners();
        compp1x.onPressed.AddListener((UnityAction)(() => { AddPlaybackSpeed(1f); }));

        p1x.name = "+1 Speed";
        p1x.transform.localScale = Vector3.one * 1.8f;
        p1x.transform.localPosition = new Vector3(0.31f, -0.2665f, 0.096f);
        p1x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compnp1x = np1x.transform.GetChild(0).GetComponent<InteractionButton>();
        compnp1x.enabled = true;
        compnp1x.onPressed.RemoveAllListeners();
        compnp1x.onPressed.AddListener((UnityAction)(() => { AddPlaybackSpeed(-1f); }));

        np1x.name = "-1 Speed";
        np1x.transform.localScale = Vector3.one * 1.8f;
        np1x.transform.localPosition = new Vector3(-0.31f, -0.2665f, 0.096f);
        np1x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compplay = playButton.transform.GetChild(0).GetComponent<InteractionButton>();
        compplay.enabled = true;
        compplay.onPressed.RemoveAllListeners();
        
        ReplayPlaybackControls.playButtonSprite = compplay.transform.GetChild(3).GetChild(0).GetComponent<Image>();
        ReplayPlaybackControls.playSprite = ReplayPlaybackControls.playButtonSprite.sprite;
        
        compplay.onPressed.AddListener((UnityAction)(() => { TogglePlayback(isPaused); }));

        playButton.name = "Play Button";
        playButton.transform.localScale = Vector3.one * 2f;
        playButton.transform.localPosition = new Vector3(0, -0.2665f, 0.1156f);
        playButton.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var textureP = bundle.LoadAsset<Texture2D>("Pause");
        ReplayPlaybackControls.pauseSprite = Sprite.Create(
            textureP,
            new Rect(0, 0, textureP.width, textureP.height),
            new Vector3(0.5f, 0.5f),
            100f
        );

        ReplayPlaybackControls.playButtonSprite.sprite = ReplayPlaybackControls.pauseSprite;
        ReplayPlaybackControls.playButtonSprite.transform.localScale = Vector3.one * 0.8f;

        var stopReplayButton = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls.Frameattachment.RotationOptions.ResetRotationButton.GetGameObject(),
            playbackControls.transform.GetChild(1));

        var exitSceneButton = GameObject.Instantiate(stopReplayButton, playbackControls.transform.GetChild(1));

        stopReplayButton.name = "Stop Replay";
        stopReplayButton.transform.localPosition = new Vector3(-0.1527f, -0.6109f, 0f);
        stopReplayButton.transform.localScale = Vector3.one * 2f;
        stopReplayButton.transform.localRotation = Quaternion.identity;

        var stopReplayComp = stopReplayButton.transform.GetChild(0).GetComponent<InteractionButton>();
        stopReplayComp.enabled = true;
        stopReplayComp.onPressed.RemoveAllListeners();
        stopReplayComp.onPressed.AddListener((UnityAction)(() => { StopReplay(); }));

        var stopReplayTMP = stopReplayButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        stopReplayTMP.text = "Stop Replay";
        stopReplayTMP.color = new Color(0.8f, 0, 0);
        stopReplayTMP.ForceMeshUpdate();

        exitSceneButton.name = "Exit Scene";
        exitSceneButton.transform.localPosition = new Vector3(0.1527f, -0.6109f, 0f);
        exitSceneButton.transform.localScale = Vector3.one * 2f;
        exitSceneButton.transform.localRotation = Quaternion.identity;

        var exitSceneComp = exitSceneButton.transform.GetChild(0).GetComponent<InteractionButton>();
        exitSceneComp.enabled = true;
        exitSceneComp.onPressed.RemoveAllListeners();
        exitSceneComp.onPressed.AddListener((UnityAction)(() => { MelonCoroutines.Start(Utilities.LoadMap(1)); }));

        var exitSceneTMP = exitSceneButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        exitSceneTMP.text = "Exit Scene";
        exitSceneTMP.color = new Color(0.8f, 0, 0);
        exitSceneTMP.ForceMeshUpdate();

        var markerPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        markerPrefab.name = "ReplayMarker";
        markerPrefab.SetActive(false);
        
        var markerRenderer = markerPrefab.GetComponent<MeshRenderer>();
        markerRenderer.material = new Material(Shader.Find("Shader Graphs/RUMBLE_Prop"));

        var highlightMat = bundle.LoadAsset<Material>("Ghost");
        highlightMaterial = new GameObject("HighlightMaterial");
        highlightMaterial.AddComponent<MeshRenderer>().sharedMaterial = highlightMat;
        GameObject.DontDestroyOnLoad(highlightMaterial);

        ReplayPlaybackControls.playbackControls = playbackControls;
        ReplayPlaybackControls.markerPrefab = markerPrefab;
        ReplayPlaybackControls.timeline = timeline;
        ReplayPlaybackControls.currentDuration = currentDuration;
        ReplayPlaybackControls.totalDuration = totalDuration;
        ReplayPlaybackControls.playbackSpeedText = playbackSpeedText;
        ReplayPlaybackControls.playbackTitle = playbackTitle;
        ReplayPlaybackControls.destroyOnPunch = destroyOnPunchComp;
        
        // Replay Settings
        var replaySettingsPanel = GameObject.Instantiate(playbackControls, ReplayTable.transform);
        GameObject.Destroy(replaySettingsPanel.transform.GetChild(6).gameObject);
        replaySettingsPanel.name = "Replay Settings";
        replaySettingsPanel.transform.localScale = Vector3.one;
        replaySettingsPanel.transform.GetChild(0).GetChild(0).gameObject.layer = LayerMask.NameToLayer("Default");
        
        for (int i = 0; i < replaySettingsPanel.transform.GetChild(1).childCount; i++)
            GameObject.Destroy(replaySettingsPanel.transform.GetChild(1).GetChild(i).gameObject);

        replaySettingsPanel.transform.localPosition = new Vector3(0.3782f, 0.88f, 0.1564f);
        replaySettingsPanel.transform.localRotation = Quaternion.Euler(34.4376f, 90, 90);
        
        var povCameraButton = GameObject.Instantiate(
            Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.ResetFighterButton.GetGameObject(),
            replaySettingsPanel.transform.GetChild(1)
        );

        povCameraButton.name = "POV Button";
        povCameraButton.transform.localPosition = new Vector3(-0.4193f, 0.0749f, -0.0164f);
        povCameraButton.transform.localRotation = Quaternion.identity;
        povCameraButton.transform.localScale = Vector3.one * 2f;
        
        var povCameraButtonComp = povCameraButton.transform.GetChild(0).GetComponent<InteractionButton>();
        povCameraButtonComp.enabled = true;
        povCameraButtonComp.useLongPress = false;
        povCameraButtonComp.onPressedAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Press"];
        povCameraButtonComp.onPressed.RemoveAllListeners();
        povCameraButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            
            if (Calls.GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<Camera>().enabled)
            {
                MelonCoroutines.Start(ReplaySettings.SelectPlayer(selectedPlayer =>
                {
                    UpdateReplayCameraPOV(selectedPlayer, ReplaySettings.hideLocalPlayer);
                }, 0.5f));
            }
            else
            {
                ReplayError("Legacy cam must be enabled to use the POV feature.");
            }
        }));

        var hideLocalPlayerButton = GameObject.Instantiate(
            Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls.Frameattachment.RotationOptions.ResetRotationButton.GetGameObject(), 
            replaySettingsPanel.transform.GetChild(1)
        );

        hideLocalPlayerButton.name = "Hide Local Player Toggle";
        hideLocalPlayerButton.transform.localPosition = new Vector3(-0.394f, -0.1135f, -0.00116f);
        hideLocalPlayerButton.transform.localRotation = Quaternion.identity;
        hideLocalPlayerButton.transform.localScale = Vector3.one * 1.6f;
        
        var hideLocalPlayerTMP =  hideLocalPlayerButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        hideLocalPlayerTMP.text = "Hide Local Player";
        hideLocalPlayerTMP.transform.localScale = Vector3.one * 0.7f;
        
        var hideLocalPlayerComp = hideLocalPlayerButton.transform.GetChild(0).GetComponent<InteractionButton>();
        hideLocalPlayerComp.enabled = true;
        hideLocalPlayerComp.isToggleButton = true;
        hideLocalPlayerComp.onToggleFalseAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Unpress"];
        hideLocalPlayerComp.onToggleTrueAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Press"];
        hideLocalPlayerComp.onPressed.RemoveAllListeners();
        hideLocalPlayerComp.onToggleStateChanged.AddListener((UnityAction<bool>)(toggle =>
        {
            ReplaySettings.hideLocalPlayer = toggle;
            hideLocalPlayerTMP.color = toggle ? Color.green : Color.red;

            if (povPlayer != null)
                UpdateReplayCameraPOV(povPlayer, toggle);
        }));
        hideLocalPlayerComp.SetButtonToggleStatus(true, false, true);

        var povIconObj = new GameObject("Player Icon");
        povIconObj.transform.SetParent(povCameraButton.transform.GetChild(0));
        povIconObj.transform.localPosition = new Vector3(0.0008f, 0.012f, -0.0039f);
        povIconObj.transform.localRotation = Quaternion.Euler(90, 0, 0);
        povIconObj.transform.localScale = Vector3.one * 0.06f;

        var povIconTexture = bundle.LoadAsset<Texture2D>("POVIcon");
        povIconObj.AddComponent<SpriteRenderer>().sprite = Sprite.Create(
            povIconTexture,
            new Rect(0, 0, povIconTexture.width, povIconTexture.height),
            new Vector3(0.5f, 0.5f),
            100f
        );
        
        var openControlsButton = GameObject.Instantiate(
            Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.ResetFighterButton.GetGameObject(),
            replaySettingsPanel.transform.GetChild(1)
        );

        openControlsButton.name = "Open Controls Toggle";
        openControlsButton.transform.localPosition = new Vector3(0.3658f, -0.1428f, -0.0138f);
        openControlsButton.transform.localRotation = Quaternion.Euler(0, 0, 90);
        openControlsButton.transform.localScale = Vector3.one * 1.5f;

        var openControlsComp = openControlsButton.transform.GetChild(0).GetComponent<InteractionButton>();
        openControlsComp.enabled = true;
        openControlsComp.useLongPress = false;
        openControlsComp.onPressed.RemoveAllListeners();
        openControlsComp.onPressed.AddListener((UnityAction)(() =>
        {
            if (ReplayPlaybackControls.playbackControlsOpen) 
                ReplayPlaybackControls.Close();
            else 
                ReplayPlaybackControls.Open();
        }));

        var openControlsText = Calls.Create.NewText().GetComponent<TextMeshPro>();
        openControlsText.name = "Open Controls Text";
        openControlsText.transform.SetParent(openControlsComp.transform);
        openControlsText.transform.localPosition = new Vector3(0.0106f, 0.0117f, -0.1206f);
        openControlsText.transform.localRotation = Quaternion.Euler(90, 90, 0);
        openControlsText.transform.localScale = Vector3.one * 0.4f;

        openControlsText.text = ". . .";
        openControlsText.color = Color.white;
        openControlsText.ForceMeshUpdate();

        var slideOutPanel = GameObject.Instantiate(bundle.LoadAsset<GameObject>("SlideOutPlayerSelector"), replaySettingsPanel.transform.GetChild(1));

        slideOutPanel.name = "Player Selector Panel";
        slideOutPanel.transform.localScale = Vector3.one * 2.5f;
        slideOutPanel.transform.localPosition = new Vector3(0.1996f, 0.5273f, 0.16f);
        slideOutPanel.SetActive(false);

        var slideOutText = Calls.Create.NewText();

        slideOutText.transform.SetParent(slideOutPanel.transform);
        slideOutText.transform.localPosition = new Vector3(0.0022f, 0.072f, -0.004f);
        slideOutText.transform.localRotation = Quaternion.identity;
        slideOutText.transform.localScale = Vector3.one * 0.18f;

        var slideOutTextComp = slideOutText.GetComponent<TextMeshPro>();
        slideOutTextComp.text = "Player Selector";
        slideOutTextComp.color = new Color(0.1137f, 0.1059f, 0.0392f);
        slideOutTextComp.enableAutoSizing = true;
        slideOutTextComp.fontSizeMin = 1f;
        slideOutTextComp.ForceMeshUpdate();

        for (int i = 0; i < 4; i++)
        {
            int index = i;
            
            var playerTag = GameObject.Instantiate(
                Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Telephone20REDUXspecialedition.FriendScreen.PlayerTags.PlayerTag20.GetGameObject(),
                slideOutPanel.transform
            );

            playerTag.name = $"Player Tag {index}";
            playerTag.transform.localScale = Vector3.one * 0.5f;
            playerTag.transform.localPosition = new Vector3(0, -0.0073f + (-0.1091f * index), -0.0098f);
            playerTag.transform.localRotation = Quaternion.identity;

            var button = playerTag.transform.GetChild(0).GetComponent<InteractionButton>();
            button.onPressed.RemoveAllListeners();
            button.onPressed.AddListener((UnityAction)(() => { ReplaySettings.selectedPlayer = ReplaySettings.PlayerAtIndex(index).player; }));
        }

        var nextPageButton = GameObject.Instantiate(playButton, slideOutPanel.transform);
        nextPageButton.name = "Next Page";
        nextPageButton.transform.localPosition = new Vector3(0.0822f, -0.4184f, 0.0371f);
        nextPageButton.transform.localRotation = Quaternion.Euler(270, 0, 0);
        nextPageButton.transform.localScale = Vector3.one * 0.8f;
        nextPageButton.transform.GetChild(0).GetChild(3).GetChild(0).GetComponent<Image>().sprite = ReplayPlaybackControls.playSprite;

        var nextPageButtonComp = nextPageButton.transform.GetChild(0).GetComponent<InteractionButton>();
        nextPageButtonComp.enabled = true;
        nextPageButtonComp.onPressed.RemoveAllListeners();
        nextPageButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            ReplaySettings.SelectPlayerPage(ReplaySettings.currentPlayerPage + 1);
        }));
        
        var previousPageButton = GameObject.Instantiate(playButton, slideOutPanel.transform);
        previousPageButton.name = "Previous Page";
        previousPageButton.transform.localPosition = new Vector3(-0.0822f, -0.4184f, 0.0371f);
        previousPageButton.transform.localRotation = Quaternion.Euler(90, 180, 0);
        previousPageButton.transform.localScale = Vector3.one * 0.8f;
        previousPageButton.transform.GetChild(0).GetChild(3).GetChild(0).GetComponent<Image>().sprite = ReplayPlaybackControls.playSprite;

        var previousPageButtonComp = previousPageButton.transform.GetChild(0).GetComponent<InteractionButton>();
        previousPageButtonComp.enabled = true;
        previousPageButtonComp.onPressed.RemoveAllListeners();
        previousPageButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            ReplaySettings.SelectPlayerPage(ReplaySettings.currentPlayerPage - 1);
        }));

        var pageNumberText = Calls.Create.NewText();
        
        pageNumberText.transform.SetParent(slideOutPanel.transform);
        pageNumberText.transform.localPosition = new Vector3(0f, -0.4152f, -0.0062f);
        pageNumberText.transform.localRotation = Quaternion.identity;
        pageNumberText.transform.localScale = Vector3.one * 0.4f;
        pageNumberText.name = "Page Number";
        
        var pageNumberTextComp = pageNumberText.GetComponent<TextMeshPro>();
        pageNumberTextComp.text = "0 / 0";
        pageNumberTextComp.color = Color.white;
        pageNumberTextComp.horizontalAlignment = HorizontalAlignmentOptions.Center;
        pageNumberTextComp.ForceMeshUpdate();
        
        var timelineRS = GameObject.Instantiate(timeline, replaySettingsPanel.transform.GetChild(1));
        timelineRS.name = "Timeline";
        timelineRS.layer = LayerMask.NameToLayer("Default");
        timelineRS.transform.localPosition = new Vector3(0, -0.2726f, 0);
        timelineRS.transform.localScale = new Vector3(0.9346f, 0.0529f, 1.9291f);
        timelineRS.transform.localRotation = Quaternion.identity;
        
        var durationRS = GameObject.Instantiate(totalDuration, replaySettingsPanel.transform.GetChild(1));
        durationRS.name = "TotalDuration";
        durationRS.gameObject.layer = LayerMask.NameToLayer("Default");
        durationRS.transform.localPosition = new Vector3(0.2265f, -0.1898f, 0);
        
        var replayNameTitle = GameObject.Instantiate(playbackTitle.gameObject, replaySettingsPanel.transform.GetChild(1));
        replayNameTitle.transform.localPosition = new Vector3(0, 0.6316f, 0);
        replayNameTitle.name = "Replay Title";
        replayNameTitle.layer = LayerMask.NameToLayer("Default");
        
        var dateText = GameObject.Instantiate(playbackTitle.gameObject, replaySettingsPanel.transform.GetChild(1));
        dateText.transform.localPosition = new Vector3(0, 0.4989f, 0);
        dateText.transform.localScale = Vector3.one * 0.7f;
        dateText.name = "Date";
        dateText.layer = LayerMask.NameToLayer("Default");

        var deleteButton = GameObject.Instantiate(crystalizeButton, replaySettingsPanel.transform.GetChild(1));
        deleteButton.name = "DeleteReplay";
        deleteButton.transform.localPosition = new Vector3(0.21f, -0.5582f, -0.0138f);
        deleteButton.transform.localRotation = Quaternion.identity;
        deleteButton.transform.localScale = Vector3.one * 2f;

        var deleteButtonComp = deleteButton.transform.GetChild(0).GetComponent<InteractionButton>();

        deleteButtonComp.onPressedAudioCall = loadReplayButtonComp.onPressedAudioCall;
        
        deleteButtonComp.OnPressed.RemoveAllListeners();
        deleteButtonComp.OnPressed.AddListener((UnityAction)(() =>
        {
            if (ReplayFiles.explorer.currentIndex != -1)
            {
                if (crystalBreakCoroutine == null)
                {
                    ReplayCrystals.Crystal crystal = ReplayCrystals.Crystals.FirstOrDefault(c => c.ReplayPath == ReplayFiles.explorer.CurrentReplayPath);
                    crystalBreakCoroutine = MelonCoroutines.Start(ReplayCrystals.CrystalBreakAnimation(ReplayFiles.explorer.CurrentReplayPath, crystal));
                }
            }
            else 
            {
                ReplayError();
            }
        }));
        
        var srD = deleteButton.transform.GetChild(0).GetChild(3).GetComponent<SpriteRenderer>();
        var textureD = bundle.LoadAsset<Texture2D>("trashcan");
        srD.sprite = Sprite.Create(
            textureD,
            new Rect(0, 0, textureD.width, textureD.height),
            new Vector3(0.5f, 0.5f),
            100f
        );
        srD.color = new Color(1, 0.163f, 0.2132f, 1);
        srD.transform.localRotation = Quaternion.Euler(270, 180, 0);
        srD.transform.localScale = Vector3.one * 0.01f;
        srD.name = "DeleteIcon";

        var replayNameComp = replayNameTitle.GetComponent<TextMeshPro>();
        replayNameComp.enableAutoSizing = true;
        replayNameComp.fontSizeMin = 0.7f;
        replayNameComp.fontSizeMax = 1.2f;
        
        var dateComp = dateText.GetComponent<TextMeshPro>();
        dateComp.enableAutoSizing = true;
        dateComp.fontSizeMin = 0.7f;
        dateComp.fontSizeMax = 1.2f;

        var renameInstructions = GameObject.Instantiate(dateText, replaySettingsPanel.transform.GetChild(1));
        renameInstructions.name = "Rename Instructions";
        renameInstructions.transform.localPosition = new Vector3(0, 0.3156f, 0);
        renameInstructions.transform.localScale = Vector3.one;
        renameInstructions.SetActive(false);
        
        var renameInstructionsComp = renameInstructions.GetComponent<TextMeshPro>();
        renameInstructionsComp.text = "Please type on your keyboard to rename\n<#32a832>Enter - Confirm     <#cc1b1b>Esc - Cancel";
        
        var deleteReplayText = GameObject.Instantiate(renameInstructions, deleteButton.transform);
        deleteReplayText.name = "DeleteText";
        deleteReplayText.transform.localPosition = new Vector3(0.0143f, 0.0713f, 0);
        deleteReplayText.SetActive(true);
        deleteReplayText.transform.localScale = Vector3.one * 0.2f;
        
        var deleteReplayTextComp = deleteReplayText.GetComponent<TextMeshPro>();
        deleteReplayTextComp.text = "<#cc1b1b>DELETE REPLAY";
        
        var copyPathButton = GameObject.Instantiate(deleteButton, replaySettingsPanel.transform.GetChild(1));
        copyPathButton.name = "CopyPathButton";
        copyPathButton.transform.localPosition = new Vector3(-0.0337f, -0.5589f, -0.0138f);
        copyPathButton.transform.localRotation = Quaternion.identity;

        copyPathButton.transform.GetChild(2).GetComponent<TextMeshPro>().text = "Copy Path";
        copyPathButton.transform.GetChild(2).GetComponent<TextMeshPro>().ForceMeshUpdate();
        copyPathButton.transform.GetChild(2).localPosition = new Vector3(0.0143f, 0.0713f, 0);

        var srCo = copyPathButton.transform.GetChild(0).GetChild(3).GetComponent<SpriteRenderer>();
        var textureCo = bundle.LoadAsset<Texture2D>("copytoclipboard");
        srCo.sprite = Sprite.Create(
            textureCo,
            new Rect(0, 0, textureCo.width, textureCo.height),
            new Vector3(0.5f, 0.5f),
            100f
        );
        srCo.color = Color.white;
        srCo.transform.localRotation = Quaternion.Euler(270, 180, 0);
        srCo.transform.localScale = Vector3.one * -0.015f;
        srCo.name = "CopyPathIcon";
        
        var copyPathButtonComp = copyPathButton.transform.GetChild(0).GetComponent<InteractionButton>();
        copyPathButtonComp.useLongPress = false;
        
        copyPathButtonComp.onPressed.RemoveAllListeners();
        copyPathButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            GUIUtility.systemCopyBuffer = ReplayFiles.explorer.CurrentReplayPath;
        }));
        
        replaySettings = replaySettingsPanel.AddComponent<ReplaySettings>();
        
        var renameButton = GameObject.Instantiate(deleteButton, replaySettingsPanel.transform.GetChild(1));
        renameButton.name = "RenameButton";
        renameButton.transform.localPosition = new Vector3(-0.2817f, -0.556f, -0.0138f);
        renameButton.transform.localRotation = Quaternion.identity;
        
        var renameButtonComp = renameButton.transform.GetChild(0).GetComponent<InteractionButton>();
        renameButtonComp.onPressed.RemoveAllListeners();

        renameButtonComp.isToggleButton = true;
        renameButtonComp.useLongPress = false;
        renameButtonComp.onToggleFalseAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Unpress"];
        renameButtonComp.onToggleTrueAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Press"];
        renameButtonComp.onToggleStateChanged.AddListener((UnityAction<bool>)((bool toggleState) =>
        {
            if (ReplayFiles.explorer.currentIndex != -1)
            {
                replaySettings.OnRenamePressed(!toggleState);
            }
        }));

        renameButton.transform.GetChild(2).GetComponent<TextMeshPro>().text = "Rename";
        renameButton.transform.GetChild(2).GetComponent<TextMeshPro>().ForceMeshUpdate();
        renameButton.transform.GetChild(2).localPosition = new Vector3(0.0171f, 0.0713f, 0);
        GameObject.Destroy(renameButton.transform.GetChild(0).GetChild(3).gameObject);
        
        ReplaySettings.deleteButton = deleteButtonComp;
        ReplaySettings.replayName = replayNameComp;
        ReplaySettings.dateText = dateComp;
        ReplaySettings.renameInstructions = renameInstructionsComp;
        ReplaySettings.renameButton = renameButtonComp;
        ReplaySettings.timeline = timelineRS;
        ReplaySettings.durationComp = durationRS;
        ReplaySettings.slideOutPanel = slideOutPanel;
        ReplaySettings.pageNumberText = pageNumberTextComp;
        ReplaySettings.povButton = povCameraButton;
        ReplaySettings.hideLocalPlayerToggle = hideLocalPlayerButton;
        ReplaySettings.openControlsButton = openControlsButton;
        
        GameObject.DontDestroyOnLoad(ReplayTable);
        GameObject.DontDestroyOnLoad(crystalPrefab);
        GameObject.DontDestroyOnLoad(playbackControls);
        GameObject.DontDestroyOnLoad(clapperboardVFX);
        GameObject.DontDestroyOnLoad(markerPrefab);
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
        PlayerInfos.Clear();
        StructureInfos.Clear();
        Pedestals.Clear();

        recordingSceneName = currentScene;
        
        foreach (var structure in CombatManager.instance.structures)
            TryRegisterStructure(structure);
        
        foreach (var player in PlayerManager.instance.AllPlayers)
        {
            if (player == null) continue;

            RecordedPlayers.Add(player);

            MelonCoroutines.Start(Patches.Patch_PlayerVisuals_ApplyPlayerVisuals.VisualDataDelay(player));
        }

        Pedestals.AddRange(Utilities.EnumerateMatchPedestals());
    }

    public bool TryRegisterStructure(Structure structure)
    {
        if (!isRecording && !isBuffering)
            return false;

        if (structure == null)
            return false;

        if (Structures.Contains(structure))
            return false;
        
        if (structure.name.StartsWith("Static Target") || structure.name.StartsWith("Moving Target"))
            return false;

        Structures.Add(structure);

        var name = structure.resourceName;
        
        StructureType type = name switch
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
        };

        if (type == StructureType.Ball &&
            structure.transform.childCount >= 3 &&
            structure.transform.GetChild(2).name == "Ballcage")
        {
            type = structure.GetComponent<Tetherball>() != null ? StructureType.TetheredCagedBall : StructureType.CagedBall;
        }
        
        StructureInfos.Add(new StructureInfo
        {
            Type = type
        });

        return true;
    }

    public Marker AddMarker(string name, Color color)
    {
        return AddMarker(name, color, Time.time);
    }

    public Marker AddMarker(string name, Color color, float time)
    {
        if (!isRecording && !isBuffering)
            return null;

        var marker = new Marker(name, time, color);

        if (isRecording)
            recordingMarkers.Add(marker);

        if (isBuffering)
            bufferMarkers.Enqueue(marker);

        return marker;
    }
    
    // ----- Save Recordings ------
    
    public void SaveReplay(Frame[] frames, List<Marker> markers, string logPrefix, bool isBufferClip = false, Action<ReplayInfo, string> onSave = null)
    {
        if (frames.Length == 0)
        {
            LoggerInstance.Warning($"{logPrefix} stopped, but no frames were captured. Replay was not saved.");
            return;
        }
        
        float duration = frames[^1].Time - frames[0].Time;

        string customMap = Utilities.GetActiveCustomMapName();

        if (string.IsNullOrEmpty(customMap))
        {
            string[] rebuilt = Utilities.RebuildCustomMapFromScene();
            
            if (rebuilt != null)
                customMap = string.Join("|", rebuilt);
        }

        if (currentScene == "Gym" && flatLandRoot?.gameObject.activeSelf == false)
            customMap = "FlatLandSingle";

        float startTime = frames[0].Time;

        foreach (var f in frames)
            f.Time -= startTime;

        foreach (var m in markers)
            m.time -= startTime;

        var replayInfo = new ReplayInfo
        {
            Header = new ReplaySerializer.ReplayHeader
            {
                Version = BuildInfo.Version,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Scene = recordingSceneName,
                Duration = duration,
                CustomMap = customMap,
                FrameCount = frames.Length,
                PedestalCount = Pedestals.Count,
                MarkerCount = markers.Count,
                AvgPing = pingCount > 0 ? pingSum / pingCount : -1,
                MinPing = pingMin,
                MaxPing = pingMax,
                TargetFPS = (int)TargetRecordingFPS.SavedValue,
                Players = PlayerInfos.Values.ToArray(),
                Structures = StructureInfos.ToArray(),
                Guid = Guid.NewGuid().ToString()
            },
            Frames = frames
        };
        
        replayInfo.Header.MarkerCount = markers.Count;
        replayInfo.Header.Markers = markers.ToArray();
        
        string pattern = Utilities.GetFriendlySceneName(recordingSceneName) switch
        {
            "Gym" => ReplayFiles.LoadFormatFile("AutoNameFormats/gym"),
            "Park" => ReplayFiles.LoadFormatFile("AutoNameFormats/park"),
            "Pit" or "Ring" => ReplayFiles.LoadFormatFile("AutoNameFormats/match"),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(pattern))
        {
            ReplayError($"{logPrefix} was not saved due to missing name format file.");
            pattern = "{Host} vs {Client} - {Scene}";
        }

        replayInfo.Header.Title = ReplaySerializer.FormatReplayString(pattern, replayInfo.Header);
        
        LoggerInstance.Msg($"{logPrefix} saved after {duration:F2}s ({frames.Length} frames)");

        string path = $"{ReplayFiles.replayFolder}/Replays/{Utilities.GetReplayName(replayInfo, isBufferClip)}";
        ReplaySerializer.BuildReplayPackage(
            path,
            replayInfo,
            () =>
            {
                AudioManager.instance.Play(ReplayCache.SFX["Call_PoseGhost_PosePerformed"], 
                    LocalPlayer.Controller.GetSubsystem<PlayerVR>().transform.position);
                LoggerInstance.Msg($"{logPrefix} saved to disk: '{path}'");
                
                if ((bool)EnableHaptics.SavedValue)
                    LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.15f, 1f, 0.15f);
                
                if (recordingIcon != null)
                {
                    recordingIcon.color = Color.green;
            
                    MelonCoroutines.Start(Utilities.LerpValue(
                        () => recordingIcon.color,
                        c => recordingIcon.color = c,
                        Color.Lerp,
                        isBufferClip && isRecording ? Color.red : new Color (0, 1, 0, 0),
                        0.7f,
                        Utilities.EaseOut,
                        delay: 0.1f
                    ));
                }

                onSave?.Invoke(replayInfo, path);

                ReplayFiles.ReloadReplays();
            }
        );
    }

    public void StartBuffering()
    {
        SetupRecordingData();
        replayBuffer.Clear();
        bufferMarkers.Clear();
        isBuffering = true;
    }
    
    public void SaveReplayBuffer()
    {
        var frames = replayBuffer.ToArray();
        SaveReplay(frames, bufferMarkers.ToList(), "Replay Buffer", true, (info, path) => ReplayAPI.ReplaySavedInternal(info, true, path));
    }
    
    
    // ----- Replay Loading -----

    public void LoadSelectedReplay()
    {
        if (ReplayFiles.explorer.currentIndex == -1)
        {
            ReplayError("Could not find file.");
            return;
        }
        
        if (currentScene == "Park" && (PhotonNetwork.CurrentRoom?.PlayerCount ?? 0) > 1)
        {
            ReplayError("Cannot start replay in room with more than 1 player.");
            return;
        }
        
        if (isPlaying)
            StopReplay();

        string targetScene = ReplayFiles.currentHeader.Scene;
        bool switchingScene = targetScene != currentScene;

        bool isCustomMap = targetScene is "Map0" or "Map1" && !string.IsNullOrWhiteSpace(ReplayFiles.currentHeader.CustomMap);
        bool isRawMapData = !string.IsNullOrWhiteSpace(ReplayFiles.currentHeader.CustomMap) && ReplayFiles.currentHeader.CustomMap.Split('|').Length > 15;

        GameObject replayCustomMap = null;

        if (ReplayFiles.currentHeader.CustomMap == "FlatLandSingle")
        {
            if (flatLandRoot?.gameObject.activeSelf == false)
            {
                switchingScene = false;
            }
            else
            {
                var button = flatLandRoot?.transform?.GetChild(1)?.GetChild(0)?.GetComponent<InteractionButton>();
                if (button == null)
                {
                    ReplayError("Could not load FlatLand Replay. Please make sure FlatLand is installed.");
                    return;
                }
                
                button.RPC_OnPressed();
                MelonCoroutines.Start(DelayedFlatLandLoad());
                return;
            }
        }
        
        if (switchingScene && isCustomMap && !isRawMapData)
        {
            replayCustomMap = Utilities.GetCustomMap(ReplayFiles.currentHeader.CustomMap);

            if (replayCustomMap == null)
                return;
        }

        if (switchingScene)
        {
            ReplayFiles.HideMetadata();

            int sceneIndex = targetScene switch
            {
                "Map0" => 3,
                "Map1" => 4,
                "Park" => 2,
                "Gym" => 1,
                _ => -1
            };

            if (sceneIndex == -1)
            {
                ReplayError($"Unknown scene '{targetScene}'");
                return;
            }

            if (sceneIndex == 2)
            {
                var parkboard = Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Parkboard.
                    GetGameObject().
                    GetComponent<ParkBoardGymVariant>();

                parkboard.doorPolicySlider.SetStep(1);
                parkboard.HostPark();

                isReplayScene = true;
            }
            else
            {
                MelonCoroutines.Start(
                    Utilities.LoadMap(sceneIndex, 2.5f, () =>
                    {
                        LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
                        ReplayFiles.ShowMetadata();

                        switch (targetScene)
                        {
                            case "Map0":
                            {
                                Calls.GameObjects.Map0.Logic.MatchHandler.GetGameObject().SetActive(false);
                                break;
                            }
                            case "Map1":
                            {
                                Calls.GameObjects.Map1.Logic.MatchHandler.GetGameObject().SetActive(false);
                                Calls.GameObjects.Map1.Logic.SceneProcessors.GetGameObject().SetActive(false);
                                break;
                            }
                            case "Park":
                            {
                                Calls.GameObjects.Park.LOGIC.ParkInstance
                                    .GetGameObject()
                                    .SetActive(false);
                                break;
                            }
                        }

                        if (isCustomMap)
                        {
                            if (replayCustomMap != null)
                            {
                                replayCustomMap.SetActive(true);
                            }
                            else if (isRawMapData)
                            {
                                var type = RegisteredMelons.FirstOrDefault(m => m.Info.Name == "CustomMultiplayerMaps")?.MelonAssembly?.Assembly?.GetTypes()?.FirstOrDefault(t => t.Name == "main");
                                if (type != null)
                                {
                                    var method = type.GetMethod(
                                        "LoadCustomMap", 
                                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                                        null,
                                        new[] { typeof(string[]) },
                                        null
                                    );
                                    
                                    string[] split = ReplayFiles.currentHeader.CustomMap.Split('|');
                                    method?.Invoke(null, new object[] { split });
                                }
                            }
                            
                            if (targetScene == "Map0")
                                Calls.GameObjects.Map0.Map0production
                                    .GetGameObject()
                                    .SetActive(false);
                            else if (targetScene == "Map1")
                                Calls.GameObjects.Map1.Map1production
                                    .GetGameObject()
                                    .SetActive(false);
                        }

                        SimpleScreenFadeInstance.Progress = 0f;
                    }, 2f)
                );
            }
        }
        else
        {
            if (currentScene == "Park")
                MelonCoroutines.Start(DelayedParkLoad());
            else
                LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
            
            SimpleScreenFadeInstance.Progress = 0f;
        }
    }
    
    public void LoadReplay(string path)
    {
        currentReplay = ReplaySerializer.LoadReplay(path);

        SetPlaybackSpeed(1f);
        
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
        replayStructures = new GameObject("Replay Structures");
        
        for (int i = 0; i < PlaybackStructures.Length; i++)
        {
            var type = currentReplay.Header.Structures[i].Type;
            PlaybackStructures[i] = ReplayCache.structurePools.GetValueOrDefault(type).FetchFromPool().gameObject;

            if (disableBaseStructureSystems)
            {
                Structure structure = PlaybackStructures[i].GetComponent<Structure>();
                structure.indistructable = true;
                structure.onBecameFreeAudio = null;
                structure.onBecameGroundedAudio = null;

                foreach (var col in structure.GetComponentsInChildren<Collider>())
                    col.enabled = false;
            
                GameObject.Destroy(PlaybackStructures[i].GetComponent<Rigidbody>());
            
                if (PlaybackStructures[i].TryGetComponent<NetworkGameObject>(out var networkGameObject))
                    GameObject.Destroy(networkGameObject);
                
                if (isRecording || isBuffering)
                    TryRegisterStructure(structure);
            }
            
            PlaybackStructures[i].SetActive(false);
            PlaybackStructures[i].transform.SetParent(replayStructures.transform);
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

            playbackStructureStates[i] = new PlaybackStructureState();

            if (!grounded)
                playbackStructureStates[i].currentState = StructureStateType.Normal;
        }
        
        // ------ Players ------

        if (PlaybackPlayers != null)
        {
            foreach (var player in PlaybackPlayers)
            {
                if (player == null) continue;
                PlayerManager.instance.AllPlayers.Remove(player.Controller.assignedPlayer);
            }
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

            for (int i = 0; i < PlaybackPlayers.Length; i++)
            {
                var shiftstones = PlaybackPlayers[i].Controller.GetSubsystem<PlayerShiftstoneSystem>().GetCurrentShiftStoneConfiguration();
                
                playbackPlayerStates[i] = new PlaybackPlayerState
                {
                    playerMeasurement = PlaybackPlayers[i].Controller.assignedPlayer.Data.PlayerMeasurement,
                    leftShiftstone = shiftstones[0],
                    rightShiftstone = shiftstones[1]
                };
            }
        
            ReorderPlayers();

            if (currentReplay.Header.Scene == "Gym")
            {
                foreach (var playbackPlayer in PlaybackPlayers)
                {
                    playbackPlayer.Controller.transform.GetChild(6).gameObject.SetActive(false);
                    playbackPlayer.Controller.transform.GetChild(9).gameObject.SetActive(false);
                }
            }
        
            isPlaying = true;
            TogglePlayback(true);
            
            ReplayAPI.ReplayStartedInternal(currentReplay);
        }));
        
        // Playback Controls
        var timelineRenderer = ReplayPlaybackControls.timeline.GetComponent<MeshRenderer>();
        
        timelineRenderer.material.SetFloat("_BP_Target", currentReplay.Header.Duration * 1000f);
        timelineRenderer.material.SetFloat("_BP_Current", 0f);

        TimeSpan t = TimeSpan.FromSeconds(currentReplay.Header.Duration);

        ReplayPlaybackControls.totalDuration.text = t.TotalHours >= 1 ? 
            $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : 
            $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        
        ReplayPlaybackControls.currentDuration.text = "0:00";

        ReplayPlaybackControls.playbackTitle.text = Path.GetFileNameWithoutExtension(path).StartsWith("Replay") 
            ? currentReplay.Header.Title 
            : Path.GetFileNameWithoutExtension(path);

        ReplaySettings.playerList = ReplaySettings.PaginateReplay(currentReplay.Header, PlaybackPlayers);
        ReplaySettings.SelectPlayerPage(0);
        ReplaySettings.povButton.SetActive(true);
        ReplaySettings.hideLocalPlayerToggle.SetActive(true);
        ReplaySettings.openControlsButton.SetActive(true);
        
        ReplayPlaybackControls.timeline.transform.GetChild(0).GetComponent<TimelineScrubber>().header = currentReplay.Header;

        AddMarkers(currentReplay.Header, timelineRenderer);
    }

    public static Marker[] AddMarkers(ReplaySerializer.ReplayHeader header, MeshRenderer timelineRenderer, bool hideMarkers = true)
    {
        foreach (var marker in timelineRenderer.transform.GetComponentsInChildren<ReplayTag>())
            GameObject.Destroy(marker.gameObject);
        
        if (header.Markers == null)
            return null;
        
        var markers = header.Markers;
        
        foreach (var marker in markers)
        {
            Vector3 position = Utilities.GetPositionOverMesh(marker.time, header.Duration, timelineRenderer);
            GameObject markerObj = GameObject.Instantiate(ReplayPlaybackControls.markerPrefab, timelineRenderer.transform);

            if (!hideMarkers)
                markerObj.layer = LayerMask.NameToLayer("Default");

            markerObj.transform.localScale = new Vector3(0.0062f, 1.0836f, 0.0128f);
            markerObj.transform.position = position;

            Color markerColor = new Color(marker.r, marker.g, marker.b, 1f);
            markerObj.GetComponent<MeshRenderer>().material.SetColor("_Overlay", markerColor);
            markerObj.AddComponent<ReplayTag>();
            markerObj.SetActive(true);
        }

        return markers;
    }
    
    public void StopReplay()
    {
        if (!isPlaying) return;
        isPlaying = false;
        ReplayPlaybackControls.Close();
        
        UpdateReplayCameraPOV(LocalPlayer);
        TogglePlayback(true, ignoreIsPlaying: true);
        SetPlaybackSpeed(1f);

        foreach (var structure in PlaybackStructures)
        {
            if (structure == null) continue;

            var comp = structure.GetComponent<Structure>();
            comp.indistructable = false;
            
            if (comp.currentFrictionVFX != null)
                GameObject.Destroy(comp.currentFrictionVFX.gameObject);

            foreach (var effect in structure.GetComponentsInChildren<VisualEffect>())
                GameObject.Destroy(effect);
        }

        foreach (var structure in HiddenStructures)
        {
            if (structure != null)
                structure.gameObject.SetActive(true);
        }
        
        HiddenStructures.Clear();
        
        if (replayStructures != null)
            GameObject.Destroy(replayStructures);

        foreach (var player in PlaybackPlayers)
            PlayerManager.instance.AllPlayers.Remove(player.Controller.assignedPlayer);
        
        if (replayPlayers != null)
            GameObject.Destroy(replayPlayers);

        if (pedestalsParent != null)
        {
            for (int i = pedestalsParent.transform.childCount - 1; i >= 0; i--)
            {
                var pedestal = pedestalsParent.transform.GetChild(i);
                pedestal.transform.SetParent(null);
                pedestal.gameObject.SetActive(false);
            }
            GameObject.Destroy(pedestalsParent);
        }

        if (ReplayRoot != null)
            GameObject.Destroy(ReplayRoot);
        
        ReplaySettings.povButton.SetActive(false);
        ReplaySettings.hideLocalPlayerToggle.SetActive(false);
        ReplaySettings.openControlsButton.SetActive(false); 

        replayStructures = null;
        replayPlayers = null;
        PlaybackStructures = null;
        PlaybackPlayers = null;

        ReplayAPI.ReplayEndedInternal(currentReplay);
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
        Player local = LocalPlayer;
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
            MelonCoroutines.Start(BuildClone(currentReplay.Header.Players[i], c => temp = c));

            while (temp == null)
                yield return null;

            PlaybackPlayers[i] = temp;
            PlaybackPlayers[i].Controller.transform.SetParent(replayPlayers.transform);
        }

        done?.Invoke();
    }

    public static IEnumerator BuildClone(PlayerInfo pInfo, Action<Clone> callback, Vector3 initialPosition = default)
    {
        var randomID = Guid.NewGuid().ToString();
        
        pInfo.Name = string.IsNullOrEmpty(pInfo.Name) ? $"Player_{pInfo.MasterId}" : pInfo.Name;
        
        pInfo.EquippedShiftStones ??= new short[] { -1, -1 };
        var shiftstones = new Il2CppStructArray<short>(2);
        
        for (int i = 0; i < 2; i++)
            shiftstones[i] = pInfo.EquippedShiftStones[i];
        
        PlayerData data = new PlayerData(
            new GeneralData
            {
                PlayFabMasterId = $"{pInfo.MasterId}_{randomID}",
                PlayFabTitleId = randomID,
                BattlePoints = pInfo.BattlePoints,
                PublicUsername = pInfo.Name
            },
            LocalPlayer.Data.RedeemedMoves,
            LocalPlayer.Data.EconomyData,
            shiftstones,
            PlayerVisualData.Default
        ); 
        
        data.PlayerMeasurement = pInfo.Measurement.Length != 0 ? pInfo.Measurement : LocalPlayer.Data.PlayerMeasurement;

        Player newPlayer = Player.CreateRemotePlayer(data);
        PlayerManager.instance.AllPlayers.Add(newPlayer);
        PlayerManager.instance.SpawnPlayerController(newPlayer, initialPosition, Quaternion.identity);

        while (newPlayer.Controller == null)
            yield return null;

        GameObject body = newPlayer.Controller.gameObject;
        
        body.name = $"Player_{pInfo.MasterId}";
        
        GameObject Overall = body.transform.GetChild(2).gameObject;
        GameObject LHand = Overall.transform.GetChild(1).gameObject;
        GameObject RHand = Overall.transform.GetChild(2).gameObject;
        GameObject Head = Overall.transform.GetChild(0).GetChild(0).gameObject;

        GameObject.Destroy(Overall.GetComponent<NetworkGameObject>());
        Overall.GetComponent<Rigidbody>().isKinematic = true;
        
        var localTransform = LocalPlayer.Controller.transform;
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

        var poseSystem = body.GetComponent<PlayerPoseSystem>();
        foreach (var pose in LocalPlayer.Controller.GetSubsystem<PlayerPoseSystem>().currentInputPoses)
            poseSystem.currentInputPoses.Add(new PoseInputSource(pose.PoseSet));
        poseSystem.enabled = true;

        var clone = newPlayer.Controller.gameObject.AddComponent<Clone>();

        clone.VRRig = Overall;
        clone.LeftHand = LHand;
        clone.RightHand = RHand;
        clone.Head = Head;
        clone.Controller = newPlayer.Controller;

        callback?.Invoke(clone);
    }
    
    
    // ----- Update Loops -----
    
    public override void OnUpdate()
    {
        HandleReplayPose();
        ReplayPlaybackControls.Update();

        if (currentScene != "Loader")
            ReplayCrystals.HandleCrystals();

        if (currentScene != "Gym" || replayTable == null || replayTable.gameObject || replayTable.metadataText == null)
            return;

        bool flatLandActive = flatLandRoot != null && flatLandRoot?.activeSelf == true;

        if (lastFlatLandActive == flatLandActive)
            return;

        lastFlatLandActive = flatLandActive;
        
        replayTable.gameObject.SetActive(true);
        replayTable.metadataText.gameObject.SetActive(true);

        if (!flatLandActive)
        {
            replayTable.tableFloat.startPos = new Vector3(5.9641f, 1.1323f, -4.5477f);
            replayTable.metadataTextFloat.startPos = new Vector3(5.9641f, 1.1323f, -4.5477f);
            
            replayTable.tableOffset = -0.4f;
            replayTable.transform.rotation = Quaternion.Euler(270, 253.3632f, 0);
        }
        else
        {
            replayTable.tableFloat.startPos = new Vector3(5.9506f, 1.3564f, 4.1906f);
            replayTable.metadataTextFloat.startPos = new Vector3(5.9575f, 1.8514f, 4.2102f);
        
            replayTable.tableOffset = 0f;
            replayTable.transform.localRotation = Quaternion.Euler(270, 121.5819f, 0);
        }
    }

    public override void OnLateUpdate()
    {
        HandleRecording();
        HandlePlayback();
    }

    public override void OnFixedUpdate()
    {
        if (currentScene == "Loader") return;

        var rightActions = ((string)RightHandControls.SavedValue).Split(',').Select(a => a.Trim()).ToArray();
        var leftActions = ((string)LeftHandControls.SavedValue).Split(',').Select(a => a.Trim()).ToArray();
        
        TryHandleController(
            leftActions,
            Calls.ControllerMap.LeftController.GetPrimary(),
            Calls.ControllerMap.LeftController.GetSecondary(),
            true
        );
        
        TryHandleController(
            rightActions,
            Calls.ControllerMap.RightController.GetPrimary(),
            Calls.ControllerMap.RightController.GetSecondary(),
            false
        );
    }
    
    
    // ----- Recording -----
    
    public void HandleRecording()
    {
        if ((!isRecording && !isBuffering) || SceneManager.instance.IsLoadingScene) return;

        float sampleRate = 1f / (int)TargetRecordingFPS.SavedValue;

        if (Time.time - lastSampleTime >= sampleRate)
        {
            lastSampleTime = Time.time;

            var frame = CaptureFrame();
            frame.Time = Time.time;

            if (isRecording)
            {
                var cloned = frame.Clone();
                ReplayAPI.OnRecordFrameInternal(cloned, false);
                Frames.Add(cloned);
            }
            
            if (isBuffering)
            {
                var cloned = frame.Clone();
                ReplayAPI.OnRecordFrameInternal(cloned, true);
                replayBuffer.Enqueue(cloned);

                float cutoffTime = frame.Time - (int)ReplayBufferDuration.SavedValue;
                while (replayBuffer.Count > 0 && replayBuffer.Peek().Time < cutoffTime)
                    replayBuffer.Dequeue();
            }

            Patches.activations.Clear();
            Events.Clear();
        }

        if (Time.time - pingTimer >= 1f)
        {
            pingTimer = Time.time;

            bool updated = false;

            if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer?.CustomProperties?.TryGetValue("ping", out var objPing) == true)
            {
                int ping = objPing.Unbox<int>();

                if (!PhotonNetwork.IsMasterClient && PhotonNetwork.MasterClient?.CustomProperties?.TryGetValue("ping", out var objHostPing) == true)
                    ping += objHostPing.Unbox<int>();

                if (ping >= 0)
                {
                    pingSum += ping;
                    pingCount++;

                    if (ping < pingMin) pingMin = ping;
                    if (ping > pingMax) pingMax = ping;

                    updated = true;
                }
            }

            if (!updated && PhotonNetwork.IsConnected)
            {
                int fallbackPing = PhotonNetwork.GetPing();

                pingSum += fallbackPing;
                pingCount++;

                if (fallbackPing < pingMin) pingMin = fallbackPing;
                if (fallbackPing > pingMax) pingMax = fallbackPing;
            }
        }
    }
    
    Frame CaptureFrame()
    {
        var flickedStructures = new HashSet<GameObject>();

        // Players
        var playerStates = Utilities.NewArray<PlayerState>(RecordedPlayers.Count);

        for (int i = 0; i < RecordedPlayers.Count; i++)
        {
            var p = RecordedPlayers[i];
            
            if (p == null)
            {
                playerStates[i] = new PlayerState { active = false };
                continue;
            }

            var stackProc = p.Controller?.GetSubsystem<PlayerStackProcessor>();

            if (stackProc == null)
                continue;

            Stack flickStack = null;

            foreach (var stack in stackProc.availableStacks)
            {
                if (stack == null)
                    continue;

                if (stack.cachedName == "Flick")
                    flickStack = stack;

                if (flickStack?.runningExecutions != null)
                {
                    foreach (var exec in flickStack.runningExecutions)
                    {
                        if (exec?.TargetProcessable == null)
                            continue;
                        
                        var proc = exec.TargetProcessable.TryCast<ProcessableComponent>();
                        
                        if (proc?.gameObject != null)
                            flickedStructures.Add(proc.gameObject);
                    }
                }
            }
            
            Transform VRRig = p.Controller.transform.GetChild(2);
            Transform LHand = VRRig.GetChild(1);
            Transform RHand = VRRig.GetChild(2);
            Transform Head = VRRig.GetChild(0).GetChild(0);

            var shiftstoneSystem = p.Controller.GetSubsystem<PlayerShiftstoneSystem>();
            var currentShiftstoneEffects = shiftstoneSystem.currentShiftstoneEffects.ToArray();
            var shiftstones = shiftstoneSystem.GetCurrentShiftStoneConfiguration();
            int left = shiftstones is { Count: > 0 } ? shiftstones[0] : -1;
            int right = shiftstones is { Count: > 0 } ? shiftstones[1] : -1;
            
            PlayerShiftstoneVFX active = PlayerShiftstoneVFX.None;

            var charge = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "ChargeStone_VFX");
            if (charge != null && charge.isPlaying)
                active |= PlayerShiftstoneVFX.Charge;

            var adamant = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "AdamantStone_VFX");
            if (adamant != null && adamant.isPlaying)
                active |= PlayerShiftstoneVFX.Adamant;

            var surge = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "SurgeStone_VFX");
            if (surge != null && surge.isPlaying)
                active |= PlayerShiftstoneVFX.Surge;

            var vigor = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "Vigorstone_VFX");
            if (vigor != null && vigor.isPlaying)
                active |= PlayerShiftstoneVFX.Vigor;

            bool recordFingers = (bool)HandFingerRecording.SavedValue;
            var playerHandPresence = p.Controller.GetSubsystem<PlayerHandPresence>();
            var LhandInput = playerHandPresence.GetHandPresenceInputForHand(InputManager.Hand.Left);
            var RhandInput = playerHandPresence.GetHandPresenceInputForHand(InputManager.Hand.Right);

            var rockCam = p.Controller.GetSubsystem<PlayerLIV>()?.LckTablet.transform.gameObject;

            var measurement = p.Controller.assignedPlayer.Data.PlayerMeasurement;
            
            var playerState = new PlayerState
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
                active = p.Controller.gameObject.activeInHierarchy,
                activeShiftstoneVFX = active,
                leftShiftstone = left,
                rightShiftstone = right,
                ArmSpan = measurement.ArmSpan,
                Length = measurement.Length,
                visualData = p.Data.VisualData.ToPlayfabDataString()
            };

            if (recordFingers)
            {
                playerState.lgripInput = LhandInput.gripInput;
                playerState.lindexInput = LhandInput.indexInput;
                playerState.lthumbInput = LhandInput.thumbInput;
                playerState.rindexInput = RhandInput.indexInput;
                playerState.rthumbInput = RhandInput.indexInput;
                playerState.rgripInput = RhandInput.gripInput;
            }

            if (rockCam != null)
            {
                playerState.rockCamActive = rockCam.transform.GetChild(0).gameObject.activeInHierarchy;
                playerState.rockCamPos = rockCam.transform.position;
                playerState.rockCamRot = rockCam.transform.rotation;
            }

            playerStates[i] = playerState;
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
            
            bool hasReplayFlickVFX = structure.GetComponentsInChildren<ReplayTag>().Any(t => t.Type == "StructureFlick");
            bool isTargetDisk = (structure.name.Contains("Disc") && structure.transform.GetChild(0).GetChild(0).gameObject.activeInHierarchy);

            var holdVFXCount = structure.GetComponentsInChildren<VisualEffect>().Count(vfx => vfx.name.Contains("Hold_VFX"));
            
            StructureStateType type = structure.processableComponent.currentState.name switch
            {
                "default" => StructureStateType.Default,
                "Frozen" => StructureStateType.Frozen,
                "Float" => StructureStateType.Float,
                "FreeGrounded" => StructureStateType.FreeGrounded,
                "StableGrounded" => StructureStateType.StableGrounded,
                "Normal" => StructureStateType.Normal,
                "Free" => StructureStateType.Free,
                _ => StructureStateType.Default
            };

            structureStates[i] = new StructureState
            {
                position = structure.transform.position,
                rotation = structure.transform.rotation,
                active = structure.gameObject.activeInHierarchy,
                grounded = structure.IsGrounded || (structure.activeJointControl != null && structure.processableComponent.currentState == structure.frozenState),
                isLeftHeld = holdVFXCount >= 1,
                isRightHeld = holdVFXCount >= 2,
                isFlicked = flickedStructures.Contains(structure.gameObject) || hasReplayFlickVFX,
                currentState = type,
                isTargetDisk = isTargetDisk
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
                active = pedestal.GetComponent<Pedestal>().enabled && pedestal.activeInHierarchy
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
    
    public void StartRecording()
    {
        SetupRecordingData();
        Frames.Clear();
        Events.Clear();
        recordingMarkers.Clear();
        isRecording = true;

        if (recordingIcon != null)
            recordingIcon.color = Color.red;
        
        if ((bool)EnableHaptics.SavedValue)
            LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.15f, 1f, 0.15f);
    }
    
    public void StopRecording()
    {
        isRecording = false;
        SaveReplay(Frames.ToArray(), recordingMarkers, "Recording", onSave: (info, path) => ReplayAPI.ReplaySavedInternal(info, false, path));

        pingCount = 0;
        pingSum = 0;
        pingMax = 0;
        pingMin = int.MaxValue;
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
            LocalPlayer.Data.PlayerMeasurement.ArmSpan
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

    bool IsPausePlayPose(Transform left, Transform right)
    {
        float fingerUpDot = Vector3.Dot(left.forward.normalized, head.up);
        bool leftHandFlat = Abs(fingerUpDot) < Cos(60f * Deg2Rad);

        float palmDownDot = Vector3.Dot(left.right.normalized, -head.up);
        bool leftPalmDown = palmDownDot > Cos(60f * Deg2Rad);

        bool leftHandCorrect = leftHandFlat && leftPalmDown;
        
        float fingerVerticalDot = Vector3.Dot(right.forward.normalized, head.up);
        bool rightHandVertical = Abs(fingerVerticalDot) >= Cos(35f * Deg2Rad);

        float palmLeftDot = Vector3.Dot((-right.right).normalized, -head.right);
        bool rightPalmFacingLeft = palmLeftDot > 0.5f;

        bool rightHandCorrect = rightHandVertical && rightPalmFacingLeft;

        float dist = Vector3.Distance(left.position, right.position);
        float maxDist = LocalPlayer.Data.PlayerMeasurement.ArmSpan * (0.125f / errorsArmspan);

        bool handsCloseEnough = dist < maxDist;
        bool leftAboveRight = left.position.y > right.position.y;

        return leftHandCorrect && leftAboveRight && rightHandCorrect && handsCloseEnough;
    }

    public void HandleReplayPose()
    {
        if (currentScene == "Loader")
            return;

        if (leftHand == null || rightHand == null || head == null)
        {
            var controller = LocalPlayer?.Controller;
            if (controller == null) return;

            var vr = controller.transform?.childCount >= 3 ? controller.transform.GetChild(2) : null;
            if (vr == null || vr.childCount < 3) return;

            var headParent = vr.GetChild(0);
            if (headParent.childCount < 1) return;

            leftHand = vr.GetChild(1);
            rightHand = vr.GetChild(2);
            head = headParent.GetChild(0);
        }

        bool pose = IsFramePose(leftHand, rightHand);

        bool triggersHeld =
            Calls.ControllerMap.LeftController.GetTrigger() > 0.8f &&
            Calls.ControllerMap.RightController.GetTrigger() > 0.8f;

        if (pose &&
            Calls.ControllerMap.LeftController.GetGrip() > 0.8f &&
            Calls.ControllerMap.RightController.GetGrip() > 0.8f)
        {
            heldTime += Time.deltaTime;
            soundTimer += Time.deltaTime;

            if (soundTimer >= 0.5f && !hasPlayed)
            {
                soundTimer -= 0.5f;
                AudioManager.instance.Play(
                    triggersHeld 
                        ? ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"]
                        : ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"],
                    head.position
                );
            }

            if (heldTime >= 2f && !hasPlayed)
            {
                hasPlayed = true;

                if (triggersHeld)
                {
                    if (isPlaying)
                    {
                        if (ReplayPlaybackControls.playbackControlsOpen && 
                            Vector3.Distance(ReplayPlaybackControls.playbackControls.transform.position, head.position) < LocalPlayer.Data.PlayerMeasurement.ArmSpan
                        )
                            ReplayPlaybackControls.Close();
                        else
                            ReplayPlaybackControls.Open();
                    }
                    else
                    {
                        ReplayError();
                    }
                }
                else
                {
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
        }
        else
        {
            hasPlayed = false;
            heldTime = 0f;
            soundTimer = 0f;
        }

        bool isPausePose = IsPausePlayPose(leftHand, rightHand);

        if (isPausePose &&
            Calls.ControllerMap.LeftController.GetTrigger() < 0.4f &&
            Calls.ControllerMap.RightController.GetTrigger() < 0.4f &&
            Calls.ControllerMap.LeftController.GetGrip() < 0.4f &&
            Calls.ControllerMap.RightController.GetGrip() < 0.4f
           )
        {
            if (!hasPaused)
            {
                hasPaused = true;
                TogglePlayback(isPaused);
            }
        }
        else if (!isPausePose)
        {
            hasPaused = false;
        }
    }

    public void TogglePlayback(bool active, bool setSpeed = true, bool ignoreIsPlaying = true)
    {
        if (!isPlaying && !ignoreIsPlaying)
        {
            ReplayError();
            return;
        }

        if (active && !isPaused) return;
        if (!active && isPaused) return;

        isPaused = !active;
        
        ReplayPlaybackControls.playButtonSprite.sprite = !isPaused ? ReplayPlaybackControls.pauseSprite : ReplayPlaybackControls.playSprite;

        if (active)
        {
            AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], head.position);

            if ((bool)EnableHaptics.SavedValue)
                LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.05f, 1f, 0.05f);

            if (setSpeed)
                SetPlaybackSpeed(previousPlaybackSpeed);
        }
        else
        {
            previousPlaybackSpeed = playbackSpeed;

            AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"], head.position);

            if ((bool)EnableHaptics.SavedValue)
                LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.05f, 1f, 0.05f);

            if (setSpeed) 
                SetPlaybackSpeed(0f);
        }

        ReplayAPI.ReplayPauseChangedInternal(active);
    }

    public void TryHandleController(
        string[] actions,
        float primary,
        float secondary,
        bool isLeft
    )
    {
        void PlayHaptics()
        {
            var haptics = LocalPlayer.Controller.GetSubsystem<PlayerHaptics>();
            
            if ((bool)EnableHaptics.SavedValue)
            {
                if (isLeft)
                    haptics.PlayControllerHaptics(1f, 0.05f, 0, 0);
                else
                    haptics.PlayControllerHaptics(0, 0, 1f, 0.05f);
            }
        }

        if (primary <= 0 || secondary <= 0)
            return;

        if (Time.time - lastTriggerTime <= 1f)
            return;
        
        lastTriggerTime = Time.time;

        foreach (var action in actions)
        {
            if (action.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;
            
            switch (action)
            {
                case "Toggle Recording":
                {
                    if (isRecording)
                        StopRecording();
                    else
                        StartRecording();

                    break;
                }
                
                case "Save Replay Buffer":
                {
                    SaveReplayBuffer();
                    PlayHaptics();
                    break;
                }

                case "Add Marker":
                {
                    if (!isRecording && !isBuffering)
                        break;

                    AddMarker("core.manual", Color.white);
                    PlayHaptics();
                
                    AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], head.position);
                    break;
                }
            
                default:
                    ReplayError($"'{action}' is not a valid binding ({(isLeft ? "Left Controller" : "Right Controller")}).");
                    break;
            }
        }
    }
    
    // ----- Playback -----
    
    public void HandlePlayback()
    {
        if (!isPlaying) return;

        elapsedPlaybackTime += Time.deltaTime * playbackSpeed;

        if (elapsedPlaybackTime >= currentReplay.Frames[^1].Time)
        {
            SetPlaybackTime(currentReplay.Frames[^1].Time);
            
            if ((bool)StopReplayWhenDone.SavedValue)
                StopReplay();
            
            return;
        }
        
        if (elapsedPlaybackTime <= 0f)
        {
            SetPlaybackTime(0f);

            if ((bool)StopReplayWhenDone.SavedValue)
                StopReplay();
            
            return;
        }
        
        SetPlaybackTime(elapsedPlaybackTime);

        ReplayPlaybackControls.timeline?.GetComponent<MeshRenderer>()?.material?.SetFloat("_BP_Current", elapsedPlaybackTime * 1000f);

        if (ReplayPlaybackControls.currentDuration != null)
        {
            TimeSpan t = TimeSpan.FromSeconds(elapsedPlaybackTime);
            
            ReplayPlaybackControls.currentDuration.text = t.TotalHours >= 1 ? 
                $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : 
                $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }
    }
    
    public void ApplyInterpolatedFrame(int frameIndex, float t)
    {
        var frames = currentReplay.Frames;

        if (replayPlayers?.activeSelf == false || replayStructures?.activeSelf == false)
            return;

        if ((playbackSpeed > 0 && frameIndex >= frames.Length - 1) ||
            (playbackSpeed < 0 && frameIndex <= 0))
            return;
        
        // Interpolation
        t = playbackSpeed >= 0 ? t : 1f - t;
        Frame a = frames[frameIndex];
        Frame b = frames[frameIndex + (playbackSpeed >= 0 ? 1 : -1)];
        
        var poolManager = PoolManager.instance;

        // ------ Structures ------

        for (int i = 0; i < PlaybackStructures.Length; i++)
        {
            var playbackStructure = PlaybackStructures[i];
            var structureComp = playbackStructure.GetComponent<Structure>();
            var sa = a.Structures[i];
            var sb = b.Structures[i];

            ref var state = ref playbackStructureStates[i];

            var vfxSize = playbackStructure.name switch
            {
                "Disc" or "Ball" => 1f,
                "RockCube" => 1.5f,
                "Wall" => 2f,

                "LargeRock" => 2.7f,

                _ => 1f
            };

            foreach (var collider in playbackStructure.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            
            var velocity = (sb.position - sa.position) / (b.Time - a.Time);

            // ------ State Event Checks ------

            // Structure Broke
            if (state.active && !sb.active)
            {
                AudioManager.instance.Play(
                    playbackStructure.GetComponent<Structure>().onDeathAudio,
                    playbackStructure.transform.position
                );

                try {
                    structureComp.onStructureDestroyed?.Invoke();
                } catch { }
                
                if (structureComp.currentFrictionVFX != null)
                    GameObject.Destroy(structureComp.currentFrictionVFX.gameObject);
            }
            
            // Structure Spawned
            if (!state.active && sb.active && frameIndex != 0)
            {
                string sfx = playbackStructure.name switch
                {
                    "Wall" => "Call_Structure_Spawn_Heavy",
                    "Ball" or "Disc" => "Call_Structure_Spawn_Light",
                    "LargeRock" => "Call_Structure_Spawn_Massive",
                    _ => "Call_Structure_Spawn_Medium"
                };

                if (ReplayCache.SFX.TryGetValue(sfx, out var audioCall) && frameIndex + 2 < frames.Length)
                    AudioManager.instance.Play(audioCall, frames[frameIndex + 2].Structures[i].position);
                
                foreach (var visualEffect in playbackStructure.GetComponentsInChildren<PooledVisualEffect>())
                    GameObject.Destroy(visualEffect.gameObject);
                
                structureComp.OnFetchFromPool();
            }
            
            state.active = sb.active;
            playbackStructure.SetActive(state.active);

            // States
            if (state.currentState != sb.currentState)
            {
                state.currentState = sb.currentState;

                bool parryFromHistop = sa.currentState == StructureStateType.Frozen && sb.currentState == StructureStateType.Float;

                // Hitstop
                bool isHitstop = state.currentState == StructureStateType.Frozen;
                var renderer = structureComp.transform.GetChild(0).GetComponent<MeshRenderer>();
                renderer?.material?.SetFloat("_shake", isHitstop ? 1 : 0);
                renderer?.material?.SetFloat("_shakeFrequency", 75 * playbackSpeed);
                
                // Parry
                bool isParried = state.currentState == StructureStateType.Float;
                if (!parryFromHistop)
                {
                    if (isParried)
                    {
                        var pool = poolManager.GetPool("Parry_VFX");
                        var effect = GameObject.Instantiate(pool.poolItem.gameObject, VFXParent.transform);

                        effect.transform.position = sa.position;
                        effect.transform.localRotation = Quaternion.identity;
                        effect.transform.localScale = Vector3.one * vfxSize;
                        effect.GetComponent<VisualEffect>().playRate = Abs(playbackSpeed);
                        effect.AddComponent<DeleteAfterSeconds>();
                        var tag = effect.AddComponent<ReplayTag>();
                        tag.attachedStructure = structureComp;
                        tag.Type = "StructureParry";

                        AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Parry"], sa.position);
                    }
                    else
                    {
                        var tag = VFXParent.GetComponentsInChildren<ReplayTag>().FirstOrDefault(tag => tag.Type == "StructureParry" && tag.attachedStructure == structureComp);
                    
                        if (tag != null)
                            GameObject.Destroy(tag.gameObject);
                    }
                }
            }

            if (state.grounded != sb.grounded && frameIndex != 0)
            {
                if (sb.grounded)
                    structureComp.processableComponent.SetCurrentState(structureComp.groundedState);
                else
                    structureComp.processableComponent.Awake();
            }

            state.grounded = structureComp.IsGrounded;
            
            // Hold started
            if (!state.isLeftHeld && sb.isLeftHeld)
                SpawnHoldVFX("Left");
            
            if (!state.isRightHeld && sb.isRightHeld)
                SpawnHoldVFX("Right");

            void SpawnHoldVFX(string hand)
            {
                var pool = poolManager.GetPool("Hold_VFX");
                var effect = GameObject.Instantiate(pool.poolItem.gameObject, playbackStructure.transform);

                effect.transform.localPosition = Vector3.zero;
                effect.transform.localRotation = Quaternion.identity;
                effect.transform.localScale = Vector3.one * vfxSize;
                effect.GetComponent<VisualEffect>().playRate = Abs(playbackSpeed);
                var tag = effect.AddComponent<ReplayTag>();
                tag.Type = "StructureHold_" + hand;

                AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Hold"], sa.position);
            }

            // Hold ended
            if (state.isLeftHeld && !sb.isLeftHeld)
                DestroyHoldVFX("Left");

            if (state.isRightHeld && !sb.isRightHeld)
                DestroyHoldVFX("Right");

            void DestroyHoldVFX(string hand)
            {
                foreach (var vfx in playbackStructure.GetComponentsInChildren<ReplayTag>())
                {
                    if (vfx == null)
                        continue;

                    if (vfx.Type == "StructureHold_" + hand && vfx.transform.parent == playbackStructure.transform)
                    {
                        GameObject.Destroy(vfx.gameObject);
                        break;
                    }
                }
            }

            state.isLeftHeld = Utilities.HasVFXType("StructureHold_Left", playbackStructure.transform);
            state.isRightHeld = Utilities.HasVFXType("StructureHold_Right", playbackStructure.transform);

            // Flick started
            if (!state.isFlicked && sb.isFlicked)
            {
                var pool = poolManager.GetPool("Flick_VFX");
                var effect = GameObject.Instantiate(pool.poolItem.gameObject, playbackStructure.transform);

                effect.transform.localPosition = Vector3.zero;
                effect.transform.localRotation = Quaternion.identity;
                effect.transform.localScale = Vector3.one * vfxSize;
                effect.GetComponent<VisualEffect>().playRate = Abs(playbackSpeed);
                var tag = effect.AddComponent<ReplayTag>();
                tag.Type = "StructureFlick";

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

            state.isFlicked = Utilities.HasVFXType("StructureFlick", playbackStructure.transform);

            // ------------
            
            structureComp.currentVelocity = velocity * playbackSpeed;

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
                vfx.playRate = Abs(playbackSpeed);
            
            if (structureComp.currentFrictionVFX != null)
                structureComp.currentFrictionVFX.visualEffect.playRate = Abs(playbackSpeed);
        }

        // ------ Players ------

        for (int i = 0; i < PlaybackPlayers.Length; i++)
        {
            var playbackPlayer = PlaybackPlayers[i];
            var pa = a.Players[i];
            var pb = b.Players[i];

            ref var state = ref playbackPlayerStates[i];

            if (state.health != pb.Health)
            {
                playbackPlayer.Controller.GetSubsystem<PlayerHealth>().SetHealth(pb.Health, (short)state.health);

                bool tookDamage =
                    (playbackSpeed >= 0f && pb.Health < pa.Health) ||
                    (playbackSpeed < 0f && pb.Health > pa.Health);
                
                if (tookDamage && frameIndex != 0 && pb.Health != 0 && currentReplay.Header.Scene != "Gym")
                {
                    var pool = PoolManager.instance.GetPool("PlayerHitmarker");
                    var effect = GameObject.Instantiate(pool.poolItem.gameObject, VFXParent.transform);

                    effect.transform.localPosition = playbackPlayer.Head.transform.position - new Vector3(0, 0.5f, 0);
                    effect.transform.localRotation = Quaternion.identity;
                    effect.GetComponent<VisualEffect>().playRate = Abs(playbackSpeed);
                    effect.AddComponent<ReplayTag>();
                    effect.gameObject.AddComponent<DeleteAfterSeconds>();

                    var hitmarker = effect.GetComponent<PlayerHitmarker>();
                    hitmarker.SetDamage(Abs(pa.Health - pb.Health));
                    hitmarker.gameObject.SetActive(true);
                    hitmarker.Play();
                    hitmarker.GetComponent<VisualEffect>().playRate = Abs(playbackSpeed);
                }
            }
            
            state.health = playbackPlayer.Controller.assignedPlayer.Data.HealthPoints;
            
            if (state.currentStack != pb.currentStack)
            { 
                state.currentStack = pb.currentStack;

                if (pb.currentStack != (short)StackType.Flick
                    && pb.currentStack != (short)StackType.HoldLeft
                    && pb.currentStack != (short)StackType.HoldRight
                    && pb.currentStack != (short)StackType.Ground
                    && pb.currentStack != (short)StackType.Parry
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

                        if (pb.currentStack == (short)StackType.Dash)
                            playbackPlayer.lastDashTime = elapsedPlaybackTime;
                    }
                }
            }

            if (state.activeShiftstoneVFX != pb.activeShiftstoneVFX)
            {
                state.activeShiftstoneVFX = pb.activeShiftstoneVFX;
                var chest = playbackPlayer.Controller.GetSubsystem<PlayerIK>().VrIK.references.chest;
                var flags = state.activeShiftstoneVFX;

                TryToggleVFX("Chargestone VFX", PlayerShiftstoneVFX.Charge);
                TryToggleVFX("Adamantstone_VFX", PlayerShiftstoneVFX.Adamant);
                TryToggleVFX("Surgestone_VFX", PlayerShiftstoneVFX.Surge);
                TryToggleVFX("Vigorstone_VFX", PlayerShiftstoneVFX.Vigor);
                
                void TryToggleVFX(string name, PlayerShiftstoneVFX flag)
                {
                    var vfx = chest.Find(name)?.GetComponent<VisualEffect>();
                    if (vfx == null) return;

                    string shiftstoneName = name switch
                    {
                        "Chargestone VFX" => "ChargeStone",
                        "Surgestone_VFX" => "SurgeStone",
                        "Vigorstone_VFX" => "VigorStone",
                        "Adamantstone_VFX" => "AdamantStone",
                        _ => ""
                    };
                    
                    if (flags.HasFlag(flag))
                    {
                        vfx.transform.localScale = Vector3.one;
                        
                        vfx.Play();
                        vfx.playRate = Abs(playbackSpeed);
                        AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_Use"], chest.transform.position);
                        var socketIndex = playbackPlayer.Controller.GetSubsystem<PlayerShiftstoneSystem>().shiftStoneSockets
                            .FirstOrDefault(s => s.assignedShifstone.name == shiftstoneName)?.assignedSocketIndex;

                        if (socketIndex.HasValue)
                            playbackPlayer.Controller.GetSubsystem<PlayerShiftstoneSystem>()
                                .ActivateUseShiftstoneEffects(socketIndex == 0 ? InputManager.Hand.Left : InputManager.Hand.Right);
                    }
                    else
                    {
                        vfx.Stop();
                    }
                }
            }

            if (state.leftShiftstone != pb.leftShiftstone)
            {
                state.leftShiftstone = pb.leftShiftstone;
                ApplyShiftstone(playbackPlayer.Controller, 0, pb.leftShiftstone);
            }
            
            if (state.rightShiftstone != pb.rightShiftstone)
            {
                state.rightShiftstone = pb.rightShiftstone;
                ApplyShiftstone(playbackPlayer.Controller, 1, pb.rightShiftstone);
            }
            
            void ApplyShiftstone(PlayerController controller, int socketIndex, int shiftstoneIndex)
            {
                var shiftstoneSystem = controller.GetSubsystem<PlayerShiftstoneSystem>();
                
                PooledMonoBehaviour pooledObject = shiftstoneIndex switch
                {
                    0 => poolManager.GetPooledObject("AdamantStone"),
                    1 => poolManager.GetPooledObject("ChargeStone"),
                    2 => poolManager.GetPooledObject("FlowStone"),
                    3 => poolManager.GetPooledObject("GuardStone"),
                    4 => poolManager.GetPooledObject("StubbornStone"),
                    5 => poolManager.GetPooledObject("SurgeStone"),
                    6 => poolManager.GetPooledObject("VigorStone"),
                    7 => poolManager.GetPooledObject("VolatileStone"),
                    _ => null
                };
            
                if (pooledObject == null)
                {
                    shiftstoneSystem.RemoveShiftStone(socketIndex, false);
                    return;
                }
            
                shiftstoneSystem.AttachShiftStone(pooledObject.GetComponent<ShiftStone>(), socketIndex, false, false);
                AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_EquipBoth"], pooledObject.transform.position);
            }

            var rockCam = playbackPlayer.Controller.GetSubsystem<PlayerLIV>()?.LckTablet;
            if (rockCam != null)
            {
                if (state.rockCamActive != pb.rockCamActive)
                {
                    state.rockCamActive = pb.rockCamActive;
                    rockCam.transform.SetParent(ReplayRoot.transform);
                    rockCam.name = playbackPlayer.name + "_RockCam";
                    rockCam.gameObject.SetActive(state.rockCamActive);
                    
                    for (int j = 0; j < rockCam.transform.childCount; j++)
                        rockCam.transform.GetChild(j).gameObject.SetActive(state.rockCamActive);
                }
                
                rockCam.transform.position = pb.rockCamPos;
                rockCam.transform.rotation = pb.rockCamRot;
            }

            if (state.playerMeasurement.ArmSpan != pb.ArmSpan || state.playerMeasurement.Length != pb.Length)
            {
                var measurement = new PlayerMeasurement(pb.Length, pb.ArmSpan);
                state.playerMeasurement = measurement;

                playbackPlayer.Controller.GetSubsystem<PlayerScaling>().ScaleController(measurement);

                UpdateReplayCameraPOV(povPlayer ?? LocalPlayer, ReplaySettings.hideLocalPlayer);

                AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Succes"], 
                    playbackPlayer.Controller.GetSubsystem<PlayerIK>().VrIK.references.head.position
                );
            }

            if (!string.Equals(state.visualData, pb.visualData) && !string.IsNullOrEmpty(pb.visualData))
            {
                var newVisualData = PlayerVisualData.FromPlayfabDataString(pb.visualData);

                playbackPlayer.Controller.assignedPlayer.Data.VisualData = newVisualData;
                playbackPlayer.Controller.Initialize(playbackPlayer.Controller.assignedPlayer);

                playbackPlayer.Controller.transform.GetChild(9).gameObject.SetActive(false);

                state.visualData = pb.visualData;
            }
            
            if (state.active != pb.active)
                playbackPlayer.Controller.gameObject.SetActive(pb.active);
            
            state.active = playbackPlayer.Controller.gameObject.activeSelf;

            var lHandPresence = PlayerHandPresence.HandPresenceInput.Empty;
            lHandPresence.gripInput = pa.lgripInput;
            lHandPresence.thumbInput = pa.lthumbInput;
            lHandPresence.indexInput = pa.lindexInput;
            playbackPlayer.lHandInput = lHandPresence;
            
            var rHandPresence = PlayerHandPresence.HandPresenceInput.Empty;
            rHandPresence.gripInput = pa.rgripInput;
            rHandPresence.thumbInput = pa.rthumbInput;
            rHandPresence.indexInput = pa.rindexInput;
            playbackPlayer.rHandInput = rHandPresence;
            
            playbackPlayer.ApplyInterpolatedPose(pa, pb, t);

            foreach (var vfx in playbackPlayer.GetComponentsInChildren<VisualEffect>())
                vfx.playRate = Abs(playbackSpeed);
        }

        // ------ Pedestals ------

        for (int i = 0; i < currentReplay.Header.PedestalCount; i++)
        {
            var playbackPedestal = replayPedestals[i];
            var pa = a.Pedestals[i];
            var pb = b.Pedestals[i];

            ref var state = ref playbackPedestalStates[i];

            if (state.active != pb.active)
                playbackPedestal.SetActive(pb.active);

            state.active = playbackPedestal.activeSelf;

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
                case EventType.OneShotFX:
                {
                    var fx = SpawnFX(evt.fxType, evt.position, evt.rotation);

                    if (fx != null)
                    {
                        GameObject closestStructure = PlaybackStructures
                            .Where(s => Vector3.Distance(s.transform.position, evt.position) < 2f)
                            .OrderBy(s => Vector3.Distance(s.transform.position, evt.position))
                            .FirstOrDefault();
                        
                        if (closestStructure != null && evt.fxType is FXOneShotType.Spawn or FXOneShotType.Break or FXOneShotType.Grounded or FXOneShotType.Ungrounded)
                        {
                            float scale = closestStructure.name switch
                            {
                                "Ball" or "Disc" or "SmallRock" => 0.7f,
                                "Wall" or "Cube" => 1f,
                                "LargeRock" => 1.3f,
                                _ => 1f
                            };

                            fx.transform.localScale = Vector3.one * scale;
                        }
                    }

                    if (evt.fxType == FXOneShotType.GroundedSFX)
                        AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Stomp"], evt.position);
                    
                    break;
                }
            }
        }

        ReplayAPI.OnPlaybackFrameInternal(a);
    }

    public GameObject SpawnFX(FXOneShotType fxType, Vector3 position, Quaternion rotation = default)
    {
        if (isRecording || isBuffering)
        {
            var evtChunk = new EventChunk
            {
                type = EventType.OneShotFX,
                fxType = fxType,
                position = position
            };

            if (fxType == FXOneShotType.Ricochet)
                evtChunk.rotation = rotation;

            Events.Add(evtChunk);
        }

        GameObject vfxObject = null;

        if (ReplayCache.FXToVFXName.TryGetValue(fxType, out var poolName))
        {
            var effect = GameObject.Instantiate(PoolManager.instance.GetPool(poolName).poolItem);
            if (effect != null)
            {
                effect.transform.SetParent(VFXParent.transform);
                effect.transform.SetPositionAndRotation(position, rotation);
                
                var vfx = effect.GetComponent<VisualEffect>();
                if (vfx != null)
                    vfx.playRate = Abs(playbackSpeed);
                
                var tag = effect.gameObject.AddComponent<ReplayTag>();
                tag.Type = poolName;
                
                effect.gameObject.AddComponent<DeleteAfterSeconds>();

                vfxObject = effect.gameObject;
            }
        }

        if (ReplayCache.FXToSFXName.TryGetValue(fxType, out string audioName) && ReplayCache.SFX.TryGetValue(audioName, out var audioCall))
        {
            AudioManager.instance.Play(audioCall, position);
        }

        return vfxObject;
    } 
    
    // ----- Controls ------
    
    public void SetPlaybackSpeed(float newSpeed)
    {
        playbackSpeed = newSpeed;

        if (isPlaying)
        {
            foreach (var structure in PlaybackStructures)
            {
                if (structure == null) continue;
            
                foreach (var vfx in structure.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = Abs(newSpeed);

                if (structure.GetComponent<Structure>().frictionVFX != null)
                    structure.GetComponent<Structure>().frictionVFX.returnToPoolTimer = null;
            }

            for (int i = 0; i < VFXParent.transform.childCount; i++)
            {
                var vfx = VFXParent.transform.GetChild(i);
                vfx.GetComponent<VisualEffect>().playRate = Abs(newSpeed);
            }

            foreach (var pedestal in Pedestals)
            {
                if (pedestal == null) continue;
            
                foreach (var vfx in pedestal.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = Abs(newSpeed);
            }
        }

        if (ReplayPlaybackControls.playbackSpeedText != null)
        {
            string label;

            if (Approximately(playbackSpeed, 0f))
                label = "Paused";
            else if (playbackSpeed < 0f)
                label = $"<< {Abs(playbackSpeed):0.0}x";
            else
                label = $">> {playbackSpeed:0.0}x";
            
            ReplayPlaybackControls.playbackSpeedText.text = label;
            ReplayPlaybackControls.playbackSpeedText.ForceMeshUpdate();
        }
    }

    public void AddPlaybackSpeed(float delta, float minSpeed = -8f, float maxSpeed = 8f)
    {
        TogglePlayback(isPaused && !Approximately(playbackSpeed + delta, 0), false);
        
        float speed = playbackSpeed + delta;

        if (Approximately(speed, 0))
            TogglePlayback(false);
        else
            TogglePlayback(true, false);
        
        speed = Round(speed * 10f) / 10f;
        speed = Clamp(speed, minSpeed, maxSpeed);
        SetPlaybackSpeed(speed);
    }

    public void SetPlaybackFrame(int frame)
    {
        int clampedFrame = Clamp(frame, 0, currentReplay.Frames.Length - 2);
        float time = currentReplay.Frames[clampedFrame].Time;
        SetPlaybackTime(time);
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

        ReplayAPI.ReplayTimeChangedInternal(time);
    }
    
    public void UpdateReplayCameraPOV(Player player, bool hideLocalPlayer = false)
    {
        RecordingCamera cam = Calls.GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<RecordingCamera>(); 
        
        var localController = LocalPlayer.Controller.transform;
        
        foreach (var renderer in localController.GetChild(1).GetComponentsInChildren<Renderer>())
            renderer.gameObject.layer = LayerMask.NameToLayer(hideLocalPlayer ? "PlayerFade" : "PlayerController");
        
        localController.GetChild(6).gameObject.SetActive(!hideLocalPlayer);
        
        var povHead = povPlayer?.Controller.GetSubsystem<PlayerIK>().VrIK.references.head;
        if (povHead != null)
        {
            if (povPlayer != null)
            {
                povHead.transform.localScale = Vector3.one;
                povPlayer.Controller.transform.GetChild(6).gameObject.SetActive(true);
            }
        }

        povPlayer = player;
        if (player != LocalPlayer)
        {
            povHead = povPlayer.Controller.GetSubsystem<PlayerIK>().VrIK.references.head;
            povHead.transform.localScale = Vector3.zero;
            povPlayer.Controller.transform.GetChild(6).gameObject.SetActive(false);
            povPlayer.Controller.transform.GetChild(9).gameObject.SetActive(false);
            
            LocalPlayer.Controller.GetSubsystem<PlayerCamera>().GetComponent<AudioListener>().enabled = false;
            povPlayer.Controller.GetSubsystem<PlayerCamera>().GetComponent<AudioListener>().enabled = true;
            
            
            foreach (var renderer in ReplayPlaybackControls.playbackControls.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.layer != LayerMask.NameToLayer("InteractionBase"))
                    renderer.gameObject.layer = LayerMask.NameToLayer("PlayerFade");
            }
            
            cam.localPlayerVR = povPlayer.Controller.GetSubsystem<PlayerVR>();
        }
        else
        {
            cam.localPlayerVR = LocalPlayer.Controller.GetSubsystem<PlayerVR>();
            if (povHead != null) povHead.transform.localScale = Vector3.one;
            
            povPlayer.Controller.GetSubsystem<PlayerCamera>().GetComponent<AudioListener>().enabled = false;
            LocalPlayer.Controller.GetSubsystem<PlayerCamera>().GetComponent<AudioListener>().enabled = true;
            
            foreach (var renderer in localController.GetChild(1).GetComponentsInChildren<Renderer>())
                renderer.gameObject.layer = LayerMask.NameToLayer("PlayerController");
            
            foreach (var renderer in ReplayPlaybackControls.playbackControls.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.layer != LayerMask.NameToLayer("InteractionBase"))
                    renderer.gameObject.layer = LayerMask.NameToLayer("Default");
            }
        }
    }

    // ----- States -----
    
    public struct PlaybackStructureState
    {
        public bool active;
        public bool isLeftHeld;
        public bool isRightHeld;
        public bool isFlicked;
        public bool grounded;
        public StructureStateType currentState;
    }

    public struct PlaybackPlayerState
    {
        public bool active;
        public int health;
        public short currentStack;
        public PlayerShiftstoneVFX activeShiftstoneVFX;
        public int leftShiftstone;
        public int rightShiftstone;
        public bool rockCamActive;
        public PlayerMeasurement playerMeasurement;
        public string visualData;
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

    public PlayerHandPresence.HandPresenceInput lHandInput;
    public PlayerHandPresence.HandPresenceInput rHandInput;

    private static readonly int PoseFistsActiveHash = Animator.StringToHash("PoseFistsActive");
    public float lastDashTime = -999f;
    private const float dashDuration = 1f;

    public PlayerAnimator pa;
    public PlayerMovement pm;
    public PlayerPoseSystem ps;

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

    bool IsDashing()
    {
        if (!pm.IsGrounded())
            lastDashTime = -999f;
        
        return Abs(Main.elapsedPlaybackTime - lastDashTime) < dashDuration;
    }

    public void Update()
    {
        if (Controller == null)
            return;

        if (pa == null || pm == null || ps == null)
        {
            pa = Controller.GetSubsystem<PlayerAnimator>();
            pm = Controller.GetSubsystem<PlayerMovement>();
            ps = Controller.GetSubsystem<PlayerPoseSystem>();
        }

        int state;

        if (pm.IsGrounded())
            state = IsDashing() ? 4 : 1;
        else
            state = 2;

        pa.animator.SetInteger(pa.movementStateAnimatorHash, state);
        
        if ((bool)Main.instance.CloseHandsOnPose.SavedValue)
            pa.animator.SetBool(PoseFistsActiveHash, ps.IsDoingAnyPose());
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
    public InteractionButton crystalizeButton;

    public TextMeshPro replayNameText;
    public TextMeshPro indexText;
    public TextMeshPro metadataText;
    public TextMeshPro heisenhouserText;
    
    public float desiredTableHeight = 1.5481f;
    public float tableOffset = 0f;
    public float desiredMetadataTextHeight = 1.8513f;

    public bool isReadingCrystal = false;

    public void Start()
    {
        tableFloat = TableRoot.GetComponent<TableFloat>();
        metadataTextFloat = metadataText.GetComponent<TableFloat>();
    }

    public void Update()
    {
        if (Main.instance.currentScene != "Loader" && Main.LocalPlayer != null)
        {
            float playerArmspan = Main.LocalPlayer.Data.PlayerMeasurement.ArmSpan;
        
            if (tableFloat != null)
                tableFloat.targetY = playerArmspan * (desiredTableHeight / Main.errorsArmspan) + tableOffset;
        
            if (metadataTextFloat != null)
                metadataTextFloat.targetY = playerArmspan * (desiredMetadataTextHeight / Main.errorsArmspan) + tableOffset;
            
            ReplayCrystals.Crystal target = ReplayCrystals.FindClosestCrystal(
                transform.position + new Vector3(0, 0.4f, 0),
                0.5f
            );

            if (target == null && !SceneManager.instance.IsLoadingScene)
            {
                if (ReplayFiles.metadataHidden && ReplayFiles.explorer.currentIndex != -1)
                    ReplayFiles.ShowMetadata();

                isReadingCrystal = false;
            }
            else
            {
                if (!ReplayFiles.metadataHidden)
                    ReplayFiles.HideMetadata();

                if (!isReadingCrystal && target != null && !target.isGrabbed && target.hasLeftTable && !target.isAnimation)
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
public class DeleteAfterSeconds : MonoBehaviour
{
    public float destroyTime = 10f;
    private float spawnTime;

    public void Awake()
    {
        spawnTime = Main.elapsedPlaybackTime;
    }

    public void Update()
    {
        if (Abs(Main.elapsedPlaybackTime - spawnTime) >= destroyTime)
            Destroy(gameObject);
    }
}

[RegisterTypeInIl2Cpp]
public class TimelineScrubber : MonoBehaviour
{
    public ReplaySerializer.ReplayHeader header;
    
    private void OnTriggerEnter(Collider other)
    {
        if (!IsFinger(other.gameObject, out var isLeft))
            return;

        AudioManager.instance.Play(ReplayCache.SFX["Call_GearMarket_GenericButton_Press"], transform.position);
        
        if ((bool)Main.instance.EnableHaptics.SavedValue)
            Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(0.6f, isLeft ? 0.15f : 0f, 0.6f, !isLeft ? 0.15f : 0f);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsFinger(other.gameObject, out _))
            return;
        
        Vector3 point = other.ClosestPointOnBounds(transform.position);
        float u = Utilities.GetProgressFromMeshPosition(point, GetComponentInParent<MeshRenderer>());

        float time = u * header.Duration;
        
        GetComponentInParent<MeshRenderer>().material?.SetFloat("_BP_Current", time * 1000f);
        
        if (Main.isPlaying)
            Main.instance.SetPlaybackTime(time);
    }
    
    private void OnTriggerExit(Collider other)
    {
        if (!IsFinger(other.gameObject, out var isLeft))
            return;

        AudioManager.instance.Play(ReplayCache.SFX["Call_Interactionbase_ButtonRelease"], transform.position);
        
        if ((bool)Main.instance.EnableHaptics.SavedValue)
            Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(0.3f, isLeft ? 0.15f : 0f, 0.3f, !isLeft ? 0.15f : 0f);
    }

    private bool IsFinger(GameObject obj, out bool isLeft)
    {
        isLeft = obj.name.EndsWith("L");
        return obj.name.Contains("Bone_Pointer_C");
    }
}

[RegisterTypeInIl2Cpp]
public class ReplayTag : MonoBehaviour
{
    public string Type;
    public Structure attachedStructure;
}
