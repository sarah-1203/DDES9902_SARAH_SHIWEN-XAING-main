using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>Coordinates soil selection, potting feedback and scene restart for Iteration 2.</summary>
public class PlantingFlowController : MonoBehaviour
{
    private const float SoilBlockSize = 0.44f;
    private static readonly string[] SoilLabels = { "Sandy Soil", "Potting Mix", "Humus Soil", "Clay Soil" };
    private static readonly string[] SoilButtonLabels = { "Sand", "PotMix", "Humus", "Clay" };
    private static readonly string[] SoilDescriptions =
    {
        "Best for: Grass. Sandy soil drains quickly and dries faster.",
        "Best for: Flower. Potting mix balances moisture and drainage.",
        "Best for: Mushroom. Humus provides the organic growing medium.",
        "Best for: none of the available plants. Clay holds too much water."
    };
    private static readonly Color[] SoilColors =
    {
        new Color(0.78f, 0.57f, 0.30f), // sandy tan
        new Color(0.37f, 0.24f, 0.13f), // potting soil brown
        new Color(0.26f, 0.33f, 0.19f), // humus olive
        new Color(0.57f, 0.25f, 0.17f)  // clay red
    };
    private ItemCycler soilSizeCycler;
    private TMP_Text soilFeedback;
    private Color soilFeedbackDefaultColor = Color.white;
    private GameObject completionMessage;
    private GameObject completionOverlay;
    private GameObject wateringSuccessNotice;
    private TMP_Text wateringSuccessText;
    private Button overlayRestartButton;
    private Button overlayEndButton;
    private Transform potTarget;
    private Holdable wateringCan;
    private HoldableMagnetSnapper wateringSnapTarget;
    private Coroutine wateringPromptRoutine;
    private bool wateringComplete;
    private bool soilAdded;
    private bool hasEnded;
    private bool completionReached;
    private int selectedSoilIndex = -1;
    private Coroutine soilFeedbackRoutine;
    private readonly List<SeedPlant> seedPlants = new List<SeedPlant>();
    private HoldableMagnetSnapper seedSnapTarget;
    private bool seedGrown;
    private string plantedVariety;
    private Transform plantedPlant;
    private Renderer[] plantedRenderers;
    private Vector3 plantedBaseScale;
    private float plantedBaseY;
    private Coroutine plantScaleRoutine;
    private int plantGrowthStage;

    private class SeedPlant
    {
        public Holdable holdable;
        public string variety;
        public Renderer[] matureRenderers;
        public GameObject seedCube;
        public TMP_Text label;
        public Vector3 originalScale;
        public bool planted;
    }

    // The interaction toolkit's trigger relay can miss colliders while the soil is
    // being held. Keep a bounds based fallback so placing the selected block in
    // the planter always completes the step.
    private const float PotPlacementRadius = 0.20f;
    private const float SeedCubeWorldSize = 0.055f;

    private void Start()
    {
        foreach (ItemCycler cycler in FindObjectsByType<ItemCycler>())
        {
            if (cycler.gameObject.name.ToLowerInvariant().Contains("soil"))
                soilSizeCycler = cycler;
        }

        ConfigureSoilOptions();
        GameObject soilFeedbackObject = FindSceneObject("Text (correct soil");
        if (soilFeedbackObject != null)
        {
            soilFeedback = soilFeedbackObject.GetComponent<TMP_Text>();
            if (soilFeedback != null) soilFeedbackDefaultColor = soilFeedback.color;
            soilFeedbackObject.SetActive(false);
        }

        completionMessage = FindSceneObject("Text (correct watering");
        if (completionMessage != null)
            completionMessage.SetActive(false);

        CreateCompletionOverlay();
        HookSoilButtons();
        HookPotSoilReceiver();
        HookWateringReceiver();
        HookSeedPlanting();
        GameObject canObject = GameObject.FindGameObjectWithTag("watercan");
        if (canObject != null) wateringCan = canObject.GetComponent<Holdable>();
    }

    private void Update()
    {
        StabilizeSnappedWateringCan();
        CatchReleasedSeedNearPot();
        // This legacy world-space message can be activated by the scene's old event.
        // The flow now uses a screen-space success notice after valid watering.
        if (completionMessage != null && completionMessage.activeSelf)
            completionMessage.SetActive(false);
        if (completionOverlay != null)
            completionOverlay.SetActive(completionReached && !hasEnded);
        HandleCompletionButtonPointer();

        // Keyboard fallback keeps the prompt dismissible if a browser or XR
        // player does not deliver pointer events to the overlay.
        if (completionReached && !hasEnded && Keyboard.current != null)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame)
                RestartScene();
            else if (Keyboard.current.escapeKey.wasPressedThisFrame)
                EndExperience();
        }

        if (!wateringComplete && wateringPromptRoutine == null && IsWateringCanSnappedToTarget())
            ScheduleWateringCompletionPrompt();

        if (soilAdded || soilSizeCycler == null || potTarget == null)
            return;

        GameObject selectedSoil = soilSizeCycler.currentItem;
        if (selectedSoil == null || !selectedSoil.activeInHierarchy) return;

        foreach (Collider soilCollider in selectedSoil.GetComponentsInChildren<Collider>())
        {
            if (soilCollider == null || !soilCollider.enabled || !soilCollider.gameObject.activeInHierarchy) continue;
            if (soilCollider.bounds.SqrDistance(potTarget.position) <= PotPlacementRadius * PotPlacementRadius)
            {
                OnSoilEnteredPot(soilCollider);
                return;
            }
        }
    }

    private void LateUpdate()
    {
        FaceSeedLabelsToCamera();
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate.name.StartsWith(objectName) && candidate.scene.IsValid())
                return candidate;
        }
        return null;
    }

    private void HookSoilButtons()
    {
        string[] buttonNames =
        {
            "Soil Option - Sand",
            "Soil Option - PotMix",
            "Soil Option - Humus",
            "Soil Option - Clay"
        };
        for (int index = 0; index < buttonNames.Length; index++)
        {
            GameObject button = GameObject.Find(buttonNames[index]);
            if (button == null)
            {
                Debug.LogWarning($"Planting flow: could not find soil option '{buttonNames[index]}'.");
                continue;
            }
            TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>(true);
            if (buttonLabel != null)
            {
                buttonLabel.text = SoilButtonLabels[index];
                buttonLabel.enableAutoSizing = false;
                buttonLabel.fontSize = 0.2f;
            }
            InteractableGeneral interactable = button.GetComponent<InteractableGeneral>();
            if (interactable == null) interactable = button.GetComponentInChildren<InteractableGeneral>(true);
            if (interactable == null) interactable = button.GetComponentInParent<InteractableGeneral>();
            if (interactable == null)
            {
                Debug.LogWarning($"Planting flow: soil option '{buttonNames[index]}' has no InteractableGeneral component.");
                continue;
            }
            int choice = index;
            interactable.onPrimaryInteract.AddListener(() => SelectSoil(choice));
        }
    }

    private void ConfigureSoilOptions()
    {
        if (soilSizeCycler == null || soilSizeCycler.items == null) return;

        int count = Mathf.Min(SoilLabels.Length, soilSizeCycler.items.Length);
        for (int index = 0; index < count; index++)
        {
            GameObject soil = soilSizeCycler.items[index];
            if (soil == null) continue;

            // These four soil options share the same cube mesh and parent, so
            // applying one scale keeps the blocks visually and physically equal.
            soil.transform.localScale = Vector3.one * SoilBlockSize;
            soil.name = $"Soil - {SoilLabels[index]}";

            Holdable holdable = soil.GetComponent<Holdable>();
            if (holdable == null) holdable = soil.GetComponentInChildren<Holdable>(true);
            if (holdable != null)
                holdable.hoverText = SoilDescriptions[index];

            foreach (Renderer renderer in soil.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                renderer.material.color = SoilColors[index];
            }
        }
    }

    private void HookPotSoilReceiver()
    {
        HoldableMagnetSnapper potSnapper = null;
        foreach (HoldableMagnetSnapper candidate in Resources.FindObjectsOfTypeAll<HoldableMagnetSnapper>())
        {
            if (!candidate.gameObject.scene.IsValid()) continue;
            Transform ancestor = candidate.transform;
            while (ancestor != null && !ancestor.name.Equals("Magnet Snap - pot", System.StringComparison.OrdinalIgnoreCase))
                ancestor = ancestor.parent;
            if (ancestor == null) continue;
            potSnapper = candidate;
            break;
        }

        if (potSnapper == null)
        {
            Debug.LogError("Planting flow: could not find the snap target in the left flower pot.");
            return;
        }

        potTarget = potSnapper.snappingPoint != null ? potSnapper.snappingPoint : potSnapper.transform;

        Transform existing = potSnapper.transform.Find("Soil Receiver");
        GameObject zone = existing != null ? existing.gameObject : new GameObject("Soil Receiver");
        zone.transform.SetParent(potSnapper.transform, false);
        SphereCollider collider = zone.GetComponent<SphereCollider>();
        if (collider == null) collider = zone.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = PotPlacementRadius;
        Rigidbody body = zone.GetComponent<Rigidbody>();
        if (body == null) body = zone.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        PotSoilCollisionRelay relay = zone.GetComponent<PotSoilCollisionRelay>();
        if (relay == null) relay = zone.AddComponent<PotSoilCollisionRelay>();
        relay.controller = this;
    }

    private void HookWateringReceiver()
    {
        wateringSnapTarget = FindWateringSnapper();
        if (wateringSnapTarget == null)
        {
            Debug.LogWarning("Planting flow: could not find the watercan snapping point.");
            return;
        }
        wateringSnapTarget.onSnap.AddListener(OnWateringTargetSnapped);
        PotWateringCollisionRelay relay = wateringSnapTarget.GetComponent<PotWateringCollisionRelay>();
        if (relay == null) relay = wateringSnapTarget.gameObject.AddComponent<PotWateringCollisionRelay>();
        relay.controller = this;
    }

    private HoldableMagnetSnapper FindWateringSnapper()
    {
        GameObject namedTarget = GameObject.Find("Magnet Snap - correct");
        if (namedTarget != null)
        {
            foreach (HoldableMagnetSnapper snapper in namedTarget.GetComponentsInChildren<HoldableMagnetSnapper>(true))
                return snapper;
        }

        foreach (HoldableMagnetSnapper candidate in Resources.FindObjectsOfTypeAll<HoldableMagnetSnapper>())
        {
            if (!candidate.gameObject.scene.IsValid()) continue;
            if (candidate.filterString != "watercan") continue;
            Transform hierarchyNode = candidate.transform;
            while (hierarchyNode != null && !hierarchyNode.name.ToLowerInvariant().Contains("correct"))
                hierarchyNode = hierarchyNode.parent;
            if (hierarchyNode != null) return candidate;
        }
        return null;
    }

    private static bool IsCorrectWateringTarget(HoldableMagnetSnapper snapper)
    {
        Transform hierarchyNode = snapper != null ? snapper.transform : null;
        while (hierarchyNode != null)
        {
            if (hierarchyNode.name.ToLowerInvariant().Contains("correct")) return true;
            hierarchyNode = hierarchyNode.parent;
        }
        return false;
    }

    private void HookSeedPlanting()
    {
        GameObject plantGroup = GameObject.Find("plant");
        if (plantGroup == null)
        {
            Debug.LogWarning("Planting flow: could not find the plant display group for seed options.");
            return;
        }

        float seedTableTopY = FindSeedTableTopY();

        foreach (Transform plantTransform in plantGroup.transform)
        {
            string name = plantTransform.name.Trim().ToLowerInvariant();
            string variety = name.Contains("mushroom") ? "Mushroom" :
                name.Contains("grass") ? "Grass" :
                name.Contains("flower") ? "Flower" : null;
            if (variety == null) continue;

            Holdable holdable = plantTransform.GetComponent<Holdable>();
            if (holdable == null)
            {
                Debug.LogWarning($"Planting flow: '{plantTransform.name}' has no Holdable component for seed interaction.");
                continue;
            }

            SeedPlant seed = new SeedPlant
            {
                holdable = holdable,
                variety = variety,
                matureRenderers = plantTransform.GetComponentsInChildren<Renderer>(true),
                originalScale = plantTransform.localScale
            };
            foreach (Renderer renderer in seed.matureRenderers) renderer.enabled = false;

            foreach (Collider childCollider in plantTransform.GetComponentsInChildren<Collider>(true))
                if (childCollider.gameObject != holdable.gameObject) childCollider.enabled = false;

            BoxCollider seedCollider = holdable.GetComponent<BoxCollider>();
            if (seedCollider == null) seedCollider = holdable.gameObject.AddComponent<BoxCollider>();
            seedCollider.center = new Vector3(0f, SeedCubeWorldSize * 0.5f, 0f);
            seedCollider.size = Vector3.one * SeedCubeWorldSize;

            TriggerFilter filter = holdable.GetComponent<TriggerFilter>();
            if (filter == null) filter = holdable.gameObject.AddComponent<TriggerFilter>();
            filter.filterString = "plant";
            holdable.gameObject.name = $"Plant Seed - {variety}";

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"{variety} Seed Cube";
            cube.transform.SetParent(holdable.transform, false);
            cube.transform.localPosition = new Vector3(0f, SeedCubeWorldSize * 0.5f, 0f);
            cube.transform.localRotation = Quaternion.identity;
            Vector3 parentScale = holdable.transform.lossyScale;
            cube.transform.localScale = new Vector3(
                SeedCubeWorldSize / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
                SeedCubeWorldSize / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
                SeedCubeWorldSize / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f));
            Collider cubeCollider = cube.GetComponent<Collider>();
            if (cubeCollider != null) Destroy(cubeCollider);
            Renderer cubeRenderer = cube.GetComponent<Renderer>();
            Material cubeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            cubeMaterial.color = variety == "Mushroom" ? new Color(0.72f, 0.47f, 0.27f) :
                variety == "Grass" ? new Color(0.42f, 0.68f, 0.34f) : new Color(0.88f, 0.62f, 0.73f);
            cubeRenderer.material = cubeMaterial;
            if (!float.IsNaN(seedTableTopY))
            {
                float verticalOffset = seedTableTopY - seedCollider.bounds.min.y;
                holdable.transform.position += Vector3.up * verticalOffset;
            }
            seed.seedCube = cube;

            GameObject labelObject = new GameObject($"{variety} Seed Label", typeof(TextMeshPro));
            labelObject.transform.SetParent(holdable.transform, false);
            labelObject.transform.position = cubeRenderer.bounds.center + Vector3.up * (SeedCubeWorldSize * 0.5f + 0.025f);
            labelObject.transform.localScale = Vector3.one * 0.04f;
            seed.label = labelObject.GetComponent<TextMeshPro>();
            seed.label.text = $"{variety} Seed";
            seed.label.fontSize = 10f;
            seed.label.fontStyle = FontStyles.Bold;
            seed.label.alignment = TextAlignmentOptions.Center;
            seed.label.color = Color.white;
            seed.label.outlineWidth = 0.15f;
            seed.label.outlineColor = Color.black;

            seedPlants.Add(seed);
        }

        seedSnapTarget = FindPotSnapper();
        if (seedSnapTarget != null) seedSnapTarget.onSnap.AddListener(OnSeedSnappedIntoPot);
        if (seedPlants.Count != 3)
            Debug.LogWarning($"Planting flow: expected three seed options, initialized {seedPlants.Count}.");
    }

    private static float FindSeedTableTopY()
    {
        GameObject table = GameObject.Find("Table");
        if (table == null) return float.NaN;
        Renderer[] renderers = table.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return float.NaN;
        float topY = float.NegativeInfinity;
        foreach (Renderer renderer in renderers)
            topY = Mathf.Max(topY, renderer.bounds.max.y);
        return float.IsInfinity(topY) ? float.NaN : topY;
    }

    private void FaceSeedLabelsToCamera()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        foreach (SeedPlant seed in seedPlants)
        {
            if (seed.label == null || !seed.label.gameObject.activeInHierarchy) continue;
            Vector3 towardCamera = camera.transform.position - seed.label.transform.position;
            if (towardCamera.sqrMagnitude > 0.001f)
                seed.label.transform.rotation = Quaternion.LookRotation(-towardCamera, camera.transform.up);
        }
    }

    private void OnSeedSnappedIntoPot()
    {
        if (seedGrown || seedPlants.Exists(option => option.planted)) return;
        if (seedSnapTarget == null || seedSnapTarget.subject == null) return;
        foreach (SeedPlant seed in seedPlants)
        {
            if (seed.holdable != seedSnapTarget.subject || seed.planted) continue;
            BeginSeedPlanting(seed);
            return;
        }
    }

    private void BeginSeedPlanting(SeedPlant seed)
    {
        if (seed == null || seed.planted || seedGrown) return;
        seed.planted = true;
        plantedVariety = seed.variety;
        ShowSoilFeedback($"{seed.variety} seed planted. Growing...");
        StartCoroutine(GrowSeedIntoPlant(seed));
    }

    private void CatchReleasedSeedNearPot()
    {
        if (seedGrown || seedSnapTarget == null || potTarget == null || seedSnapTarget.subject != null) return;
        foreach (SeedPlant seed in seedPlants)
        {
            if (seed.planted || seed.holdable == null || seed.holdable.moving) continue;
            if (Vector3.Distance(seed.holdable.transform.position, potTarget.position) > 0.55f) continue;

            seedSnapTarget.subject = seed.holdable;
            seedSnapTarget.snapFlag = false;
            seedSnapTarget.HandleSnapping();
            return;
        }
    }

    private IEnumerator GrowSeedIntoPlant(SeedPlant seed)
    {
        yield return new WaitForSeconds(3f);
        if (seedSnapTarget == null || seedSnapTarget.subject != seed.holdable ||
            seed.holdable.myMagnetSnapper != seedSnapTarget)
        {
            seed.planted = false;
            yield break;
        }
        seedSnapTarget.SoftReleaseSubject();

        Transform plant = seed.holdable.transform;
        plant.SetParent(null, true);
        plant.rotation = Quaternion.identity;
        if (seed.seedCube != null) seed.seedCube.SetActive(false);
        if (seed.label != null) seed.label.gameObject.SetActive(false);
        foreach (Renderer renderer in seed.matureRenderers)
            if (renderer != null) renderer.enabled = true;

        Rigidbody body = seed.holdable.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }
        foreach (Collider collider in plant.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

        Vector3 targetPosition = potTarget != null ? potTarget.position : plant.position;
        targetPosition.y = GetPotPlantingSurfaceY(targetPosition.y);
        plant.position = targetPosition;
        Vector3 targetScale = seed.originalScale * 0.5f;
        plant.localScale = targetScale;
        Bounds bounds = GetRendererBounds(seed.matureRenderers);
        if (bounds.size != Vector3.zero)
            plant.position += Vector3.up * (targetPosition.y - bounds.min.y);

        Vector3 finalPosition = plant.position;
        Vector3 smallScale = targetScale * 0.03f;
        plant.localScale = smallScale;
        const float growDuration = 1.2f;
        float elapsed = 0f;
        while (elapsed < growDuration)
        {
            elapsed += Time.deltaTime;
            plant.localScale = Vector3.Lerp(smallScale, targetScale, Mathf.Clamp01(elapsed / growDuration));
            plant.position = finalPosition;
            Bounds currentBounds = GetRendererBounds(seed.matureRenderers);
            if (currentBounds.size != Vector3.zero)
                plant.position += Vector3.up * (targetPosition.y - currentBounds.min.y);
            yield return null;
        }
        plant.localScale = targetScale;
        plantedPlant = plant;
        plantedRenderers = seed.matureRenderers;
        plantedBaseScale = targetScale;
        plantedBaseY = targetPosition.y;
        seedGrown = true;
        ApplyPlantGrowthStage(soilAdded ? 1 : 0);
        ShowSoilFeedback($"{seed.variety} has grown!");
    }

    private static Bounds GetRendererBounds(Renderer[] renderers)
    {
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return hasBounds ? bounds : new Bounds();
    }

    private static float GetPotPlantingSurfaceY(float fallbackY)
    {
        GameObject pot = GameObject.Find("Pot");
        if (pot == null) return fallbackY + 0.12f;
        Renderer[] renderers = pot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return fallbackY + 0.12f;

        float rimTopY = float.NegativeInfinity;
        foreach (Renderer renderer in renderers)
            if (renderer != null) rimTopY = Mathf.Max(rimTopY, renderer.bounds.max.y);
        return float.IsInfinity(rimTopY) ? fallbackY + 0.12f : rimTopY - 0.055f;
    }

    private HoldableMagnetSnapper FindPotSnapper()
    {
        foreach (HoldableMagnetSnapper candidate in Resources.FindObjectsOfTypeAll<HoldableMagnetSnapper>())
        {
            if (!candidate.gameObject.scene.IsValid()) continue;
            Transform ancestor = candidate.transform;
            while (ancestor != null && !ancestor.name.Equals("Magnet Snap - pot", System.StringComparison.OrdinalIgnoreCase))
                ancestor = ancestor.parent;
            if (ancestor != null) return candidate;
        }
        return null;
    }

    private void StabilizeSnappedWateringCan()
    {
        if (wateringCan == null) return;
        HoldableMagnetSnapper snapper = wateringCan.myMagnetSnapper;
        if (!IsCorrectWateringTarget(snapper)) return;

        Rigidbody body = wateringCan.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }
    }

    public void OnWateringCanEnteredPot(Collider other)
    {
        if (wateringComplete || !soilAdded || !seedGrown || other == null) return;
        GameObject otherObject = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
        if (otherObject.tag != "watercan" && other.transform.root.tag != "watercan") return;
        Holdable liquid = otherObject.GetComponent<Holdable>();
        if (liquid == null) liquid = other.GetComponentInParent<Holdable>();
        if (liquid == null) return;

        if (liquid != wateringCan || !IsWateringCanSnappedToTarget()) return;
        ScheduleWateringCompletionPrompt();
    }

    private bool IsWateringCanSnappedToTarget()
    {
        return wateringCan != null && wateringSnapTarget != null &&
            wateringCan.myMagnetSnapper == wateringSnapTarget &&
            wateringSnapTarget.subject == wateringCan;
    }

    private void OnWateringTargetSnapped()
    {
        if (completionMessage != null) completionMessage.SetActive(false);
        // This listener is attached only to the watercan-filtered correct snap target.
        if (wateringSuccessText != null) wateringSuccessText.text = "Correct!";
        if (wateringSuccessNotice != null)
        {
            wateringSuccessNotice.SetActive(true);
        }

        ScheduleWateringCompletionPrompt();
    }

    private void ScheduleWateringCompletionPrompt()
    {
        if (wateringComplete || wateringPromptRoutine != null) return;
        if (wateringSuccessText != null) wateringSuccessText.text = "Correct! Watering complete.";
        if (wateringSuccessNotice != null) wateringSuccessNotice.SetActive(true);
        wateringPromptRoutine = StartCoroutine(ShowWateringCompletionAfterDelay());
    }

    private IEnumerator ShowWateringCompletionAfterDelay()
    {
        yield return new WaitForSeconds(3f);
        wateringPromptRoutine = null;
        if (wateringComplete) yield break;

        wateringComplete = true;
        ApplyPlantGrowthStage(2);
        HideSoilFeedback();
        if (wateringSuccessNotice != null) wateringSuccessNotice.SetActive(false);
        completionReached = true;
        Debug.Log("Planting flow: watering can remained snapped to the correct target for three seconds.");
    }

    private void SelectSoil(int index)
    {
        selectedSoilIndex = index;
        soilAdded = false;
        if (soilSizeCycler != null) soilSizeCycler.SelectItem(index);
        Debug.Log($"Planting flow: selected soil index {index}, item={(soilSizeCycler != null && soilSizeCycler.currentItem != null ? soilSizeCycler.currentItem.name : "null")}");
        HideSoilFeedback();
    }

    private void ShowSoilFeedback(string message, bool isError = false)
    {
        if (soilFeedback == null) return;
        if (soilFeedbackRoutine != null) StopCoroutine(soilFeedbackRoutine);
        soilFeedback.text = message;
        soilFeedback.color = isError ? new Color(1f, 0.18f, 0.18f, soilFeedbackDefaultColor.a) : soilFeedbackDefaultColor;
        soilFeedback.gameObject.SetActive(true);
        soilFeedbackRoutine = StartCoroutine(HideSoilFeedbackAfterDelay(5f));
    }

    private IEnumerator HideSoilFeedbackAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (soilFeedback != null) soilFeedback.gameObject.SetActive(false);
        soilFeedbackRoutine = null;
    }

    private void HideSoilFeedback()
    {
        if (soilFeedbackRoutine != null)
        {
            StopCoroutine(soilFeedbackRoutine);
            soilFeedbackRoutine = null;
        }
        if (soilFeedback != null) soilFeedback.gameObject.SetActive(false);
    }

    public void OnSoilEnteredPot(Collider soilCollider)
    {
        if (soilCollider == null || soilAdded) return;
        GameObject selectedSoil = soilSizeCycler != null ? soilSizeCycler.currentItem : null;
        Transform soilTransform = soilCollider.transform;
        Rigidbody soilBody = soilCollider.attachedRigidbody;
        bool isSelectedSoil = selectedSoil != null &&
            (soilTransform == selectedSoil.transform || soilTransform.IsChildOf(selectedSoil.transform) ||
             (soilBody != null && (soilBody.transform == selectedSoil.transform || soilBody.transform.IsChildOf(selectedSoil.transform))));
        Debug.Log($"Planting flow: pot contact collider={soilCollider.name}, activeItem={(selectedSoil != null ? selectedSoil.name : "null")}, index={(soilSizeCycler != null ? soilSizeCycler.itemIndex : -1)}, matches={isSelectedSoil}");
        if (!isSelectedSoil) return;

        int correctSoilIndex = GetCorrectSoilIndex(plantedVariety);
        int selectedIndex = soilSizeCycler != null ? soilSizeCycler.itemIndex : -1;
        selectedSoil.SetActive(false);

        if (correctSoilIndex < 0)
        {
            ShowSoilFeedback("Plant a Grass, Flower, or Mushroom seed before adding soil.");
            return;
        }

        if (selectedIndex != correctSoilIndex)
        {
            ShowSoilFeedback($"Incorrect soil for {plantedVariety}. Try {SoilLabels[correctSoilIndex]}.", true);
            return;
        }

        soilAdded = true;
        ApplyPlantGrowthStage(1);
        ShowSoilFeedback($"Correct soil for {plantedVariety}. Soil added successfully. Continue with watering.");
    }

    private static int GetCorrectSoilIndex(string variety)
    {
        switch (variety)
        {
            case "Grass": return 0;
            case "Flower": return 1;
            case "Mushroom": return 2;
            default: return -1;
        }
    }

    private void ApplyPlantGrowthStage(int stage)
    {
        plantGrowthStage = Mathf.Max(plantGrowthStage, stage);
        if (!seedGrown || plantedPlant == null || plantedRenderers == null) return;
        if (plantScaleRoutine != null) StopCoroutine(plantScaleRoutine);
        float scaleMultiplier = plantGrowthStage >= 2 ? 1.8f : plantGrowthStage == 1 ? 1.5f : 1f;
        plantScaleRoutine = StartCoroutine(AnimatePlantScale(plantedBaseScale * scaleMultiplier));
    }

    private IEnumerator AnimatePlantScale(Vector3 targetScale)
    {
        Vector3 startingScale = plantedPlant.localScale;
        const float duration = 0.8f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            plantedPlant.localScale = Vector3.Lerp(startingScale, targetScale, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
            KeepPlantBaseAt(plantedBaseY);
            yield return null;
        }
        plantedPlant.localScale = targetScale;
        KeepPlantBaseAt(plantedBaseY);
        plantScaleRoutine = null;
    }

    private void KeepPlantBaseAt(float baseY)
    {
        Bounds bounds = GetRendererBounds(plantedRenderers);
        if (bounds.size != Vector3.zero)
            plantedPlant.position += Vector3.up * (baseY - bounds.min.y);
    }

    private void CreateCompletionOverlay()
    {
        EnsureUiEventSystem();
        GameObject canvasObject = new GameObject("Planting Finish Flow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        wateringSuccessNotice = new GameObject("Watering Complete", typeof(RectTransform), typeof(Image));
        wateringSuccessNotice.transform.SetParent(canvasObject.transform, false);
        RectTransform successRect = wateringSuccessNotice.GetComponent<RectTransform>();
        successRect.anchorMin = successRect.anchorMax = new Vector2(0.5f, 0.5f);
        successRect.anchoredPosition = Vector2.zero;
        successRect.sizeDelta = new Vector2(760, 180);
        wateringSuccessNotice.GetComponent<Image>().color = new Color(0.08f, 0.28f, 0.16f, 0.96f);

        GameObject successTextObject = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        successTextObject.transform.SetParent(wateringSuccessNotice.transform, false);
        RectTransform successTextRect = successTextObject.GetComponent<RectTransform>();
        successTextRect.anchorMin = Vector2.zero;
        successTextRect.anchorMax = Vector2.one;
        successTextRect.offsetMin = successTextRect.offsetMax = Vector2.zero;
        wateringSuccessText = successTextObject.GetComponent<TextMeshProUGUI>();
        wateringSuccessText.text = "Correct!";
        wateringSuccessText.fontSize = 48;
        wateringSuccessText.fontStyle = FontStyles.Bold;
        wateringSuccessText.alignment = TextAlignmentOptions.Center;
        wateringSuccessText.color = Color.white;
        wateringSuccessNotice.SetActive(false);

        completionOverlay = new GameObject("Finish Choices", typeof(RectTransform), typeof(Image));
        completionOverlay.transform.SetParent(canvasObject.transform, false);
        RectTransform panel = completionOverlay.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(900, 330);
        completionOverlay.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.10f, 0.96f);

        GameObject messageObject = new GameObject("Finish Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        messageObject.transform.SetParent(completionOverlay.transform, false);
        RectTransform messageRect = messageObject.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0.06f, 0.44f);
        messageRect.anchorMax = new Vector2(0.94f, 0.94f);
        messageRect.offsetMin = messageRect.offsetMax = Vector2.zero;
        TMP_Text message = messageObject.GetComponent<TextMeshProUGUI>();
        message.text = "Watering complete!\nChoose Restart to begin again or End to exit.";
        message.fontSize = 38;
        message.alignment = TextAlignmentOptions.Center;
        message.color = Color.white;

        overlayRestartButton = CreateCompletionButton("Restart", new Vector2(-170, -85), completionOverlay.transform, new Color(0.24f, 0.55f, 0.32f, 1f));
        overlayEndButton = CreateCompletionButton("End", new Vector2(170, -85), completionOverlay.transform, new Color(0.35f, 0.38f, 0.42f, 1f));
        overlayRestartButton.onClick.AddListener(RestartScene);
        overlayEndButton.onClick.AddListener(EndExperience);
        completionOverlay.SetActive(false);
    }

    private static void EnsureUiEventSystem()
    {
        EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
            eventSystem = new GameObject("Interaction Event System", typeof(EventSystem)).GetComponent<EventSystem>();

        InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null) inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        inputModule.enabled = true;
    }

    private static Button CreateCompletionButton(string label, Vector2 position, Transform parent, Color color)
    {
        GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = position;
        buttonRect.sizeDelta = new Vector2(280, 76);
        buttonObject.GetComponent<Image>().color = color;

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.55f, 0.75f, 0.58f, 1f);
        button.colors = colors;

        GameObject labelObject = new GameObject("Button Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        TMP_Text labelText = labelObject.GetComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 30;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = Color.white;
        return button;
    }

    private void HandleCompletionButtonPointer()
    {
        if (!completionReached || hasEnded || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame ||
            overlayRestartButton == null || overlayEndButton == null)
            return;

        Vector2 pointer = Mouse.current.position.ReadValue();
        RectTransform restartRect = overlayRestartButton.GetComponent<RectTransform>();
        RectTransform endRect = overlayEndButton.GetComponent<RectTransform>();
        if (RectTransformUtility.RectangleContainsScreenPoint(restartRect, pointer, null))
            RestartScene();
        else if (RectTransformUtility.RectangleContainsScreenPoint(endRect, pointer, null))
            EndExperience();
    }

    private void EndExperience()
    {
        hasEnded = true;
        if (completionOverlay != null) completionOverlay.SetActive(false);
        Application.Quit();
#if UNITY_EDITOR
        Debug.Log("End selected. Application.Quit is ignored in the Unity Editor.");
#endif
    }

    private void RestartScene()
    {
        hasEnded = true;
        if (completionOverlay != null) completionOverlay.SetActive(false);
        Scene current = SceneManager.GetActiveScene();
        string sceneToReload = string.IsNullOrEmpty(current.path) ? current.name : current.path;
        SceneManager.LoadScene(sceneToReload, LoadSceneMode.Single);
    }
}
