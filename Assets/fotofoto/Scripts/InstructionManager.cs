using UnityEngine;

public class InstructionManager : MonoBehaviour
{
    // =========================================================
    //  Singleton Instance
    // =========================================================
    public static InstructionManager Instance { get; private set; }

    // =========================================================
    //  Instruction Panel Enum
    // =========================================================
    public enum InstructionPanel
    {
        None = -1,
        Watermark = 0,
        SoloFoto = 1,
        CoFoto = 2,
        RemixFoto = 3,
        LoadFoto = 4,
        SaveFoto = 5,
        PrintFoto = 6
    }

    // =========================================================
    //  Current State
    // =========================================================
    [Header("Current Instruction Panel")]
    public InstructionPanel currentPanel = InstructionPanel.None;

    [Header("Panel GameObjects (assign in Inspector)")]
    public GameObject watermarkPanel;
    public GameObject soloFotoPanel;
    public GameObject coFotoPanel;
    public GameObject remixFotoPanel;
    public GameObject loadFotoPanel;
    public GameObject saveFotoPanel;
    public GameObject printFotoPanel;

    [Header("Watermark Auto-Transition")]
    public float watermarkDuration = 3f; // Show watermark for 3 seconds
    private float watermarkStartTime = 0f;
    private bool watermarkTimerActive = false;

    // =========================================================
    //  Events (optional - other scripts can subscribe)
    // =========================================================
    public delegate void PanelChangedDelegate(InstructionPanel oldPanel, InstructionPanel newPanel);
    public event PanelChangedDelegate OnPanelChanged;

    // =========================================================
    //  Awake - Setup Singleton
    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InstructionManager] Duplicate instance detected, destroying this one");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("[InstructionManager] ✅ Singleton initialized");
    }

    // =========================================================
    //  Public Methods to Change Panel
    // =========================================================

    /// <summary>
    /// Set the current instruction panel and activate/deactivate GameObjects
    /// </summary>
    public void SetPanel(InstructionPanel panel)
    {
        if (currentPanel != panel)
        {
            InstructionPanel oldPanel = currentPanel;
            currentPanel = panel;

            Debug.Log($"[InstructionManager] Panel changed: {oldPanel} -> {currentPanel}");

            // Activate/deactivate panel GameObjects
            UpdatePanelVisibility();

            // Start watermark timer if switching to watermark
            if (currentPanel == InstructionPanel.Watermark)
            {
                watermarkStartTime = Time.time;
                watermarkTimerActive = true;
                Debug.Log($"[InstructionManager] Watermark timer started ({watermarkDuration}s)");
            }
            else
            {
                watermarkTimerActive = false;
            }

            // Trigger event
            OnPanelChanged?.Invoke(oldPanel, currentPanel);
        }
    }

    /// <summary>
    /// Update panel visibility based on currentPanel
    /// </summary>
    private void UpdatePanelVisibility()
    {
        // Deactivate all panels first
        if (watermarkPanel) watermarkPanel.SetActive(false);
        if (soloFotoPanel) soloFotoPanel.SetActive(false);
        if (coFotoPanel) coFotoPanel.SetActive(false);
        if (remixFotoPanel) remixFotoPanel.SetActive(false);
        if (loadFotoPanel) loadFotoPanel.SetActive(false);
        if (saveFotoPanel) saveFotoPanel.SetActive(false);
        if (printFotoPanel) printFotoPanel.SetActive(false);

        // Activate only the current panel
        switch (currentPanel)
        {
            case InstructionPanel.Watermark:
                if (watermarkPanel) watermarkPanel.SetActive(true);
                break;
            case InstructionPanel.SoloFoto:
                if (soloFotoPanel) soloFotoPanel.SetActive(true);
                break;
            case InstructionPanel.CoFoto:
                if (coFotoPanel) coFotoPanel.SetActive(true);
                break;
            case InstructionPanel.RemixFoto:
                if (remixFotoPanel) remixFotoPanel.SetActive(true);
                break;
            case InstructionPanel.PrintFoto:
                if (printFotoPanel) printFotoPanel.SetActive(true);
                break;
            case InstructionPanel.LoadFoto:
                if (loadFotoPanel) loadFotoPanel.SetActive(true);
                break;
            case InstructionPanel.SaveFoto:
                if (saveFotoPanel) saveFotoPanel.SetActive(true);
                break;
            case InstructionPanel.None:
                // All panels already deactivated
                break;
        }

        Debug.Log($"[InstructionManager] Updated panel visibility for: {currentPanel}");
    }

    /// <summary>
    /// Set panel by index (0-6)
    /// </summary>
    public void SetPanelByIndex(int index)
    {
        if (index >= 0 && index <= 6)
        {
            SetPanel((InstructionPanel)index);
        }
        else
        {
            Debug.LogWarning($"[InstructionManager] Invalid panel index: {index}");
        }
    }

    /// <summary>
    /// Auto-detect which panel is currently active based on GameObject visibility
    /// </summary>
    public void AutoDetectActivePanel()
    {
        if (watermarkPanel != null && watermarkPanel.activeSelf)
        {
            currentPanel = InstructionPanel.Watermark;
            Debug.Log("[InstructionManager] Auto-detected: Watermark");
        }
        else if (soloFotoPanel != null && soloFotoPanel.activeSelf)
        {
            currentPanel = InstructionPanel.SoloFoto;
            Debug.Log("[InstructionManager] Auto-detected: SoloFoto");
        }
        else if (coFotoPanel != null && coFotoPanel.activeSelf)
        {
            currentPanel = InstructionPanel.CoFoto;
            Debug.Log("[InstructionManager] Auto-detected: CoFoto");
        }
        else if (remixFotoPanel != null && remixFotoPanel.activeSelf)
        {
            currentPanel = InstructionPanel.RemixFoto;
            Debug.Log("[InstructionManager] Auto-detected: RemixFoto");
        }
        else if (printFotoPanel != null && printFotoPanel.activeSelf)
        {
            currentPanel = InstructionPanel.PrintFoto;
            Debug.Log("[InstructionManager] Auto-detected: PrintFoto");
        }
        else if (loadFotoPanel != null && loadFotoPanel.activeSelf)
        {
            currentPanel = InstructionPanel.LoadFoto;
            Debug.Log("[InstructionManager] Auto-detected: LoadFoto");
        }
        else if (saveFotoPanel != null && saveFotoPanel.activeSelf)
        {
            currentPanel = InstructionPanel.SaveFoto;
            Debug.Log("[InstructionManager] Auto-detected: SaveFoto");
        }
        else
        {
            currentPanel = InstructionPanel.None;
            Debug.Log("[InstructionManager] Auto-detected: None");
        }
    }

    // =========================================================
    //  Helper Methods
    // =========================================================

    /// <summary>
    /// Check if currently in a specific panel
    /// </summary>
    public bool IsInPanel(InstructionPanel panel)
    {
        Debug.Log($"[InstructionManager] Checking if in panel: {panel} (Current: {currentPanel})");
        return currentPanel == panel;
    }

    /// <summary>
    /// Check if in any instruction panel (not None)
    /// </summary>
    public bool IsInAnyPanel()
    {
        return currentPanel != InstructionPanel.None;
    }

    /// <summary>
    /// Check if capture is allowed in the current panel
    /// Capture is disabled for CoFoto, RemixFoto, and PrintFoto
    /// </summary>
    public bool IsCaptureAllowed()
    {
        Debug.Log($"[InstructionManager] Checking capture permission in panel: {currentPanel}");
        return currentPanel != InstructionPanel.CoFoto &&
               currentPanel != InstructionPanel.RemixFoto &&
               currentPanel != InstructionPanel.PrintFoto;
    }

    /// <summary>
    /// Get the current panel name as string
    /// </summary>
    public string GetCurrentPanelName()
    {
        switch (currentPanel)
        {
            case InstructionPanel.None: return "None";
            case InstructionPanel.Watermark: return "Watermark";
            case InstructionPanel.SoloFoto: return "Solo Foto";
            case InstructionPanel.CoFoto: return "Co Foto";
            case InstructionPanel.RemixFoto: return "Remix Foto";
            case InstructionPanel.PrintFoto: return "Print Foto";
            default: return currentPanel.ToString();
        }
    }

    // =========================================================
    //  Update - Handle Watermark Timer
    // =========================================================
    private void Update()
    {
        Debug.Log("[IM] Current Panel = " + currentPanel);

        // Watermark auto-transition timer
        if (watermarkTimerActive && currentPanel == InstructionPanel.Watermark)
        {
            float elapsed = Time.time - watermarkStartTime;

            if (elapsed >= watermarkDuration)
            {
                Debug.Log($"[InstructionManager] Watermark timer expired ({elapsed:F1}s), switching to SoloFoto");
                SetPanel(InstructionPanel.SoloFoto);
                watermarkTimerActive = false;
            }
        }

#if UNITY_EDITOR
        // Debug hotkeys for testing in editor
        if (Input.GetKeyDown(KeyCode.Alpha0)) SetPanel(InstructionPanel.Watermark);
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetPanel(InstructionPanel.SoloFoto);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetPanel(InstructionPanel.CoFoto);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetPanel(InstructionPanel.RemixFoto);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SetPanel(InstructionPanel.PrintFoto);
        if (Input.GetKeyDown(KeyCode.Alpha9)) SetPanel(InstructionPanel.None);
#endif
    }
}
