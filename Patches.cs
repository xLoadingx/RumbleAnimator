using System.Collections;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Pools;
using MelonLoader;
using RumbleAnimator.Recording;
using RumbleAnimator.Utils;
using UnityEngine;
using UnityEngine.Events;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace RumbleAnimator;
using HarmonyLib;

public class Patches
{
    public static string lastStackCaller = null;
    public static float lastStackTimestamp = -1;
    
    [HarmonyPatch(typeof(PlayerStackProcessor), nameof(PlayerStackProcessor.Execute))]
    public static class Patch_StackExecute
    {
        public static void Postfix(PlayerStackProcessor __instance, Stack stack, StackConfiguration overrideConfig)
        {
            var player = __instance?.GetComponent<PlayerController>();
            if (!player)
            {
                MelonLogger.Msg("[StackExecute] No player found on processor.");
                return;
            }

            string masterID = player.assignedPlayer.Data.GeneralData.PlayFabMasterId;
            lastStackCaller = masterID;

            float timestamp = Time.time - Main.recordingStartTime;
            string stackName = stack?.CachedName ?? "null";

            MelonLogger.Msg($"[StackExecute] Stack: {stackName}, Time: {timestamp:F3}");

            bool shouldRecord = Main.isRecording &&
                                lastStackTimestamp != timestamp;

            MelonLogger.Msg($"[StackExecute] Conditions - Recording: {Main.isRecording}, " +
                            $"TimeCheck: {lastStackTimestamp != timestamp}");

            if (shouldRecord)
            {
                lastStackTimestamp = timestamp;

                var stackEvent = new StackEvent
                {
                    timestamp = timestamp,
                    stack = stackName
                };

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                Codec.EncodeStackEvent(bw, masterID, stackEvent);
                ReplayFile.WriteFramedData(ms.ToArray(), Main.currentRecordingFrame, FrameType.StackEvent);

                MelonLogger.Msg($"[StackExecute] RECORDED StackEvent: {stackName} at {timestamp:F3} seconds (Frame {Main.currentRecordingFrame})");
            }
            else
            {
                MelonLogger.Msg($"[StackExecute] Skipped recording stack '{stackName}' at {timestamp:F3}");
            }
        }
    }

    [HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Instantiate))]
    public class Patch_PoolManager_Instantiate
    {
        static void Postfix(GameObject __result)
        {
            MelonCoroutines.Start(WaitForLastStackCaller(__result));
        }

        public static IEnumerator WaitForLastStackCaller(GameObject __result)
        {
            if (!(Main.isRecording || Main.isPlaying))
                yield break;

            while (lastStackCaller is null)
                yield return null;

            if (__result?.GetComponent<Structure>() is null) yield break;

            string masterID = lastStackCaller;
            Main.players.TryGetValue(masterID, out var state);

            bool isFromClone = Main.isPlaying &&
                state != null &&
                state?.Clone?.RootObject.GetComponent<ReplayClone>() != null;
            
            if (!Main.structures.Contains(__result) && (Main.isRecording || isFromClone))
            {
                Main.structures.Add(__result);

                if (Main.isRecording)
                {
                    var pooled = __result.GetComponent<PooledMonoBehaviour>();
                    if (pooled is not null)
                    {
                        var replayData = new StructureReplayData
                        {
                            type = pooled.resourceName,
                            frames = new List<StructureFrame>()
                        };

                        Main.structureDataList.Add(replayData);

                        var data = Codec.EncodeStructureData(pooled.resourceName, false, Main.structureDataList.Count);
                        ReplayFile.WriteFramedData(data, Main.currentRecordingFrame, FrameType.StructureData);
                    }
                }

                if (isFromClone)
                {
                    __result.GetComponent<Rigidbody>().isKinematic = true;
                }
            }
        }
    }
}