// for remix foto
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using PassthroughCameraSamples;
using Meta.XR;

public class QRCodeDetection : MonoBehaviour
{
#if ZXING_ENABLED
    [Header("Required Components")]
    [SerializeField] private QrCodeScanner scanner;
    [SerializeField] private EnvironmentRaycastManager envRaycastManager;

    [Header("UI Elements")]
    [SerializeField] private GameObject enterQRBtn;
    [SerializeField] private GameObject retakeQRBtn;
    [SerializeField] private GameObject qrScanFrame;

    [Header("Passthrough Settings")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private OVRPassthroughLayer qrScanFramePassthroughLayer; // Separate layer for qrScanFrame
    [SerializeField] private string qrPassthroughTag = "QR";

    private Vector3 _qrHoleOriginalScale;    // TRUE inspector scale
    private Vector3 _qrHoleScaleAtDetection; // Scale captured at QR detection time
    private GameObject _qrHoleObject;         // passthrough quad
    private Vector3 _qrScanFrameOriginalScale; // Original scale of qrScanFrame from Inspector

    [Header("Display Settings")]
    [SerializeField] private float fixedDistanceFromCamera = 1.0f;

    [Header("Gesture Capture")]
    [SerializeField] private HandGestureDetector gestureDetector; // For gesture-based capture
    [SerializeField] private int captureWidth = 1024;
    [SerializeField] private int captureHeight = 768;

    // Mode tracking
    public enum QRMode
    {
        TakingQR,      // Scanning for QR code - gesture capture DISABLED
        TakingPicture  // Taking pictures - gesture capture ENABLED
    }

    private QRMode _currentMode = QRMode.TakingQR;
    private bool _isScanning = false;
    private bool _qrDetected = false;
    private string _currentQRUrl = "";

    private GameObject _displayPlane;
    private MeshRenderer _planeRenderer;
    private GameObject _displayCanvasUI; // UI Canvas alternative for testing
    private Texture2D _qrImageTexture; // Store the downloaded QR image texture
    private List<GameObject> capturedPlanes = new List<GameObject>(); // Captured planes with QR texture

    // Preview frame for thumb resize
    private GameObject _previewFrame;
    private MeshRenderer _previewRenderer;

    private WebCamTextureManager _webCamTextureManager;
    private PassthroughCameraEye _passthroughCameraEye;

    private Camera _passthroughCamera;

    private AudioSource _audioSource;
    [SerializeField] private AudioClip qrDetectedSound;
    [SerializeField] private AudioClip buttonClickSound;

    // ----------------------------------------------------------------------
    // AWAKE
    // ----------------------------------------------------------------------
    private void Awake()
    {
        Debug.Log("=== QRCodeDetection: AWAKE ===");

        // Check scanner
        if (scanner == null)
        {
            Debug.LogError("[QR] ❌ QrCodeScanner is NOT assigned in Inspector!");
        }
        else
        {
            Debug.Log("[QR] ✓ QrCodeScanner is assigned");
        }

        // camera refs
        _webCamTextureManager = FindAnyObjectByType<WebCamTextureManager>();
        if (_webCamTextureManager != null)
        {
            _passthroughCameraEye = _webCamTextureManager.Eye;
            Debug.Log("[QR] ✓ WebCamTextureManager found");
        }
        else
        {
            Debug.LogError("[QR] ❌ WebCamTextureManager NOT found!");
        }

        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig != null)
            _passthroughCamera = rig.centerEyeAnchor.GetComponent<Camera>();
        else
            _passthroughCamera = Camera.main;

        // sounds
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;

        if (enterQRBtn) enterQRBtn.SetActive(false);
        if (retakeQRBtn) retakeQRBtn.SetActive(false);

        // Disable qrScanFrame's passthrough layer initially
        if (qrScanFramePassthroughLayer != null)
        {
            qrScanFramePassthroughLayer.enabled = false;
            Debug.Log("[QR] qrScanFramePassthroughLayer disabled initially");
        }
        else if (qrScanFrame != null)
        {
            Debug.LogWarning("[QR] qrScanFramePassthroughLayer is not assigned! Please create a separate OVRPassthroughLayer for qrScanFrame");
        }

        // CRITICAL: Disable qrScanFrame's MeshRenderer so it shows passthrough, not grey mesh
        if (qrScanFrame != null)
        {
            var renderer = qrScanFrame.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
                Debug.Log("[QR] ✓ qrScanFrame MeshRenderer disabled (shows passthrough, not grey)");
            }
        }

        // Subscribe to gesture detection
        if (gestureDetector != null)
        {
            gestureDetector.OnFlickerGesture += OnGestureCapture;

            // ONLY subscribe to OnQRFrameActive (not OnFrameActive!)
            // OnFrameActive is for SoloFoto (passthrough capture)
            // OnQRFrameActive is for RemixFoto/SaveFoto (QR image preview)
            gestureDetector.OnQRFrameActive += OnFrameResize;
            Debug.Log("[QR] ✓ Subscribed to HandGestureDetector.OnQRFrameActive");
        }
        else
        {
            Debug.LogWarning("[QR] HandGestureDetector not assigned - gesture capture disabled");
        }

        // passthrough hole setup (but NOT scale storing)
        SetupQRPassthrough();
    }

    // ----------------------------------------------------------------------
    // START — store *real* inspector scale here
    // ----------------------------------------------------------------------
    private void Start()
    {
        if (_qrHoleObject != null)
        {
            _qrHoleOriginalScale = _qrHoleObject.transform.localScale;
            Debug.Log($"[QR] Stored TRUE inspector scale in Start(): {_qrHoleOriginalScale}");
        }
    }

    private void OnEnable()
    {
        Debug.Log("[QR] OnEnable called - reactivating passthrough hole");

        // Reactivate and re-register the passthrough hole
        if (_qrHoleObject != null && passthroughLayer != null)
        {
            // Reactivate the GameObject
            _qrHoleObject.SetActive(true);
            Debug.Log("[QR] ✓ Reactivated passthrough hole GameObject");

            // Re-register with passthrough layer
            try
            {
                passthroughLayer.AddSurfaceGeometry(_qrHoleObject, updateTransform: true);
                Debug.Log("[QR] ✓ Re-registered passthrough hole with passthrough layer");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QR] Failed to re-register passthrough hole: {e.Message}");
            }

            // Reset scale to original
            _qrHoleObject.transform.localScale = _qrHoleOriginalScale;
        }
        else if (_qrHoleObject == null)
        {
            Debug.LogWarning("[QR] ⚠️ Passthrough hole reference is null! This shouldn't happen.");
        }

        // Reset scanning state when panel is re-enabled
        _qrDetected = false;
        _isScanning = false;
        Debug.Log("[QR] ✓ Reset scanning state");
    }

    // ----------------------------------------------------------------------
    // FIND QR PASSTHROUGH HOLE (do NOT modify scale)
    // ----------------------------------------------------------------------
    private void SetupQRPassthrough()
{
    Debug.Log("=== SetupQRPassthrough ===");

    // find passthrough layer
    if (passthroughLayer == null)
        passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();

    if (passthroughLayer == null)
    {
        Debug.LogError("[QR] No OVRPassthroughLayer found");
        return;
    }

    // find QR hole
    _qrHoleObject = GameObject.FindGameObjectWithTag(qrPassthroughTag);
    if (_qrHoleObject == null)
    {
        Debug.LogError("[QR] No object with tag 'QR' found");
        return;
    }

    // ensure it has mesh renderer
    var renderer = _qrHoleObject.GetComponent<MeshRenderer>();
    if (renderer == null)
    {
        Debug.LogError("[QR] Passthrough hole MUST have a MeshRenderer");
        return;
    }

    // ----------------------------------------------------
    // ⭐⭐ CRITICAL: Remove collider to prevent blocking raycasts ⭐⭐
    // ----------------------------------------------------
    var collider = _qrHoleObject.GetComponent<Collider>();
    if (collider != null)
    {
        Destroy(collider);
        Debug.Log("[QR] ✓ Collider removed from passthrough hole to prevent raycast blocking");
    }

    // ----------------------------------------------------
    // ⭐⭐ CRITICAL: Disable renderer so hole shows passthrough, not grey mesh ⭐⭐
    // ----------------------------------------------------
    renderer.enabled = false;
    Debug.Log("[QR] ✓ MeshRenderer disabled on passthrough hole");

    // ----------------------------------------------------
    // ⭐⭐ FIX: Set real world scale BEFORE registration ⭐⭐
    // IMPORTANT: Check if this is a RectTransform (UI element) or regular Transform
    // ----------------------------------------------------
    RectTransform rectTransform = _qrHoleObject.GetComponent<RectTransform>();

    if (rectTransform != null)
    {
        // This is a UI element being used as passthrough surface geometry
        // OVR passthrough interprets UI coordinates differently - need LARGER scale values
        // The sizeDelta (100x100) is in UI pixels, but passthrough needs world-space scale
        Debug.Log($"[QR] ►►► QR hole is a UI RectTransform - sizeDelta: {rectTransform.sizeDelta}, current scale: {rectTransform.localScale}");

        // For UI elements used as passthrough geometry, we need to scale UP significantly
        // This compensates for the UI->World space conversion
        _qrHoleObject.transform.localScale = new Vector3(100.0f, 100.0f, 1.0f);
        _qrHoleOriginalScale = _qrHoleObject.transform.localScale;

        Debug.LogWarning($"[QR] ►►► Set UI passthrough hole scale to: {_qrHoleOriginalScale}");
    }
    else
    {
        // This is a world-space object (Quad/Plane) - set scale in meters
        Debug.Log("[QR] ►►► QR hole is a world-space Transform (not RectTransform)");
        _qrHoleObject.transform.localScale = new Vector3(0.1f, 0.1f, 1.0f); // 10cm x 10cm frame
        _qrHoleOriginalScale = _qrHoleObject.transform.localScale;
        Debug.LogWarning($"[QR] ►►► Set world-space passthrough hole scale to: {_qrHoleOriginalScale}");
    }

    Debug.Log("[QR] Stored passthrough hole scale: " + _qrHoleOriginalScale);

    // ----------------------------------------------------
    // NOW register with passthrough layer
    // ----------------------------------------------------
    try
    {
        passthroughLayer.AddSurfaceGeometry(_qrHoleObject, updateTransform: true);
        Debug.Log("[QR] ✓ Registered passthrough hole successfully");
    }
    catch (Exception e)
    {
        Debug.LogError("[QR] Failed AddSurfaceGeometry: " + e.Message);
    }

    Debug.Log($"[QR] ✓ Passthrough hole setup complete - Renderer enabled: {renderer.enabled}");
}

    // ----------------------------------------------------------------------
    // UPDATE — Show/hide QR hole correctly using real original scale
    // ----------------------------------------------------------------------
    private void Update()
    {
        bool remixActive =
            InstructionManager.Instance != null &&
            InstructionManager.Instance.currentPanel == InstructionManager.InstructionPanel.RemixFoto;

        // Check if we're in RemixFoto or SaveFoto panel - show captured planes only in these panels
        bool shouldShowCapturedPlanes = false;
        if (InstructionManager.Instance != null)
        {
            shouldShowCapturedPlanes =
                InstructionManager.Instance.currentPanel == InstructionManager.InstructionPanel.RemixFoto ||
                InstructionManager.Instance.currentPanel == InstructionManager.InstructionPanel.SaveFoto;
        }

        // Show/hide all captured planes based on current panel
        foreach (var plane in capturedPlanes)
        {
            if (plane != null && plane.activeSelf != shouldShowCapturedPlanes)
            {
                plane.SetActive(shouldShowCapturedPlanes);
            }
        }

        // -------- Q R  H O L E --------
        // Only manipulate if scanning (not after QR detected)
        if (_qrHoleObject == null && !_qrDetected && remixActive)
        {
            // Hole is null but we're scanning - this is a problem!
            Debug.LogError("[QR] ►►► UPDATE: _qrHoleObject is NULL while scanning!");
        }

        if (_qrHoleObject != null && !_qrDetected)
        {
            if (remixActive)
            {
                // Scanning mode - show passthrough hole
                if (!_qrHoleObject.activeSelf)
                {
                    Debug.Log($"[QR] ►►► UPDATE: Activating QR hole, setting scale to: {_qrHoleOriginalScale}");
                    _qrHoleObject.SetActive(true);
                    _qrHoleObject.transform.localScale = _qrHoleOriginalScale;

                    var renderer = _qrHoleObject.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                    Debug.Log($"[QR] ►►► UPDATE: QR hole activated with scale: {_qrHoleObject.transform.localScale}");
                }
            }
            else
            {
                // Not in Remix panel - hide it
                if (_qrHoleObject.activeSelf || _qrHoleObject.transform.localScale != Vector3.zero)
                {
                    Debug.Log("[QR] ►►► UPDATE: Hiding QR hole (not in Remix panel)");
                    _qrHoleObject.transform.localScale = new Vector3(0.0f, 0.0f, 0.0f);
                }
            }
        }

        // UI - qrScanFrame should ONLY show during TakingQR mode in RemixFoto panel
        // Control visibility via its dedicated passthrough layer
        if (qrScanFramePassthroughLayer != null)
        {
            bool shouldShowScanFrame = remixActive && !_qrDetected && _currentMode == QRMode.TakingQR;

            // Enable/disable the passthrough layer to show/hide the qrScanFrame
            if (qrScanFramePassthroughLayer.enabled != shouldShowScanFrame)
            {
                qrScanFramePassthroughLayer.enabled = shouldShowScanFrame;
                Debug.Log($"[QR] qrScanFramePassthroughLayer enabled changed to: {shouldShowScanFrame} (remixActive:{remixActive}, detected:{_qrDetected}, mode:{_currentMode})");
            }
        }
        // if (enterQRBtn) enterQRBtn.SetActive(remixActive && _qrDetected);
        // if (retakeQRBtn) retakeQRBtn.SetActive(remixActive && _qrDetected);

        // Display plane should ONLY show in RemixFoto panel when QR is detected and in TakingQR mode
        // Hide it in TakingPicture mode OR when not in RemixFoto panel
        if (_displayPlane != null)
        {
            bool shouldShowDisplayPlane = remixActive && _qrDetected && _currentMode == QRMode.TakingQR;

            if (_displayPlane.activeSelf != shouldShowDisplayPlane)
            {
                _displayPlane.SetActive(shouldShowDisplayPlane);
                Debug.Log($"[QR] Display plane visibility changed to: {shouldShowDisplayPlane} (remixActive:{remixActive}, detected:{_qrDetected}, mode:{_currentMode})");
            }
        }

        // DEBUG: Log what objects are active that might block raycasts
        if (remixActive && Time.frameCount % 120 == 0) // Every 2 seconds
        {
            Debug.Log($"[QR] DEBUG - Remix Active State:");
            Debug.Log($"  - _qrHoleObject active: {(_qrHoleObject != null ? _qrHoleObject.activeSelf.ToString() : "NULL")}");
            Debug.Log($"  - _qrHoleObject scale: {(_qrHoleObject != null ? _qrHoleObject.transform.localScale.ToString() : "NULL")}");
            Debug.Log($"  - _displayPlane active: {(_displayPlane != null ? _displayPlane.activeSelf.ToString() : "NULL")}");
            Debug.Log($"  - qrScanFrame active: {(qrScanFrame != null ? qrScanFrame.activeSelf.ToString() : "NULL")}");
            Debug.Log($"  - enterQRBtn active: {(enterQRBtn != null ? enterQRBtn.activeSelf.ToString() : "NULL")}");
            Debug.Log($"  - retakeQRBtn active: {(retakeQRBtn != null ? retakeQRBtn.activeSelf.ToString() : "NULL")}");
        }

        // scanning logic
        if (remixActive && !_qrDetected)
        {
            if (!_isScanning)
            {
                _isScanning = true;
                Debug.Log("[QR] Started scanning for QR codes");
            }

            // Call ScanForQRCode every frame while scanning
            ScanForQRCode();
        }
        else
        {
            if (_isScanning)
            {
                StopScanning();
                _isScanning = false;
                Debug.Log("[QR] Stopped scanning");
            }
        }
    }

    // ----------------------------------------------------------------------
    // SCAN LOGIC
    // ----------------------------------------------------------------------
    private async void ScanForQRCode()
    {
        if (_qrDetected) return;

        if (scanner == null)
        {
            Debug.LogError("[QR] ❌ Scanner is NULL! Cannot scan!");
            return;
        }

        if (_webCamTextureManager == null || _webCamTextureManager.WebCamTexture == null)
        {
            Debug.LogError("[QR] ❌ WebCamTexture is NULL! Cannot scan!");
            return;
        }

        var qrResults = await scanner.ScanFrameAsync() ?? Array.Empty<QrCodeResult>();

        if (qrResults.Length == 0)
        {
            // Only log occasionally to avoid spam
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[QR] Scan complete, found 0 results (webcam playing: {_webCamTextureManager.WebCamTexture.isPlaying})");
            }
            return;
        }

        // Process ONLY the first QR code result
        var qr = qrResults[0];
        Debug.Log($"[QR] ★★★ QR code detected! Text: {qr.text}");

        if (!IsUrl(qr.text))
        {
            Debug.Log($"[QR] Not a URL, skipping: {qr.text}");
            return;
        }

        // Play sound
        if (qrDetectedSound != null)
            _audioSource.PlayOneShot(qrDetectedSound);

        _currentQRUrl = qr.text;

        // CREATE FOLLOW PLANE + LOAD IMAGE
        Debug.Log("[QR] ►►► STEP 1: About to call CreateDisplayPlane");
        CreateDisplayPlane();
        Debug.Log("[QR] ►►► STEP 2: CreateDisplayPlane returned successfully");

        StartCoroutine(LoadImageFromURL(qr.text));
        Debug.Log("[QR] ►►► STEP 3: StartCoroutine called successfully");

        _qrDetected = true;
        Debug.Log("[QR] ►►► STEP 4: Set _qrDetected = true");

        _isScanning = false;
        Debug.Log("[QR] ►►► STEP 5: Set _isScanning = false");

        // IMMEDIATELY hide qrScanFrame to prevent blocking
        Debug.Log("[QR] ►►► STEP 6: About to hide qrScanFrame");
        if (qrScanFrame != null)
        {
            qrScanFrame.SetActive(false);
            Debug.Log("[QR] ⚠️ FORCE DISABLED qrScanFrame immediately after QR detection");
        }
        Debug.Log("[QR] ►►► STEP 7: qrScanFrame hidden");

        // Immediately deactivate the passthrough hole to prevent blocking
        Debug.Log("[QR] ►►► STEP 8: About to deactivate passthrough hole");

        // If _qrHoleObject is null, try to find it again by tag
        if (_qrHoleObject == null)
        {
            Debug.LogError("[QR] ►►► ►►► ►►► ERROR: _qrHoleObject is NULL! Trying to find by tag...");
            Debug.LogError($"[QR] ►►► ►►► ►►► Searching for tag: '{qrPassthroughTag}'");

            // Try to find all objects with this tag
            GameObject[] allWithTag = GameObject.FindGameObjectsWithTag(qrPassthroughTag);
            Debug.LogError($"[QR] ►►► ►►► ►►► Found {allWithTag.Length} objects with tag '{qrPassthroughTag}'");

            _qrHoleObject = GameObject.FindGameObjectWithTag(qrPassthroughTag);
            if (_qrHoleObject != null)
            {
                Debug.LogWarning($"[QR] ►►► ►►► ►►► Found hole object by tag! Name: {_qrHoleObject.name}, Scale: {_qrHoleObject.transform.lossyScale}");
            }
            else
            {
                Debug.LogError("[QR] ►►► ►►► ►►► CRITICAL: Could not find QR hole by tag!");
                Debug.LogError("[QR] ►►► ►►► ►►► This means either:");
                Debug.LogError("[QR] ►►► ►►► ►►► 1. The GameObject was destroyed");
                Debug.LogError("[QR] ►►► ►►► ►►► 2. The tag was removed/changed");
                Debug.LogError("[QR] ►►► ►►► ►►► 3. SetupQRPassthrough() never ran successfully");
            }
        }

        if (_qrHoleObject != null)
        {
            // CRITICAL: Capture the scale BEFORE deactivating!
            _qrHoleScaleAtDetection = _qrHoleObject.transform.lossyScale;
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Captured hole scale at detection: {_qrHoleScaleAtDetection}");

            _qrHoleObject.SetActive(false);
            Debug.Log("[QR] ✓ Passthrough hole deactivated immediately after QR detection");
        }
        else
        {
            Debug.LogError("[QR] ►►► ►►► ►►► ERROR: _qrHoleObject is STILL NULL after retry!");
            // Use the original scale as fallback
            _qrHoleScaleAtDetection = _qrHoleOriginalScale;
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Using _qrHoleOriginalScale as fallback: {_qrHoleScaleAtDetection}");

            // Check if the fallback scale is valid
            if (_qrHoleScaleAtDetection == Vector3.zero || _qrHoleScaleAtDetection.magnitude < 0.01f)
            {
                Debug.LogError("[QR] ►►► ►►► ►►► CRITICAL: Fallback scale is ZERO or too small!");
                Debug.LogError("[QR] ►►► ►►► ►►► The display plane will be invisible!");
                Debug.LogError("[QR] ►►► ►►► ►►► Setting emergency default scale...");
                _qrHoleScaleAtDetection = new Vector3(0.3f, 0.3f, 1.0f); // 30cm x 30cm emergency default
                Debug.LogWarning($"[QR] ►►► ►►► ►►► Emergency scale set to: {_qrHoleScaleAtDetection}");
            }
        }
        Debug.Log("[QR] ►►► STEP 9: Passthrough hole processing complete");

        // DEBUG: Temporarily disable button display to test raycast
        // The Canvas/buttons are blocking ISDK Physics raycasts
        // TODO: Buttons need to be ISDK-compatible (PokeInteractable, etc)

        Debug.LogWarning("[QR] ⚠️ BUTTONS DISABLED FOR RAYCAST TESTING");

        Debug.Log("[QR] ►►► STEP 10: About to show buttons");
        // Commenting out button activation to test raycast
        if (enterQRBtn != null)
        {
            enterQRBtn.SetActive(true);
            Debug.LogWarning("[QR] ►►► ►►► ►►► Enter button activated");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Enter button position: {enterQRBtn.transform.position}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Enter button layer: {LayerMask.LayerToName(enterQRBtn.layer)}");

            // Check what components it has
            var button = enterQRBtn.GetComponent<UnityEngine.UI.Button>();
            var pokeInteractable = enterQRBtn.GetComponent(System.Type.GetType("Oculus.Interaction.PokeInteractable"));

            Debug.LogWarning($"[QR] ►►► ►►► ►►► Has Unity Button: {button != null}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Has PokeInteractable: {pokeInteractable != null}");

            if (button != null)
            {
                Debug.LogWarning($"[QR] ►►► ►►► ►►► Button interactable: {button.interactable}");
            }
        }
        Debug.Log("[QR] ►►► STEP 11: Enter button processed");

        if (retakeQRBtn)
        {
            retakeQRBtn.SetActive(true);
            Debug.Log("[QR] ✓ Retake button shown");
        }
        Debug.Log("[QR] ►►► STEP 12: Retake button processed");

        // NOTE: Keeping script enabled so button clicks work
        // The Update() loop is prevented from interfering by commenting out lines 276-277

        Debug.Log("[QR] ═══════════════════════════════════════════");
        Debug.Log("[QR] QR DETECTED - Checking all GameObjects:");
        Debug.Log($"[QR]   _qrHoleObject: active={(_qrHoleObject != null ? _qrHoleObject.activeSelf.ToString() : "NULL")}, layer={(_qrHoleObject != null ? LayerMask.LayerToName(_qrHoleObject.layer) : "NULL")}");
        Debug.Log($"[QR]   qrScanFrame: active={qrScanFrame.activeSelf}");
        if (qrScanFrame != null)
        {
            var images = qrScanFrame.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            Debug.Log($"[QR]   qrScanFrame has {images.Length} Image components");
            foreach (var img in images)
            {
                Debug.Log($"[QR]     - {img.gameObject.name}: active={img.gameObject.activeSelf}, raycastTarget={img.raycastTarget}");
            }
        }
        Debug.Log($"[QR]   enterQRBtn: active={enterQRBtn.activeSelf}");
        Debug.Log($"[QR]   retakeQRBtn: active={retakeQRBtn.activeSelf}");

        // Log ALL active GameObjects with colliders in the scene
        var allColliders = FindObjectsOfType<Collider>();
        Debug.Log($"[QR] Total colliders in scene: {allColliders.Length}");
        foreach (var col in allColliders)
        {
            if (col.gameObject.activeSelf && col.enabled)
            {
                Debug.Log($"[QR]   Active Collider: {col.gameObject.name} (layer: {LayerMask.LayerToName(col.gameObject.layer)}, bounds: {col.bounds.size})");
            }
        }
        Debug.Log("[QR] ═══════════════════════════════════════════");
    }

    // ----------------------------------------------------------------------
    // CREATE DISPLAY PLANE (always follows head)
    // ----------------------------------------------------------------------
    private void CreateDisplayPlane()
    {
        Debug.Log("[QR] ★★★ CreateDisplayPlane called!");

        if (_displayPlane != null) Destroy(_displayPlane);

        _displayPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _planeRenderer = _displayPlane.GetComponent<MeshRenderer>();
        Destroy(_displayPlane.GetComponent<Collider>());

        // TESTING: Use Default layer to ensure it's visible
        // Move to Ignore Raycast layer to prevent blocking ISDK raycasts
        //int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        //if (ignoreRaycastLayer != -1)
        //{
        //    _displayPlane.layer = ignoreRaycastLayer;
        //    Debug.Log("[QR] ✓ Display plane set to 'Ignore Raycast' layer");
        //}

        // Use Default layer for now to test visibility
        _displayPlane.layer = 0; // Layer 0 = Default
        Debug.LogWarning("[QR] ►►► ►►► ►►► Display plane set to DEFAULT layer for testing");

        OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            Debug.LogError("[QR] ►►► ►►► ►►► CRITICAL: Cannot find OVRCameraRig or centerEyeAnchor!");
            return;
        }

        Transform cam = cameraRig.centerEyeAnchor;
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Camera found: {cam.name}, position: {cam.position}");

        // TESTING: Position in world space directly in front of camera, don't parent
        Vector3 worldPos = cam.position + cam.forward * fixedDistanceFromCamera;
        Quaternion worldRot = Quaternion.LookRotation(cam.forward, cam.up);

        _displayPlane.transform.position = worldPos;
        _displayPlane.transform.rotation = worldRot;

        Debug.LogWarning($"[QR] ►►► ►►► ►►► Plane positioned in WORLD SPACE at: {worldPos}");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► This is {fixedDistanceFromCamera}m in front of camera");

        // Start with zero scale - will be resized when texture loads
        _displayPlane.transform.localScale = Vector3.zero;

        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = new Color(0, 0, 0, 0); // Transparent initially
        mat.renderQueue = 5000; // Render on top
        _planeRenderer.material = mat;

        // Don't create border - not needed anymore
        // CreatePlaneBorder(_displayPlane, Color.green);

        Debug.Log($"[QR] ✓ Plane created at distance {fixedDistanceFromCamera}, initial scale {_displayPlane.transform.localScale}");
        Debug.Log("[QR] ✓ Plane will become visible when texture loads");
        Debug.LogWarning("[QR] ►►► ►►► ►►► ===== PLANE DEBUG INFO =====");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Plane GameObject: {_displayPlane.name}");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Plane active in hierarchy: {_displayPlane.activeInHierarchy}");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Renderer: {_planeRenderer}");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Renderer enabled: {_planeRenderer.enabled}");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Material: {_planeRenderer.material}");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Shader: {_planeRenderer.material.shader.name}");
        Debug.LogWarning($"[QR] ►►► ►►► ►►► Color: {_planeRenderer.material.color}");
        Debug.LogWarning("[QR] ►►► ►►► ►►► ===========================");

        // ALSO create a UI Canvas overlay as a fallback test
        // CreateTestUIOverlay(); // DISABLED - 3D quad is working now!
    }

    // ----------------------------------------------------------------------
    // LOAD IMAGE + SCALE TO MATCH HOLE
    // ----------------------------------------------------------------------
    private IEnumerator LoadImageFromURL(string url)
    {
        Debug.LogError($"[QR] ►►► COROUTINE STARTED - URL: {url}");
        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        Debug.Log("[QR] ►►► About to send web request");
        yield return www.SendWebRequest();
        Debug.Log("[QR] ►►► Web request returned");
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[QR] Failed download");
            yield break;
        }

        Debug.Log("[QR] ►►► Download successful, getting texture");
        Texture2D tex = DownloadHandlerTexture.GetContent(www);

        // Store texture for gesture capture
        _qrImageTexture = tex;
        Debug.Log("[QR] ✓ QR image texture stored for gesture capture");

        Shader shader =
            Shader.Find("Unlit/Texture") ??
            Shader.Find("Mobile/Unlit (Supports Lightmap)") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("UI/Default");

        Debug.LogWarning($"[QR] ►►► ►►► ►►► Selected shader: {shader.name}");

        Material m = new Material(shader);
        m.mainTexture = tex;

        m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        // CRITICAL: Make this render on top of everything, especially passthrough
        m.renderQueue = 5000; // Render queue for overlay (higher = renders later = on top)

        Debug.LogWarning($"[QR] ►►► ►►► ►►► Material renderQueue set to: {m.renderQueue}");

        _planeRenderer.material = m;
        _planeRenderer.enabled = false;
        _planeRenderer.enabled = true;
        Debug.Log("[QR] ►►► Texture applied to plane renderer");

        // Resize plane to match the hole's size at detection
        // Use the captured scale instead of reading from the (now deactivated) hole object
        Debug.LogWarning($"[QR] ►►► ►►► ►►► About to resize plane. _qrHoleScaleAtDetection = {_qrHoleScaleAtDetection}");

        if (_qrHoleScaleAtDetection != Vector3.zero)
        {
            Vector3 hole = _qrHoleScaleAtDetection;
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Using captured hole scale: {hole}");
            float aspect = (float)tex.width / tex.height;

            float height = hole.y;
            float width = height * aspect;

            _displayPlane.transform.localScale = new Vector3(width, height, 1f);
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Display plane resized to: {_displayPlane.transform.localScale}");

            // Make sure the plane is active and visible
            _displayPlane.SetActive(true);
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Display plane activated! Active: {_displayPlane.activeSelf}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Display plane position: {_displayPlane.transform.position}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Display plane rotation: {_displayPlane.transform.rotation.eulerAngles}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Display plane world scale: {_displayPlane.transform.lossyScale}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Display plane layer: {LayerMask.LayerToName(_displayPlane.layer)}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Renderer enabled: {_planeRenderer.enabled}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Material shader: {_planeRenderer.material.shader.name}");
            Debug.LogWarning($"[QR] ►►► ►►► ►►► Texture size: {tex.width}x{tex.height}");

            // Keep the image visible - it's not blocking the buttons
            // The button issue is something else
            Debug.LogWarning("[QR] ►►► ►►► ►►► Image will stay visible - investigating button issue");
        }
        else
        {
            Debug.LogError("[QR] ►►► ►►► ►►► ERROR: _qrHoleScaleAtDetection is ZERO! Cannot resize plane!");
            Debug.LogError($"[QR] ►►► ►►► ►►► Fallback - trying original scale: {_qrHoleOriginalScale}");

            // Fallback to original scale
            if (_qrHoleOriginalScale != Vector3.zero)
            {
                float aspect = (float)tex.width / tex.height;
                float height = _qrHoleOriginalScale.y;
                float width = height * aspect;
                _displayPlane.transform.localScale = new Vector3(width, height, 1f);
                _displayPlane.SetActive(true);
                Debug.LogWarning("[QR] ►►► ►►► ►►► Fallback resize complete!");
            }
        }
        Debug.LogWarning("[QR] ►►► ►►► ►►► COROUTINE COMPLETE");
    }

    // ----------------------------------------------------------------------
    // BUTTON HANDLERS
    // ----------------------------------------------------------------------
    public void OnEnterButtonClicked()
    {
        Debug.Log("[QR] ★★★ Enter button clicked!");

        // Play sound
        if (buttonClickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(buttonClickSound);
        }

        // Switch to TakingPicture mode - enables gesture capture
        _currentMode = QRMode.TakingPicture;
        Debug.Log("[QR] ✓ Mode switched to TakingPicture - gesture capture ENABLED");

        // Enable the gesture detector
        if (gestureDetector != null)
        {
            gestureDetector.enabled = true;
            Debug.LogWarning("[QR] ★★★ HandGestureDetector ENABLED ★★★");

            // Set QR texture on the frame if available
            if (_qrImageTexture != null)
            {
                // CRITICAL: Use a shader that properly supports mainTextureOffset and mainTextureScale
                // Sprites/Default does NOT work correctly with UV offset/scale!
                Shader shader = null;

                // Try multiple shader names
                string[] shaderNames = new string[]
                {
                    "Unlit/Texture",
                    "Legacy Shaders/Unlit/Texture",
                    "Mobile/Unlit (Supports Lightmap)",
                    "Hidden/BlitCopy",
                    "UI/Default",
                    "Particles/Standard Unlit"
                };

                // Force use Particles/Standard Unlit - this shader MUST work with textures
                shader = Shader.Find("Particles/Standard Unlit");
                if (shader != null)
                {
                    Debug.LogError($"[QR] ★★★ Using Particles/Standard Unlit shader ★★★");
                }
                else
                {
                    Debug.LogError($"[QR] ❌ Particles/Standard Unlit not found! Trying fallbacks...");

                    // Try fallbacks
                    foreach (string shaderName in shaderNames)
                    {
                        Debug.LogError($"[QR] Trying shader: {shaderName}");
                        shader = Shader.Find(shaderName);
                        if (shader != null)
                        {
                            Debug.LogError($"[QR] ★★★ FOUND SHADER: {shaderName} ★★★");
                            break;
                        }
                    }
                }

                if (shader != null)
                {
                    Material qrMaterial = new Material(shader);

                    // Set texture using both mainTexture and _MainTex (different shaders use different names)
                    qrMaterial.mainTexture = _qrImageTexture;
                    if (shader.name.Contains("Standard"))
                    {
                        // Standard shader needs additional setup
                        qrMaterial.SetTexture("_MainTex", _qrImageTexture);
                        qrMaterial.SetColor("_Color", Color.white);
                    }
                    else
                    {
                        // Try setting common texture property names
                        qrMaterial.SetTexture("_MainTex", _qrImageTexture);
                        qrMaterial.color = Color.white;
                    }

                    qrMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    qrMaterial.renderQueue = 3000;

                    // Set texture tiling and offset (will be updated by HandGestureDetector)
                    qrMaterial.mainTextureScale = new Vector2(1, 1);
                    qrMaterial.mainTextureOffset = new Vector2(0, 0);

                    // Debug the texture
                    Debug.LogWarning($"[QR] ★★★ QR texture applied to frame! ★★★");
                    Debug.LogWarning($"[QR]   Texture size: {_qrImageTexture.width}x{_qrImageTexture.height}");
                    Debug.LogWarning($"[QR]   Texture format: {_qrImageTexture.format}");
                    Debug.LogWarning($"[QR]   Shader: {shader.name}");
                    Debug.LogWarning($"[QR]   Material.mainTexture: {qrMaterial.mainTexture != null}");
                    Debug.LogWarning($"[QR]   Material.color: {qrMaterial.color}");

                    gestureDetector.SetQRFrameMaterial(qrMaterial);
                }
                else
                {
                    Debug.LogError("[QR] Could not find shader for QR material!");
                }
            }
            else
            {
                Debug.LogWarning("[QR] ★★★ No QR texture available - frame will show magenta ★★★");
            }
        }
        else
        {
            Debug.LogError("[QR] ❌ gestureDetector is NULL!");
        }

        // Hide the display plane to prevent blocking ray tracing to buttons
        if (_displayPlane != null)
        {
            _displayPlane.SetActive(false);
            Debug.Log("[QR] ✓ Display plane hidden to allow button interaction");
        }

        // Completely hide the passthrough hole - replace with preview
        if (_qrHoleObject != null)
        {
            _qrHoleObject.transform.localScale = Vector3.zero;
            Debug.Log("[QR] ✓ Passthrough hole hidden - preview will show instead");
        }

        // Create preview frame for thumb resize and show it immediately
        CreatePreviewFrame();

        // Show preview frame in center of view with initial scale
        if (_previewFrame != null && _passthroughCamera != null)
        {
            Transform cam = _passthroughCamera.transform;
            Vector3 centerPosition = cam.position + cam.forward * fixedDistanceFromCamera;
            Quaternion centerRotation = Quaternion.LookRotation(cam.forward, cam.up);
            Vector3 initialScale = new Vector3(0.3f, 0.3f, 1f); // Small initial preview size

            _previewFrame.transform.position = centerPosition;
            _previewFrame.transform.rotation = centerRotation;
            _previewFrame.transform.localScale = initialScale;
            _previewFrame.SetActive(true);

            Debug.LogWarning($"[QR] ★★★ INITIAL PREVIEW FRAME SETUP ★★★");
            Debug.LogWarning($"[QR] Position: {centerPosition}");
            Debug.LogWarning($"[QR] Rotation: {centerRotation.eulerAngles}");
            Debug.LogWarning($"[QR] Scale: {initialScale}");
            Debug.LogWarning($"[QR] Active: {_previewFrame.activeSelf}");
            Debug.LogWarning($"[QR] Renderer enabled: {_previewRenderer.enabled}");
            Debug.LogWarning($"[QR] Material: {_previewRenderer.material?.shader.name ?? "NULL"}");

            // Calculate initial UV mapping
            Vector2 uvOffset, uvScale;
            CalculateUVMappingFromPosition(centerPosition, centerRotation, initialScale, out uvOffset, out uvScale);
            if (_previewRenderer != null && _previewRenderer.material != null)
            {
                _previewRenderer.material.mainTextureOffset = uvOffset;
                _previewRenderer.material.mainTextureScale = uvScale;
                Debug.LogWarning($"[QR] Initial UV Offset: {uvOffset}, Scale: {uvScale}");
            }

            Debug.LogWarning("[QR] ★★★ Preview frame shown in center of view - ready for resize ★★★");
        }
        else
        {
            Debug.LogError($"[QR] ❌ Cannot show initial preview! _previewFrame: {_previewFrame != null}, _passthroughCamera: {_passthroughCamera != null}");
        }
    }

    public void OnRetakeButtonClicked()
    {
        Debug.Log("[QR] ★★★ Retake button clicked!");

        // Play sound
        if (buttonClickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(buttonClickSound);
        }

        // Reset to TakingQR mode - disables gesture capture
        _currentMode = QRMode.TakingQR;

        // Disable the gesture detector
        if (gestureDetector != null)
        {
            gestureDetector.enabled = false;
            Debug.Log("[QR] ✓ HandGestureDetector DISABLED");
        }

        // Reset detection
        _qrDetected = false;
        _currentQRUrl = "";
        _qrImageTexture = null;

        // Destroy display plane
        if (_displayPlane != null)
        {
            Destroy(_displayPlane);
            _displayPlane = null;
        }

        // Destroy preview frame
        if (_previewFrame != null)
        {
            Destroy(_previewFrame);
            _previewFrame = null;
        }

        // Hide buttons
        if (enterQRBtn) enterQRBtn.SetActive(false);
        if (retakeQRBtn) retakeQRBtn.SetActive(false);

        // Re-enable this script to allow scanning again
        this.enabled = true;

        // Resume scanning
        _isScanning = false;
        Debug.Log("[QR] ✓ Reset complete, ready to scan again");
    }

    /// <summary>
    /// Reset all captured planes - destroys them and clears the list
    /// Called by PanelNavigationManager.OnSelectReset()
    /// </summary>
    public void ResetCapturedPlanes()
    {
        Debug.Log($"[QR] Resetting {capturedPlanes.Count} captured planes");

        foreach (var plane in capturedPlanes)
        {
            if (plane != null)
            {
                Destroy(plane);
            }
        }

        capturedPlanes.Clear();
        Debug.Log("[QR] ✅ All captured planes cleared");
    }

    /// <summary>
    /// Get count of captured planes
    /// </summary>
    public int GetCapturedPlaneCount()
    {
        return capturedPlanes.Count;
    }

    // ----------------------------------------------------------------------
    // GESTURE CAPTURE CALLBACK
    // ----------------------------------------------------------------------
    private void OnGestureCapture(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        // Only capture if in RemixFoto or SaveFoto panel
        if (InstructionManager.Instance == null)
        {
            Debug.Log("[QR] InstructionManager not found, ignoring gesture");
            return;
        }

        bool inRemixFoto = InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.RemixFoto);
        bool inSaveFoto = InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.SaveFoto);

        if (!inRemixFoto && !inSaveFoto)
        {
            Debug.Log($"[QR] Not in RemixFoto or SaveFoto panel, ignoring gesture (current panel: {InstructionManager.Instance.currentPanel})");
            return;
        }

        Debug.Log($"[QR] ✓ Gesture capture allowed - in {(inRemixFoto ? "RemixFoto" : "SaveFoto")} panel");

        // Check mode - only capture in TakingPicture mode
        if (_currentMode != QRMode.TakingPicture)
        {
            Debug.Log($"[QR] Wrong mode ({_currentMode}) - gesture capture only works in TakingPicture mode");
            return;
        }

        if (_qrImageTexture == null)
        {
            Debug.LogWarning("[QR] No QR image texture available for capture!");
            return;
        }

        Debug.Log($"[QR] ★★★ Gesture capture triggered! Creating plane with QR image");
        CreateCapturedPlane(position, rotation, scale);
    }

    // ----------------------------------------------------------------------
    // CREATE CAPTURED PLANE WITH QR IMAGE
    // ----------------------------------------------------------------------
    private void CreateCapturedPlane(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Debug.Log($"[QR] Creating captured plane at pos:{position} scale:{scale}");

        GameObject capturedPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
        capturedPlane.name = $"QR_CapturedPlane_{capturedPlanes.Count}";

        // Remove collider
        Destroy(capturedPlane.GetComponent<Collider>());

        // Set transform
        capturedPlane.transform.position = position;
        capturedPlane.transform.rotation = rotation;
        capturedPlane.transform.localScale = scale;

        // Calculate UV mapping based on spatial position
        Vector2 uvOffset, uvScale;
        CalculateUVMappingFromPosition(position, rotation, scale, out uvOffset, out uvScale);

        // Create material with QR image texture
        MeshRenderer renderer = capturedPlane.GetComponent<MeshRenderer>();

        Shader shader = Shader.Find("Particles/Standard Unlit") ??
                       Shader.Find("Unlit/Texture") ??
                       Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.mainTexture = _qrImageTexture;

        // Apply UV offset and scale to show only the portion of the image for this spatial location
        mat.mainTextureOffset = uvOffset;
        mat.mainTextureScale = uvScale;

        // If using Standard shader, use emission for visibility in passthrough
        if (shader.name == "Standard")
        {
            mat.SetColor("_Color", Color.black);
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", _qrImageTexture);
            mat.SetColor("_EmissionColor", Color.white * 3f);
        }

        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.renderQueue = 5000; // Render above passthrough

        renderer.material = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        capturedPlanes.Add(capturedPlane);

        Debug.Log($"[QR] ✓✓✓ Captured plane #{capturedPlanes.Count} created with UV offset:{uvOffset} scale:{uvScale}");

        // Play sound
        if (qrDetectedSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(qrDetectedSound);
        }
    }

    // ----------------------------------------------------------------------
    // CALCULATE UV MAPPING FROM SPATIAL POSITION
    // Treats the image as wrapped around a sphere - revealing portions of it
    // ----------------------------------------------------------------------
    private void CalculateUVMappingFromPosition(Vector3 position, Quaternion rotation, Vector3 scale, out Vector2 uvOffset, out Vector2 uvScale)
    {
        // Get camera position (center of the imaginary sphere)
        Transform cameraTransform = _passthroughCamera != null ? _passthroughCamera.transform : Camera.main.transform;
        Vector3 sphereCenter = cameraTransform.position;

        // Calculate direction from sphere center to the plane (this is where we're "looking")
        Vector3 directionToPlane = (position - sphereCenter).normalized;

        // Convert 3D direction to spherical coordinates for equirectangular mapping
        // Yaw (longitude): horizontal angle around the sphere (0-360°)
        // Pitch (latitude): vertical angle from equator (-90° to +90°)
        float yaw = Mathf.Atan2(directionToPlane.x, directionToPlane.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(directionToPlane.y) * Mathf.Rad2Deg;

        // Map to UV coordinates (0-1 range)
        // For equirectangular projection (360° panorama):
        float u = (yaw + 180f) / 360f;  // Horizontal: -180° to +180° → 0 to 1
        float v = (pitch + 90f) / 180f;  // Vertical: -90° to +90° → 0 to 1

        // Calculate the angular size of the plane (how much of the sphere it covers)
        float distance = Vector3.Distance(sphereCenter, position);

        // Get the four corners of the quad in world space
        Vector3[] corners = new Vector3[4];
        corners[0] = position + rotation * new Vector3(-scale.x/2, -scale.y/2, 0); // Bottom-left
        corners[1] = position + rotation * new Vector3( scale.x/2, -scale.y/2, 0); // Bottom-right
        corners[2] = position + rotation * new Vector3(-scale.x/2,  scale.y/2, 0); // Top-left
        corners[3] = position + rotation * new Vector3( scale.x/2,  scale.y/2, 0); // Top-right

        // Calculate UV coordinates for each corner
        float minU = 1f, maxU = 0f, minV = 1f, maxV = 0f;

        foreach (Vector3 corner in corners)
        {
            Vector3 dirToCorner = (corner - sphereCenter).normalized;
            float cornerYaw = Mathf.Atan2(dirToCorner.x, dirToCorner.z) * Mathf.Rad2Deg;
            float cornerPitch = Mathf.Asin(dirToCorner.y) * Mathf.Rad2Deg;

            float cornerU = (cornerYaw + 180f) / 360f;
            float cornerV = (cornerPitch + 90f) / 180f;

            minU = Mathf.Min(minU, cornerU);
            maxU = Mathf.Max(maxU, cornerU);
            minV = Mathf.Min(minV, cornerV);
            maxV = Mathf.Max(maxV, cornerV);
        }

        // Handle wrap-around at 0°/360° boundary
        if (maxU - minU > 0.5f) // Wrapped around
        {
            // Recalculate treating 0-180 as 1.0-1.5 range
            minU = 1f; maxU = 0f;
            foreach (Vector3 corner in corners)
            {
                Vector3 dirToCorner = (corner - sphereCenter).normalized;
                float cornerYaw = Mathf.Atan2(dirToCorner.x, dirToCorner.z) * Mathf.Rad2Deg;
                float cornerU = (cornerYaw + 180f) / 360f;
                if (cornerU < 0.5f) cornerU += 1f; // Wrap values near 0

                minU = Mathf.Min(minU, cornerU);
                maxU = Mathf.Max(maxU, cornerU);
            }
            if (minU > 1f) minU -= 1f;
            if (maxU > 1f) maxU -= 1f;
        }

        // Calculate UV offset (bottom-left corner) and scale (size)
        uvOffset = new Vector2(minU, minV);
        uvScale = new Vector2(maxU - minU, maxV - minV);

        Debug.Log($"[QR] Sphere UV Mapping - Center: U={u:F3}, V={v:F3} | Offset:{uvOffset} Scale:{uvScale} | Yaw:{yaw:F1}° Pitch:{pitch:F1}°");
    }

    // ----------------------------------------------------------------------
    // PREVIEW FRAME FOR THUMB RESIZE
    // ----------------------------------------------------------------------
    private void CreatePreviewFrame()
    {
        if (_previewFrame != null)
        {
            Destroy(_previewFrame);
        }

        _previewFrame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _previewFrame.name = "QR_PreviewFrame";
        _previewRenderer = _previewFrame.GetComponent<MeshRenderer>();

        // Remove collider
        Destroy(_previewFrame.GetComponent<Collider>());

        // CRITICAL: Set to DEFAULT layer so it's rendered by main camera
        // "Ignore Raycast" might cause rendering issues
        _previewFrame.layer = LayerMask.NameToLayer("Default");
        Debug.LogWarning($"[QR] ★★★ Preview frame set to layer: {LayerMask.LayerToName(_previewFrame.layer)} ★★★");

        // Create material with QR image texture using UNLIT shader for maximum visibility
        if (_qrImageTexture != null)
        {
            // Use Unlit/Texture for guaranteed visibility - no lighting required!
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogError("[QR] Unlit/Texture shader not found! Trying Sprites/Default");
                shader = Shader.Find("Sprites/Default");
            }

            Material mat = new Material(shader);
            mat.mainTexture = _qrImageTexture;
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

            // CRITICAL: Render queue MUST be MUCH higher than passthrough (3000) and HandGestureDetector frame
            // Passthrough typically renders at ~3000, so we use 6000 to be safe
            mat.renderQueue = 6000; // VERY HIGH - render on top of EVERYTHING
            mat.color = Color.white; // Full brightness

            _previewRenderer.material = mat;
            _previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _previewRenderer.receiveShadows = false;

            Debug.LogWarning("[QR] ★★★ Preview frame using Unlit/Texture shader with renderQueue=6000 ★★★");
            Debug.LogWarning($"[QR] ★★★ PREVIEW FRAME SETUP - Material: {mat.shader.name}, RenderQueue: {mat.renderQueue}, Texture: {_qrImageTexture.width}x{_qrImageTexture.height} ★★★");
        }
        else
        {
            Debug.LogError("[QR] ❌ Cannot create preview frame - _qrImageTexture is NULL!");
            Debug.LogError("[QR] ❌ Creating BRIGHT RED fallback material for testing!");

            // Create a BRIGHT RED fallback so we can see if the quad is rendering at all
            Shader fallbackShader = Shader.Find("Unlit/Color");
            Material fallbackMat = new Material(fallbackShader);
            fallbackMat.color = Color.red;
            fallbackMat.renderQueue = 6000;
            _previewRenderer.material = fallbackMat;
            Debug.LogError("[QR] ❌ Preview frame should now be BRIGHT RED!");
        }

        // CRITICAL: Add a visible border so user can see the preview frame boundaries!
        CreatePreviewFrameBorder();

        // ALSO: Create corner spheres for absolute visibility
        CreateCornerSpheres();

        Debug.Log("[QR] ✓ Preview frame created - ready to show texture");
    }

    /// <summary>
    /// Create bright colored spheres at each corner of the preview frame
    /// These are 3D objects that WILL be visible no matter what
    /// </summary>
    private void CreateCornerSpheres()
    {
        Vector3[] cornerPositions = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, -0.02f), // Bottom-left
            new Vector3(0.5f, -0.5f, -0.02f),  // Bottom-right
            new Vector3(0.5f, 0.5f, -0.02f),   // Top-right
            new Vector3(-0.5f, 0.5f, -0.02f)   // Top-left
        };

        Color[] cornerColors = new Color[]
        {
            Color.red,    // Bottom-left = RED
            Color.green,  // Bottom-right = GREEN
            Color.blue,   // Top-right = BLUE
            Color.yellow  // Top-left = YELLOW
        };

        for (int i = 0; i < 4; i++)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"PreviewCorner_{i}";
            sphere.transform.SetParent(_previewFrame.transform);
            sphere.transform.localPosition = cornerPositions[i];
            sphere.transform.localScale = Vector3.one * 0.03f; // 3cm diameter spheres

            // Remove collider
            Destroy(sphere.GetComponent<Collider>());

            // Make it VERY visible with emission
            MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
            Material mat = new Material(Shader.Find("Standard"));
            mat.SetColor("_Color", Color.black);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", cornerColors[i] * 10f); // VERY BRIGHT
            mat.renderQueue = 6002; // Render on top
            renderer.material = mat;

            Debug.LogWarning($"[QR] ★★★ Created {cornerColors[i]} corner sphere at {cornerPositions[i]} ★★★");
        }

        Debug.LogWarning("[QR] ★★★ 4 BRIGHT CORNER SPHERES added! RED=BL, GREEN=BR, BLUE=TR, YELLOW=TL ★★★");
    }

    /// <summary>
    /// Create a bright green border around the preview frame so it's visible during resizing
    /// </summary>
    private void CreatePreviewFrameBorder()
    {
        GameObject borderObj = new GameObject("PreviewFrame_Border");
        borderObj.transform.SetParent(_previewFrame.transform);
        borderObj.transform.localPosition = Vector3.zero;
        borderObj.transform.localRotation = Quaternion.identity;
        borderObj.transform.localScale = Vector3.one;

        LineRenderer lineRenderer = borderObj.AddComponent<LineRenderer>();

        // Configure LineRenderer for bright visibility
        Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material = lineMaterial;
        lineRenderer.startColor = Color.green;
        lineRenderer.endColor = Color.green;
        lineRenderer.startWidth = 0.01f; // 1cm thick border
        lineRenderer.endWidth = 0.01f;
        lineRenderer.positionCount = 5; // 4 corners + back to start
        lineRenderer.useWorldSpace = false; // Use local space
        lineRenderer.loop = true;

        // Set render queue to be on top of everything
        lineRenderer.material.renderQueue = 6001; // Higher than preview frame (6000)

        // Define border corners (in local space of the quad, which is a 1x1 quad)
        Vector3[] corners = new Vector3[5];
        corners[0] = new Vector3(-0.5f, -0.5f, -0.01f); // Bottom-left, slightly in front
        corners[1] = new Vector3(0.5f, -0.5f, -0.01f);  // Bottom-right
        corners[2] = new Vector3(0.5f, 0.5f, -0.01f);   // Top-right
        corners[3] = new Vector3(-0.5f, 0.5f, -0.01f);  // Top-left
        corners[4] = new Vector3(-0.5f, -0.5f, -0.01f); // Back to start

        lineRenderer.SetPositions(corners);

        Debug.LogWarning("[QR] ★★★ BRIGHT GREEN BORDER added to preview frame! ★★★");
    }

    /// <summary>
    /// Called continuously while user is resizing frame with thumbs
    /// Updates preview frame to show the portion of the QR image they're capturing
    /// </summary>
    private void OnFrameResize(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        // Log every call (not just occasionally) for now
        Debug.LogWarning($"[QR] ★★★ OnFrameResize CALLED! Mode: {_currentMode}, Pos: {position}, Scale: {scale} ★★★");

        // Only show preview in TakingPicture mode
        if (_currentMode != QRMode.TakingPicture)
        {
            Debug.LogError($"[QR] ❌ OnFrameResize SKIPPED - Wrong mode! Current: {_currentMode}, Need: TakingPicture");
            return;
        }

        Debug.LogWarning("[QR] ★★★ OnFrameResize proceeding - Mode is correct! ★★★");

        // Check required components
        if (_previewFrame == null)
        {
            Debug.LogWarning("[QR] OnFrameResize: _previewFrame is NULL - preview cannot be shown!");
            return;
        }

        if (_qrImageTexture == null)
        {
            Debug.LogWarning("[QR] OnFrameResize: _qrImageTexture is NULL - no texture to show!");
            return;
        }

        if (_displayPlane == null)
        {
            Debug.LogWarning("[QR] OnFrameResize: _displayPlane is NULL - cannot calculate UV mapping!");
            return;
        }

        // Show and position the preview frame
        _previewFrame.SetActive(true);
        _previewFrame.transform.position = position;
        _previewFrame.transform.rotation = rotation;
        _previewFrame.transform.localScale = scale;

        // Calculate what portion of the QR image texture will be cropped
        // Use the SAME calculation as when capturing (sphere projection to equirectangular UV)
        Vector2 uvOffset, uvScale;
        CalculateUVMappingFromPosition(position, rotation, scale, out uvOffset, out uvScale);

        // Update material to show the cropped portion as preview
        if (_previewRenderer != null && _previewRenderer.material != null)
        {
            _previewRenderer.material.mainTextureOffset = uvOffset;
            _previewRenderer.material.mainTextureScale = uvScale;

            // AGGRESSIVE LOGGING - every 30 frames (0.5 seconds at 60fps)
            if (Time.frameCount % 30 == 0)
            {
                Debug.LogWarning($"[QR] ★★★ PREVIEW FRAME STATUS ★★★");
                Debug.LogWarning($"[QR] Position: {position}, Scale: {scale}");
                Debug.LogWarning($"[QR] UV Offset: {uvOffset}, UV Scale: {uvScale}");
                Debug.LogWarning($"[QR] Frame Active: {_previewFrame.activeSelf}, InHierarchy: {_previewFrame.activeInHierarchy}");
                Debug.LogWarning($"[QR] Renderer Enabled: {_previewRenderer.enabled}");
                Debug.LogWarning($"[QR] Material: {_previewRenderer.material.shader.name}");
                Debug.LogWarning($"[QR] RenderQueue: {_previewRenderer.material.renderQueue}");
                Debug.LogWarning($"[QR] Texture: {(_previewRenderer.material.mainTexture != null ? $"{_previewRenderer.material.mainTexture.width}x{_previewRenderer.material.mainTexture.height}" : "NULL")}");
                Debug.LogWarning($"[QR] World Position: {_previewFrame.transform.position}");
                Debug.LogWarning($"[QR] World Scale: {_previewFrame.transform.lossyScale}");
                Debug.LogWarning($"[QR] Layer: {LayerMask.LayerToName(_previewFrame.layer)}");
            }
        }
        else
        {
            Debug.LogError("[QR] OnFrameResize: _previewRenderer or material is NULL!");
        }

        // Note: HandGestureDetector now computes UVs internally in UpdateQRFrameFromFingers
        // No need to set it here
    }

    // ----------------------------------------------------------------------
    // CALCULATE FLAT CROP UVs
    // Projects frame corners onto the display plane to show crop preview
    // ----------------------------------------------------------------------
    private void CalculateFlatCropUVs(Vector3 framePos, Quaternion frameRot, Vector3 frameScale, out Vector2 uvOffset, out Vector2 uvScale)
    {
        // Default values
        uvOffset = Vector2.zero;
        uvScale = Vector2.one;

        if (_displayPlane == null)
        {
            return;
        }

        // Get display plane properties
        Transform planeTransform = _displayPlane.transform;
        Vector3 planePos = planeTransform.position;
        Vector3 planeNormal = planeTransform.forward;
        Vector3 planeRight = planeTransform.right;
        Vector3 planeUp = planeTransform.up;
        Vector3 planeScale = planeTransform.lossyScale;

        // Calculate the 4 corners of the frame in world space
        Vector3[] frameCorners = new Vector3[4];
        frameCorners[0] = framePos + frameRot * new Vector3(-frameScale.x/2, -frameScale.y/2, 0); // Bottom-left
        frameCorners[1] = framePos + frameRot * new Vector3( frameScale.x/2, -frameScale.y/2, 0); // Bottom-right
        frameCorners[2] = framePos + frameRot * new Vector3( frameScale.x/2,  frameScale.y/2, 0); // Top-right
        frameCorners[3] = framePos + frameRot * new Vector3(-frameScale.x/2,  frameScale.y/2, 0); // Top-left

        // Project each frame corner onto the display plane and get local UV coordinates
        float minU = float.MaxValue, maxU = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;

        foreach (Vector3 corner in frameCorners)
        {
            // Project corner onto plane
            Vector3 toCorner = corner - planePos;
            float distance = Vector3.Dot(toCorner, planeNormal);
            Vector3 projectedPoint = corner - planeNormal * distance;

            // Convert to local plane coordinates (-0.5 to 0.5)
            Vector3 localPoint = projectedPoint - planePos;
            float localX = Vector3.Dot(localPoint, planeRight) / planeScale.x;
            float localY = Vector3.Dot(localPoint, planeUp) / planeScale.y;

            // Convert to UV coordinates (0 to 1)
            float u = localX + 0.5f;
            float v = localY + 0.5f;

            minU = Mathf.Min(minU, u);
            maxU = Mathf.Max(maxU, u);
            minV = Mathf.Min(minV, v);
            maxV = Mathf.Max(maxV, v);
        }

        // Clamp to valid UV range
        minU = Mathf.Clamp01(minU);
        maxU = Mathf.Clamp01(maxU);
        minV = Mathf.Clamp01(minV);
        maxV = Mathf.Clamp01(maxV);

        // Calculate UV offset and scale for material
        uvOffset = new Vector2(minU, minV);
        uvScale = new Vector2(maxU - minU, maxV - minV);

        Debug.Log($"[QR] Flat crop UVs - Min: ({minU:F3}, {minV:F3}), Max: ({maxU:F3}, {maxV:F3})");
    }

    // ----------------------------------------------------------------------
    // CLEANUP
    // ----------------------------------------------------------------------
    private void OnDisable()
    {
        Debug.Log("[QR] OnDisable called - cleaning up passthrough surface");

        // Disable qrScanFrame's passthrough layer when this component is disabled (panel switching)
        if (qrScanFramePassthroughLayer != null)
        {
            qrScanFramePassthroughLayer.enabled = false;
            Debug.Log("[QR] ✓ qrScanFramePassthroughLayer disabled in OnDisable");
        }

        CleanupPassthroughHole();
    }

    private void OnDestroy()
    {
        Debug.Log("[QR] OnDestroy called - final cleanup");

        if (gestureDetector != null)
        {
            gestureDetector.OnFlickerGesture -= OnGestureCapture;
            gestureDetector.OnQRFrameActive -= OnFrameResize;
        }

        CleanupPassthroughHole();
    }

    /// <summary>
    /// Clean up passthrough hole to prevent accumulation when switching panels
    /// DON'T destroy the GameObject - just deactivate and unregister from passthrough
    /// </summary>
    private void CleanupPassthroughHole()
    {
        if (_qrHoleObject != null && passthroughLayer != null)
        {
            try
            {
                passthroughLayer.RemoveSurfaceGeometry(_qrHoleObject);
                Debug.Log("[QR] ✓ Removed passthrough surface geometry");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QR] Failed to remove surface geometry: {e.Message}");
            }

            // Deactivate but don't destroy (so it can be found again with tag)
            _qrHoleObject.SetActive(false);
            Debug.Log("[QR] ✓ Deactivated passthrough hole GameObject");
        }
    }

    // ----------------------------------------------------------------------
    private bool IsUrl(string text)
    {
        return text.StartsWith("http://") || text.StartsWith("https://");
    }

    private void StopScanning() => _isScanning = false;

    // Helper to set layer recursively on GameObject and all children
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    // Create a visible border around a plane using LineRenderer
    private void CreatePlaneBorder(GameObject plane, Color borderColor)
    {
        GameObject borderObj = new GameObject("PlaneBorder");
        borderObj.transform.SetParent(plane.transform);
        borderObj.transform.localPosition = Vector3.zero;
        borderObj.transform.localRotation = Quaternion.identity;
        borderObj.transform.localScale = Vector3.one;

        LineRenderer lineRenderer = borderObj.AddComponent<LineRenderer>();

        // Configure LineRenderer
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;
        lineRenderer.startWidth = 0.01f; // 1cm thick border
        lineRenderer.endWidth = 0.01f;
        lineRenderer.positionCount = 5; // 4 corners + back to start
        lineRenderer.useWorldSpace = false; // Use local space
        lineRenderer.loop = true;

        // Set render queue to be on top
        lineRenderer.material.renderQueue = 5001; // Above the plane

        // Define border corners (in local space of the plane, which is a 1x1 quad)
        Vector3[] corners = new Vector3[5];
        corners[0] = new Vector3(-0.5f, -0.5f, -0.01f); // Bottom-left, slightly in front
        corners[1] = new Vector3(0.5f, -0.5f, -0.01f);  // Bottom-right
        corners[2] = new Vector3(0.5f, 0.5f, -0.01f);   // Top-right
        corners[3] = new Vector3(-0.5f, 0.5f, -0.01f);  // Top-left
        corners[4] = new Vector3(-0.5f, -0.5f, -0.01f); // Back to start

        lineRenderer.SetPositions(corners);

        Debug.LogWarning("[QR] ►►► ►►► ►►► Created GREEN BORDER around plane!");
    }

    // Create a UI Canvas overlay - this WILL be visible in VR passthrough!
    private void CreateTestUIOverlay()
    {
        // Create Canvas
        _displayCanvasUI = new GameObject("TestUIOverlay");
        Canvas canvas = _displayCanvasUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = _displayCanvasUI.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;

        // Position in front of camera
        OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
        if (cameraRig != null)
        {
            Transform cam = cameraRig.centerEyeAnchor;
            Vector3 worldPos = cam.position + cam.forward * fixedDistanceFromCamera;
            Quaternion worldRot = Quaternion.LookRotation(cam.forward, cam.up);

            _displayCanvasUI.transform.position = worldPos;
            _displayCanvasUI.transform.rotation = worldRot;
        }

        // Set canvas size
        RectTransform canvasRect = _displayCanvasUI.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(500, 500); // 500x500 pixels

        // Create bright red image on the canvas
        GameObject imageObj = new GameObject("TestImage");
        imageObj.transform.SetParent(_displayCanvasUI.transform, false);

        UnityEngine.UI.Image image = imageObj.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.yellow; // BRIGHT YELLOW

        RectTransform imageRect = imageObj.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.sizeDelta = Vector2.zero; // Fill entire canvas

        Debug.LogWarning("[QR] ►►► ►►► ►►► Created YELLOW UI CANVAS OVERLAY!");
        Debug.LogWarning("[QR] ►►► ►►► ►►► If you can't see this, then UI rendering is broken!");
    }

    /// <summary>
    /// Save all captured images to persistent storage WITH metadata (position, rotation, scale)
    /// </summary>
    /// <param name="customFileName">Custom base file name, or null for default numbering</param>
    /// <returns>Number of images saved</returns>
    public int SaveAllCaptures(string customFileName = null)
    {
        // Clean up null references
        capturedPlanes.RemoveAll(plane => plane == null);

        Debug.LogError($"[LIVE] ★★★ SaveAllCaptures called - capturedPlanes.Count = {capturedPlanes.Count}");

        if (capturedPlanes.Count == 0)
        {
            Debug.LogError("[LIVE] ❌ No captured images to save! capturedPlanes list is empty!");
            return 0;
        }

        int savedCount = 0;
        string baseName = string.IsNullOrEmpty(customFileName) ? "Image" : customFileName;

        // Create scene data to store all metadata
        SceneData sceneData = new SceneData();

        for (int i = 0; i < capturedPlanes.Count; i++)
        {
            GameObject plane = capturedPlanes[i];
            Debug.LogError($"[LIVE] Processing plane {i}: {plane.name}");

            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();

            if (renderer == null || renderer.material == null || renderer.material.mainTexture == null)
            {
                Debug.LogError($"[LIVE] ❌ Plane {i} has no valid texture - Renderer:{renderer != null} Material:{renderer?.material != null} Texture:{renderer?.material?.mainTexture != null}");
                continue;
            }

            Texture2D texture = renderer.material.mainTexture as Texture2D;
            if (texture == null)
            {
                Debug.LogError($"[LIVE] ❌ Plane {i} texture is not Texture2D (it's {renderer.material.mainTexture.GetType().Name})");

                // Try to convert RenderTexture to Texture2D
                if (renderer.material.mainTexture is RenderTexture renderTexture)
                {
                    Debug.LogError($"[LIVE] Texture is RenderTexture, converting to Texture2D...");
                    texture = RenderTextureToTexture2D(renderTexture);
                }
                else
                {
                    continue;
                }
            }

            // Check if texture is readable
            try
            {
                texture.GetPixel(0, 0); // Test if readable
                Debug.LogError($"[LIVE] ✓ Texture {i} is readable: {texture.width}x{texture.height}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LIVE] ❌ Texture {i} is NOT READABLE: {e.Message}");
                Debug.LogError($"[LIVE] Attempting to create readable copy...");

                // Create a readable copy
                texture = CreateReadableTexture(texture);
            }

            // Get UV offset and scale from the material to crop the texture
            Vector2 uvOffset = renderer.material.mainTextureOffset;
            Vector2 uvScale = renderer.material.mainTextureScale;

            Debug.LogError($"[LIVE] UV Offset: {uvOffset}, UV Scale: {uvScale}");

            // Crop the texture based on UV mapping (like SoloFoto does)
            Texture2D croppedTexture = CropTextureByUV(texture, uvOffset, uvScale);
            Debug.LogError($"[LIVE] ✓ Cropped texture from {texture.width}x{texture.height} to {croppedTexture.width}x{croppedTexture.height}");

            // Generate file name
            string fileName = $"{baseName}_{i + 1}.png";
            string filePath = System.IO.Path.Combine(Application.persistentDataPath, fileName);

            try
            {
                // Encode the CROPPED texture to PNG
                byte[] bytes = croppedTexture.EncodeToPNG();

                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogError($"[LIVE] ❌ EncodeToPNG returned null or empty bytes for {fileName}");
                    continue;
                }

                System.IO.File.WriteAllBytes(filePath, bytes);

                // Store metadata
                ImageMetadata metadata = new ImageMetadata
                {
                    imagePath = fileName,
                    position = plane.transform.position,
                    rotation = plane.transform.rotation,
                    scale = plane.transform.localScale
                };
                sceneData.images.Add(metadata);

                savedCount++;
                Debug.LogError($"[LIVE] ✓✓✓ SAVED: {filePath} ({bytes.Length} bytes)");
                Debug.LogError($"[LIVE]   Position: {metadata.position}, Rotation: {metadata.rotation.eulerAngles}, Scale: {metadata.scale}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LIVE] ❌❌❌ Failed to save {fileName}: {e.Message}");
                Debug.LogError($"[LIVE] Stack trace: {e.StackTrace}");
            }
        }

        // Save metadata JSON file
        if (savedCount > 0)
        {
            string metadataFileName = $"{baseName}_metadata.json";
            string metadataPath = System.IO.Path.Combine(Application.persistentDataPath, metadataFileName);

            try
            {
                string json = JsonUtility.ToJson(sceneData, true);
                System.IO.File.WriteAllText(metadataPath, json);
                Debug.LogError($"[LIVE] ✓✓✓ SAVED METADATA: {metadataPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LIVE] ❌ Failed to save metadata: {e.Message}");
            }
        }

        Debug.LogError($"[LIVE] ✅✅✅ FINAL RESULT: Saved {savedCount}/{capturedPlanes.Count} images to {Application.persistentDataPath}");
        return savedCount;
    }

    // Helper method to convert RenderTexture to Texture2D
    private Texture2D RenderTextureToTexture2D(RenderTexture renderTexture)
    {
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        return texture;
    }

    // Helper method to create a readable copy of a texture
    private Texture2D CreateReadableTexture(Texture2D source)
    {
        // Create a temporary RenderTexture
        RenderTexture tmp = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear);

        // Blit the source texture to the RenderTexture
        Graphics.Blit(source, tmp);

        // Read the RenderTexture into a new Texture2D
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmp;

        Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        readable.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmp);

        return readable;
    }

    // Crop texture based on UV offset and scale (matches the visual crop shown on the plane)
    private Texture2D CropTextureByUV(Texture2D source, Vector2 uvOffset, Vector2 uvScale)
    {
        // Convert UV coordinates to pixel coordinates
        int sourceWidth = source.width;
        int sourceHeight = source.height;

        // Calculate pixel coordinates from UV (0-1 range)
        int startX = Mathf.RoundToInt(uvOffset.x * sourceWidth);
        int startY = Mathf.RoundToInt(uvOffset.y * sourceHeight);
        int cropWidth = Mathf.RoundToInt(uvScale.x * sourceWidth);
        int cropHeight = Mathf.RoundToInt(uvScale.y * sourceHeight);

        // Clamp to valid ranges
        startX = Mathf.Clamp(startX, 0, sourceWidth - 1);
        startY = Mathf.Clamp(startY, 0, sourceHeight - 1);
        cropWidth = Mathf.Clamp(cropWidth, 1, sourceWidth - startX);
        cropHeight = Mathf.Clamp(cropHeight, 1, sourceHeight - startY);

        Debug.LogError($"[LIVE] Crop region: X={startX}, Y={startY}, W={cropWidth}, H={cropHeight}");

        // Create a new texture with the cropped size
        Texture2D croppedTexture = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false);

        // Copy pixels from the source texture to the cropped texture
        Color[] pixels = source.GetPixels(startX, startY, cropWidth, cropHeight);
        croppedTexture.SetPixels(pixels);
        croppedTexture.Apply();

        return croppedTexture;
    }

#endif
}

// Serializable classes for saving metadata
[System.Serializable]
public class ImageMetadata
{
    public string imagePath;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
}

[System.Serializable]
public class SceneData
{
    public List<ImageMetadata> images = new List<ImageMetadata>();
}
