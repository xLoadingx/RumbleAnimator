using Il2CppRUMBLE.Interactions;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Slabs.Forms;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using RumbleAnimator.Recording;
using UnityEngine;
using RumbleModdingAPI;
using UnityEngine.VFX;

namespace RumbleAnimator.Utils;

public class SlabBuilder
{
    public static GameObject slabPrefab;
    public static GameObject smallerSlabSettings;
    public static Transform spawnTransform;
    public static InteractionButton slabButton;
    public static List<TextMeshPro> fileNameTexts = new();
    public static List<GameObject> buttons = new();
    public static GameObject replayFilesText;
    public static GameObject previousPageButton;
    public static GameObject nextPageButton;

    public static Components.DisableOnHit mainSlabDestroy;
    
    public static List<string[]> paginatedFiles = new();
    private static int currentPage = 0;

    public static bool IsBuilt;
    public static float Distance = 0.4f;
    public static float HeightOffset = 2.0f;

    public static void BuildSlab()
    {
        slabPrefab = GameObject.Instantiate(Calls.GameObjects.Gym.Logic.School.GetGameObject());
        slabPrefab.name = "ReplaySlab";
        slabPrefab.transform.localScale = Vector3.one * 0.8f;
        slabPrefab.transform.position = new Vector3(0, -100, 0);
        GameObject.DontDestroyOnLoad(slabPrefab);

        Transform slab = slabPrefab.transform.GetChild(0);
        slab.transform.localScale = Vector3.one;
        slab.localPosition = Vector3.zero;
        slab.GetChild(0).GetChild(0).localPosition = Vector3.zero;

        slabButton = slabPrefab.transform.GetChild(1).GetChild(1).GetChild(0).GetComponent<InteractionButton>();
        spawnTransform = slab.GetChild(3);

        Transform slabCanvas = slab.GetChild(0).GetChild(0).GetChild(0).GetChild(1);
        for (int i = 0; i < slabCanvas.childCount; i++)
            GameObject.Destroy(slabCanvas.GetChild(i).gameObject);

        GameObject slabMesh = slab.GetChild(0).GetChild(0).GetChild(0).GetChild(0).GetChild(0).gameObject;
        GameObject tutorialSlab = Calls.GameObjects.Gym.Tutorial.StaticTutorials.RUMBLEStarterGuide
            .BaseSlab.SlabRock.GetGameObject();

        slabMesh.GetComponent<MeshFilter>().mesh = tutorialSlab.GetComponent<MeshFilter>().mesh;
        slabMesh.GetComponent<MeshRenderer>().material = tutorialSlab.GetComponent<MeshRenderer>().material;
        slabMesh.transform.localRotation *= Quaternion.Euler(0, 180, 0);

        var meshes = Utilities.LoadObjectFromBundle<GameObject>("replaysettingsmeshes", "ReplayMenuMeshes");
        meshes.name = "Buttons";
        meshes.transform.SetParent(slabMesh.transform);
        meshes.transform.localRotation = Quaternion.Euler(270, 180, 0);
        meshes.transform.localScale = Vector3.one * 118.1141f;
        meshes.transform.localPosition = new Vector3(-0.0651f, 0.2347f, 0.1563f);

        var controls = Calls.GameObjects.Gym.Scene.GymProduction.DressingRoom.ControlPanel
            .GetGameObject().transform.GetChild(0).GetChild(0);

        meshes.GetComponent<MeshRenderer>().material = new Material(
            controls.GetChild(0).GetComponent<MeshRenderer>().material
        );

        nextPageButton = Calls.Create.NewButton(() =>
        {
            currentPage = (currentPage + 1) % 4;
            ShowPage(currentPage);
        });
        nextPageButton.transform.SetParent(slabMesh.transform);
        nextPageButton.transform.localRotation = Quaternion.Euler(0, 90, 90);
        nextPageButton.transform.localPosition = new Vector3(-0.3095f, -0.1884f, 0.1305f);
        nextPageButton.name = "NextPageButton";

        previousPageButton = Calls.Create.NewButton(() =>
        {
            currentPage = (currentPage - 1 + 4) % 4;
            ShowPage(currentPage);
        });
        previousPageButton.transform.SetParent(slabMesh.transform);
        previousPageButton.transform.localRotation = Quaternion.Euler(0, 270, 270);
        previousPageButton.transform.localPosition = new Vector3(0.0935f, -0.1884f, 0.1305f);
        previousPageButton.name = "PreviousPageButton";

        for (int i = 0; i < meshes.transform.childCount; i++)
        {
            GameObject buttonFrame = meshes.transform.GetChild(i).gameObject;
            if (buttonFrame.GetComponent<RectTransform>())
                continue;

            buttonFrame.GetComponent<MeshRenderer>().material = new Material(
                controls.GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material
            );

            buttonFrame.transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = new Material(
                controls.GetChild(1).GetChild(0).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material
            );
            
            Transform interactionButton = buttonFrame.transform.GetChild(0);
            var interactorButton = Calls.Create.NewButton(() =>
            {
                MelonLogger.Msg($"File selected: {interactionButton.GetComponent<Components.TagHolder>().holder}");
                MelonCoroutines.Start(Utilities.EaseLerp(new Vector3(-0.06f, 0.0836f, 0.1309f),
                new Vector3(0, 0, 0), 1.3f, Vector3.Lerp,
                curPos =>
                {
                    if (smallerSlabSettings != null) 
                        smallerSlabSettings.transform.localPosition = curPos;
                }));
            });
            interactorButton.name = "PushableButton";
            interactorButton.transform.SetParent(interactionButton);

            float buttonZPosition = i switch
            {
                1 => 0.0031f,
                2 => 0.0019f,
                3 => 0.0007f,
                _ => 0.00427f
            };

            interactorButton.transform.localRotation = Quaternion.Euler(0, 270, 0);
            interactorButton.transform.localPosition = new Vector3(0, -0.0015f, buttonZPosition);
            interactorButton.transform.localScale = new Vector3(0.0033f, 0.0081f, 0.0452f);
            interactorButton.GetComponent<MeshRenderer>().enabled = false;
            interactorButton.transform.GetChild(0).GetChild(2).GetComponent<MeshRenderer>().enabled = false;
            interactorButton.transform.GetChild(0).GetComponent<MeshRenderer>().enabled = false;
            buttonFrame.transform.GetChild(0).GetChild(0).SetParent(interactorButton.transform.GetChild(0), true);
            interactionButton.gameObject.AddComponent<Components.TagHolder>();
            buttons.Add(interactionButton.gameObject);
            
            GameObject fileNameTxt = Calls.Create.NewText(
                "FileName", 1f, Color.yellow, Vector3.zero, Quaternion.identity
            );
            fileNameTxt.transform.SetParent(buttonFrame.transform);
            fileNameTxt.name = "FileName";
            fileNameTxt.transform.localRotation = Quaternion.Euler(90, 0, 0);
            fileNameTxt.transform.localPosition = new Vector3(0.0003f, -0.00103f, 0.0038f);
            fileNameTxt.transform.localScale = Vector3.one * 0.0025f;
            fileNameTxt.GetComponent<TextMeshPro>().enableWordWrapping = false;
            fileNameTxt.GetComponent<TextMeshPro>().overflowMode =  TextOverflowModes.Overflow;
            fileNameTxt.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;
            fileNameTxt.GetComponent<TextMeshPro>().enableWordWrapping = false;
            
            fileNameTexts.Add(fileNameTxt.GetComponent<TextMeshPro>());
        }

        GameObject replayText = Calls.Create.NewText(
            "Replays", 2f, new Color(0.102f, 0.0667f, 0.051f, 1),
            new Vector3(-0.0982f, 0.7627f, 0.1681f), Quaternion.identity
        );
        replayText.name = "ReplaysText";
        replayText.transform.SetParent(slabMesh.transform);
        replayText.transform.localRotation = Quaternion.Euler(0, 180, 0);
        replayText.transform.localPosition = new Vector3(-0.2619f, 0.7336f, 0.1516f);

        replayFilesText = GameObject.Instantiate(replayText);
        replayFilesText.GetComponent<TextMeshPro>().color = Color.yellow;
        replayFilesText.GetComponent<TextMeshPro>().enableWordWrapping = false;
        replayFilesText.GetComponent<TextMeshPro>().text = "Replay Files";
        replayFilesText.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;
        replayFilesText.transform.localScale = Vector3.one * 0.23f;
        replayFilesText.name = "ReplayFilesText";
        replayFilesText.transform.SetParent(slabMesh.transform);
        replayFilesText.transform.localPosition = new Vector3(-0.1069f, 0.561f, 0.1578f);
        replayFilesText.transform.localRotation = Quaternion.Euler(0, 180, 0);
        
        GameObject nextPageText = Calls.Create.NewText("Next Page", 0.7f, new Color(0.102f, 0.0667f, 0.051f, 1), Vector3.zero, Quaternion.identity);
        nextPageText.transform.SetParent(nextPageButton.transform);
        nextPageText.transform.localRotation = Quaternion.Euler(90, 90, 0);
        nextPageText.transform.localPosition = new Vector3(-0.1065f, 0.0204f, -0.2475f);
        nextPageText.name = "NextPageText";

        GameObject previousPageText = Calls.Create.NewText("Previous Page", 0.7f, new Color(0.102f, 0.0667f, 0.051f, 1), Vector3.zero, Quaternion.identity);
        previousPageText.transform.SetParent(previousPageButton.transform);
        previousPageText.transform.localRotation = Quaternion.Euler(90, 270, 0);
        previousPageText.transform.localPosition = new Vector3(0.1021f, 0.0204f, 0.1656f);
        previousPageText.name = "PreviousPageText";

        var infoSlab = slab.GetChild(0).GetChild(0).GetChild(0);
        GameObject.Destroy(infoSlab.GetComponent<DisposableObject>());
        GameObject.Destroy(infoSlab.GetComponent<CollisionHandler>());
        
        GameObject disposableGO = infoSlab.GetChild(2).gameObject;
        mainSlabDestroy = disposableGO.AddComponent<Components.DisableOnHit>();
        mainSlabDestroy.destroyVFX = infoSlab.GetChild(4).GetComponent<VisualEffect>();
        mainSlabDestroy.disableObject = infoSlab.GetChild(0).gameObject;
        mainSlabDestroy.Initialize();
        
        smallerSlabSettings = GameObject.Instantiate(Calls.GameObjects.Gym.Logic.Notifications.NotificationSlabOther.
            GetGameObject().transform.
            GetChild(0).
            GetChild(0).
            GetChild(0).gameObject
        );
        smallerSlabSettings.name = "SmallerSlabSettings";
        smallerSlabSettings.transform.SetParent(slabMesh.transform);
        smallerSlabSettings.transform.localPosition = new Vector3(-0.06f, 0.0836f, 0.1309f);

        IsBuilt = true;
    }

    public static void ShowPage(int page)
    {
        if (page < 0 || page >= paginatedFiles.Count)
            return;
        
        var files = paginatedFiles[page];
        
        for (int i = 0; i < fileNameTexts.Count; i++)
        {
            if (i >= files.Length)
            {
                fileNameTexts[i].transform.parent.gameObject.SetActive(false);
                continue;
            }

            buttons[i].GetComponent<Components.TagHolder>().holder = files[i];
            
            using var stream = new FileStream(files[i], FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);
            var header = ReplayFile.GetHeaderFromFile(files[i], stream, reader);

            string display = $"Unrecognized scene ({header.Scene}) | {header.Date}";
            string sceneName = Utilities.GetFriendlySceneName(header.Scene);

            string hostPrefix = header.HostPlayer is not null && header.HostPlayer != header.LocalPlayer
                ? $"{header.HostPlayer}'s "
                : "";

            if (sceneName is "Gym")
            {
                display = $"Gym - {header.LocalPlayer} | {header.Date}";
            }
            else if (sceneName is "Park")
            {
                display = $"{hostPrefix}Park - {header.LocalPlayer} | {header.Date} {header.playerCount} | {(header.playerCount > 1 ? "Players" : "Player")}";
            }
            else
            {
                string finalScene = sceneName == "Custom"
                    ? (header.Meta.TryGetValue("customMapName", out var name) ? name?.ToString() ?? "Custom" : "Custom")
                    : sceneName;

                display = $"{header.LocalPlayer} vs {header.opponent ?? "Unknown"} on {finalScene} | {header.Date}";
            }
            
            fileNameTexts[i].transform.parent.gameObject.SetActive(true);
            fileNameTexts[i].text = display;
            fileNameTexts[i].ForceMeshUpdate();
        }
    }

    public static void ShowSlab()
    {
        if (!IsBuilt) BuildSlab();

        Transform headsetTr = Calls.Players.GetLocalPlayer().Controller.transform.GetChild(1).GetChild(0).GetChild(0);
        Vector3 flatForward = headsetTr.forward;
        flatForward.y = 0f;
        flatForward.Normalize();

        Vector3 spawnPos = headsetTr.position + flatForward * Distance;
        spawnPos.y -= HeightOffset;

        spawnTransform.position = spawnPos;
        
        Vector3 direction = (spawnTransform.position - headsetTr.position).normalized;
        float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        spawnTransform.rotation = Quaternion.Euler(0f, angle, 0f);

        paginatedFiles.Clear();
        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays");
        int itemsPerPage = 4;
        string[] replayFiles = Directory.GetFiles(directory, "*.replay");
        int totalPages = (int)Math.Ceiling(replayFiles.Length / (float)itemsPerPage);

        for (int page = 0; page < totalPages; page++)
        {
            var pageItems = replayFiles
                .Skip(page * itemsPerPage)
                .Take(itemsPerPage)
                .ToArray();
            paginatedFiles.Add(pageItems);
        }

        currentPage = 0;
        ShowPage(currentPage);
        replayFilesText.GetComponent<TextMeshPro>().text = $"Replay Files ({replayFiles.Length})";
        replayFilesText.GetComponent<TextMeshPro>().ForceMeshUpdate();
        
        slabPrefab.transform.GetChild(0).GetChild(0).GetChild(0).GetChild(0).GetChild(0).gameObject.SetActive(true);
        
        slabButton.RPC_OnPressed();
    }
}