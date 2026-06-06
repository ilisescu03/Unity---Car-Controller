using UnityEngine;

/// <summary>
/// Drives a vehicle's audio. Three <see cref="AudioSource"/> slots are wired
/// up in the Inspector: <c>Engine</c> is a single loop that covers idle and
/// driving in either direction, with its pitch swept by a simulated rpm that
/// climbs under throttle and drops on each gear shift. <c>Signal</c> is an
/// independent loop that plays whenever a turn signal is active; <c>Hit</c>
/// is a one-shot played every time the car collides with another body.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarAudio : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Audio sources
    // ---------------------------------------------------------------------

    [Header("Engine")]
    [Tooltip("Looping engine sound. Always playing once the script starts; its " +
             "pitch sweeps with the simulated rpm in either direction of travel.")]
    [SerializeField] private AudioSource engineSource;

    [Header("Independent sources")]
    [Tooltip("Looping turn-signal tick. Plays while either turn signal is active.")]
    [SerializeField] private AudioSource signalSource;

    [Tooltip("One-shot collision sound. Re-triggered on every new contact.")]
    [SerializeField] private AudioSource hitSource;

    // ---------------------------------------------------------------------
    // Tuning
    // ---------------------------------------------------------------------

    [Header("Engine pitch")]
    [Tooltip("Pitch of the engine loop at zero rpm (idle).")]
    [SerializeField] private float minThrottlePitch = 0.8f;

    [Tooltip("Pitch of the engine loop at redline rpm.")]
    [SerializeField] private float maxThrottlePitch = 1.4f;

    [Header("Gear simulation")]
    [Tooltip("How fast the simulated rpm climbs per second under full throttle. " +
             "Larger values mean each gear is shorter, so shifts happen more often.")]
    [SerializeField] private float rpmRiseRate = 0.55f;

    [Tooltip("How fast the simulated rpm decays per second when the throttle is released.")]
    [SerializeField] private float rpmDecayRate = 0.9f;

    [Tooltip("Simulated rpm immediately after an upshift, on a 0-1 scale. " +
             "Lower values produce a deeper pitch drop on each shift.")]
    [Range(0f, 1f)]
    [SerializeField] private float postShiftRpm = 0.5f;

    [Tooltip("Number of simulated forward gears. After the top gear, rpm holds at redline.")]
    [Min(1)]
    [SerializeField] private int gearCount = 5;

    [Header("Collision")]
    [Tooltip("Minimum car speed (m/s) required to play the hit sound. " +
             "Light scrapes and resting contacts stay below this; real crashes go well above.")]
    [SerializeField] private float hitSpeedThreshold = 2f;

    [Tooltip("Car speed (m/s) at which the hit sound reaches its maximum volume and pitch. " +
             "Speeds above this are clamped.")]
    [SerializeField] private float hitReferenceSpeed = 20f;

    [Tooltip("Hit volume at the threshold speed (quietest impact).")]
    [Range(0f, 1f)]
    [SerializeField] private float hitMinVolume = 0.35f;

    [Tooltip("Hit volume at or above the reference speed (loudest impact).")]
    [Range(0f, 1f)]
    [SerializeField] private float hitMaxVolume = 1f;

    [Tooltip("Hit pitch at the threshold speed. Lower than 1 makes slow bumps sound dull and heavy.")]
    [SerializeField] private float hitMinPitch = 0.85f;

    [Tooltip("Hit pitch at or above the reference speed. Higher than 1 makes fast crashes sound sharp.")]
    [SerializeField] private float hitMaxPitch = 1.2f;

    [Tooltip("Random plus/minus jitter applied to hit volume on every collision so repeated " +
             "impacts at the same speed don't sound mechanically identical.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float hitVolumeJitter = 0.1f;

    [Tooltip("Random plus/minus jitter applied to hit pitch on every collision so repeated " +
             "impacts at the same speed don't sound mechanically identical.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float hitPitchJitter = 0.08f;

    [Header("Lights")]
    [Tooltip("Optional lights component used to detect the turn-signal state. " +
             "If left empty, the script tries GetComponent at start-up.")]
    [SerializeField] private CarLights lights;

    // ---------------------------------------------------------------------
    // Cached references and runtime state
    // ---------------------------------------------------------------------

    private CarController controller;

    // Simulated engine rpm on a 0-1 scale (0 = idle, 1 = redline).
    private float engineRpm;

    // Current simulated gear, 1-based. Increments on upshift, resets to 1 at idle.
    private int currentGear = 1;

    // ---------------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------------

    private void Start()
    {
        controller = GetComponent<CarController>();
        if (lights == null) lights = GetComponent<CarLights>();

        ConfigureLoop(engineSource,  true);
        ConfigureLoop(signalSource,  true);
        ConfigureLoop(hitSource,     false);

        if (engineSource != null && !engineSource.isPlaying) engineSource.Play();
    }

    private void Update()
    {
        UpdateEnginePitch();
        UpdateSignalLoop();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hitSource == null || hitSource.clip == null) return;

        float speed = controller != null ? controller.CurrentSpeed : 0f;
        if (speed < hitSpeedThreshold) return;

        // Map current speed into a 0-1 strength between threshold and reference,
        // then drive volume and pitch from it. A small random jitter on top
        // keeps back-to-back impacts from sounding like the exact same sample.
        float span = Mathf.Max(0.0001f, hitReferenceSpeed - hitSpeedThreshold);
        float strength = Mathf.Clamp01((speed - hitSpeedThreshold) / span);

        float volume = Mathf.Lerp(hitMinVolume, hitMaxVolume, strength)
                       + Random.Range(-hitVolumeJitter, hitVolumeJitter);
        float pitch  = Mathf.Lerp(hitMinPitch,  hitMaxPitch,  strength)
                       + Random.Range(-hitPitchJitter,  hitPitchJitter);

        hitSource.pitch = pitch;
        hitSource.PlayOneShot(hitSource.clip, Mathf.Clamp01(volume));
    }

    // ---------------------------------------------------------------------
    // Internal update
    // ---------------------------------------------------------------------

    /// <summary>
    /// Advances the simulated rpm and writes the resulting pitch to the
    /// engine source. The same logic runs for forward and reverse throttle —
    /// the absolute value is used so accelerating in either direction revs
    /// the engine and triggers the same gear shifts.
    /// </summary>
    private void UpdateEnginePitch()
    {
        float throttle = controller != null ? Mathf.Abs(controller.Throttle) : 0f;
        AdvanceEngineRpm(throttle);

        if (engineSource != null)
        {
            engineSource.pitch = Mathf.Lerp(minThrottlePitch, maxThrottlePitch, engineRpm);
        }
    }

    /// <summary>
    /// Advances <see cref="engineRpm"/> for the current frame. Throttle held
    /// ramps rpm up at <see cref="rpmRiseRate"/>; reaching redline upshifts
    /// (rpm snaps to <see cref="postShiftRpm"/>, gear++) until
    /// <see cref="gearCount"/> is exhausted, after which rpm holds at 1.
    /// Releasing the throttle decays rpm toward idle and resets the gear.
    /// </summary>
    private void AdvanceEngineRpm(float throttle)
    {
        if (throttle > 0.01f)
        {
            engineRpm += rpmRiseRate * throttle * Time.deltaTime;
            if (engineRpm >= 1f)
            {
                if (currentGear < gearCount)
                {
                    engineRpm = postShiftRpm;
                    currentGear++;
                }
                else
                {
                    engineRpm = 1f;
                }
            }
        }
        else
        {
            engineRpm = Mathf.MoveTowards(engineRpm, 0f, rpmDecayRate * Time.deltaTime);
            if (engineRpm <= 0.001f) currentGear = 1;
        }
    }

    /// <summary>
    /// Starts or stops the turn-signal loop based on the lights component's
    /// current signal state. Runs independently of the engine sources.
    /// </summary>
    private void UpdateSignalLoop()
    {
        bool signaling = lights != null && (lights.SignalLeftOn || lights.SignalRightOn);
        SetPlaying(signalSource, signaling);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Starts or stops <paramref name="source"/> idempotently, leaving an
    /// already-playing source alone so its loop is not restarted every frame.
    /// </summary>
    private void SetPlaying(AudioSource source, bool shouldPlay)
    {
        if (source == null) return;
        if (shouldPlay)
        {
            if (!source.isPlaying) source.Play();
        }
        else
        {
            if (source.isPlaying) source.Stop();
        }
    }

    private void ConfigureLoop(AudioSource source, bool loop)
    {
        if (source != null) source.loop = loop;
    }
}
