using System.Collections;
using UnityEngine;

/// <summary>A customer's spot at the far end of the serving table: knows whether a plate stopped in it, and reacts to what was served.</summary>
public class CustomerZone : MonoBehaviour
{
    private const float CheerHopHeight = 0.7f;
    private const float CheerHopSeconds = 0.3f;
    private const float GagSeconds = 0.8f;
    private const float GagDegrees = 18f;

    [Tooltip("The customer figure that cheers or gags.")]
    [SerializeField] private Transform _customer;
    [Tooltip("Zone width (x) and depth (z) in world units, centered on this transform.")]
    [SerializeField] private Vector2 _size = new Vector2(2.25f, 3.5f);

    /// <summary>True once this customer has been handed a plate.</summary>
    public bool IsServed { get; private set; }

    /// <summary>True when a world point lies inside this customer's zone (ignoring height).</summary>
    public bool Contains(Vector3 point)
    {
        Vector3 center = transform.position;
        return Mathf.Abs(point.x - center.x) <= _size.x * 0.5f && Mathf.Abs(point.z - center.z) <= _size.y * 0.5f;
    }

    /// <summary>Hands this customer their plate: a happy double hop for a good pancake, a disgusted shake for a burned one.</summary>
    public void Serve(bool happy)
    {
        IsServed = true;
        if (_customer != null)
        {
            StartCoroutine(happy ? Cheer() : Gag());
        }
    }

    private IEnumerator Cheer()
    {
        Vector3 home = _customer.position;
        Vector3 scale = _customer.localScale;

        for (int i = 0; i < 2; i++)
        {
            yield return JuiceTweens.Hop(_customer, home, CheerHopHeight, CheerHopSeconds, scale);
            yield return JuiceTweens.Squash(_customer, scale, 0.15f);
        }
    }

    private IEnumerator Gag()
    {
        Quaternion rest = _customer.localRotation;
        float elapsed = 0f;

        while (elapsed < GagSeconds)
        {
            elapsed += Time.deltaTime;
            float shake = Mathf.Sin(elapsed * 30f) * GagDegrees * (1f - elapsed / GagSeconds);
            _customer.localRotation = rest * Quaternion.Euler(0f, 0f, shake);
            yield return null;
        }

        _customer.localRotation = rest;
    }
}
