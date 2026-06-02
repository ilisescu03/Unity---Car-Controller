using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the current speed of a <see cref="CarController"/> on a legacy
/// UI <see cref="Text"/> component. Speed is shown in km/h by default.
/// </summary>
public class CarUI : MonoBehaviour
{
    [Tooltip("The car whose speed is displayed.")]
    [SerializeField] private CarController car;

    [Tooltip("Legacy UI Text component that receives the formatted speed string.")]
    [SerializeField] private Text speedText;

    [Tooltip("Multiplier applied to m/s before display. 3.6 = km/h, 2.237 = mph.")]
    [SerializeField] private float speedUnitMultiplier = 3.6f;

    [Tooltip("Suffix appended after the speed value (e.g. \" km/h\" or \" mph\").")]
    [SerializeField] private string speedUnitSuffix = " km/h";

    private void Update()
    {
        if (car == null || speedText == null) return;

        int displaySpeed = Mathf.RoundToInt(car.CurrentSpeed * speedUnitMultiplier);
        speedText.text = displaySpeed + speedUnitSuffix;
    }
}
