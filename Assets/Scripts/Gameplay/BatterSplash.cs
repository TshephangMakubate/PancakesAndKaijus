using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns a quick puff of batter droplets at a world position, arcing them
/// outward under gravity while they shrink and vanish. Purely code-driven so
/// it needs no particle asset.
/// </summary>
public class BatterSplash : MonoBehaviour
{
    private const string UnlitShaderName = "Universal Render Pipeline/Unlit";
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private const float Gravity = 12f;

    [Tooltip("Optional material for droplets. If empty, a cream unlit material is created at runtime.")]
    [SerializeField] private Material _batterMaterial;
    [SerializeField] private Color _batterColor = new Color(0.98f, 0.93f, 0.78f);
    [SerializeField] private int _dropletCount = 7;
    [SerializeField] private float _lifetime = 0.45f;
    [SerializeField] private float _horizontalSpread = 2.4f;
    [SerializeField] private float _upwardSpeed = 2.8f;
    [SerializeField] private float _dropletSize = 0.16f;

    private Material _runtimeMaterial;

    private void Awake()
    {
        if (_batterMaterial != null)
        {
            _runtimeMaterial = _batterMaterial;
        }
        else
        {
            Shader unlit = Shader.Find(UnlitShaderName);
            _runtimeMaterial = new Material(unlit);
            _runtimeMaterial.SetColor(BaseColorId, _batterColor);
        }
    }

    /// <summary>Bursts a puff of droplets outward from the given position.</summary>
    public void Play(Vector3 origin)
    {
        for (int i = 0; i < _dropletCount; i++)
        {
            StartCoroutine(AnimateDroplet(origin));
        }
    }

    private IEnumerator AnimateDroplet(Vector3 origin)
    {
        GameObject droplet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Collider collider = droplet.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        droplet.transform.SetParent(transform, true);

        MeshRenderer renderer = droplet.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = _runtimeMaterial;
        }

        float size = _dropletSize * Random.Range(0.6f, 1.25f);
        Vector3 position = origin;

        // Mostly upward and sideways, staying on the 2D play plane.
        Vector3 velocity = new Vector3(
            Random.Range(-_horizontalSpread, _horizontalSpread),
            Random.Range(_upwardSpeed * 0.6f, _upwardSpeed),
            0f);

        float elapsed = 0f;
        while (elapsed < _lifetime)
        {
            elapsed += Time.deltaTime;
            velocity.y -= Gravity * Time.deltaTime;
            position += velocity * Time.deltaTime;
            position.z = origin.z;

            float shrink = 1f - (elapsed / _lifetime);
            droplet.transform.position = position;
            droplet.transform.localScale = Vector3.one * size * shrink;

            yield return null;
        }

        Destroy(droplet);
    }
}
