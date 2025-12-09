using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the brightness of OVRPassthroughLayer (Reconstructed passthrough)
/// Attach this to a GameObject and assign the slider and passthrough layer in Inspector
/// </summary>
public class PassthroughBrightnessController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Slider brightnessSlider;

    [Header("Brightness Settings")]
    [SerializeField] [Range(0f, 1f)] private float minBrightness = 0f;
    [SerializeField] [Range(0f, 1f)] private float maxBrightness = 1f;
    [SerializeField] private float defaultBrightness = 1f;

    private void Start()
    {
        // Find passthrough layer if not assigned
        if (passthroughLayer == null)
        {
            // Find ALL passthrough layers
            OVRPassthroughLayer[] allLayers = FindObjectsOfType<OVRPassthroughLayer>();

            if (allLayers.Length == 0)
            {
                Debug.LogError("[PassthroughBrightness] No OVRPassthroughLayer found in scene!");
                enabled = false;
                return;
            }

            // Look for the RECONSTRUCTED one specifically
            foreach (var layer in allLayers)
            {
                if (layer.projectionSurfaceType == OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed)
                {
                    passthroughLayer = layer;
                    Debug.Log($"[PassthroughBrightness] Found Reconstructed passthrough layer on: {layer.gameObject.name}");
                    break;
                }
            }

            // Fallback: if no Reconstructed found, warn user
            if (passthroughLayer == null)
            {
                Debug.LogWarning("[PassthroughBrightness] No Reconstructed passthrough layer found! Using first available layer.");
                passthroughLayer = allLayers[0];
            }
        }
        else
        {
            // Verify the assigned layer is Reconstructed
            if (passthroughLayer.projectionSurfaceType != OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed)
            {
                Debug.LogWarning($"[PassthroughBrightness] Assigned layer is {passthroughLayer.projectionSurfaceType}, not Reconstructed! Brightness control may not work as expected.");
            }
        }

        // Setup slider
        if (brightnessSlider != null)
        {
            // Set slider range
            brightnessSlider.minValue = 0f;
            brightnessSlider.maxValue = 1f;

            // Set default value (normalized 0-1)
            float normalizedDefault = Mathf.InverseLerp(minBrightness, maxBrightness, defaultBrightness);
            brightnessSlider.value = normalizedDefault;

            // Add listener
            brightnessSlider.onValueChanged.AddListener(OnBrightnessSliderChanged);

            // Apply initial brightness
            OnBrightnessSliderChanged(brightnessSlider.value);

            Debug.Log($"[PassthroughBrightness] Initialized - Range: {minBrightness} to {maxBrightness}, Default: {defaultBrightness}");
        }
        else
        {
            Debug.LogWarning("[PassthroughBrightness] No slider assigned - using default brightness");
            SetBrightness(defaultBrightness);
        }
    }

    private void OnDestroy()
    {
        // Clean up listener
        if (brightnessSlider != null)
        {
            brightnessSlider.onValueChanged.RemoveListener(OnBrightnessSliderChanged);
        }
    }

    /// <summary>
    /// Called when slider value changes (0-1 range)
    /// </summary>
    private void OnBrightnessSliderChanged(float normalizedValue)
    {
        // Convert slider value (0-1) to brightness range
        float brightness = Mathf.Lerp(minBrightness, maxBrightness, normalizedValue);
        SetBrightness(brightness);

        Debug.Log($"[PassthroughBrightness] Slider changed: {normalizedValue:F2} -> Brightness: {brightness:F2}");
    }

    /// <summary>
    /// Set passthrough brightness (0 = completely dark, 1 = normal/full brightness)
    /// </summary>
    public void SetBrightness(float brightness)
    {
        if (passthroughLayer == null)
        {
            Debug.LogError("[PassthroughBrightness] Cannot set brightness - passthroughLayer is null!");
            return;
        }

        // Clamp brightness to valid range (0-1 for opacity)
        brightness = Mathf.Clamp01(brightness);

        // For Reconstructed passthrough, we control brightness via textureOpacity
        // textureOpacity controls how much of the passthrough is visible
        // 0 = completely transparent (black)
        // 1 = fully visible (normal brightness)
        passthroughLayer.textureOpacity = brightness;

        Debug.LogError($"[PassthroughBrightness] ★★★ Brightness set to {brightness:F2} (textureOpacity) ★★★");
    }

    /// <summary>
    /// Public method to set brightness directly (for other scripts)
    /// </summary>
    public void SetBrightnessValue(float brightness)
    {
        SetBrightness(brightness);

        // Update slider if it exists
        if (brightnessSlider != null)
        {
            float normalizedValue = Mathf.InverseLerp(minBrightness, maxBrightness, brightness);
            brightnessSlider.value = normalizedValue;
        }
    }

    /// <summary>
    /// Reset to default brightness
    /// </summary>
    public void ResetToDefault()
    {
        SetBrightnessValue(defaultBrightness);
        Debug.Log($"[PassthroughBrightness] Reset to default: {defaultBrightness}");
    }
}
