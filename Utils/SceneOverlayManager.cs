using System.Collections;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumbleAnimator.Utils;

public class SceneOverlayManager
{
    private static Scene additiveScene;
    private static GameObject customMap;
    private static List<GameObject> hiddenParkObjects = new();
    
    public static IEnumerator LoadSceneForReplay(string sceneName, Vector3 safePosition, Action onLoaded = null)
    {
        HideParkVisuals();

        var loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (!loadOp.isDone) yield return null;

        additiveScene = SceneManager.GetSceneByName(sceneName);
        
        foreach (var player in Calls.Players.GetAllPlayers())
            Utilities.TeleportPlayer(player, safePosition);

        onLoaded?.Invoke();
    }

    public static void LoadCustomMap(string mapName, Vector3 safePosition)
    {
        HideParkVisuals();
        
        var root = GameObject.Find("CustomMultiplayerMaps");
        if (root is null)
        {
            MelonLogger.Msg($"[Replay] Replay was recorded with a custom map but custom maps aren't installed!");
            return;
        }

        for (int i = 0; i < root.transform.childCount; i++)
        {
            var child = root.transform.GetChild(i);
            if (child.name == mapName)
            {
                child.gameObject.SetActive(true);
                customMap = child.gameObject;
                break;
            }
        }
        
        foreach (var player in Calls.Players.GetAllPlayers())
            Utilities.TeleportPlayer(player, safePosition);
    }

    public static IEnumerator UnloadReplayScene()
    {
        if (additiveScene.IsValid())
        {
            var unloadOp = SceneManager.UnloadSceneAsync(additiveScene);
            while (!unloadOp.isDone) yield return null;
        }

        customMap?.SetActive(false);

        ShowParkVisuals();
    }

    private static void HideParkVisuals()
    {
        var park = SceneManager.GetSceneByName("Park");
        foreach (var go in park.GetRootGameObjects())
        {
            if (!go.name.Contains("Player"))
            {
                go.SetActive(false);
                hiddenParkObjects.Add(go);
            }
        }
    }

    private static void ShowParkVisuals()
    {
        foreach (var go in hiddenParkObjects)
            if (go != null) go.SetActive(true);

        hiddenParkObjects.Clear();
    }
}