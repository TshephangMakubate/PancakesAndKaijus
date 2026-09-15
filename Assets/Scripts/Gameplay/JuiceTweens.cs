using System.Collections;
using UnityEngine;

/// <summary>Small coroutine tweens for arcade-style pops, hops, and squashes.</summary>
public static class JuiceTweens
{
    private const float HopTiltDegrees = -12f;

    /// <summary>Scales a transform up from zero with a springy overshoot.</summary>
    public static IEnumerator PopIn(Transform target, Vector3 finalScale, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (target == null)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            target.localScale = finalScale * EaseOutBack(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (target != null)
        {
            target.localScale = finalScale;
        }
    }

    /// <summary>Hops a transform along an arc to a world position, tilting cheekily mid-air and scaling to a new size.</summary>
    public static IEnumerator Hop(Transform target, Vector3 endWorld, float height, float duration, Vector3 endLocalScale)
    {
        if (target == null)
        {
            yield break;
        }

        Vector3 start = target.position;
        Vector3 startScale = target.localScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (target == null)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            Vector3 position = Vector3.Lerp(start, endWorld, t);
            position.y += 4f * height * t * (1f - t);
            target.position = position;
            target.localScale = Vector3.Lerp(startScale, endLocalScale, t);
            target.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI) * HopTiltDegrees);
            yield return null;
        }

        if (target != null)
        {
            target.position = endWorld;
            target.localScale = endLocalScale;
            target.localRotation = Quaternion.identity;
        }
    }

    /// <summary>Splats wide, rebounds tall, then settles back to the base scale.</summary>
    public static IEnumerator Squash(Transform target, Vector3 baseScale, float amount)
    {
        Vector3 splat = Vector3.Scale(baseScale, new Vector3(1f + amount, 1f - amount, 1f + amount));
        Vector3 rebound = Vector3.Scale(baseScale, new Vector3(1f - amount * 0.5f, 1f + amount * 0.6f, 1f - amount * 0.5f));

        yield return ScaleTo(target, splat, 0.06f);
        yield return ScaleTo(target, rebound, 0.08f);
        yield return ScaleTo(target, baseScale, 0.12f);
    }

    /// <summary>Slides a transform to a new local position and rotation.</summary>
    public static IEnumerator MoveLocal(Transform target, Vector3 endPosition, Quaternion endRotation, float duration)
    {
        if (target == null)
        {
            yield break;
        }

        Vector3 startPosition = target.localPosition;
        Quaternion startRotation = target.localRotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (target == null)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
            target.localPosition = Vector3.LerpUnclamped(startPosition, endPosition, t);
            target.localRotation = Quaternion.SlerpUnclamped(startRotation, endRotation, t);
            yield return null;
        }

        if (target != null)
        {
            target.localPosition = endPosition;
            target.localRotation = endRotation;
        }
    }

    /// <summary>Flings a transform away under gravity, spinning and shrinking, then destroys it.</summary>
    public static IEnumerator TossAway(Transform target, Vector3 velocity, float spinDegreesPerSecond, float duration)
    {
        if (target == null)
        {
            yield break;
        }

        Vector3 startScale = target.localScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (target == null)
            {
                yield break;
            }

            float dt = Time.deltaTime;
            elapsed += dt;
            velocity += Physics.gravity * dt;
            target.position += velocity * dt;
            target.Rotate(0f, 0f, spinDegreesPerSecond * dt, Space.World);
            target.localScale = startScale * (1f - Mathf.Clamp01(elapsed / duration) * 0.6f);
            yield return null;
        }

        if (target != null)
        {
            Object.Destroy(target.gameObject);
        }
    }

    private static IEnumerator ScaleTo(Transform target, Vector3 to, float duration)
    {
        if (target == null)
        {
            yield break;
        }

        Vector3 from = target.localScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (target == null)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            target.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (target != null)
        {
            target.localScale = to;
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
