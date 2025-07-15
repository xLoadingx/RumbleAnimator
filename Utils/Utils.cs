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

    public static string FixColorTags(string input)
    {
        if (input is null)
            return null;
        
        var output = new StringBuilder();
        bool firstTag = true;

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '<' && i + 1 < input.Length && input[i + 1] == '#')
            {
                if (!firstTag)
                    output.Append("</color>");
                else
                    firstTag = false;
            }

            output.Append(input[i]);
        }

        output.Append("</color>");
        return output.ToString();
    }

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

    public enum EaseType
    {
        None,
        EaseIn,
        EaseOut,
        EaseInOut
    }

    public static float ApplyEase(float t, EaseType type)
    {
        return type switch
        {
            EaseType.None => t,
            EaseType.EaseIn => Pow(t, 3),
            EaseType.EaseOut => 1f - Pow(1f - t, 3),
            EaseType.EaseInOut => 0.5f * (1f - Cos(t * PI)),
            _ => t
        };
    }

    public static IEnumerator EaseLerp<T>(
        T from,
        T to,
        float duration,
        Func<T, T, float, T> lerpFunc,
        Action<T, float> onUpdate,
        Action onComplete = null,
        EaseType ease = EaseType.EaseOut)
    {
        float time = 0f;
        float t = 0f;
        while (time < duration)
        {
            t = time / duration;
            float easedT = ApplyEase(t, ease);
            onUpdate(lerpFunc(from, to, easedT), t);
            time += Time.deltaTime;
            yield return null;
        }

        onUpdate(to, t);
        onComplete?.Invoke();
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