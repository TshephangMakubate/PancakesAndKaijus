using UnityEngine;

/// <summary>Pulls the camera back along its view direction as the waffle stack grows, so the top of the tower stays in frame.</summary>
[RequireComponent(typeof(Camera))]
public class StackCameraZoom : MonoBehaviour
{
    [Tooltip("Stack whose top waffle the camera keeps in view.")]
    [SerializeField] private WaffleStack _stack;
    [Tooltip("Headroom kept above the top waffle, in world units.")]
    [SerializeField] private float _topMargin = 1.2f;
    [Tooltip("Room kept beside the top waffle for its sway, in world units.")]
    [SerializeField] private float _sideMargin = 0.6f;
    [Tooltip("Seconds the zoom takes to catch up; higher is floatier.")]
    [SerializeField] private float _smoothTime = 0.6f;
    [Tooltip("Furthest the camera will pull back, in world units.")]
    [SerializeField] private float _maxPullback = 40f;

    private Camera _camera;
    private Transform _rig;
    private Vector3 _rigOrigin;
    private float _pullback;
    private float _pullbackVelocity;

    private void Awake()
    {
        _camera = GetComponent<Camera>();

        // Dolly a parent so CameraShake and GameplayCameraRig keep owning the camera's own local pose.
        _rig = new GameObject("StackZoomRig").transform;
        _rig.SetParent(transform.parent, false);
        transform.SetParent(_rig, false);
        _rigOrigin = _rig.position;
    }

    private void LateUpdate()
    {
        if (_stack == null)
        {
            return;
        }

        float target = Mathf.Min(RequiredPullback(), _maxPullback);
        _pullback = Mathf.SmoothDamp(_pullback, target, ref _pullbackVelocity, _smoothTime);
        _rig.position = _rigOrigin - transform.forward * _pullback;
    }

    /// <summary>How far back from the resting camera spot it must sit for the stack top (plus margins) to fit on screen.</summary>
    private float RequiredPullback()
    {
        Vector3 forward = transform.forward;
        Vector3 up = transform.up;
        Vector3 right = transform.right;
        Vector3 restCamera = transform.position + forward * _pullback;

        float tanVertical = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHorizontal = tanVertical * _camera.aspect;

        Vector3 top = _stack.TopRestPosition + Vector3.up * _topMargin;
        float needed = 0f;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 offset = top + Vector3.right * (side * _sideMargin) - restCamera;
            float depth = Vector3.Dot(offset, forward);
            needed = Mathf.Max(needed, Vector3.Dot(offset, up) / tanVertical - depth);
            needed = Mathf.Max(needed, Mathf.Abs(Vector3.Dot(offset, right)) / tanHorizontal - depth);
        }

        return needed;
    }
}
