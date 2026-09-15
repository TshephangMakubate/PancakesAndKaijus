using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Applies and exposes the gameplay camera framing (position, rotation, FOV,
/// near clip) plus the lens-distortion intensity for the low, wide-angle
/// "face-off" shot. All values are serialized so the composition can be tuned
/// in the Inspector without recompiling. Runs in edit mode for live preview.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class GameplayCameraRig : MonoBehaviour
{
    [Header("Framing")]
    [Tooltip("Local position of the camera rig (close and slightly left to crop the pan handle).")]
    [SerializeField] private Vector3 _position = new Vector3(-0.3f, -0.95f, -1.6f);
    [Tooltip("Local euler rotation of the camera rig (slight downward-into-frame pitch).")]
    [SerializeField] private Vector3 _eulerRotation = new Vector3(5f, 0f, 0f);
    [Tooltip("Perspective vertical field of view in degrees.")]
    [SerializeField] private float _fieldOfView = 80f;
    [Tooltip("Near clip plane, small so the close pan stays visible.")]
    [SerializeField] private float _nearClipPlane = 0.05f;
    [Tooltip("Use an orthographic projection for a flat, 2D-style look.")]
    [SerializeField] private bool _orthographic = false;
    [Tooltip("Half of the vertical viewport height in world units (orthographic only).")]
    [SerializeField] private float _orthographicSize = 4.2f;

    [Header("Post Processing")]
    [Tooltip("Volume whose shared profile carries the Lens Distortion override.")]
    [SerializeField] private Volume _volume;
    [Tooltip("Lens distortion intensity that sells the wide-angle face-off look.")]
    [SerializeField, Range(-1f, 1f)] private float _lensDistortionIntensity = 0.3f;

    private Camera _camera;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    /// <summary>
    /// Pushes the serialized framing and lens-distortion values onto the camera
    /// and the volume profile. Safe to call from edit mode and play mode.
    /// </summary>
    public void Apply()
    {
        if (_camera == null)
        {
            _camera = GetComponent<Camera>();
        }

        if (_camera != null)
        {
            _camera.orthographic = _orthographic;
            _camera.orthographicSize = _orthographicSize;
            _camera.fieldOfView = _fieldOfView;
            _camera.nearClipPlane = _nearClipPlane;
        }

        transform.localPosition = _position;
        transform.localEulerAngles = _eulerRotation;

        ApplyLensDistortion();
    }

    /// <summary>
    /// Enables the Lens Distortion override on the volume profile and drives its
    /// intensity, adding the override if the profile does not already carry one.
    /// Editing the shared profile persists to the profile asset in the editor.
    /// </summary>
    private void ApplyLensDistortion()
    {
        if (_volume == null || _volume.sharedProfile == null)
        {
            return;
        }

        if (!_volume.sharedProfile.TryGet(out LensDistortion lensDistortion))
        {
            lensDistortion = _volume.sharedProfile.Add<LensDistortion>(true);
        }

        lensDistortion.active = true;
        lensDistortion.intensity.overrideState = true;
        lensDistortion.intensity.value = _lensDistortionIntensity;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(_volume.sharedProfile);
        }
#endif
    }
}
