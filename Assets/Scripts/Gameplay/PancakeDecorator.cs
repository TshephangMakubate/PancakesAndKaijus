using UnityEngine;

/// <summary>A plated pancake's size in its jiggle space: centered on X, resting on Y = 0, facing the camera down -Z.</summary>
public readonly struct PancakeFrame
{
    public readonly float HalfWidth;
    public readonly float Height;
    public readonly float HalfDepth;

    public PancakeFrame(float halfWidth, float height, float halfDepth)
    {
        HalfWidth = halfWidth;
        Height = height;
        HalfDepth = halfDepth;
    }
}

/// <summary>Builds whipped cream, sprinkles, and cherry toppings from primitives: tidy for correct presses, haphazard for mistakes.</summary>
public class PancakeDecorator : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private const string LitShaderName = "Universal Render Pipeline/Lit";
    private const string UnlitShaderName = "Universal Render Pipeline/Unlit";
    private const float PopSeconds = 0.18f;
    private const float CherryPopSeconds = 0.3f;
    private const float JumbleSeconds = 0.22f;
    private const int NeatSprinkleCount = 6;
    private const int MessySprinkleCount = 9;
    private const int TopDownSprinkleCount = 10;

    [Header("Colors")]
    [SerializeField] private Color _creamColor = new Color(1f, 0.98f, 0.94f);
    [SerializeField] private Color[] _sprinkleColors =
    {
        new Color(1f, 0.45f, 0.7f),
        new Color(0.35f, 0.7f, 1f),
        new Color(1f, 0.88f, 0.3f),
        new Color(0.45f, 0.9f, 0.5f),
        new Color(0.7f, 0.5f, 1f)
    };
    [SerializeField] private Color _cherryColor = new Color(0.85f, 0.08f, 0.15f);
    [SerializeField] private Color _stemColor = new Color(0.25f, 0.45f, 0.15f);

    [Header("Sizes")]
    [SerializeField] private float _creamSize = 0.34f;
    [SerializeField] private float _sprinkleLength = 0.18f;
    [SerializeField] private float _cherrySize = 0.24f;

    [Header("Mess")]
    [Tooltip("How far a flubbed topping can land from where it belongs.")]
    [SerializeField] private float _messScatter = 0.6f;
    [Tooltip("How hard every topping gets knocked askew when the sequence is messed up.")]
    [SerializeField] private float _jumbleStrength = 1f;

    private Material _creamMaterial;
    private Material[] _sprinkleMaterials;
    private Material _cherryMaterial;
    private Material _stemMaterial;

    private void Awake()
    {
        _creamMaterial = CreateMaterial(_creamColor);
        _cherryMaterial = CreateMaterial(_cherryColor);
        _stemMaterial = CreateMaterial(_stemColor);

        int colorCount = _sprinkleColors != null && _sprinkleColors.Length > 0 ? _sprinkleColors.Length : 1;
        _sprinkleMaterials = new Material[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            _sprinkleMaterials[i] = CreateMaterial(_sprinkleColors != null && i < _sprinkleColors.Length ? _sprinkleColors[i] : Color.white);
        }
    }

    /// <summary>Adds the topping for one sequence step: cream on even steps, sprinkles on odd, neat or messy.</summary>
    public void AddPiece(Transform root, PancakeFrame frame, int step, int stepCount, bool neat)
    {
        if (root == null)
        {
            return;
        }

        float slotWidth = frame.HalfWidth * 1.6f / Mathf.Max(1, stepCount);
        float slotX = -frame.HalfWidth * 0.8f + (step + 0.5f) * slotWidth;

        if (step % 2 == 0)
        {
            if (neat)
            {
                AddNeatCream(root, frame, slotX);
            }
            else
            {
                AddMessyCream(root, frame, slotX);
            }
        }
        else if (neat)
        {
            AddNeatSprinkles(root, frame, slotX, step);
        }
        else
        {
            AddMessySprinkles(root, frame, slotX);
        }
    }

    /// <summary>Crowns a perfectly decorated pancake with a cherry.</summary>
    public void AddCherry(Transform root, PancakeFrame frame)
    {
        if (root == null)
        {
            return;
        }

        float top = frame.Height + _creamSize * 0.95f;
        Spawn(root, PrimitiveType.Sphere, _cherryMaterial, new Vector3(0f, top + _cherrySize * 0.4f, 0f), Quaternion.identity, Vector3.one * _cherrySize, CherryPopSeconds);
        Spawn(root, PrimitiveType.Capsule, _stemMaterial, new Vector3(0.03f, top + _cherrySize * 1.05f, 0f), Quaternion.Euler(0f, 0f, -20f), new Vector3(0.035f, 0.1f, 0.035f), CherryPopSeconds);
    }

    /// <summary>Knocks every topping askew; <paramref name="messiness"/> in [0, 1] scales how far.</summary>
    public void Jumble(Transform root, float messiness)
    {
        if (root == null)
        {
            return;
        }

        float amount = Mathf.Clamp01(messiness) * _jumbleStrength;
        foreach (Transform piece in root)
        {
            Vector3 position = piece.localPosition + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.12f, 0.06f), 0f) * amount;
            Quaternion rotation = piece.localRotation * Quaternion.Euler(0f, 0f, Random.Range(-45f, 45f) * amount);
            StartCoroutine(JuiceTweens.MoveLocal(piece, position, rotation, JumbleSeconds));
        }
    }

    /// <summary>Adds an upright, undistorted toppings root at the pancake's base so toppings ride along as it flips.</summary>
    public Transform CreateToppingsRoot(Transform pancake, out PancakeFrame frame)
    {
        Renderer pancakeRenderer = pancake.GetComponent<Renderer>();
        Bounds bounds = pancakeRenderer != null ? pancakeRenderer.bounds : new Bounds(pancake.position, new Vector3(2.3f, 0.6f, 0.8f));
        frame = new PancakeFrame(bounds.extents.x, bounds.extents.y * 2f, bounds.extents.z);

        // Cancels the pancake's flattened scale so round toppings stay round.
        Transform mount = new GameObject("ToppingMount").transform;
        mount.SetParent(pancake, false);
        Vector3 scale = pancake.localScale;
        mount.localScale = new Vector3(SafeInverse(scale.x), SafeInverse(scale.y), SafeInverse(scale.z));

        Transform toppings = new GameObject("Toppings").transform;
        toppings.SetParent(mount, false);
        toppings.SetPositionAndRotation(bounds.center - Vector3.up * bounds.extents.y, Quaternion.identity);
        return toppings;
    }

    /// <summary>Tops a pancake lying flat (seen from above): a tidy swirl ringed with sprinkles, or a haphazard mess.</summary>
    public void DecorateTopDown(Transform root, float radius, bool neat, bool cherry)
    {
        if (root == null)
        {
            return;
        }

        if (!neat)
        {
            AddMessyTopDown(root, radius);
            return;
        }

        Vector3 swirl = new Vector3(0f, _creamSize * 0.3f, 0f);
        Spawn(root, PrimitiveType.Sphere, _creamMaterial, swirl, Quaternion.identity, new Vector3(1f, 0.7f, 1f) * _creamSize, PopSeconds);
        Spawn(root, PrimitiveType.Sphere, _creamMaterial, swirl + Vector3.up * (_creamSize * 0.35f), Quaternion.identity, new Vector3(0.72f, 0.55f, 0.72f) * _creamSize, PopSeconds);
        Spawn(root, PrimitiveType.Sphere, _creamMaterial, swirl + Vector3.up * (_creamSize * 0.62f), Quaternion.identity, new Vector3(0.38f, 0.45f, 0.38f) * _creamSize, PopSeconds);

        for (int i = 0; i < TopDownSprinkleCount; i++)
        {
            float angle = i * 360f / TopDownSprinkleCount;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0.02f, radius * 0.65f);
            Spawn(root, PrimitiveType.Capsule, _sprinkleMaterials[i % _sprinkleMaterials.Length], position, Quaternion.Euler(90f, angle + 90f, 0f), SprinkleScale(), PopSeconds);
        }

        if (cherry)
        {
            AddCherry(root, new PancakeFrame(radius, 0f, radius));
        }
    }

    /// <summary>Creates a collider-free primitive under <paramref name="parent"/> with the given material.</summary>
    public static GameObject CreateVisual(PrimitiveType type, string name, Transform parent, Material material)
    {
        GameObject visual = GameObject.CreatePrimitive(type);
        visual.name = name;

        // Disabled right away because Destroy only lands at the end of the frame.
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        visual.transform.SetParent(parent, false);

        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        return visual;
    }

    private void AddNeatCream(Transform root, PancakeFrame frame, float slotX)
    {
        // A tidy three-tier soft-serve swirl sitting squarely on top.
        Vector3 basePosition = new Vector3(slotX, frame.Height + _creamSize * 0.3f, 0f);
        Spawn(root, PrimitiveType.Sphere, _creamMaterial, basePosition, Quaternion.identity, new Vector3(1f, 0.7f, 1f) * _creamSize, PopSeconds);
        Spawn(root, PrimitiveType.Sphere, _creamMaterial, basePosition + Vector3.up * (_creamSize * 0.35f), Quaternion.identity, new Vector3(0.72f, 0.55f, 0.72f) * _creamSize, PopSeconds);
        Spawn(root, PrimitiveType.Sphere, _creamMaterial, basePosition + Vector3.up * (_creamSize * 0.62f), Quaternion.identity, new Vector3(0.38f, 0.45f, 0.38f) * _creamSize, PopSeconds);
    }

    private void AddMessyCream(Transform root, PancakeFrame frame, float slotX)
    {
        // A lopsided splat that may slide off the edge, plus a drip down the front.
        float x = slotX + Random.Range(-_messScatter, _messScatter);
        bool slidOff = Mathf.Abs(x) > frame.HalfWidth * 0.9f;
        float y = slidOff ? Random.Range(0.05f, frame.Height * 0.6f) : frame.Height + _creamSize * 0.1f;
        float size = _creamSize * Random.Range(0.8f, 1.3f);

        Spawn(root, PrimitiveType.Sphere, _creamMaterial, new Vector3(x, y, -frame.HalfDepth * Random.Range(0f, 0.8f)),
            Quaternion.Euler(0f, 0f, Random.Range(-40f, 40f)), new Vector3(1.3f, 0.45f, 1f) * size, PopSeconds);
        Spawn(root, PrimitiveType.Sphere, _creamMaterial, new Vector3(x + Random.Range(-0.1f, 0.1f), Mathf.Max(0.02f, y - size * 0.45f), -frame.HalfDepth - 0.02f),
            Quaternion.identity, new Vector3(0.35f, 0.6f, 0.35f) * size, PopSeconds);
    }

    private void AddNeatSprinkles(Transform root, PancakeFrame frame, float slotX, int step)
    {
        // Two tidy rows across the front face, alternating tilt.
        float z = -frame.HalfDepth - 0.02f;
        for (int i = 0; i < NeatSprinkleCount; i++)
        {
            int column = i % 3;
            int row = i / 3;
            Vector3 position = new Vector3(
                slotX + (column - 1) * _sprinkleLength * 0.8f + row * _sprinkleLength * 0.4f,
                frame.Height * (0.62f - row * 0.28f),
                z);
            Quaternion rotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 35f : -35f);
            Spawn(root, PrimitiveType.Capsule, _sprinkleMaterials[(step + i) % _sprinkleMaterials.Length], position, rotation, SprinkleScale(), PopSeconds);
        }
    }

    private void AddMessySprinkles(Transform root, PancakeFrame frame, float slotX)
    {
        // Flung everywhere: some miss the pancake and land on the plate.
        float z = -frame.HalfDepth - 0.02f;
        for (int i = 0; i < MessySprinkleCount; i++)
        {
            Vector3 position = new Vector3(
                slotX + Random.Range(-_messScatter, _messScatter),
                Random.Range(-0.25f, frame.Height + 0.25f),
                z);
            Quaternion rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            Material material = _sprinkleMaterials[Random.Range(0, _sprinkleMaterials.Length)];
            Spawn(root, PrimitiveType.Capsule, material, position, rotation, SprinkleScale(), PopSeconds);
        }
    }

    private void AddMessyTopDown(Transform root, float radius)
    {
        // Splats and sprinkles land wherever; anything past the pancake's edge drops to the plate below.
        const float plateDrop = -0.12f;

        for (int i = 0; i < 2; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius * 1.1f;
            float size = _creamSize * Random.Range(0.7f, 1.2f);
            float y = offset.magnitude > radius ? plateDrop : size * 0.15f;
            Quaternion tilt = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
            Spawn(root, PrimitiveType.Sphere, _creamMaterial, new Vector3(offset.x, y, offset.y), tilt, new Vector3(1.4f, 0.4f, 1.1f) * size, PopSeconds);
        }

        for (int i = 0; i < MessySprinkleCount + 3; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius * 1.35f;
            float y = offset.magnitude > radius ? plateDrop : 0.02f;
            Material material = _sprinkleMaterials[Random.Range(0, _sprinkleMaterials.Length)];
            Spawn(root, PrimitiveType.Capsule, material, new Vector3(offset.x, y, offset.y), Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), SprinkleScale(), PopSeconds);
        }
    }

    private Vector3 SprinkleScale()
    {
        return new Vector3(_sprinkleLength * 0.33f, _sprinkleLength * 0.5f, _sprinkleLength * 0.33f);
    }

    private void Spawn(Transform root, PrimitiveType type, Material material, Vector3 position, Quaternion rotation, Vector3 scale, float popSeconds)
    {
        GameObject piece = CreateVisual(type, "Topping", root, material);
        piece.transform.localPosition = position;
        piece.transform.localRotation = rotation;
        piece.transform.localScale = Vector3.zero;
        StartCoroutine(JuiceTweens.PopIn(piece.transform, scale, popSeconds));
    }

    private static float SafeInverse(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find(LitShaderName);
        if (shader == null)
        {
            shader = Shader.Find(UnlitShaderName);
        }

        var material = new Material(shader);
        material.SetColor(BaseColorId, color);
        return material;
    }
}
