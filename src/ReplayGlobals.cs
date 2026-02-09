using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
// using Concentus;
// using Concentus.Enums;
// using Concentus.Oggfile;
// using Concentus.Structs;
using Il2CppPhoton.Voice;
using Il2CppPhoton.Voice.PUN;
using Il2CppPhoton.Voice.Unity;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Environment.MatchFlow;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppRUMBLE.Social;
using Il2CppRUMBLE.Social.Phone;
using Il2CppSystem.Text;
using Il2CppSystem.Text.RegularExpressions;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleModdingAPI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using Random = UnityEngine.Random;

namespace ReplayMod;

public static class ReplayCache
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
    
    public static readonly Dictionary<string, FXOneShotType> AudioCallToFX = new()
    {
        { "Call_Structure_Impact_Light", FXOneShotType.ImpactLight },
        { "Call_Structure_Impact_Medium", FXOneShotType.ImpactMedium },
        { "Call_Structure_Impact_Heavy", FXOneShotType.ImpactHeavy },
        { "Call_Structure_Impact_Massive", FXOneShotType.ImpactMassive },
        { "Call_Structure_Ground", FXOneShotType.GroundedSFX },
        { "Call_RockCam_Spawn", FXOneShotType.RockCamSpawn },
        { "Call_RockCam_Despawn", FXOneShotType.RockCamDespawn },
        { "Call_RockCam_Stick", FXOneShotType.RockCamStick },
        { "Call_Bodyhit_Hard", FXOneShotType.Fistbump },
        { "Call_FistBumpBonus", FXOneShotType.FistbumpGoin }
    };
    
    public static readonly Dictionary<string, FXOneShotType> VFXNameToFX = new()
    {
        { "StructureCollision_VFX", FXOneShotType.StructureCollision },
        { "Ricochet_VFX", FXOneShotType.Ricochet },
        { "Ground_VFX", FXOneShotType.Grounded },
        { "Unground_VFX", FXOneShotType.Ungrounded },
        { "DustImpact_VFX", FXOneShotType.DustImpact },
        { "DustSpawn_VFX", FXOneShotType.Spawn },
        { "DustBreak_VFX", FXOneShotType.Break },
        { "DustBreakDISC_VFX", FXOneShotType.BreakDisc },
        { "RockCamSpawn_VFX", FXOneShotType.RockCamSpawn },
        { "RockCamDespawn_VFX", FXOneShotType.RockCamDespawn },
        { "PlayerBoxInteractionVFX",FXOneShotType.Fistbump },
        { "FistbumpCoin", FXOneShotType.FistbumpGoin },
    };
    
    public static readonly Dictionary<FXOneShotType, string> FXToVFXName = new()
    {
        { FXOneShotType.StructureCollision, "StructureCollision_VFX" },
        { FXOneShotType.Ricochet, "Ricochet_VFX" },
        { FXOneShotType.Grounded, "Ground_VFX" },
        { FXOneShotType.Ungrounded, "Unground_VFX" },
        { FXOneShotType.DustImpact, "DustImpact_VFX" },
        { FXOneShotType.Spawn, "DustSpawn_VFX" },
        { FXOneShotType.Break, "DustBreak_VFX" },
        { FXOneShotType.BreakDisc, "DustBreakDISC_VFX" },
        { FXOneShotType.RockCamSpawn, "RockCamSpawn_VFX" },
        { FXOneShotType.RockCamDespawn, "RockCamDespawn_VFX" },
        { FXOneShotType.Fistbump, "PlayerBoxInteractionVFX" },
        { FXOneShotType.FistbumpGoin, "FistbumpCoin" },
        { FXOneShotType.Jump, "Jump_VFX" },
        { FXOneShotType.Dash, "Dash_VFX" }
    };

    public static readonly Dictionary<FXOneShotType, string> FXToSFXName = new()
    {
        { FXOneShotType.ImpactLight, "Call_Structure_Impact_Light" },
        { FXOneShotType.ImpactMedium, "Call_Structure_Impact_Medium" },
        { FXOneShotType.ImpactHeavy, "Call_Structure_Impact_Heavy" },
        { FXOneShotType.ImpactMassive, "Call_Structure_Impact_Massive" },
        { FXOneShotType.GroundedSFX, "Call_Structure_Ground" },
        { FXOneShotType.RockCamSpawn, "Call_RockCam_Spawn" },
        { FXOneShotType.RockCamDespawn, "Call_RockCam_Despawn" },
        { FXOneShotType.RockCamStick, "Call_RockCam_Stick" },
        { FXOneShotType.Fistbump, "Call_Bodyhit_Hard" },
        { FXOneShotType.FistbumpGoin, "Call_FistBumpBonus" }
    };
    
    public static Dictionary<StructureType, Pool<PooledMonoBehaviour>> structurePools;
    public static Dictionary<string, AudioCall> SFX;
    
    public static void BuildCacheTables()
    {
        structurePools = new();
        SFX = new();

        // Pool Cache
        foreach (var pool in PoolManager.instance.availablePools)
        {
            var name = pool.poolItem.resourceName;

            if (name.Contains("RockCube")) structurePools[StructureType.Cube] = pool;
            else if (name.Contains("Pillar")) structurePools[StructureType.Pillar] = pool;
            else if (name.Contains("Disc")) structurePools[StructureType.Disc] = pool;
            else if (name.Contains("Wall")) structurePools[StructureType.Wall] = pool;
            else if (name == "Ball") structurePools[StructureType.Ball] = pool;
            else if (name.Contains("LargeRock")) structurePools[StructureType.LargeRock] = pool;
            else if (name.Contains("SmallRock")) structurePools[StructureType.SmallRock] = pool;
            else if (name.Contains("BoulderBall")) {
                structurePools[StructureType.CagedBall] = pool;
                structurePools[StructureType.TetheredCagedBall] = pool;
            }
        }
        
        AudioCall[] audioCalls = Resources.FindObjectsOfTypeAll<AudioCall>();

        foreach (var audioCall in audioCalls)
        {
            if (audioCall == null || string.IsNullOrEmpty(audioCall.name))
                continue;
            
            SFX[audioCall.name] = audioCall;
        }
            
    }
}

public static class Utilities
{
    private static GameObject customMultiplayerMaps;
    
    
    // ----- Names -----
    
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

    public static string[] RebuildCustomMapFromScene()
    {
        var parent = GameObject.Find("CustomMapParent");
        if (parent == null) return null;

        List<string> data = new List<string>();
        data.Add("1");
        data.Add(parent.transform.childCount.ToString());

        for (int i = 0; i < parent.transform.childCount; i++)
        {
            var child = parent.transform.GetChild(i);

            string name = child.name;

            Color color = Color.white;
            var renderer = child.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null && renderer.material.HasProperty("_Color"))
                color = renderer.material.color;

            Vector3 pos = child.position;
            Vector3 rot = child.rotation.eulerAngles;
            Vector3 scale = child.localScale;

            data.Add(name);
            data.Add(color.r.ToString(CultureInfo.InvariantCulture));
            data.Add(color.g.ToString(CultureInfo.InvariantCulture));
            data.Add(color.b.ToString(CultureInfo.InvariantCulture));
            
            data.Add(pos.x.ToString(CultureInfo.InvariantCulture));
            data.Add(pos.y.ToString(CultureInfo.InvariantCulture));
            data.Add(pos.z.ToString(CultureInfo.InvariantCulture));
            
            data.Add(rot.x.ToString(CultureInfo.InvariantCulture));
            data.Add(rot.y.ToString(CultureInfo.InvariantCulture));
            data.Add(rot.z.ToString(CultureInfo.InvariantCulture));
            
            data.Add(scale.x.ToString(CultureInfo.InvariantCulture));
            data.Add(scale.y.ToString(CultureInfo.InvariantCulture));
            data.Add(scale.z.ToString(CultureInfo.InvariantCulture));
        }

        return data.ToArray();
    }

    public static GameObject GetCustomMap(string mapName)
    {
        customMultiplayerMaps ??= GameObject.Find("CustomMultiplayerMaps");

        if (customMultiplayerMaps == null)
        {
            Main.instance.ReplayError("Selected replay uses a custom map, but custom maps are not installed.");
            return null;
        }

        GameObject map = customMultiplayerMaps.transform.Find(mapName)?.gameObject;

        if (map == null)
        {
            Main.instance.ReplayError($"Could not find the custom map '{mapName}.");
            return null;
        }

        return map;
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

        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c.ToString(), "");

        s = Regex.Replace(s, @"\s+", " ").Trim();

        s = s.Replace("\\", "_").Replace("/", "_");
        
        return string.IsNullOrEmpty(s) ? "Unknown" : s;
    }
    

    // ----- Replay Helpers -----
    
    public static string GetReplayName(ReplayInfo replayInfo, bool isClip = false)
    {
        string sceneName = GetFriendlySceneName(replayInfo.Header.Scene);
        string customMapName = replayInfo.Header.CustomMap;
        
        var localPlayer = PlayerManager.instance.localPlayer;
        string localPlayerName = CleanName(localPlayer.Data.GeneralData.PublicUsername);
        
        string opponent = replayInfo.Header.Players.FirstOrDefault(p => p.MasterId != localPlayer.Data.GeneralData.PlayFabMasterId)?.Name;
        string opponentName = !string.IsNullOrEmpty(opponent)
            ? CleanName(opponent)
            : "Unknown";
        
        string clip = isClip ? "Clip_" : "" ;
        
        DateTime.TryParse(replayInfo.Header.Date, out var dateTime);
        string timestamp = dateTime.ToString("yyyy-MM-dd_hh-mm-ss");
        
        string matchFormat = $"Replay_{clip}{localPlayerName}-vs-{opponentName}_on_{sceneName}_{timestamp}.replay";
        
        if (!string.IsNullOrEmpty(customMapName))
            return $"Replay_{clip}{localPlayerName}-vs-{opponentName}_on_{customMapName}_{timestamp}.replay";

        return sceneName switch
        {
            "Ring" or "Pit" => matchFormat,
            "Park" => $"Replay_{clip}{sceneName}_{replayInfo.Header.Players.Length}P_{localPlayerName}_{timestamp}.replay",
            _ => $"Replay_{clip}{sceneName}_{localPlayerName}_{timestamp}.replay"
        };
    }
    
    public static bool IsReplayClone(PlayerController controller)
    {
        if (controller == null || Main.instance.PlaybackPlayers == null)
            return false;
        
        foreach (var clone in Main.instance.PlaybackPlayers)
            if (clone.Controller == controller)
                return true;

        return false;
    }
    
    public static IEnumerable<GameObject> EnumerateMatchPedestals()
    {
        return GameObject.FindObjectsOfType<Pedestal>(true).Select(p => p.gameObject);
    }
    
    public static IEnumerator LoadMap(int index, float fadeDuration = 2f, Action onLoaded = null, float onLoadedDelay = 0.01f)
    {
        CombatManager.instance.CleanStructureList();
        
        foreach (var structure in CombatManager.instance.structures.ToArray())
            structure?.Kill(Vector3.zero, false, false);
        
        SceneManager.instance.LoadSceneAsync(index, false, false, fadeDuration);

        while (SceneManager.instance.IsLoadingScene)
            yield return null;

        yield return new WaitForSeconds(onLoadedDelay);
        onLoaded?.Invoke();
    }
    
    public static bool HasVFXType(string type, Transform obj)
    {
        var tags = obj.GetComponentsInChildren<ReplayTag>();
        foreach (var t in tags)
        {
            if (t.Type == type)
                return true;
        }
        return false;
    }
    
    public static Vector3 GetPositionOverMesh(float a, float b, MeshRenderer renderer)
    {
        float u = Clamp01(a / b);

        Bounds localBounds = renderer.localBounds;

        float localX = Lerp(localBounds.min.x, localBounds.max.x, u);

        Vector3 localPos = localBounds.center;
        localPos.x = localX;
        return renderer.transform.TransformPoint(localPos);
    }

    public static float GetProgressFromMeshPosition(Vector3 worldPos, MeshRenderer renderer)
    {
        Vector3 localPos = renderer.transform.InverseTransformPoint(worldPos);

        Bounds localBounds = renderer.localBounds;

        float minX = localBounds.min.x;
        float maxX = localBounds.max.x;

        if (Approximately(maxX, minX))
            return 0f;

        float u = InverseLerp(minX, maxX, localPos.x);
        return Clamp01(u);
    }

    // ----- Lerping -----
    
    public static float EaseInOut(float t) => t < 0.5f ? 2 * t * t : 1 - Pow(-2 * t + 2, 2) / 2;
    public static float EaseOut(float t) => 1 - Pow(1 - t, 4);
    public static float EaseIn(float t) => Pow(t, 3);
    
    public static IEnumerator LerpValue<T>(
        Func<T> getter,
        Action<T> setter,
        Func<T, T, float, T> lerpFunc,
        T targetValue,
        float duration,
        Func<float, float> easing = null,
        Action done = null,
        float delay = 0f,
        Func<bool> isValid = null
    )
    {
        yield return new WaitForSeconds(delay);
        
        T startValue = getter();
        float t = 0f;

        while (t < 1f)
        {
            if (isValid != null && !isValid())
                yield break;
            
            t += Time.deltaTime / duration;
            
            float easedT = easing?.Invoke(Clamp01(t)) ?? t;
            setter(lerpFunc(startValue, targetValue, easedT));
            yield return null;
        }

        setter(targetValue);
        done?.Invoke();
    }
    
    
    // ----- Other -----
    
    public static Color32 RandomColor()
    {
        float h = Random.value;
        float s = Random.Range(0.6f, 0.9f);
        float v = Random.Range(0.7f, 1.0f);
        
        Color c = Color.HSVToRGB(h, s, v);
        return c;
    }
    
    public static T[] NewArray<T>(int count, T[] copyFrom = null) where T : new()
    {
        var arr = new T[count];
        for (int i = 0; i < count; i++)
            arr[i] = copyFrom != null && i < copyFrom.Length
                ? copyFrom[i]
                : new T();
        return arr;
    }
}

public static class ReplayFiles
{
    public static string replayFolder = $"{MelonEnvironment.UserDataDirectory}/ReplayMod";
    
    public static List<string> replayPaths = new();
    public static int currentIndex = -1;
    public static ReplayTable table;
    public static bool metadataLerping = false;
    public static bool metadataHidden = false;
    
    public static FileSystemWatcher replayWatcher;
    public static FileSystemWatcher metadataFormatWatcher;
    public static bool reloadQueued;
    public static bool suppressWatcher;

    public static string currentReplayPath = null;
    public static ReplaySerializer.ReplayHeader currentHeader = null;

    
    // ----- Init -----
    
    public static void Init()
    {
        Directory.CreateDirectory(Path.Combine(replayFolder, "Replays"));
        
        EnsureDefaultFormats();
        StartWatchingReplays();
    }

    public static void EnsureDefaultFormats()
    {
        void WriteIfNotExists(string filePath, string contents)
        {
            string path = Path.Combine(replayFolder, "Settings", filePath);
            if (!File.Exists(path))
                File.WriteAllText(path, contents);
        }

        string metadataFormatsFolder = Path.Combine(replayFolder, "Settings", "MetadataFormats");
        Directory.CreateDirectory(metadataFormatsFolder);
        
        string autoNameFormatsFolder = Path.Combine(replayFolder, "Settings", "AutoNameFormats");
        Directory.CreateDirectory(autoNameFormatsFolder);
        
        const string TagHelpText =
            "Available tags:\n" +
            "{Host}\n" +
            "{Client} - first non-host player\n" +
            "{LocalPlayer} - the person who recorded the replay\n" +
            "{Player#}\n" +
            "{Scene}\n" +
            "{Map} - same as {Scene}\n" +
            "{DateTime}\n" +
            "{PlayerCount} - e.g. '1 player', '3 players'\n" +
            "{PlayerList} - Can specify how many player names are shown\n" +
            "{AveragePing}\n" +
            "{MinimumPing} - The lowest ping in the recording\n" +
            "{MaximumPing} - The highest ping in the recording\n" +
            "{Version}\n" +
            "{StructureCount}\n" +
            "{MarkerCount}\n" +
            "{Duration}\n" +
            "{FPS} - Target FPS of the recording\n" +
            "\n" +
            "You can pass parameters to tags using ':'.\n" +
            "Example: {PlayerList:3}, {DateTime:yyyyMMdd}\n\n" +
            "###\n";
        
        WriteIfNotExists("MetadataFormats/metadata_gym.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\nDuration: {Duration}");
        WriteIfNotExists("MetadataFormats/metadata_park.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\n\n{Scene}\n-----------\nHost: {Host}\nPing: {AveragePing} ms ({MinimumPing}-{MaximumPing})\n\n{PlayerList:3}\nDuration: {Duration}");
        WriteIfNotExists("MetadataFormats/metadata_match.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\n\n{Scene}\n-----------\nHost: {Host}\nPing: {AveragePing} ms ({MinimumPing}-{MaximumPing})\nDuration: {Duration}");
        
        WriteIfNotExists("AutoNameFormats/gym.txt", TagHelpText + "{LocalPlayer} - {Scene}");
        WriteIfNotExists("AutoNameFormats/park.txt", TagHelpText + "{Host} - {Scene}\n");
        WriteIfNotExists("AutoNameFormats/match.txt", TagHelpText + "{Host} vs {Client} - {Scene}");
    }
    
    
    // ----- Format Files -----

    public static string LoadFormatFile(string path)
    {
        string fullPath = Path.Combine(replayFolder, "Settings", path + ".txt");
        if (!File.Exists(fullPath)) return null;
        
        var lines = File.ReadAllLines(fullPath);
        int startIndex = Array.FindIndex(lines, line => line.Trim() == "###");
        if (startIndex == -1 || startIndex + 1 >= lines.Length) 
            return null;

        var formatLines = lines.Skip(startIndex + 1);
        return string.Join("\n", formatLines);
    }

    public static string GetMetadataFormat(string scene)
    {
        return scene switch
        {
            "Gym" => LoadFormatFile("MetadataFormats/metadata_gym"),

            "Park" => LoadFormatFile("MetadataFormats/metadata_park"),

            "Map0" or "Map1" => LoadFormatFile("MetadataFormats/metadata_match"),

            _ => "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\nDuration: {Duration}\n\n{StructureCount}"
        };
    }
    

    public static ReplaySerializer.ReplayHeader GetManifest(string path)
    {
        var header = ReplaySerializer.GetManifest(path);
        
        if (string.IsNullOrEmpty(header.Title))
        {
            string scene = header.Scene;

            string pattern = scene switch
            {
                "Gym" => LoadFormatFile("AutoNameFormats/gym"),
                "Park" => LoadFormatFile("AutoNameFormats/park"),
                "Map0" or "Map1" => LoadFormatFile("AutoNameFormats/match"),
                _ => null
            };

            header.Title = ReplaySerializer.FormatReplayString(pattern, header);
        }
        
        return header;
    }
    

    // ----- Metadata -----
    
    static void StartWatchingReplays()
    {
        replayWatcher = new FileSystemWatcher(Path.Combine(replayFolder, "Replays"), "*.replay");

        replayWatcher.NotifyFilter =
            NotifyFilters.FileName |
            NotifyFilters.LastWrite |
            NotifyFilters.CreationTime;

        replayWatcher.Created += OnReplayFolderChanged;
        replayWatcher.Deleted += OnReplayFolderChanged;
        replayWatcher.Renamed += OnReplayFolderChanged;
        
        replayWatcher.EnableRaisingEvents = true;

        metadataFormatWatcher = new FileSystemWatcher(Path.Combine(replayFolder, "Settings", "MetadataFormats"), "*.txt");

        metadataFormatWatcher.NotifyFilter = NotifyFilters.LastWrite;
        metadataFormatWatcher.Changed += OnFormatChanged;
        metadataFormatWatcher.EnableRaisingEvents = true;
    }

    static void OnReplayFolderChanged(object sender, FileSystemEventArgs e)
    {
        IEnumerator ReloadNextFrame()
        {
            yield return null;

            reloadQueued = false;
            ReloadReplays();
        }
        
        if (reloadQueued || suppressWatcher) return;
        
        reloadQueued = true;
        MelonCoroutines.Start(ReloadNextFrame());
    }

    static void OnFormatChanged(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(currentReplayPath) || currentHeader == null)
            return;

        var format = GetMetadataFormat(currentHeader.Scene);
        table.metadataText.text = ReplaySerializer.FormatReplayString(format, currentHeader);
        table.metadataText.ForceMeshUpdate();
    }
    

    public static void HideMetadata()
    {
        if (table.metadataText == null || metadataLerping) return;

        metadataLerping = true;
        metadataHidden = true;
        
        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.desiredMetadataTextHeight,
            v => table.desiredMetadataTextHeight = v,
            Lerp,
            1.5229f,
            1f,
            Utilities.EaseInOut
        ));

        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.metadataText.transform.localScale,
            v => table.metadataText.transform.localScale = v,
            Vector3.Lerp,
            Vector3.zero,
            1.3f,
            Utilities.EaseInOut,
            () => metadataLerping = false
        ));
    }

    public static void ShowMetadata()
    {
        if (table.metadataText == null || metadataLerping) return;

        metadataLerping = true;
        
        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.desiredMetadataTextHeight,
            v => table.desiredMetadataTextHeight = v,
            Lerp,
            1.9514f,
            1.3f,
            Utilities.EaseInOut
        ));

        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.metadataText.transform.localScale,
            v => table.metadataText.transform.localScale = v,
            Vector3.Lerp,
            Vector3.one * 0.25f,
            1.3f,
            Utilities.EaseInOut,
            () => metadataLerping = false
        ));
        
        metadataHidden = false;
    }
    

    public static string BuildPlayerLine(PlayerInfo[] players, int maxNames)
    {
        if (players == null || players.Length == 0)
            return string.Empty;

        int count = players.Length;
        int shown = Math.Min(count, maxNames);

        var names = new List<string>(shown);
        for (int i = 0; i < shown; i++)
            names.Add($"{players[i].Name}<#FFF>");

        string line = string.Join(", ", names);

        if (count > maxNames)
            line += $" +{count - maxNames} others";

        return
            $"{count} player{(count == 1 ? "" : "s")}\n" +
            $"{line}\n";
    }
    
    
    // ----- Replay Selection -----
    
    public static void SelectReplay(int index)
    {
        if (table.replayNameText == null) return;
        
        if (index < 0 || index >= replayPaths.Count)
        {
            currentIndex = -1;
            currentReplayPath = null;
            table.replayNameText.text = "No Replay Selected";
            table.indexText.text = $"(0 / {replayPaths.Count})";
            HideMetadata();
            Main.instance.replaySettings.gameObject.SetActive(false);
        }
        else
        {
            currentIndex = index;
            currentReplayPath = replayPaths[index];

            int shownIndex = currentIndex < 0 ? 0 : currentIndex + 1;
            table.indexText.text = $"({shownIndex} / {replayPaths.Count})";

            try
            {
                var header = GetManifest(currentReplayPath);
                currentHeader = header;
                table.replayNameText.text = ReplaySerializer.GetReplayDisplayName(currentReplayPath, currentHeader);
                Main.instance.replaySettings.Show(currentReplayPath);
                
                var format = GetMetadataFormat(header.Scene);
                table.metadataText.text = ReplaySerializer.FormatReplayString(format, header);
                ShowMetadata();
                Main.instance.replaySettings.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                Main.instance.LoggerInstance.Error($"Failed to load replay `{currentReplayPath}':{e}");
                
                table.replayNameText.text = "Invalid Replay";
                table.indexText.text = "Invalid Replay";
                HideMetadata();
                Main.instance.replaySettings.gameObject.SetActive(false);
                currentReplayPath = null;
                currentIndex = -1;
            }
        }
        
        table.replayNameText.ForceMeshUpdate();
        table.indexText.ForceMeshUpdate();
        table.metadataText.ForceMeshUpdate();
        ApplyTMPSettings(table.replayNameText, 5f, 0.51f, true);
        ApplyTMPSettings(table.indexText, 5f, 0.51f, false);
        ApplyTMPSettings(table.metadataText, 15f, 2f, true);
        table.metadataText.enableAutoSizing = true;
    }

    public static void NextReplay()
    {
        if (replayPaths.Count == 0) return;

        int nextIndex = currentIndex + 1;
        if (nextIndex > replayPaths.Count - 1)
            nextIndex = -1;

        SelectReplay(nextIndex);
    }

    public static void PreviousReplay()
    {
        if (replayPaths.Count == 0) return;

        int previousIndex = currentIndex - 1;
        if (previousIndex < -1)
            previousIndex = replayPaths.Count - 1;
        
        SelectReplay(previousIndex);
    }
    

    public static void LoadReplays()
    {
        replayPaths = Directory
            .GetFiles(Path.Combine(replayFolder, "Replays"), "*.replay", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        currentIndex = Clamp(currentIndex, -1, replayPaths.Count - 1);

        SelectReplay(currentIndex);
    }

    public static void ReloadReplays()
    {
        LoadReplays();

        if (!string.IsNullOrEmpty(currentReplayPath))
        {
            int newIndex = replayPaths.FindIndex(p =>
            {
                var header = ReplaySerializer.GetManifest(p);
                return header.Guid == currentHeader?.Guid;
            });
            currentIndex = newIndex >= 0 ? newIndex : -1;
        }
        else
        {
            currentIndex = -1;
        }

        SelectReplay(currentIndex);
    }
    

    static void ApplyTMPSettings(TextMeshPro text, float horizontal, float vertical, bool apply)
    {
        if (text == null) return;
        
        text.horizontalAlignment = HorizontalAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, horizontal);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, vertical);

        if (apply)
        {
            text.fontSizeMin = 0.1f;
            text.fontSizeMax = 7f;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = true;
            text.autoSizeTextContainer = true;
        }
    }
}

public static class ReplayPlaybackControls
{
    public static bool playbackControlsOpen;

    public static GameObject playbackControls;
    public static GameObject timeline;
    public static TextMeshPro totalDuration;
    public static TextMeshPro currentDuration;
    public static TextMeshPro playbackTitle;
    public static TextMeshPro playbackSpeedText;

    public static Image playButtonSprite;
    public static Sprite pauseSprite;
    public static Sprite playSprite;

    public static DestroyOnPunch destroyOnPunch;

    public static GameObject markerPrefab;
    
    public static float smoothing = 7f;

    public static void Update()
    {
        var head = Main.instance.head;
        if (head == null || playbackControls == null || !(bool)Main.instance.PlaybackControlsFollow.SavedValue)
            return;

        float armSpan = Main.LocalPlayer.Data.PlayerMeasurement.ArmSpan;
        float distanceToPlayer = Vector3.Distance(playbackControls.transform.position, head.position);

        if (distanceToPlayer < armSpan)
            return;

        var (targetPos, targetRot) = GetTargetSlabTransform(head);

        float proximityT = InverseLerp(armSpan, armSpan * 1.5f, distanceToPlayer);
        float scaledT = (1f - Exp(-smoothing * Time.deltaTime)) * proximityT;

        playbackControls.transform.position = Vector3.Lerp(playbackControls.transform.position, targetPos, scaledT);
        playbackControls.transform.rotation = Quaternion.Slerp(playbackControls.transform.rotation, targetRot, scaledT);
    }

    public static (Vector3 position, Quaternion rotation) GetTargetSlabTransform(Transform head)
    {
        Vector3 forward = head.forward;
        forward.y = 0f;
        
        Vector3 position = head.position + forward * 0.6f + Vector3.down * 0.1f;

        Vector3 lookDir = head.position - position;
        lookDir.y = 0f;

        Quaternion rotation = Quaternion.LookRotation(lookDir);

        return (position, rotation);
    }
    
    public static void Open()
    {
        if (Main.instance.head == null) return;

        if (playbackControlsOpen)
            playbackControls.SetActive(false);
        
        playbackControlsOpen = true;

        var (position, rotation) = GetTargetSlabTransform(Main.instance.head);
        
        if (Main.LocalPlayer.Controller.GetSubsystem<PlayerMovement>().IsGrounded())
        {
            playbackControls.transform.position = position;
            playbackControls.transform.rotation = rotation;
            playbackControls.SetActive(true);

            AudioManager.instance.Play(ReplayCache.SFX["Call_Slab_Construct"], Main.instance.head.position);
        }
    }

    public static void Close()
    {
        if (!playbackControlsOpen) return;
        
        playbackControlsOpen = false;
    
        playbackControls.SetActive(false);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_Slab_Dismiss"], Main.instance.head.position);
        PoolManager.instance.GetPool("DustBreak_VFX").FetchFromPool(playbackControls.transform.position, playbackControls.transform.rotation)
            .transform.localScale = Vector3.one * 0.4f;
    }
}

[RegisterTypeInIl2Cpp]
public class DestroyOnPunch : MonoBehaviour
{
    public InteractionHand leftHand;
    public InteractionHand rightHand;
    public Action onDestroy;

    public float punchThreshold = 3.0f;

    public void OnTriggerEnter(Collider other)
    {
        Vector3 velocity = Vector3.zero;

        bool isLeftHand = true;
        if (other.name.Contains("Bone_Pointer_C_L"))
        {
            velocity = leftHand.SampleVelocity(1);
        }
        else if (other.name.Contains("Bone_Pointer_C_R"))
        {
            velocity = rightHand.SampleVelocity(1);
            isLeftHand = false;
        }

        if (velocity.magnitude > punchThreshold)
        {
            onDestroy?.Invoke();
            
            if ((bool)Main.instance.EnableHaptics.SavedValue)
                Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(
                    isLeftHand ? 1f : 0f, isLeftHand ? 0.2f : 0f, !isLeftHand ? 1f : 0f, !isLeftHand ? 0.2f : 0f
                );
        }
    }
}

[RegisterTypeInIl2Cpp]
public class ReplaySettings : MonoBehaviour
{
    private string currentPath;
    private ReplaySerializer.ReplayHeader currentHeader;
    
    public static TextMeshPro replayName;
    public static TextMeshPro dateText;
    public static TextMeshPro renameInstructions;
    public static TextMeshPro durationComp;
    
    public static InteractionButton renameButton;
    public static InteractionButton deleteButton;
    public static InteractionButton copyPathButton;
    
    public static GameObject povButton;
    public static GameObject hideLocalPlayerToggle;
    public static GameObject openControlsButton;
    
    public static bool hideLocalPlayer = true;
    public static TextMeshPro pageNumberText;
    
    public static bool selectionInProgress;
    public static Player selectedPlayer;
    public static Dictionary<int, List<(UserData, Player)>> playerList = new();
    public static int currentPlayerPage = 0;

    public static GameObject slideOutPanel;

    public static GameObject timeline;

    private bool isRenaming = false;
    private StringBuilder renameBuffer = new();
    private string rawReplayName;

    public void Show(string path)
    {
        povButton.SetActive(Main.isPlaying);
        hideLocalPlayerToggle.SetActive(Main.isPlaying);
        openControlsButton.SetActive(Main.isPlaying);
        
        currentPath = path;
        currentHeader = ReplaySerializer.GetManifest(path);

        rawReplayName = Path.GetFileNameWithoutExtension(path);
        
        replayName.text = ReplaySerializer.GetReplayDisplayName(path, currentHeader);
        replayName.ForceMeshUpdate();
        
        renameBuffer.Clear();
        renameBuffer.Append(rawReplayName);

        dateText.text = ReplaySerializer.FormatReplayString("{DateTime:yyyy/MM/dd hh:mm tt}", currentHeader);
        dateText.ForceMeshUpdate();
        
        slideOutPanel.SetActive(false);
        slideOutPanel.transform.localPosition = new Vector3(0.1709f, 0.5273f, 0.16f);

        timeline.transform.GetChild(0).GetComponent<TimelineScrubber>().header = currentHeader;
        timeline.GetComponent<MeshRenderer>().material.SetFloat("_BP_Target", currentHeader.Duration * 1000f);
        Main.AddMarkers(currentHeader, timeline.GetComponent<MeshRenderer>(), false);
        
        renameButton.SetButtonToggleStatus(false, withEvents: true);
        renameInstructions.gameObject.SetActive(false);
        isRenaming = false;

        TimeSpan t = TimeSpan.FromSeconds(currentHeader.Duration);
        
        durationComp.text = t.TotalHours >= 1 ? 
            $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : 
            $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        
        durationComp.ForceMeshUpdate();
        
        isRenaming = false;
    }

    private void Update()
    {
        if (!isRenaming)
            return;
        
        if (Input.GetKeyDown(KeyCode.Return))
        {
            TryRename(renameBuffer.ToString());
            isRenaming = false;
            renameInstructions.gameObject.SetActive(false);
            renameButton.SetButtonToggleStatus(false, true);
            return;
        } 
        
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            replayName.text = ReplaySerializer.GetReplayDisplayName(currentPath, currentHeader);
            replayName.ForceMeshUpdate();
            renameButton.SetButtonToggleStatus(false, true);
            renameInstructions.gameObject.SetActive(false);
            isRenaming = false;
            return;
        }

        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (renameBuffer.Length > 0)
                {
                    renameBuffer.Remove(renameBuffer.Length - 1, 1);
                    AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], transform.position);
                }
            } else if (IsAllowedChar(c))
            {
                renameBuffer.Append(c);
                AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardUnlocked"], transform.position);
            }
            else
            {
                Main.instance.ReplayError();
            }
        }

        replayName.text = ReplaySerializer.GetReplayDisplayName(currentPath, currentHeader, renameBuffer.ToString(), false);
        replayName.ForceMeshUpdate();
    }

    public void OnRenamePressed(bool toggleState)
    {
        if (toggleState)
        {
            replayName.text = ReplaySerializer.GetReplayDisplayName(currentPath, currentHeader);
            replayName.ForceMeshUpdate();
            isRenaming = false;
            renameInstructions.gameObject.SetActive(false);
        }
        else
        {
            isRenaming = true;
            renameBuffer.Clear();
            renameBuffer.Append(rawReplayName);
            
            renameInstructions.gameObject.SetActive(true);
        }
    }

    private bool IsAllowedChar(char c)
    {
        char[] blocked = Path.GetInvalidFileNameChars();
        return !blocked.Contains(c);
    }

    private void TryRename(string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            Main.instance.ReplayError($"Invalid replay name ({newName})", transform.position);
            return;
        }
        
        string dir = Path.GetDirectoryName(currentPath);
        string newPath = Path.Combine(dir, newName + ".replay");

        if (File.Exists(newPath))
        {
            Main.instance.ReplayError($"Name already exists ({newPath})", transform.position);
            return;
        }

        File.Move(currentPath, newPath);
        currentPath = newPath;
        Show(currentPath);

        AudioManager.instance.Play(ReplayCache.SFX["Call_PoseGhost_MovePerformed"], transform.position);
    }
    
        public static Dictionary<int, List<(UserData, Player)>> PaginateReplay(
        ReplaySerializer.ReplayHeader header, 
        Clone[] PlaybackPlayers, 
        bool includeLocalPlayer = true
    )
    {
        var allEntries = new List<(UserData, Player)>();

        if (includeLocalPlayer)
        {
            var local = Main.LocalPlayer;
            allEntries.Add((
                new UserData(
                    PlatformManager.CurrentPlatform,
                    local.Data.GeneralData.PlayFabMasterId,
                    Guid.NewGuid().ToString(),
                    $"You ({local.Data.GeneralData.PublicUsername})",
                    local.Data.GeneralData.BattlePoints
                ),
                local
            ));
        }

        var players = header.Players;

        for (int i = 0; i < players.Length; i++)
        {
            allEntries.Add((
                new UserData(
                    PlatformManager.Platform.Unknown,
                     $"{players[i].MasterId}_{Guid.NewGuid().ToString()}",
                    Guid.NewGuid().ToString(),
                    players[i].Name,
                    players[i].BattlePoints
                ),
                PlaybackPlayers[i].Controller.assignedPlayer
            ));
        }

        var result = new Dictionary<int, List<(UserData, Player)>>();
        int pageCount = CeilToInt(allEntries.Count / 4f);

        for (int i = 0; i < pageCount; i++)
        {
            var page = new List<(UserData, Player)>();

            for (int j = 0; j < 4; j++)
            {
                int index = i * 4 + j;
                if (index >= allEntries.Count)
                    break;

                page.Add(allEntries[index]);
            }

            result[i] = page;
        }

        return result;
    }

    public static IEnumerator SelectPlayer(Action<Player> callback, float afterSelectionDelay)
    {
        if (selectionInProgress)
            yield break;
        
        selectionInProgress = true;
        selectedPlayer = null;

        TogglePlayerSelection(true);

        while (selectedPlayer == null && selectionInProgress)
            yield return null;

        yield return new WaitForSeconds(afterSelectionDelay);
        
        TogglePlayerSelection(false);
        
        callback?.Invoke(selectedPlayer);
        selectionInProgress = false;
    }

    public static void TogglePlayerSelection(bool active)
    {
        slideOutPanel.SetActive(true);

        if (active)
            SelectPlayerPage(0);

        if (!active)
            selectionInProgress = false;
        
        AudioManager.instance.Play(ReplayCache.SFX[active ? "Call_Phone_ScreenUp" : "Call_Phone_ScreenDown"], slideOutPanel.transform.localPosition);

        Vector3 position = active ? new Vector3(-1.1906f, 0.5273f, 0.16f) : new Vector3(-0.1288f, 0.5273f, 0.16f);
        MelonCoroutines.Start(Utilities.LerpValue(
            () => slideOutPanel.transform.localPosition,
            v => slideOutPanel.transform.localPosition = v,
            Vector3.Lerp,
            position,
            0.8f,
            Utilities.EaseIn,
            () => { if (!active) slideOutPanel.SetActive(false); }
        ));
    }

    public static (UserData data, Player player) PlayerAtIndex(int index) 
        => playerList.TryGetValue(currentPlayerPage, out var list) ? (index >= 0 && index < list.Count ? list[index] : (null, null)) : (null, null);

    public static void SelectPlayerPage(int page)
    {
        int maxPage = Max(0, playerList.Count - 1);
        currentPlayerPage = Clamp(page, 0, maxPage);

        pageNumberText.text = $"{currentPlayerPage + (playerList.Count == 0 ? 0 : 1)} / {playerList.Count}";
        pageNumberText.ForceMeshUpdate();

        var slabs = slideOutPanel.GetComponentsInChildren<PlayerTag>(true);
        var usersOnPage = playerList.TryGetValue(currentPlayerPage, out var value) ? value : new List<(UserData, Player)>();

        for (int i = 0; i < slabs.Length; i++)
        {
            if (i < usersOnPage.Count)
            {
                slabs[i].gameObject.SetActive(true);
                slabs[i].Initialize(usersOnPage[i].Item1);
            }
            else
            {
                slabs[i].gameObject.SetActive(false);
            }
        }
    }
}

public static class ReplayCrystals
{
    public static GameObject crystalPrefab;
    public static GameObject crystalParent;
    public static List<Crystal> Crystals = new();
    public static Crystal heldCrystal;
    public static bool isHeldByRight;
    
    public static VisualEffect crystalizeVFX;

    public static (string path, Color32 color) lastReplayColor = new();
    
    public static void HandleCrystals()
    {
        const float grabRadius = 0.1f;

        if (heldCrystal != null)
        {
            bool released = 
                (isHeldByRight && Calls.ControllerMap.RightController.GetGrip() < 0.5f) ||
                (!isHeldByRight && Calls.ControllerMap.LeftController.GetGrip() < 0.5f);

            if (released)
            {
                heldCrystal.Release();
                heldCrystal.transform.SetParent(crystalParent.transform, true);
                
                heldCrystal = null;
                return;
            }
        }
        
        if (heldCrystal == null)
        {
            if (Calls.ControllerMap.RightController.GetGrip() > 0.5f)
            {
                var crystal = FindClosestCrystal(Main.instance.rightHand.position, grabRadius);
                if (crystal != null)
                {
                    heldCrystal = crystal;
                    isHeldByRight = true;
                    crystal.Grab();
                    crystal.transform.SetParent(Main.instance.rightHand.transform, true);
                }
            } else if (Calls.ControllerMap.LeftController.GetGrip() > 0.5f)
            {
                var crystal = FindClosestCrystal(Main.instance.leftHand.position, grabRadius);
                if (crystal != null)
                {
                    heldCrystal = crystal;
                    isHeldByRight = false;
                    crystal.Grab();
                    crystal.transform.SetParent(Main.instance.leftHand.transform, true);
                }
            }
        }
    }
    
    
    public static void LoadCrystals(string scene = null)
    {
        if (string.IsNullOrEmpty(scene))
            scene = Main.instance.currentScene;
        
        string path = Path.Combine(
            MelonEnvironment.UserDataDirectory,
            "ReplayMod",
            "Settings",
            "replayCrystals.json"
        );

        if (!File.Exists(path))
            return;

        string json = File.ReadAllText(path);
        var allStates = JsonConvert.DeserializeObject<Dictionary<string, CrystalState[]>>(json);

        if (allStates != null && allStates.TryGetValue(scene, out var states))
        {
            Crystals = new();

            foreach (var state in states)
                CreateCrystal().RestoreState(state);
        }
    }

    public static void SaveCrystals(string scene = null)
    {
        if (string.IsNullOrEmpty(scene))
            scene = Main.instance.currentScene;
        
        string path = Path.Combine(
            MelonEnvironment.UserDataDirectory,
            "ReplayMod",
            "Settings",
            "replayCrystals.json"
        );

        Dictionary<string, CrystalState[]> allStates = new();

        if (File.Exists(path))
        {
            string existingJson = File.ReadAllText(path);
            allStates = JsonConvert.DeserializeObject<Dictionary<string, CrystalState[]>>(existingJson)
                ?? new();
        }

        if (Crystals == null)
            return;

        var states = new List<CrystalState>();
        foreach (var crystal in Crystals)
        {
            if (crystal != null)
                states.Add(crystal.CaptureState());
        }

        allStates[scene] = states.ToArray();
        
        string newJson = JsonConvert.SerializeObject(allStates, Formatting.Indented);
        File.WriteAllText(path, newJson);
    }
    

    public static Crystal FindClosestCrystal(Vector3 handPos, float maxDistance)
    {
        Crystal closest = null;
        float closestSqr = maxDistance * maxDistance;

        foreach (var crystal in Crystals)
        {
            if (crystal == null)
                continue;
            
            float sqr = (crystal.transform.position - handPos).sqrMagnitude;
            if (sqr < closestSqr)
            {
                closestSqr = sqr;
                closest = crystal;
            }
        }

        return closest;
    }
    
    public static Crystal CreateCrystal(Vector3 position, ReplaySerializer.ReplayHeader header, string path, bool useAnimation = false, bool applyRandomColor = false)
    {
        if (crystalParent == null)
            crystalParent = new GameObject("Crystals");
        
        Crystal crystal = GameObject.Instantiate(crystalPrefab, crystalParent.transform).AddComponent<Crystal>();
            
        var name = Path.GetFileNameWithoutExtension(path).StartsWith("Replay") ? header.Title : Path.GetFileNameWithoutExtension(path);
        
        crystal.name = $"Crystal ({name}, {header.Date})";
        crystal.transform.position = position;
        crystal.Title = name;

        GameObject text = Calls.Create.NewText(name, 1f, Color.white, Vector3.zero, Quaternion.identity);

        text.name = "Replay Title";
        text.transform.SetParent(crystal.transform, false);
        text.transform.localScale = Vector3.zero;
        text.transform.localRotation = Quaternion.Euler(0, 270, 270);

        crystal.titleText = text.GetComponent<TextMeshPro>();
        crystal.titleText.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 2);
        crystal.titleText.horizontalAlignment = HorizontalAlignmentOptions.Center;
        crystal.titleText.ForceMeshUpdate();

        var lookAt = text.AddComponent<LookAtPlayer>();
        lookAt.lockX = true;
        lookAt.lockZ = true;
                
        crystal.ReplayPath = ReplayFiles.currentReplayPath;

        if (applyRandomColor)
        {
            crystal.BaseColor = Utilities.RandomColor();
            crystal.ApplyVisuals();
        }
        
        crystal.gameObject.SetActive(true);
                
        Crystals.Add(crystal);

        if (useAnimation)
            MelonCoroutines.Start(CrystalSpawnAnimation(crystal));

        return crystal;
    }

    public static Crystal CreateCrystal()
    {
        if (crystalParent == null)
            crystalParent = new GameObject("Crystals");
        
        Crystal crystal = GameObject.Instantiate(crystalPrefab, crystalParent.transform).AddComponent<Crystal>();
        crystal.hasLeftTable = true;
        crystal.gameObject.SetActive(true);

        GameObject text = Calls.Create.NewText("", 1f, Color.white, Vector3.zero, Quaternion.identity);

        text.name = "Replay Title";
        text.transform.SetParent(crystal.transform, false);
        text.transform.localScale = Vector3.zero;
        text.transform.localRotation = Quaternion.Euler(0, 270, 270);

        crystal.titleText = text.GetComponent<TextMeshPro>();
        crystal.titleText.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 2);
        crystal.titleText.horizontalAlignment = HorizontalAlignmentOptions.Center;
        crystal.titleText.ForceMeshUpdate();
        
        var lookAt = text.AddComponent<LookAtPlayer>();
        lookAt.lockX = true;
        lookAt.lockZ = true;
        
        Crystals.Add(crystal);

        return crystal;
    }

    public static IEnumerator ReadCrystal(Crystal crystal)
    {
        crystal.isAnimation = true;
        
        lastReplayColor = (crystal.ReplayPath, crystal.BaseColor);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_GearMarket_ButtonUnpress"], crystal.transform.position);

        yield return Utilities.LerpValue(
            () => crystal.transform.position,
            v => crystal.transform.position = v,
            Vector3.Lerp,
            Main.instance.replayTable.transform.position + new Vector3(0, 0.3045f, 0),
            1f,
            Utilities.EaseInOut
        );
        
        yield return new WaitForSeconds(1f);

        AudioManager.instance.Play(ReplayCache.SFX["Call_Phone_ScreenDown"], crystal.transform.position);
        
        yield return Utilities.LerpValue(
            () => crystal.BaseColor,
            v =>
            {
                crystal.BaseColor = v;
                crystal.ApplyVisuals();
            },
            Color32.Lerp,
            new(50, 50, 50, 255),
            1f,
            Utilities.EaseInOut
        );

        yield return new WaitForSeconds(0.5f);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_ToolTip_Close"], crystal.transform.position);

        MelonCoroutines.Start(Utilities.LerpValue(
            () => crystal.transform.position,
            v => crystal.transform.position = v,
            Vector3.Lerp,
            Main.instance.replayTable.transform.position,
            0.5f,
            Utilities.EaseInOut
        ));

        yield return Utilities.LerpValue(
            () => crystal.transform.localScale,
            v => crystal.transform.localScale = v,
            Vector3.Lerp,
            Vector3.zero,
            0.5f,
            Utilities.EaseInOut,
            () =>
            {
                int index = ReplayFiles.replayPaths.IndexOf(crystal.ReplayPath);
                
                if (index != -1)
                    ReplayFiles.SelectReplay(index);
                else
                    AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Failure"], crystal.transform.position);
                
                Crystals.Remove(crystal);
                GameObject.Destroy(crystal.gameObject);
                SaveCrystals();
            }
        );
    }

    public static IEnumerator CrystalBreakAnimation(string replayPath, Crystal crystal = null, float dist = 0.005f)
    {
        AudioManager.instance.Play(ReplayCache.SFX["Call_GearMarket_ButtonUnpress"], Main.instance.replayTable.transform.position);

        crystalizeVFX.transform.localPosition = new Vector3(0, 0, 0.3903f);
        
        bool isNewCrystal = false;
        if (crystal == null)
        {
            isNewCrystal = true;
            crystal = CreateCrystal();
            crystal.transform.position = Main.instance.replayTable.transform.position;
            crystal.transform.localScale = Vector3.zero;
            crystal.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            crystal.BaseColor = Utilities.RandomColor();
            crystal.ApplyVisuals();
            
            MelonCoroutines.Start(Utilities.LerpValue(
                () => crystal.transform.localScale,
                v => crystal.transform.localScale = v,
                Vector3.Lerp,
                Vector3.one * 50f,
                1f,
                Utilities.EaseInOut
            ));
        }

        crystal.isAnimation = true;

        if (isNewCrystal)
        {
            MelonCoroutines.Start(Utilities.LerpValue(
                () => crystal.transform.position,
                v => crystal.transform.position = v,
                Vector3.Lerp,
                Main.instance.replayTable.transform.position + new Vector3(0, 0.4045f, 0),
                1.5f,
                Utilities.EaseInOut
            ));
        }
        else
        {
            yield return Utilities.LerpValue(
                () => crystal.transform.position,
                v => crystal.transform.position = v,
                Vector3.Lerp,
                Main.instance.replayTable.transform.position + new Vector3(0, 0.4045f, 0),
                1.5f,
                Utilities.EaseInOut
            );
        }
        
        yield return new WaitForSeconds(0.2f);

        MelonCoroutines.Start(SpinEaseInOut(crystal.transform, 360, 2.4f, Vector3.forward));

        yield return new WaitForSeconds(1f);

        AudioManager.instance.Play(ReplayCache.SFX["Call_FistBumpBonus"], crystal.transform.position);
        AudioManager.instance.Play(ReplayCache.SFX["Call_Slab_Dismiss"], crystal.transform.position);
        AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_Use"], crystal.transform.position);

        Main.instance.replayTable.transform.GetChild(7).GetComponent<VisualEffect>().Play();

        if (crystal.TryGetComponent<Renderer>(out var mainRenderer))
            mainRenderer.enabled = false;

        foreach (var shard in crystal.GetComponentsInChildren<Transform>(true))
        {
            if (shard == crystal.transform) continue;
            
            shard.gameObject.SetActive(true);

            Vector3 target = shard.localPosition.normalized * dist;
            MelonCoroutines.Start(Utilities.LerpValue(
                () => shard.localPosition,
                v => shard.localPosition = v,
                Vector3.Lerp,
                target,
                1f,
                Utilities.EaseOut,
                isValid: () => shard != null
            ));
        }

        yield return new WaitForSeconds(0.948f);
        
        foreach (var shard in crystal.GetComponentsInChildren<Transform>(true))
        {
            if (shard == crystal.transform) continue;

            MelonCoroutines.Start(Utilities.LerpValue(
                () => shard.localScale,
                v => shard.localScale = v,
                Vector3.Lerp,
                Vector3.zero,
                0.5f,
                Utilities.EaseIn,
                isValid: () => shard != null,
                delay: Random.value,
                done: () => AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], shard.transform.position)
            ));
        }

        yield return new WaitForSeconds(1f);

        Crystals.Remove(crystal);
        GameObject.Destroy(crystal);
        SaveCrystals();

        File.Delete(replayPath);

        Main.instance.crystalBreakCoroutine = null;
    }

    public static IEnumerator SpinEaseInOut(Transform target, float maxSpeed, float duration, Vector3 axis)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float eased = 6 * t * (1 - t);
            float spinSpeed = maxSpeed * eased;

            target.Rotate(axis, spinSpeed * Time.deltaTime, Space.Self);

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    public static IEnumerator CrystalSpawnAnimation(Crystal crystal)
    {
        crystal.isAnimation = true;
        
        crystal.transform.localScale = Vector3.zero;
        crystal.transform.position = Main.instance.replayTable.transform.position;

        crystalizeVFX.transform.localPosition = new Vector3(0, 0, 0.3045f);

        crystal.BaseColor = new (50, 50, 50, 255);
        crystal.ApplyVisuals();

        AudioManager.instance.Play(ReplayCache.SFX["Call_MoveSelector_Unlock"], crystal.transform.position);

        MelonCoroutines.Start(Utilities.LerpValue(
            () => crystal.transform.position,
            v => crystal.transform.position = v,
            Vector3.Lerp,
            Main.instance.replayTable.transform.position + new Vector3(0, 0.3045f, 0),
            1.5f,
            Utilities.EaseInOut
        ));

        yield return Utilities.LerpValue(
            () => crystal.transform.localScale,
            v => crystal.transform.localScale = v,
            Vector3.Lerp,
            Vector3.one * 50f,
            1.5f,
            Utilities.EaseInOut
        );

        yield return new WaitForSeconds(1f);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_Use"], crystal.transform.position);
        
        crystalizeVFX.Play();

        MelonCoroutines.Start(Utilities.LerpValue(
            () => crystal.BaseColor,
            v =>
            {
                crystal.BaseColor = v;
                crystal.ApplyVisuals();
            },
            Color32.Lerp,
            !string.IsNullOrEmpty(lastReplayColor.path) && crystal.ReplayPath == lastReplayColor.path ? lastReplayColor.color : Utilities.RandomColor(),
            0.05f,
            Utilities.EaseInOut
        ));
        

        yield return Utilities.LerpValue(
            () => crystal.transform.localRotation,
            v => crystal.transform.localRotation = v,
            Quaternion.Slerp,
            Quaternion.Euler(290, 0, 0),
            0.05f
        );

        yield return Utilities.LerpValue(
            () => crystal.transform.localRotation,
            v => crystal.transform.localRotation = v,
            Quaternion.Slerp,
            Quaternion.Euler(270, 0, 0),
            0.7f,
            Utilities.EaseInOut
        );

        crystal.isAnimation = false;
    }
    
    [RegisterTypeInIl2Cpp]
    public class Crystal : MonoBehaviour
    {
        public string ReplayPath;
        
        public string Title;
        public TextMeshPro titleText;

        public Color32 BaseColor;

        private Renderer rend;
        private MaterialPropertyBlock mpb;
        
        public bool isGrabbed;
        public bool isAnimation;
        public bool hasLeftTable;

        private Vector3 basePosition;
        private Vector3 velocity;
        private Vector3 lastPosition;

        private Vector3 angularVelocity;
        private Quaternion lastRotation;
        
        private bool hasSavedAfterRelease;

        private object scaleRoutine;
        private object positionRoutine;
        private bool isTextVisible;

        private void Awake()
        {
            basePosition = transform.position;
            lastPosition = transform.position;
        }
        
        public CrystalState CaptureState()
        {
            return new CrystalState
            {
                ReplayPath = ReplayPath,
                Title = Title,
                x = transform.position.x,
                y = transform.position.y,
                z = transform.position.z,
                BaseColor = BaseColor
            };
        }

        public void RestoreState(CrystalState state)
        {
            ReplayPath = state.ReplayPath;
            BaseColor = state.BaseColor;

            basePosition = new Vector3(state.x, state.y, state.z);
            transform.position = basePosition;

            Title = state.Title;
            titleText.text = state.Title;
            titleText.ForceMeshUpdate();
            
            ApplyVisuals();
        }
        
        public void ApplyVisuals()
        {
            Color baseColor = BaseColor;

            if (rend == null)
                rend = GetComponent<Renderer>();

            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            ApplyBlockToRenderer(rend, baseColor);

            foreach (var childRenderer in GetComponentsInChildren<Renderer>(true))
            {
                if (childRenderer == rend) continue;
                ApplyBlockToRenderer(childRenderer, baseColor);
            }
        }

        private void ApplyBlockToRenderer(Renderer r, Color baseColor)
        {
            if (r == null) return;

            mpb.Clear();
            mpb.SetColor("_Base_Color", baseColor);
            mpb.SetColor("_Edge_Color", DeriveEdge(baseColor));
            mpb.SetColor("_Shadow_Color", DeriveShadow(baseColor));
            r.SetPropertyBlock(mpb);
        }
        
        public void ShowText()
        {
            Animate(Vector3.one * 0.02f, new Vector3(0f, 0f, 0.0049f));
            isTextVisible = true;
        }

        public void HideText()
        {
            Animate(Vector3.zero, Vector3.zero);
            isTextVisible = false;
        }

        void Animate(Vector3 scale, Vector3 position)
        {
            if (scaleRoutine != null)
                MelonCoroutines.Stop(scaleRoutine);

            scaleRoutine = MelonCoroutines.Start(
                Utilities.LerpValue(
                    () => titleText.transform.localScale,
                    v => titleText.transform.localScale = v,
                    Vector3.Lerp,
                    scale,
                    0.5f,
                    Utilities.EaseInOut,
                    isValid: () => titleText.transform != null
                )
            );

            if (positionRoutine != null)
                MelonCoroutines.Stop(positionRoutine);

            positionRoutine = MelonCoroutines.Start(
                Utilities.LerpValue(
                    () => titleText.transform.localPosition,
                    v => titleText.transform.localPosition = v,
                    Vector3.Lerp,
                    position,
                    0.5f,
                    Utilities.EaseInOut,
                    isValid: () => titleText.transform != null
                )
            );
        }

        public void Grab()
        {
            if (isGrabbed || isAnimation)
                return;

            isGrabbed = true;
            hasLeftTable = true;
            hasSavedAfterRelease = false;
            velocity = Vector3.zero;

            HideText();
        }

        public void Release()
        {
            if (!isGrabbed)
                return;

            isGrabbed = false;
        }
        
        void Update()
        {
            if (!isAnimation)
            {
                if (!isGrabbed)
                {
                    ApplyThrowVelocity();
                    HandleProximity();
                }
                else
                {
                    basePosition = transform.position;
                    velocity = (transform.position - lastPosition) / Time.deltaTime;
                    
                    Quaternion current = transform.rotation;
                    Quaternion delta = current * Quaternion.Inverse(lastRotation);

                    delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f)
                        angleDeg -= 360f;

                    angularVelocity = axis * (angleDeg * Mathf.Deg2Rad) / Time.deltaTime;

                    lastRotation = current;
                }

                lastPosition = transform.position;
            }
            else
            {
                HideText();
            }
        }
        

        void HandleProximity()
        {
            var head = Main.instance.head;
            if (head == null)
                return;

            float dist = Vector3.Distance(head.position, transform.position);

            const float showDist = 2f;
            const float hideDist = 2.2f;

            if (!isTextVisible && dist < showDist)
            {
                isTextVisible = true;
                ShowText();
            } else if (isTextVisible && dist > hideDist)
            {
                isTextVisible = false;
                HideText();
            }
        }

        void ApplyThrowVelocity()
        {
            if (velocity.sqrMagnitude < 0.001f && angularVelocity.sqrMagnitude < 0.001f)
            {
                if (!hasSavedAfterRelease)
                {
                    SaveCrystals();
                    hasSavedAfterRelease = true;
                }

                velocity = Vector3.zero;
                return;
            }

            basePosition += velocity * Time.deltaTime;
            velocity = Vector3.Lerp(velocity, Vector3.zero, 8f * Time.deltaTime);
            transform.position = basePosition;

            float angle = angularVelocity.magnitude * Time.deltaTime;
            if (angle > 0f)
                transform.rotation = Quaternion.AngleAxis(angle * Rad2Deg, angularVelocity.normalized) * transform.rotation;
            
            angularVelocity = Vector3.Lerp(angularVelocity, Vector3.zero, 8f * Time.deltaTime);

            if (angularVelocity.sqrMagnitude < 0.0005f)
            {
                float a = Quaternion.Angle(transform.rotation, Quaternion.Euler(-90f, 0, 0));
                float b = Quaternion.Angle(transform.rotation, Quaternion.Euler(90f, 0, 0));

                if (a < 5f || b < 5f)
                {
                    Quaternion target = a < b ? Quaternion.Euler(-90f, 0, 0) : Quaternion.Euler(90f, 0, 0);

                    transform.rotation = Quaternion.Slerp(transform.rotation, target, 8f * Time.deltaTime);
                }
            }
        }

        static Color DeriveEdge(Color baseColor)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Clamp01(s + 0.05f), Clamp01(v + 0.35f));
        }

        static Color DeriveShadow(Color baseColor)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Clamp01(s - 0.25f), Clamp01(v - 0.4f));
        }
    }

    [Serializable]
    public struct CrystalState
    {
        public string ReplayPath;
        public string Title;

        public float x;
        public float y;
        public float z;
        
        public Color32 BaseColor;
    }
}

// Voice recording - UNUSED
// Not enabled due to Il2CPP codec limitaitons and privacy concerns
// Kept for future ideas
internal static class ReplayVoices
{
    // Remote
    private static PunVoiceClient voice;
    
    private static readonly Dictionary<(int playerId, int voiceId), VoiceStreamWriter> writers = new();
    public static string tempVoiceDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ReplayMod", "TempVoices");
    
    public static List<VoiceTrackInfo> voiceTrackInfos = new();

    public static void HookRemote()
    {
        voice ??= PunVoiceClient.Instance;
    
        voice.RemoteVoiceAdded += (Il2CppSystem.Action<RemoteVoiceLink>)(OnRemoteVoiceAdded);
    
        Directory.CreateDirectory(tempVoiceDir);
    }
    
    public static void OnRemoteVoiceAdded(RemoteVoiceLink link)
    {
        int playerId = link.PlayerId;
        int voiceId = link.VoiceId;

        string name = PlayerManager.instance.AllPlayers.ToArray()
                .FirstOrDefault(p => p.Data.GeneralData.ActorNo == playerId)?
                .Data.GeneralData.PublicUsername
            ?? $"Unknown";

        name = Utilities.CleanName(name);

        string fileName = $"{name}_actor_{playerId}_voice_{voiceId}.ogg";
        
        voiceTrackInfos.Add(new VoiceTrackInfo
        {
            ActorId = playerId,
            FileName = fileName,
            StartTime = Time.time
        });

        string path = Path.Combine(
            tempVoiceDir,
            fileName
        );

        var writer = new VoiceStreamWriter(
            playerId,
            link.VoiceInfo.SamplingRate,
            link.VoiceInfo.Channels,
            path
        );

        var key = (playerId, voiceId);
        writers[key] = writer;

        link.FloatFrameDecoded += (Il2CppSystem.Action<FrameOut<float>>)((FrameOut<float> frame) =>
        {
            if (!writers.TryGetValue(key, out var w))
                return;

            w.Write(frame.Buf);

            if (frame.EndOfStream)
                StopWriter(key);
        });

        link.RemoteVoiceRemoved += (Il2CppSystem.Action)(() =>
        {
            StopWriter(key);
        });
    }

    private static void StopWriter((int playerId, int voiceId) key)
    {
        if (!writers.Remove(key, out var writer))
            return;

        writer.Dispose();

        if (!writer.HasFrames && File.Exists(writer.Path))
            File.Delete(writer.Path);
    }

    public class VoiceStreamWriter
    {
        public readonly int ActorId;
        public readonly string Path;
        public bool HasFrames { get; private set; }

        private readonly FileStream file;
        // private readonly OpusOggWriteStream ogg;

        public VoiceStreamWriter(
            int actorId,
            int sampleRate,
            int channels,
            string path
        )
        {
            ActorId = actorId;
            Path = path;

            file = File.Create(path);
            
            // Causes system.runtime error
            // var encoder = new OpusEncoder(
            //     sampleRate,
            //     channels,
            //     OpusApplication.OPUS_APPLICATION_VOIP
            // );
            
            // encoder.Bitrate = 24000;
            // encoder.UseVBR = true;
            // encoder.UseDTX = true;
            // encoder.Complexity = 6;
            //
            // ogg = new OpusOggWriteStream(encoder, file);
        }

        public void Write(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return;

            HasFrames = true;
            // ogg.WriteSamples(samples, 0, samples.Length);
        }

        public void Dispose()
        {
            // ogg.Finish();
            file?.Dispose();
        }
    }
}