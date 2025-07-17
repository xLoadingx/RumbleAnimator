using System.Collections;
using Il2CppRUMBLE.Interactions;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Slabs.Forms;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using RumbleAnimator.Recording;
using UnityEngine;
using RumbleModdingAPI;
using UnityEngine.UI;
using UnityEngine.VFX;
using static UnityEngine.Mathf;

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
    public static GameObject pageAmountText;
    public static GameObject previousPageButton;
    public static GameObject nextPageButton;
    
    public static GameObject currentHighlight;
    public static string currentSelectedFile;
    public static bool isAnimating;
    public static object currentSlabRoutine;

    public static Components.DisableOnHit mainSlabDestroy;
    
    public static event EventDelegates.File onFileSelected;
    
    public static List<string[]> paginatedFiles = new();
    private static int currentPage = 0;

    public static bool IsBuilt;
    public static bool IsSmallerSlabShown;
    public static bool IsShown;
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
            pageAmountText.GetComponent<TextMeshPro>().text = $"Page {currentPage + 1} of {paginatedFiles.Count}";
            ShowPage(currentPage);
        });
        nextPageButton.transform.SetParent(slabMesh.transform);
        nextPageButton.transform.localRotation = Quaternion.Euler(0, 90, 90);
        nextPageButton.transform.localPosition = new Vector3(-0.3716f, -0.1339f, 0.1383f);
        nextPageButton.transform.localScale = Vector3.one * 0.7f;
        nextPageButton.name = "NextPageButton";

        previousPageButton = Calls.Create.NewButton(() =>
        {
            currentPage = (currentPage - 1 + 4) % 4;
            pageAmountText.GetComponent<TextMeshPro>().text = $"Page {currentPage + 1} of {paginatedFiles.Count}";
            ShowPage(currentPage);
        });
        previousPageButton.transform.SetParent(slabMesh.transform);
        previousPageButton.transform.localRotation = Quaternion.Euler(0, 270, 270);
        previousPageButton.transform.localPosition = new Vector3(0.1927f, -0.1339f, 0.1383f);
        previousPageButton.transform.localScale = Vector3.one * 0.7f;
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
                if (Main.isPlaying || isAnimating) return;
                
                currentHighlight?.SetActive(false);
                
                if (currentSelectedFile == interactionButton.GetComponent<Components.TagHolder>().holder)
                {
                    currentHighlight = null;
                    currentSelectedFile = null;

                    MelonCoroutines.Start(WaitThenStart(HideSmallSlab()));
                }
                else
                {
                    smallerSlabSettings.transform.GetChild(0).gameObject.SetActive(true);
                    currentHighlight = buttonFrame.transform.GetChild(1).gameObject;
                    currentHighlight.SetActive(true);
                    
                    currentSelectedFile = interactionButton.GetComponent<Components.TagHolder>().holder;

                    onFileSelected?.Invoke(currentSelectedFile);
                    
                    if (!IsSmallerSlabShown)
                    {
                        smallerSlabSettings.transform.localRotation = Quaternion.Euler(0, 0, 0);
                        smallerSlabSettings.SetActive(true);
                        MelonCoroutines.Start(Utilities.PlaySound("Slab_Construct.wav"));

                        MelonCoroutines.Start(WaitThenStart(ShowSmallSlab()));
                    }
                    else
                    {
                        MelonCoroutines.Start(Utilities.PlaySound("Slab_Textchange.wav"));
                        smallerSlabSettings.transform.GetChild(3).GetComponent<VisualEffect>().Play();
                    }
                }
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
            interactorButton.transform.localScale = new Vector3(0.0033f, -0.00101f, 0.0425f);
            interactorButton.GetComponent<MeshRenderer>().enabled = false;
            interactorButton.transform.GetChild(0).GetChild(2).GetComponent<MeshRenderer>().enabled = false;
            interactorButton.transform.GetChild(0).GetComponent<MeshRenderer>().enabled = false;
            var buttonMesh = buttonFrame.transform.GetChild(0).GetChild(0);
            buttonMesh.SetParent(interactorButton.transform.GetChild(0), true);
            buttonMesh.transform.localRotation = Quaternion.Euler(0, 90, 0);
            buttonMesh.transform.localPosition = new Vector3(0, -0.0435f, 0);
            interactorButton.transform.localPosition = new Vector3(0, -0.0009f, buttonZPosition);
            interactionButton.gameObject.AddComponent<Components.TagHolder>();
            buttons.Add(interactionButton.gameObject);

            GameObject buttonHighlight = GameObject.Instantiate(Calls.GameObjects.Gym.Logic.HeinhouserProducts
                .Leaderboard.PlayerTags.PersonalHighscoreTag.PlayerTag.GetGameObject().transform.GetChild(0).GetChild(0)
                .GetChild(7)).gameObject;
            buttonHighlight.transform.SetParent(buttonFrame.transform, false);
            buttonHighlight.transform.localPosition = new Vector3(0.0002f, -0.0011f, 0.00407f);
            buttonHighlight.transform.localRotation = Quaternion.Euler(0, 270, 90);
            buttonHighlight.transform.localScale = new Vector3(0.0001f, 0.0006f, 0.014f);
            buttonHighlight.SetActive(false);
            
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

        FileSystemWatcher watcher = new FileSystemWatcher();
        watcher.Path = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays");
        watcher.IncludeSubdirectories = false;
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

        watcher.Created += (sender, e) => { UpdatePages(); ShowPage(currentPage); };
        watcher.Deleted += (sender, e) => { UpdatePages(); ShowPage(currentPage); };
        watcher.Changed += (sender, e) => { UpdatePages(); ShowPage(currentPage); };

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

        var sprite = Calls.GameObjects.Gym.Logic.HeinhouserProducts.Telephone.FriendScreen.GetGameObject()
            .transform.GetChild(2).GetChild(0).GetChild(0).GetChild(3).GetChild(0).GetComponent<Image>().sprite;
        
        var nextPageCanvas = new GameObject("Canvas").AddComponent<Canvas>();
        nextPageCanvas.renderMode = RenderMode.WorldSpace;
        nextPageCanvas.transform.SetParent(nextPageButton.transform.GetChild(0), false);
        nextPageCanvas.transform.localPosition = new Vector3(0, 0.0135f, 0);
        nextPageCanvas.transform.localRotation = Quaternion.Euler(90, 90, 0);
        nextPageCanvas.transform.localScale = Vector3.one * 0.001f;
        
        var nextPageImageGO = new GameObject("NextPageImage");
        nextPageImageGO.transform.SetParent(nextPageCanvas.transform, false);
        var nextPageImage = nextPageImageGO.AddComponent<Image>();
        nextPageImage.sprite = sprite;
        nextPageImage.color = new Color(0.824f, 0.827f, 0.287f, 1);
        
        var previousPageCanvas = new GameObject("Canvas").AddComponent<Canvas>();
        previousPageCanvas.renderMode = RenderMode.WorldSpace;
        previousPageCanvas.transform.SetParent(previousPageButton.transform.GetChild(0), false);
        previousPageCanvas.transform.localPosition = new Vector3(0, 0.0135f, 0);
        previousPageCanvas.transform.localRotation = Quaternion.Euler(90, 90, 0);
        previousPageCanvas.transform.localScale = Vector3.one * 0.001f;
        
        var previousPageImageGO = new GameObject("PreviousPageImage");
        previousPageImageGO.transform.SetParent(previousPageCanvas.transform, false);
        var previousPageImage = previousPageImageGO.AddComponent<Image>();
        previousPageImage.sprite = sprite;
        previousPageImage.color = new Color(0.824f, 0.827f, 0.287f, 1);
        
        pageAmountText = Calls.Create.NewText("1 / 1", 0.7f, new Color(0.102f, 0.0667f, 0.051f, 1), Vector3.zero, Quaternion.identity);
        pageAmountText.transform.SetParent(slabMesh.transform);
        pageAmountText.transform.localRotation = Quaternion.Euler(0, 180, 0);
        pageAmountText.transform.localPosition = new Vector3(-0.38f, -0.1417f, 0.1484f);
        pageAmountText.name = "PageAmountText";
        
        GameObject punchText = Calls.Create.NewText("Punch to Dismiss!", 0.7f, new Color(0.102f, 0.0667f, 0.051f, 1), Vector3.zero, Quaternion.identity);
        punchText.transform.SetParent(slabMesh.transform);
        punchText.transform.localRotation = Quaternion.Euler(0, 180, 0);
        punchText.transform.localPosition = new Vector3(-0.2891f, -0.5278f, 0.1484f);
        punchText.name = "PunchText";

        var infoSlab = slab.GetChild(0).GetChild(0).GetChild(0);
        GameObject.Destroy(infoSlab.GetComponent<DisposableObject>());
        GameObject.Destroy(infoSlab.GetComponent<CollisionHandler>());
        
        GameObject disposableGO = infoSlab.GetChild(2).gameObject;
        mainSlabDestroy = disposableGO.AddComponent<Components.DisableOnHit>();
        mainSlabDestroy.destroyVFX = infoSlab.GetChild(4).GetComponent<VisualEffect>();
        mainSlabDestroy.destroySFXName = "Slab_Dismiss.wav";
        mainSlabDestroy.disableObject = infoSlab.GetChild(0).gameObject;
        mainSlabDestroy.onDisabled += () => { 
            IsSmallerSlabShown = false;
            IsShown = false;
        };
        mainSlabDestroy.Initialize();
        
        smallerSlabSettings = GameObject.Instantiate(Calls.GameObjects.Gym.Logic.Notifications.NotificationSlabOther.
            GetGameObject().transform.
            GetChild(0).
            GetChild(0).
            GetChild(0).gameObject
        );
        smallerSlabSettings.name = "SmallerSlabSettings";
        smallerSlabSettings.transform.SetParent(slabMesh.transform);
        smallerSlabSettings.transform.SetPositionAndRotation(new Vector3(-0.06f, 0.0836f, 0.1309f), Quaternion.Euler(0, 0, 0));
        smallerSlabSettings.transform.localScale = new Vector3(1.25f, 1.25f, 0.4027f);
        GameObject.Destroy(smallerSlabSettings.GetComponent<SlabText>());
        GameObject.Destroy(smallerSlabSettings.GetComponent<DisposableObject>());
        smallerSlabSettings.transform.GetChild(1).gameObject.SetActive(false);
        var disableOnHit = smallerSlabSettings.transform.GetChild(2).gameObject.AddComponent<Components.DisableOnHit>();
        disableOnHit.disableObject = smallerSlabSettings.transform.GetChild(0).gameObject;
        disableOnHit.destroyVFX = smallerSlabSettings.transform.GetChild(4).GetComponent<VisualEffect>();
        disableOnHit.destroySFXName = "Slab_Dismiss.wav";
        disableOnHit.onDisabled += () => { IsSmallerSlabShown = false; smallerSlabSettings.transform.GetChild(0).GetChild(0).GetChild(0).gameObject.SetActive(false); };
        disableOnHit.Initialize();

        GameObject smallerSlabObjects = new GameObject("SmallSlabObjects");
        smallerSlabObjects.transform.SetParent(smallerSlabSettings.transform.GetChild(0).GetChild(0), false);
        GameObject playButton = Calls.Create.NewButton(() => { Main.PlayReplayFile(currentSelectedFile); });
        playButton.transform.SetParent(smallerSlabObjects.transform, false);
        playButton.transform.localRotation = Quaternion.Euler(270, 180, 0);
        playButton.transform.localPosition = new Vector3(0, 0.1527f, 0);

        IsBuilt = true;
    }

    public static IEnumerator HideSmallSlab()
    {
        isAnimating = true;
        bool moveZDone = false;
        bool rotYDone = false;
        
        MelonCoroutines.Start(Utilities.EaseLerp(0.4296f, 0.1309f, 0.8f, Lerp,
            (pos, time) =>
            {
                if (smallerSlabSettings != null)
                    smallerSlabSettings.transform.localPosition =
                        new Vector3(-1.0647f, 0.0836f, pos);
            },
            onComplete: () => moveZDone = true));
                    
        MelonCoroutines.Start(Utilities.EaseLerp(35.3818f, 0f, 0.8f, Lerp,
            (rot, time) =>
            {
                if (smallerSlabSettings != null)
                    smallerSlabSettings.transform.localRotation = Quaternion.Euler(0, rot, 0);
            },
            ease: Utilities.EaseType.EaseInOut, onComplete: () => rotYDone = true));

        while (!moveZDone || !rotYDone)
            yield return null;
        
        MelonCoroutines.Start(Utilities.PlaySound("Slab_Textchange.wav"));
        smallerSlabSettings.transform.GetChild(3).GetComponent<VisualEffect>().Play();
        smallerSlabSettings.transform.GetChild(0).GetChild(0).GetChild(0).gameObject.SetActive(false);
        
        yield return Utilities.EaseLerp(-1.0647f, -0.06f, 1.3f, Lerp,
            (pos, time) =>
            {
                if (smallerSlabSettings != null)
                    smallerSlabSettings.transform.localPosition =
                        new Vector3(pos, 0.0836f, 0.1309f);
            });

        smallerSlabSettings.SetActive(false);
        IsSmallerSlabShown = false;
        isAnimating = false;
    }

    public static IEnumerator ShowSmallSlab()
    {
        isAnimating = true;
        
        yield return Utilities.EaseLerp(-0.06f, -1.0647f, 1.3f, Lerp,
            (pos, time) =>
            {
                if (smallerSlabSettings != null)
                    smallerSlabSettings.transform.localPosition = new Vector3(pos, 0.0836f, 0.1309f);
            });
        
        MelonCoroutines.Start(Utilities.PlaySound("Slab_Textchange.wav"));
        smallerSlabSettings.transform.GetChild(3).GetComponent<VisualEffect>().Play();
        smallerSlabSettings.transform.GetChild(0).GetChild(0).GetChild(0).gameObject.SetActive(true);

        bool moveZDone = false;
        bool rotYDone = false;
        
        MelonCoroutines.Start(Utilities.EaseLerp(0.1309f, 0.4296f, 0.8f, Lerp,
            (pos, time) =>
            {
                if (smallerSlabSettings != null)
                    smallerSlabSettings.transform.localPosition =
                        new Vector3(-1.0647f, 0.0836f, pos);
            }, 
            onComplete: () => moveZDone = true));
                                
        MelonCoroutines.Start(Utilities.EaseLerp(0f, 35.3818f, 0.8f, Lerp,
            (rot, time) =>
            {
                if (smallerSlabSettings != null)
                    smallerSlabSettings.transform.localRotation = Quaternion.Euler(0, rot, 0);
            },
            ease: Utilities.EaseType.EaseInOut,
            onComplete: () => rotYDone = true));

        while (!moveZDone || !rotYDone)
            yield return null;
        
        smallerSlabSettings.SetActive(true);
        IsSmallerSlabShown = true;
        isAnimating = false;
    }

    private static IEnumerator WaitThenStart(IEnumerator nextRoutine)
    {
        while (isAnimating)
            yield return null;

        currentSlabRoutine = MelonCoroutines.Start(nextRoutine);
    }

    public static void ShowPage(int page)
    {
        if (page < 0 || page >= paginatedFiles.Count)
            return;
        
        var files = paginatedFiles[page];

        if (files.Length is 0)
        {
            for (int i = 0; i < fileNameTexts.Count; i++)
                fileNameTexts[i].transform.parent.gameObject.SetActive(false);
            return;
        }
        
        for (int i = 0; i < fileNameTexts.Count; i++)
        {
            if (i >= files.Length)
            {
                fileNameTexts[i].transform.parent.gameObject.SetActive(false);
                continue;
            }

            buttons[i].GetComponent<Components.TagHolder>().holder = files[i];
            
            ReplayFile.WithReplayReader(files[i], reader =>
            {
                var header = ReplayFile.GetHeaderFromFile(files[i], reader);
                
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
            });
        }
    }

    public static Dictionary<string, string> UpdatePages()
    {
        paginatedFiles.Clear();

        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "MatchReplays");
        int itemsPerPage = 4;
        
        string[] allFiles = Directory.GetFiles(directory)
            .Where(f => f.EndsWith(".replay") || f.EndsWith(".replay.br"))
            .ToArray();
        
        var filesByName = new Dictionary<string, string>();

        foreach (string file in allFiles)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (file.EndsWith(".br") || !filesByName.ContainsKey(name))
            {
                filesByName[name] = file;
            }
        }
        
        string[] finalReplayFiles = filesByName.Values.ToArray();
        
        finalReplayFiles = finalReplayFiles
            .OrderByDescending(File.GetCreationTime)
            .ToArray();
        
        int totalPages = (int)Math.Ceiling(finalReplayFiles.Length / (float)itemsPerPage);

        for (int page = 0; page < totalPages; page++)
        {
            var pageItems = finalReplayFiles
                .Skip(page * itemsPerPage)
                .Take(itemsPerPage)
                .ToArray();

            if (pageItems.Length > 0)
                paginatedFiles.Add(pageItems);
        }

        IsSmallerSlabShown = false;
        smallerSlabSettings.SetActive(false);

        return filesByName;
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

        var filesByName = UpdatePages();

        currentPage = 0;
        ShowPage(currentPage);
        
        replayFilesText.GetComponent<TextMeshPro>().text = filesByName.Count > 0 ? $"Replay Files ({filesByName.Count})" : "No Replay Files";
        replayFilesText.GetComponent<TextMeshPro>().ForceMeshUpdate();

        pageAmountText.GetComponent<TextMeshPro>().text = $"Page {currentPage + 1} / {paginatedFiles.Count}";
        slabPrefab.transform.GetChild(0).GetChild(0).GetChild(0).GetChild(0).GetChild(0).gameObject.SetActive(true);
        slabPrefab.SetActive(true);
        
        smallerSlabSettings.transform.SetPositionAndRotation(new Vector3(-0.06f, 0.0836f, 0.1309f), Quaternion.Euler(0, 0, 0));
        IsSmallerSlabShown = false;
        smallerSlabSettings.SetActive(false);
        
        mainSlabDestroy.Initialize();
        smallerSlabSettings.transform.GetChild(2).GetComponent<Components.DisableOnHit>().Initialize();
        
        slabButton.RPC_OnPressed();
        IsShown = true;
    }
}