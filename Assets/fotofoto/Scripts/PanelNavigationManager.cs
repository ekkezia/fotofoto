using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages navigation between instruction panels
/// Activates the selected panel and deactivates all others
/// </summary>
public class PanelNavigationManager : MonoBehaviour
{
    [Header("Instruction Panels")]
    [SerializeField] public GameObject navigationCanvas;
    [SerializeField] private GameObject watermarkPanel;
    [SerializeField] private GameObject soloFotoPanel;
    [SerializeField] private GameObject coFotoPanel;
    [SerializeField] private GameObject remixFotoPanel;
    [SerializeField] private GameObject loadFotoPanel;
    [SerializeField] private GameObject saveFotoPanel;
    [SerializeField] private GameObject printFotoPanel;

    [Header("Current State")]
    [SerializeField] private InstructionManager.InstructionPanel currentPanel = InstructionManager.InstructionPanel.None;
    private InstructionManager.InstructionPanel previousPanel = InstructionManager.InstructionPanel.None; // Track previous panel for Save Panel

    [Header("Sound Effects")]
    [SerializeField] private AudioClip buttonClickSound;
    private AudioSource _audioSource;

    [Header("Capture Systems")]
    [SerializeField] private LiveMaskAndCapture liveMaskAndCapture;
    [SerializeField] private QRCodeDetection qrCodeDetection;

    [Header("Save Panel")]
    [SerializeField] private TMP_InputField fileNameInputField; // Assign the input field from Save Panel
    [SerializeField] private UnityEngine.UI.Button saveButton; // The save button to disable
    [SerializeField] private UnityEngine.UI.Image saveButtonImage; // The image component of the save button
    [SerializeField] private Sprite saveButtonNormalSprite; // Normal save button sprite
    [SerializeField] private Sprite saveButtonSavedSprite; // "Saved!" confirmation sprite
    [SerializeField] private float saveButtonCooldown = 5f; // Cooldown time in seconds

    [Header("Navigation Buttons")]
    [SerializeField] private UnityEngine.UI.Button soloFotoButton;
    [SerializeField] private UnityEngine.UI.Button coFotoButton;
    [SerializeField] private UnityEngine.UI.Button remixFotoButton;
    [SerializeField] private UnityEngine.UI.Button loadFotoButton;
    [SerializeField] private UnityEngine.UI.Button saveFotoButton;
    [SerializeField] private UnityEngine.UI.Button printFotoButton;

    // Store original normal sprites for each button
    private Dictionary<UnityEngine.UI.Button, Sprite> originalButtonSprites = new Dictionary<UnityEngine.UI.Button, Sprite>();

    [Header("Load Panel")]
    [SerializeField] private Transform loadPanelScrollContent; // The Content object inside the Scroll View
    [SerializeField] private GameObject fileButtonPrefab; // Prefab for the file name button (assign in Unity)
    [SerializeField] private Transform loadedPlanesParent; // Parent transform for loaded planes (optional, for organization)

    private List<GameObject> currentlyLoadedPlanes = new List<GameObject>(); // Track loaded planes for cleanup

    [Header("Hand Tracking & Menu Visibility")]
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;
    [SerializeField] private HandGestureDetector handGestureDetector;
    [SerializeField] private float noLShapeDelay = 2f; // Delay before showing menu when no L-shape detected

    private float noLShapeTimer = 0f;
    private bool menuVisible = false;

    private void Awake()
    {
        Debug.LogError("[PanelNavigationManager] ★★★ AWAKE CALLED ★★★");

        // Setup AudioSource
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D sound

        Debug.Log("[PanelNavigationManager] AudioSource initialized");
    }

    private void Start()
    {
        Debug.LogError("[PanelNavigationManager] ★★★ START CALLED ★★★");
        Debug.Log("[PanelNavigationManager] Initialized");

if (liveMaskAndCapture != null)
{
    Debug.LogError($"[PanelNavigationManager] ★★★ REFERENCED INSTANCE: {liveMaskAndCapture.gameObject.name} (InstanceID: {liveMaskAndCapture.GetInstanceID()}) ★★★");
}

        // Verify hand references
        if (leftHand == null)
        {
            Debug.LogError("[PanelNavigationManager] ❌ Left OVRHand is NOT assigned in Inspector!");
        }
        else
        {
            Debug.Log("[PanelNavigationManager] ✓ Left OVRHand assigned");
        }

        if (rightHand == null)
        {
            Debug.LogError("[PanelNavigationManager] ❌ Right OVRHand is NOT assigned in Inspector!");
        }
        else
        {
            Debug.Log("[PanelNavigationManager] ✓ Right OVRHand assigned");
        }

        if (handGestureDetector == null)
        {
            Debug.LogWarning("[PanelNavigationManager] ⚠️ HandGestureDetector is NOT assigned - L-shape detection disabled!");
        }
        else
        {
            Debug.Log("[PanelNavigationManager] ✓ HandGestureDetector assigned");

            // Subscribe to frame events for menu visibility control
            // OnFrameActive = SoloFoto, OnQRFrameActive = RemixFoto
            handGestureDetector.OnFrameActive += OnFrameBecameActive;
            handGestureDetector.OnQRFrameActive += OnFrameBecameActive; // ALSO hide instructions for RemixFoto!
            handGestureDetector.OnFrameInactive += OnFrameBecameInactive;
            Debug.Log("[PanelNavigationManager] ✓ Subscribed to HandGestureDetector events (both SoloFoto and RemixFoto)");
        }

        // Store original button sprites before we start changing them
        StoreOriginalButtonSprites();

        // Start with all panels inactive
        DeactivateAllPanels();

        // Set panel to Watermark on Init
        SetActivePanel(InstructionManager.InstructionPanel.Watermark);
    }

    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (handGestureDetector != null)
        {
            handGestureDetector.OnFrameActive -= OnFrameBecameActive;
            handGestureDetector.OnQRFrameActive -= OnFrameBecameActive;
            handGestureDetector.OnFrameInactive -= OnFrameBecameInactive;
        }
    }

    // Event handlers for HandGestureDetector
    private void OnFrameBecameActive(Vector3 pos, Quaternion rot, Vector3 scale)
    {
        Debug.LogError("[PanelNavigationManager] ★★★ OnFrameActive EVENT - L-shape CORRECT, resetting timer and hiding menu");
        noLShapeTimer = 0f;

        // Immediately hide menu when L-shape becomes correct
        if (menuVisible)
        {
            menuVisible = false;
            SetInstructionVisibility(false);
            Debug.LogError("[PanelNavigationManager] ★★★ Menu HIDDEN immediately (L-shape active) ★★★");
        }
    }

    private void OnFrameBecameInactive()
    {
        Debug.LogError("[PanelNavigationManager] ★★★ OnFrameInactive EVENT - L-shape LOST, starting timer");
        // Timer will start incrementing in HandleInstructionVisibility
        noLShapeTimer = 0f; // Reset timer when frame becomes inactive
    }

    private void Update()
    {
        Debug.LogError("[PanelNavigationManager] ★★★ UPDATE CALLED ★★★");

        // Get total capture count
        int totalCaptures = GetTotalCaptureCount();

        // Get visible hand count
        int visibleHands = GetVisibleHandCount();

        Debug.Log($"[PanelNavigationManager] UPDATE - Captures: {totalCaptures}, VisibleHands: {visibleHands}");

        // Update navigation button states based on current panel and capture count
        UpdateNavigationButtonStates(totalCaptures);

        // Handle visibility for all "Instruction" tagged objects (menu + instructions)
        HandleInstructionVisibility(totalCaptures, visibleHands);
    }

    // ========================================
    // BUTTON CALLBACKS - Attach these to your UI buttons!
    // ========================================

    /// <summary>
    /// Button callback: Show Solo Foto panel
    /// </summary>
    public void OnSelectSoloFoto()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectSoloFoto button clicked");
        SetActivePanel(InstructionManager.InstructionPanel.SoloFoto);
    }

    /// <summary>
    /// Button callback: Show Co Foto panel
    /// </summary>
    public void OnSelectCoFoto()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectCoFoto button clicked");
        SetActivePanel(InstructionManager.InstructionPanel.CoFoto);
    }

    /// <summary>
    /// Button callback: Show Remix Foto panel
    /// </summary>
    public void OnSelectRemixFoto()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectRemixFoto button clicked");
        SetActivePanel(InstructionManager.InstructionPanel.RemixFoto);
    }

    /// <summary>
    /// Button callback: Show Print Foto panel
    /// </summary>
    public void OnSelectPrintFoto()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectPrintFoto button clicked");
        SetActivePanel(InstructionManager.InstructionPanel.PrintFoto);
    }

    /// <summary>
    /// Button callback: Show Load Foto panel
    /// </summary>
    public void OnSelectLoadFoto()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectLoadFoto button clicked");
        SetActivePanel(InstructionManager.InstructionPanel.LoadFoto);
    }

    /// <summary>
    /// Button callback: Navigate to Save Foto panel
    /// </summary>
    public void OnSelectSaveFoto()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectSaveFoto button clicked");
        SetActivePanel(InstructionManager.InstructionPanel.SaveFoto);
    }

    /// <summary>
    /// Button callback: Execute save - Call this from the Save button in Save Panel
    /// IMPORTANT: Make sure to assign this method to the Save button's onClick event in Unity Inspector
    /// </summary>
    public void OnExecuteSave()
    {
        Debug.LogError("[PanelNavigationManager] ★★★★★★ OnExecuteSave button clicked ★★★★★★");

        // Check if button is already disabled (cooldown active)
        if (saveButton != null && !saveButton.interactable)
        {
            Debug.Log("[PanelNavigationManager] Save button on cooldown - ignoring click");
            return;
        }

        // Play button click sound
        PlayButtonClickSound();

        // Get custom file name from input field
        string customFileName = null;
        if (fileNameInputField != null && !string.IsNullOrWhiteSpace(fileNameInputField.text))
        {
            customFileName = fileNameInputField.text.Trim();
            Debug.LogError($"[PanelNavigationManager] Using custom file name: '{customFileName}'");
        }
        else
        {
            // Generate unique default name with timestamp
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            customFileName = $"Image_{timestamp}";
            Debug.LogError($"[PanelNavigationManager] No custom file name provided, using default: '{customFileName}'");
        }

        // Save all captured images
        int savedCount = 0;

        Debug.LogError($"[PanelNavigationManager] liveMaskAndCapture null? {liveMaskAndCapture == null}");

        if (liveMaskAndCapture != null)
        {
            int count = liveMaskAndCapture.SaveAllCaptures(customFileName);
            savedCount += count;
            Debug.LogError($"[PanelNavigationManager] ✓ Saved {count} Solo Foto images");
        }
        else
        {
            Debug.LogError("[PanelNavigationManager] ❌❌❌ LiveMaskAndCapture reference NOT ASSIGNED in Inspector! ❌❌❌");
        }

        // TODO: Also save QR Code captures if needed
        if (qrCodeDetection != null)
        {
            int count = qrCodeDetection.SaveAllCaptures(customFileName);
            savedCount += count;
        }

        Debug.LogError($"[PanelNavigationManager] ✅ Total images saved: {savedCount}");
        Debug.LogError($"[PanelNavigationManager] Save location: {Application.persistentDataPath}");

        // Clear the input field for next save
        if (fileNameInputField != null)
        {
            fileNameInputField.text = "";
        }

        // Start cooldown coroutine to change button appearance and disable it
        Debug.LogError($"[PanelNavigationManager] savedCount = {savedCount}, starting cooldown? {savedCount > 0}");

        if (savedCount > 0)
        {
            Debug.LogError("[PanelNavigationManager] ★★★ STARTING SAVE BUTTON COOLDOWN COROUTINE");
            StartCoroutine(SaveButtonCooldownRoutine());
        }
        else
        {
            Debug.LogError("[PanelNavigationManager] ❌ NOT starting cooldown - savedCount is 0!");
        }
    }

    /// <summary>
    /// Coroutine: Change save button to "saved" state for 5 seconds, then restore
    /// </summary>
    private System.Collections.IEnumerator SaveButtonCooldownRoutine()
    {
        Debug.LogError("[PanelNavigationManager] ★★★★★★ SAVE BUTTON COOLDOWN COROUTINE STARTED ★★★★★★");

        // Disable the button to prevent multiple clicks
        if (saveButton != null)
        {
            saveButton.interactable = false;
            Debug.LogError("[PanelNavigationManager] ✓ Save button DISABLED");
        }
        else
        {
            Debug.LogError("[PanelNavigationManager] ❌ saveButton is NULL - not assigned in Inspector!");
        }

        // Change button image to "saved" sprite
        Debug.LogError($"[PanelNavigationManager] saveButtonImage null? {saveButtonImage == null}");
        Debug.LogError($"[PanelNavigationManager] saveButtonSavedSprite null? {saveButtonSavedSprite == null}");

        if (saveButtonImage != null && saveButtonSavedSprite != null)
        {
            saveButtonImage.sprite = saveButtonSavedSprite;
            Debug.LogError("[PanelNavigationManager] ✓✓✓ Save button image changed to SAVED sprite");
        }
        else
        {
            Debug.LogError("[PanelNavigationManager] ❌ Cannot change sprite - saveButtonImage or saveButtonSavedSprite not assigned!");
        }

        // Wait for cooldown period
        yield return new UnityEngine.WaitForSeconds(saveButtonCooldown);

        // Restore button to normal state
        if (saveButtonImage != null && saveButtonNormalSprite != null)
        {
            saveButtonImage.sprite = saveButtonNormalSprite;
            Debug.Log("[PanelNavigationManager] ✓ Save button image restored to NORMAL sprite");
        }

        // Re-enable the button
        if (saveButton != null)
        {
            saveButton.interactable = true;
            Debug.Log("[PanelNavigationManager] ✓ Save button ENABLED (cooldown complete)");
        }

        Debug.Log($"[PanelNavigationManager] ★★★ Save button cooldown complete after {saveButtonCooldown}s");
    }

    // ========================================
    // LOAD PANEL FUNCTIONALITY
    // ========================================

    /// <summary>
    /// Populate the Load Panel scroll view with saved SCENE names (metadata files)
    /// Call this when the Load Panel is opened
    /// </summary>
    public void PopulateLoadPanelFileList()
    {
        StartCoroutine(PopulateLoadPanelFileListCoroutine());
    }

    private System.Collections.IEnumerator PopulateLoadPanelFileListCoroutine()
    {
        Debug.LogError("[PanelNavigationManager] ★★★★★★ POPULATING LOAD PANEL FILE LIST ★★★★★★");

        if (loadPanelScrollContent == null)
        {
            Debug.LogError("[PanelNavigationManager] ❌ Load Panel Scroll Content not assigned!");
            yield break;
        }

        if (fileButtonPrefab == null)
        {
            Debug.LogError("[PanelNavigationManager] ❌ File Button Prefab not assigned!");
            yield break;
        }

        // Clear existing buttons
        int childCount = loadPanelScrollContent.childCount;
        Debug.LogError($"[PanelNavigationManager] Clearing {childCount} existing buttons");

        // Store children in list first (foreach doesn't work well with Destroy)
        List<GameObject> childrenToDestroy = new List<GameObject>();
        foreach (Transform child in loadPanelScrollContent)
        {
            childrenToDestroy.Add(child.gameObject);
        }

        // Now destroy them
        foreach (GameObject child in childrenToDestroy)
        {
            DestroyImmediate(child); // Use DestroyImmediate for UI to ensure they're gone
        }

        Debug.LogError($"[PanelNavigationManager] Child count after clearing: {loadPanelScrollContent.childCount}");

        // Get all METADATA JSON files from persistent data path
        string savePath = Application.persistentDataPath;
        Debug.LogError($"[PanelNavigationManager] Save path: {savePath}");

        if (!System.IO.Directory.Exists(savePath))
        {
            Debug.LogError($"[PanelNavigationManager] ❌ Save directory does NOT exist: {savePath}");
            yield break;
        }

        Debug.LogError($"[PanelNavigationManager] ✓ Save directory exists");

        // Ensure scroll content has a Vertical Layout Group for proper button stacking
        UnityEngine.UI.VerticalLayoutGroup layoutGroup = loadPanelScrollContent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (layoutGroup == null)
        {
            layoutGroup = loadPanelScrollContent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            Debug.LogError("[PanelNavigationManager] ✓ Added VerticalLayoutGroup to scroll content");
        }
        else
        {
            Debug.LogError("[PanelNavigationManager] ✓ VerticalLayoutGroup already exists");
        }

        // Configure layout settings for proper spacing
        layoutGroup.spacing = 15f; // 15px gap between buttons
        layoutGroup.childForceExpandHeight = false; // Don't stretch button height
        layoutGroup.childForceExpandWidth = true; // Stretch to fill width
        layoutGroup.childControlHeight = true; // Control height (use button's 60px)
        layoutGroup.childControlWidth = true; // Control width
        layoutGroup.padding = new RectOffset(10, 10, 10, 10); // 10px padding on all sides

        Debug.LogError($"[PanelNavigationManager] Layout configured: spacing={layoutGroup.spacing}, padding={layoutGroup.padding.top}");

        // Ensure ContentSizeFitter for dynamic height
        UnityEngine.UI.ContentSizeFitter sizeFitter = loadPanelScrollContent.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (sizeFitter == null)
        {
            sizeFitter = loadPanelScrollContent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            sizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            Debug.LogError("[PanelNavigationManager] ✓ Added ContentSizeFitter to scroll content");
        }
        else
        {
            Debug.LogError("[PanelNavigationManager] ✓ ContentSizeFitter already exists");
        }

        // Try multiple search patterns to catch all metadata files
        string[] searchPatterns = new string[] { "*_metadata.json", "*metadata.json" };
        List<string> allJsonFiles = new List<string>();

        foreach (string pattern in searchPatterns)
        {
            try
            {
                string[] files = System.IO.Directory.GetFiles(savePath, pattern);
                Debug.LogError($"[PanelNavigationManager] Pattern '{pattern}' found {files.Length} files");
                allJsonFiles.AddRange(files);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PanelNavigationManager] ❌ Error searching with pattern '{pattern}': {e.Message}");
            }
        }

        // Remove duplicates
        string[] jsonFiles = allJsonFiles.Distinct().ToArray();
        Debug.LogError($"[PanelNavigationManager] ★★★ TOTAL UNIQUE FILES FOUND: {jsonFiles.Length} ★★★");

        // Debug: Log ALL files in directory to see what's actually there
        try
        {
            string[] allFiles = System.IO.Directory.GetFiles(savePath);
            Debug.LogError($"[PanelNavigationManager] Total files in directory: {allFiles.Length}");
            foreach (string file in allFiles)
            {
                string fileName = System.IO.Path.GetFileName(file);
                bool isMetadata = fileName.Contains("metadata");
                Debug.LogError($"[PanelNavigationManager] File: {fileName} (metadata: {isMetadata})");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] ❌ Error listing all files: {e.Message}");
        }

        // Log each metadata file found
        foreach (string file in jsonFiles)
        {
            Debug.LogError($"[PanelNavigationManager] ★ Metadata file: {file}");
        }

        if (jsonFiles.Length == 0)
        {
            Debug.LogError("[PanelNavigationManager] ❌ No saved scenes found - creating 'No Files' message");
            CreateNoFilesMessage();
            yield break;
        }

        Debug.LogError($"[PanelNavigationManager] ★★★ Creating {jsonFiles.Length} buttons ★★★");
        Debug.LogError($"[PanelNavigationManager] loadPanelScrollContent child count BEFORE: {loadPanelScrollContent.childCount}");

        // Create a button for each scene (metadata file)
        int buttonCount = 0;
        foreach (string jsonPath in jsonFiles)
        {
            string fileName = System.IO.Path.GetFileName(jsonPath);

            // Remove "_metadata.json" or "metadata.json" to get the scene name
            string sceneName = fileName.Replace("_metadata.json", "").Replace("metadata.json", "");

            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Creating button for scene: '{sceneName}'");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] From file: '{fileName}'");

            // Instantiate button from prefab
            GameObject buttonObj = Instantiate(fileButtonPrefab, loadPanelScrollContent);
            buttonObj.name = $"LoadButton_{sceneName}"; // Give it a meaningful name

            RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();

            // Set proper button size - LayoutGroup will handle positioning
            // Using LayoutElement to enforce size instead of manual anchors
            UnityEngine.UI.LayoutElement layoutElement = buttonObj.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.minHeight = 50; // Minimum height 50px (reduced from 70)
            layoutElement.preferredHeight = 50; // Preferred height 50px (reduced from 70)
            layoutElement.flexibleHeight = 0; // Don't stretch vertically

            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ========== BUTTON SETUP START ==========");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Name: {buttonObj.name}");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Active: {buttonObj.activeSelf}");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Parent: {buttonObj.transform.parent.name}");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] LayoutElement: minHeight={layoutElement.minHeight}, preferredHeight={layoutElement.preferredHeight}");

            // Get or add Image component for the background
            UnityEngine.UI.Image buttonImage = buttonObj.GetComponent<UnityEngine.UI.Image>();

            if (buttonImage == null)
            {
                buttonImage = buttonObj.AddComponent<UnityEngine.UI.Image>();
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Added Image component to button");
            }
            else
            {
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ✓ Found Image component on button");
            }

            try
            {
                // Create a simple white sprite (required for Image component)
                Texture2D tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();

                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
                buttonImage.sprite = sprite;
                buttonImage.type = UnityEngine.UI.Image.Type.Simple;

                // Set the normal background color (will change on hover)
                buttonImage.color = new Color(0.3f, 0.3f, 0.3f, 1.0f); // Dark gray background

                // CRITICAL: Enable raycast target so button can receive clicks
                buttonImage.raycastTarget = true;

                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Set button background color: {buttonImage.color}");
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Image raycastTarget: {buttonImage.raycastTarget}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ❌ Error setting button image: {e.Message}");
            }

            // Set button text - ALWAYS create fresh text to ensure it shows properly
            try
            {
                // First, try to find existing text and just update it
                TMP_Text buttonText = buttonObj.GetComponentInChildren<TMP_Text>();

                if (buttonText != null)
                {
                    // Found existing text - just update it
                    buttonText.text = sceneName;
                    buttonText.fontSize = 16; // Smaller font size (reduced from 20)
                    buttonText.color = Color.white;
                    buttonText.alignment = TextAlignmentOptions.Center;
                    buttonText.enableAutoSizing = false;
                    buttonText.fontStyle = FontStyles.Normal; // Regular weight

                    Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ✓ Updated existing TMP_Text to: '{sceneName}'");
                }
                else
                {
                    // No text found - create new one
                    GameObject textObj = new GameObject("ButtonText");
                    textObj.transform.SetParent(buttonObj.transform, false);

                    TMP_Text newText = textObj.AddComponent<TextMeshProUGUI>();
                    newText.text = sceneName;
                    newText.fontSize = 16; // Smaller font size (reduced from 20)
                    newText.color = Color.white;
                    newText.alignment = TextAlignmentOptions.Center;
                    newText.enableAutoSizing = false;
                    newText.fontStyle = FontStyles.Normal; // Regular weight

                    // Make text fill the button
                    RectTransform textRect = textObj.GetComponent<RectTransform>();
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = Vector2.one;
                    textRect.offsetMin = new Vector2(15, 10); // Padding
                    textRect.offsetMax = new Vector2(-15, -10);

                    Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ✓ Created new TMP_Text: '{sceneName}'");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ❌ Error creating button text: {e.Message}\n{e.StackTrace}");
            }

            // Capture file path for closure (must be outside if/else)
            string jsonPathCopy = jsonPath;

            // Add click listener and configure button
            UnityEngine.UI.Button button = buttonObj.GetComponent<UnityEngine.UI.Button>();
            if (button == null)
            {
                button = buttonObj.AddComponent<UnityEngine.UI.Button>();
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] Added Button component");
            }

            // Clear existing listeners first
            button.onClick.RemoveAllListeners();

            // Add click listener
            button.onClick.AddListener(() => {
                Debug.LogError($"[PanelNavigationManager] ★★★ BUTTON CLICKED: {jsonPathCopy} ★★★");
                OnLoadSceneClicked(jsonPathCopy);
            });

            // CRITICAL: Set the Image as the button's target graphic (this makes hover work!)
            if (buttonImage != null)
            {
                button.targetGraphic = buttonImage;
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ✓ Set targetGraphic to Image");
            }
            else
            {
                Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ❌ Cannot set targetGraphic - buttonImage is null!");
            }

            // Configure button colors - these will replace the Image's color on hover
            UnityEngine.UI.ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.3f, 0.3f, 0.3f, 1f); // Dark gray (normal state)
            colors.highlightedColor = new Color(0.2f, 0.5f, 0.9f, 1f); // Bright blue (on hover)
            colors.pressedColor = new Color(0.15f, 0.4f, 0.7f, 1f); // Darker blue (when clicked)
            colors.selectedColor = new Color(0.25f, 0.55f, 0.85f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.15f; // Smooth transition
            button.colors = colors;

            // Set transition mode to ColorTint (required for colors to work)
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;

            // Enable interaction
            button.interactable = true;

            // Add thin border using Outline component
            UnityEngine.UI.Outline outline = buttonObj.GetComponent<UnityEngine.UI.Outline>();
            if (outline == null)
            {
                outline = buttonObj.AddComponent<UnityEngine.UI.Outline>();
            }
            outline.effectColor = new Color(0.6f, 0.6f, 0.6f, 1f); // Light gray border
            outline.effectDistance = new Vector2(1, -1); // Thin border (1px)
            outline.useGraphicAlpha = true;

            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ✓ Added thin border (Outline)");

            Debug.LogError($"[PanelNavigationManager] [{buttonCount}] ✓✓✓ Button configured:");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}]   - targetGraphic: {button.targetGraphic != null}");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}]   - transition: {button.transition}");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}]   - interactable: {button.interactable}");
            Debug.LogError($"[PanelNavigationManager] [{buttonCount}]   - colors: normal={colors.normalColor}, highlight={colors.highlightedColor}");

            buttonCount++;
        }

        Debug.LogError($"[PanelNavigationManager] ★★★★★★ CREATED {buttonCount} SCENE BUTTONS ★★★★★★");
        Debug.LogError($"[PanelNavigationManager] loadPanelScrollContent child count AFTER: {loadPanelScrollContent.childCount}");

        // Ensure the parent canvas has a GraphicRaycaster for button clicks to work
        Canvas parentCanvas = loadPanelScrollContent.GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            UnityEngine.UI.GraphicRaycaster raycaster = parentCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster == null)
            {
                raycaster = parentCanvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                Debug.LogError("[PanelNavigationManager] ✓ Added GraphicRaycaster to parent Canvas");
            }
            else
            {
                Debug.LogError("[PanelNavigationManager] ✓ GraphicRaycaster exists on parent Canvas");
            }
        }

        // Wait one frame for buttons to be fully instantiated
        yield return null;

        // List all children to verify they exist
        for (int i = 0; i < loadPanelScrollContent.childCount; i++)
        {
            Transform child = loadPanelScrollContent.GetChild(i);
            Debug.LogError($"[PanelNavigationManager] Child {i}: {child.name} (active: {child.gameObject.activeSelf})");
        }

        // Force layout rebuild
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(loadPanelScrollContent.GetComponent<RectTransform>());
        Debug.LogError("[PanelNavigationManager] ✓ Forced layout rebuild");
    }

    /// <summary>
    /// Create a "No Files Found" message when no saves exist
    /// </summary>
    private void CreateNoFilesMessage()
    {
        GameObject messageObj = new GameObject("NoFilesMessage");
        messageObj.transform.SetParent(loadPanelScrollContent, false);

        TMP_Text messageText = messageObj.AddComponent<TextMeshProUGUI>();
        messageText.text = "No saved captures found.\nCapture some images first!";
        messageText.fontSize = 24;
        messageText.alignment = TextAlignmentOptions.Center;
        messageText.color = Color.white;

        RectTransform rt = messageObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400, 100);

        Debug.LogError("[PanelNavigationManager] ✓ Created 'No Files' message");
    }

    /// <summary>
    /// Clear all previously loaded planes to prevent stacking
    /// </summary>
    private void ClearLoadedPlanes()
    {
        Debug.LogError($"[PanelNavigationManager] Clearing {currentlyLoadedPlanes.Count} previously loaded planes");

        foreach (GameObject plane in currentlyLoadedPlanes)
        {
            if (plane != null)
            {
                Destroy(plane);
            }
        }

        currentlyLoadedPlanes.Clear();
        Debug.LogError("[PanelNavigationManager] ✓ All previously loaded planes cleared");
    }

    /// <summary>
    /// Called when a scene name button is clicked in the Load Panel
    /// Loads the entire scene with all images at their saved positions
    /// </summary>
    private void OnLoadSceneClicked(string metadataPath)
    {
        Debug.LogError($"[PanelNavigationManager] ★★★ Load scene clicked: {metadataPath}");

        PlayButtonClickSound();

        // Clear previously loaded planes first
        ClearLoadedPlanes();

        if (!System.IO.File.Exists(metadataPath))
        {
            Debug.LogError($"[PanelNavigationManager] ❌ Metadata file not found: {metadataPath}");
            return;
        }

        try
        {
            // Read metadata JSON
            string json = System.IO.File.ReadAllText(metadataPath);
            LiveMaskAndCapture.SceneData sceneData = JsonUtility.FromJson<LiveMaskAndCapture.SceneData>(json);

            if (sceneData == null || sceneData.images == null || sceneData.images.Count == 0)
            {
                Debug.LogError("[PanelNavigationManager] ❌ Invalid or empty scene data");
                return;
            }

            Debug.LogError($"[PanelNavigationManager] ✓ Loaded scene metadata with {sceneData.images.Count} images");

            // Load each image with its metadata
            string savePath = Application.persistentDataPath;
            int loadedCount = 0;

            foreach (var metadata in sceneData.images)
            {
                string imagePath = System.IO.Path.Combine(savePath, metadata.imagePath);

                if (!System.IO.File.Exists(imagePath))
                {
                    Debug.LogError($"[PanelNavigationManager] ❌ Image file not found: {imagePath}");
                    continue;
                }

                // Read image file
                byte[] fileData = System.IO.File.ReadAllBytes(imagePath);
                Texture2D texture = new Texture2D(2, 2);

                if (texture.LoadImage(fileData))
                {
                    // Create plane at saved position with saved rotation and scale
                    GameObject loadedPlane = CreateLoadedPlane(texture, metadata.position, metadata.rotation, metadata.scale);

                    // Add to tracking list for cleanup
                    currentlyLoadedPlanes.Add(loadedPlane);

                    // Optionally parent it
                    if (loadedPlanesParent != null)
                    {
                        loadedPlane.transform.SetParent(loadedPlanesParent);
                    }

                    loadedCount++;
                    Debug.LogError($"[PanelNavigationManager] ✓ Loaded: {metadata.imagePath} at position {metadata.position}");
                }
                else
                {
                    Debug.LogError($"[PanelNavigationManager] ❌ Failed to load texture from: {imagePath}");
                    Destroy(texture);
                }
            }

            Debug.LogError($"[PanelNavigationManager] ✅✅✅ Scene loaded: {loadedCount}/{sceneData.images.Count} images");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] ❌ Failed to load scene: {e.Message}");
            Debug.LogError($"[PanelNavigationManager] Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Display the loaded texture by spawning a 3D plane in front of the camera
    /// Similar to how LiveMaskAndCapture creates captured planes
    /// </summary>
    private void DisplayLoadedImage(Texture2D texture)
    {
        // Get camera reference
        Camera cam = Camera.main;
        if (cam == null)
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
                cam = rig.centerEyeAnchor.GetComponent<Camera>();
        }

        if (cam == null)
        {
            Debug.LogError("[PanelNavigationManager] ❌ No camera found!");
            return;
        }

        // Calculate aspect ratio from texture
        float aspectRatio = (float)texture.width / texture.height;

        // Default size - make it reasonable for viewing
        float planeHeight = 0.3f; // 30cm tall
        float planeWidth = planeHeight * aspectRatio;

        // Position in front of camera
        float distanceFromCamera = 0.5f; // 50cm in front
        Vector3 spawnPosition = cam.transform.position + cam.transform.forward * distanceFromCamera;
        Quaternion spawnRotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
        Vector3 planeScale = new Vector3(planeWidth, planeHeight, 1f);

        // Create the plane GameObject
        GameObject loadedPlane = CreateLoadedPlane(texture, spawnPosition, spawnRotation, planeScale);

        // Optionally parent it for organization
        if (loadedPlanesParent != null)
        {
            loadedPlane.transform.SetParent(loadedPlanesParent);
        }

        Debug.Log($"[PanelNavigationManager] ✓ Spawned loaded image plane at {spawnPosition}");
        Debug.Log($"[PanelNavigationManager] Plane size: {planeWidth:F3}x{planeHeight:F3}m (aspect ratio: {aspectRatio:F2})");
    }

    /// <summary>
    /// Create a 3D plane with the loaded texture
    /// Based on LiveMaskAndCapture.CreateCapturedPiece
    /// </summary>
    private GameObject CreateLoadedPlane(Texture2D texture, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        GameObject plane = new GameObject($"LoadedImage_{System.IO.Path.GetRandomFileName()}");

        // Set transform
        plane.transform.rotation = rotation;
        plane.transform.position = position;
        plane.transform.localScale = scale;

        // Create mesh
        MeshFilter mf = plane.AddComponent<MeshFilter>();
        MeshRenderer mr = plane.AddComponent<MeshRenderer>();

        mf.mesh = CreateQuadMesh(new Vector2(1f, 1f)); // 1x1 quad, scaling handled by transform

        // Create material
        Shader shader = Shader.Find("Unlit/Texture") ??
                        Shader.Find("Mobile/Unlit (Supports Lightmap)") ??
                        Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogError("[PanelNavigationManager] No suitable unlit shader found! Falling back to Standard");
            shader = Shader.Find("Standard");
        }

        Material planeMat = new Material(shader);
        planeMat.mainTexture = texture;

        // Disable backface culling so visible from both sides
        planeMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        // Set render queue to render on top of passthrough
        planeMat.renderQueue = 3000;

        mr.material = planeMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // Toggle renderer to force refresh
        mr.enabled = false;
        mr.enabled = true;

        Debug.Log($"[PanelNavigationManager] ✓ Created loaded plane - Texture: {texture.width}x{texture.height}");

        return plane;
    }

    /// <summary>
    /// Create a simple quad mesh for the plane
    /// </summary>
    private Mesh CreateQuadMesh(Vector2 size)
    {
        Mesh mesh = new Mesh();
        mesh.name = "LoadedImageQuad";

        float halfW = size.x * 0.5f;
        float halfH = size.y * 0.5f;

        // Vertices
        mesh.vertices = new Vector3[]
        {
            new Vector3(-halfW, -halfH, 0),
            new Vector3( halfW, -halfH, 0),
            new Vector3(-halfW,  halfH, 0),
            new Vector3( halfW,  halfH, 0)
        };

        // UVs
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        // Triangles
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Button callback: Hide all panels
    /// </summary>
    public void OnCloseAllPanels()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnCloseAllPanels button clicked");
        SetActivePanel(InstructionManager.InstructionPanel.None);
    }

    /// <summary>
    /// Button callback: Reset all captured planes (Solo Foto + QR Code)
    /// </summary>
    public void OnSelectReset()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectReset button clicked");

        // Play button click sound
        PlayButtonClickSound();

        int totalCleared = 0;

        // Reset Solo Foto planes
        if (liveMaskAndCapture != null)
        {
            int count = liveMaskAndCapture.GetCapturedPlaneCount();
            liveMaskAndCapture.ResetCapturedPlanes();
            totalCleared += count;
            Debug.Log($"[PanelNavigationManager] ✓ Cleared {count} Solo Foto planes");
        }
        else
        {
            Debug.LogWarning("[PanelNavigationManager] LiveMaskAndCapture reference not assigned!");
        }

        // Reset QR Code planes
        if (qrCodeDetection != null)
        {
            int count = qrCodeDetection.GetCapturedPlaneCount();
            qrCodeDetection.ResetCapturedPlanes();
            totalCleared += count;
            Debug.Log($"[PanelNavigationManager] ✓ Cleared {count} QR Code planes");
        }
        else
        {
            Debug.LogWarning("[PanelNavigationManager] QRCodeDetection reference not assigned!");
        }

        Debug.Log($"[PanelNavigationManager] ✅ Total planes cleared: {totalCleared}");
    }

    // ========================================
    // MODULAR METHOD - Use this for dynamic calls
    // ========================================

    /// <summary>
    /// Modular method: Select any panel by enum
    /// </summary>
    /// <param name="panel">The panel to activate</param>
    public void SelectPanel(InstructionManager.InstructionPanel panel)
    {
        Debug.Log($"[PanelNavigationManager] ★★★ SelectPanel called with: {panel}");
        SetActivePanel(panel);
    }

    /// <summary>
    /// Modular method: Select panel by index (0=Solo, 1=Co, 2=Remix, 3=Print)
    /// Useful for dropdown or programmatic calls
    /// </summary>
    /// <param name="index">Panel index (0-3)</param>
    public void SelectPanelByIndex(int index)
    {
        Debug.Log($"[PanelNavigationManager] ★★★ SelectPanelByIndex called with: {index}");

        switch (index)
        {
            case 0:
                SetActivePanel(InstructionManager.InstructionPanel.SoloFoto);
                break;
            case 1:
                SetActivePanel(InstructionManager.InstructionPanel.CoFoto);
                break;
            case 2:
                SetActivePanel(InstructionManager.InstructionPanel.RemixFoto);
                break;
            case 3:
                SetActivePanel(InstructionManager.InstructionPanel.PrintFoto);
                break;
            default:
                Debug.LogWarning($"[PanelNavigationManager] Invalid index: {index}");
                break;
        }
    }

    /// <summary>
    /// Set the active panel and update InstructionManager
    /// </summary>
    private void SetActivePanel(InstructionManager.InstructionPanel panel)
    {
        Debug.Log($"[PanelNavigationManager] Switching from {currentPanel} to {panel}");

        // Play button click sound
        PlayButtonClickSound();

        // Update InstructionManager if available
        if (InstructionManager.Instance != null)
        {
            InstructionManager.Instance.SetPanel(panel);
        }
        else
        {
            Debug.LogWarning("[PanelNavigationManager] InstructionManager.Instance is NULL!");
        }

        // SPECIAL CASE: When switching to Save Panel, keep the previous panel visible
        // so user can see what they're saving (Solo Foto, Co Foto, or Remix Foto)
        bool isGoingToSavePanel = (panel == InstructionManager.InstructionPanel.SaveFoto);
        bool isPreviousPanelACapturePanel = (currentPanel == InstructionManager.InstructionPanel.SoloFoto ||
                                              currentPanel == InstructionManager.InstructionPanel.CoFoto ||
                                              currentPanel == InstructionManager.InstructionPanel.RemixFoto);

        if (isGoingToSavePanel && isPreviousPanelACapturePanel)
        {
            // Don't deactivate previous panel - keep it visible
            Debug.Log($"[PanelNavigationManager] ★ Going to Save Panel from {currentPanel} - keeping previous panel visible");
            previousPanel = currentPanel;

            // Only activate Save Panel, don't deactivate others
            if (saveFotoPanel != null)
            {
                saveFotoPanel.SetActive(true);
                Debug.Log("[PanelNavigationManager] ✓ Save Foto panel activated (keeping previous panel visible)");
            }

            currentPanel = panel;
            return; // Skip the normal deactivate/activate logic
        }

        // Normal behavior: Deactivate all panels first
        DeactivateAllPanels();

        // Activate the selected panel
        switch (panel)
        {
            case InstructionManager.InstructionPanel.SoloFoto:
                if (soloFotoPanel != null)
                {
                    soloFotoPanel.SetActive(true);
                    Debug.Log("[PanelNavigationManager] ✓ Solo Foto panel activated");
                }
                break;

            case InstructionManager.InstructionPanel.CoFoto:
                if (coFotoPanel != null)
                {
                    coFotoPanel.SetActive(true);
                    Debug.Log("[PanelNavigationManager] ✓ Co Foto panel activated");
                }
                break;

            case InstructionManager.InstructionPanel.RemixFoto:
                if (remixFotoPanel != null)
                {
                    remixFotoPanel.SetActive(true);
                    Debug.Log("[PanelNavigationManager] ✓ Remix Foto panel activated");
                }
                break;

            case InstructionManager.InstructionPanel.LoadFoto:
                if (loadFotoPanel != null)
                {
                    loadFotoPanel.SetActive(true);
                    Debug.Log("[PanelNavigationManager] ✓ Load Foto panel activated");

                    // Populate the file list when Load Panel is opened
                    PopulateLoadPanelFileList();
                }
                break;

            case InstructionManager.InstructionPanel.SaveFoto:
                if (saveFotoPanel != null)
                {
                    saveFotoPanel.SetActive(true);
                    Debug.Log("[PanelNavigationManager] ✓ Save Foto panel activated");
                }
                break;

            case InstructionManager.InstructionPanel.PrintFoto:
                if (printFotoPanel != null)
                {
                    printFotoPanel.SetActive(true);
                    Debug.Log("[PanelNavigationManager] ✓ Print Foto panel activated");
                }
                break;

            case InstructionManager.InstructionPanel.None:
                Debug.Log("[PanelNavigationManager] ✓ All panels deactivated");
                break;
        }

        currentPanel = panel;
    }

    /// <summary>
    /// Deactivate all instruction panels
    /// </summary>
    private void DeactivateAllPanels()
    {
        if (soloFotoPanel != null) soloFotoPanel.SetActive(false);
        if (coFotoPanel != null) coFotoPanel.SetActive(false);
        if (remixFotoPanel != null) remixFotoPanel.SetActive(false);
        if (loadFotoPanel != null) loadFotoPanel.SetActive(false);
        if (saveFotoPanel != null) saveFotoPanel.SetActive(false);
        if (printFotoPanel != null) printFotoPanel.SetActive(false);
    }

    /// <summary>
    /// Get the currently active panel
    /// </summary>
    public InstructionManager.InstructionPanel GetCurrentPanel()
    {
        return currentPanel;
    }

    /// <summary>
    /// Play button click sound effect
    /// </summary>
    private void PlayButtonClickSound()
    {
        if (_audioSource != null && buttonClickSound != null)
        {
            _audioSource.PlayOneShot(buttonClickSound);
            Debug.Log("[PanelNavigationManager] 🔊 Button click sound played");
        }
        else if (buttonClickSound == null)
        {
            Debug.LogWarning("[PanelNavigationManager] Button click sound not assigned in Inspector!");
        }
    }

    // ========================================
    // NAVIGATION BUTTON STATE MANAGEMENT
    // ========================================

    /// <summary>
    /// Update navigation button states based on current panel and capture count
    /// - Sets "selected" state for current panel button (pressed appearance)
    /// - Disables Save button when no captures exist
    /// </summary>
    private void UpdateNavigationButtonStates(int captureCount)
    {
        // Update each button's selected state based on current panel
        if (soloFotoButton != null)
        {
            bool isCurrentPanel = (currentPanel == InstructionManager.InstructionPanel.SoloFoto);
            UpdateButtonAppearance(soloFotoButton, isCurrentPanel);
        }

        if (coFotoButton != null)
        {
            bool isCurrentPanel = (currentPanel == InstructionManager.InstructionPanel.CoFoto);
            UpdateButtonAppearance(coFotoButton, isCurrentPanel);
        }

        if (remixFotoButton != null)
        {
            bool isCurrentPanel = (currentPanel == InstructionManager.InstructionPanel.RemixFoto);
            UpdateButtonAppearance(remixFotoButton, isCurrentPanel);
        }

        if (loadFotoButton != null)
        {
            bool isCurrentPanel = (currentPanel == InstructionManager.InstructionPanel.LoadFoto);
            UpdateButtonAppearance(loadFotoButton, isCurrentPanel);
        }

        if (printFotoButton != null)
        {
            bool isCurrentPanel = (currentPanel == InstructionManager.InstructionPanel.PrintFoto);
            UpdateButtonAppearance(printFotoButton, isCurrentPanel);
        }

        // Save button: disable when no captures exist
        if (saveFotoButton != null)
        {
            bool hasCaptures = captureCount > 0;
            bool isCurrentPanel = (currentPanel == InstructionManager.InstructionPanel.SaveFoto);

            // Disable if no captures OR if already in Save panel
            saveFotoButton.interactable = hasCaptures && !isCurrentPanel;

            // Update appearance even when disabled (to show selected state)
            UpdateButtonAppearance(saveFotoButton, isCurrentPanel);
        }
    }

    /// <summary>
    /// Store the original normal sprites for all navigation buttons
    /// Called once at Start() before we modify any sprites
    /// </summary>
    private void StoreOriginalButtonSprites()
    {
        UnityEngine.UI.Button[] buttons = new UnityEngine.UI.Button[]
        {
            soloFotoButton,
            coFotoButton,
            remixFotoButton,
            loadFotoButton,
            saveFotoButton,
            printFotoButton
        };

        foreach (var button in buttons)
        {
            if (button != null && button.targetGraphic is UnityEngine.UI.Image image)
            {
                originalButtonSprites[button] = image.sprite;
                Debug.Log($"[PanelNavigationManager] Stored original sprite for button: {button.name}");
            }
        }
    }

    /// <summary>
    /// Update button appearance to show pressed sprite when it's the active panel
    /// </summary>
    private void UpdateButtonAppearance(UnityEngine.UI.Button button, bool isActivePanel)
    {
        if (button == null) return;

        // Get the button's Image component
        UnityEngine.UI.Image buttonImage = button.targetGraphic as UnityEngine.UI.Image;
        if (buttonImage == null) return;

        if (isActivePanel)
        {
            // Show pressed sprite (from SpriteState)
            if (button.spriteState.pressedSprite != null)
            {
                buttonImage.sprite = button.spriteState.pressedSprite;
                Debug.Log($"[PanelNavigationManager] Button '{button.name}' showing pressed sprite");
            }
            else
            {
                // Fallback to color if no pressed sprite configured
                buttonImage.color = button.colors.pressedColor;
                Debug.LogWarning($"[PanelNavigationManager] Button '{button.name}' has no pressed sprite, using pressed color instead");
            }

            // Make button non-interactable (can't click the already-active panel)
            button.interactable = false;
        }
        else
        {
            // Restore original normal sprite
            if (originalButtonSprites.ContainsKey(button) && originalButtonSprites[button] != null)
            {
                buttonImage.sprite = originalButtonSprites[button];
                Debug.Log($"[PanelNavigationManager] Button '{button.name}' restored to original sprite");
            }
            else
            {
                Debug.LogWarning($"[PanelNavigationManager] No original sprite stored for button '{button.name}'");
            }

            // Reset color to normal and make interactable
            buttonImage.color = button.colors.normalColor;
            button.interactable = true;
        }
    }

    // ========================================
    // UI VISIBILITY MANAGEMENT
    // ========================================

    /// <summary>
/// Handle visibility for all "Instruction" tagged objects (menu + instructions):
/// - Timer only increments when frame is INACTIVE (L-shape incorrect)
/// - OnFrameActive event resets timer and hides menu immediately
/// - OnFrameInactive event resets timer (starts counting from 0 when L-shape lost)
/// - Menu shows when timer >= 2 seconds (no correct gesture for 2s)
/// - EXCEPTION: When in Save Panel, instructions stay visible (don't hide captured images)
/// </summary>
private void HandleInstructionVisibility(int captureCount, int visibleHands)
{
    // IMPORTANT: If we're in Save Panel, don't hide instructions (keep captured images visible)
    if (currentPanel == InstructionManager.InstructionPanel.PrintFoto)
    {
        Debug.Log($"[PanelNavigationManager] In Save Panel - skipping instruction visibility logic (keep images visible)");

        // Ensure instructions are visible when in Save Panel
        if (!menuVisible)
        {
            menuVisible = true;
            SetInstructionVisibility(true);
            Debug.Log("[PanelNavigationManager] ✓ Forced instructions VISIBLE in Save Panel");
        }
        return;
    }

    // Only increment timer if frame is NOT active
    if (handGestureDetector != null && !handGestureDetector.IsFrameActive)
    {
        noLShapeTimer += Time.deltaTime;
        Debug.Log($"[PanelNavigationManager] Frame INACTIVE - Timer: {noLShapeTimer:F1}s / {noLShapeDelay:F1}s");
    }
    else
    {
        Debug.Log($"[PanelNavigationManager] Frame ACTIVE - Timer paused at: {noLShapeTimer:F1}s");
    }

    // Show menu if no correct L-shape for 2+ seconds
    bool newMenuVisible = noLShapeTimer >= noLShapeDelay;

    // Only update visibility if it changed
    if (newMenuVisible != menuVisible)
    {
        menuVisible = newMenuVisible;
        SetInstructionVisibility(menuVisible);
        Debug.LogError($"[PanelNavigationManager] ★★★ Menu {(menuVisible ? "SHOWN" : "HIDDEN")} - Timer: {noLShapeTimer:F1}s ★★★");
    }
}

    /// <summary>
    /// Set visibility for all "Instruction" tagged GameObjects
    /// Uses CanvasGroup for UI elements and enables/disables renderers for 3D objects
    /// </summary>
    private void SetInstructionVisibility(bool visible)
    {
        GameObject[] instructions = GameObject.FindGameObjectsWithTag("Instruction");

        Debug.LogError($"[PanelNavigationManager] ★★★ Found {instructions.Length} objects with 'Instruction' tag ★★★");

        foreach (var instruction in instructions)
        {
            // Check if this is a UI element (has Canvas or RectTransform)
            bool isUIElement = instruction.GetComponent<Canvas>() != null || instruction.GetComponent<UnityEngine.RectTransform>() != null;

            if (isUIElement)
            {
                // For UI elements, use CanvasGroup
                CanvasGroup canvasGroup = instruction.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = instruction.AddComponent<CanvasGroup>();
                    Debug.LogError($"[PanelNavigationManager] Added CanvasGroup to UI element {instruction.name}");
                }

                // Control visibility using CanvasGroup
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;

                Debug.LogError($"[PanelNavigationManager] Set UI element {instruction.name} visibility to {(visible ? "VISIBLE" : "HIDDEN")} (alpha: {canvasGroup.alpha})");
            }
            else
            {
                // For 3D objects (like armatures), disable all renderers recursively
                MeshRenderer[] meshRenderers = instruction.GetComponentsInChildren<MeshRenderer>(true);
                SkinnedMeshRenderer[] skinnedRenderers = instruction.GetComponentsInChildren<SkinnedMeshRenderer>(true);

                foreach (var renderer in meshRenderers)
                {
                    renderer.enabled = visible;
                }

                foreach (var renderer in skinnedRenderers)
                {
                    renderer.enabled = visible;
                }

                Debug.LogError($"[PanelNavigationManager] Set 3D object {instruction.name} visibility to {(visible ? "VISIBLE" : "HIDDEN")} ({meshRenderers.Length} MeshRenderers + {skinnedRenderers.Length} SkinnedMeshRenderers)");
            }
        }

        Debug.LogError($"[PanelNavigationManager] ★★★ Set {instructions.Length} instruction objects to {(visible ? "VISIBLE" : "HIDDEN")} ★★★");
    }

    /// <summary>
    /// Get total number of captures across both systems
    /// </summary>
    private int GetTotalCaptureCount()
    {
        int count = 0;
        int soloCount = 0;
        int qrCount = 0;

        Debug.LogError($"[PanelNavigationManager] ★★★ GetTotalCaptureCount START ★★★");
        Debug.LogError($"[PanelNavigationManager] liveMaskAndCapture null? {liveMaskAndCapture == null}");
        Debug.LogError($"[PanelNavigationManager] qrCodeDetection null? {qrCodeDetection == null}");

        if (liveMaskAndCapture != null)
        {
            soloCount = liveMaskAndCapture.GetCapturedPlaneCount();
            count += soloCount;
            Debug.LogError($"[PanelNavigationManager] ★★★ Solo Foto GetCapturedPlaneCount() returned: {soloCount} ★★★");
        }
        else
        {
            Debug.LogError("[PanelNavigationManager] ❌ liveMaskAndCapture is NULL!");
        }

        if (qrCodeDetection != null)
        {
            qrCount = qrCodeDetection.GetCapturedPlaneCount();
            count += qrCount;
            Debug.LogError($"[PanelNavigationManager] ★★★ QR Code GetCapturedPlaneCount() returned: {qrCount} ★★★");
        }
        else
        {
            Debug.LogWarning("[PanelNavigationManager] qrCodeDetection is NULL - this is OK if not using QR codes");
        }

        Debug.LogError($"[PanelNavigationManager] ★★★ FINAL TOTAL = {count} (Solo:{soloCount} + QR:{qrCount}) ★★★");
        return count;
    }

    /// <summary>
    /// Get number of currently visible hands
    /// </summary>
    private int GetVisibleHandCount()
    {
        int count = 0;

        bool leftTracked = leftHand != null && leftHand.IsTracked;
        bool rightTracked = rightHand != null && rightHand.IsTracked;

        if (leftTracked)
        {
            count++;
        }

        if (rightTracked)
        {
            count++;
        }

        Debug.Log($"[PanelNavigationManager] Hand tracking - Left: {leftTracked}, Right: {rightTracked}, Count: {count}");

        return count;
    }
}

