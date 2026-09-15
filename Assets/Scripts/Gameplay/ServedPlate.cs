using System;
using UnityEngine;

/// <summary>A pancake plate on the serving table: slides when launched, and reports when it comes to rest or falls off the edge.</summary>
[RequireComponent(typeof(Rigidbody))]
public class ServedPlate : MonoBehaviour
{
    private const float SettleSpeed = 0.15f;
    private const float SettleSeconds = 0.35f;
    private const float FallDepth = 1f;
    private const float DespawnSeconds = 3f;
    private const float TumbleTorque = 2.5f;

    private Rigidbody _body;
    private Vector3 _tableCenter;
    private Vector2 _tableHalfSize;
    private float _stillTime;
    private bool _resting;
    private bool _tumbling;

    /// <summary>Raised each time the plate comes to a stop on the table.</summary>
    public event Action<ServedPlate> Settled;

    /// <summary>Raised once when the plate drops off the table.</summary>
    public event Action<ServedPlate> FellOff;

    /// <summary>The pancake this plate carries.</summary>
    public PancakeRecord Record { get; private set; }

    public bool IsLaunched { get; private set; }

    /// <summary>True once a customer has taken this plate; it then stays put.</summary>
    public bool IsClaimed { get; private set; }

    /// <summary>True once the plate has fallen off the table.</summary>
    public bool IsGone { get; private set; }

    /// <summary>How many times the plate has come to rest (it can be knocked and settle again).</summary>
    public int SettleCount { get; private set; }

    /// <summary>True when the plate is not sliding (never launched, resting, claimed, or gone).</summary>
    public bool IsSettled => !IsLaunched || IsGone || IsClaimed || _resting;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
    }

    public void Init(PancakeRecord record, Vector3 tableCenter, Vector2 tableHalfSize)
    {
        Record = record;
        _tableCenter = tableCenter;
        _tableHalfSize = tableHalfSize;
    }

    public void Launch(Vector3 velocity, float spin)
    {
        IsLaunched = true;
        _body.isKinematic = false;
        _body.linearVelocity = velocity;
        _body.angularVelocity = new Vector3(0f, spin, 0f);
    }

    /// <summary>Locks the plate in place with its customer.</summary>
    public void Claim()
    {
        IsClaimed = true;
        _body.linearVelocity = Vector3.zero;
        _body.angularVelocity = Vector3.zero;
        _body.isKinematic = true;
    }

    private void FixedUpdate()
    {
        if (!IsLaunched || IsClaimed || IsGone)
        {
            return;
        }

        Vector3 local = _body.position - _tableCenter;

        // Past the edge: let it tip and tumble instead of sliding off perfectly flat.
        if (!_tumbling && (Mathf.Abs(local.x) > _tableHalfSize.x || Mathf.Abs(local.z) > _tableHalfSize.y))
        {
            _tumbling = true;
            _resting = false;
            _body.constraints = RigidbodyConstraints.None;
            Vector3 torque = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)) * TumbleTorque;
            _body.AddTorque(torque, ForceMode.Impulse);
        }

        if (local.y < -FallDepth)
        {
            IsGone = true;
            FellOff?.Invoke(this);
            Destroy(gameObject, DespawnSeconds);
            return;
        }

        if (_tumbling)
        {
            return;
        }

        if (_body.linearVelocity.sqrMagnitude < SettleSpeed * SettleSpeed)
        {
            _stillTime += Time.fixedDeltaTime;
            if (!_resting && _stillTime >= SettleSeconds)
            {
                _resting = true;
                SettleCount++;
                Settled?.Invoke(this);
            }
        }
        else
        {
            _stillTime = 0f;
            _resting = false;
        }
    }
}
