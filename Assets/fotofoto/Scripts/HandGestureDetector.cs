using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Universal gesture detection system for hand tracking
/// Detects middle finger frame positioning and index finger flicker gestures
/// Other scripts can subscribe to events to respond to gestures
/// </summary>
public class HandGestureDetector : MonoBehaviour
{
    // =========================================================
    //  Inspector References
    // =========================================================
    [Header("Hand Tracking")]
    public OVRSkeleton leftSkeleton;
    public OVRSkeleton rightSkeleton;
    public OVRHand leftOVRHand;
    public OVRHand rightOVRHand;

    [Header("Frame Settings")]
    public Transform frameQuad; // For SoloFoto - passthrough frame
    [Range(0f, 0.1f)]
    public float cornerInset = 0.03f; // How much to inset corners (in meters) to avoid capturing hands

    [Header("Passthrough Integration")]
    public OVRPassthroughLayer passthroughLayer; // For registering passthrough frames

    // QR Frame (RemixFoto) - separate from passthrough frame
    private GameObject qrFrameQuad; // Dedicated quad for RemixFoto (never registered with passthrough)
    private MeshRenderer qrFrameRenderer;
    private Material qrFrameMaterial; // Material for QR frame (magenta or QR texture)

    // =========================================================
    //  Events - Other scripts subscribe to these
    // =========================================================
    /// <summary>
    /// Fired when both middle fingers are positioned (frame is active)
    /// Provides frame position, rotation, and scale
    /// </summary>
    public event Action<Vector3, Quaternion, Vector3> OnFrameActive;

    /// <summary>
    /// Fired specifically for QR preview updates. This event is invoked every
    /// frame while the hand-frame is active and provides the frame transform
    /// without applying any camera offset — useful for projecting textures
    /// (like a QR image) onto a preview quad.
    /// </summary>
    public event Action<Vector3, Quaternion, Vector3> OnQRFrameActive;

    /// <summary>
    /// Fired when middle fingers are no longer positioned (frame deactivated)
    /// </summary>
    public event Action OnFrameInactive;

    /// <summary>
    /// Fired when index finger flicker gesture is detected (capture trigger)
    /// Provides frame position, rotation, and scale at time of gesture
    /// </summary>
    public event Action<Vector3, Quaternion, Vector3> OnFlickerGesture;

    // =========================================================
    //  State
    // =========================================================
    private HandData leftHand;
    private HandData rightHand;

    private bool isFrameActive = false;
    private Vector3 currentFramePosition;
    private Quaternion currentFrameRotation;
    private Vector3 currentFrameScale;

    public bool Ready => leftHand != null && rightHand != null && leftHand.Ready && rightHand.Ready;

    // L-shape tracking for menu visibility
    private bool shouldShowFrame = false;
    public bool ShouldShowFrame => shouldShowFrame;
    
    // =========================================================
    //  Initialization
    // =========================================================
    private void Awake()
    {
        // Disable by default - only enable when in appropriate panel or after user action
        // enabled = false;
        Debug.Log("[HandGestureDetector] Initialized - DISABLED by default");
    }

    private void Start()
    {
        leftHand = new HandData(leftSkeleton, "LEFT");
        rightHand = new HandData(rightSkeleton, "RIGHT");

        StartCoroutine(leftHand.Init());
        StartCoroutine(rightHand.Init());

        // Find passthrough layer if not assigned
        if (passthroughLayer == null)
        {
            passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();
            if (passthroughLayer != null)
            {
                Debug.Log("[HandGestureDetector] Found OVRPassthroughLayer");
            }
        }

        // Ensure frameQuad (SoloFoto passthrough frame) is registered with passthrough
        if (frameQuad != null && passthroughLayer != null)
        {
            try
            {
                passthroughLayer.AddSurfaceGeometry(frameQuad.gameObject, updateTransform: true);
                Debug.LogWarning("[HandGestureDetector] SoloFoto frameQuad registered with passthrough");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HandGestureDetector] Failed to register SoloFoto frame: {e.Message}");
            }
        }

        // Create dedicated QR frame quad for RemixFoto (NEVER registered with passthrough)
        CreateQRFrameQuad();

        Debug.Log("[HandGestureDetector] Hand tracking initialization started");
    }

    // =========================================================
    //  Update - Detect Gestures
    // =========================================================
    private void Update()
    {
        if (!Ready)
        {
            return;
        }

        // Update hand angles
        leftHand.UpdateAngles();
        rightHand.UpdateAngles();

        // Check if hands are actively tracked (using OVRHand)
        bool leftHandTracked = leftOVRHand != null && leftOVRHand.IsTracked && leftHand.Ready;
        bool rightHandTracked = rightOVRHand != null && rightOVRHand.IsTracked && rightHand.Ready;

        // Declare finger folded variables once at the beginning
        bool leftFingersFolded = false;
        bool rightFingersFolded = false;

        // CRITICAL: Frame requires BOTH hands to be tracked
        // If only one hand is tracked, frame should be inactive
        if (!leftHandTracked || !rightHandTracked)
        {
            // At least one hand is missing - deactivate frame immediately
            if (isFrameActive)
            {
                isFrameActive = false;
                OnFrameInactive?.Invoke();

                // Hide visual frame when deactivated
                if (frameQuad != null)
                {
                    frameQuad.gameObject.SetActive(false);
                }

                Debug.Log("[HandGestureDetector] Frame deactivated - ONE OR BOTH hands not tracked");
            }

            // Not both hands tracked = cannot show frame
            shouldShowFrame = false;

            return; // Skip rest of Update
        }

        // Both hands are tracked - now check finger positions
        // Check if middle, ring, and pinky are FOLDED (angle > 45°)
        leftFingersFolded = leftHand.middleA > 45f && leftHand.ringA > 45f && leftHand.pinkyA > 45f;
        rightFingersFolded = rightHand.middleA > 45f && rightHand.ringA > 45f && rightHand.pinkyA > 45f;

        // Only show frame when BOTH hands have fingers folded and index proximals are available
        shouldShowFrame = leftFingersFolded && rightFingersFolded &&
                              leftHand.IndexProximal && rightHand.IndexProximal &&
                              leftHand.WristRoot && rightHand.WristRoot;

        Debug.Log($"[HandGestureDetector] BOTH hands tracked - LeftFolded:{leftFingersFolded} RightFolded:{rightFingersFolded} ShouldShowFrame:{shouldShowFrame}");

        if (shouldShowFrame)
        {
            // Check which panel we're in
            bool inSoloFoto = InstructionManager.Instance != null &&
                             InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.SoloFoto);

            bool inRemixFoto = InstructionManager.Instance != null &&
                              InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.RemixFoto);

            bool inSaveFoto = InstructionManager.Instance != null &&
                             InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.SaveFoto);

            // Log current panel state occasionally
            if (Time.frameCount % 60 == 0) // Every 60 frames
            {
                Debug.LogWarning($"[HandGestureDetector] Panel check: SoloFoto={inSoloFoto}, RemixFoto={inRemixFoto}, SaveFoto={inSaveFoto}");
            }

            // Show/hide appropriate frame based on current panel
            if (inSoloFoto)
            {
                // SoloFoto: Show passthrough frame, hide QR frame
                if (frameQuad != null && !frameQuad.gameObject.activeSelf)
                {
                    frameQuad.gameObject.SetActive(true);
                    Debug.Log("[HandGestureDetector] Activated SoloFoto passthrough frame");
                }
                if (qrFrameQuad != null && qrFrameQuad.activeSelf)
                {
                    qrFrameQuad.SetActive(false);
                    Debug.Log("[HandGestureDetector] Deactivated QR frame (in SoloFoto)");
                }

                // Update passthrough frame position
                UpdateFrameFromFingers();
            }
            else if (inRemixFoto || inSaveFoto)
            {
                // RemixFoto/SaveFoto: Show QR frame, hide passthrough frame
                if (frameQuad != null && frameQuad.gameObject.activeSelf)
                {
                    frameQuad.gameObject.SetActive(false);
                    Debug.Log("[HandGestureDetector] Deactivated SoloFoto passthrough frame");
                }
                if (qrFrameQuad != null && !qrFrameQuad.activeSelf)
                {
                    qrFrameQuad.SetActive(true);
                    Debug.Log("[HandGestureDetector] Activated QR frame");
                }

                // Update QR frame position
                UpdateQRFrameFromFingers();
            }

            // Fire appropriate events based on mode
            if (inSoloFoto)
            {
                // SoloFoto mode - fire OnFrameActive for passthrough capture
                if (!isFrameActive)
                {
                    isFrameActive = true;
                    Debug.Log("[HandGestureDetector] Frame activated - SoloFoto mode");
                }
                OnFrameActive?.Invoke(currentFramePosition, currentFrameRotation, currentFrameScale);
            }
            else if (inRemixFoto || inSaveFoto)
            {
                // RemixFoto/SaveFoto mode - fire OnQRFrameActive for QR texture preview
                if (!isFrameActive)
                {
                    isFrameActive = true;
                    Debug.Log("[HandGestureDetector] Frame activated - RemixFoto/SaveFoto mode");
                }
                OnQRFrameActive?.Invoke(currentFramePosition, currentFrameRotation, currentFrameScale);
            }
        }
        else
        {
            if (isFrameActive)
            {
                isFrameActive = false;
                OnFrameInactive?.Invoke();

                // Hide visual frame when deactivated
                if (frameQuad != null)
                {
                    frameQuad.gameObject.SetActive(false);
                }

                Debug.Log("[HandGestureDetector] Frame deactivated");
            }
        }

        // Detect index finger flicker (capture gesture)
        DetectFlicker();
    }

    // =========================================================
    //  Frame Positioning from Thumb Tips
    // =========================================================
    private void UpdateFrameFromFingers()
    {
        if (!leftHand.ThumbTip || !rightHand.ThumbTip)
            return;

        Vector3 leftPos = leftHand.ThumbTip.position;
        Vector3 rightPos = rightHand.ThumbTip.position;

        // Get camera reference
        Camera cam = Camera.main;
        if (cam == null)
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
                cam = rig.centerEyeAnchor.GetComponent<Camera>();
        }

        if (cam != null)
        {
            // Determine which hand is lower (bottom corner)
            bool leftIsLower = leftPos.y < rightPos.y;

            float verticalDist = Mathf.Abs(leftPos.y - rightPos.y);
            float horizontalDist = Mathf.Abs(leftPos.x - rightPos.x);

            Vector3 bottomLeft, bottomRight, topRight, topLeft;
            if (leftIsLower)
            {
                // Left hand is bottom-left, right hand is top-right
                bottomLeft = leftPos;
                topRight = rightPos;

                
                topLeft = new Vector3(leftPos.x, leftPos.y + verticalDist, leftPos.z);
                bottomRight = new Vector3(rightPos.x, rightPos.y - verticalDist, rightPos.z);
            }
            else
            {
                // Right hand is bottom-right, left hand is top-left
                bottomRight = rightPos;
                topLeft = leftPos;

                bottomLeft = new Vector3(leftPos.x, leftPos.y - verticalDist, leftPos.z);
                topRight = new Vector3(rightPos.x, rightPos.y + verticalDist, rightPos.z);
            }

            // Apply corner inset to avoid capturing hands
            // Move corners inward by cornerInset amount
            Vector3 centerBeforeInset = (bottomLeft + topRight) / 2f;

            // Calculate direction from center to each corner
            Vector3 toBL = (bottomLeft - centerBeforeInset).normalized;
            Vector3 toBR = (bottomRight - centerBeforeInset).normalized;
            Vector3 toTL = (topLeft - centerBeforeInset).normalized;
            Vector3 toTR = (topRight - centerBeforeInset).normalized;
 
            // Inset corners by moving them toward center
            bottomLeft = bottomLeft - toBL * cornerInset;
            bottomRight = bottomRight - toBR * cornerInset;
            topLeft = topLeft - toTL * cornerInset;
            topRight = topRight - toTR * cornerInset;

            // Calculate center
            Vector3 center = (bottomLeft + topRight) / 2f;

            // Calculate width and height from corners
            float width = Vector3.Distance(bottomLeft, bottomRight);
            float height = Vector3.Distance(bottomLeft, topLeft);

            // Calculate the right vector (left to right)
            Vector3 right = (bottomRight - bottomLeft).normalized;

            // Calculate the up vector (bottom to top)
            Vector3 up = (topLeft - bottomLeft).normalized;

            // Calculate forward vector (perpendicular to the plane)
            Vector3 forward = Vector3.Cross(right, up).normalized;

            // Build rotation from these axes
            currentFrameRotation = Quaternion.LookRotation(forward, up);

            // Position slightly offset toward camera
            Vector3 toCamera = (cam.transform.position - center).normalized;
            currentFramePosition = center + toCamera * 0.02f;
            currentFrameScale = new Vector3(width, height, 1f);

            // Update visual frame if assigned (position/rotation/scale only - don't force active)
            // Use SetVisualFrameEnabled() to control visibility externally
            if (frameQuad != null)
            {
                frameQuad.position = currentFramePosition;
                frameQuad.rotation = currentFrameRotation;
                frameQuad.localScale = currentFrameScale;
                // Don't force SetActive(true) here - let external scripts control visibility
                // frameQuad.gameObject.SetActive(true); // REMOVED - controlled by SetVisualFrameEnabled()

                // DEBUG: Log frame state every 60 frames
                if (Time.frameCount % 60 == 0)
                {
                    MeshRenderer renderer = frameQuad.GetComponent<MeshRenderer>();
                    Debug.LogWarning($"[HandGestureDetector] SOLO FOTO FRAME STATE:");
                    Debug.LogWarning($"  GameObject.active: {frameQuad.gameObject.activeSelf}");
                    Debug.LogWarning($"  GameObject.activeInHierarchy: {frameQuad.gameObject.activeInHierarchy}");
                    Debug.LogWarning($"  Renderer.enabled: {(renderer != null ? renderer.enabled.ToString() : "NULL")}");
                    Debug.LogWarning($"  Material.shader: {(renderer != null && renderer.material != null ? renderer.material.shader.name : "NULL")}");
                    Debug.LogWarning($"  Position: {currentFramePosition}");
                    Debug.LogWarning($"  Scale: {currentFrameScale}");
                }
            }

            Debug.Log($"[HandGestureDetector] Frame - LeftLower:{leftIsLower} Width:{width:F3} Height:{height:F3} Inset:{cornerInset:F3}m");
        }
    }

    // =========================================================
    //  QR Frame Positioning (RemixFoto/SaveFoto)
    // =========================================================
    private void UpdateQRFrameFromFingers()
    {
        if (!leftHand.ThumbTip || !rightHand.ThumbTip)
            return;

        if (qrFrameQuad == null)
            return;

        Vector3 leftPos = leftHand.ThumbTip.position;
        Vector3 rightPos = rightHand.ThumbTip.position;

        // Same calculation as UpdateFrameFromFingers
        bool leftIsLower = leftPos.y < rightPos.y;
        float verticalDist = Mathf.Abs(leftPos.y - rightPos.y);

        Vector3 bottomLeft, bottomRight, topRight, topLeft;
        if (leftIsLower)
        {
            bottomLeft = leftPos;
            topRight = rightPos;
            topLeft = new Vector3(leftPos.x, leftPos.y + verticalDist, leftPos.z);
            bottomRight = new Vector3(rightPos.x, rightPos.y - verticalDist, rightPos.z);
        }
        else
        {
            bottomRight = rightPos;
            topLeft = leftPos;
            bottomLeft = new Vector3(leftPos.x, leftPos.y - verticalDist, leftPos.z);
            topRight = new Vector3(rightPos.x, rightPos.y + verticalDist, rightPos.z);
        }

        // Apply corner inset
        Vector3 centerBeforeInset = (bottomLeft + topRight) / 2f;
        Vector3 toBL = (bottomLeft - centerBeforeInset).normalized;
        Vector3 toBR = (bottomRight - centerBeforeInset).normalized;
        Vector3 toTL = (topLeft - centerBeforeInset).normalized;
        Vector3 toTR = (topRight - centerBeforeInset).normalized;

        bottomLeft = bottomLeft - toBL * cornerInset;
        bottomRight = bottomRight - toBR * cornerInset;
        topLeft = topLeft - toTL * cornerInset;
        topRight = topRight - toTR * cornerInset;

        // Calculate center, size, rotation
        Vector3 center = (bottomLeft + topRight) / 2f;
        float width = Vector3.Distance(bottomLeft, bottomRight);
        float height = Vector3.Distance(bottomLeft, topLeft);

        Vector3 right = (bottomRight - bottomLeft).normalized;
        Vector3 up = (topLeft - bottomLeft).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;

        Quaternion rotation = Quaternion.LookRotation(forward, up);

        // NO camera offset for QR frame (unlike SoloFoto)
        Vector3 position = center;
        Vector3 scale = new Vector3(width, height, 1f);

        // Store in current* variables for events
        currentFramePosition = position;
        currentFrameRotation = rotation;
        currentFrameScale = scale;

        // Update QR frame quad transform
        qrFrameQuad.transform.position = position;
        qrFrameQuad.transform.rotation = rotation;
        qrFrameQuad.transform.localScale = scale;

        // CRITICAL: Compute UV offset/scale using spherical projection
        // This ensures the texture preview matches what will be captured
        ComputeAndApplyQRFrameUV(position, rotation, scale);

        // DEBUG: Log detailed QR frame state (reduced frequency)
        if (Time.frameCount % 30 == 0)
        {
            Debug.LogWarning($"[HandGestureDetector] ★★★ QR FRAME STATE ★★★");
            Debug.LogWarning($"  Position: {position}");
            Debug.LogWarning($"  Rotation: {rotation.eulerAngles}");
            Debug.LogWarning($"  Scale: {scale} (Width:{width:F3} Height:{height:F3})");
            Debug.LogWarning($"  GameObject.active: {qrFrameQuad.activeSelf}");
            if (qrFrameRenderer != null && qrFrameRenderer.material != null)
            {
                Debug.LogWarning($"  Material.mainTextureOffset: {qrFrameRenderer.material.mainTextureOffset}");
                Debug.LogWarning($"  Material.mainTextureScale: {qrFrameRenderer.material.mainTextureScale}");
            }
        }
    }

    /// <summary>
    /// Compute UV offset/scale for QR frame using spherical projection (same as capture)
    /// Maps the frame's spatial position/size to the equirectangular QR image UVs
    /// WORKAROUND: Modifies mesh UVs directly since material UV offset/scale don't work with available shaders
    /// </summary>
    private void ComputeAndApplyQRFrameUV(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (qrFrameRenderer == null || qrFrameRenderer.material == null)
            return;

        // Get camera position (center of the imaginary sphere)
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 sphereCenter = cam.transform.position;

        // Get the four corners of the quad in world space
        Vector3[] corners = new Vector3[4];
        corners[0] = position + rotation * new Vector3(-scale.x/2, -scale.y/2, 0); // Bottom-left
        corners[1] = position + rotation * new Vector3( scale.x/2, -scale.y/2, 0); // Bottom-right
        corners[2] = position + rotation * new Vector3(-scale.x/2,  scale.y/2, 0); // Top-left
        corners[3] = position + rotation * new Vector3( scale.x/2,  scale.y/2, 0); // Top-right

        // Calculate UV coordinates for each corner using spherical projection
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
        Vector2 uvOffset = new Vector2(minU, minV);
        Vector2 uvScale = new Vector2(maxU - minU, maxV - minV);

        // WORKAROUND: Modify mesh UVs directly instead of using material properties
        // This works with ANY shader including Unlit/Color
        MeshFilter meshFilter = qrFrameQuad.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.mesh != null)
        {
            Mesh mesh = meshFilter.mesh;

            // Get current vertices to understand mesh structure
            Vector3[] vertices = mesh.vertices;

            // Unity Quad vertices are ordered: BL(0), BR(1), TL(2), TR(3)
            // But we need to verify the actual order by checking Y coordinates
            Vector2[] uvs = new Vector2[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                // Determine which corner this vertex is based on its local position
                Vector3 v = vertices[i];
                bool isLeft = v.x < 0;
                bool isBottom = v.y < 0;

                float u, uv;

                if (isLeft && isBottom)
                {
                    // Bottom-left corner
                    u = uvOffset.x;
                    uv = uvOffset.y;
                }
                else if (!isLeft && isBottom)
                {
                    // Bottom-right corner
                    u = uvOffset.x + uvScale.x;
                    uv = uvOffset.y;
                }
                else if (isLeft && !isBottom)
                {
                    // Top-left corner
                    u = uvOffset.x;
                    uv = uvOffset.y + uvScale.y;
                }
                else
                {
                    // Top-right corner
                    u = uvOffset.x + uvScale.x;
                    uv = uvOffset.y + uvScale.y;
                }

                uvs[i] = new Vector2(u, uv);
            }

            mesh.uv = uvs;
        }

        // DEBUG: Verify UV was applied
        if (Time.frameCount % 30 == 0)
        {
            Debug.LogWarning($"[HandGestureDetector] Mesh UV Modified: Offset={uvOffset}, Scale={uvScale}");
            Debug.LogWarning($"[HandGestureDetector] Material texture: {(qrFrameRenderer.material.mainTexture != null ? $"{qrFrameRenderer.material.mainTexture.width}x{qrFrameRenderer.material.mainTexture.height}" : "NULL")}");
            Debug.LogWarning($"[HandGestureDetector] Material shader: {qrFrameRenderer.material.shader.name}");
        }
    }

    // =========================================================
    //  Index Flicker Detection (Capture Trigger)
    // =========================================================
    private float lastCaptureTime = -999f;
    private const float CAPTURE_COOLDOWN = 2f; // 2 second cooldown between captures

    private void DetectFlicker()
    {
        if (!isFrameActive)
            return;

        // Check cooldown
        if (Time.time - lastCaptureTime < CAPTURE_COOLDOWN)
        {
            return; // Still in cooldown
        }

        bool leftFlicker = leftHand.DetectIndexFlicker();
        bool rightFlicker = rightHand.DetectIndexFlicker();

        if (leftFlicker || rightFlicker)
        {
            lastCaptureTime = Time.time;
            Debug.Log($"[HandGestureDetector] ★★★ Index flicker detected! Next capture available in {CAPTURE_COOLDOWN}s");
            OnFlickerGesture?.Invoke(currentFramePosition, currentFrameRotation, currentFrameScale);
        }
    }

    // =========================================================
    //  Public Methods
    // =========================================================
    /// <summary>
    /// Get current frame state
    /// </summary>
    public bool IsFrameActive => isFrameActive;

    /// <summary>
    /// Get current frame transform data
    /// </summary>
    public void GetFrameTransform(out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = currentFramePosition;
        rotation = currentFrameRotation;
        scale = currentFrameScale;
    }

    /// <summary>
    /// DEPRECATED: No longer needed - frames are managed automatically per panel
    /// Kept for backwards compatibility
    /// </summary>
    public void SetVisualFrameEnabled(bool enabled)
    {
        Debug.LogWarning("[HandGestureDetector] SetVisualFrameEnabled() is deprecated - frames are managed automatically");
    }

    /// <summary>
    /// DEPRECATED: Use SetQRFrameMaterial() instead for RemixFoto frame
    /// Kept for backwards compatibility
    /// </summary>
    public void SetFrameMaterial(Material customMaterial)
    {
        Debug.LogWarning("[HandGestureDetector] SetFrameMaterial() is deprecated - use SetQRFrameMaterial() instead");
    }

    /// <summary>
    /// Create the dedicated QR frame quad for RemixFoto
    /// This quad is NEVER registered with passthrough, so it can show custom materials
    /// </summary>
    private void CreateQRFrameQuad()
    {
        qrFrameQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        qrFrameQuad.name = "HandGesture_QRFrameQuad";
        qrFrameRenderer = qrFrameQuad.GetComponent<MeshRenderer>();

        // Remove collider (don't need it)
        Collider collider = qrFrameQuad.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        // Create BRIGHT MAGENTA material - use Unlit/Color for VR compatibility
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            // Fallback to Sprites/Default if Unlit/Color not found
            shader = Shader.Find("Sprites/Default");
        }

        if (shader != null)
        {
            qrFrameMaterial = new Material(shader);
            qrFrameMaterial.color = Color.magenta;
            qrFrameMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            qrFrameMaterial.renderQueue = 3000; // Render on top

            qrFrameRenderer.material = qrFrameMaterial;
            qrFrameRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            qrFrameRenderer.receiveShadows = false;

            Debug.LogWarning($"[HandGestureDetector] QR Frame created with shader: {shader.name}");
        }
        else
        {
            Debug.LogError("[HandGestureDetector] Could not find Unlit/Color or Sprites/Default shader for QR frame!");
        }

        // Start disabled (will be enabled when in RemixFoto)
        qrFrameQuad.SetActive(false);

        Debug.LogWarning("[HandGestureDetector] ★★★ Created dedicated QR frame quad with MAGENTA material ★★★");
        Debug.LogWarning("[HandGestureDetector] This quad is NEVER registered with passthrough!");
    }

    /// <summary>
    /// Set the QR frame material (for showing QR texture or other custom textures)
    /// </summary>
    public void SetQRFrameMaterial(Material customMaterial)
    {
        if (qrFrameRenderer == null)
        {
            Debug.LogWarning("[HandGestureDetector] Cannot set QR frame material - qrFrameRenderer is null");
            return;
        }

        if (customMaterial != null)
        {
            qrFrameMaterial = customMaterial;
            qrFrameRenderer.material = customMaterial;
            Debug.LogWarning($"[HandGestureDetector] QR frame material set to: {customMaterial.shader.name}");
        }
        else
        {
            Debug.LogWarning("[HandGestureDetector] Cannot set null material on QR frame");
        }
    }

    /// <summary>
    /// Update the QR frame's UV offset and scale to show a specific region of the texture
    /// This is VERY cheap - just setting material properties, no texture operations
    /// </summary>
    public void SetQRFrameUV(Vector2 uvOffset, Vector2 uvScale)
    {
        if (qrFrameRenderer == null || qrFrameRenderer.material == null)
        {
            return;
        }

        qrFrameRenderer.material.mainTextureOffset = uvOffset;
        qrFrameRenderer.material.mainTextureScale = uvScale;
    }

    /// <summary>
    /// Quick helper - no longer needed since QR frame is always magenta by default
    /// Keeping for backwards compatibility
    /// </summary>
    public void SetFrameMagenta()
    {
        Debug.LogWarning("[HandGestureDetector] SetFrameMagenta() called - QR frame is already magenta by default!");
    }

    /// <summary>
    /// Get debug status string
    /// </summary>
    public string GetDebugStatus()
    {
        if (!Ready)
            return "Waiting for hand tracking...";

        return $"Left: {leftHand.ColoredState}\n" +
               $"Right: {rightHand.ColoredState}\n" +
               $"Frame: {(isFrameActive ? "ACTIVE" : "inactive")}";
    }

    // =========================================================
    //  HandData Class - Internal hand tracking logic
    // =========================================================
    private class HandData
    {
        public OVRSkeleton skel;
        public Transform ThumbMetacarpal; // Thumb metacarpal for frame corner
        public Transform IndexProximal; // Index proximal (first knuckle) for stable corner position
        public Transform ThumbTip;
        public Transform IndexTip;
        public Transform WristRoot; // Wrist/palm for stable rotation

        public bool Ready = false;
        public string handName;

        public float indexA, middleA, ringA, pinkyA;
        private bool wasIndexOut = false;

        public string ColoredState
        {
            get
            {
                bool isIndexOut = indexA < 20f;
                return isIndexOut
                    ? $"<color=green>INDEX OUT ({indexA:F1}°)</color>"
                    : $"<color=white>I:{indexA:F1} M:{middleA:F1} R:{ringA:F1} P:{pinkyA:F1}</color>";
            }
        }

        public HandData(OVRSkeleton s, string name)
        {
            skel = s;
            handName = name;
        }

        public IEnumerator Init()
        {
            while (skel.Bones == null || skel.Bones.Count == 0)
                yield return null;

            // Debug: Log all bone names
            Debug.Log($"[HandGestureDetector] {handName} - Listing all bones:");
            foreach (var b in skel.Bones)
            {
                Debug.Log($"  - {b.Transform.name}");
            }

            foreach (var b in skel.Bones)
            {
                string name = b.Transform.name.ToLower();

                // Get wrist/root for stable rotation
                if (name.Contains("hand") && (name.Contains("wrist") || name.Contains("start")))
                {
                    WristRoot = b.Transform;
                }

                // Get thumb metacarpal for frame positioning
                if (name.Contains("thumb") && name.Contains("metacarpal"))
                {
                    ThumbMetacarpal = b.Transform;
                }

                // Get index proximal (first knuckle) for stable corner position
                if (name.Contains("index") && name.Contains("proximal"))
                {
                    IndexProximal = b.Transform;
                }

                // Get thumb and index tips for pinch detection
                if (name.Contains("thumb") && name.Contains("tip"))
                {
                    ThumbTip = b.Transform;
                }
                if (name.Contains("index") && name.Contains("tip"))
                {
                    IndexTip = b.Transform;
                }
            }

            Ready = true;
            Debug.Log($"[HandGestureDetector] {handName} hand ready - Wrist: {(WristRoot != null ? "Found" : "NULL")}, Index Proximal: {(IndexProximal != null ? "Found" : "NULL")}, Thumb Tip: {(ThumbTip != null ? "Found" : "NULL")}, Index Tip: {(IndexTip != null ? "Found" : "NULL")}");
        }

        public bool DetectIndexFlicker()
        {
            bool isIndexOut = (indexA < 20f);
            bool flickered = wasIndexOut && !isIndexOut;
            wasIndexOut = isIndexOut;
            return flickered;
        }

        public void UpdateAngles()
        {
            indexA = GetFingerAngle("index");
            middleA = GetFingerAngle("middle");
            ringA = GetFingerAngle("ring");
            pinkyA = GetFingerAngle("pinky", "little"); // Try both "pinky" and "little"

            // Debug log all finger angles
            Debug.Log($"[HandGestureDetector] {handName} - Index:{indexA:F1}° Middle:{middleA:F1}° Ring:{ringA:F1}° Pinky:{pinkyA:F1}°");
        }

        private float GetFingerAngle(string fingerName, string alternateName = null)
        {
            Transform baseJoint = null, mid = null, tip = null;

            foreach (var b in skel.Bones)
            {
                string n = b.Transform.name.ToLower();
                bool isMatch = n.Contains(fingerName);

                // Check alternate name if provided
                if (alternateName != null && !isMatch)
                {
                    isMatch = n.Contains(alternateName);
                }

                if (isMatch && n.Contains("metacarpal")) baseJoint = b.Transform;
                if (isMatch && n.Contains("intermediate")) mid = b.Transform;
                if (isMatch && n.Contains("tip")) tip = b.Transform;
            }

            if (!baseJoint || !mid || !tip)
            {
                // Debug missing bones
                if (baseJoint == null || mid == null || tip == null)
                {
                    Debug.LogWarning($"[HandGestureDetector] {handName} missing bones for {fingerName} - Base:{(baseJoint != null)} Mid:{(mid != null)} Tip:{(tip != null)}");
                }
                return 999f;
            }

            Vector3 v1 = mid.position - baseJoint.position;
            Vector3 v2 = tip.position - mid.position;

            float dot = Vector3.Dot(v1.normalized, v2.normalized);
            return Mathf.Acos(Mathf.Clamp(dot, -1, 1)) * Mathf.Rad2Deg;
        }
    }
}
