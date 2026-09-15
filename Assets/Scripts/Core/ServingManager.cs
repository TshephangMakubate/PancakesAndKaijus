using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Round 3: pull back and release to slide your pancake plates down the table to the customers.</summary>
public class ServingManager : MonoBehaviour
{
    private const float IntroSeconds = 1f;
    private const float TitleBannerSeconds = 1.8f;
    private const float GoBannerSeconds = 0.5f;
    private const float EndBannerSeconds = 1.4f;
    private const float SettleGraceSeconds = 3f;
    private const float PlatePopSeconds = 0.25f;
    private const float BlockedSpotSpawnSeconds = 1.5f;
    private const float LaunchSpotClearance = 1.7f;
    private const float MinLaunchPower = 0.08f;
    private const float PerfectAccuracy = 0.999f;
    private const float FloatingTextSeconds = 1.1f;
    private const float FloatingTextSize = 12f;
    private const float AimLift = 0.35f;
    private const float MaxLaunchSpin = 4f;
    private const string TitleMessage = "Round 3\nServe your customers!";
    private const string GoMessage = "GO!";
    private const string TimesUpMessage = "Time's up!";
    private const string AllServedMessage = "Everyone's fed!";
    private const string OutOfPlatesMessage = "Out of pancakes!";

    // Unity's cylinder mesh is 2 units tall, so a Y scale of s gives a height of 2s.
    private const float RimHeight = 0.12f;
    private const float PancakeRadius = 0.525f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Vector3 RimScale = new Vector3(1.5f, 0.06f, 1.5f);
    private static readonly Vector3 WellScale = new Vector3(1.2f, 0.062f, 1.2f);
    private static readonly Vector3 PancakeScale = new Vector3(1.05f, 0.07f, 1.05f);
    private static readonly Color GoodTextColor = new Color(0.3f, 0.85f, 0.35f);
    private static readonly Color BadTextColor = new Color(0.9f, 0.25f, 0.2f);
    private static readonly Color NeutralTextColor = new Color(0.95f, 0.95f, 0.9f);

    [Header("Config & Presentation")]
    [SerializeField] private GameConfig _config;
    [SerializeField] private HUDController _hud;
    [SerializeField] private PancakeDecorator _decorator;

    [Header("Table")]
    [Tooltip("Where each of your plates waits to be slid.")]
    [SerializeField] private Transform _launchSpot;
    [Tooltip("Center of the table top; its forward (Z) points at the customers.")]
    [SerializeField] private Transform _tableCenter;
    [Tooltip("Half the table's width (x) and length (z).")]
    [SerializeField] private Vector2 _tableHalfSize = new Vector2(4.5f, 12f);
    [SerializeField] private CustomerZone[] _customers;
    [Tooltip("Where the other team's idle plates sit. They can be knocked around but never score.")]
    [SerializeField] private Transform[] _opponentPlateSpots;

    [Header("Plates")]
    [SerializeField] private GameObject _pancakePrefab;
    [SerializeField] private Material _teamPlateMaterial;
    [SerializeField] private Material _opponentPlateMaterial;
    [SerializeField] private Material _plateWellMaterial;
    [SerializeField] private float _plateFriction = 0.12f;
    [SerializeField] private float _plateBounciness = 0.35f;
    [Tooltip("Air-hockey style slow-down; higher stops plates sooner.")]
    [SerializeField] private float _plateDrag = 0.6f;

    [Header("Aiming")]
    [Tooltip("Pull-back distance (world units) for a full-power slide.")]
    [SerializeField] private float _maxDragDistance = 3.5f;
    [SerializeField] private float _minLaunchSpeed = 4f;
    [SerializeField] private float _maxLaunchSpeed = 22f;
    [Tooltip("How far left or right of straight ahead you can aim, in degrees.")]
    [SerializeField] private float _maxAimAngle = 70f;
    [SerializeField] private Color _aimColor = new Color(0.9f, 0.25f, 0.25f);

    [Header("Scoring")]
    [SerializeField] private int _goodServePoints = 100;
    [SerializeField] private int _perfectBonus = 50;
    [SerializeField] private int _burnedServePenalty = 50;

    [Header("Testing")]
    [Tooltip("Plates to serve when this scene is played directly without cooking first (every third one burned).")]
    [SerializeField] private int _fallbackPlateCount = 6;

    private readonly Queue<PancakeRecord> _queue = new Queue<PancakeRecord>();
    private readonly List<ServedPlate> _plates = new List<ServedPlate>();

    private Camera _camera;
    private PhysicsMaterial _slideMaterial;
    private Material _aimMaterial;
    private LineRenderer _aimArrow;
    private LineRenderer _aimBand;

    private ServedPlate _readyPlate;
    private bool _spawning;
    private float _spotBlockedTime;
    private bool _aiming;
    private Vector3 _pendingDirection = Vector3.forward;
    private float _pendingPower;

    private bool _roundActive;
    private float _roundTimer;
    private int _score;
    private int _servedCount;

    private Vector3 TableForward
    {
        get
        {
            Vector3 forward = _tableCenter.forward;
            forward.y = 0f;
            return forward.normalized;
        }
    }

    private int PlatesLeft => _queue.Count + (_readyPlate != null ? 1 : 0) + (_spawning ? 1 : 0);

    private bool AllCustomersServed
    {
        get
        {
            if (_customers == null || _customers.Length == 0)
            {
                return false;
            }

            foreach (CustomerZone customer in _customers)
            {
                if (customer != null && !customer.IsServed)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool AnyPlateSliding
    {
        get
        {
            foreach (ServedPlate plate in _plates)
            {
                if (plate != null && !plate.IsSettled)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Start()
    {
        _camera = Camera.main;
        _slideMaterial = new PhysicsMaterial("PlateSlide")
        {
            dynamicFriction = _plateFriction,
            staticFriction = _plateFriction,
            bounciness = _plateBounciness,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };

        CreateAimVisuals();
        StartCoroutine(RunServing());
    }

    private void Update()
    {
        if (!_roundActive)
        {
            return;
        }

        _roundTimer = Mathf.Max(0f, _roundTimer - Time.deltaTime);
        _hud?.SetTimer(_roundTimer);

        TrySpawnNextPlate();
        HandleAiming();
    }

    private IEnumerator RunServing()
    {
        if (_config == null || _launchSpot == null || _tableCenter == null)
        {
            Debug.LogError("ServingManager: Config, launch spot, and table center must all be assigned.");
            yield break;
        }

        FillQueue();
        SpawnOpponentPlates();

        _hud?.SetRound(_config.RoundCount, _config.DisplayedRoundCount);
        _hud?.SetTimer(_config.ServingRoundSeconds);
        UpdateHud();
        yield return new WaitForSeconds(IntroSeconds);

        _hud?.ShowBanner(TitleMessage);
        yield return new WaitForSeconds(TitleBannerSeconds);
        _hud?.ShowBanner(GoMessage);
        yield return new WaitForSeconds(GoBannerSeconds);
        _hud?.HideMessage();

        _roundTimer = _config.ServingRoundSeconds;
        _roundActive = true;

        while (_roundTimer > 0f && !AllCustomersServed && (PlatesLeft > 0 || AnyPlateSliding))
        {
            yield return null;
        }

        _roundActive = false;
        CancelAim();

        string ending = AllCustomersServed ? AllServedMessage : _roundTimer <= 0f ? TimesUpMessage : OutOfPlatesMessage;
        _hud?.ShowBanner(ending);

        // Plates still sliding when the round ends get to finish and score.
        float grace = 0f;
        while (AnyPlateSliding && grace < SettleGraceSeconds)
        {
            grace += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(EndBannerSeconds);
        ShowResults();
    }

    private void FillQueue()
    {
        _queue.Clear();

        if (!PancakeCarryover.HasData)
        {
            Debug.Log($"ServingManager: No pancakes carried over from cooking; using {_fallbackPlateCount} test plates.");
            for (int i = 0; i < _fallbackPlateCount; i++)
            {
                _queue.Enqueue(new PancakeRecord(1f, i % 3 == 2));
            }

            return;
        }

        // Serve from the top of the stack down.
        IReadOnlyList<PancakeRecord> pancakes = PancakeCarryover.Pancakes;
        for (int i = pancakes.Count - 1; i >= 0; i--)
        {
            _queue.Enqueue(pancakes[i]);
        }
    }

    private void SpawnOpponentPlates()
    {
        if (_opponentPlateSpots == null)
        {
            return;
        }

        foreach (Transform spot in _opponentPlateSpots)
        {
            if (spot != null)
            {
                BuildPlate(spot.position, _opponentPlateMaterial, new PancakeRecord(1f, false));
            }
        }
    }

    private void TrySpawnNextPlate()
    {
        if (_readyPlate != null || _spawning || _queue.Count == 0)
        {
            return;
        }

        if (!IsLaunchSpotClear())
        {
            _spotBlockedTime += Time.deltaTime;
            if (_spotBlockedTime < BlockedSpotSpawnSeconds)
            {
                return;
            }
        }

        _spotBlockedTime = 0f;
        StartCoroutine(SpawnReadyPlate(_queue.Dequeue()));
    }

    private IEnumerator SpawnReadyPlate(PancakeRecord record)
    {
        _spawning = true;

        ServedPlate plate = BuildPlate(_launchSpot.position, _teamPlateMaterial, record);
        plate.GetComponent<Rigidbody>().isKinematic = true;
        plate.Settled += OnPlateSettled;
        plate.FellOff += OnPlateFellOff;
        _plates.Add(plate);

        yield return JuiceTweens.PopIn(plate.transform, Vector3.one, PlatePopSeconds);

        _readyPlate = plate;
        _spawning = false;
        UpdateHud();
    }

    private bool IsLaunchSpotClear()
    {
        Vector3 spot = _launchSpot.position;
        foreach (ServedPlate plate in _plates)
        {
            if (plate == null || plate == _readyPlate || plate.IsGone)
            {
                continue;
            }

            Vector3 offset = plate.transform.position - spot;
            offset.y = 0f;
            if (offset.magnitude < LaunchSpotClearance)
            {
                return false;
            }
        }

        return true;
    }

    private ServedPlate BuildPlate(Vector3 position, Material rimMaterial, PancakeRecord record)
    {
        var root = new GameObject("ServingPlate");
        root.transform.position = position;

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.linearDamping = _plateDrag;
        body.angularDamping = 1.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        GameObject rim = PancakeDecorator.CreateVisual(PrimitiveType.Cylinder, "Rim", root.transform, rimMaterial);
        rim.transform.localPosition = new Vector3(0f, RimHeight * 0.5f, 0f);
        rim.transform.localScale = RimScale;
        MeshCollider rimCollider = rim.AddComponent<MeshCollider>();
        rimCollider.sharedMesh = rim.GetComponent<MeshFilter>().sharedMesh;
        rimCollider.convex = true;
        rimCollider.sharedMaterial = _slideMaterial;

        GameObject well = PancakeDecorator.CreateVisual(PrimitiveType.Cylinder, "Well", root.transform, _plateWellMaterial);
        well.transform.localPosition = new Vector3(0f, RimHeight * 0.5f + 0.004f, 0f);
        well.transform.localScale = WellScale;

        if (_pancakePrefab != null)
        {
            GameObject pancake = Instantiate(_pancakePrefab, root.transform);
            pancake.transform.localPosition = new Vector3(0f, RimHeight + PancakeScale.y, 0f);
            pancake.transform.localRotation = Quaternion.identity;
            pancake.transform.localScale = PancakeScale;

            foreach (Collider pancakeCollider in pancake.GetComponentsInChildren<Collider>())
            {
                pancakeCollider.enabled = false;
            }

            WaffleController waffle = pancake.GetComponent<WaffleController>();
            waffle?.SetCharLevel(record.Accuracy, _config, record.Burned);
        }

        Transform toppings = new GameObject("Toppings").transform;
        toppings.SetParent(root.transform, false);
        toppings.localPosition = new Vector3(0f, RimHeight + PancakeScale.y * 2f, 0f);
        bool perfect = !record.Burned && record.Accuracy >= PerfectAccuracy;
        _decorator?.DecorateTopDown(toppings, PancakeRadius, !record.Burned, perfect);

        ServedPlate plate = root.AddComponent<ServedPlate>();
        plate.Init(record, _tableCenter.position, _tableHalfSize);
        return plate;
    }

    private void HandleAiming()
    {
        Pointer pointer = Pointer.current;
        if (_readyPlate == null || pointer == null)
        {
            CancelAim();
            return;
        }

        if (!_aiming)
        {
            if (!pointer.press.wasPressedThisFrame)
            {
                return;
            }

            _aiming = true;
            _pendingPower = 0f;
        }

        Vector3 origin = _readyPlate.transform.position;
        if (TryGetTablePoint(pointer.position.ReadValue(), out Vector3 grab))
        {
            UpdatePendingShot(origin, grab);
            ShowAim(origin, grab);
        }

        if (!pointer.press.isPressed)
        {
            Vector3 direction = _pendingDirection;
            float power = _pendingPower;
            CancelAim();

            if (power >= MinLaunchPower)
            {
                LaunchReadyPlate(direction, power);
            }
        }
    }

    /// <summary>Slingshot aim: the plate flies opposite to where you pull, within the allowed aim cone.</summary>
    private void UpdatePendingShot(Vector3 origin, Vector3 grab)
    {
        Vector3 forward = TableForward;
        Vector3 pull = origin - grab;
        pull.y = 0f;

        if (Vector3.Dot(pull, forward) <= 0f)
        {
            _pendingPower = 0f;
            return;
        }

        float angle = Mathf.Clamp(Vector3.SignedAngle(forward, pull, Vector3.up), -_maxAimAngle, _maxAimAngle);
        _pendingDirection = Quaternion.AngleAxis(angle, Vector3.up) * forward;
        _pendingPower = Mathf.Clamp01(pull.magnitude / _maxDragDistance);
    }

    private void LaunchReadyPlate(Vector3 direction, float power)
    {
        ServedPlate plate = _readyPlate;
        _readyPlate = null;

        float speed = Mathf.Lerp(_minLaunchSpeed, _maxLaunchSpeed, power);
        plate.Launch(direction * speed, Random.Range(-MaxLaunchSpin, MaxLaunchSpin));
        UpdateHud();
    }

    private bool TryGetTablePoint(Vector2 screenPosition, out Vector3 point)
    {
        point = Vector3.zero;
        if (_camera == null)
        {
            return false;
        }

        Ray ray = _camera.ScreenPointToRay(screenPosition);
        var tablePlane = new Plane(Vector3.up, _tableCenter.position);
        if (!tablePlane.Raycast(ray, out float distance))
        {
            return false;
        }

        point = ray.GetPoint(distance);
        return true;
    }

    private void OnPlateSettled(ServedPlate plate)
    {
        if (plate.IsClaimed)
        {
            return;
        }

        Vector3 position = plate.transform.position;
        CustomerZone customer = FindCustomerAt(position);

        if (customer == null)
        {
            if (plate.SettleCount == 1)
            {
                ShowFloatingText(position, "Too short!", NeutralTextColor);
            }

            return;
        }

        if (customer.IsServed)
        {
            ShowFloatingText(position, "Full!", NeutralTextColor);
            return;
        }

        PancakeRecord record = plate.Record;
        bool good = WaffleController.IsCooked(record.Accuracy, record.Burned);
        bool perfect = good && record.Accuracy >= PerfectAccuracy;
        int points = good ? _goodServePoints + (perfect ? _perfectBonus : 0) : -_burnedServePenalty;

        plate.Claim();
        customer.Serve(good);
        _servedCount++;
        _score += points;
        UpdateHud();

        string label = good ? (perfect ? $"+{points} Perfect!" : $"+{points}") : $"{points} Yuck!";
        ShowFloatingText(position, label, good ? GoodTextColor : BadTextColor);
    }

    private void OnPlateFellOff(ServedPlate plate)
    {
        bool good = WaffleController.IsCooked(plate.Record.Accuracy, plate.Record.Burned);
        Vector3 edge = plate.transform.position;
        edge.y = _tableCenter.position.y;
        ShowFloatingText(edge, good ? "Wasted!" : "Tossed!", NeutralTextColor);
    }

    private CustomerZone FindCustomerAt(Vector3 position)
    {
        if (_customers == null)
        {
            return null;
        }

        foreach (CustomerZone customer in _customers)
        {
            if (customer != null && customer.Contains(position))
            {
                return customer;
            }
        }

        return null;
    }

    private void UpdateHud()
    {
        _hud?.SetPlatesLeft(PlatesLeft);
        _hud?.SetScore(_score);
    }

    private void ShowResults()
    {
        int customerCount = _customers != null ? _customers.Length : 0;
        string results = "Game Over!\n";

        PlayerSession session = PancakeCarryover.Session;
        if (session != null)
        {
            int accuracyPct = Mathf.RoundToInt(session.CumulativeAccuracy * 100f);
            results += $"Waffles Made: {session.TotalWaffles}\nCook Accuracy: {accuracyPct}%\n";
        }

        results += $"Customers Served: {_servedCount}/{customerCount}\nServing Score: {_score}";
        _hud?.ShowMessage(results);
    }

    private void CreateAimVisuals()
    {
        _aimMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        _aimMaterial.SetColor(BaseColorId, _aimColor);
        _aimArrow = CreateLine("AimArrow", 5, 0.16f);
        _aimBand = CreateLine("AimBand", 2, 0.05f);
    }

    private LineRenderer CreateLine(string lineName, int points, float width)
    {
        var lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = points;
        line.widthMultiplier = width;
        line.sharedMaterial = _aimMaterial;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.enabled = false;
        return line;
    }

    private void ShowAim(Vector3 origin, Vector3 grab)
    {
        Vector3 lift = Vector3.up * AimLift;
        Vector3 start = origin + lift;

        _aimBand.enabled = true;
        _aimBand.SetPosition(0, start);
        _aimBand.SetPosition(1, new Vector3(grab.x, origin.y, grab.z) + lift);

        bool hasShot = _pendingPower > 0f;
        _aimArrow.enabled = hasShot;
        if (!hasShot)
        {
            return;
        }

        Vector3 tip = start + _pendingDirection * (1f + _pendingPower * 4f);
        Vector3 back = -_pendingDirection * 0.6f;
        Vector3 side = Vector3.Cross(Vector3.up, _pendingDirection) * 0.45f;

        _aimArrow.SetPosition(0, start);
        _aimArrow.SetPosition(1, tip);
        _aimArrow.SetPosition(2, tip + back + side);
        _aimArrow.SetPosition(3, tip);
        _aimArrow.SetPosition(4, tip + back - side);
        _aimMaterial.SetColor(BaseColorId, Color.Lerp(Color.white, _aimColor, _pendingPower));
    }

    private void CancelAim()
    {
        _aiming = false;
        _pendingPower = 0f;

        if (_aimArrow != null)
        {
            _aimArrow.enabled = false;
        }

        if (_aimBand != null)
        {
            _aimBand.enabled = false;
        }
    }

    private void ShowFloatingText(Vector3 position, string message, Color color)
    {
        // Add the text first: it swaps the Transform for a RectTransform.
        var textObject = new GameObject("FloatingText");
        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        textObject.transform.position = position + Vector3.up * 0.8f;
        if (_camera != null)
        {
            textObject.transform.rotation = _camera.transform.rotation;
        }

        text.text = message;
        text.fontSize = FloatingTextSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.rectTransform.sizeDelta = new Vector2(10f, 3f);

        StartCoroutine(RiseAndFade(text, color));
    }

    private IEnumerator RiseAndFade(TextMeshPro text, Color color)
    {
        Vector3 start = text.transform.position;
        float elapsed = 0f;

        while (elapsed < FloatingTextSeconds && text != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / FloatingTextSeconds);
            text.transform.position = start + Vector3.up * (t * 1.5f);
            text.transform.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.3f);
            text.color = new Color(color.r, color.g, color.b, 1f - t * t);
            yield return null;
        }

        if (text != null)
        {
            Destroy(text.gameObject);
        }
    }
}
