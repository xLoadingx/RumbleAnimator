using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppSystem.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using MethodBase = System.Reflection.MethodBase;
using SceneManager = Il2CppRUMBLE.Managers.SceneManager;

namespace ReplayMod;

public class Patches
{
    public static List<(string playerId, short stackId)> activations = new();

    public static Dictionary<string, Queue<(float time, int damage)>> damageQueues = new();
    public static Dictionary<string, float> lastLargeDamageTime = new();
    
    public static int GetPlayerIndex(Player player) => Main.instance.RecordedPlayers.IndexOf(player);
    
    // ----- Pools -----
    [HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Instantiate))]
    public class Patch_PoolManager_Instantiate
    {
        static void Postfix(GameObject __result)
        {
            if (!Main.isRecording && !Main.isBuffering)
                return;

            var structure = __result.GetComponent<Structure>();
            Main.instance.TryRegisterStructure(structure);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play))]
    [HarmonyPatch(new[] { typeof(AudioCall), typeof(Vector3), typeof(bool) })]
    public class Patch_AudioManager_Play
    {
        static void Postfix(Vector3 position, AudioCall call)
        {
            if (call == null)
                return;

            FXOneShotType? type = ReplayCache.AudioCallToFX.TryGetValue(call.name, out var foundType) ? foundType : null;

            if (type.HasValue)
            {
                Main.instance.Events.Add(new EventChunk
                {
                    type = EventType.OneShotFX,
                    fxType = type.Value,
                    position = position
                });
            }
        }
    }

    [HarmonyPatch(typeof(Pool<PooledMonoBehaviour>), nameof(Pool<PooledMonoBehaviour>.FetchFromPool))]
    [HarmonyPatch(new Type[]
        { })]
    public class Patch_PoolMonoBehavior_FetchFromPoolNoParameters
    {
        static void Postfix(PooledMonoBehaviour __result)
        {
            var vfx = __result.GetComponent<VisualEffect>(); 
            if (vfx == null) 
                return;

            vfx.playRate = 1f;
        }
    }

    [HarmonyPatch(typeof(Pool<PooledMonoBehaviour>), nameof(Pool<PooledMonoBehaviour>.FetchFromPool))]
    [HarmonyPatch(new[] { typeof(Vector3), typeof(Quaternion) })]
    public class Patch_PoolMonoBehavior_FetchFromPool
    {
        static void Postfix(PooledMonoBehaviour __result, Vector3 position, Quaternion rotation)
        {
            var vfx = __result.GetComponent<VisualEffect>(); 
            if (vfx == null) 
                return; 
            
            string name = vfx.name;

            if (Main.isRecording || Main.isBuffering)
            {
                FXOneShotType? type = ReplayCache.VFXNameToFX.TryGetValue(name, out var foundType) ? foundType : null;

                if (type.HasValue)
                {
                    var evt = new EventChunk
                    {
                        type = EventType.OneShotFX, 
                        fxType = type.Value, 
                        position = position
                    }; 
                    
                    if (type is FXOneShotType.Ricochet) 
                        evt.rotation = rotation; Main.instance.Events.Add(evt);
                }
            }

            if (Main.playbackSpeed != 0f)
            {
                if (name is "Jump_VFX" or "Dash_VFX" && Main.instance.PlaybackPlayers?.Length > 0)
                {
                    float minDistance = Main.instance.PlaybackPlayers
                        .Select(player => Vector3.Distance(player.Controller.transform.GetChild(1).GetChild(2).position, vfx.transform.position))
                        .DefaultIfEmpty(999f)
                        .Min();
            
                    if (minDistance < 0.2f)
                    {
                        vfx.playRate = Abs(Main.playbackSpeed); 
                        GameObject.Destroy(vfx.GetComponent<PooledVisualEffect>()); 
                        vfx.transform.SetParent(Main.instance.VFXParent.transform); 
                        vfx.gameObject.AddComponent<DeleteAfterSeconds>(); 
                        return;
                    }
                }
                
                vfx.playRate = 1f;
            }
        }
    }
    
    // ----- Events -----

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.ReduceHealth))]
    public class Patch_PlayerHealth_ReduceHealth
    {
        static void Postfix(PlayerHealth __instance, short amount)
        {
            if (!Main.isRecording && !Main.isBuffering)
                return;

            if (!(bool)Main.instance.EnableLargeDamageMarker.SavedValue)
                return;
            
            string playerId = __instance.parentController.assignedPlayer.Data.GeneralData.PlayFabMasterId;

            if (!damageQueues.TryGetValue(playerId, out var queue))
            {
                queue = new Queue<(float, int)>();
                damageQueues[playerId] = queue;
            }

            float time = Main.lastSampleTime;
            queue.Enqueue((time, amount));

            while (queue.Count > 0 && queue.Peek().time < time - (float)Main.instance.DamageWindow.SavedValue)
                queue.Dequeue();

            int sum = 0;
            foreach (var e in queue)
                sum += e.damage;

            float oldestTimeInQueue = queue.Count > 0 ? queue.Peek().time : -999f;
            float lastProcessed = lastLargeDamageTime.GetValueOrDefault(playerId, -999f);

            if (sum >= (int)Main.instance.DamageThreshold.SavedValue && oldestTimeInQueue > lastProcessed)
            {
                lastLargeDamageTime[playerId] = time;
                queue.Clear();
                
                Main.instance.Events.Add(new EventChunk
                {
                    type = EventType.Marker,
                    markerType = MarkerType.LargeDamage,
                    position = Main.instance.head.position,
                    playerIndex = GetPlayerIndex(__instance.ParentController.assignedPlayer)
                });
            }
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.SpawnHitEffects))]
    public class Patch_PlayerHealth_SpawnHitEffects
    {
        static void Postfix(PlayerHealth __instance, short damage, Vector3 position)
        {
            Main.instance.Events.Add(new EventChunk
            {
                damage = damage,
                position = position,
                playerIndex = GetPlayerIndex(__instance.ParentController.assignedPlayer)
            });
        }
    }
    
    // ----- Late-Player joining/leaving -----
    
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Initialize))]
    public class Patch_PlayerController_Initialize
    {
        static void Postfix(PlayerController __instance)
        {
            if (!Main.isRecording && !Main.isBuffering)
                return;

            var player = __instance.assignedPlayer;
            if (player == null) return;

            string id = player.Data.GeneralData.PlayFabMasterId;

            if (Main.instance.MasterIdToIndex.TryGetValue(id, out int idx))
            {
                Main.instance.RecordedPlayers[idx] = player;
                return;
            }

            int index = Main.instance.RecordedPlayers.Count;
            Main.instance.MasterIdToIndex[id] = index;
            Main.instance.RecordedPlayers.Add(player);

            var info = new PlayerInfo(player);

            Main.instance.PlayerInfos.Add(info);
        }
    }

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.DeActivate))]
    public class Patch_PlayerController_Destroy
    {
        static void Prefix(PlayerController __instance)
        {
            if (!Main.isRecording && !Main.isBuffering)
                return;

            string id = __instance?.assignedPlayer?.Data?.GeneralData?.PlayFabMasterId;
            
            if (string.IsNullOrEmpty(id))
                return;

            if (Main.instance.MasterIdToIndex.TryGetValue(id, out int idx))
                Main.instance.RecordedPlayers[idx] = null;
        }
    }
    

    // ----- Stack Recording -----
    [HarmonyPatch(typeof(PlayerStackProcessor), nameof(PlayerStackProcessor.Execute))]
    public class Patch_PlayerStackProcessor_Execute
    {
        static void Postfix(Stack stack, PlayerStackProcessor __instance)
        {
            if (!Main.isRecording && !Main.isBuffering)
                return;

            if (ReplayCache.NameToStackType.TryGetValue(stack.cachedName, out var type))
                activations.Add((__instance.ParentController.assignedPlayer.Data.GeneralData.PlayFabMasterId, (short)type));
        }
    }
    
    
    // ----- Other -----
    [HarmonyPatch(typeof(SceneManager), nameof(SceneManager.LoadSceneAsync))]
    [HarmonyPatch(new[] { typeof(int), typeof(bool), typeof(bool), typeof(float), typeof(LoadSceneMode), typeof(AudioCall) })]
    public class Patch_SceneManager_LoadSceneAsync
    {
        static void Prefix() => Main.instance.StopReplay();
    }

    [HarmonyPatch(typeof(ParkBoardTrigger), nameof(ParkBoardTrigger.OnTriggerEnter))]
    public class Patch_ParkBoardTrigger_OnTriggerEnter
    {
        static void Postfix(Collider other)
        {
            // In a replay park
            if (PhotonNetwork.CurrentRoom == null)
                MelonCoroutines.Start(Utilities.LoadMap(1));
        }
    }

    [HarmonyPatch(typeof(PlayerHandPresence), nameof(PlayerHandPresence.UpdateHandPresenceAnimationStates))]
    public class Patch_PlayerHandPresence_UpdateHandPresenceAnimationStates
    {
        static void Prefix(PlayerHandPresence __instance, InputManager.Hand hand, ref PlayerHandPresence.HandPresenceInput input)
        {
            if (!Main.isPlaying || !Utilities.IsReplayClone(__instance.parentController) || Main.instance.PlaybackPlayers == null)
                return;

            Clone playbackPlayer = null;
            foreach (var player in Main.instance.PlaybackPlayers)
            {
                if (player == null)
                    continue;

                if (player.Controller == __instance.parentController)
                {
                    playbackPlayer = player;
                    break;
                }
            }

            if (playbackPlayer == null)
                return;

            input = hand == InputManager.Hand.Left
                ? playbackPlayer.lHandInput
                : playbackPlayer.rHandInput;
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.AttemptResetHealth))]
    public class Patch_PlayerHealth_AttemptResetHealth 
    {
        static bool Prefix(PlayerHealth __instance)
        {
            if (!Main.isPlaying || !Utilities.IsReplayClone(__instance.parentController))
                return true;

            return false;
        }
    }
}