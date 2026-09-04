using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using PassthroughCameraSamples;


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
    [SerializeField] private GameObject animatedInstructionHand; // Optional: assign the "instruction" hand object directly
    [SerializeField] private WebCamTextureManager passthroughCameraTextureManager;

    [Header("Instruction Canvas Anchoring")]
    [SerializeField] private bool anchorInstructionCanvasToLeftHand = true;
    [SerializeField] private Transform instructionCanvasTransform;
    [SerializeField] private Transform leftHandInstructionAnchor;
    [SerializeField] private Vector3 leftHandInstructionOffset = Vector3.zero;
    [SerializeField] private float instructionCanvasAnchorSmoothing = 18f;

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

    [Header("Saving to Local Device")]
    private float noLShapeTimer = 0f;
    private bool menuVisible = false;
    private bool isSavingToDevice = false;
    private Transform resolvedInstructionCanvasTransform;
    private Transform resolvedLeftHandInstructionAnchor;
    private bool hasPositionedInstructionCanvas;

    private struct TemporaryHiddenObject
    {
        public GameObject gameObject;
        public bool wasActive;

        public TemporaryHiddenObject(GameObject gameObject, bool wasActive)
        {
            this.gameObject = gameObject;
            this.wasActive = wasActive;
        }
    }

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

        UpdateInstructionCanvasAnchor();
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
    /// Button callback: Open the in-app save panel for naming captures so Load can restore them.
    /// </summary>
    public void OnSelectSaveInAppFoto()
    {
        Debug.Log("[PanelNavigationManager] ★★★ OnSelectSaveInAppFoto button clicked");
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

        if (fileNameInputField == null || string.IsNullOrWhiteSpace(fileNameInputField.text))
        {
            Debug.LogError("[PanelNavigationManager] No file name provided - skipping in-app save");
            ShowAndroidToast("Enter a name to save in app");
            return;
        }

        string customFileName = fileNameInputField.text.Trim();
        Debug.LogError($"[PanelNavigationManager] Using custom file name for in-app save: '{customFileName}'");

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
    // SAVE TO LOCAL DEVICE FUNCTIONALITY
    // ========================================

     /// <summary>
    /// Alternative save mode: capture a full-screen screenshot of the current app view
    /// and save it to persistent storage. On Android this requests a media scan so
    /// the image appears in the system Gallery. Keeps the existing SaveAllCaptures
    /// functionality separate.
    /// Attach this to a separate Save-to-Device button (recommended) or call from UI.
    /// </summary>
    public void OnExecuteSaveToDevice()
    {
        Debug.LogError("[PanelNavigationManager] ★★★★★★ OnExecuteSaveToDevice button clicked ★★★★★★");
        TriggerSaveToDevice("button");
    }

    private void TriggerSaveToDevice(string source)
    {
        if (isSavingToDevice)
        {
            Debug.LogWarning($"[PanelNavigationManager] SaveToDevice already running, ignored source={source}");
            return;
        }

        if (source == "button")
        {
            ShowAndroidToast("Download clicked");
            PlayButtonClickSound();
        }
        else
        {
            ShowAndroidToast("Saving photo...");
        }

        StartCoroutine(SaveCaptureToDeviceRoutine());
    }

    private System.Collections.IEnumerator SaveCaptureToDeviceRoutine()
    {
        isSavingToDevice = true;

        List<TemporaryHiddenObject> hiddenMenuObjects = HideMenuForDeviceCapture();

        // Wait one frame for UI hide to apply, then capture at end of frame
        yield return null;
        yield return new WaitForEndOfFrame();

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fullFileName = $"fotofoto_full_{timestamp}.png";
        string framesFileName = $"fotofoto_frames_{timestamp}.png";
        string fullPath = Path.Combine(Application.persistentDataPath, fullFileName);
        string framesPath = Path.Combine(Application.persistentDataPath, framesFileName);
        bool savedFullView = false;
        bool savedFramesOnly = false;

        Texture2D fullViewScreenshot = null;
        Texture2D framesOnlyScreenshot = null;
        try
        {
            fullViewScreenshot = CaptureDeviceViewTexture();

            if (fullViewScreenshot == null)
            {
                Debug.LogError("[PanelNavigationManager] ❌ Full-view screenshot capture returned NULL, will try fallback CaptureScreenshot()");
            }
            else
            {
                savedFullView = TryWriteTexturePng(fullViewScreenshot, fullPath, "full camera view");
            }

            framesOnlyScreenshot = CaptureCapturedFramesOnlyTexture();
            if (framesOnlyScreenshot == null)
            {
                Debug.LogError("[PanelNavigationManager] ❌ Frames-only screenshot capture returned NULL");
            }
            else
            {
                savedFramesOnly = TryWriteTexturePng(framesOnlyScreenshot, framesPath, "captured frames only");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] ❌ SaveCaptureToDevice error: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            if (fullViewScreenshot != null)
            {
                UnityEngine.Object.Destroy(fullViewScreenshot);
            }

            if (framesOnlyScreenshot != null)
            {
                UnityEngine.Object.Destroy(framesOnlyScreenshot);
            }
        }

        // Fallback: some XR paths return null texture; this API writes screenshot directly.
        if (!savedFullView)
        {
            bool fallbackStarted = false;
            try
            {
                ScreenCapture.CaptureScreenshot(fullPath);
                fallbackStarted = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PanelNavigationManager] ❌ Fallback CaptureScreenshot error: {e.Message}\n{e.StackTrace}");
            }

            if (fallbackStarted)
            {
                float timeout = 2.0f;
                float elapsed = 0f;
                while (elapsed < timeout && !File.Exists(fullPath))
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                savedFullView = File.Exists(fullPath);
                Debug.LogError(savedFullView
                    ? $"[PanelNavigationManager] ✓ Fallback full-view screenshot saved to: {fullPath}"
                    : $"[PanelNavigationManager] ❌ Fallback full-view screenshot not found after {timeout:F1}s: {fullPath}");
            }
        }

        RestoreTemporarilyHiddenObjects(hiddenMenuObjects);

#if UNITY_ANDROID && !UNITY_EDITOR
        int publishedCount = 0;
        if (savedFullView)
        {
            publishedCount += PublishDevicePng(fullPath, fullFileName, "full view") ? 1 : 0;
        }

        if (savedFramesOnly)
        {
            publishedCount += PublishDevicePng(framesPath, framesFileName, "captured frames") ? 1 : 0;
        }

        if (publishedCount == 2)
        {
            ShowAndroidToast("Saved full view + frames");
        }
        else if (publishedCount == 1)
        {
            ShowAndroidToast("Saved 1 photo. Check logcat.");
        }
        else
        {
            ShowAndroidToast("Save failed. Check logcat.");
        }
#elif UNITY_IOS && !UNITY_EDITOR
        Debug.LogError("[PanelNavigationManager] iOS: to save to Photos install NativeGallery or implement native bridge (NSPhotoLibraryAddUsageDescription required).");
#else
        Debug.LogError((savedFullView || savedFramesOnly)
            ? $"[PanelNavigationManager] Non-mobile platform - saved full={savedFullView}, frames={savedFramesOnly} to persistentDataPath."
            : "[PanelNavigationManager] Non-mobile platform - screenshot saves failed.");
#endif

        isSavingToDevice = false;
    }

    private List<TemporaryHiddenObject> HideMenuForDeviceCapture()
    {
        var hiddenObjects = new List<TemporaryHiddenObject>();

        AddInstructionTaggedObjects(hiddenObjects);
        AddTemporaryHiddenObject(hiddenObjects, ResolveNavigationButtonGroup());
        AddTemporaryHiddenObject(hiddenObjects, ResolveHandMenuCanvas());
        AddTemporaryHiddenObject(hiddenObjects, ResolveAnimatedInstructionHand());

        Debug.LogError($"[PanelNavigationManager] Hidden {hiddenObjects.Count} menu object(s) for device screenshot");
        return hiddenObjects;
    }

    private void AddInstructionTaggedObjects(List<TemporaryHiddenObject> hiddenObjects)
    {
        GameObject[] instructionObjects = GameObject.FindGameObjectsWithTag("Instruction");
        foreach (var instructionObject in instructionObjects)
        {
            AddTemporaryHiddenObject(hiddenObjects, instructionObject);
        }
    }

    private Texture2D CaptureDeviceViewTexture()
    {
        Texture2D composite = TryCaptureCameraAndAppComposite();
        if (composite != null)
        {
            Debug.LogError("[PanelNavigationManager] ✓ Captured camera + app composite screenshot");
            return composite;
        }

        Debug.LogWarning("[PanelNavigationManager] Camera composite unavailable, falling back to Unity screenshot only");
        return ScreenCapture.CaptureScreenshotAsTexture();
    }

    private Texture2D CaptureCapturedFramesOnlyTexture()
    {
        Vector2Int captureSize = ResolveDeviceCaptureSize();
        Texture2D framesOnly = CaptureAppOverlayTexture(captureSize.x, captureSize.y);
        if (framesOnly != null)
        {
            Debug.LogError("[PanelNavigationManager] ✓ Captured frames-only screenshot");
        }

        return framesOnly;
    }

    private Vector2Int ResolveDeviceCaptureSize()
    {
        int width = Screen.width;
        int height = Screen.height;

        if (width > 16 && height > 16)
        {
            return new Vector2Int(width, height);
        }

        WebCamTexture cameraTexture = ResolvePassthroughWebCamTexture();
        if (cameraTexture != null && cameraTexture.width > 16 && cameraTexture.height > 16)
        {
            return new Vector2Int(cameraTexture.width, cameraTexture.height);
        }

        return new Vector2Int(1024, 1024);
    }

    private bool TryWriteTexturePng(Texture2D texture, string path, string label)
    {
        if (texture == null)
        {
            return false;
        }

        try
        {
            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(path, png);
            Debug.LogError($"[PanelNavigationManager] ✓ Saved {label} PNG to: {path}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] ❌ Failed writing {label} PNG: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }

    private bool PublishDevicePng(string path, string fileName, string label)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool publishedToPictures = TrySavePngToAndroidPictures(path, fileName, out string mediaStoreUri);
        if (publishedToPictures)
        {
            Debug.LogError($"[PanelNavigationManager] ✓ Published {label} to Android Download/fotofoto: {mediaStoreUri}");
            return true;
        }

        bool insertedToGallery = TryInsertImageIntoAndroidGallery(path, fileName, out string galleryUriOrPath);
        if (insertedToGallery)
        {
            Debug.LogError($"[PanelNavigationManager] ✓ Saved {label} via MediaStore.insertImage: {galleryUriOrPath}");
            return true;
        }

        Debug.LogError($"[PanelNavigationManager] ❌ Failed to publish {label} into MediaStore Download/Gallery, attempting media scan fallback");
        TryRequestAndroidMediaScan(path);
        return false;
#else
        return File.Exists(path);
#endif
    }

    private Texture2D TryCaptureCameraAndAppComposite()
    {
        WebCamTexture cameraTexture = ResolvePassthroughWebCamTexture();
        if (cameraTexture == null || !cameraTexture.isPlaying || cameraTexture.width <= 16 || cameraTexture.height <= 16)
        {
            Debug.LogWarning("[PanelNavigationManager] Passthrough WebCamTexture not ready for camera composite");
            return null;
        }

        int targetWidth = Screen.width > 16 ? Screen.width : cameraTexture.width;
        int targetHeight = Screen.height > 16 ? Screen.height : cameraTexture.height;

        Texture2D cameraBackground = null;
        Texture2D appOverlay = null;
        try
        {
            cameraBackground = CreateCameraBackgroundTexture(cameraTexture, targetWidth, targetHeight);
            appOverlay = CaptureAppOverlayTexture(targetWidth, targetHeight);

            if (cameraBackground == null || appOverlay == null)
            {
                if (cameraBackground != null) Destroy(cameraBackground);
                if (appOverlay != null) Destroy(appOverlay);
                return null;
            }

            CompositeOverlayOntoBackground(cameraBackground, appOverlay);
            Destroy(appOverlay);
            return cameraBackground;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] Camera composite capture failed: {e.Message}\n{e.StackTrace}");
            if (cameraBackground != null) Destroy(cameraBackground);
            if (appOverlay != null) Destroy(appOverlay);
            return null;
        }
    }

    private WebCamTexture ResolvePassthroughWebCamTexture()
    {
        if (passthroughCameraTextureManager == null)
        {
            passthroughCameraTextureManager = FindAnyObjectByType<WebCamTextureManager>();
        }

        return passthroughCameraTextureManager != null ? passthroughCameraTextureManager.WebCamTexture : null;
    }

    private Texture2D CreateCameraBackgroundTexture(WebCamTexture cameraTexture, int targetWidth, int targetHeight)
    {
        Color32[] sourcePixels = cameraTexture.GetPixels32();
        if (sourcePixels == null || sourcePixels.Length == 0)
        {
            Debug.LogWarning("[PanelNavigationManager] WebCamTexture returned no pixels");
            return null;
        }

        int sourceWidth = cameraTexture.width;
        int sourceHeight = cameraTexture.height;
        var output = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        Color32[] outputPixels = new Color32[targetWidth * targetHeight];

        float sourceAspect = (float)sourceWidth / sourceHeight;
        float targetAspect = (float)targetWidth / targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                float u = (x + 0.5f) / targetWidth;
                float v = (y + 0.5f) / targetHeight;

                if (sourceAspect > targetAspect)
                {
                    float visibleWidth = targetAspect / sourceAspect;
                    u = (1f - visibleWidth) * 0.5f + u * visibleWidth;
                }
                else
                {
                    float visibleHeight = sourceAspect / targetAspect;
                    v = (1f - visibleHeight) * 0.5f + v * visibleHeight;
                }

                ApplyWebCamOrientation(cameraTexture, ref u, ref v);

                int sourceX = Mathf.Clamp(Mathf.RoundToInt(u * (sourceWidth - 1)), 0, sourceWidth - 1);
                int sourceY = Mathf.Clamp(Mathf.RoundToInt(v * (sourceHeight - 1)), 0, sourceHeight - 1);
                outputPixels[y * targetWidth + x] = sourcePixels[sourceY * sourceWidth + sourceX];
            }
        }

        output.SetPixels32(outputPixels);
        output.Apply();
        return output;
    }

    private void ApplyWebCamOrientation(WebCamTexture cameraTexture, ref float u, ref float v)
    {
        if (cameraTexture.videoVerticallyMirrored)
        {
            v = 1f - v;
        }

        int rotation = ((cameraTexture.videoRotationAngle % 360) + 360) % 360;
        switch (rotation)
        {
            case 90:
                float rotatedU90 = v;
                float rotatedV90 = 1f - u;
                u = rotatedU90;
                v = rotatedV90;
                break;
            case 180:
                u = 1f - u;
                v = 1f - v;
                break;
            case 270:
                float rotatedU270 = 1f - v;
                float rotatedV270 = u;
                u = rotatedU270;
                v = rotatedV270;
                break;
        }
    }

    private Texture2D CaptureAppOverlayTexture(int targetWidth, int targetHeight)
    {
        Camera cameraToRender = ResolveScreenshotCamera();
        if (cameraToRender == null)
        {
            Debug.LogWarning("[PanelNavigationManager] No camera available for app overlay capture");
            return null;
        }

        RenderTexture renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = cameraToRender.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        CameraClearFlags previousClearFlags = cameraToRender.clearFlags;
        Color previousBackgroundColor = cameraToRender.backgroundColor;

        try
        {
            cameraToRender.targetTexture = renderTexture;
            cameraToRender.clearFlags = CameraClearFlags.SolidColor;
            cameraToRender.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cameraToRender.Render();

            RenderTexture.active = renderTexture;
            Texture2D texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            texture.Apply();
            return texture;
        }
        finally
        {
            cameraToRender.targetTexture = previousTarget;
            cameraToRender.clearFlags = previousClearFlags;
            cameraToRender.backgroundColor = previousBackgroundColor;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private Camera ResolveScreenshotCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            return cam;
        }

        var rig = FindAnyObjectByType<OVRCameraRig>();
        return rig != null && rig.centerEyeAnchor != null
            ? rig.centerEyeAnchor.GetComponent<Camera>()
            : null;
    }

    private void CompositeOverlayOntoBackground(Texture2D background, Texture2D overlay)
    {
        Color32[] backgroundPixels = background.GetPixels32();
        Color32[] overlayPixels = overlay.GetPixels32();
        int count = Mathf.Min(backgroundPixels.Length, overlayPixels.Length);

        for (int i = 0; i < count; i++)
        {
            Color32 overlayPixel = overlayPixels[i];
            float alpha = overlayPixel.a / 255f;
            if (alpha <= 0.01f)
            {
                continue;
            }

            float inverseAlpha = 1f - alpha;
            Color32 backgroundPixel = backgroundPixels[i];
            backgroundPixels[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(overlayPixel.r * alpha + backgroundPixel.r * inverseAlpha), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(overlayPixel.g * alpha + backgroundPixel.g * inverseAlpha), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(overlayPixel.b * alpha + backgroundPixel.b * inverseAlpha), 0, 255),
                255);
        }

        background.SetPixels32(backgroundPixels);
        background.Apply();
    }

    private void AddTemporaryHiddenObject(List<TemporaryHiddenObject> hiddenObjects, GameObject objectToHide)
    {
        if (objectToHide == null)
        {
            return;
        }

        foreach (var hiddenObject in hiddenObjects)
        {
            if (hiddenObject.gameObject == objectToHide)
            {
                return;
            }
        }

        bool wasActive = objectToHide.activeSelf;
        hiddenObjects.Add(new TemporaryHiddenObject(objectToHide, wasActive));

        if (wasActive)
        {
            objectToHide.SetActive(false);
        }
    }

    private void RestoreTemporarilyHiddenObjects(List<TemporaryHiddenObject> hiddenObjects)
    {
        foreach (var hiddenObject in hiddenObjects)
        {
            if (hiddenObject.gameObject != null)
            {
                hiddenObject.gameObject.SetActive(hiddenObject.wasActive);
            }
        }
    }

    private GameObject ResolveNavigationButtonGroup()
    {
        UnityEngine.UI.Button[] knownNavigationButtons =
        {
            soloFotoButton,
            coFotoButton,
            remixFotoButton,
            loadFotoButton,
            saveFotoButton,
            printFotoButton
        };

        foreach (var button in knownNavigationButtons)
        {
            if (button != null && button.transform.parent != null)
            {
                return button.transform.parent.gameObject;
            }
        }

        return FindSceneGameObjectByName("navigation");
    }

    private GameObject ResolveHandMenuCanvas()
    {
        return FindSceneGameObjectByName("Hand Canvas");
    }

    private GameObject FindSceneGameObjectByName(string objectName)
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var obj in objects)
        {
            if (obj != null && obj.scene.IsValid() && obj.name == objectName)
            {
                return obj;
            }
        }

        return null;
    }

    private GameObject ResolveAnimatedInstructionHand()
    {
        if (animatedInstructionHand != null)
        {
            return animatedInstructionHand;
        }

        // Most common location in this scene
        if (soloFotoPanel != null)
        {
            Transform child = soloFotoPanel.transform.Find("instruction");
            if (child != null)
            {
                return child.gameObject;
            }
        }

        // Fallback: locate active/inactive object named "instruction" with Animator
        Animator[] animators = UnityEngine.Object.FindObjectsOfType<Animator>(true);
        foreach (var animator in animators)
        {
            if (animator != null &&
                animator.gameObject != null &&
                string.Equals(animator.gameObject.name, "instruction", StringComparison.OrdinalIgnoreCase))
            {
                return animator.gameObject;
            }
        }

        return null;
    }

    private void ShowAndroidToast(string message)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var toastClass = new AndroidJavaClass("android.widget.Toast"))
            {
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    var toast = toastClass.CallStatic<AndroidJavaObject>("makeText", activity, message, 0);
                    toast.Call("show");
                }));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PanelNavigationManager] Failed to show Android toast: {e.Message}");
        }
#endif
    }

    private bool TrySavePngToAndroidPictures(string sourcePath, string fileName, out string mediaStoreUri)
    {
        mediaStoreUri = null;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            Debug.LogError($"[PanelNavigationManager] Source file missing for MediaStore publish: {sourcePath}");
            return false;
        }

        byte[] pngBytes;
        try
        {
            pngBytes = File.ReadAllBytes(sourcePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] Failed reading screenshot bytes for MediaStore publish: {e.Message}");
            return false;
        }

        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var mediaStoreDownloads = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            using (var mediaColumns = new AndroidJavaClass("android.provider.MediaStore$MediaColumns"))
            using (var values = new AndroidJavaObject("android.content.ContentValues"))
            using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                int sdkInt = buildVersion.GetStatic<int>("SDK_INT");
                string displayNameCol = mediaColumns.GetStatic<string>("DISPLAY_NAME");
                string mimeTypeCol = mediaColumns.GetStatic<string>("MIME_TYPE");
                string relativePathCol = mediaColumns.GetStatic<string>("RELATIVE_PATH");
                string isPendingCol = mediaColumns.GetStatic<string>("IS_PENDING");

                values.Call("put", displayNameCol, fileName);
                values.Call("put", mimeTypeCol, "image/png");
                values.Call("put", relativePathCol, "Download/fotofoto");
                if (sdkInt >= 29)
                {
                    using (var one = new AndroidJavaObject("java.lang.Integer", 1))
                    {
                        values.Call("put", isPendingCol, one);
                    }
                }

                using (var collection = mediaStoreDownloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
                using (var itemUri = resolver.Call<AndroidJavaObject>("insert", collection, values))
                {
                    if (itemUri == null)
                    {
                        Debug.LogError("[PanelNavigationManager] MediaStore insert returned null URI");
                        return false;
                    }

                    using (var outStream = resolver.Call<AndroidJavaObject>("openOutputStream", itemUri))
                    {
                        if (outStream == null)
                        {
                            Debug.LogError("[PanelNavigationManager] MediaStore openOutputStream returned null");
                            return false;
                        }
                        outStream.Call("write", pngBytes);
                        outStream.Call("flush");
                    }

                    if (sdkInt >= 29)
                    {
                        using (var pendingValues = new AndroidJavaObject("android.content.ContentValues"))
                        {
                            using (var zero = new AndroidJavaObject("java.lang.Integer", 0))
                            {
                                pendingValues.Call("put", isPendingCol, zero);
                            }
                            resolver.Call<int>("update", itemUri, pendingValues, null, null);
                        }
                    }

                    mediaStoreUri = itemUri.Call<string>("toString");
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] MediaStore publish failed: {e.Message}\n{e.StackTrace}");
            return false;
        }
#else
        return false;
#endif
    }

    private void TryRequestAndroidMediaScan(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var mediaScanner = new AndroidJavaClass("android.media.MediaScannerConnection"))
            {
                string[] paths = new string[] { path };
                string[] mimeTypes = new string[] { "image/png" };
                mediaScanner.CallStatic("scanFile", activity, paths, mimeTypes, null);
                Debug.LogError("[PanelNavigationManager] Requested Android media scan fallback");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] Android media scan fallback failed: {e.Message}");
        }
#endif
    }

    private bool TryInsertImageIntoAndroidGallery(string sourcePath, string title, out string imageUriOrPath)
    {
        imageUriOrPath = null;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            Debug.LogError($"[PanelNavigationManager] Gallery insert source file missing: {sourcePath}");
            return false;
        }

        try
        {
            byte[] png = File.ReadAllBytes(sourcePath);

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory"))
            using (var mediaStoreImages = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
            using (var bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeByteArray", png, 0, png.Length))
            {
                if (bitmap == null)
                {
                    Debug.LogError("[PanelNavigationManager] BitmapFactory.decodeByteArray returned null");
                    return false;
                }

                string inserted = mediaStoreImages.CallStatic<string>(
                    "insertImage",
                    resolver,
                    bitmap,
                    title,
                    "fotofoto capture");

                if (string.IsNullOrEmpty(inserted))
                {
                    Debug.LogError("[PanelNavigationManager] MediaStore.insertImage returned empty path/uri");
                    return false;
                }

                imageUriOrPath = inserted;
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] MediaStore.insertImage failed: {e.Message}\n{e.StackTrace}");
            return false;
        }
#else
        return false;
#endif
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

            if (loadedCount > 0)
            {
                ClosePanelsAfterSceneLoaded();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PanelNavigationManager] ❌ Failed to load scene: {e.Message}");
            Debug.LogError($"[PanelNavigationManager] Stack trace: {e.StackTrace}");
        }
    }

    private void ClosePanelsAfterSceneLoaded()
    {
        if (InstructionManager.Instance != null)
        {
            InstructionManager.Instance.SetPanel(InstructionManager.InstructionPanel.None);
        }

        DeactivateAllPanels();
        currentPanel = InstructionManager.InstructionPanel.None;
        Debug.LogError("[PanelNavigationManager] Load panel hidden after scene load");
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

    private void UpdateInstructionCanvasAnchor()
    {
        if (!anchorInstructionCanvasToLeftHand)
        {
            return;
        }

        Transform canvasTransform = ResolveInstructionCanvasTransform();
        Transform handAnchor = ResolveLeftHandInstructionAnchor();
        Camera cam = ResolveScreenshotCamera();

        if (canvasTransform == null || handAnchor == null || cam == null)
        {
            return;
        }

        Vector3 targetPosition =
            handAnchor.position +
            cam.transform.right * leftHandInstructionOffset.x +
            Vector3.up * leftHandInstructionOffset.y +
            cam.transform.forward * leftHandInstructionOffset.z;

        Quaternion targetRotation = cam.transform.rotation;

        if (!hasPositionedInstructionCanvas || instructionCanvasAnchorSmoothing <= 0f)
        {
            canvasTransform.position = targetPosition;
            canvasTransform.rotation = targetRotation;
            hasPositionedInstructionCanvas = true;
            return;
        }

        float t = 1f - Mathf.Exp(-instructionCanvasAnchorSmoothing * Time.deltaTime);
        canvasTransform.position = Vector3.Lerp(canvasTransform.position, targetPosition, t);
        canvasTransform.rotation = Quaternion.Slerp(canvasTransform.rotation, targetRotation, t);
    }

    private Transform ResolveInstructionCanvasTransform()
    {
        if (instructionCanvasTransform != null)
        {
            return instructionCanvasTransform;
        }

        if (resolvedInstructionCanvasTransform != null)
        {
            return resolvedInstructionCanvasTransform;
        }

        GameObject canvasObject = FindSceneGameObjectByName("Instructions Canvas");
        resolvedInstructionCanvasTransform = canvasObject != null ? canvasObject.transform : null;
        return resolvedInstructionCanvasTransform;
    }

    private Transform ResolveLeftHandInstructionAnchor()
    {
        if (leftHandInstructionAnchor != null)
        {
            return leftHandInstructionAnchor;
        }

        if (leftHand != null && leftHand.IsTracked)
        {
            return leftHand.transform;
        }

        if (resolvedLeftHandInstructionAnchor != null)
        {
            return resolvedLeftHandInstructionAnchor;
        }

        GameObject leftHandAnchorObject =
            FindSceneGameObjectByName("LeftHandAnchorDetached") ??
            FindSceneGameObjectByName("LeftHandAnchor") ??
            FindSceneGameObjectByName("LeftHandOnControllerAnchor");

        resolvedLeftHandInstructionAnchor = leftHandAnchorObject != null ? leftHandAnchorObject.transform : null;
        return resolvedLeftHandInstructionAnchor;
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
