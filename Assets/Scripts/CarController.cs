using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives a four-wheeled vehicle using Unity's WheelCollider physics and the
/// new Input System. Handles steering, throttle, handbrake, motor braking and
/// reverse. When a <see cref="CarLights"/> component is assigned, driving
/// signals and light toggles are forwarded to it each frame.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Driving tuning
    // ---------------------------------------------------------------------

    [Tooltip("Torque applied to the driven wheels per unit of throttle input.")]
    [SerializeField] private float motorForce = 100f;

    [Tooltip("Maximum steering angle of the front wheels at low speed, in degrees.")]
    [SerializeField] private float maxSteeringAngle = 30f;

    [Tooltip("Speed (m/s) at which the steering angle has fully fallen off to highSpeedSteeringFactor.")]
    [SerializeField] private float steeringFalloffSpeed = 20f;

    [Tooltip("Fraction of maxSteeringAngle still available at or above steeringFalloffSpeed. Lower values reduce twitchiness and tyre scrub at high speed.")]
    [Range(0f, 1f)]
    [SerializeField] private float highSpeedSteeringFactor = 0.35f;

    [Tooltip("Brake torque applied to every wheel when the handbrake is held.")]
    [SerializeField] private float brakeForce = 4000f;

    [Tooltip("Brake torque applied to every wheel when motor braking (reverse key held while moving forward). Usually lower than handbrake force.")]
    [SerializeField] private float motorBrakeForce = 500f;

    [Tooltip("Fraction of the brake torque applied to the front wheels. 1 = same as rear (max stopping power, less steering), 0 = front wheels never brake (max steering responsiveness, weak braking). ~0.6 keeps both effective.")]
    [Range(0f, 1f)]
    [SerializeField] private float frontBrakeBias = 0.6f;

    [Tooltip("Forward speed (m/s) above which the reverse key acts as a motor brake instead of engaging reverse.")]
    [SerializeField] private float motorBrakeSpeedThreshold = 0.1f;

    [Tooltip("Top speed when driving forward, in m/s. Motor torque is cut off above this.")]
    [SerializeField] private float maxForwardSpeed = 30f;

    [Tooltip("Top speed when driving in reverse, in m/s. Motor torque is cut off above this.")]
    [SerializeField] private float maxReverseSpeed = 10f;

    [Header("Stability")]
    [Tooltip("Local-space offset applied to the Rigidbody's center of mass at start. A negative Y (below the wheels) dramatically reduces the risk of the car flipping during hard turns.")]
    [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.5f, 0f);

    [Tooltip("Anti-roll bar stiffness per axle, in newtons. Higher values resist body roll more strongly. Set to 0 to disable.")]
    [SerializeField] private float antiRollForce = 5000f;

    // ---------------------------------------------------------------------
    // Wheel colliders (physics) and visible wheel meshes
    // ---------------------------------------------------------------------

    [Header("Wheel colliders")]
    [SerializeField] private WheelCollider frontLeftWheel;
    [SerializeField] private WheelCollider frontRightWheel;
    [SerializeField] private WheelCollider rearLeftWheel;
    [SerializeField] private WheelCollider rearRightWheel;

    [Header("Wheel meshes")]
    [SerializeField] private Transform frontLeftWheelTransform;
    [SerializeField] private Transform frontRightWheelTransform;
    [SerializeField] private Transform rearLeftWheelTransform;
    [SerializeField] private Transform rearRightWheelTransform;

    // ---------------------------------------------------------------------
    // Lights bridge
    // ---------------------------------------------------------------------

    [Header("Lights")]
    [Tooltip("Optional lights component. If left empty, the script tries GetComponent at start-up.")]
    [SerializeField] private CarLights lights;

    // ---------------------------------------------------------------------
    // Runtime input state
    // ---------------------------------------------------------------------

    private float horizontalInput;       // -1 left, 0 none, +1 right
    private float verticalInput;         // -1 reverse, 0 none/motor brake, +1 forward
    private float currentSteeringAngle;
    private float currentBrakeForce;
    private bool isBraking;              // handbrake (space) is held
    private bool isMotorBraking;         // reverse key held while moving forward

    // Previous-frame key states for edge-triggered toggles.
    private bool lightsKeyPrev;
    private bool leftSignalKeyPrev;
    private bool rightSignalKeyPrev;

    // ---------------------------------------------------------------------
    // Cached references
    // ---------------------------------------------------------------------

    private Rigidbody rb;

    // Rotation offsets between each WheelCollider and its visible mesh,
    // captured once so the mesh stays aligned as the collider rotates.
    private Quaternion frontLeftWheelOffset;
    private Quaternion frontRightWheelOffset;
    private Quaternion rearLeftWheelOffset;
    private Quaternion rearRightWheelOffset;

    /// <summary>
    /// Current ground speed of the car in metres per second. Always non-negative;
    /// use <see cref="ForwardSpeed"/> when the sign of motion matters.
    /// </summary>
    public float CurrentSpeed => rb != null ? rb.linearVelocity.magnitude : 0f;

    /// <summary>
    /// Signed forward speed of the car in metres per second. Positive when
    /// moving forward, negative when moving in reverse.
    /// </summary>
    public float ForwardSpeed => rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;

    // ---------------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------------

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;
        if (lights == null) lights = GetComponent<CarLights>();

        // Capture the rotation difference between each collider and its mesh.
        frontLeftWheelOffset  = Quaternion.Inverse(frontLeftWheel.transform.rotation)  * frontLeftWheelTransform.rotation;
        frontRightWheelOffset = Quaternion.Inverse(frontRightWheel.transform.rotation) * frontRightWheelTransform.rotation;
        rearLeftWheelOffset   = Quaternion.Inverse(rearLeftWheel.transform.rotation)   * rearLeftWheelTransform.rotation;
        rearRightWheelOffset  = Quaternion.Inverse(rearRightWheel.transform.rotation)  * rearRightWheelTransform.rotation;
    }

    private void Update()
    {
        GetInput();
        UpdateWheelPoses();

        // Forward the current driving signals to the lights component.
        if (lights != null)
        {
            lights.SetDrivingState(isBraking, isMotorBraking, verticalInput < 0f);
        }
    }

    private void FixedUpdate()
    {
        HandleMotor();
        HandleSteering();
        ApplyAntiRoll();
    }

    // ---------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------

    /// <summary>
    /// Polls the keyboard each frame and converts key state into steering,
    /// throttle, brake, and light-toggle values.
    /// </summary>
    private void GetInput()
    {
        if (Keyboard.current == null) return;

        horizontalInput = 0f;
        verticalInput = 0f;
        isMotorBraking = false;

        // Steering: A/Left and D/Right.
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontalInput = -1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontalInput = 1f;

        // Each throttle key either drives the car in its direction or acts as a
        // motor brake when pressed against the car's current direction of motion.
        bool forwardKey = Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed;
        bool reverseKey = Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed;
        float forwardSpeed = rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;

        // W/Up: forward throttle, or motor brake while the car is rolling backwards.
        if (forwardKey && !reverseKey)
        {
            if (forwardSpeed < -motorBrakeSpeedThreshold) isMotorBraking = true;
            else                                          verticalInput = 1f;
        }

        // S/Down: reverse throttle, or motor brake while the car is rolling forwards.
        if (reverseKey && !forwardKey)
        {
            if (forwardSpeed > motorBrakeSpeedThreshold) isMotorBraking = true;
            else                                         verticalInput = -1f;
        }

        // Handbrake.
        isBraking = Keyboard.current.spaceKey.isPressed;

        // Edge-triggered toggles forwarded to the lights component.
        bool lightsKey = Keyboard.current.lKey.isPressed;
        if (lightsKey && !lightsKeyPrev && lights != null) lights.ToggleHeadLights();
        lightsKeyPrev = lightsKey;

        bool leftSignalKey = Keyboard.current.qKey.isPressed;
        if (leftSignalKey && !leftSignalKeyPrev && lights != null) lights.ToggleSignalLeft();
        leftSignalKeyPrev = leftSignalKey;

        bool rightSignalKey = Keyboard.current.eKey.isPressed;
        if (rightSignalKey && !rightSignalKeyPrev && lights != null) lights.ToggleSignalRight();
        rightSignalKeyPrev = rightSignalKey;
    }

    // ---------------------------------------------------------------------
    // Driving
    // ---------------------------------------------------------------------

    /// <summary>
    /// Applies motor torque to the front wheels and engages braking when the
    /// handbrake or motor brake is active. Motor torque is cut off once the
    /// car reaches its top speed in the current direction of travel.
    /// </summary>
    public void HandleMotor()
    {
        // Cut motor torque once the car reaches its top speed in the direction
        // the player is currently trying to accelerate. Braking is unaffected.
        float forwardSpeed = rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
        float torque = verticalInput * motorForce;
        if (verticalInput > 0f && forwardSpeed >=  maxForwardSpeed) torque = 0f;
        if (verticalInput < 0f && forwardSpeed <= -maxReverseSpeed) torque = 0f;
        // Don't drive against the brakes — otherwise the motor fights the brake torque.
        if (isBraking || isMotorBraking) torque = 0f;

        // Front-wheel drive.
        frontLeftWheel.motorTorque = torque;
        frontRightWheel.motorTorque = torque;

        // Handbrake takes priority over motor brake when both are somehow active.
        if      (isBraking)      currentBrakeForce = brakeForce;
        else if (isMotorBraking) currentBrakeForce = motorBrakeForce;
        else                     currentBrakeForce = 0f;
        ApplyBraking();
    }

    /// <summary>
    /// Applies brake torque to all four wheels using a front-to-rear brake bias.
    /// The rear wheels receive the full <see cref="currentBrakeForce"/>; the
    /// fronts receive a fraction of it (<see cref="frontBrakeBias"/>), so they
    /// help stop the car while still keeping enough lateral grip to steer.
    /// </summary>
    public void ApplyBraking()
    {
        float frontBrake = currentBrakeForce * frontBrakeBias;

        frontLeftWheel.brakeTorque  = frontBrake;
        frontRightWheel.brakeTorque = frontBrake;
        rearLeftWheel.brakeTorque   = currentBrakeForce;
        rearRightWheel.brakeTorque  = currentBrakeForce;
    }

    /// <summary>
    /// Sets the front-wheel steering angle from horizontal input. The effective
    /// maximum angle shrinks as the car speeds up so the tyres don't scrub
    /// away velocity at high speed.
    /// </summary>
    public void HandleSteering()
    {
        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        float t = steeringFalloffSpeed > 0f ? Mathf.Clamp01(speed / steeringFalloffSpeed) : 0f;
        float angleMultiplier = Mathf.Lerp(1f, highSpeedSteeringFactor, t);

        currentSteeringAngle = maxSteeringAngle * angleMultiplier * horizontalInput;
        frontLeftWheel.steerAngle = currentSteeringAngle;
        frontRightWheel.steerAngle = currentSteeringAngle;
    }

    /// <summary>
    /// Aligns one visible wheel mesh with its <see cref="WheelCollider"/>,
    /// applying the cached rotation offset so the mesh keeps its authored
    /// orientation as the collider rotates.
    /// </summary>
    public void UpdateWheelPose(WheelCollider collider, Transform meshTransform, Quaternion offset)
    {
        collider.GetWorldPose(out Vector3 worldPos, out Quaternion worldRot);
        meshTransform.position = worldPos;
        meshTransform.rotation = worldRot * offset;
    }

    /// <summary>Updates all four wheel-mesh transforms.</summary>
    public void UpdateWheelPoses()
    {
        UpdateWheelPose(frontLeftWheel,  frontLeftWheelTransform,  frontLeftWheelOffset);
        UpdateWheelPose(frontRightWheel, frontRightWheelTransform, frontRightWheelOffset);
        UpdateWheelPose(rearLeftWheel,   rearLeftWheelTransform,   rearLeftWheelOffset);
        UpdateWheelPose(rearRightWheel,  rearRightWheelTransform,  rearRightWheelOffset);
    }

    // ---------------------------------------------------------------------
    // Stability
    // ---------------------------------------------------------------------

    /// <summary>
    /// Applies an anti-roll force to both axles. When one wheel on an axle is
    /// more compressed than its partner, an opposing force is applied to each
    /// side of the chassis to resist body roll. This keeps the car planted
    /// during hard turns and is the standard fix for WheelCollider rollover.
    /// </summary>
    private void ApplyAntiRoll()
    {
        if (antiRollForce <= 0f || rb == null) return;
        StabilizeAxle(frontLeftWheel, frontRightWheel);
        StabilizeAxle(rearLeftWheel,  rearRightWheel);
    }

    private void StabilizeAxle(WheelCollider left, WheelCollider right)
    {
        // Suspension travel on each side, expressed as 0 (fully extended) to 1
        // (fully compressed). Wheels that aren't touching the ground stay at 1.
        float travelL = 1f;
        float travelR = 1f;

        bool groundedL = left.GetGroundHit(out WheelHit hitL);
        if (groundedL)
            travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance;

        bool groundedR = right.GetGroundHit(out WheelHit hitR);
        if (groundedR)
            travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance;

        // Push down on the less-compressed side and lift the more-compressed
        // side, scaled by the difference in travel.
        float force = (travelL - travelR) * antiRollForce;
        if (groundedL) rb.AddForceAtPosition(left.transform.up  * -force, left.transform.position);
        if (groundedR) rb.AddForceAtPosition(right.transform.up *  force, right.transform.position);
    }
}
