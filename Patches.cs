using System.Collections;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using MelonLoader;
using RumbleAnimator.Recording;
using RumbleAnimator.Utils;
using UnityEngine;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace RumbleAnimator;
using HarmonyLib;

public class Patches
{
    public static string lastStackCaller = null;
    public static float lastStackTimestamp = -1;
    public static string lastStackExecuted;
    
    [HarmonyPatch(typeof(PlayerStackProcessor), nameof(PlayerStackProcessor.Execute))]
    public static class Patch_StackExecute
    {
        public static void Postfix(PlayerStackProcessor __instance, Stack stack, StackConfiguration overrideConfig)
        {
            var player = __instance?.GetComponent<PlayerController>();
            if (!player) return;

            string masterID = player.assignedPlayer.Data.GeneralData.PlayFabMasterId;
            lastStackCaller = masterID;

            var timestamp = Time.time - Main.recordingStartTime;
            if (Main.isRecording && !__instance.GetComponent<ReplayClone>() && lastStackTimestamp != Time.time - Main.recordingStartTime && lastStackExecuted != stack?.CachedName)
            {
                lastStackTimestamp = timestamp;
                lastStackExecuted = stack.CachedName;
                
                var stackEvent = new StackEvent
                {
                    timestamp = timestamp,
                    stack = stack?.CachedName
                };

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                Codec.EncodeStackEvent(bw, masterID, stackEvent);
                ReplayFile.WriteFramedData(ms.ToArray(), Main.currentRecordingFrame, FrameType.StackEvent);
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

                if (isFromClone)
                {
                    __result.GetComponent<Rigidbody>().isKinematic = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Structure), nameof(Structure.Kill))]
    public class Patch_Structure_Kill
    {
        public static void Prefix(Structure __instance)
        {
            if (!Main.isRecording)
                return;
            
            GameObject go = __instance.gameObject;

            int index = Main.structures.IndexOf(go);
            if (index is -1 || index >= Main.structureDataList.Count)
                return;

            var replayData = Main.structureDataList[index];
            if (replayData.destroyedAtFrame == null)
            {
                replayData.destroyedAtFrame = Main.currentRecordingFrame;
                MelonLogger.Msg($"[Replay] Structure {__instance.name} destroyed at frame {Main.currentRecordingFrame}");
                
                var data = Codec.EncodeStructureDestroyed(index, Main.currentRecordingFrame);
                ReplayFile.WriteFramedData(data, Main.currentRecordingFrame, FrameType.StructureDestroyed);
            }
        }
    }
}