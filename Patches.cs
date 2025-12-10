using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Pools;
using UnityEngine;

namespace RumbleAnimator;

public class Patches
{
    [HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Instantiate))]
    public class Patch_PoolManager_Instantiate
    {
        static void Postfix(GameObject __result)
        {
            if (!Main.instance.isRecording)
                return;

            var structure = __result.GetComponent<Structure>();
            if (structure != null && !Main.instance.Structures.Contains(structure))
                Main.instance.Structures.Add(structure);
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
}