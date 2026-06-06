using UnityEngine;

/// <summary>
/// Drives a vehicle's lights and their matching emissive materials. Receives
/// driving state from an external controller via <see cref="SetDrivingState"/>
/// and exposes toggles for head lights and turn signals. Each material slot
/// is optional and is resolved at start-up to a per-renderer instance so
/// emission changes never leak into the shared project asset.
/// </summary>
public class CarLights : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Lights
    // ---------------------------------------------------------------------

    [Header("Stop / brake lights")]
    [SerializeField] private Light stopLightLeft;
    [SerializeField] private Light stopLightRight;

    [Header("Head lights")]
    [SerializeField] private Light headLightLeft;
    [SerializeField] private Light headLightRight;

    [Header("Tail / reverse lights")]
    [SerializeField] private Light tailLightLeft;
    [SerializeField] private Light tailLightRight;

    [Header("Turn signal lights")]
    [SerializeField] private Light turnLightFrontLeft;
    [SerializeField] private Light turnLightRearLeft;
    [SerializeField] private Light turnLightFrontRight;
    [SerializeField] private Light turnLightRearRight;

    // ---------------------------------------------------------------------
    // Optional emissive materials
    //
    // Each slot accepts a Material asset used by one of the vehicle's child
    // Renderers. At Start() the script locates that Renderer, replaces the
    // matching slot with a per-renderer instance, and drives its emission at
    // runtime. The source asset is left unmodified, so multiple vehicles
    // sharing the same asset stay independent.
    //
    // Leave a slot empty if a particular lamp has no emissive mesh.
    // ---------------------------------------------------------------------

    [Header("Emissive materials (optional)")]
    [SerializeField] private Material stopLightLeftMat;
    [SerializeField] private Material stopLightRightMat;
    [SerializeField] private Material headLightLeftMat;
    [SerializeField] private Material headLightRightMat;
    [SerializeField] private Material tailLightLeftMat;
    [SerializeField] private Material tailLightRightMat;
    [SerializeField] private Material turnLightFrontLeftMat;
    [SerializeField] private Material turnLightRearLeftMat;
    [SerializeField] private Material turnLightFrontRightMat;
    [SerializeField] private Material turnLightRearRightMat;

    // ---------------------------------------------------------------------
    // Emission appearance
    // ---------------------------------------------------------------------

    [Header("Emission colors")]
    [SerializeField] private Color stopLightColor = Color.red;
    [SerializeField] private Color headLightColor = Color.white;
    [SerializeField] private Color tailLightColor = Color.white;
    [SerializeField] private Color turnLightColor = new Color(1f, 0.55f, 0f);

    [Header("Light intensities")]
    [SerializeField] private float stopBrakeIntensity = 5f;
    [SerializeField] private float stopRunningIntensity = 2.5f;
    [SerializeField] private float headLightIntensity = 5f;
    [SerializeField] private float tailLightIntensity = 3f;
    [SerializeField] private float turnLightIntensity = 3f;

    [Tooltip("Duration of one half-cycle of the turn-signal blink, in seconds.")]
    [SerializeField] private float signalBlinkInterval = 0.4f;

    // ---------------------------------------------------------------------
    // Runtime state
    // ---------------------------------------------------------------------

    // Driving signals pushed in by the controller.
    private bool isBraking;
    private bool isMotorBraking;
    private bool isReversing;

    // Toggled by the player through the public API.
    private bool headLightsOn;
    private bool signalLeftOn;
    private bool signalRightOn;

    // Blink bookkeeping for the turn signals.
    private float signalBlinkTimer;
    private bool signalBlinkState;

    // Per-instance copies of the assigned material assets, resolved at Start().
    private Material stopLightLeftLocal;
    private Material stopLightRightLocal;
    private Material headLightLeftLocal;
    private Material headLightRightLocal;
    private Material tailLightLeftLocal;
    private Material tailLightRightLocal;
    private Material turnLightFrontLeftLocal;
    private Material turnLightRearLeftLocal;
    private Material turnLightFrontRightLocal;
    private Material turnLightRearRightLocal;

    // ---------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------

    /// <summary>True while the head lights toggle is on.</summary>
    public bool HeadLightsOn => headLightsOn;

    /// <summary>True while the left turn signal is active.</summary>
    public bool SignalLeftOn => signalLeftOn;

    /// <summary>True while the right turn signal is active.</summary>
    public bool SignalRightOn => signalRightOn;

    /// <summary>
    /// Reports the current driving state to the lights. Expected to be called
    /// once per frame by the driving controller.
    /// </summary>
    public void SetDrivingState(bool braking, bool motorBraking, bool reversing)
    {
        isBraking = braking;
        isMotorBraking = motorBraking;
        isReversing = reversing;
    }

    /// <summary>Toggles the head lights on or off.</summary>
    public void ToggleHeadLights()
    {
        headLightsOn = !headLightsOn;
    }

    /// <summary>Toggles the left turn signal; cancels the right if it was active.</summary>
    public void ToggleSignalLeft()
    {
        signalLeftOn = !signalLeftOn;
        if (signalLeftOn) signalRightOn = false;
        signalBlinkTimer = 0f;
        signalBlinkState = true;
    }

    /// <summary>Toggles the right turn signal; cancels the left if it was active.</summary>
    public void ToggleSignalRight()
    {
        signalRightOn = !signalRightOn;
        if (signalRightOn) signalLeftOn = false;
        signalBlinkTimer = 0f;
        signalBlinkState = true;
    }

    // ---------------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------------

    private void Start()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool anyUnresolved = false;
        stopLightLeftLocal       = ResolveLocalMaterial(stopLightLeftMat,       renderers, ref anyUnresolved);
        stopLightRightLocal      = ResolveLocalMaterial(stopLightRightMat,      renderers, ref anyUnresolved);
        headLightLeftLocal       = ResolveLocalMaterial(headLightLeftMat,       renderers, ref anyUnresolved);
        headLightRightLocal      = ResolveLocalMaterial(headLightRightMat,      renderers, ref anyUnresolved);
        tailLightLeftLocal       = ResolveLocalMaterial(tailLightLeftMat,       renderers, ref anyUnresolved);
        tailLightRightLocal      = ResolveLocalMaterial(tailLightRightMat,      renderers, ref anyUnresolved);
        turnLightFrontLeftLocal  = ResolveLocalMaterial(turnLightFrontLeftMat,  renderers, ref anyUnresolved);
        turnLightRearLeftLocal   = ResolveLocalMaterial(turnLightRearLeftMat,   renderers, ref anyUnresolved);
        turnLightFrontRightLocal = ResolveLocalMaterial(turnLightFrontRightMat, renderers, ref anyUnresolved);
        turnLightRearRightLocal  = ResolveLocalMaterial(turnLightRearRightMat,  renderers, ref anyUnresolved);

        if (anyUnresolved) DumpRendererMaterials(renderers);
    }

    private void Update()
    {
        ApplyLightState();
    }

    // ---------------------------------------------------------------------
    // Internal update
    // ---------------------------------------------------------------------

    /// <summary>
    /// Applies the current driving signals and toggle state to every
    /// <see cref="Light"/> and emissive material. Also ticks the turn-signal
    /// blink timer.
    /// </summary>
    private void ApplyLightState()
    {
        // Stop lights: bright while braking; dim "running light" mode when
        // head lights are on; otherwise off.
        bool brakeActive = isBraking || isMotorBraking;
        float stopIntensity = brakeActive ? stopBrakeIntensity
                            : headLightsOn ? stopRunningIntensity
                            : 0f;
        bool stopOn = stopIntensity > 0f;
        SetLightAndEmission(stopLightLeft,  stopLightLeftLocal,  stopOn, stopIntensity, stopLightColor);
        SetLightAndEmission(stopLightRight, stopLightRightLocal, stopOn, stopIntensity, stopLightColor);

        // Head lights follow the head-light toggle.
        SetLightAndEmission(headLightLeft,  headLightLeftLocal,  headLightsOn, headLightIntensity, headLightColor);
        SetLightAndEmission(headLightRight, headLightRightLocal, headLightsOn, headLightIntensity, headLightColor);

        // Reverse lights activate while reverse drive is engaged.
        SetLightAndEmission(tailLightLeft,  tailLightLeftLocal,  isReversing, tailLightIntensity, tailLightColor);
        SetLightAndEmission(tailLightRight, tailLightRightLocal, isReversing, tailLightIntensity, tailLightColor);

        // Turn signals blink while either side is active.
        if (signalLeftOn || signalRightOn)
        {
            signalBlinkTimer += Time.deltaTime;
            if (signalBlinkTimer >= signalBlinkInterval)
            {
                signalBlinkTimer = 0f;
                signalBlinkState = !signalBlinkState;
            }
        }
        else
        {
            signalBlinkState = false;
        }

        bool leftBlink  = signalLeftOn  && signalBlinkState;
        bool rightBlink = signalRightOn && signalBlinkState;
        SetLightAndEmission(turnLightFrontLeft,  turnLightFrontLeftLocal,  leftBlink,  turnLightIntensity, turnLightColor);
        SetLightAndEmission(turnLightRearLeft,   turnLightRearLeftLocal,   leftBlink,  turnLightIntensity, turnLightColor);
        SetLightAndEmission(turnLightFrontRight, turnLightFrontRightLocal, rightBlink, turnLightIntensity, turnLightColor);
        SetLightAndEmission(turnLightRearRight,  turnLightRearRightLocal,  rightBlink, turnLightIntensity, turnLightColor);
    }

    // ---------------------------------------------------------------------
    // Light / material helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Enables or disables a <see cref="Light"/> and updates the emission
    /// color of an accompanying material instance. <paramref name="mat"/>
    /// is expected to be a per-renderer instance (see
    /// <see cref="ResolveLocalMaterial"/>) so changes do not leak into the
    /// shared project asset.
    /// </summary>
    private void SetLightAndEmission(Light light, Material mat, bool on, float intensity, Color color)
    {
        if (light != null)
        {
            light.enabled = on;
            if (on) light.intensity = intensity;
        }

        if (mat != null)
        {
            if (on)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", color * intensity);
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    /// <summary>
    /// Locates a child <see cref="Renderer"/> that uses <paramref name="source"/>,
    /// replaces that slot with a per-renderer instance, and returns the
    /// instance so it can be modified without affecting the shared asset.
    /// Falls back to a name-based match to handle duplicates created during
    /// FBX import. Sets <paramref name="anyUnresolved"/> to true when no
    /// matching renderer is found.
    /// </summary>
    private Material ResolveLocalMaterial(Material source, Renderer[] renderers, ref bool anyUnresolved)
    {
        if (source == null) return null;

        // Pass 1: exact asset-reference match.
        foreach (Renderer r in renderers)
        {
            Material[] shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == source)
                {
                    Material[] instances = r.materials;
                    r.materials = instances;
                    return instances[i];
                }
            }
        }

        // Pass 2: name match, used when FBX import creates a duplicate object
        // with the same name as the .mat asset selected in the Inspector.
        foreach (Renderer r in renderers)
        {
            Material[] shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] != null && shared[i].name == source.name)
                {
                    Material[] instances = r.materials;
                    r.materials = instances;
                    Debug.Log($"[CarLights] Matched '{source.name}' by name on '{r.gameObject.name}' slot {i} (asset reference did not match; likely an FBX import duplicate).", this);
                    return instances[i];
                }
            }
        }

        Debug.LogWarning($"[CarLights] Material '{source.name}' is not used by any child Renderer of '{name}'.", this);
        anyUnresolved = true;
        return null;
    }

    /// <summary>
    /// Logs every material in use by the supplied renderers, grouped by
    /// renderer and slot index. Called once when at least one Inspector
    /// material slot fails to resolve, to help diagnose the mismatch.
    /// </summary>
    private void DumpRendererMaterials(Renderer[] renderers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CarLights] Materials in use by '{name}' child Renderers:");
        foreach (Renderer r in renderers)
        {
            Material[] shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                string matName = shared[i] != null ? shared[i].name : "(null)";
                sb.AppendLine($"  - {r.gameObject.name} [slot {i}] : {matName}");
            }
        }
        Debug.Log(sb.ToString(), this);
    }
}
