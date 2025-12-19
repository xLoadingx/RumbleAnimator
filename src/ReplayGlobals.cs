using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Il2CppPhoton.Voice;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Pools;
using Il2CppSystem.Text;
using Il2CppSystem.Text.RegularExpressions;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using Il2CppPhoton.Voice.PUN;
using Il2CppPhoton.Voice.Unity;
using NAudio.Wave;
using UnityEngine;
using static UnityEngine.Mathf;

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
            
            string opponent = replayInfo.Header.Players.FirstOrDefault(p => p.MasterId != localPlayer.Data.GeneralData.PlayFabMasterId).Name;
            string opponentName = !string.IsNullOrEmpty(opponent)
                ? CleanName(opponent)
                : "Unknown";

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            
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

        public static IEnumerator LoadMap(int index, float fadeDuration = 2f, Action onLoaded = null)
        {
            foreach (var structure in CombatManager.instance.structures.ToArray())
                structure.Kill(Vector3.zero, false, false);
            
            SceneManager.instance.LoadSceneAsync(index, false, false, fadeDuration);

            while (SceneManager.instance.IsLoadingScene)
                yield return null;

            yield return new WaitForSeconds(0.1f);
            MelonLogger.Msg("Loaded");
            onLoaded?.Invoke();
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
            Action done = null
        )
        {
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
        
        public static FileSystemWatcher replayWatcher;
        public static bool reloadQueued;
        public static bool suppressWatcher;

        public static string currentReplayPath = null;
        public static ReplaySerializer.ReplayHeader currentHeader = null;

        private static Dictionary<string, ReplaySerializer.ReplayHeader> manifestCache = new();

        public static void Init()
        {
            Directory.CreateDirectory(replayFolder);
            LoadReplays();
            
            Task.Run(() =>
            {
                foreach (var path in replayPaths)
                    ReplaySerializer.GetManifest(path);
            });
            
            StartWatchingReplays();
        }

        public static ReplaySerializer.ReplayHeader GetCachedManifest(string path)
        {
            if (!manifestCache.TryGetValue(path, out var header))
            {
                header = ReplaySerializer.GetManifest(path);
                manifestCache[path] = header;
            }

            if (string.IsNullOrEmpty(header.Title))
                header.Title = ReplaySerializer.BuildTitle(header);
            
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
        }

        static string BuildPlayerLine(PlayerInfo[] players)
        {
            if (players == null || players.Length <= 2)
                return string.Empty;

            const int maxNames = 3;

            int count = players.Length;
            int shown = Math.Min(count, maxNames);

            var names = new List<string>(shown);
            for (int i = 0; i < shown; i++)
                names.Add($"{players[i].Name}<#FFF>");

            string line = string.Join(", ", names);

            if (count > maxNames)
                line += $" +{count - maxNames} others";

            return
                $"{count} players\n" +
                $"{line}\n";
        }

        public static TimeSpan GetDuration(ReplaySerializer.ReplayHeader header)
        {
            if (header.FPS <= 0)
                return TimeSpan.Zero;

            double seconds = (double)header.FrameCount / header.FPS;
            return TimeSpan.FromSeconds(seconds);
        }
        
        public static void SelectReplay(int index)
        {
            if (table.replayNameText == null) return;
            
            if (index < 0 || index >= replayPaths.Count)
            {
                currentIndex = -1;
                currentReplayPath = null;
                table.replayNameText.text = "No Replay Selected";
                table.replayNameText.text = "No Replay Selected";
                HideMetadata();
            }
            else
            {
                currentIndex = index;
                currentReplayPath = replayPaths[index];

                table.indexText.text = $"({currentIndex + 1} / {replayPaths.Count})";

                try
                {
                    var header = GetCachedManifest(currentReplayPath);
                    currentHeader = header;
                    table.replayNameText.text = string.IsNullOrWhiteSpace(header.Title)
                        ? Path.GetFileNameWithoutExtension(currentReplayPath)
                        : header.Title;

                    var duration = GetDuration(header);
                    table.metadataText.text = 
                        $"{header.Title}\n" +
                        $"{header.DateUTC}\n" +
                        $"Version {header.Version}\n\n" +
                        $"{(string.IsNullOrEmpty(header.CustomMap) ? Utilities.GetFriendlySceneName(header.Scene) : header.CustomMap)}\n" +
                        $"{BuildPlayerLine(header.Players)}\n" +
                        $"FPS: {header.FPS}\n" +
                        $"Frames: {header.FrameCount}\n" +
                        $"Duration: {duration.Minutes}:{duration.Seconds:D2}\n\n" +
                        $"{header.Structures.Length} structure{(header.Structures.Length > 1 ? "s" : "")}";
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

            int nextIndex = currentIndex == -1
                ? 0
                : (currentIndex + 1) % replayPaths.Count;

            SelectReplay(nextIndex);
        }

        public static void PreviousReplay()
        {
            if (replayPaths.Count == 0) return;

            int previousIndex = currentIndex == -1
                ? replayPaths.Count - 1
                : (currentIndex -1 + replayPaths.Count) % replayPaths.Count;
            
            SelectReplay(previousIndex);
        }

        public static void LoadReplays()
        {
            replayPaths = Directory
                .GetFiles(replayFolder, "*.replay")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            currentIndex = Clamp(currentIndex, -1, replayPaths.Count - 1);
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

    internal static class ReplayVoices
    {
        private static readonly Dictionary<int, VoiceRecorder> active = new();
        public static bool hooked = false;
        
        private static void Log(string msg)
        {
            Main.instance.LoggerInstance.Msg($"[ReplayVoices] {msg}");
        }

        public static void Hook()
        {
            if (hooked)
                return;

            hooked = true;
            Log("Hooked into PunVoiceClient.RemoteVoiceAdded");

            PunVoiceClient.instance.RemoteVoiceAdded += (Il2CppSystem.Action<RemoteVoiceLink>)((RemoteVoiceLink link) =>
            {
                Log($"RemoteVoiceAdded | PlayerId={link.PlayerId} VoiceId={link.VoiceId}");
                
                if (Main.instance.isRecording)
                    StartRecording(link, PlayerManager.instance.AllPlayers.ToArray().FirstOrDefault(p => p.Data.GeneralData.ActorNo == link.PlayerId)?.Data.GeneralData.PublicUsername);

                link.RemoteVoiceRemoved += (Il2CppSystem.Action)(() =>
                {
                    Log($"RemoteVoiceRemoved | PlayerId={link.PlayerId}");
                    StopRecording(link);
                });
            });
        }

        public static void StartRecording(RemoteVoiceLink link, string playerName)
        {
            if (active.ContainsKey(link.PlayerId))
            {
                Log($"Already recording PlayerId={link.PlayerId}");
                return;
            }

            if (string.IsNullOrEmpty(playerName))
                playerName = "Unknown";
            
            Log($"StartRecording | PlayerId={link.PlayerId} Name={playerName}");

            var recorder = new VoiceRecorder(link, playerName);
            active.Add(link.PlayerId, recorder);
            recorder.Start();
        }
        
        public static void StopRecording(RemoteVoiceLink link)
        {
            if (!active.TryGetValue(link.PlayerId, out var recorder))
            {
                Log($"StopRecording called but no active recorder | PlayerId={link.PlayerId}");
                return;
            }
            
            Log($"StopRecording | PlayerId={link.PlayerId}");

            recorder.Stop();
            active.Remove(link.PlayerId);
        }

        internal class VoiceRecorder
        {
            private readonly RemoteVoiceLink link;
            private readonly string playerName;

            private int frameCount;

            private readonly int sampleRate;
            private readonly int channels;
            
            private WaveFileWriter writer;
            private bool initialized;

            public VoiceRecorder(RemoteVoiceLink link, string playerName)
            {
                this.link = link;
                this.playerName = Utilities.CleanName(playerName);
                sampleRate = link.VoiceInfo.SamplingRate;
                channels = link.VoiceInfo.Channels;
            }

            public void Start()
            {
                Log($"Recorder start | {playerName} SR={sampleRate} C={channels}");
                link.FloatFrameDecoded += (Il2CppSystem.Action<FrameOut<float>>)OnFrameDecoded;
            }

            public void Stop()
            {
                Log($"Recorder stop | {playerName} Frames={frameCount}");
                link.FloatFrameDecoded -= (Il2CppSystem.Action<FrameOut<float>>)OnFrameDecoded;
                writer?.Dispose();
                writer = null;
            }

            private void OnFrameDecoded(FrameOut<float> frame)
            {
                if (!initialized)
                    InitWriter(frame);

                frameCount++;

                if (frameCount == 1)
                    Log($"First frame | {playerName} Samples={frame.Buf.Length}");

                writer.WriteSamples(frame.Buf.ToArray(), 0, frame.Buf.Length);
            }

            private void InitWriter(FrameOut<float> frame)
            {
                Directory.CreateDirectory(ReplayFiles.currentReplayPath);

                var path = Path.Combine(ReplayFiles.currentReplayPath, $"{playerName}.wav");

                Log($"Creating WAV | {path}");
                
                var format = WaveFormat.CreateIeeeFloatWaveFormat(
                    sampleRate,
                    channels
                );

                writer = new WaveFileWriter(path, format);
                initialized = true;
            }
        }
    }
}