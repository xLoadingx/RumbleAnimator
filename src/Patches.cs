using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;

namespace RumbleAnimator;

public class Patches
{
    public static List<(string playerId, short stackId)> activations = new();

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
                    playerId = __instance.ParentController.AssignedPlayer.Data.GeneralData.PlayFabMasterId
                };

                Main.instance.Events.Add(chunk);
            } catch {}
        }
    }

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

            var info = new PlayerInfo
            {
                ActorId = (byte)player.Data.GeneralData.ActorNo,
                MasterId = id,
                Name = player.Data.GeneralData.PublicUsername,
                BattlePoints = player.Data.GeneralData.BattlePoints,
                VisualData = player.Data.VisualData.ToPlayfabDataString(),
                EquippedShiftStones = player.Data.EquipedShiftStones.ToArray(),
                Measurement = player.Data.PlayerMeasurement,
                WasHost = (player.Data.GeneralData.ActorNo == PhotonNetwork.MasterClient?.ActorNumber)
            };

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