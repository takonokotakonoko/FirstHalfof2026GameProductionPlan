
using UnityEngine;
using UnityEngine.UI;

public class SightSensitivityController : MonoBehaviour
{
    [SerializeField] public Slider sightSlider;
    [SerializeField] public PlayerMove playerMove;

    private const float DefaultSensitivity = 2f;

    private void Start()
    {
        sightSlider.minValue = 2.0f;
        sightSlider.maxValue = 7.0f;
        sightSlider.value = DefaultSensitivity;

        sightSlider.onValueChanged.AddListener(
            OnSensitivityChanged
        );

        OnSensitivityChanged(sightSlider.value);
    }

    private void OnSensitivityChanged(float value)
    {
        if (playerMove != null)
        {
            playerMove.SetCameraSensitivity(value);
        }
    }

    private void OnDestroy()
    {
        if (sightSlider != null)
        {
            sightSlider.onValueChanged.RemoveListener(
                OnSensitivityChanged
            );
        }
    }
}
