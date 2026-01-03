using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
// using Concentus;
// using Concentus.Enums;
// using Concentus.Oggfile;
// using Concentus.Structs;
using Il2CppPhoton.Voice;
using Il2CppPhoton.Voice.PUN;
using Il2CppPhoton.Voice.Unity;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Environment.MatchFlow;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Pools;
using Il2CppSystem.Text;
using Il2CppSystem.Text.RegularExpressions;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleModdingAPI;
using UnityEngine;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using Random = UnityEngine.Random;

namespace RumbleAnimator;

public class ReplayGlobals
{
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
                else if (name.Contains("BoulderBall")) structurePools[StructureType.CagedBall] = pool;
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
            
            string opponent = replayInfo.Header.Players.FirstOrDefault(p => p.MasterId != localPlayer.Data.GeneralData.PlayFabMasterId)?.Name;
            string opponentName = !string.IsNullOrEmpty(opponent)
                ? CleanName(opponent)
                : "Unknown";

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            
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

        public static T[] NewArray<T>(int count, T[] copyFrom = null) where T : new()
        {
            var arr = new T[count];
            for (int i = 0; i < count; i++)
                arr[i] = copyFrom != null && i < copyFrom.Length
                    ? copyFrom[i]
                    : new T();
            return arr;
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

        public static IEnumerator LoadMap(int index, float fadeDuration = 2f, Action onLoaded = null)
        {
            foreach (var structure in CombatManager.instance.structures.ToArray())
                structure?.Kill(Vector3.zero, false, false);
            
            SceneManager.instance.LoadSceneAsync(index, false, false, fadeDuration);

            while (SceneManager.instance.IsLoadingScene)
                yield return null;

            yield return new WaitForSeconds(0.1f);
            MelonLogger.Msg("Loaded");
            onLoaded?.Invoke();
        }

        public static Color32 RandomColor()
        {
            float h = Random.value;
            float s = Random.Range(0.6f, 0.9f);
            float v = Random.Range(0.7f, 1.0f);
            
            Color c = Color.HSVToRGB(h, s, v);
            return c;
        }

        public static float EaseInOut(float t)
        {
            return t < 0.5f ? 2 * t * t : 1 - Pow(-2 * t + 2, 2) / 2;
        }
        
        public static IEnumerator LerpValue<T>(
            Func<T> getter,
            Action<T> setter,
            Func<T, T, float, T> lerpFunc,
            T targetValue,
            float duration,
            Func<float, float> easing = null,
            Action done = null,
            float delay = 0f
        )
        {
            yield return new WaitForSeconds(delay);
            
            T startValue = getter();
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                float easedT = easing?.Invoke(Clamp01(t)) ?? t;
                setter(lerpFunc(startValue, targetValue, easedT));
                yield return null;
            }

            setter(targetValue);
            done?.Invoke();
        }
    }

    public static class ReplayFiles
    {
        public static string replayFolder = $"{MelonEnvironment.UserDataDirectory}/MatchReplays";
        
        public static List<string> replayPaths = new();
        public static int currentIndex = -1;
        public static ReplayTable table;
        public static bool metadataLerping = false;
        public static bool metadataHidden = false;
        
        public static FileSystemWatcher replayWatcher;
        public static bool reloadQueued;
        public static bool suppressWatcher;

        public static string currentReplayPath = null;
        public static ReplaySerializer.ReplayHeader currentHeader = null;

        private static Dictionary<string, ReplaySerializer.ReplayHeader> manifestCache = new();

        public static void Init()
        {
            Directory.CreateDirectory(replayFolder);
            
            Task.Run(() =>
            {
                foreach (var path in replayPaths)
                    ReplaySerializer.GetManifest(path);
            });
            
            StartWatchingReplays();
            EnsureDefaultFormats();
        }

        public static void EnsureDefaultFormats()
        {
            void WriteIfNotExists(string filePath, string contents)
            {
                string path = Path.Combine(replayFolder, filePath);
                if (!File.Exists(path))
                    File.WriteAllText(path, contents);
            }

            string metadataFormatsFolder = Path.Combine(replayFolder, "MetadataFormats");
            Directory.CreateDirectory(metadataFormatsFolder);
            
            string autoNameFormatsFolder = Path.Combine(replayFolder, "AutoNameFormats");
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
                "{Version}\n" +
                "{StructureCount}\n" +
                "{Duration}\n" +
                "{FPS} - Target FPS of the recording\n" +
                "\n" +
                "You can pass parameters to tags using ':'.\n" +
                "Example: {PlayerList:3}, {DateTime:yyyyMMdd}\n\n" +
                "###\n";
            
            WriteIfNotExists("MetadataFormats/metadata_gym.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\nDuration: {Duration}\n\n{StructureCount}");
            WriteIfNotExists("MetadataFormats/metadata_park.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\n\n{Scene}\nHost: {Host}\n{PlayerList:3}\nDuration: {Duration}\n\n{StructureCount}");
            WriteIfNotExists("MetadataFormats/metadata_match.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\n\n{Scene}\nHost: {Host}\nDuration: {Duration}\n\n{StructureCount}");
            
            WriteIfNotExists("AutoNameFormats/gym.txt", TagHelpText + "{LocalPlayer} - {Scene}");
            WriteIfNotExists("AutoNameFormats/park.txt", TagHelpText + "{Host} - {Scene}\n<scale=85%>{PlayerCount}</scale>");
            WriteIfNotExists("AutoNameFormats/match.txt", TagHelpText + "{Host} vs {Client} - {Scene}");
        }

        public static string LoadFormatFile(string path)
        {
            string fullPath = Path.Combine(replayFolder, path + ".txt");
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

        public static ReplaySerializer.ReplayHeader GetCachedManifest(string path)
        {
            if (!manifestCache.TryGetValue(path, out var header))
            {
                header = ReplaySerializer.GetManifest(path);
                manifestCache[path] = header;
            }

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

        static void StartWatchingReplays()
        {
            replayWatcher = new FileSystemWatcher(replayFolder, "*.replay");

            replayWatcher.NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.CreationTime;

            replayWatcher.Created += OnReplayFolderChanged;
            replayWatcher.Deleted += OnReplayFolderChanged;
            replayWatcher.Renamed += OnReplayFolderChanged;
            
            replayWatcher.EnableRaisingEvents = true;
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
            }
            else
            {
                currentIndex = index;
                currentReplayPath = replayPaths[index];

                int shownIndex = currentIndex < 0 ? 0 : currentIndex + 1;
                table.indexText.text = $"({shownIndex} / {replayPaths.Count})";

                try
                {
                    var header = GetCachedManifest(currentReplayPath);
                    currentHeader = header;
                    table.replayNameText.text = string.IsNullOrWhiteSpace(header.Title)
                        ? Path.GetFileNameWithoutExtension(currentReplayPath)
                        : header.Title;

                    var format = GetMetadataFormat(header.Scene);
                    table.metadataText.text = ReplaySerializer.FormatReplayString(format, header);
                    ShowMetadata();
                }
                catch
                {
                    table.replayNameText.text = "Invalid Replay";
                    table.indexText.text = "Invalid Replay";
                    HideMetadata();
                    currentReplayPath = null;
                    currentIndex = -1;
                }
            }
            
            table.replayNameText.ForceMeshUpdate();
            table.indexText.ForceMeshUpdate();
            table.metadataText.ForceMeshUpdate();
            ApplyTMPSettings(table.replayNameText, 5f, 0.51f);
            ApplyTMPSettings(table.indexText, 5f, 0.51f);
            ApplyTMPSettings(table.metadataText, 15f, 2f);
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
                .GetFiles(replayFolder, "*.replay")
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
                int newIndex = replayPaths.IndexOf(currentReplayPath);
                currentIndex = newIndex >= 0 ? newIndex : -1;
            }
            else
            {
                currentIndex = -1;
            }

            SelectReplay(currentIndex);
        }

        static void ApplyTMPSettings(TextMeshPro text, float horizontal, float vertical)
        {
            if (text == null) return;
            
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            var rect = text.GetComponent<RectTransform>();
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, horizontal);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, vertical);

            text.fontSizeMin = 0.1f;
            text.fontSizeMax = 10f;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
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
        
        public static void LoadCrystals()
        {
            string path = Path.Combine(
                MelonEnvironment.UserDataDirectory,
                "MatchReplays",
                "replayCrystals.json"
            );

            CrystalState[] states = null;

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                states = JsonConvert.DeserializeObject<CrystalState[]>(json);
            }

            if (states != null)
            {
                Crystals = new();

                foreach (var state in states)
                    CreateCrystal().RestoreState(state);
            }
        }

        public static void SaveCrystals()
        {
            if (Crystals == null)
                return;
            
            var states = new CrystalState[Crystals.Count];

            for (int i = 0; i < Crystals.Count; i++)
            {
                var crystal = Crystals[i];
                if (crystal == null)
                    continue;
                
                states[i] = crystal.CaptureState();
            }

            string json = JsonConvert.SerializeObject(
                states,
                Formatting.Indented
            );

            string path = Path.Combine(
                MelonEnvironment.UserDataDirectory,
                "MatchReplays",
                "replayCrystals.json"
            );
            
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
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

        public static Crystal CreateCrystal(Vector3 position, ReplaySerializer.ReplayHeader header, bool useAnimation = false, bool applyRandomColor = false)
        {
            if (crystalParent == null)
                crystalParent = new GameObject("Crystals");
            
            Crystal crystal = GameObject.Instantiate(crystalPrefab, crystalParent.transform).AddComponent<Crystal>();
                
            crystal.name = $"Crystal ({header.Title}, {header.Date})";
            crystal.transform.position = position;
            crystal.Title = header.Title;

            GameObject text = Calls.Create.NewText(header.Title, 1f, Color.white, Vector3.zero, Quaternion.identity);

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

        public static IEnumerator CrystalSpawnAnimation(Crystal crystal)
        {
            crystal.isAnimation = true;
            
            crystal.transform.localScale = Vector3.zero;
            crystal.transform.position = Main.instance.replayTable.transform.position;

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
            private bool hasSavedAfterRelease;

            private object scaleRoutine;
            private object positionRoutine;
            private bool isTextVisible;

            private void Awake()
            {
                basePosition = transform.position;
                lastPosition = transform.position;
            }

            public void ApplyVisuals()
            {
                Color baseColor = BaseColor;

                rend ??= GetComponent<Renderer>();
                mpb ??= new MaterialPropertyBlock();
                
                rend.GetPropertyBlock(mpb);
                
                mpb.SetColor("_Base_Color", baseColor);
                mpb.SetColor("_Edge_Color", DeriveEdge(baseColor));
                mpb.SetColor("_Shadow_Color", DeriveShadow(baseColor));

                rend.SetPropertyBlock(mpb);
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
                        Utilities.EaseInOut
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
                        Utilities.EaseInOut
                    )
                );
            }

            public void Grab()
            {
                if (isGrabbed)
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
                        LerpUpright();
                        HandleProximity();
                    }
                    else
                    {
                        basePosition = transform.position;
                        velocity = (transform.position - lastPosition) / Time.deltaTime;
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
                if (velocity.sqrMagnitude < 0.001f)
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
            }

            void LerpUpright()
            {
                Quaternion target = Quaternion.Euler(-90f, 0f, 0f);

                float smooth = 1f - Exp(-6f * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    target,
                    smooth
                );
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
        public static string tempVoiceDir = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays", "TempVoices");
        
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
                StartTime = Main.elapsedRecordingTime
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
}