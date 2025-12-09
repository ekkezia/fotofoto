using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Controls the instruction panel that appears at the start
/// Closes when user taps it with their index finger
/// </summary>
public class InstructionPanelController : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private GameObject instructionPanel; // Child panel GameObject with UI elements
    [SerializeField] private TextMeshProUGUI titleTextComponent;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private Canvas canvas; // Will be this GameObject if script is on Canvas
    
    [Header("Hand Animation")]
    [SerializeField] private GameObject instructionHand;
    [SerializeField] private float handRotationSpeed = 2f;
    [SerializeField] private float handRotationAngle = 30f;
    [SerializeField] private float handMoveDistance = 0.05f;
    [SerializeField] private Vector3 handInitialRotationOffset = new Vector3(0f, 0f, -45f); // Changed to -45 as requested
    [SerializeField] private bool enableHandProximityHiding = true; // Toggle hiding when user hand is near
    [SerializeField] private float handHideDistance = 0.5f;
    
    [Header("Settings")]
    [SerializeField] private string titleText = "fotoQR";
    [SerializeField] private string instructionMessage = "Welcome to QR Snapshot!\n\nScan any QR code in your environment, then tap it with your index finger to capture a photo of that moment.\n\nYour captured photos will float in 3D space where you took them.\n\nTap this panel with your index finger to begin.";
    [SerializeField] private bool showOnStart = true;
    
    [Header("Positioning")]
    [SerializeField] private float distanceFromCamera = 0.5f;
    [SerializeField] private float eyeLevelOffset = 0f;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private float positionDelay = 0.5f;
    
    [Header("Collider Settings")]
    [SerializeField] private Vector3 colliderSize = new Vector3(0.8f, 0.6f, 0.1f);
    [SerializeField] private Vector3 colliderCenter = Vector3.zero;
    
    [Header("Audio (Optional)")]
    [SerializeField] private AudioClip closeSound;
    private AudioSource _audioSource;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugCollider = true;
    [SerializeField] private bool testClosePanel = false;
    [SerializeField] private bool useManualCollisionDetection = true; // Bypass Unity's trigger system
    
    private bool _hasTriggeredClose = false;
    
    private BoxCollider _panelCollider;
    private bool _isVisible = true;
    private Camera _mainCamera;
    private OVRCameraRig _cameraRig;
    private Transform _userHandTransform;
    private Vector3 _handOriginalRotation;
    private Vector3 _handOriginalPosition;
    private bool _isPositioned = false;

    private void Awake()
    {
        Debug.Log("[InstructionPanel] Initializing...");
        
        _cameraRig = FindAnyObjectByType<OVRCameraRig>();
        if (_cameraRig != null)
        {
            _mainCamera = _cameraRig.centerEyeAnchor.GetComponent<Camera>();
            Debug.Log("[InstructionPanel] Found OVRCameraRig");
        }
        else
        {
            _mainCamera = Camera.main;
            Debug.LogWarning("[InstructionPanel] No OVRCameraRig found, using Camera.main");
        }
        
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        
        if (instructionPanel == null)
        {
            instructionPanel = gameObject;
        }
        
        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = GetComponent<Canvas>();
            }
        }
        
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _mainCamera;
            
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                if (canvasRect.sizeDelta.x == 0 || canvasRect.sizeDelta.y == 0)
                {
                    canvasRect.sizeDelta = new Vector2(800, 600);
                }
                
                if (canvasRect.localScale == Vector3.zero)
                {
                    canvasRect.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                }
            }
            
            var ovrOverlay = canvas.GetComponent<OVROverlayCanvas>();
            if (ovrOverlay != null)
            {
                ovrOverlay.enabled = false;
            }
        }
        
        if (titleTextComponent != null)
        {
            titleTextComponent.text = titleText;
        }
        
        if (instructionText == null)
        {
            instructionText = GetComponentInChildren<TextMeshProUGUI>();
        }
        
        if (instructionText != null)
        {
            instructionText.text = instructionMessage;
        }
        
        // Setup hand - store original position and apply rotation offset
        if (instructionHand != null)
        {
            _handOriginalPosition = instructionHand.transform.localPosition;
            _handOriginalRotation = instructionHand.transform.localEulerAngles;
            
            // Apply the -45 degree Z rotation (no animation on this)
            Vector3 adjustedRotation = _handOriginalRotation + handInitialRotationOffset;
            instructionHand.transform.localEulerAngles = adjustedRotation;
            _handOriginalRotation = adjustedRotation;
            
            Debug.Log($"[InstructionPanel] Hand setup - Position: {_handOriginalPosition}, Rotation: {adjustedRotation}");
        }
        else
        {
            Debug.LogWarning("[InstructionPanel] No instruction hand GameObject assigned!");
        }
        
        SetupCollider();
        
        Debug.Log("[InstructionPanel] Initialization complete");
    }

    private void SetupCollider()
    {
        // The Rigidbody and Collider MUST be on the same GameObject as this script
        // for OnTriggerEnter to work!
        GameObject colliderObject = gameObject; // THIS GameObject (should be Canvas)
        
        Debug.Log($"[InstructionPanel] Setting up collider on: {colliderObject.name}");
        
        // Check for existing Rigidbody
        Rigidbody existingRb = colliderObject.GetComponent<Rigidbody>();
        
        // Add Rigidbody if not found
        if (existingRb == null)
        {
            existingRb = colliderObject.AddComponent<Rigidbody>();
            Debug.Log("[InstructionPanel] Added Rigidbody to " + colliderObject.name);
        }
        
        // Configure Rigidbody
        existingRb.isKinematic = false; // Non-kinematic for trigger detection
        existingRb.useGravity = false;
        existingRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        existingRb.constraints = RigidbodyConstraints.FreezeAll; // Frozen so it doesn't move
        
        // Add BoxCollider to the SAME GameObject
        _panelCollider = colliderObject.GetComponent<BoxCollider>();
        if (_panelCollider == null)
        {
            _panelCollider = colliderObject.AddComponent<BoxCollider>();
            Debug.Log("[InstructionPanel] Added BoxCollider to " + colliderObject.name);
        }
        
        _panelCollider.isTrigger = true;
        _panelCollider.size = colliderSize;
        _panelCollider.center = colliderCenter;
        
        Debug.Log($"[InstructionPanel] ========== COLLIDER SETUP ==========");
        Debug.Log($"[InstructionPanel] Collider on GameObject: {colliderObject.name}");
        Debug.Log($"[InstructionPanel] Script on GameObject: {gameObject.name}");
        Debug.Log($"[InstructionPanel] SAME OBJECT: {colliderObject == gameObject}");
        Debug.Log($"[InstructionPanel] Has Rigidbody: {existingRb != null} (Kinematic: {existingRb.isKinematic})");
        Debug.Log($"[InstructionPanel] Collider Size: {_panelCollider.size}");
        Debug.Log($"[InstructionPanel] Collider Center: {_panelCollider.center}");
        Debug.Log($"[InstructionPanel] Collider is Trigger: {_panelCollider.isTrigger}");
        Debug.Log($"[InstructionPanel] GameObject Layer: {LayerMask.LayerToName(colliderObject.layer)}");
        
        // Check layer collision
        CheckLayerCollision();
    }
    
    private void CheckLayerCollision()
    {
        if (_panelCollider == null) return;
        
        int panelLayer = _panelCollider.gameObject.layer;
        
        // Find Index finger collider in scene
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (GameObject obj in allObjects)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null && col.CompareTag("Index"))
            {
                int indexLayer = col.gameObject.layer;
                bool canCollide = !Physics.GetIgnoreLayerCollision(panelLayer, indexLayer);
                
                Debug.Log($"[InstructionPanel] ========== LAYER COLLISION CHECK ==========");
                Debug.Log($"[InstructionPanel] Panel Layer: {panelLayer} ({LayerMask.LayerToName(panelLayer)})");
                Debug.Log($"[InstructionPanel] Index Layer: {indexLayer} ({LayerMask.LayerToName(indexLayer)})");
                Debug.Log($"[InstructionPanel] Can Collide: {canCollide}");
                
                // Check Index finger Rigidbody
                Rigidbody indexRb = col.GetComponent<Rigidbody>();
                if (indexRb == null)
                {
                    indexRb = col.GetComponentInParent<Rigidbody>();
                }
                
                Debug.Log($"[InstructionPanel] ========== INDEX RIGIDBODY CHECK ==========");
                Debug.Log($"[InstructionPanel] Index has Rigidbody: {indexRb != null}");
                if (indexRb != null)
                {
                    Debug.Log($"[InstructionPanel] Index Rigidbody isKinematic: {indexRb.isKinematic}");
                    Debug.Log($"[InstructionPanel] Index Rigidbody detectCollisions: {indexRb.detectCollisions}");
                    Debug.Log($"[InstructionPanel] Index Rigidbody collision mode: {indexRb.collisionDetectionMode}");
                }
                
                Debug.Log($"[InstructionPanel] Index Collider isTrigger: {col.isTrigger}");
                Debug.Log($"[InstructionPanel] Index Collider enabled: {col.enabled}");
                
                // Check panel Rigidbody
                Rigidbody panelRb = _panelCollider.GetComponent<Rigidbody>();
                Debug.Log($"[InstructionPanel] ========== PANEL RIGIDBODY CHECK ==========");
                Debug.Log($"[InstructionPanel] Panel Rigidbody isKinematic: {panelRb?.isKinematic}");
                Debug.Log($"[InstructionPanel] Panel Rigidbody detectCollisions: {panelRb?.detectCollisions}");
                Debug.Log($"[InstructionPanel] Panel Collider isTrigger: {_panelCollider.isTrigger}");
                
                // CRITICAL CHECK: For triggers to work, at least one must have a non-kinematic Rigidbody
                // OR both must be kinematic but moving
                bool panelIsKinematic = panelRb != null && panelRb.isKinematic;
                bool indexIsKinematic = indexRb != null && indexRb.isKinematic;
                
                if (panelIsKinematic && indexIsKinematic)
                {
                    Debug.LogWarning($"[InstructionPanel] ⚠️ BOTH Rigidbodies are kinematic!");
                    Debug.LogWarning($"[InstructionPanel] Trigger events may not fire between two kinematic Rigidbodies");
                    Debug.LogWarning($"[InstructionPanel] Solution: Make Panel Rigidbody non-kinematic OR ensure Index moves via Rigidbody.MovePosition");
                }
                
                if (!canCollide)
                {
                    Debug.LogError($"[InstructionPanel] ❌ LAYERS CANNOT COLLIDE!");
                }
                
                break;
            }
        }
    }

    private void Start()
    {
        StartCoroutine(DelayedStart());
    }

    private IEnumerator DelayedStart()
    {
        yield return new WaitForSeconds(positionDelay);
        yield return null;
        
        PositionPanel();
        _isPositioned = true;
        
        if (showOnStart)
        {
            ShowPanel();
        }
        else
        {
            HidePanel();
        }
        
        Debug.Log("[InstructionPanel] Delayed start complete, panel positioned");
    }

    private void PositionPanel()
    {
        if (canvas == null || _mainCamera == null)
        {
            Debug.LogError("[InstructionPanel] Cannot position - canvas or camera is null!");
            return;
        }
        
        Vector3 targetPosition = _mainCamera.transform.position + (_mainCamera.transform.forward * distanceFromCamera);
        targetPosition.y = _mainCamera.transform.position.y + eyeLevelOffset;
        
        canvas.transform.position = targetPosition;
        
        if (faceCamera)
        {
            Vector3 directionToCamera = _mainCamera.transform.position - canvas.transform.position;
            canvas.transform.rotation = Quaternion.LookRotation(-directionToCamera);
        }
        
        Debug.Log($"[InstructionPanel] Positioned at: {canvas.transform.position}");
    }

    private void Update()
    {
        // Manual test trigger
        if (testClosePanel)
        {
            testClosePanel = false;
            Debug.Log("[InstructionPanel] Manual test - closing panel");
            ClosePanel();
        }
        
        if (!_isVisible) return;
        
        if (faceCamera && canvas != null && _mainCamera != null && _isPositioned)
        {
            Vector3 directionToCamera = _mainCamera.transform.position - canvas.transform.position;
            canvas.transform.rotation = Quaternion.LookRotation(-directionToCamera);
        }
        
        if (instructionHand != null && instructionHand.activeSelf)
        {
            AnimateInstructionHand();
        }
        
        if (enableHandProximityHiding)
        {
            CheckUserHandProximity();
        }
        
        // MANUAL COLLISION DETECTION - Bypass Unity's broken trigger system
        if (useManualCollisionDetection && !_hasTriggeredClose && _panelCollider != null)
        {
            ManualCollisionCheck();
        }
        
        // DEBUG: Continuous collision check
        if (showDebugCollider && _panelCollider != null)
        {
            // Check if Index finger is nearby
            Collider[] nearbyColliders = Physics.OverlapBox(
                _panelCollider.transform.TransformPoint(_panelCollider.center),
                _panelCollider.size / 2f,
                _panelCollider.transform.rotation
            );
            
            foreach (var col in nearbyColliders)
            {
                if (col.CompareTag("Index"))
                {
                    Debug.LogWarning($"[InstructionPanel] ⚠️ Index finger IS INSIDE collider bounds but OnTriggerEnter not firing!");
                    Debug.LogWarning($"[InstructionPanel] Index position: {col.transform.position}");
                    Debug.LogWarning($"[InstructionPanel] Panel position: {_panelCollider.transform.position}");
                    Debug.LogWarning($"[InstructionPanel] Index has Rigidbody: {col.GetComponent<Rigidbody>() != null}");
                    Debug.LogWarning($"[InstructionPanel] Index Rigidbody kinematic: {col.GetComponent<Rigidbody>()?.isKinematic}");
                }
            }
        }
    }
    
    private void ManualCollisionCheck()
    {
        // Manually check if Index finger is inside the collider
        Collider[] overlapping = Physics.OverlapBox(
            _panelCollider.transform.TransformPoint(_panelCollider.center),
            _panelCollider.size / 2f,
            _panelCollider.transform.rotation,
            -1, // All layers
            QueryTriggerInteraction.Collide
        );
        
        foreach (var col in overlapping)
        {
            if (col.CompareTag("Index") && !_hasTriggeredClose)
            {
                Debug.Log("[InstructionPanel] ✓✓✓ MANUAL DETECTION - Index finger detected! Closing panel...");
                _hasTriggeredClose = true;
                ClosePanel();
                break;
            }
        }
    }

    private void AnimateInstructionHand()
    {
        // Calculate tapping motion value (0 to 1 and back)
        float tapCycle = (Mathf.Sin(Time.time * handRotationSpeed) + 1f) * 0.5f;
        
        // Rotation animation (up and down tapping)
        float rotationAngle = tapCycle * handRotationAngle;
        Vector3 rotation = _handOriginalRotation;
        rotation.x += rotationAngle; // Rotate on X axis for up/down motion
        instructionHand.transform.localEulerAngles = rotation;
        
        // Position animation (forward and backward tapping motion)
        // Try multiple axes to see which one moves the hand toward the panel
        Vector3 position = _handOriginalPosition;
        
        // Option 1: Move along Z axis (most common for forward/back)
        position.z += tapCycle * handMoveDistance;
        
        // Option 2: If your hand faces a different direction, try X axis
        // position.x += tapCycle * handMoveDistance;
        
        // Option 3: Or Y axis
        // position.y += tapCycle * handMoveDistance;
        
        instructionHand.transform.localPosition = position;
    }

    private void CheckUserHandProximity()
    {
        if (instructionHand == null)
        {
            Debug.LogWarning("[InstructionPanel] Instruction hand is NULL - cannot check proximity");
            return;
        }
        
        if (_cameraRig != null)
        {
            Transform leftHand = _cameraRig.leftHandAnchor;
            Transform rightHand = _cameraRig.rightHandAnchor;
            
            bool handNearby = false;
            
            if (leftHand != null)
            {
                float distance = Vector3.Distance(leftHand.position, canvas.transform.position);
                if (distance < handHideDistance)
                {
                    handNearby = true;
                    Debug.Log($"[InstructionPanel] Left hand nearby - distance: {distance:F2}m");
                }
            }
            
            if (rightHand != null)
            {
                float distance = Vector3.Distance(rightHand.position, canvas.transform.position);
                if (distance < handHideDistance)
                {
                    handNearby = true;
                    Debug.Log($"[InstructionPanel] Right hand nearby - distance: {distance:F2}m");
                }
            }
            
            if (handNearby && instructionHand.activeSelf)
            {
                instructionHand.SetActive(false);
                Debug.Log("[InstructionPanel] User hand detected - hiding instruction hand");
            }
            else if (!handNearby && !instructionHand.activeSelf && _isVisible)
            {
                instructionHand.SetActive(true);
                Debug.Log("[InstructionPanel] User hand moved away - showing instruction hand");
            }
        }
        else
        {
            // No camera rig - always show instruction hand
            if (!instructionHand.activeSelf && _isVisible)
            {
                instructionHand.SetActive(true);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[InstructionPanel] ========== TRIGGER ENTER ==========");
        Debug.Log($"[InstructionPanel] Trigger entered by: {other.gameObject.name}");
        Debug.Log($"[InstructionPanel] Other tag: {other.tag}");
        Debug.Log($"[InstructionPanel] Other layer: {LayerMask.LayerToName(other.gameObject.layer)}");
        Debug.Log($"[InstructionPanel] Panel layer: {LayerMask.LayerToName(gameObject.layer)}");
        Debug.Log($"[InstructionPanel] Canvas layer: {(canvas != null ? LayerMask.LayerToName(canvas.gameObject.layer) : "NULL")}");
        Debug.Log($"[InstructionPanel] Collider on: {(_panelCollider != null ? _panelCollider.gameObject.name : "NULL")}");
        
        if (other.CompareTag("Index"))
        {
            Debug.Log("[InstructionPanel] ✓✓✓ Index finger detected! Closing panel...");
            ClosePanel();
        }
        else
        {
            Debug.LogWarning($"[InstructionPanel] Touch detected but tag is '{other.tag}', expected 'Index'");
        }
    }
    
    private void OnTriggerStay(Collider other)
    {
        // Add this to see if Stay fires when Enter doesn't
        if (other.CompareTag("Index"))
        {
            Debug.Log($"[InstructionPanel] TRIGGER STAY - Index finger is inside (frame: {Time.frameCount})");
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[InstructionPanel] TRIGGER EXIT by: {other.gameObject.name}");
    }

    public void ShowPanel()
    {
        if (instructionPanel != null)
        {
            instructionPanel.SetActive(true);
            _isVisible = true;
            
            if (instructionHand != null)
            {
                instructionHand.SetActive(true);
            }
            
            Debug.Log("[InstructionPanel] Panel shown");
        }
    }

    public void HidePanel()
    {
        if (instructionPanel != null)
        {
            instructionPanel.SetActive(false);
            _isVisible = false;
            _hasTriggeredClose = false; // Reset for next time
            
            if (instructionHand != null)
            {
                instructionHand.SetActive(false);
            }
            
            Debug.Log("[InstructionPanel] Panel hidden");
        }
    }

    public void ClosePanel()
    {
        Debug.Log("[InstructionPanel] ✓✓✓ CLOSING PANEL");
        
        // Play sound if available
        if (closeSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(closeSound);
            Debug.Log("[InstructionPanel] Playing close sound");
        }
        else
        {
            Debug.Log("[InstructionPanel] No close sound assigned");
        }
        
        HidePanel();
    }

    public void ClosePanelWithFade(float fadeDuration = 0.3f)
    {
        StartCoroutine(FadeOutAndClose(fadeDuration));
    }

    private IEnumerator FadeOutAndClose(float duration)
    {
        CanvasGroup canvasGroup = instructionPanel.GetComponent<CanvasGroup>();
        
        if (canvasGroup == null)
        {
            canvasGroup = instructionPanel.AddComponent<CanvasGroup>();
        }
        
        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / duration);
            yield return null;
        }
        
        canvasGroup.alpha = 0f;
        HidePanel();
        
        canvasGroup.alpha = 1f;
    }
    
    public void OnCloseButtonClicked()
    {
        ClosePanel();
    }
    
    private void OnDrawGizmos()
    {
        if (!showDebugCollider) return;
        
        // Draw collider even if not visible, for debugging
        if (_panelCollider != null)
        {
            // Bright cyan wireframe
            Gizmos.color = Color.cyan;
            Gizmos.matrix = _panelCollider.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(_panelCollider.center, _panelCollider.size);
            
            // Semi-transparent fill
            Gizmos.color = new Color(0, 1, 1, 0.3f);
            Gizmos.DrawCube(_panelCollider.center, _panelCollider.size);
            
            // Draw center point
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_panelCollider.transform.TransformPoint(_panelCollider.center), 0.02f);
        }
        
        // Also draw gizmo even in edit mode
        if (!Application.isPlaying && instructionPanel != null)
        {
            BoxCollider tempCollider = instructionPanel.GetComponent<BoxCollider>();
            if (tempCollider != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.matrix = tempCollider.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(colliderCenter, colliderSize);
            }
        }
    }
}