using System.Collections;
using Il2CppRootMotion.FinalIK;
using Il2CppRUMBLE.CharacterCreation;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using Object = System.Object;

namespace RumbleAnimator.Utils;

public class CloneBuilder
{
    public class CloneInfo
    {
        public GameObject RootObject;
        public GameObject VRRig;
        public GameObject LeftHand;
        public GameObject RightHand;
        public GameObject Head;

        public GameObject BodyDouble;
        public GameObject dVRRig;
        public GameObject DLeftHand;
        public GameObject DRightHand;
        public GameObject DHead;

        public GameObject PhysicsRightHand;
        public GameObject PhysicsLeftHand;
        public GameObject PhysicsHead;

        public PlayerStackProcessor StackProcessor;
        public PlayerController Controller;
    }

    public static CloneInfo BuildClone(Vector3 initialPosition, string visualDataString, string masterID, int BP, PlayerMeasurement measurement)
    {
        Player localPlayer = Calls.Players.GetLocalPlayer();

        GameObject clone = GameObject.Instantiate(Calls.Managers.GetPlayerManager().PlayerControllerPrefab.gameObject);
        PlayerController cloneController = clone.GetComponent<PlayerController>();

        clone.transform.position = initialPosition;

        Transform vr = clone.transform.GetChild(1);
        GameObject rHand = vr.GetChild(1).gameObject;
        GameObject lHand = vr.GetChild(2).gameObject;
        GameObject head = vr.GetChild(0).GetChild(0).gameObject;

        clone.transform.GetChild(9).gameObject.SetActive(false); // LIV

        clone.AddComponent<ReplayClone>();

        var vrik = clone.GetComponentInChildren<VRIK>();
        if (vrik != null)
            vrik.enabled = true;

        var psp = clone.GetComponent<PlayerStackProcessor>();
        psp.Initialize(cloneController);
        psp.activeStackHandles = localPlayer.Controller.GetComponent<PlayerStackProcessor>().activeStackHandles;
        
        cloneController.assignedPlayer.Data = localPlayer.Data;
        cloneController.assignedPlayer.Data.SetMeasurement(localPlayer.Data.PlayerMeasurement, true);
        cloneController.assignedPlayer.Data.visualData = localPlayer.Data.visualData;
        cloneController.Initialize(cloneController.AssignedPlayer);

        foreach (var driver in clone.GetComponentsInChildren<TrackedPoseDriver>())
            driver.enabled = false;

        head.GetComponent<Camera>().enabled = false;
        head.GetComponent<AudioListener>().enabled = false;

        clone.GetComponent<PlayerPoseSystem>().currentInputPoses.Clear();
        MelonLogger.Msg("Clone poses cleared");

        GameObject bodyDouble = GameObject.Instantiate(localPlayer.Controller.gameObject);
        bodyDouble.name = "BodyDouble";

        GameObject dRHand = bodyDouble.transform.GetChild(1).GetChild(1).gameObject;
        GameObject dLHand = bodyDouble.transform.GetChild(1).GetChild(2).gameObject;
        GameObject dHead = bodyDouble.transform.GetChild(1).GetChild(0).GetChild(0).gameObject;
        GameObject dOverall = bodyDouble.transform.GetChild(1).gameObject;

        bodyDouble.transform.GetChild(4).gameObject.SetActive(false);

        foreach (var driver in bodyDouble.GetComponentsInChildren<TrackedPoseDriver>())
            driver.enabled = false;

        bodyDouble.transform.GetChild(9).gameObject.SetActive(false); // LIV

        GameObject health = GameObject.Find("Health");
        if (health != null)
            health.transform.SetParent(clone.transform);

        clone.transform.GetChild(1).gameObject.SetActive(false);
        clone.transform.GetChild(8).gameObject.SetActive(true);

        GameObject clonePhysics = clone.transform.GetChild(4).gameObject;
        GameObject physicsLHand = clonePhysics.transform.GetChild(2).gameObject;
        GameObject physicsRHand = clonePhysics.transform.GetChild(2).gameObject;

        physicsLHand.SetActive(false);
        physicsRHand.SetActive(false);
        clonePhysics.SetActive(false);

        clone.transform.GetChild(5).gameObject.SetActive(false); // Hitboxes
        clone.GetComponent<PlayerMovement>().enabled = false;

        MelonCoroutines.Start(VisualReskin(bodyDouble.transform.GetChild(0).GetChild(0).gameObject
            .GetComponent<SkinnedMeshRenderer>(), visualDataString, masterID, BP, bodyDouble.GetComponent<PlayerController>(), measurement));

        return new CloneInfo
        {
            RootObject = clone,
            VRRig = vr.gameObject,
            LeftHand = lHand,
            RightHand = rHand,
            Head = head,
            Controller = cloneController,
            StackProcessor = psp,

            BodyDouble = bodyDouble,
            dVRRig = dOverall,
            DLeftHand = dLHand,
            DRightHand = dRHand,
            DHead = dHead,

            PhysicsLeftHand = physicsLHand,
            PhysicsRightHand = physicsRHand,
            PhysicsHead = physicsRHand
        };
    }

    private static IEnumerator VisualReskin(SkinnedMeshRenderer renderer, string visualDataString, string masterID, int BP, PlayerController BodyDouble, PlayerMeasurement measurement)
    {
        yield return new WaitForSeconds(0.1f);

        renderer.sharedMesh = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(0).GetChild(0)
            .GetComponent<SkinnedMeshRenderer>().sharedMesh;
        renderer.material = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(0)
            .GetComponent<PlayerVisuals>().NonHeadClippedMaterial;
        renderer.updateWhenOffscreen = true;
        
        var visualData = PlayerVisualData.FromPlayfabDataString(visualDataString);
        var randomID = Guid.NewGuid().ToString();
        PlayerData clonedData = new PlayerData(
            new GeneralData
            {
                PlayFabMasterId = masterID,
                PlayFabTitleId = randomID,
                BattlePoints = BP
            },
            Calls.Players.GetLocalPlayer().Data.RedeemedMoves,
            Calls.Players.GetLocalPlayer().Data.EconomyData,
            Calls.Players.GetLocalPlayer().Data.EquipedShiftStones,
            visualData
        );

        Player clonePlayer = Player.CreateRemotePlayer(clonedData);
        BodyDouble.assignedPlayer = clonePlayer;
        clonePlayer.Controller = BodyDouble;
        PlayerManager.Instance.AllPlayers.Add(clonePlayer);

        clonePlayer.Data.SetMeasurement(measurement, false);
        CharacterCreationLookupTable.Instance.BakeApplyAndCachePlayerVisuals(randomID, visualData, false);
        
        Calls.Players.GetLocalPlayer().Controller.GetProcessorComponent<PlayerVisuals>().Initialize(Calls.Players.GetLocalPlayer().Controller);

        MelonLogger.Msg("Rebinding SkinnedMeshRenderer with delayed fix");
    }
}