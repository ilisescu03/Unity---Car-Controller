using UnityEngine;
// 1. Added the Input System namespace
using UnityEngine.InputSystem;

public class CarController : MonoBehaviour
{
    // How strongly the engine pushes the wheels forward/backward
    [SerializeField] private float motorForce = 100f;
    // Maximum angle the front wheels can turn (in degrees)
    [SerializeField] private float maxSteeringAngle = 30f;
    // How strongly the brakes stop the wheels
    [SerializeField] private float brakeForce = 1000f;

    // The physics colliders Unity uses to simulate each wheel
    [SerializeField] private WheelCollider frontLeftWheel;
    [SerializeField] private WheelCollider frontRightWheel;
    [SerializeField] private WheelCollider rearLeftWheel;
    [SerializeField] private WheelCollider rearRightWheel;

    // The visible 3D wheel meshes that we move/rotate to match the colliders
    [SerializeField] private Transform frontLeftWheelTransform;
    [SerializeField] private Transform frontRightWheelTransform;
    [SerializeField] private Transform rearLeftWheelTransform;
    [SerializeField] private Transform rearRightWheelTransform;

    // Steering input from the player: -1 (left), 0 (none), +1 (right)
    private float horizontalInput;
    // Throttle input from the player: -1 (reverse), 0 (none), +1 (forward)
    private float verticalInput;
    // Current angle applied to the front wheels this frame
    private float currentSteeringAngle;
    // Current brake force applied to the wheels this frame
    private float currentBrakeForce;
    // True while the player is holding the brake key
    private bool isBraking;

    // Rotation difference between the wheel collider and the visible wheel mesh,
    // so the mesh stays aligned correctly when the collider spins
    private Quaternion frontLeftWheelOffset;
    private Quaternion frontRightWheelOffset;
    private Quaternion rearLeftWheelOffset;
    private Quaternion rearRightWheelOffset;

    void Start()
    {
        // Calculate and store the rotation offset between each WheelCollider and
        // its visible mesh once at startup, so we can reapply it every frame.
        frontLeftWheelOffset  = Quaternion.Inverse(frontLeftWheel.transform.rotation)  * frontLeftWheelTransform.rotation;
        frontRightWheelOffset = Quaternion.Inverse(frontRightWheel.transform.rotation) * frontRightWheelTransform.rotation;
        rearLeftWheelOffset   = Quaternion.Inverse(rearLeftWheel.transform.rotation)   * rearLeftWheelTransform.rotation;
        rearRightWheelOffset  = Quaternion.Inverse(rearRightWheel.transform.rotation)  * rearRightWheelTransform.rotation;
    }

    // Reads keyboard input and turns it into steering, throttle and brake values
    void GetInput()
    {
        if (Keyboard.current != null)
        {
            // Reset input each frame so values don't stick when keys are released
            horizontalInput = 0f;
            verticalInput = 0f;

            // A / Left arrow steers left, D / Right arrow steers right
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontalInput = -1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontalInput = 1f;

            // W / Up arrow accelerates, S / Down arrow reverses
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) verticalInput = 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) verticalInput = -1f;

            // Spacebar brakes the car
            isBraking = Keyboard.current.spaceKey.isPressed;
        }
    }

    // Update runs every frame and handles input and visual wheel updates
    void Update()
    {
        GetInput();
        UpdateWheelPoses();
    }

    // FixedUpdate runs on the physics tick and handles all physics-based movement
    void FixedUpdate()
    {
        HandleMotor();
        HandleSteering();
    }

    // Applies engine power to the front wheels and decides if brakes are active
    public void HandleMotor()
    {
        // Front-wheel drive: only the front wheels receive motor torque
        frontLeftWheel.motorTorque = verticalInput * motorForce;
        frontRightWheel.motorTorque = verticalInput * motorForce;

        // If the player is braking, set brake force; otherwise zero it out
        if (isBraking)
        {
            currentBrakeForce = brakeForce;
        }
        else
        {
            currentBrakeForce = 0f;
        }

        ApplyBraking();
    }

    // Pushes the current brake force onto every wheel
    public void ApplyBraking()
    {

        frontLeftWheel.brakeTorque = currentBrakeForce;
        frontRightWheel.brakeTorque = currentBrakeForce;
        rearLeftWheel.brakeTorque = currentBrakeForce;
        rearRightWheel.brakeTorque = currentBrakeForce;
    }

    // Turns the front wheels based on horizontal input
    public void HandleSteering()
    {
        // Scale the input (-1..1) by the max angle to get the actual steering angle
        currentSteeringAngle = maxSteeringAngle * horizontalInput;
        frontLeftWheel.steerAngle = currentSteeringAngle;
        frontRightWheel.steerAngle = currentSteeringAngle;
    }

    // Moves and rotates a single visible wheel mesh to match its physics collider
    public void UpdateWheelPose(WheelCollider _collider, Transform _transform, Quaternion _offset)
    {
        Vector3 _pos;
        Quaternion _quat;
        // Ask the physics wheel where it actually is in the world right now
        _collider.GetWorldPose(out _pos, out _quat);
        _transform.position = _pos;
        // Apply the saved offset so the mesh stays aligned with the collider
        _transform.rotation = _quat * _offset;
    }

    // Syncs all four visible wheels to their physics colliders each frame
    public void UpdateWheelPoses()
    {
        UpdateWheelPose(frontLeftWheel,  frontLeftWheelTransform,  frontLeftWheelOffset);
        UpdateWheelPose(frontRightWheel, frontRightWheelTransform, frontRightWheelOffset);
        UpdateWheelPose(rearLeftWheel,   rearLeftWheelTransform,   rearLeftWheelOffset);
        UpdateWheelPose(rearRightWheel,  rearRightWheelTransform,  rearRightWheelOffset);
    }
}
