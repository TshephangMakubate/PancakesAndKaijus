using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Round 3: every player runs their own serving station and slides pancake
/// plates down the table to the customers, so each team has two plates in play
/// at once. Customers eat, leave, and are replaced, so there is always someone
/// to serve until the clock or the pancakes run out.
/// <para>
/// Each player uses their one button from the cooking round: the aim arrow
/// swings on its own, holding the button charges the slide's power, and
/// releasing it lets the plate go. A mouse drag still aims the nearest ready
/// plate, which is handy for testing on your own.
/// </para>
/// </summary>
public class ServingManager : MonoBehaviour
{
    private const int TeamCount = 2;
    private const int PlayersPerTeam = 2;
    private const int StationCount = TeamCount * PlayersPerTeam;

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
    private const float KeyLabelSize = 9f;
    private const float KeyLabelOffset = 1.35f;
    private const float AimLift = 0.35f;
    private const float IdleArrowLength = 1.6f;
    private const float MaxLaunchSpin = 4f;
    private const string TitleMessage = "Round 3\nServe your customers!";
    private const string GoMessage = "GO!";
    private const string TimesUpMessage = "Time's up!";
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
    private static readonly string[] TeamNames = { "Red", "Blue" };
    private static readonly Color[] TeamColors = { new Color(0.9f, 0.25f, 0.3f), new Color(0.3f, 0.45f, 0.95f) };

    [Header("Config & Presentation")]
    [SerializeField] private GameConfig _config;
    [Tooltip("Supplies each player's button and which teams are playing.")]
    [SerializeField] private SequenceConfig _sequenceConfig;
    [SerializeField] private HUDController _hud;
    [SerializeField] private PancakeDecorator _decorator;

    [Header("Table")]
    [Tooltip("One station per player, in order: Red P1, Red P2, Blue P1, Blue P2.")]
    [SerializeField] private Transform[] _launchSpots;
    [Tooltip("Center of the table top; its forward (Z) points at the customers.")]
    [SerializeField] private Transform _tableCenter;
    [Tooltip("Half the table's width (x) and length (z).")]
    [SerializeField] private Vector2 _tableHalfSize = new Vector2(7f, 12f);
    [SerializeField] private CustomerZone[] _customers;

    [Header("Plates")]
    [SerializeField] private GameObject _pancakePrefab;
    [FormerlySerializedAs("_teamPlateMaterial")]
    [SerializeField] private Material _redPlateMaterial;
    [FormerlySerializedAs("_opponentPlateMaterial")]
    [SerializeField] private Material _bluePlateMaterial;
    [SerializeField] private Material _plateWellMaterial;
    [SerializeField] private float _plateFriction = 0.12f;
    [SerializeField] private float _plateBounciness = 0.35f;
    [Tooltip("Air-hockey style slow-down; higher stops plates sooner.")]
    [SerializeField] private float _plateDrag = 0.6f;

    [Header("Button Aiming")]
    [Tooltip("How far left or right of straight ahead the swinging arrow reaches, in degrees.")]
    [SerializeField] private float _buttonAimAngle = 40f;
    [Tooltip("How fast the arrow swings, in radians per second.")]
    [SerializeField] private float _aimSwingSpeed = 2.2f;
    [Tooltip("Seconds for the power to charge from empty to full while the button is held. It then falls back down.")]
    [SerializeField, Min(0.1f)] private float _chargeSeconds = 1.1f;
    [Tooltip("Releasing with less power than this (0-1) cancels the shot so the player can aim again, instead of dribbling the plate forward.")]
    [SerializeField, Range(0f, 0.5f)] private float _cancelPower = 0.15f;

    [Header("Mouse Aiming")]
    [Tooltip("Pull-back distance (world units) for a full-power slide.")]
    [SerializeField] private float _maxDragDistance = 3.5f;
    [Tooltip("How far left or right of straight ahead you can aim, in degrees.")]
    [SerializeField] private float _maxAimAngle = 70f;

    [Header("Slide")]
    [SerializeField] private float _minLaunchSpeed = 4f;
    [SerializeField] private float _maxLaunchSpeed = 22f;

    [Header("Scoring")]
    [SerializeField] private int _goodServePoints = 100;
    [SerializeField] private int _perfectBonus = 50;
    [SerializeField] private int _burnedServePenalty = 50;

    [Header("Testing")]
    [Tooltip("Plates per team when a team has no pancakes carried over from cooking (every third one burned).")]
    [SerializeField] private int _fallbackPlateCount = 6;

    /// <summary>One player's serving station and the plate waiting on it.</summary>
    private sealed class Station
    {
        public int Index;
        public int Team;
        public Transform Spot;
        public ServedPlate Ready;
        public bool Spawning;
        public float BlockedTime;

        public float SwingPhase;
        public bool Charging;
        public float ChargeTime;
        public bool WasPressed;
        public bool MouseAiming;

        public Vector3 Direction = Vector3.forward;
        public float Power;

        public Material AimMaterial;
        public LineRenderer Arrow;
        public LineRenderer Band;
    }

    private readonly Queue<PancakeRecord>[] _queues = { new Queue<PancakeRecord>(), new Queue<PancakeRecord>() };
    private readonly bool[] _teamActive = new bool[TeamCount];
    private readonly int[] _scores = new int[TeamCount];
    private readonly int[] _servedCounts = new int[TeamCount];
    private readonly List<Station> _stations = new List<Station>();
    private readonly List<ServedPlate> _plates = new List<ServedPlate>();

    private Camera _camera;
    private PhysicsMaterial _slideMaterial;
    private SequenceInputBinder _input;
    private Station _mouseStation;

    private bool _roundActive;
    private float _roundTimer;

    private Vector3 TableForward
    {
        get
        {
            Vector3 forward = _tableCenter.forward;
            forward.y = 0f;
            return forward.normalized;
        }
    }

    private bool AnyPlatesLeft
    {
        get
        {
            for (int team = 0; team < TeamCount; team++)
            {
                if (_teamActive[team] && PlatesLeft(team) > 0)
                {
                    return true;
                }
            }

            return false;
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

        StartCoroutine(RunServing());
    }

    private void OnDestroy()
    {
        _input?.Dispose();
        _input = null;

        if (_customers == null)
        {
            return;
        }

        foreach (CustomerZone customer in _customers)
        {
            if (customer != null)
            {
                customer.BecameAvailable -= OnCustomerAvailable;
            }
        }
    }

    private void Update()
    {
        if (!_roundActive)
        {
            return;
        }

        _roundTimer = Mathf.Max(0f, _roundTimer - Time.deltaTime);
        _hud?.SetTimer(_roundTimer);

        foreach (Station station in _stations)
        {
            TrySpawnNextPlate(station);
            UpdateButtonAim(station);
        }

        HandleMouseAiming();
    }

    private IEnumerator RunServing()
    {
        if (_config == null || _tableCenter == null || _launchSpots == null || _launchSpots.Length < StationCount)
        {
            Debug.LogError("ServingManager: Config, table center and four launch spots must be assigned. Re-run Waffle Party > Create Serving Scene.");
            yield break;
        }

        if (_sequenceConfig == null)
        {
            Debug.LogWarning("ServingManager: No SequenceConfig assigned, so only gamepads (and the mouse) can serve.");
        }

        _teamActive[0] = _sequenceConfig == null || _sequenceConfig.IsTeamActive(0);
        _teamActive[1] = _sequenceConfig != null && _sequenceConfig.IsTeamActive(1);

        BuildStations();
        FillQueues();

        if (_customers != null)
        {
            foreach (CustomerZone customer in _customers)
            {
                if (customer != null)
                {
                    customer.BecameAvailable += OnCustomerAvailable;
                }
            }
        }

        _input = new SequenceInputBinder(_sequenceConfig);

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
        _input.Enable();

        while (_roundTimer > 0f && (AnyPlatesLeft || AnyPlateSliding))
        {
            yield return null;
        }

        _roundActive = false;
        _input.Disable();
        CancelAllAims();

        _hud?.ShowBanner(_roundTimer <= 0f ? TimesUpMessage : OutOfPlatesMessage);

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

    /// <summary>
    /// Sets up a station per player on an active team. A team sitting out
    /// leaves idle plates on its pads instead: obstacles that can be knocked
    /// about but never score.
    /// </summary>
    private void BuildStations()
    {
        _stations.Clear();

        for (int i = 0; i < StationCount; i++)
        {
            Transform spot = _launchSpots[i];
            int team = i / PlayersPerTeam;
            if (spot == null)
            {
                continue;
            }

            if (!_teamActive[team])
            {
                BuildPlate(spot.position, PlateMaterial(team), new PancakeRecord(1f, false), -1);
                continue;
            }

            var station = new Station
            {
                Index = i,
                Team = team,
                Spot = spot,
                // Offset the swings so teammates' arrows don't move in lockstep.
                SwingPhase = i * 1.3f
            };

            station.AimMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            station.AimMaterial.SetColor(BaseColorId, TeamColors[team]);
            station.Arrow = CreateLine($"AimArrow{i + 1}", 5, 0.16f, station.AimMaterial);
            station.Band = CreateLine($"AimBand{i + 1}", 2, 0.05f, station.AimMaterial);

            CreateKeyLabel(station);
            _stations.Add(station);
        }
    }

    private void FillQueues()
    {
        for (int team = 0; team < TeamCount; team++)
        {
            _queues[team].Clear();
            if (!_teamActive[team])
            {
                continue;
            }

            // Only team A cooks in this build, so the carried-over stack is theirs.
            if (team == 0 && PancakeCarryover.HasData)
            {
                IReadOnlyList<PancakeRecord> pancakes = PancakeCarryover.Pancakes;
                for (int i = pancakes.Count - 1; i >= 0; i--)
                {
                    _queues[team].Enqueue(pancakes[i]);
                }

                continue;
            }

            Debug.Log($"ServingManager: No pancakes carried over for team {TeamNames[team]}; using {_fallbackPlateCount} test plates.");
            for (int i = 0; i < _fallbackPlateCount; i++)
            {
                _queues[team].Enqueue(new PancakeRecord(1f, i % 3 == 2));
            }
        }
    }

    private int PlatesLeft(int team)
    {
        int count = _queues[team].Count;
        foreach (Station station in _stations)
        {
            if (station.Team == team && (station.Ready != null || station.Spawning))
            {
                count++;
            }
        }

        return count;
    }

    private Material PlateMaterial(int team)
    {
        return team == 0 ? _redPlateMaterial : _bluePlateMaterial;
    }

    private void TrySpawnNextPlate(Station station)
    {
        if (station.Ready != null || station.Spawning || _queues[station.Team].Count == 0)
        {
            return;
        }

        if (!IsSpotClear(station.Spot.position))
        {
            station.BlockedTime += Time.deltaTime;
            if (station.BlockedTime < BlockedSpotSpawnSeconds)
            {
                return;
            }
        }

        station.BlockedTime = 0f;
        StartCoroutine(SpawnReadyPlate(station, _queues[station.Team].Dequeue()));
    }

    private IEnumerator SpawnReadyPlate(Station station, PancakeRecord record)
    {
        station.Spawning = true;
        _plates.RemoveAll(p => p == null);

        ServedPlate plate = BuildPlate(station.Spot.position, PlateMaterial(station.Team), record, station.Team);
        plate.GetComponent<Rigidbody>().isKinematic = true;
        plate.Settled += OnPlateSettled;
        plate.FellOff += OnPlateFellOff;
        _plates.Add(plate);

        yield return JuiceTweens.PopIn(plate.transform, Vector3.one, PlatePopSeconds);

        station.Ready = plate;
        station.Spawning = false;
        UpdateHud();
    }

    private bool IsSpotClear(Vector3 spot)
    {
        foreach (ServedPlate plate in _plates)
        {
            if (plate == null || plate.IsGone || IsReadyPlate(plate))
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

    private bool IsReadyPlate(ServedPlate plate)
    {
        foreach (Station station in _stations)
        {
            if (station.Ready == plate)
            {
                return true;
            }
        }

        return false;
    }

    private ServedPlate BuildPlate(Vector3 position, Material rimMaterial, PancakeRecord record, int team)
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
        plate.Init(record, _tableCenter.position, _tableHalfSize, team);
        return plate;
    }

    /// <summary>
    /// One-button aiming: the arrow swings until the player presses, then the
    /// direction locks and power rises and falls while the button is held.
    /// Letting go slides the plate.
    /// </summary>
    private void UpdateButtonAim(Station station)
    {
        PlayerButtonInput input = _input?.Player(station.Index);
        bool pressed = input != null && input.IsPressed;
        bool pressedThisFrame = pressed && !station.WasPressed;
        station.WasPressed = pressed;

        if (station.MouseAiming)
        {
            return;
        }

        if (station.Ready == null)
        {
            station.Charging = false;
            HideAim(station);
            return;
        }

        if (!station.Charging)
        {
            station.SwingPhase += Time.deltaTime * _aimSwingSpeed;
            float angle = Mathf.Sin(station.SwingPhase) * _buttonAimAngle;
            station.Direction = Quaternion.AngleAxis(angle, Vector3.up) * TableForward;
            station.Power = 0f;

            if (pressedThisFrame)
            {
                station.Charging = true;
                station.ChargeTime = 0f;
            }
        }

        if (station.Charging)
        {
            station.ChargeTime += Time.deltaTime;
            station.Power = Mathf.PingPong(station.ChargeTime / _chargeSeconds, 1f);

            if (!pressed)
            {
                station.Charging = false;

                // Letting go while the arrow is tiny calls the shot off, and the
                // arrow picks up swinging from where it was locked.
                if (station.Power < _cancelPower)
                {
                    station.Power = 0f;
                }
                else
                {
                    LaunchReadyPlate(station, station.Direction, station.Power);
                    HideAim(station);
                    return;
                }
            }
        }

        ShowArrow(station, station.Ready.transform.position);
    }

    /// <summary>Slingshot drag with the mouse, grabbing whichever ready plate is nearest the click.</summary>
    private void HandleMouseAiming()
    {
        Pointer pointer = Pointer.current;
        if (pointer == null)
        {
            return;
        }

        if (_mouseStation == null)
        {
            if (!pointer.press.wasPressedThisFrame || !TryGetTablePoint(pointer.position.ReadValue(), out Vector3 click))
            {
                return;
            }

            _mouseStation = NearestReadyStation(click);
            if (_mouseStation == null)
            {
                return;
            }

            _mouseStation.MouseAiming = true;
            _mouseStation.Power = 0f;
        }

        Station station = _mouseStation;
        if (station.Ready == null)
        {
            EndMouseAim();
            return;
        }

        Vector3 origin = station.Ready.transform.position;
        if (TryGetTablePoint(pointer.position.ReadValue(), out Vector3 grab))
        {
            UpdatePendingShot(station, origin, grab);
            ShowArrow(station, origin);
            ShowBand(station, origin, grab);
        }

        if (!pointer.press.isPressed)
        {
            Vector3 direction = station.Direction;
            float power = station.Power;
            EndMouseAim();

            if (power >= MinLaunchPower)
            {
                LaunchReadyPlate(station, direction, power);
            }
        }
    }

    private Station NearestReadyStation(Vector3 point)
    {
        Station nearest = null;
        float best = float.MaxValue;

        foreach (Station station in _stations)
        {
            if (station.Ready == null || station.Charging)
            {
                continue;
            }

            float distance = (station.Ready.transform.position - point).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = station;
            }
        }

        return nearest;
    }

    private void EndMouseAim()
    {
        if (_mouseStation != null)
        {
            _mouseStation.MouseAiming = false;
            _mouseStation.Power = 0f;
            HideAim(_mouseStation);
        }

        _mouseStation = null;
    }

    /// <summary>Slingshot aim: the plate flies opposite to where you pull, within the allowed aim cone.</summary>
    private void UpdatePendingShot(Station station, Vector3 origin, Vector3 grab)
    {
        Vector3 forward = TableForward;
        Vector3 pull = origin - grab;
        pull.y = 0f;

        if (Vector3.Dot(pull, forward) <= 0f)
        {
            station.Power = 0f;
            return;
        }

        float angle = Mathf.Clamp(Vector3.SignedAngle(forward, pull, Vector3.up), -_maxAimAngle, _maxAimAngle);
        station.Direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
        station.Power = Mathf.Clamp01(pull.magnitude / _maxDragDistance);
    }

    private void LaunchReadyPlate(Station station, Vector3 direction, float power)
    {
        ServedPlate plate = station.Ready;
        station.Ready = null;

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
        if (plate.IsClaimed || plate.TeamIndex < 0)
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

        if (!customer.IsAvailable)
        {
            // It waits there; the next customer to sit down takes it.
            ShowFloatingText(position, "Busy!", NeutralTextColor);
            return;
        }

        ServeTo(plate, customer);
    }

    /// <summary>A freshly seated customer takes any plate already waiting in their spot.</summary>
    private void OnCustomerAvailable(CustomerZone customer)
    {
        foreach (ServedPlate plate in _plates)
        {
            if (plate != null && plate.TeamIndex >= 0 && plate.IsLaunched && plate.IsSettled
                && !plate.IsClaimed && !plate.IsGone && customer.Contains(plate.transform.position))
            {
                ServeTo(plate, customer);
                return;
            }
        }
    }

    private void ServeTo(ServedPlate plate, CustomerZone customer)
    {
        PancakeRecord record = plate.Record;
        bool good = WaffleController.IsCooked(record.Accuracy, record.Burned);
        bool perfect = good && record.Accuracy >= PerfectAccuracy;
        int points = good ? _goodServePoints + (perfect ? _perfectBonus : 0) : -_burnedServePenalty;

        plate.Claim();
        customer.Serve(good, plate);

        int team = plate.TeamIndex;
        _servedCounts[team]++;
        _scores[team] += points;
        UpdateHud();

        Vector3 position = plate.transform.position;
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
        for (int team = 0; team < TeamCount; team++)
        {
            if (_teamActive[team])
            {
                _hud?.SetTeamServing(team, TeamNames[team], _scores[team], PlatesLeft(team));
            }
        }
    }

    private void ShowResults()
    {
        string results = "Game Over!\n";

        PlayerSession session = PancakeCarryover.Session;
        if (session != null)
        {
            int accuracyPct = Mathf.RoundToInt(session.CumulativeAccuracy * 100f);
            results += $"Waffles Made: {session.TotalWaffles}\nCook Accuracy: {accuracyPct}%\n";
        }

        for (int team = 0; team < TeamCount; team++)
        {
            if (_teamActive[team])
            {
                results += $"{TeamNames[team]}: {_servedCounts[team]} served, {_scores[team]} pts\n";
            }
        }

        if (_teamActive[0] && _teamActive[1])
        {
            results += _scores[0] == _scores[1]
                ? "It's a tie!"
                : $"{TeamNames[_scores[0] > _scores[1] ? 0 : 1]} team wins!";
        }

        _hud?.ShowMessage(results.TrimEnd('\n'));
    }

    private LineRenderer CreateLine(string lineName, int points, float width, Material material)
    {
        var lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = points;
        line.widthMultiplier = width;
        line.sharedMaterial = material;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.enabled = false;
        return line;
    }

    /// <summary>The direction arrow, growing and deepening in colour with power.</summary>
    private void ShowArrow(Station station, Vector3 origin)
    {
        Vector3 start = origin + Vector3.up * AimLift;
        Vector3 direction = station.Direction;
        float length = IdleArrowLength + station.Power * 4f;

        Vector3 tip = start + direction * length;
        Vector3 back = -direction * 0.6f;
        Vector3 side = Vector3.Cross(Vector3.up, direction) * 0.45f;

        station.Arrow.enabled = true;
        station.Arrow.SetPosition(0, start);
        station.Arrow.SetPosition(1, tip);
        station.Arrow.SetPosition(2, tip + back + side);
        station.Arrow.SetPosition(3, tip);
        station.Arrow.SetPosition(4, tip + back - side);
        station.AimMaterial.SetColor(BaseColorId, Color.Lerp(Color.white, TeamColors[station.Team], 0.35f + station.Power * 0.65f));
    }

    /// <summary>The rubber band from the plate back to the mouse.</summary>
    private static void ShowBand(Station station, Vector3 origin, Vector3 grab)
    {
        Vector3 lift = Vector3.up * AimLift;
        station.Band.enabled = true;
        station.Band.SetPosition(0, origin + lift);
        station.Band.SetPosition(1, new Vector3(grab.x, origin.y, grab.z) + lift);
    }

    private static void HideAim(Station station)
    {
        if (station.Arrow != null)
        {
            station.Arrow.enabled = false;
        }

        if (station.Band != null)
        {
            station.Band.enabled = false;
        }
    }

    private void CancelAllAims()
    {
        EndMouseAim();
        foreach (Station station in _stations)
        {
            station.Charging = false;
            station.Power = 0f;
            HideAim(station);
        }
    }

    /// <summary>Paints the player's button in front of their pad, in their team colour.</summary>
    private void CreateKeyLabel(Station station)
    {
        string key = _sequenceConfig != null ? _sequenceConfig.KeyFor(station.Index) : string.Empty;
        int slash = key.LastIndexOf('/');
        string label = slash >= 0 ? key.Substring(slash + 1).ToUpperInvariant() : key.ToUpperInvariant();
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        var labelObject = new GameObject($"KeyLabel{station.Index + 1}");
        TextMeshPro text = labelObject.AddComponent<TextMeshPro>();
        labelObject.transform.position = station.Spot.position - TableForward * KeyLabelOffset + Vector3.up * 0.03f;
        labelObject.transform.rotation = Quaternion.LookRotation(Vector3.down, TableForward);

        text.text = label;
        text.fontSize = KeyLabelSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = TeamColors[station.Team];
        text.rectTransform.sizeDelta = new Vector2(3f, 1.5f);
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
