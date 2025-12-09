using System.Collections.Generic;
using PassthroughCameraSamples;
using UnityEngine;
using Meta.XR;
using System;
using System.Linq;

public class QrCodeDisplayManager : MonoBehaviour
{
#if ZXING_ENABLED
    [SerializeField] private QrCodeScanner scanner;
    [SerializeField] private EnvironmentRaycastManager envRaycastManager;

    private readonly Dictionary<string, MarkerController> _activeMarkers = new();
    private WebCamTextureManager _webCamTextureManager;
    private PassthroughCameraEye _passthroughCameraEye;
    private Camera _passthroughCamera;

    private enum QrRaycastMode
    {
        CenterOnly,
        PerCorner
    }
    
    [SerializeField] private QrRaycastMode raycastMode = QrRaycastMode.PerCorner;
    
    [Header("Passthrough Capture")]
    [SerializeField] private bool enableCapture = true;
    [SerializeField] private int captureResolution = 512;
    [SerializeField] private Vector2 captureSize = new Vector2(0.4f, 0.4f);
    
    // Track captured pieces per QR code
    private Dictionary<string, List<GameObject>> _capturedPieces = new Dictionary<string, List<GameObject>>();
    
    // Cache for WebCamTexture access
    private WebCamTexture _cachedWebCamTexture;

    private void Awake()
    {
        _webCamTextureManager = FindAnyObjectByType<WebCamTextureManager>();
        if (_webCamTextureManager == null)
        {
            Debug.LogWarning("[QrCodeDisplayManager] No WebCamTextureManager found. Some QR capture features will be disabled.");
        }
        else
        {
            _passthroughCameraEye = _webCamTextureManager.Eye;
            Debug.Log("[QrCodeDisplayManager] WebCamTextureManager found. Some QR capture features will be enabled.");
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
        
        Debug.Log($"[QrCodeDisplayManager] Passthrough camera: {(_passthroughCamera != null ? "Found" : "NULL")}");
    }

    private void Update()
    {
        UpdateMarkers();
    }
    
    private async void UpdateMarkers()
    {
        var qrResults = await scanner.ScanFrameAsync() ?? Array.Empty<QrCodeResult>();

        foreach (var qrResult in qrResults)
        {
            if (qrResult?.corners == null || qrResult.corners.Length < 4)
            {
                continue;
            }

            var count = qrResult.corners.Length;
            var uvs = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                uvs[i] = new Vector2(qrResult.corners[i].x, qrResult.corners[i].y);
            }

            var centerUV = Vector2.zero;
            foreach (var uv in uvs) centerUV += uv;
            centerUV /= count;

            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(_passthroughCameraEye);
            var centerPixel = new Vector2Int(
                Mathf.RoundToInt(centerUV.x * intrinsics.Resolution.x),
                Mathf.RoundToInt(centerUV.y * intrinsics.Resolution.y)
            );

            var centerRay = PassthroughCameraUtils.ScreenPointToRayInWorld(_passthroughCameraEye, centerPixel);
            if (!envRaycastManager || !envRaycastManager.Raycast(centerRay, out var centerHitInfo))
            {
                continue;
            }

            var center = centerHitInfo.point;
            var distance = Vector3.Distance(centerRay.origin, center);
            var worldCorners = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                var pixelCoord = new Vector2Int(
                    Mathf.RoundToInt(uvs[i].x * intrinsics.Resolution.x),
                    Mathf.RoundToInt(uvs[i].y * intrinsics.Resolution.y)
                );
                var r = PassthroughCameraUtils.ScreenPointToRayInWorld(_passthroughCameraEye, pixelCoord);

                if (raycastMode == QrRaycastMode.PerCorner)
                {
                    if (envRaycastManager.Raycast(r, out var cornerHit))
                    {
                        worldCorners[i] = cornerHit.point;
                    }
                    else
                    {
                        worldCorners[i] = r.origin + r.direction * distance;
                    }
                }
                else // CenterOnly
                {
                    worldCorners[i] = r.origin + r.direction * distance;
                }
            }

            // Pose estimation
            center = Vector3.zero;
            foreach (var c in worldCorners)
            {
                center += c;
            }
            center /= count;

            var up = (worldCorners[1] - worldCorners[0]).normalized;
            var right = (worldCorners[2] - worldCorners[1]).normalized;
            var normal = -Vector3.Cross(up, right).normalized;
            var poseRot = Quaternion.LookRotation(normal, up);

            var width = Vector3.Distance(worldCorners[0], worldCorners[1]);
            var height = Vector3.Distance(worldCorners[0], worldCorners[3]);
            var scaleFactor = 1.5f;
            var scale = new Vector3(width * scaleFactor, height * scaleFactor, 1f);

            if (_activeMarkers.TryGetValue(qrResult.text, out var marker))
            {
                marker.UpdateMarker(center, poseRot, scale, qrResult.text);
            }
            else
            {
                var markerGo = MarkerPool.Instance.GetMarker();
                if (!markerGo)
                {
                    continue;
                }

                marker = markerGo.GetComponent<MarkerController>();
                if (!marker)
                {
                    continue;
                }

                Debug.Log($"[QrCodeDisplayManager] New marker for QR code: {qrResult.text}");

                marker.UpdateMarker(center, poseRot, scale, qrResult.text);
                _activeMarkers[qrResult.text] = marker;
                
                // Set the capture callback so marker can trigger capture
                if (enableCapture)
                {
                    marker.SetCaptureCallback(() => CapturePassthroughAtMarker(marker, qrResult.text));
                }
            }
        }

        // Cleanup
        var keysToRemove = new List<string>();
        foreach (var kvp in _activeMarkers)
        {
            if (!kvp.Value.gameObject.activeSelf)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _activeMarkers.Remove(key);
        }
    }
    
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
            Debug.LogError("[QrCodeDisplayManager] WebCamTextureManager is null!");
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
                    Debug.Log($"[QrCodeDisplayManager] Found WebCamTexture via property: {name}");
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
                    Debug.Log($"[QrCodeDisplayManager] Found WebCamTexture via field: {name}");
                    _cachedWebCamTexture = value;
                    return value;
                }
            }
        }
        
        // Log all available members for debugging
        var properties = managerType.GetProperties().Select(p => p.Name);
        var fields = managerType.GetFields().Select(f => f.Name);
        Debug.LogError($"[QrCodeDisplayManager] Could not find WebCamTexture! Available properties: {string.Join(", ", properties)}");
        Debug.LogError($"[QrCodeDisplayManager] Available fields: {string.Join(", ", fields)}");
        
        return null;
    }
    
    /// <summary>
    /// Captures the passthrough view at the marker's position
    /// Uses the webcam texture from the QR scanner
    /// </summary>
    private void CapturePassthroughAtMarker(MarkerController marker, string qrCode)
    {
        Debug.Log($"[QrCodeDisplayManager] ========== CAPTURE START ==========");
        Debug.Log($"[QrCodeDisplayManager] Attempting capture for QR: {qrCode}");
        
        // Try to get WebCamTexture
        WebCamTexture webCamTexture = GetWebCamTexture();
        
        if (webCamTexture == null || !webCamTexture.isPlaying)
        {
            Debug.LogWarning("[QrCodeDisplayManager] WebCamTexture not available, trying RenderTexture fallback");
            CaptureWithRenderTexture(marker, qrCode);
            return;
        }
        
        Debug.Log($"[QrCodeDisplayManager] ✓ WebCam texture active: {webCamTexture.width}x{webCamTexture.height}");
        Debug.Log($"[QrCodeDisplayManager] WebCam isPlaying: {webCamTexture.isPlaying}");
        Debug.Log($"[QrCodeDisplayManager] WebCam didUpdateThisFrame: {webCamTexture.didUpdateThisFrame}");
        
        try
        {
            // Create a copy of the current webcam frame
            Texture2D capturedTexture = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGB24, false);
            
            Color[] pixels = webCamTexture.GetPixels();
            Debug.Log($"[QrCodeDisplayManager] Got {pixels.Length} pixels from webcam");
            
            capturedTexture.SetPixels(pixels);
            capturedTexture.Apply();
            
            Debug.Log($"[QrCodeDisplayManager] ✓ Captured texture created: {capturedTexture.width}x{capturedTexture.height}");
            
            // Test: Check if we have actual image data
            Color testPixel = capturedTexture.GetPixel(capturedTexture.width / 2, capturedTexture.height / 2);
            Debug.Log($"[QrCodeDisplayManager] Center pixel color: {testPixel}");
            
            // Create the captured piece
            GameObject piece = CreateCapturedPiece(capturedTexture, marker.transform.position, marker.transform.rotation, qrCode);
            
            // Track the piece
            if (!_capturedPieces.ContainsKey(qrCode))
            {
                _capturedPieces[qrCode] = new List<GameObject>();
            }
            _capturedPieces[qrCode].Add(piece);
            
            Debug.Log($"[QrCodeDisplayManager] ✓✓✓ Created piece for QR '{qrCode}' (Total: {_capturedPieces[qrCode].Count})");
            Debug.Log($"[QrCodeDisplayManager] ========== CAPTURE END ==========");
        }
        catch (Exception e)
        {
            Debug.LogError($"[QrCodeDisplayManager] Exception during capture: {e.Message}");
            Debug.LogError($"[QrCodeDisplayManager] Stack trace: {e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Fallback capture method using RenderTexture
    /// </summary>
    private void CaptureWithRenderTexture(MarkerController marker, string qrCode)
    {
        if (_passthroughCamera == null)
        {
            Debug.LogError("[QrCodeDisplayManager] No camera available for capture!");
            return;
        }
        
        Debug.Log("[QrCodeDisplayManager] Using RenderTexture capture method");
        
        RenderTexture rt = new RenderTexture(captureResolution, captureResolution, 24);
        RenderTexture previousRT = _passthroughCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        
        try
        {
            _passthroughCamera.targetTexture = rt;
            _passthroughCamera.Render();
            
            RenderTexture.active = rt;
            Texture2D capturedTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            capturedTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            capturedTexture.Apply();
            
            Debug.Log("[QrCodeDisplayManager] ✓ Captured using RenderTexture!");
            
            GameObject piece = CreateCapturedPiece(capturedTexture, marker.transform.position, marker.transform.rotation, qrCode);
            
            if (!_capturedPieces.ContainsKey(qrCode))
            {
                _capturedPieces[qrCode] = new List<GameObject>();
            }
            _capturedPieces[qrCode].Add(piece);
            
            Debug.Log($"[QrCodeDisplayManager] ✓✓✓ Created piece for QR '{qrCode}' (Total: {_capturedPieces[qrCode].Count})");
        }
        finally
        {
            _passthroughCamera.targetTexture = previousRT;
            RenderTexture.active = previousActive;
            rt.Release();
            Destroy(rt);
        }
    }
    
    private GameObject CreateCapturedPiece(Texture2D texture, Vector3 position, Quaternion rotation, string qrCode)
    {
        // Get piece count for this QR code
        int pieceCount = _capturedPieces.ContainsKey(qrCode) ? _capturedPieces[qrCode].Count : 0;
        
        // Create independent GameObject (NOT parented to marker!)
        GameObject piece = new GameObject($"CapturedPiece_{qrCode}_{pieceCount}");
        
        // Offset position slightly forward to avoid z-fighting
        Vector3 offset = rotation * Vector3.forward * 0.05f;
        piece.transform.position = position + offset;
        piece.transform.rotation = rotation;
        
        // Create mesh
        MeshFilter meshFilter = piece.AddComponent<MeshFilter>();
        MeshRenderer pieceRenderer = piece.AddComponent<MeshRenderer>();
        
        meshFilter.mesh = CreateQuadMesh(captureSize);
        
        // DETAILED SHADER AND TEXTURE LOGGING
        Debug.Log($"[QrCodeDisplayManager] Texture details: {texture.width}x{texture.height}, format: {texture.format}");
        
        // Try multiple shaders
        Shader shader = null;
        string[] shaderNames = new string[]
        {
            "Unlit/Texture",
            "Mobile/Unlit (Supports Lightmap)",
            "Mobile/Diffuse",
            "Standard",
            "Sprites/Default"
        };
        
        foreach (string shaderName in shaderNames)
        {
            Shader testShader = Shader.Find(shaderName);
            if (testShader != null && testShader.isSupported)
            {
                shader = testShader;
                Debug.Log($"[QrCodeDisplayManager] Using shader: {shaderName}");
                break;
            }
        }
        
        if (shader == null)
        {
            Debug.LogError("[QrCodeDisplayManager] NO SUPPORTED SHADER FOUND!");
            shader = Shader.Find("Standard"); // Last resort
        }
        
        Material pieceMat = new Material(shader);
        pieceMat.mainTexture = texture;
        pieceMat.SetColor("_Color", new Color(1f, 1f, 1f, 1f));

        // Set all possible texture properties
        if (pieceMat.HasProperty("_MainTex"))
        {
            pieceMat.SetTexture("_MainTex", texture);
            Debug.Log("[QrCodeDisplayManager] Set _MainTex");
        }
        if (pieceMat.HasProperty("_BaseMap"))
        {
            pieceMat.SetTexture("_BaseMap", texture);
            Debug.Log("[QrCodeDisplayManager] Set _BaseMap");
        }
        if (pieceMat.HasProperty("_Color"))
        {
            pieceMat.SetColor("_Color", new Color(1f, 1f, 1f, 0.5f)); 
            Debug.Log("[QrCodeDisplayManager] Set _Color to white");
        }
        
        pieceRenderer.material = pieceMat;
        
        Debug.Log($"[QrCodeDisplayManager] Material shader: {pieceMat.shader.name}");
        Debug.Log($"[QrCodeDisplayManager] Material mainTexture: {(pieceMat.mainTexture != null ? "SET" : "NULL")}");
                
        Debug.Log($"[QrCodeDisplayManager] ✓ Created piece at {position}");
        
        return piece;
    }
    
private Mesh CreateQuadMesh(Vector2 size)
{
    Mesh mesh = new Mesh();
    mesh.name = "CapturedPieceMesh";
    
    float halfWidth = size.x / 2f;
    float halfHeight = size.y / 2f;
    
    // Simple single-sided quad - only 4 vertices needed
    Vector3[] vertices = new Vector3[]
    {
        new Vector3(-halfWidth, -halfHeight, 0),
        new Vector3(halfWidth, -halfHeight, 0),
        new Vector3(halfWidth, halfHeight, 0),
        new Vector3(-halfWidth, halfHeight, 0),
    };
    
    Vector2[] uvs = new Vector2[]
    {
        new Vector2(0, 0),
        new Vector2(1, 0),
        new Vector2(1, 1),
        new Vector2(0, 1),
    };
    
    // Single quad (2 triangles)
    int[] triangles = new int[]
    {
        0, 2, 1,
        0, 3, 2,
    };
    
    mesh.vertices = vertices;
    mesh.uv = uvs;
    mesh.triangles = triangles;
    mesh.RecalculateNormals();
    
    return mesh;
}

    public int GetTotalCapturedPieces()
    {
        int total = 0;
        foreach (var list in _capturedPieces.Values)
        {
            total += list.Count;
        }
        return total;
    }
    
    public void ClearAllCapturedPieces()
    {
        foreach (var list in _capturedPieces.Values)
        {
            foreach (var piece in list)
            {
                if (piece != null)
                {
                    Destroy(piece);
                }
            }
        }
        _capturedPieces.Clear();
        Debug.Log("[QrCodeDisplayManager] Cleared all captured pieces");
    }
#endif
}