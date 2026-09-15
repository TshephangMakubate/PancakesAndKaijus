using UnityEngine;

/// <summary>
/// Positions the two face-off chefs symmetrically about a center line and
/// rotates them inward toward each other. The left chef follows its authored
/// anchor; the right chef is driven as an exact X-mirror in code so both
/// players always share identical framing and pan visibility. Runs in edit
/// mode so the composition can be tuned live in the Inspector without
/// hand-placing both sides of the hierarchy.
/// </summary>
[ExecuteAlways]
public class ChefFaceoffLayout : MonoBehaviour
{
    [Header("Chef roots")]
    [Tooltip("Root transform of the left chef (body, head, arm, hand).")]
    [SerializeField] private Transform _leftChef;
    [Tooltip("Root transform of the right chef; driven as an exact mirror of the left.")]
    [SerializeField] private Transform _rightChef;

    [Header("Anchors (tune in the Inspector)")]
    [Tooltip("Anchor that defines the left chef's world placement. Move this to tune the composition.")]
    [SerializeField] private Transform _leftAnchor;
    [Tooltip("Reference anchor kept in sync as the mirror of the left anchor.")]
    [SerializeField] private Transform _rightAnchor;

    [Header("Mirror settings")]
    [Tooltip("World X of the center divider that the two chefs mirror about.")]
    [SerializeField] private float _centerX = 0f;
    [Tooltip("Inward yaw in degrees. The left chef turns clockwise (from above) and the right counter-clockwise, so they face each other.")]
    [SerializeField, Range(0f, 45f)] private float _inwardAngle = 25f;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            Apply();
        }
    }

    /// <summary>
    /// Places the left chef at its anchor, then derives the right chef as the
    /// exact X-mirror about <see cref="_centerX"/> with opposite inward yaw, so
    /// the pair reads as a symmetric face-off and neither side gains an edge.
    /// </summary>
    public void Apply()
    {
        if (_leftChef == null || _rightChef == null || _leftAnchor == null)
        {
            return;
        }

        Vector3 leftPosition = _leftAnchor.position;
        _leftChef.position = leftPosition;
        // Left chef turns clockwise viewed from above (negative yaw) to face inward.
        _leftChef.rotation = Quaternion.Euler(0f, -_inwardAngle, 0f);

        Vector3 rightPosition = new Vector3(2f * _centerX - leftPosition.x, leftPosition.y, leftPosition.z);
        if (_rightAnchor != null)
        {
            _rightAnchor.position = rightPosition;
        }

        _rightChef.position = rightPosition;
        // Right chef mirrors: counter-clockwise viewed from above (positive yaw).
        _rightChef.rotation = Quaternion.Euler(0f, _inwardAngle, 0f);
    }
}
