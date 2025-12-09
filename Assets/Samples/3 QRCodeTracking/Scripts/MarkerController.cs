using UnityEngine;
using TMPro;
using System;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MarkerController : MonoBehaviour
{
    [Header("Components")]
    private TextMeshProUGUI _textMesh;
    private GameObject _quadInstance;
    private MeshRenderer _quadRenderer;
    
    [Header("Settings")]
    public float lastUpdateTime;
    public float timeoutDuration = 2f;
    
    private Camera _camera;
    private bool _textureLoaded = false;
    private string _currentUrl = "";
    public string currentQRData = "";

    [Header("Collision")]
    [SerializeField] private bool showQuadOnCollision = true;
    private bool _hasCollided = false;

    [Header("Sound Effect")]
    private AudioSource _audioSource;
    private AudioClip _collisionSound;
    
    [Header("Capture")]
    private Action _onCaptureTriggered;
    private int _captureCount = 0;

    private void Awake()
    {
        Debug.Log($"[MarkerController] Awake on {gameObject.name}");
        
        // Find camera
        _camera = Camera.main;
        if (_camera == null)
        {
            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            Debug.Log("[MarkerController] Main Camera not found, searching OVRCameraRig...");
            if (cameraRig != null)
            {
                _camera = cameraRig.centerEyeAnchor.GetComponent<Camera>();
            }
        }
        
        _textMesh = GetComponentInChildren<TextMeshProUGUI>();
        if (_textMesh == null)
        {
            Debug.LogError("[MarkerController] No TextMeshProUGUI found!");
        }

        // Setup audio
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _collisionSound = Resources.Load<AudioClip>("Sounds/capture");
    }

    public void SetQuad(GameObject quad)
    {
        _quadInstance = quad;
        _quadRenderer = quad.GetComponent<MeshRenderer>();
        
        Debug.Log($"[MarkerController] Quad assigned to {gameObject.name}: {quad.name}");
        Debug.Log($"[MarkerController] Quad renderer: {(_quadRenderer != null ? "Found" : "NULL")}");
        Debug.Log($"[MarkerController] Quad material: {_quadRenderer?.material?.name ?? "NULL"}");
    }
    
    public void SetCaptureCallback(Action callback)
    {
        _onCaptureTriggered = callback;
        Debug.Log("[MarkerController] Capture callback set");
    }

    public void UpdateMarker(Vector3 position, Quaternion rotation, Vector3 scale, string text)
    {
        Debug.Log($"[MarkerController] UpdateMarker: {text}");

        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = scale;

        lastUpdateTime = Time.time;
        currentQRData = text;

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        if (_textMesh)
        {
            _textMesh.text = "CAPTURE";
            _textMesh.fontSize = 20;
            _textMesh.alignment = TextAlignmentOptions.Center;
            _textMesh.color = Color.white;
        }
    }

    private bool IsUrl(string text)
    {
        return text.StartsWith("http://") || text.StartsWith("https://");
    }

    private IEnumerator LoadImageFromURL(string url)
    {
        Debug.Log($"[MarkerController] Loading image from: {url}");

        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[MarkerController] Failed: {www.error}");
            yield break;
        }

        Texture2D downloadedTexture = DownloadHandlerTexture.GetContent(www);
        Debug.Log($"[MarkerController] ✓ Downloaded: {downloadedTexture.width}x{downloadedTexture.height}");

        if (_quadInstance == null)
        {
            Debug.LogError("[MarkerController] Quad is NULL!");
            yield break;
        }

        if (_quadRenderer == null)
        {
            Debug.LogError("[MarkerController] Renderer is NULL!");
            yield break;
        }

        float aspectRatio = (float)downloadedTexture.width / downloadedTexture.height;
        Vector3 baseScale = Vector3.one * 0.8f;
        
        if (aspectRatio > 1f)
        {
            _quadInstance.transform.localScale = new Vector3(baseScale.x * aspectRatio, baseScale.y, baseScale.z);
        }
        else
        {
            _quadInstance.transform.localScale = new Vector3(baseScale.x, baseScale.y / aspectRatio, baseScale.z);
        }

        Material material = _quadRenderer.material;
        material.mainTexture = downloadedTexture;
        
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", downloadedTexture);
        }
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", downloadedTexture);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        _textureLoaded = true;

        Debug.Log("[MarkerController] ✓✓✓ Texture applied!");
    }

    private void Update()
    {
        // Text faces camera
        if (_textMesh && _camera != null)
        {
            _textMesh.transform.rotation = Quaternion.LookRotation(
                _textMesh.transform.position - _camera.transform.position
            );
        }

        if (_quadInstance != null && _quadInstance.activeSelf && _camera != null)
        {
            _quadInstance.transform.Rotate(0, 0, 0);
        }
    }

    private void ResetMarker()
    {
        _currentUrl = "";
        _textureLoaded = false;
        _hasCollided = false;
        currentQRData = "";
        _captureCount = 0;

        if (_quadInstance != null)
        {
            _quadInstance.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[MarkerController] Trigger entered by: {other.gameObject.name}");

        if (other.CompareTag("Index"))
        {
            _hasCollided = true;
            _captureCount++;
            
            Debug.Log($"[MarkerController] ✓ Touch #{_captureCount} detected!");

            if (_collisionSound != null)
            {
                _audioSource.PlayOneShot(_collisionSound);
            }

            if (_onCaptureTriggered != null)
            {
                _onCaptureTriggered.Invoke();
                Debug.Log($"[MarkerController] ✓✓✓ CAPTURE TRIGGERED (Touch #{_captureCount})");
            }
            else
            {
                Debug.LogWarning("[MarkerController] No capture callback set!");
            }
        }
    }    
    
    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[MarkerController] Trigger exited by: {other.gameObject.name}");

        if (other.CompareTag("Index"))
        {
            _hasCollided = false;
            Debug.Log("[MarkerController] ✗ Collision ended!");

            if (showQuadOnCollision == false && _quadInstance != null)
            {
                _quadInstance.SetActive(false);
            }
        }
    }

    private void OnDisable()
    {
        ResetMarker();
    }
}