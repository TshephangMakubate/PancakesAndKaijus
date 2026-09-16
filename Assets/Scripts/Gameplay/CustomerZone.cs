using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// A customer's spot at the far end of the serving table. It knows whether a
/// plate stopped in it and plays out the visit: the customer reacts, eats, gets
/// up and leaves, and a new customer sits down to wait for the next plate.
/// </summary>
public class CustomerZone : MonoBehaviour
{
    private const float CheerHopHeight = 0.7f;
    private const float CheerHopSeconds = 0.3f;
    private const float GagSeconds = 0.8f;
    private const float GagDegrees = 18f;
    private const float ChewSquash = 0.08f;
    private const float ChewInterval = 0.45f;
    private const float PlateClearSeconds = 0.25f;

    // Leaving: back away from the table and sink out of sight behind it.
    private static readonly Vector3 LeaveOffset = new Vector3(0f, -3f, 3.5f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Each new customer draws a fresh outfit so the seat visibly changes hands.
    private static readonly Color[] ShirtColors =
    {
        new Color(0.86f, 0.25f, 0.35f),
        new Color(0.35f, 0.6f, 0.9f),
        new Color(0.5f, 0.55f, 1f),
        new Color(0.95f, 0.75f, 0.25f),
        new Color(0.4f, 0.8f, 0.55f),
        new Color(0.75f, 0.45f, 0.85f),
        new Color(0.95f, 0.5f, 0.3f)
    };

    private static readonly Color[] HairColors =
    {
        new Color(0.35f, 0.18f, 0.1f),
        new Color(0.08f, 0.08f, 0.1f),
        new Color(0.45f, 0.3f, 0.15f),
        new Color(0.85f, 0.7f, 0.35f),
        new Color(0.6f, 0.2f, 0.12f)
    };

    [Tooltip("The customer figure that cheers or gags.")]
    [SerializeField] private Transform _customer;
    [Tooltip("Zone width (x) and depth (z) in world units, centered on this transform.")]
    [SerializeField] private Vector2 _size = new Vector2(2.25f, 3.5f);

    [Header("Visit")]
    [Tooltip("How long a customer eats a good pancake before leaving.")]
    [SerializeField, Min(0f)] private float _eatSeconds = 3.5f;
    [Tooltip("How long a customer glares at a burned pancake before storming off.")]
    [SerializeField, Min(0f)] private float _refuseSeconds = 1f;
    [SerializeField, Min(0.05f)] private float _leaveSeconds = 0.6f;
    [Tooltip("Empty-seat pause between one customer leaving and the next sitting down.")]
    [SerializeField, Min(0f)] private float _vacantSeconds = 0.6f;
    [SerializeField, Min(0.05f)] private float _arriveSeconds = 0.6f;

    private Vector3 _seatLocalPosition;
    private Quaternion _seatLocalRotation;
    private Vector3 _seatScale;
    private MaterialPropertyBlock _block;

    /// <summary>Raised when a new customer has sat down and can take a plate.</summary>
    public event Action<CustomerZone> BecameAvailable;

    /// <summary>True while a customer is seated and waiting for a plate.</summary>
    public bool IsAvailable { get; private set; } = true;

    private void Awake()
    {
        if (_customer != null)
        {
            _seatLocalPosition = _customer.localPosition;
            _seatLocalRotation = _customer.localRotation;
            _seatScale = _customer.localScale;
        }
    }

    /// <summary>True when a world point lies inside this customer's zone (ignoring height).</summary>
    public bool Contains(Vector3 point)
    {
        Vector3 center = transform.position;
        return Mathf.Abs(point.x - center.x) <= _size.x * 0.5f && Mathf.Abs(point.z - center.z) <= _size.y * 0.5f;
    }

    /// <summary>
    /// Hands this customer their plate. A good pancake gets a happy hop and is
    /// eaten; a burned one gets a disgusted shake. Either way the plate is
    /// cleared and the customer leaves, making room for the next one.
    /// </summary>
    public void Serve(bool happy, ServedPlate plate)
    {
        if (!IsAvailable)
        {
            return;
        }

        IsAvailable = false;
        StartCoroutine(Visit(happy, plate));
    }

    private IEnumerator Visit(bool happy, ServedPlate plate)
    {
        if (_customer != null)
        {
            yield return happy ? Cheer() : Gag();
        }

        yield return happy ? Chew(_eatSeconds) : new WaitForSeconds(_refuseSeconds);
        yield return ClearPlate(plate);

        if (_customer != null)
        {
            yield return JuiceTweens.MoveLocal(_customer, _seatLocalPosition + LeaveOffset, _seatLocalRotation, _leaveSeconds);
            _customer.gameObject.SetActive(false);
        }

        yield return new WaitForSeconds(_vacantSeconds);

        if (_customer != null)
        {
            DressNewCustomer();

            _customer.localPosition = _seatLocalPosition + LeaveOffset;
            _customer.localRotation = _seatLocalRotation;
            _customer.localScale = _seatScale;
            _customer.gameObject.SetActive(true);
            yield return JuiceTweens.MoveLocal(_customer, _seatLocalPosition, _seatLocalRotation, _arriveSeconds);
        }

        IsAvailable = true;
        BecameAvailable?.Invoke(this);
    }

    private IEnumerator Cheer()
    {
        Vector3 home = _customer.position;

        for (int i = 0; i < 2; i++)
        {
            yield return JuiceTweens.Hop(_customer, home, CheerHopHeight, CheerHopSeconds, _seatScale);
            yield return JuiceTweens.Squash(_customer, _seatScale, 0.15f);
        }
    }

    private IEnumerator Gag()
    {
        float elapsed = 0f;

        while (elapsed < GagSeconds)
        {
            elapsed += Time.deltaTime;
            float shake = Mathf.Sin(elapsed * 30f) * GagDegrees * (1f - elapsed / GagSeconds);
            _customer.localRotation = _seatLocalRotation * Quaternion.Euler(0f, 0f, shake);
            yield return null;
        }

        _customer.localRotation = _seatLocalRotation;
    }

    /// <summary>Little squash-bobs while eating.</summary>
    private IEnumerator Chew(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            if (_customer != null)
            {
                yield return JuiceTweens.Squash(_customer, _seatScale, ChewSquash);
            }

            yield return new WaitForSeconds(ChewInterval);
        }
    }

    /// <summary>Shrinks the finished plate away.</summary>
    private static IEnumerator ClearPlate(ServedPlate plate)
    {
        if (plate == null)
        {
            yield break;
        }

        Transform target = plate.transform;
        Vector3 start = target.localScale;
        float elapsed = 0f;

        while (elapsed < PlateClearSeconds && target != null)
        {
            elapsed += Time.deltaTime;
            target.localScale = start * (1f - Mathf.Clamp01(elapsed / PlateClearSeconds));
            yield return null;
        }

        if (plate != null)
        {
            Destroy(plate.gameObject);
        }
    }

    /// <summary>Recolours the figure's shirt and hair so the next customer reads as someone new.</summary>
    private void DressNewCustomer()
    {
        _block ??= new MaterialPropertyBlock();

        Tint("Body", ShirtColors[UnityEngine.Random.Range(0, ShirtColors.Length)]);
        Tint("Hair", HairColors[UnityEngine.Random.Range(0, HairColors.Length)]);
    }

    private void Tint(string partName, Color color)
    {
        Transform part = _customer.Find(partName);
        if (part == null || !part.TryGetComponent(out Renderer renderer))
        {
            return;
        }

        renderer.GetPropertyBlock(_block);
        _block.SetColor(BaseColorId, color);
        renderer.SetPropertyBlock(_block);
    }
}
