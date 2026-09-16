using UnityEngine;

/// <summary>
/// Frames the serving table so it fills as much of the screen as the aspect
/// ratio allows. The camera keeps a fixed pitch; its distance and position are
/// solved so the table top and the customers behind it touch the margins.
/// Re-solves whenever the screen size changes.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ServingCameraFit : MonoBehaviour
{
    private const int Iterations = 24;
    private const float CustomerHeight = 2.6f;
    private const float CustomerOverhang = 1.8f;

    [Tooltip("Center of the table top.")]
    [SerializeField] private Transform _table;
    [Tooltip("Half the table's width (x) and length (z).")]
    [SerializeField] private Vector2 _tableHalfSize = new Vector2(7f, 12f);
    [Tooltip("Downward tilt of the camera, in degrees.")]
    [SerializeField, Range(30f, 89f)] private float _pitch = 58f;

    [Header("Screen Margins (fraction of the screen)")]
    [Tooltip("Room left at the top for the timer and scores.")]
    [SerializeField, Range(0f, 0.4f)] private float _top = 0.12f;
    [SerializeField, Range(0f, 0.4f)] private float _bottom = 0.02f;
    [SerializeField, Range(0f, 0.4f)] private float _sides = 0.02f;

    private Camera _camera;
    private int _fittedWidth;
    private int _fittedHeight;

    /// <summary>Points the fitter at a table. Used by the scene setup.</summary>
    public void Configure(Transform table, Vector2 tableHalfSize)
    {
        _table = table;
        _tableHalfSize = tableHalfSize;
    }

    private void LateUpdate()
    {
        if (Screen.width != _fittedWidth || Screen.height != _fittedHeight)
        {
            Fit();
        }
    }

    /// <summary>Solves the camera placement for the current screen.</summary>
    public void Fit()
    {
        if (_camera == null)
        {
            _camera = GetComponent<Camera>();
        }

        if (_table == null || _camera == null)
        {
            return;
        }

        _fittedWidth = Screen.width;
        _fittedHeight = Screen.height;

        Vector3 center = _table.position;
        float hw = _tableHalfSize.x;
        float hl = _tableHalfSize.y;

        // The table top, plus the customers' heads beyond its far edge.
        Vector3[] points =
        {
            center + new Vector3(-hw, 0f, -hl),
            center + new Vector3(hw, 0f, -hl),
            center + new Vector3(-hw, 0f, hl),
            center + new Vector3(hw, 0f, hl),
            center + new Vector3(-hw, CustomerHeight, hl + CustomerOverhang),
            center + new Vector3(hw, CustomerHeight, hl + CustomerOverhang)
        };

        Quaternion rotation = Quaternion.Euler(_pitch, 0f, 0f);
        Vector3 forward = rotation * Vector3.forward;
        transform.rotation = rotation;

        float fillWidth = Mathf.Max(0.1f, 1f - 2f * _sides);
        float fillHeight = Mathf.Max(0.1f, 1f - _top - _bottom);
        float targetCenterX = 0.5f;
        float targetCenterY = _bottom + fillHeight * 0.5f;

        Vector3 lookAt = center;
        float distance = Mathf.Max(hw, hl) * 3f;
        float tanHalf = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

        for (int i = 0; i < Iterations; i++)
        {
            transform.position = lookAt - forward * distance;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 point in points)
            {
                Vector3 viewport = _camera.WorldToViewportPoint(point);
                minX = Mathf.Min(minX, viewport.x);
                maxX = Mathf.Max(maxX, viewport.x);
                minY = Mathf.Min(minY, viewport.y);
                maxY = Mathf.Max(maxY, viewport.y);
            }

            // Recentre first, then scale: on-screen size is roughly inverse to distance.
            float viewHeight = 2f * distance * tanHalf;
            float viewWidth = viewHeight * _camera.aspect;
            lookAt += transform.right * (((minX + maxX) * 0.5f - targetCenterX) * viewWidth);
            lookAt += transform.up * (((minY + maxY) * 0.5f - targetCenterY) * viewHeight);

            float scale = Mathf.Max((maxX - minX) / fillWidth, (maxY - minY) / fillHeight);
            distance *= Mathf.Lerp(1f, scale, 0.8f);
        }

        transform.position = lookAt - forward * distance;
    }
}
