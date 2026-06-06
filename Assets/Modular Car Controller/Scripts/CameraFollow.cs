using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Car follow camera. Holds a yaw/pitch orbit around the target at a
/// configurable distance. Mouse movement swings the camera around the car
/// directly (no button to hold), the scroll wheel zooms in and out, and once
/// the player stops moving the mouse the camera eases back behind the car
/// while it drives forward — so the chase view re-forms on its own without
/// snapping.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The vehicle (or any Transform) the camera should follow.")]
    [SerializeField] private Transform target;

    [Header("Orbit distance")]
    [Tooltip("Current distance from the target along the orbit. Adjusted at runtime by the scroll wheel.")]
    [SerializeField] private float distance = 6f;
    [Tooltip("Closest the camera can zoom in.")]
    [SerializeField] private float minDistance = 3f;
    [Tooltip("Farthest the camera can zoom out.")]
    [SerializeField] private float maxDistance = 12f;
    [Tooltip("How many world units the distance changes per scroll notch.")]
    [SerializeField] private float zoomSensitivity = 0.01f;

    [Header("Orbit angles")]
    [Tooltip("Default pitch (degrees) used as the resting angle behind the car. Positive looks down.")]
    [SerializeField] private float defaultPitch = 15f;
    [Tooltip("Steepest the camera can tilt up toward the sky.")]
    [SerializeField] private float minPitch = -10f;
    [Tooltip("Steepest the camera can tilt down toward the ground.")]
    [SerializeField] private float maxPitch = 70f;

    [Header("Orbit input")]
    [Tooltip("Degrees of orbit per pixel of mouse movement. Lower = slower, more cinematic.")]
    [SerializeField] private float orbitSensitivity = 0.2f;
    [Tooltip("If true, vertical mouse movement is inverted.")]
    [SerializeField] private bool invertY = false;

    [Header("Auto-recenter")]
    [Tooltip("Seconds of no mouse movement before the camera starts easing back behind the car.")]
    [SerializeField] private float recenterDelay = 1.5f;
    [Tooltip("Maximum yaw recenter speed in degrees per second. Scales with forward speed.")]
    [SerializeField] private float recenterYawSpeed = 120f;
    [Tooltip("Pitch recenter speed in degrees per second.")]
    [SerializeField] private float recenterPitchSpeed = 40f;
    [Tooltip("Forward speed (m/s) at which the camera recenters at full speed. " +
             "Slower than this and recentering is proportionally weaker; at zero it doesn't recenter at all.")]
    [SerializeField] private float recenterFullSpeed = 12f;

    [Header("Smoothing")]
    [Tooltip("How quickly the camera catches up to its desired position. Higher = snappier.")]
    [SerializeField] private float positionLerpSpeed = 10f;
    [Tooltip("How quickly the camera rotates to face the target. Higher = snappier.")]
    [SerializeField] private float rotationLerpSpeed = 12f;

    [Header("Look")]
    [Tooltip("Extra height above the target's pivot to aim at (look slightly above the car).")]
    [SerializeField] private float lookAtHeight = 1f;

    [Header("Collision")]
    [Tooltip("Layers the camera should treat as solid. Anything in the target's hierarchy is ignored automatically.")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [Tooltip("Radius of the sphere swept from the look anchor toward the camera. " +
             "Larger values pull the camera in earlier and avoid the near plane poking through thin walls.")]
    [SerializeField] private float collisionRadius = 0.25f;
    [Tooltip("Extra gap kept between the camera and any obstacle it would otherwise touch.")]
    [SerializeField] private float collisionPadding = 0.15f;
    [Tooltip("Camera will never be pulled closer to the anchor than this, even when sandwiched.")]
    [SerializeField] private float minClampedDistance = 0.6f;

    [Header("Cursor")]
    [Tooltip("If true, the cursor is locked to the window centre and hidden at startup.")]
    [SerializeField] private bool lockCursor = true;
    [Tooltip("If true, pressing Escape releases the cursor and clicking the window re-locks it.")]
    [SerializeField] private bool toggleCursorWithEscape = true;

    // Absolute world yaw and pitch of the orbit, in degrees.
    private float yaw;
    private float pitch;
    private float timeSinceUserInput;
    private bool initialized;

    // Optional — used to gate auto-recenter on forward speed when present.
    private CarController controller;

    private void Start()
    {
        if (target != null) controller = target.GetComponent<CarController>();
        if (lockCursor) SetCursorLocked(true);
    }

    private void Update()
    {
        UpdateCursorState();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        if (!initialized)
        {
            yaw = target.eulerAngles.y;
            pitch = defaultPitch;
            initialized = true;
        }

        HandleMouseInput();
        ApplyAutoRecenter();

        // Build the orbit position: rotate a "behind the target" vector by the
        // current yaw and pitch, then anchor it above the target's pivot.
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 anchor = target.position + Vector3.up * lookAtHeight;
        Vector3 desiredPosition = anchor + orbit * new Vector3(0f, 0f, -distance);

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            positionLerpSpeed * Time.deltaTime);

        // Sphere-sweep from the anchor to the lerped camera position; if anything
        // (other than the car itself) is in the way, pull the camera in to just
        // before the hit so the view never ends up inside or behind geometry.
        ResolveCameraCollision(anchor);

        Vector3 lookDirection = anchor - transform.position;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationLerpSpeed * Time.deltaTime);
        }

        // Zero out roll so the horizon stays level even if math nudges us off.
        Vector3 e = transform.eulerAngles;
        transform.eulerAngles = new Vector3(e.x, e.y, 0f);
    }

    /// <summary>
    /// Reads mouse delta and scroll. Any mouse movement updates the orbit
    /// angles directly; the scroll wheel zooms within the configured limits.
    /// </summary>
    private void HandleMouseInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        // While the cursor is unlocked the user is interacting with the OS, not
        // the game — ignore mouse delta so dragging an editor window doesn't
        // whip the camera around.
        if (lockCursor && Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 delta = mouse.delta.ReadValue();
        if (delta.sqrMagnitude > 0.0001f)
        {
            yaw   += delta.x * orbitSensitivity;
            pitch += (invertY ? delta.y : -delta.y) * orbitSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            timeSinceUserInput = 0f;
        }
        else
        {
            timeSinceUserInput += Time.deltaTime;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance = Mathf.Clamp(
                distance - scroll * zoomSensitivity,
                minDistance,
                maxDistance);
        }
    }

    /// <summary>
    /// Eases yaw back behind the car (and pitch back to its default) once the
    /// player has stopped moving the mouse. The yaw lerp is scaled by forward
    /// speed so the camera holds still when parked and gathers behind the car
    /// only as the car actually moves forward — matching GTA's chase behaviour.
    /// </summary>
    private void ApplyAutoRecenter()
    {
        if (timeSinceUserInput < recenterDelay) return;

        float forwardSpeed = controller != null ? controller.ForwardSpeed : 0f;
        float speedFactor = recenterFullSpeed > 0f
            ? Mathf.Clamp01(forwardSpeed / recenterFullSpeed)
            : 1f;

        if (speedFactor > 0f)
        {
            float targetYaw = target.eulerAngles.y;
            yaw = Mathf.MoveTowardsAngle(
                yaw,
                targetYaw,
                recenterYawSpeed * speedFactor * Time.deltaTime);
        }

        pitch = Mathf.MoveTowards(
            pitch,
            defaultPitch,
            recenterPitchSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Sphere-casts from the look anchor toward the current camera position,
    /// skipping any collider that belongs to the target. If something solid is
    /// hit, the camera is pulled in along the same line so it sits just in
    /// front of the obstacle instead of clipping through it.
    /// </summary>
    private void ResolveCameraCollision(Vector3 anchor)
    {
        Vector3 toCamera = transform.position - anchor;
        float currentDistance = toCamera.magnitude;
        if (currentDistance < 0.0001f) return;

        Vector3 direction = toCamera / currentDistance;
        RaycastHit[] hits = Physics.SphereCastAll(
            anchor,
            collisionRadius,
            direction,
            currentDistance,
            collisionMask,
            QueryTriggerInteraction.Ignore);

        float closest = currentDistance;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (target != null && hit.collider.transform.IsChildOf(target)) continue;
            if (hit.distance <= 0f) continue;
            if (hit.distance < closest) closest = hit.distance;
        }

        if (closest < currentDistance)
        {
            float safe = Mathf.Max(minClampedDistance, closest - collisionPadding);
            transform.position = anchor + direction * safe;
        }
    }

    /// <summary>
    /// Releases the cursor on Escape and re-locks it when the player clicks
    /// the window again — standard FPS-style cursor handling for editor and
    /// build use.
    /// </summary>
    private void UpdateCursorState()
    {
        if (!lockCursor || !toggleCursorWithEscape) return;

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked
            && mouse != null
            && mouse.leftButton.wasPressedThisFrame)
        {
            SetCursorLocked(true);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
