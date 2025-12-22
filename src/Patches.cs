using System.Collections.Generic;
using HarmonyLib;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
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
            if (!Main.instance.isRecording)
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

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Initialize))]
    public class Patch_PlayerController_Initialize
    {
        static void Postfix(PlayerController __instance)
        {
            if (!Main.instance.isRecording)
                return;

            string id = __instance.assignedPlayer.Data.GeneralData.PlayFabMasterId;

            if (Main.instance.MasterIdToIndex.TryGetValue(id, out int idx))
            {
                Main.instance.RecordedPlayers[idx] = __instance.assignedPlayer;
                return;
            }

            int index = Main.instance.RecordedPlayers.Count;
            Main.instance.MasterIdToIndex[id] = index;
            Main.instance.RecordedPlayers.Add(__instance.assignedPlayer);
        }
    }

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.DeActivate))]
    public class Patch_PlayerController_Destroy
    {
        static void Prefix(PlayerController __instance)
        {
            if (!Main.instance.isRecording)
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
            if (ReplayGlobals.ReplayCache.NameToStackType.TryGetValue(stack.cachedName, out var type))
                activations.Add((__instance.ParentController.assignedPlayer.Data.GeneralData.PlayFabMasterId, (short)type));
        }
    }
}