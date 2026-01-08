using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using UnityEngine;
using UnityEngine.VFX;
using static UnityEngine.Mathf;

namespace RumbleAnimator;

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
            if (structure != null && !Main.instance.Structures.Contains(structure))
            {
                Main.instance.Structures.Add(structure);

                MeshRenderer renderer = structure.GetComponentInChildren<MeshRenderer>();
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();

                Main.instance.structureRenderers.Add((renderer, mpb));
            }
        }
    }

    [HarmonyPatch(typeof(Pool<PooledMonoBehaviour>), nameof(Pool<PooledMonoBehaviour>.FetchFromPool))]
    [HarmonyPatch(new[] { typeof(Vector3), typeof(Quaternion) })]
    public class Patch_PoolMonoBehavior_FetchFromPool
    {
        static void Postfix(PooledMonoBehaviour __result, Vector3 position, Quaternion rotation)
        {
            var vfx = __result.GetComponent<VisualEffect>();
            if (vfx != null)
            {
                if (vfx.name == "Jump_VFX" && Main.instance.PlaybackPlayers != null && Main.instance.PlaybackPlayers.Length > 0)
                {
                    float minDistance = Main.instance.PlaybackPlayers
                        .Select(player => Vector3.Distance(player.Controller.transform.GetChild(1).GetChild(2).position, vfx.transform.position))
                        .Prepend(999f)
                        .Min();

                    if (minDistance < 0.2f)
                    {
                        vfx.playRate = Abs(Main.playbackSpeed);
                        vfx.GetComponent<PooledVisualEffect>().returnToPoolTime = -1f;
                        vfx.transform.SetParent(Main.instance.VFXParent.transform);
                    }
                        
                }
                else
                {
                    vfx.playRate = 1;
                }
            }
        }
    }
    
    // ----- Events -----
    
    [HarmonyPatch(typeof(PlayerScaling), nameof(PlayerScaling.ScaleController))]
    public class Patch_PlayerScaling_ScaleController
    {
        // This errors for no apparent reason so I just shoved it in a try block
        
        static void Postfix(PlayerScaling __instance, PlayerMeasurement measurement)
        {
            try
            {
                if (!Main.isRecording && !Main.isBuffering)
                    return;
            
                EventChunk chunk = new EventChunk
                {
                    Length = measurement.Length, 
                    ArmSpan = measurement.ArmSpan, 
                    playerIndex = GetPlayerIndex(__instance.ParentController.assignedPlayer)
                };

                Main.instance.Events.Add(chunk);
            } catch {}
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.ReduceHealth))]
    public class Patch_PlayerHealth_ReduceHealth
    {
        static void Postfix(PlayerHealth __instance, short amount, bool useEffects)
        {
            if (!Main.isRecording && !Main.isBuffering)
                return;
            
            string playerId = __instance.parentController.assignedPlayer.Data.GeneralData.PlayFabMasterId;

            if (!damageQueues.TryGetValue(playerId, out var queue))
            {
                queue = new Queue<(float, int)>();
                damageQueues[playerId] = queue;
            }

            float time = Main.lastSampleTime;
            queue.Enqueue((time, amount));

            int sum = 0;
            while (queue.Count > 0 && queue.Peek().time < time - 3f)
                queue.Dequeue();

            foreach (var e in queue)
                sum += e.damage;

            float lastTime = lastLargeDamageTime.GetValueOrDefault(playerId, -999f);

            if (sum >= 7 && time - lastTime >= 3f)
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

            string id = __instance.assignedPlayer.Data.GeneralData.PlayFabMasterId;

            if (Main.instance.MasterIdToIndex.TryGetValue(id, out int idx))
                Main.instance.RecordedPlayers[idx] = null;
        }
    }
    

    // ----- Stack Recording -----
    [HarmonyPatch(typeof(PlayerStackProcessor), nameof(PlayerStackProcessor.Execute))]
    public class Patch_PlayerStackProcessor_Execute
    {
        static void Postfix(Stack stack, StackConfiguration overrideConfig, PlayerStackProcessor __instance)
        {
            if (!Main.isRecording && !Main.isBuffering)
                return;

            if (ReplayGlobals.ReplayCache.NameToStackType.TryGetValue(stack.cachedName, out var type))
                activations.Add((__instance.ParentController.assignedPlayer.Data.GeneralData.PlayFabMasterId, (short)type));
        }
    }
}