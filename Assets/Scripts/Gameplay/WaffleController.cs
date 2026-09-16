using System.Collections;
using UnityEngine;

/// <summary>
/// Represents a single waffle instance: char level via a runtime-instanced
/// material color plus a parabolic flip-toss motion. Colors are driven on the
/// URP Lit <c>_BaseColor</c> property.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class WaffleController : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int CullId = Shader.PropertyToID("_Cull");

    // Accuracy at or above this bakes a cooked (golden) waffle; below it burns.
    private const float CookedThreshold = 0.5f;

    // Uniform enlargement of the inverted-hull outline shell.
    private const float OutlineScale = 1.09f;

    // Dark brown rim colour that pops a perfectly cooked waffle off the plate.
    private static readonly Color OutlineColor = new Color(0.18f, 0.10f, 0.04f, 1f);

    [SerializeField] private MeshRenderer _meshRenderer;

    private Material _instancedMaterial;
    private GameObject _outline;
    private Vector3 _restPosition;
    private bool _hasRestPosition;

    private void Awake()
    {
        if (_meshRenderer == null)
        {
            _meshRenderer = GetComponent<MeshRenderer>();
        }

        // Instance the material so the shared .mat asset is never dirtied.
        if (_meshRenderer != null)
        {
            _instancedMaterial = _meshRenderer.material;
        }

        EnsureOutline();
        CacheRestPosition();
    }

    /// <summary>
    /// Builds (or reuses) a slightly larger, front-culled dark-brown shell child
    /// that renders as a crisp outline around a perfectly cooked waffle. Starts
    /// hidden and is toggled on only when the waffle bakes golden.
    /// </summary>
    private void EnsureOutline()
    {
        if (_outline != null)
        {
            return;
        }

        Transform existing = transform.Find("Outline");
        if (existing != null)
        {
            _outline = existing.gameObject;
            _outline.SetActive(false);
            return;
        }

        MeshFilter sourceFilter = GetComponent<MeshFilter>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null)
        {
            return;
        }

        _outline = new GameObject("Outline");
        _outline.transform.SetParent(transform, false);
        _outline.transform.localPosition = Vector3.zero;
        _outline.transform.localRotation = Quaternion.identity;
        _outline.transform.localScale = Vector3.one * OutlineScale;

        MeshFilter outlineFilter = _outline.AddComponent<MeshFilter>();
        outlineFilter.sharedMesh = sourceFilter.sharedMesh;

        MeshRenderer outlineRenderer = _outline.AddComponent<MeshRenderer>();
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null)
        {
            Material outlineMaterial = new Material(unlit);
            outlineMaterial.SetColor(BaseColorId, OutlineColor);
            outlineMaterial.SetFloat(CullId, 1f); // Cull front faces -> inverted hull.
            outlineRenderer.material = outlineMaterial;
        }

        outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _outline.SetActive(false);
    }

    private void CacheRestPosition()
    {
        if (!_hasRestPosition)
        {
            _restPosition = transform.localPosition;
            _hasRestPosition = true;
        }
    }

    /// <summary>Whether a pancake with this accuracy comes out cooked (golden) rather than burned.</summary>
    public static bool IsCooked(float accuracy, bool burned)
    {
        return !burned && Mathf.Clamp01(accuracy) >= CookedThreshold;
    }

    /// <summary>
    /// Bakes the waffle to one of two outcomes: a cooked golden waffle when
    /// accuracy meets <see cref="CookedThreshold"/> and no press failed,
    /// otherwise a burned one. Any failed press forces burned.
    /// </summary>
    public void SetCharLevel(float accuracy, GameConfig config, bool forceBurned = false)
    {
        if (_instancedMaterial == null || config == null)
        {
            return;
        }

        // Cooked or burned only - a single failed press burns it black.
        bool cooked = IsCooked(accuracy, forceBurned);
        Color color = cooked ? config.WaffleGoldenColor : config.WaffleCharredColor;

        _instancedMaterial.SetColor(BaseColorId, color);

        // Only a perfectly cooked (golden) waffle earns the dark-brown outline.
        EnsureOutline();
        if (_outline != null)
        {
            _outline.SetActive(cooked);
        }
    }

    /// <summary>Shows the raw batter color with no outline, e.g. while the pancake is still in the pan.</summary>
    public void SetRaw(GameConfig config)
    {
        if (_instancedMaterial == null || config == null)
        {
            return;
        }

        _instancedMaterial.SetColor(BaseColorId, config.WaffleRawColor);

        EnsureOutline();
        if (_outline != null)
        {
            _outline.SetActive(false);
        }
    }

    /// <summary>
    /// Tosses the waffle in a parabolic vertical arc while spinning, then
    /// settles it back to its rest position.
    /// </summary>
    public IEnumerator FlipToss(float height, float duration, float spins)
    {
        CacheRestPosition();

        if (duration <= 0f)
        {
            yield break;
        }

        Quaternion startRotation = transform.localRotation;
        float totalSpin = 360f * spins;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Parabolic arc peaks at t = 0.5.
            float arc = 4f * height * t * (1f - t);
            transform.localPosition = _restPosition + Vector3.up * arc;
            transform.localRotation = startRotation * Quaternion.Euler(totalSpin * t, 0f, 0f);

            yield return null;
        }

        transform.localPosition = _restPosition;
        transform.localRotation = startRotation;
    }

    /// <summary>
    /// Whimsical serve: launches the waffle from <paramref name="startWorld"/>
    /// on a high, hang-time arc to <paramref name="landWorld"/> while spinning
    /// with a playful wobble and a scale "boing", then settles with a bouncy
    /// squash-and-stretch landing on the plate at <paramref name="landRotation"/>.
    /// </summary>
    public IEnumerator FlipToPlate(Vector3 startWorld, Vector3 landWorld, Quaternion landRotation, float height, float duration, float spins)
    {
        Quaternion restRotation = transform.localRotation;
        Vector3 baseScale = transform.localScale;
        transform.position = startWorld;

        if (duration <= 0f)
        {
            transform.position = landWorld;
            transform.rotation = landRotation;
            yield break;
        }

        float totalSpin = 360f * spins;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Ease-out horizontal travel gives extra hang time near the apex.
            float horiz = 1f - Mathf.Pow(1f - t, 2f);
            Vector3 pos = Vector3.Lerp(startWorld, landWorld, horiz);

            // Tall parabolic arc for a dramatic toss.
            pos.y += 4f * height * t * (1f - t);
            transform.position = pos;

            // Fast multi-spin with a cheeky side-to-side wobble.
            float wobble = Mathf.Sin(t * Mathf.PI * 3f) * 12f;
            transform.localRotation = restRotation * Quaternion.Euler(totalSpin * t, 0f, wobble);

            // Scale "boing": puffs up toward the apex, thins on the way down.
            float pulse = 1f + Mathf.Sin(t * Mathf.PI) * 0.18f;
            transform.localScale = baseScale * pulse;

            yield return null;
        }

        transform.position = landWorld;
        transform.rotation = landRotation;

        

        yield return LandingSquash(baseScale);
        transform.localScale = baseScale;
    }

    private IEnumerator LandingSquash(Vector3 baseScale)
    {
        // Splat wide-and-flat, rebound tall-and-narrow, then damp back to rest.
        Vector3 splat = new Vector3(baseScale.x * 1.35f, baseScale.y, baseScale.z * 0.55f);
        Vector3 rebound = new Vector3(baseScale.x * 0.82f, baseScale.y, baseScale.z * 1.22f);

        yield return ScaleOver(baseScale, splat, 0.07f);
        yield return ScaleOver(splat, rebound, 0.09f);
        yield return ScaleOver(rebound, baseScale, 0.13f);
    }

    private IEnumerator ScaleOver(Vector3 from, Vector3 to, float duration)
    {
        if (duration <= 0f)
        {
            transform.localScale = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = Vector3.Lerp(from, to, t);
            yield return null;
        }

        transform.localScale = to;
    }
}
