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

    [Tooltip("Maximum steering angle of the front wheels, in degrees.")]
    [SerializeField] private float maxSteeringAngle = 30f;

    [Tooltip("Brake torque applied to every wheel when braking.")]
    [SerializeField] private float brakeForce = 1000f;

    [Tooltip("Forward speed (m/s) above which the reverse key acts as a motor brake instead of engaging reverse.")]
    [SerializeField] private float motorBrakeSpeedThreshold = 0.1f;

    [Tooltip("Top speed when driving forward, in m/s. Motor torque is cut off above this.")]
    [SerializeField] private float maxForwardSpeed = 30f;

    [Tooltip("Top speed when driving in reverse, in m/s. Motor torque is cut off above this.")]
    [SerializeField] private float maxReverseSpeed = 10f;

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

    // ---------------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------------

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
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

        // Throttle: W/Up applies forward torque.
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) verticalInput = 1f;

        // S/Down acts as a motor brake while moving forward above the threshold,
        // and switches to reverse drive once the car has effectively stopped.
        bool reverseKey = Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed;
        if (reverseKey)
        {
            float forwardSpeed = rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
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

        // Front-wheel drive.
        frontLeftWheel.motorTorque = torque;
        frontRightWheel.motorTorque = torque;

        currentBrakeForce = (isBraking || isMotorBraking) ? brakeForce : 0f;
        ApplyBraking();
    }

    /// <summary>Applies the current brake torque to every wheel.</summary>
    public void ApplyBraking()
    {
        frontLeftWheel.brakeTorque = currentBrakeForce;
        frontRightWheel.brakeTorque = currentBrakeForce;
        rearLeftWheel.brakeTorque = currentBrakeForce;
        rearRightWheel.brakeTorque = currentBrakeForce;
    }

    /// <summary>Sets the front-wheel steering angle from horizontal input.</summary>
    public void HandleSteering()
    {
        currentSteeringAngle = maxSteeringAngle * horizontalInput;
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
}
