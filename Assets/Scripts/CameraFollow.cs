using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The vehicle (or any Transform) the camera should follow.")]
    // The object (usually the car) that the camera will follow
    [SerializeField] private Transform target;

    [Header("Offset (in target's local space, yaw-only)")]
    [Tooltip("Distance behind the target along its forward axis.")]
    // How far behind the car the camera sits
    [SerializeField] private float distance = 6f;
    [Tooltip("Height above the target.")]
    // How high above the car the camera sits
    [SerializeField] private float height = 2.5f;
    [Tooltip("Lateral offset (positive = right of target).")]
    // Side offset; positive shifts the camera to the right of the car
    [SerializeField] private float lateral = 0f;

    [Header("Smoothing")]
    [Tooltip("How quickly the camera catches up to the target's position. Higher = snappier.")]
    // Controls how fast the camera moves toward its desired position
    [SerializeField] private float positionLerpSpeed = 8f;
    [Tooltip("How quickly the camera rotates to face the target. Higher = snappier.")]
    // Controls how fast the camera rotates to look at the target
    [SerializeField] private float rotationLerpSpeed = 8f;

    [Header("Look")]
    [Tooltip("Extra height above the target's pivot to aim at (look slightly above the car).")]
    // Raises the look-at point slightly above the car so we don't aim at the ground
    [SerializeField] private float lookAtHeight = 1f;

    // LateUpdate runs after all other Updates, so the car has finished moving
    // before the camera reads its position. This avoids jittery follow movement.
    void LateUpdate()
    {
        // Nothing to follow yet, so skip this frame
        if (target == null) return;

        // Get the car's Y rotation (yaw) only — we ignore pitch and roll so the
        // camera doesn't tilt with the car going over bumps or ramps.
        float targetYaw = target.eulerAngles.y;
        Quaternion yawRotation = Quaternion.Euler(0f, targetYaw, 0f);

        // Build an offset relative to the car: side, up, and behind it
        Vector3 localOffset = new Vector3(lateral, height, -distance);
        // Rotate that offset by the car's yaw and add to its position to get
        // the world-space spot where the camera should be
        Vector3 desiredPosition = target.position + yawRotation * localOffset;


        // Smoothly move the camera from its current position toward the desired one
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            positionLerpSpeed * Time.deltaTime);


        // The point in the world the camera should look at (slightly above the car)
        Vector3 lookTarget = target.position + Vector3.up * lookAtHeight;
        // Direction from the camera to that look-at point
        Vector3 lookDirection = lookTarget - transform.position;


        // Only rotate if the direction has meaningful length (avoids errors when
        // the camera is exactly on top of the target)
        if (lookDirection.sqrMagnitude > 0.0001f)
        {

            // Calculate the rotation needed to face the look target
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);

            // Smoothly rotate the camera toward the desired rotation
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationLerpSpeed * Time.deltaTime);
        }

        // Zero out the Z (roll) rotation so the camera never tilts sideways,
        // keeping the horizon level even if math nudges it off.
        Vector3 e = transform.eulerAngles;
        transform.eulerAngles = new Vector3(e.x, e.y, 0f);
    }
}
