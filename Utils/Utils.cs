using System.Collections;
using Il2CppSystem.Text;
using MelonLoader;
using NAudio.Wave;
using UnityEngine;
using Object = UnityEngine.Object;
using static UnityEngine.Mathf;

namespace RumbleAnimator.Utils;

public static class Utilities
{
    public static Dictionary<string, string> SceneNameMap { get; } = new()
    {
        { "Gym", "Gym" },
        { "Park", "Park" },
        { "Map0", "Ring" },
        { "Ring", "Ring" },
        { "Map1", "Pit" },
        { "Pit", "Pit" }
    };

    public static string TryGetActiveCustomMap()
    {
        var root = GameObject.Find("CustomMultiplayerMaps");
        if (root is null) return null;

        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);

            if (child.gameObject.activeInHierarchy)
                return child.name;
        }

        return null;
    }

    public static IEnumerator EaseLerp<T>(
        T from,
        T to,
        float duration,
        Func<T, T, float, T> lerpFunc,
        Action<T> onUpdate)
    {
        float time = 0f;
        while (time < duration)
        {
            float t = time / duration;
            float easedT = 0.5f * (1f - Cos(t * PI));
            onUpdate(lerpFunc(from, to, easedT));
            time += Time.deltaTime;
            yield return null;
        }

        onUpdate(to);
    }

    public static IEnumerator PlaySound(string name)
    {
        using (var stream = Main.ModAssembly.GetManifestResourceStream($"RumbleAnimator.Audio.{name}"))
        {
            if (stream is not null)
            {
                var reader = new WaveFileReader(stream);
                var waveOut = new WaveOutEvent();
                waveOut.DeviceNumber = 0;
                waveOut.Init(reader);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    yield return null;
                }

                waveOut.Dispose();
                reader.Dispose();
            }
        }
        
    }

    public static T LoadObjectFromBundle<T>(string bundleName, string name, bool shouldInstantiate = true) where T : Object
    {
        using var stream = Main.ModAssembly.GetManifestResourceStream($"RumbleAnimator.AssetBundles.{bundleName}");

        if (stream is null)
        {
            MelonLogger.Error($"Could not find AssetBundle with name '{bundleName}' in Embedded Resources");
            return null;
        }
        
        byte[] bundleBytes = new byte[stream.Length];
        stream.Read(bundleBytes, 0, bundleBytes.Length);

        var bundle = Il2CppAssetBundleManager.LoadFromMemory(bundleBytes);
        T obj = bundle.LoadAsset<T>(name);
        bundle.Unload(false);

        if (obj is null)
        {
            MelonLogger.Error($"AssetBundle was loaded correctly, but the object '{name}' could not be found.");
            return null;
        }

        return shouldInstantiate ? GameObject.Instantiate(obj) : obj;
    }

    public static string GetFriendlySceneName(string scene = null)
    {
        string sceneKey = (scene ?? Main.currentScene)?.Trim();
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