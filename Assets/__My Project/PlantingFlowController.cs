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
    private const int CorrectSoilIndex = 1; // 0.5 kg, 2 kg, 4 kg, 10 kg
    private ItemCycler soilSizeCycler;
    private TMP_Text soilFeedback;
    private GameObject completionMessage;
    private TMP_Text completionText;
    private GameObject completionOverlay;
    private Button overlayRestartButton;
    private Button overlayEndButton;
    private Transform potTarget;
    private bool soilAdded;
    private bool hasEnded;
    private bool completionReached;
    private int selectedSoilIndex = -1;

    // The interaction toolkit's trigger relay can miss colliders while the soil is
    // being held. Keep a bounds based fallback so placing the selected block in
    // the planter always completes the step.
    private const float PotPlacementRadius = 0.8f;

    private void Start()
    {
        foreach (ItemCycler cycler in FindObjectsByType<ItemCycler>())
        {
            if (cycler.gameObject.name.ToLowerInvariant().Contains("soil"))
                soilSizeCycler = cycler;
        }

        GameObject soilFeedbackObject = FindSceneObject("Text (correct soil");
        if (soilFeedbackObject != null)
        {
            soilFeedback = soilFeedbackObject.GetComponent<TMP_Text>();
            soilFeedbackObject.SetActive(false);
        }

        completionMessage = FindSceneObject("Text (correct watering");
        if (completionMessage != null)
        {
            completionText = completionMessage.GetComponent<TMP_Text>();
            completionMessage.SetActive(false);
        }

        CreateCompletionOverlay();
        HookSoilButtons();
        HookPotSoilReceiver();
    }

    private void Update()
    {
        if (completionMessage != null && completionMessage.activeInHierarchy)
        {
            if (completionText != null)
                completionText.text = "Planting complete!\nChoose Restart to return everything to the beginning.";
            completionReached = true;
            completionMessage.SetActive(false);
        }

        if (completionOverlay != null)
            completionOverlay.SetActive(completionReached && !hasEnded);
        HandleCompletionButtonPointer();

        // Keyboard fallback keeps the prompt dismissible if a browser or XR
        // player does not deliver pointer events to the overlay.
        if (completionReached && !hasEnded && Keyboard.current != null)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
                RestartScene();
            else if (Keyboard.current.escapeKey.wasPressedThisFrame)
                EndExperience();
        }

        if (soilAdded || soilSizeCycler == null || soilSizeCycler.itemIndex != CorrectSoilIndex || potTarget == null)
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
        string[] buttonNames = { "Button:0.5kg", "Button:2kg", "Button:4kg", "Button:10kg" };
        for (int index = 0; index < buttonNames.Length; index++)
        {
            GameObject button = GameObject.Find(buttonNames[index]);
            if (button == null)
            {
                Debug.LogWarning($"Planting flow: could not find soil option '{buttonNames[index]}'.");
                continue;
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

        Transform existing = potSnapper.transform.Find("Soil Pot Collision Zone");
        GameObject zone = existing != null ? existing.gameObject : new GameObject("Soil Pot Collision Zone");
        zone.transform.SetParent(potSnapper.transform, false);
        SphereCollider collider = zone.GetComponent<SphereCollider>();
        if (collider == null) collider = zone.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 0.65f;
        Rigidbody body = zone.GetComponent<Rigidbody>();
        if (body == null) body = zone.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        PotSoilCollisionRelay relay = zone.GetComponent<PotSoilCollisionRelay>();
        if (relay == null) relay = zone.AddComponent<PotSoilCollisionRelay>();
        relay.controller = this;
    }

    private void SelectSoil(int index)
    {
        selectedSoilIndex = index;
        soilAdded = false;
        if (soilSizeCycler != null) soilSizeCycler.SelectItem(index);
        Debug.Log($"Planting flow: selected soil index {index}, item={(soilSizeCycler != null && soilSizeCycler.currentItem != null ? soilSizeCycler.currentItem.name : "null")}");

        if (soilFeedback == null) return;
        soilFeedback.gameObject.SetActive(true);
        if (index == CorrectSoilIndex)
            soilFeedback.text = "2 kg selected — use the shovel to move the soil into the small pot on the left.";
        else
            soilFeedback.text = "That amount is not correct. Choose 2 kg, then use the shovel to add it to the pot.";
    }

    public void OnSoilEnteredPot(Collider soilCollider)
    {
        if (soilCollider == null || soilAdded) return;
        // The existing 2 kg prefab has no TriggerFilter component (only the
        // oversized 10 kg prefab does). Identify it by the cycler's active object.
        GameObject correctSoil = soilSizeCycler != null ? soilSizeCycler.currentItem : null;
        Transform soilTransform = soilCollider.transform;
        Rigidbody soilBody = soilCollider.attachedRigidbody;
        bool isSelectedSoil = correctSoil != null &&
            (soilTransform == correctSoil.transform || soilTransform.IsChildOf(correctSoil.transform) ||
             (soilBody != null && (soilBody.transform == correctSoil.transform || soilBody.transform.IsChildOf(correctSoil.transform))));
        Debug.Log($"Planting flow: pot contact collider={soilCollider.name}, activeItem={(correctSoil != null ? correctSoil.name : "null")}, index={(soilSizeCycler != null ? soilSizeCycler.itemIndex : -1)}, matches={isSelectedSoil}");
        if (!isSelectedSoil) return;
        if (soilSizeCycler == null || soilSizeCycler.itemIndex != CorrectSoilIndex)
        {
            if (soilFeedback != null)
            {
                soilFeedback.gameObject.SetActive(true);
                soilFeedback.text = "Choose 2 kg, then place that soil block in the small pot on the left.";
            }
            return;
        }

        soilAdded = true;
        correctSoil.SetActive(false);
        if (soilFeedback != null)
        {
            soilFeedback.gameObject.SetActive(false);
        }
        if (soilFeedback != null)
        {
            soilFeedback.gameObject.SetActive(true);
            soilFeedback.text = "Soil added successfully. Continue with watering.";
        }
    }

    private void CreateCompletionOverlay()
    {
        EnsureUiEventSystem();
        GameObject canvasObject = new GameObject("Planting Completion Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        completionOverlay = new GameObject("Completion Choices", typeof(RectTransform), typeof(Image));
        completionOverlay.transform.SetParent(canvasObject.transform, false);
        RectTransform panel = completionOverlay.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(900, 330);
        completionOverlay.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.10f, 0.96f);

        GameObject messageObject = new GameObject("Completion Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        messageObject.transform.SetParent(completionOverlay.transform, false);
        RectTransform messageRect = messageObject.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0.06f, 0.44f);
        messageRect.anchorMax = new Vector2(0.94f, 0.94f);
        messageRect.offsetMin = messageRect.offsetMax = Vector2.zero;
        TMP_Text message = messageObject.GetComponent<TextMeshProUGUI>();
        message.text = "Planting complete!\nChoose Restart to return everything to the beginning.";
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
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
            eventSystem = new GameObject("Planting UI EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

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

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
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
