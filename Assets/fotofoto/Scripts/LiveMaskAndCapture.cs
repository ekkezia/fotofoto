// for solo foto
using UnityEngine;
using TMPro;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using PassthroughCameraSamples;

public class LiveMaskAndCapture : MonoBehaviour
{
    // ---------------------------------------------------------
    //  Inspector References
    // ---------------------------------------------------------
    [Header("References")]
    public HandGestureDetector gestureDetector; // NEW - centralized gesture detection
    public Transform cam;
    public OVRPassthroughLayer passthroughLayer;

    [Header("Capture Settings")]
    public Camera captureCamera;
    public int captureWidth = 1024;
    public int captureHeight = 768;
    public AudioClip captureSound;
    public ComputeShader downsampleShader;
    [Range(1f, 10f)]
    public float zoomFactor = 1f; // ✅ Adjustable zoom for cropping

    // ---------------------------------------------------------
    //  Internals
    // ---------------------------------------------------------
    private AudioSource audioSource;
    private TextMeshProUGUI gui;

    private List<GameObject> capturedPlanes = new List<GameObject>();
    private bool isCapturing = false;

    // WebCam stuff
    private WebCamTexture _cachedWebCamTexture;
    private WebCamTextureManager _webCamTextureManager;
    private PassthroughCameraEye _passthroughCameraEye;
    private Camera _passthroughCamera;
    // Debounce
    private float lastCaptureTime = 0f;
    public float captureCooldown = 0.5f;   // 1 second debounce

    // Printer
    public Printer printer;

    private void Awake()
    {
        _webCamTextureManager = FindAnyObjectByType<WebCamTextureManager>();
        if (_webCamTextureManager == null)
        {
            Debug.LogWarning("[LIVE] No WebCamTextureManager found. Some capture features will be disabled.");
        }
        else
        {
            _passthroughCameraEye = _webCamTextureManager.Eye;
            Debug.Log("[LIVE] WebCamTextureManager found. Capture features enabled.");
        }

        // Get the passthrough camera
        var cameraRig = FindAnyObjectByType<OVRCameraRig>();
        if (cameraRig != null)
        {
            _passthroughCamera = cameraRig.centerEyeAnchor.GetComponent<Camera>();
        }
        else
        {
            _passthroughCamera = Camera.main;
        }

        Debug.Log($"[LIVE] Passthrough camera: {(_passthroughCamera != null ? "Found" : "NULL")}");
    }

    // =========================================================
    //  Start
    // =========================================================
    void Start()
    {
            Debug.LogError($"[LiveMaskAndCapture] ★★★ THIS INSTANCE: {gameObject.name} (InstanceID: {GetInstanceID()}) ★★★");

        CreateGUI();

        // -----------------------------------------------------
        // 1. Create passthrough quad
        // -----------------------------------------------------
        // GameObject ptQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        // ptQuad.name = "UserPassthrough_Surface";
        // ptQuad.transform.position = cam.position + cam.forward * 1.5f;
        // ptQuad.transform.rotation = cam.rotation;
        // ptQuad.transform.localScale = new Vector3(2f, 1.2f, 1f);

        // // 2. Bind quad to passthrough
        // passthroughLayer.AddSurfaceGeometry(ptQuad);

        // // 3. Put in PTCapture layer
        // ptQuad.layer = LayerMask.NameToLayer("PTCapture");

        // 4. Ensure captureCamera sees passthrough + default
        if (captureCamera)
        {
            captureCamera.cullingMask =
                (1 << LayerMask.NameToLayer("PTCapture")) |
                (1 << LayerMask.NameToLayer("Default"));
        }

        if (!captureCamera) captureCamera = Camera.main;

        // -----------------------------------------------------
        // Audio source
        // -----------------------------------------------------
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.1f;

        // -----------------------------------------------------
        // Subscribe to HandGestureDetector events
        // -----------------------------------------------------
        if (gestureDetector != null)
        {
            gestureDetector.OnFlickerGesture += OnGestureCapture;
            Debug.Log("[LIVE] ✅ Subscribed to HandGestureDetector");
        }
        else
        {
            Debug.LogError("[LIVE] ❌ HandGestureDetector not assigned!");
        }

        Debug.Log("[LIVE] ✅ Capture system initialized");
    }

    // =========================================================
    //  Update Loop
    // =========================================================
    void Update()
    {
        // Enable/disable gesture detector based on panel
        // IMPORTANT: Both Solo Foto and Remix Foto need the gesture detector!
        bool inSoloFoto = InstructionManager.Instance != null &&
                         InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.SoloFoto);

        bool inRemixFoto = InstructionManager.Instance != null &&
                          InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.RemixFoto);

        bool needsGestureDetector = inSoloFoto || inRemixFoto;

        if (gestureDetector != null)
        {
            gestureDetector.enabled = needsGestureDetector;
        }

        // Show/hide captured planes based on panel
        foreach (var plane in capturedPlanes)
        {
            if (plane != null)
            {
                plane.SetActive(inSoloFoto);
            }
        }

        if (gestureDetector != null && gestureDetector.Ready)
        {
            gui.text = gestureDetector.GetDebugStatus() + $"\nCaptured: {capturedPlanes.Count}";
        }
        else
        {
            gui.text = inSoloFoto ? "Waiting for HandGestureDetector..." : $"Not in Solo Foto\nCaptured: {capturedPlanes.Count}";
        }

        // GUI follows camera
        gui.transform.parent.position = cam.position + cam.forward * 1.5f;
        gui.transform.parent.rotation = cam.rotation;
    }

    // =========================================================
    //  Gesture Callback - Called by HandGestureDetector
    // =========================================================
    private void OnGestureCapture(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        // Only capture if in SoloFoto panel
        if (InstructionManager.Instance == null ||
            !InstructionManager.Instance.IsInPanel(InstructionManager.InstructionPanel.SoloFoto))
        {
            Debug.Log("[LIVE] Not in SoloFoto panel, ignoring gesture");
            return;
        }

        // Check if already capturing
        if (isCapturing)
        {
            Debug.Log("[LIVE] Already capturing, ignoring gesture");
            return;
        }

        // Check cooldown
        if (Time.time - lastCaptureTime < captureCooldown)
        {
            Debug.Log("[LIVE] ⏱️ Capture on cooldown, ignoring gesture");
            return;
        }

        // SoloFoto only allows 1 capture
        // if (capturedPlanes.Count > 0)
        // {
        //     Debug.Log("[LIVE] SoloFoto mode - already captured once, ignoring further captures");
        //     return;
        // }

        lastCaptureTime = Time.time;
        Debug.Log($"[LIVE] ★★★ Gesture capture triggered! Pos:{position} Scale:{scale}");

        StartCoroutine(CapturePassthroughPlane(position, rotation, scale));
    }

    // =========================================================
    //  Cleanup
    // =========================================================
    private void OnDestroy()
    {
        if (gestureDetector != null)
        {
            gestureDetector.OnFlickerGesture -= OnGestureCapture;
        }
    }

    // =========================================================
    //  Public Methods
    // =========================================================
    /// <summary>
    /// Reset all captured planes - destroys them and clears the list
    /// Called by PanelNavigationManager.OnSelectReset()
    /// </summary>
    public void ResetCapturedPlanes()
    {
        Debug.Log($"[LIVE] Resetting {capturedPlanes.Count} captured planes");

        foreach (var plane in capturedPlanes)
        {
            if (plane != null)
            {
                Destroy(plane);
            }
        }

        capturedPlanes.Clear();
        Debug.Log("[LIVE] ✅ All captured planes cleared");
    }

    /// <summary>
    /// Get count of captured planes
    /// </summary>
    /// <summary>
    public int GetCapturedPlaneCount()
    {
        // Clean up any null references first
        capturedPlanes.RemoveAll(plane => plane == null);
        
        int count = capturedPlanes.Count;
        Debug.LogError($"[LiveMaskAndCapture] ★★★ GetCapturedPlaneCount() called - returning {count} ★★★");
        
        // Log each plane
        for (int i = 0; i < capturedPlanes.Count; i++)
        {
            Debug.Log($"[LiveMaskAndCapture] Plane {i}: {(capturedPlanes[i] != null ? capturedPlanes[i].name : "NULL")}");
        }
        
        return count;
    }

    /// <summary>
    /// Data class to store image metadata (position, rotation, scale)
    /// </summary>
    [System.Serializable]
    public class ImageMetadata
    {
        public string imagePath;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    /// <summary>
    /// Data class to store the entire scene (all images + metadata)
    /// </summary>
    [System.Serializable]
    public class SceneData
    {
        public List<ImageMetadata> images = new List<ImageMetadata>();
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

            // Generate file name
            string fileName = $"{baseName}_{i + 1}.png";
            string filePath = System.IO.Path.Combine(Application.persistentDataPath, fileName);

            try
            {
                // Encode to PNG
                byte[] bytes = texture.EncodeToPNG();

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

    /// <summary>
    /// Convert RenderTexture to Texture2D
    /// </summary>
    private Texture2D RenderTextureToTexture2D(RenderTexture renderTexture)
    {
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        return texture;
    }

    /// <summary>
    /// Create a readable copy of a texture
    /// </summary>
    private Texture2D CreateReadableTexture(Texture2D source)
    {
        RenderTexture tmp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, tmp);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmp;

        Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        readable.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmp);

        return readable;
    }


    // =========================================================
    //  Capture Steps
    // =========================================================
    IEnumerator CapturePassthroughPlane(Vector3 capturePos, Quaternion captureRot, Vector3 captureScale)
    {
        isCapturing = true;

        // ✅ Wait one frame to ensure webcam texture is updated
        yield return new WaitForEndOfFrame();

        Debug.Log($"[LIVE] 📸 Starting capture at pos:{capturePos} scale:{captureScale}");

        yield return StartCoroutine(CaptureFromWebCam(capturePos, captureRot, captureScale));

        isCapturing = false;
    }

    IEnumerator CaptureFromWebCam(Vector3 capturePos, Quaternion captureRot, Vector3 captureScale)
    {
        Debug.Log("[LIVE] ========== CAPTURE FROM WEBCAM START ==========");
        Debug.Log($"[LIVE] Params - Pos:{capturePos} Rot:{captureRot.eulerAngles} Scale:{captureScale}");

        // Try to get WebCamTexture
        WebCamTexture webCamTexture = GetWebCamTexture();

        if (webCamTexture == null || !webCamTexture.isPlaying)
        {
            Debug.LogWarning("[LIVE] WebCamTexture not available, trying RenderTexture fallback");
            yield return StartCoroutine(CaptureWithRenderTexture(capturePos, captureRot, captureScale));
            yield break;
        }

        Debug.Log($"[LIVE] ✓ WebCam texture active: {webCamTexture.width}x{webCamTexture.height}");
        Debug.Log($"[LIVE] WebCam isPlaying: {webCamTexture.isPlaying}");
        Debug.Log($"[LIVE] WebCam didUpdateThisFrame: {webCamTexture.didUpdateThisFrame}");

        try
        {
            Debug.Log("[LIVE] Step 1: Getting pixels from webcam...");
            Color[] pixels = webCamTexture.GetPixels();
            Debug.Log($"[LIVE] Got {pixels.Length} pixels from webcam");

            Debug.Log("[LIVE] Step 2: Creating Texture2D...");
            Texture2D fullTexture = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGB24, false);

            Debug.Log("[LIVE] Step 3: Setting pixels...");
            fullTexture.SetPixels(pixels);
            fullTexture.Apply();

            Debug.Log($"[LIVE] ✓ Full texture captured: {fullTexture.width}x{fullTexture.height}");

            // Test multiple pixel colors to verify we have real image data
            Color centerPixel = fullTexture.GetPixel(fullTexture.width / 2, fullTexture.height / 2);
            Color topLeftPixel = fullTexture.GetPixel(10, 10);
            Color bottomRightPixel = fullTexture.GetPixel(fullTexture.width - 10, fullTexture.height - 10);
            Debug.Log($"[LIVE] Center pixel: {centerPixel}");
            Debug.Log($"[LIVE] TopLeft pixel: {topLeftPixel}");
            Debug.Log($"[LIVE] BottomRight pixel: {bottomRightPixel}");

            // Calculate average brightness to see if image is too dark
            float avgBrightness = (centerPixel.r + centerPixel.g + centerPixel.b) / 3f;
            Debug.Log($"[LIVE] Average pixel brightness: {avgBrightness} (0=black, 1=white)");

            // Calculate the frame's 4 corners in 3D space
            Vector3[] frameCorners = CalculateFrameCorners(capturePos, captureRot, captureScale);

            // Apply smart crop based on frame position in 3D space
            Texture2D croppedTexture = CropToFramePosition(fullTexture, frameCorners, captureScale, webCamTexture.width, webCamTexture.height);
            Debug.Log($"[LIVE] ✓ Cropped to: {croppedTexture.width}x{croppedTexture.height} based on frame position");

            // Clean up the full texture since we now have a cropped version
            if (croppedTexture != fullTexture)
            {
                Destroy(fullTexture);
            }

            Debug.Log("[LIVE] Step 4: Calling CreateCapturedPiece...");
            GameObject piece = CreateCapturedPiece(croppedTexture, capturePos, captureRot, captureScale);

            if (piece == null)
            {
                Debug.LogError("[LIVE] CreateCapturedPiece returned NULL!");
                yield break;
            }

            Debug.Log("[LIVE] Step 5: Adding piece to list...");
            capturedPlanes.Add(piece);

            Debug.Log("[LIVE] Step 6: Playing sound and printing...");
            if (captureSound && audioSource)
            {
                audioSource.PlayOneShot(captureSound);
            }

            // ✅ Safely trigger printer
            // if (printer != null)
            // {
            //     try
            //     {
            //         string message = $"Captured photo #{capturedPlanes.Count}";
            //         Debug.Log($"[LIVE] Sending to printer: {message}");
            //         printer.PrintText(message);
            //     }
            //     catch (System.Exception e)
            //     {
            //         Debug.LogError($"[LIVE] Printer error: {e.Message}");
            //     }
            // }
            // else
            // {
            //     Debug.LogWarning("[LIVE] No printer assigned to LiveMaskAndCapture.");
            // }

            Debug.Log($"[LIVE] ✓✓✓ Created Captured Piece #{capturedPlanes.Count}!");
            Debug.Log($"[LIVE] ========== CAPTURE FROM WEBCAM END ==========");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LIVE] Exception during capture: {e.Message}");
            Debug.LogError($"[LIVE] Stack trace: {e.StackTrace}");
        }
    }

    IEnumerator CaptureWithRenderTexture(Vector3 capturePos, Quaternion captureRot, Vector3 captureScale)
    {
        if (_passthroughCamera == null)
        {
            Debug.LogError("[LIVE] No camera available for capture!");
            yield break;
        }

        Debug.Log("[LIVE] Using RenderTexture capture method");

        yield return new WaitForEndOfFrame();

        RenderTexture rt = new RenderTexture(captureWidth, captureHeight, 24);
        RenderTexture previousRT = _passthroughCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            _passthroughCamera.targetTexture = rt;
            _passthroughCamera.Render();

            RenderTexture.active = rt;
            Texture2D fullTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            fullTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            fullTexture.Apply();

            Debug.Log("[LIVE] ✓ Captured using RenderTexture!");

            // ✅ Simple center crop with zoom
            Texture2D croppedTexture = SimpleCenterCrop(fullTexture, zoomFactor);

            Debug.Log($"[LIVE] ✓ Cropped to: {croppedTexture.width}x{croppedTexture.height} (zoom: {zoomFactor}x)");
            Destroy(fullTexture);

            GameObject piece = CreateCapturedPiece(croppedTexture, capturePos, captureRot, captureScale);

            capturedPlanes.Add(piece);

            if (captureSound && audioSource)
                audioSource.PlayOneShot(captureSound);

            Debug.Log($"[LIVE] ✓✓✓ Created piece #{capturedPlanes.Count}");
        }
        finally
        {
            _passthroughCamera.targetTexture = previousRT;
            RenderTexture.active = previousActive;
            rt.Release();
            Destroy(rt);
        }
    }

    // =========================================================
    //  Calculate Frame Corners in 3D Space
    // =========================================================
    private Vector3[] CalculateFrameCorners(Vector3 center, Quaternion rotation, Vector3 scale)
    {
        // Calculate the 4 corners of the frame quad
        Vector3[] corners = new Vector3[4];

        float halfWidth = scale.x / 2f;
        float halfHeight = scale.y / 2f;

        // Local space corners
        Vector3[] localCorners = new Vector3[]
        {
            new Vector3(-halfWidth, -halfHeight, 0), // Bottom-left
            new Vector3(halfWidth, -halfHeight, 0),  // Bottom-right
            new Vector3(halfWidth, halfHeight, 0),   // Top-right
            new Vector3(-halfWidth, halfHeight, 0)   // Top-left
        };

        // Transform to world space
        for (int i = 0; i < 4; i++)
        {
            corners[i] = center + rotation * localCorners[i];
        }

        return corners;
    }

    // =========================================================
    //  Crop Texture Based on Frame's 3D Position
    // =========================================================
    private Texture2D CropToFramePosition(Texture2D sourceTexture, Vector3[] frameCorners, Vector3 frameScale, int webcamWidth, int webcamHeight)
    {
        var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(_passthroughCameraEye);

        // Project each 3D corner to 2D UV coordinates in the webcam texture
        Vector2[] uvCorners = new Vector2[4];

        for (int i = 0; i < 4; i++)
        {
            // Convert world position to camera-relative ray
            Vector3 fromCamera = frameCorners[i] - _passthroughCamera.transform.position;

            // Project to screen space using camera intrinsics
            Vector3 cameraLocal = _passthroughCamera.transform.InverseTransformDirection(fromCamera);

            // Simple perspective projection
            float u = (cameraLocal.x / cameraLocal.z) * intrinsics.FocalLength.x + intrinsics.PrincipalPoint.x;
            float v = (cameraLocal.y / cameraLocal.z) * intrinsics.FocalLength.y + intrinsics.PrincipalPoint.y;

            // Normalize to UV space (0-1)
            uvCorners[i] = new Vector2(
                u / intrinsics.Resolution.x,
                v / intrinsics.Resolution.y
            );

            Debug.Log($"[LIVE] Corner {i}: World {frameCorners[i]} -> UV {uvCorners[i]}");
        }

        // Find bounding box in UV space
        float minU = Mathf.Min(uvCorners[0].x, uvCorners[1].x, uvCorners[2].x, uvCorners[3].x);
        float maxU = Mathf.Max(uvCorners[0].x, uvCorners[1].x, uvCorners[2].x, uvCorners[3].x);
        float minV = Mathf.Min(uvCorners[0].y, uvCorners[1].y, uvCorners[2].y, uvCorners[3].y);
        float maxV = Mathf.Max(uvCorners[0].y, uvCorners[1].y, uvCorners[2].y, uvCorners[3].y);

        // Calculate target aspect ratio from actual 3D frame dimensions (width / height)
        float targetAspectRatio = frameScale.x / frameScale.y;

        // Calculate current bounding box dimensions
        float currentWidth = maxU - minU;
        float currentHeight = maxV - minV;
        float currentAspectRatio = currentWidth / currentHeight;

        Debug.Log($"[LIVE] Frame aspect ratio: {targetAspectRatio:F2} | Bounding box aspect ratio: {currentAspectRatio:F2}");

        // Adjust crop region to match target aspect ratio (like CSS object-fit: contain)
        // Find the largest rectangle with target aspect ratio that fits INSIDE the bounding box
        float centerU = (minU + maxU) / 2f;
        float centerV = (minV + maxV) / 2f;

        if (currentAspectRatio > targetAspectRatio)
        {
            // Bounding box is too wide - reduce width to match aspect ratio
            float newWidth = currentHeight * targetAspectRatio;
            minU = centerU - newWidth / 2f;
            maxU = centerU + newWidth / 2f;
            Debug.Log($"[LIVE] Reduced width: {currentWidth:F3} -> {newWidth:F3} (contains within height)");
        }
        else
        {
            // Bounding box is too tall - reduce height to match aspect ratio
            float newHeight = currentWidth / targetAspectRatio;
            minV = centerV - newHeight / 2f;
            maxV = centerV + newHeight / 2f;
            Debug.Log($"[LIVE] Reduced height: {currentHeight:F3} -> {newHeight:F3} (contains within width)");
        }

        // Clamp to valid range
        minU = Mathf.Clamp01(minU);
        maxU = Mathf.Clamp01(maxU);
        minV = Mathf.Clamp01(minV);
        maxV = Mathf.Clamp01(maxV);

        // Convert UV to pixel coordinates
        int startX = Mathf.RoundToInt(minU * webcamWidth);
        int startY = Mathf.RoundToInt(minV * webcamHeight);
        int cropWidth = Mathf.RoundToInt((maxU - minU) * webcamWidth);
        int cropHeight = Mathf.RoundToInt((maxV - minV) * webcamHeight);

        // Ensure valid crop dimensions
        cropWidth = Mathf.Max(1, Mathf.Min(cropWidth, webcamWidth - startX));
        cropHeight = Mathf.Max(1, Mathf.Min(cropHeight, webcamHeight - startY));

        Debug.Log($"[LIVE] Crop region - UV: ({minU:F3},{minV:F3}) to ({maxU:F3},{maxV:F3})");
        Debug.Log($"[LIVE] Crop region - Pixels: ({startX},{startY}) size: {cropWidth}x{cropHeight}");

        // Extract the crop region
        Color[] croppedPixels = sourceTexture.GetPixels(startX, startY, cropWidth, cropHeight);

        // Calculate output dimensions that preserve the frame's aspect ratio
        // Use captureWidth as the base, calculate height from aspect ratio
        int outputWidth = captureWidth;
        int outputHeight = Mathf.RoundToInt(captureWidth / targetAspectRatio);

        // If height exceeds captureHeight, use captureHeight as base instead
        if (outputHeight > captureHeight)
        {
            outputHeight = captureHeight;
            outputWidth = Mathf.RoundToInt(captureHeight * targetAspectRatio);
        }

        Debug.Log($"[LIVE] Frame scale: {frameScale.x:F3}x{frameScale.y:F3}");
        Debug.Log($"[LIVE] Output dimensions: {outputWidth}x{outputHeight}");

        // Create output texture with aspect-ratio-preserving dimensions
        Texture2D outputTexture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGB24, false);

        // Scale to output resolution
        Color[] scaledPixels = new Color[outputWidth * outputHeight];
        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                float u = (float)x / (outputWidth - 1);
                float v = (float)y / (outputHeight - 1);

                int srcX = Mathf.RoundToInt(u * (cropWidth - 1));
                int srcY = Mathf.RoundToInt(v * (cropHeight - 1));

                scaledPixels[y * outputWidth + x] = croppedPixels[srcY * cropWidth + srcX];
            }
        }

        outputTexture.SetPixels(scaledPixels);
        outputTexture.Apply();

        return outputTexture;
    }

    // =========================================================
    //  Aspect-Ratio-Preserving Crop (like CSS object-fit: cover)
    // =========================================================
    private Texture2D SimpleCenterCrop(Texture2D sourceTexture, float zoom)
    {
        int sourceWidth = sourceTexture.width;
        int sourceHeight = sourceTexture.height;

        // Target aspect ratio from captureWidth/captureHeight
        float targetAspect = (float)captureWidth / captureHeight;
        float sourceAspect = (float)sourceWidth / sourceHeight;

        int cropWidth, cropHeight;

        if (sourceAspect > targetAspect)
        {
            // Source is wider - crop width to match target aspect
            cropHeight = Mathf.RoundToInt(sourceHeight / zoom);
            cropWidth = Mathf.RoundToInt(cropHeight * targetAspect);
        }
        else
        {
            // Source is taller - crop height to match target aspect
            cropWidth = Mathf.RoundToInt(sourceWidth / zoom);
            cropHeight = Mathf.RoundToInt(cropWidth / targetAspect);
        }

        // Ensure crop dimensions don't exceed source
        cropWidth = Mathf.Min(cropWidth, sourceWidth);
        cropHeight = Mathf.Min(cropHeight, sourceHeight);

        // Center the crop
        int startX = (sourceWidth - cropWidth) / 2;
        int startY = (sourceHeight - cropHeight) / 2;

        Debug.Log($"[LIVE] AspectCrop - Source: {sourceWidth}x{sourceHeight} ({sourceAspect:F2}), " +
                  $"Target: {captureWidth}x{captureHeight} ({targetAspect:F2}), " +
                  $"Crop: {cropWidth}x{cropHeight} at ({startX},{startY})");

        // Extract center crop with correct aspect ratio
        Color[] croppedPixels = sourceTexture.GetPixels(startX, startY, cropWidth, cropHeight);

        // Create new texture at output resolution
        Texture2D croppedTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);

        // Scale the cropped pixels to fill the output resolution (bilinear sampling)
        Color[] scaledPixels = new Color[captureWidth * captureHeight];
        for (int y = 0; y < captureHeight; y++)
        {
            for (int x = 0; x < captureWidth; x++)
            {
                // Map output pixel to source crop pixel with bilinear filtering
                float u = (float)x / (captureWidth - 1);
                float v = (float)y / (captureHeight - 1);

                float srcXFloat = u * (cropWidth - 1);
                float srcYFloat = v * (cropHeight - 1);

                int srcX = Mathf.FloorToInt(srcXFloat);
                int srcY = Mathf.FloorToInt(srcYFloat);

                // Bilinear interpolation
                int srcX1 = Mathf.Min(srcX + 1, cropWidth - 1);
                int srcY1 = Mathf.Min(srcY + 1, cropHeight - 1);

                float fracX = srcXFloat - srcX;
                float fracY = srcYFloat - srcY;

                Color c00 = croppedPixels[srcY * cropWidth + srcX];
                Color c10 = croppedPixels[srcY * cropWidth + srcX1];
                Color c01 = croppedPixels[srcY1 * cropWidth + srcX];
                Color c11 = croppedPixels[srcY1 * cropWidth + srcX1];

                Color cX0 = Color.Lerp(c00, c10, fracX);
                Color cX1 = Color.Lerp(c01, c11, fracX);
                Color final = Color.Lerp(cX0, cX1, fracY);

                scaledPixels[y * captureWidth + x] = final;
            }
        }

        croppedTexture.SetPixels(scaledPixels);
        croppedTexture.Apply();

        return croppedTexture;
    }

    // =========================================================
    //  Create Quad Frame with Captured Image as Texture
    // =========================================================
private GameObject CreateCapturedPiece(Texture2D texture, Vector3 position, Quaternion rotation, Vector3 scale)
{
    Debug.Log("[LIVE] ========== CreateCapturedPiece CALLED ==========");
    Debug.Log($"[LIVE] Texture: {(texture != null ? $"{texture.width}x{texture.height}" : "NULL")}");
    Debug.Log($"[LIVE] Position: {position}, Rotation: {rotation}, Scale: {scale}");

    GameObject piece = new GameObject($"CapturedPiece_{capturedPlanes.Count}");

    // Set rotation first
    piece.transform.rotation = rotation;

    // Position with small offset along the frame's forward direction to be visible
    // HandGestureDetector already offsets toward camera, add tiny forward offset
    Vector3 forwardOffset = rotation * Vector3.forward * 0.001f; // 1mm forward
    piece.transform.position = position + forwardOffset;

    piece.transform.localScale = scale;   // finger-defined size

    // -----------------------------------------------------------
    // Mesh - Create 1x1 quad, let transform.localScale handle sizing
    // -----------------------------------------------------------
    MeshFilter mf = piece.AddComponent<MeshFilter>();
    MeshRenderer mr = piece.AddComponent<MeshRenderer>();

    mf.mesh = CreateQuadMesh(new Vector2(1f, 1f));   // 1×1 quad, scaling done by transform

    // -----------------------------------------------------------
    // Material - Use same shader approach as QRCodeDetection
    // -----------------------------------------------------------
    Shader shader =
        Shader.Find("Unlit/Texture") ??
        Shader.Find("Mobile/Unlit (Supports Lightmap)") ??
        Shader.Find("Sprites/Default");

    if (shader == null)
    {
        Debug.LogError("[LIVE] No suitable unlit shader found! Falling back to Standard");
        shader = Shader.Find("Standard");
    }

    Debug.Log($"[LIVE] Using shader: {shader.name}");

    Material pieceMat = new Material(shader);
    pieceMat.mainTexture = texture;

    // Disable backface culling so texture is visible from both sides
    pieceMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

    // Set render queue to render on top of passthrough
    pieceMat.renderQueue = 3000;

    mr.material = pieceMat;
    mr.shadowCastingMode = ShadowCastingMode.Off;
    mr.receiveShadows = false;

    // Toggle renderer to force refresh (same as QRCodeDetection)
    mr.enabled = false;
    mr.enabled = true;

    // Check if texture has actual image data
    Color testPixel = texture.GetPixel(texture.width / 2, texture.height / 2);
    Debug.Log($"[LIVE] Texture center pixel color: {testPixel} (should not be pure black/white)");

    Debug.Log($"[LIVE] ✓ Created piece - Texture:{texture.width}x{texture.height}");
    Debug.Log($"[LIVE] Material shader: {pieceMat.shader.name}");
    Debug.Log($"[LIVE] Material mainTexture: {(pieceMat.mainTexture != null ? "SET" : "NULL")}");
    Debug.Log($"[LIVE] Material mainTexture dimensions: {pieceMat.mainTexture.width}x{pieceMat.mainTexture.height}");
    Debug.Log($"[LIVE] ✓ Transform scale: {scale}");
    Debug.Log($"[LIVE] ✓ Position: {piece.transform.position}");
    Debug.Log($"[LIVE] ✓ Rotation: {piece.transform.rotation.eulerAngles}");
    Debug.Log($"[LIVE] ✓ Active: {piece.activeSelf}");
    Debug.Log($"[LIVE] ✓ Layer: {piece.layer}");
    Debug.Log($"[LIVE] ✓ MeshRenderer enabled: {mr.enabled}");
    Debug.Log($"[LIVE] ✓ Mesh vertex count: {mf.mesh.vertexCount}");

    // -----------------------------------------------------------
    // Add border for debugging (uncomment to see frame outline)
    // -----------------------------------------------------------
    // AddBorder(piece);

    return piece;
}

    // =========================================================
    //  Webcam stuff
    // =========================================================
    /// <summary>
    /// Gets WebCamTexture using reflection to find the correct property
    /// </summary>
    private WebCamTexture GetWebCamTexture()
    {
        if (_cachedWebCamTexture != null && _cachedWebCamTexture.isPlaying)
        {
            return _cachedWebCamTexture;
        }

        if (_webCamTextureManager == null)
        {
            Debug.LogError("[LIVE] WebCamTextureManager is null!");
            return null;
        }

        var managerType = _webCamTextureManager.GetType();

        // Try all possible property/field names
        string[] possibleNames = {
            "Texture", "WebCamTexture", "CameraTexture", "_texture",
            "_webCamTexture", "_cameraTexture", "texture", "webCamTexture"
        };

        foreach (var name in possibleNames)
        {
            // Try property
            var prop = managerType.GetProperty(name,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (prop != null)
            {
                var value = prop.GetValue(_webCamTextureManager) as WebCamTexture;
                if (value != null)
                {
                    Debug.Log($"[LIVE] Found WebCamTexture via property: {name}");
                    _cachedWebCamTexture = value;
                    return value;
                }
            }

            // Try field
            var field = managerType.GetField(name,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                var value = field.GetValue(_webCamTextureManager) as WebCamTexture;
                if (value != null)
                {
                    Debug.Log($"[LIVE] Found WebCamTexture via field: {name}");
                    _cachedWebCamTexture = value;
                    return value;
                }
            }
        }

        // Log all available members for debugging
        var properties = managerType.GetProperties().Select(p => p.Name);
        var fields = managerType.GetFields().Select(f => f.Name);
        Debug.LogError($"[LIVE] Could not find WebCamTexture! Available properties: {string.Join(", ", properties)}");
        Debug.LogError($"[LIVE] Available fields: {string.Join(", ", fields)}");

        return null;
    }

    // =========================================================
    //  GUI
    // =========================================================
    void CreateGUI()
    {
        // TEMPORARILY DISABLED - Debug GUI text hidden
        return;

        TMP_Settings.LoadDefaultSettings();

        GameObject canvasObj = new GameObject("MaskGUI");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        canvasObj.transform.localScale = Vector3.one * 0.0015f;
        canvasObj.transform.position = cam.position + cam.forward * 0.6f;
        canvasObj.transform.rotation = cam.rotation;

        GameObject t = new GameObject("DebugText");
        t.transform.SetParent(canvasObj.transform, false);

        gui = t.AddComponent<TextMeshProUGUI>();
        gui.fontSize = 48;
        gui.alignment = TextAlignmentOptions.TopLeft;
        gui.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 500);

        gui.text = "Booting...";
    }


    private void AddBorder(GameObject quad)
    {
        LineRenderer lr = quad.AddComponent<LineRenderer>();

        lr.loop = true;
        lr.positionCount = 4;

        // Backface-normal fix: always visible
        lr.useWorldSpace = true;

        lr.startWidth = 0.005f;
        lr.endWidth = 0.005f;

        // Bright visible color
        lr.startColor = Color.yellow;
        lr.endColor = Color.yellow;

        // Render above passthrough
        lr.material = new Material(Shader.Find("Unlit/Color"));
        lr.material.color = Color.yellow;
        lr.material.renderQueue = 5000;

        // Define border corners in local space
        Vector3 p0 = new Vector3(-0.5f, -0.5f, 0f);
        Vector3 p1 = new Vector3(-0.5f, 0.5f, 0f);
        Vector3 p2 = new Vector3(0.5f, 0.5f, 0f);
        Vector3 p3 = new Vector3(0.5f, -0.5f, 0f);

        // Convert to world positions based on quad transform
        lr.SetPosition(0, quad.transform.TransformPoint(p0));
        lr.SetPosition(1, quad.transform.TransformPoint(p1));
        lr.SetPosition(2, quad.transform.TransformPoint(p2));
        lr.SetPosition(3, quad.transform.TransformPoint(p3));
    }

private Mesh CreateQuadMesh(Vector2 size)
{
    Mesh mesh = new Mesh();
    
    float w = size.x / 2f;
    float h = size.y / 2f;

    mesh.vertices = new Vector3[]
    {
        new Vector3(-w, -h, 0),
        new Vector3( w, -h, 0),
        new Vector3( w,  h, 0),
        new Vector3(-w,  h, 0),
    };

    mesh.uv = new Vector2[]
    {
        new Vector2(0, 0),
        new Vector2(1, 0),
        new Vector2(1, 1),
        new Vector2(0, 1),
    };

    mesh.triangles = new int[]
    {
        0, 2, 1,
        0, 3, 2
    };

    mesh.RecalculateNormals();
    return mesh;
}




}
