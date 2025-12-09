using System.Collections.Generic;
using UnityEngine;

public class MarkerPool : MonoBehaviour
{
    public static MarkerPool Instance { get; private set; }

    [Tooltip("Prefab for a QR code marker.")]
    [SerializeField] private GameObject markerPrefab;

    [Tooltip("Number of markers to pre-instantiate.")]
    [SerializeField] private int poolSize = 9;

    private List<GameObject> _pool;

    private void Awake()
    {
        Debug.Log($"[MarkerPool] Initializing...");

        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        _pool = new List<GameObject>(poolSize);

        for (var i = 0; i < poolSize; i++)
        {
            // Create marker from prefab
            var marker = Instantiate(markerPrefab, transform);
            marker.name = $"Marker_{i}";
            marker.SetActive(false);
            _pool.Add(marker);

            Debug.Log($"[MarkerPool] Created marker: {marker.name}");

            // Add collider to marker for collision detection
            BoxCollider markerCollider = marker.GetComponent<BoxCollider>();
            if (markerCollider == null)
            {
                markerCollider = marker.AddComponent<BoxCollider>();
            }
            markerCollider.size = new Vector3(0.2f, 0.2f, 0.05f);
            markerCollider.isTrigger = true;

            Debug.Log($"[MarkerPool] Added collider to {marker.name}");

            // Create a simple 3D Quad for this marker
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"ImageQuad_{i}";
            
            // Parent to marker
            quad.transform.SetParent(marker.transform, false);
            quad.transform.localPosition = new Vector3(0, 0, 0.01f);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = Vector3.one * 0.8f;
            
            // Setup material
            MeshRenderer quadRenderer = quad.GetComponent<MeshRenderer>();
            if (quadRenderer != null)
            {
                Shader workingShader = null;
                
                string[] shaderNames = new string[]
                {
                    "Unlit/Texture",
                    "Mobile/Unlit (Supports Lightmap)",
                    "Mobile/Diffuse",
                    "Sprites/Default",
                    "UI/Default"
                };
                
                foreach (string shaderName in shaderNames)
                {
                    Shader shader = Shader.Find(shaderName);
                    if (shader != null && shader.isSupported)
                    {
                        workingShader = shader;
                        Debug.Log($"[MarkerPool] Found working shader: {shaderName}");
                        break;
                    }
                }
                
                if (workingShader != null)
                {
                    Material newMat = new Material(workingShader);
                    newMat.color = Color.white;
                    quadRenderer.material = newMat;
                    Debug.Log($"[MarkerPool] Applied working shader: {workingShader.name}");
                }
                else
                {
                    Debug.LogError("[MarkerPool] CRITICAL: No supported shader found! Quad will be magenta.");
                }
            }
            
            if (quadRenderer != null)
            {
                quadRenderer.enabled = true;
            }
            
            Collider quadCollider = quad.GetComponent<Collider>();
            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }
            
            quad.SetActive(false);

            MarkerController controller = marker.GetComponent<MarkerController>();
            if (controller != null)
            {
                controller.SetQuad(quad);
                Debug.Log($"[MarkerPool] Assigned quad to {marker.name}");
            }
            else
            {
                Debug.LogError($"[MarkerPool] No MarkerController on {marker.name}!");
            }
        }

        Debug.Log($"[MarkerPool] Initialized {_pool.Count} markers");
    }

    public GameObject GetMarker()
    {
        // DETAILED LOGGING
        int activeCount = 0;
        int inactiveCount = 0;
        
        foreach (var marker in _pool)
        {
            if (marker.activeSelf)
            {
                activeCount++;
                var controller = marker.GetComponent<MarkerController>();
                Debug.Log($"[MarkerPool] Active marker: {marker.name}, QR: {controller?.currentQRData ?? "NULL"}");
            }
            else
            {
                inactiveCount++;
            }
        }
        
        Debug.Log($"[MarkerPool] Pool status - Active: {activeCount}, Inactive: {inactiveCount}");
        
        foreach (var marker in _pool)
        {
            if (marker.activeSelf) continue;
            
            Debug.Log($"[MarkerPool] ✓ Returning INACTIVE marker: {marker.name}");
            marker.SetActive(true);
            return marker;
        }

        Debug.LogError($"[MarkerPool] ✗✗✗ NO AVAILABLE MARKERS! All {_pool.Count} markers are active!");
        return null;
    }
}