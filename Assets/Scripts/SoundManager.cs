using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Singleton sound manager for Unity.
/// Handles music (with crossfade), pooled SFX playback, and volume control.
/// Attach to an empty GameObject named "SoundManager" and mark it DontDestroyOnLoad,
/// or just drop it in your first scene — it will persist automatically.
/// </summary>
[DisallowMultipleComponent]
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Mixer (optional)")]
    [Tooltip("Assign an AudioMixer with 'MusicVolume' and 'SFXVolume' exposed parameters for smoother mixing. Leave empty to use plain AudioSource volume instead.")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string musicMixerParam = "MusicVolume";
    [SerializeField] private string sfxMixerParam = "SFXVolume";

    [Header("Sound Library")]
    [SerializeField] private List<Sound> sounds = new List<Sound>();

    [Header("SFX Pool")]
    [SerializeField] private int sfxPoolSize = 12;

    [Header("Music")]
    [SerializeField] private float musicFadeDuration = 1f;

    [Header("Volumes (0-1, used if no mixer assigned)")]
    [Range(0f, 1f)][SerializeField] private float masterVolume = 1f;
    [Range(0f, 1f)][SerializeField] private float musicVolume = 1f;
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 1f;

    private Dictionary<string, Sound> soundLookup;
    private AudioSource musicSourceA;
    private AudioSource musicSourceB;
    private bool usingSourceA = true;
    private Coroutine musicFadeRoutine;

    private List<AudioSource> sfxPool;
    private int sfxPoolIndex;

    [Serializable]
    public class Sound
    {
        public string name;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.1f, 3f)] public float pitch = 1f;
        public bool loop = false;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildLookup();
        SetupMusicSources();
        SetupSfxPool();
    }

    private void BuildLookup()
    {
        soundLookup = new Dictionary<string, Sound>();
        foreach (var s in sounds)
        {
            if (s == null || string.IsNullOrEmpty(s.name)) continue;
            if (!soundLookup.ContainsKey(s.name))
                soundLookup.Add(s.name, s);
            else
                Debug.LogWarning($"SoundManager: duplicate sound name '{s.name}' ignored.");
        }
    }

    private void SetupMusicSources()
    {
        musicSourceA = gameObject.AddComponent<AudioSource>();
        musicSourceB = gameObject.AddComponent<AudioSource>();
        foreach (var src in new[] { musicSourceA, musicSourceB })
        {
            src.loop = true;
            src.playOnAwake = false;
            if (audioMixer != null)
            {
                var groups = audioMixer.FindMatchingGroups("Music");
                if (groups.Length > 0) src.outputAudioMixerGroup = groups[0];
            }
        }
    }

    private void SetupSfxPool()
    {
        sfxPool = new List<AudioSource>(sfxPoolSize);
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            if (audioMixer != null)
            {
                var groups = audioMixer.FindMatchingGroups("SFX");
                if (groups.Length > 0) src.outputAudioMixerGroup = groups[0];
            }
            sfxPool.Add(src);
        }
    }

    // ---------------- SFX ----------------

    /// <summary>Play a one-shot sound effect by name from the library.</summary>
    public void PlaySFX(string name, float volumeScale = 1f, float pitchScale = 1f)
    {
        if (!soundLookup.TryGetValue(name, out var sound))
        {
            Debug.LogWarning($"SoundManager: sound '{name}' not found.");
            return;
        }
        PlaySFXClip(sound.clip, sound.volume * volumeScale, sound.pitch * pitchScale);
    }

    /// <summary>Play a one-shot sound effect from a direct AudioClip reference.</summary>
    public void PlaySFXClip(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        var src = GetNextSfxSource();
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume) * (audioMixer == null ? sfxVolume * masterVolume : 1f);
        src.pitch = pitch;
        src.loop = false;
        src.Play();
    }

    private AudioSource GetNextSfxSource()
    {
        // Round-robin through the pool; skips currently playing sources when possible.
        for (int i = 0; i < sfxPool.Count; i++)
        {
            int idx = (sfxPoolIndex + i) % sfxPool.Count;
            if (!sfxPool[idx].isPlaying)
            {
                sfxPoolIndex = (idx + 1) % sfxPool.Count;
                return sfxPool[idx];
            }
        }
        // All busy — steal the next one in line (oldest-ish).
        var stolen = sfxPool[sfxPoolIndex];
        sfxPoolIndex = (sfxPoolIndex + 1) % sfxPool.Count;
        return stolen;
    }

    // ---------------- Music ----------------

    /// <summary>Play a music track by name from the library, crossfading from the current track.</summary>
    public void PlayMusic(string name, bool loop = true, bool forceRestart = false)
    {
        if (!soundLookup.TryGetValue(name, out var sound))
        {
            Debug.LogWarning($"SoundManager: music '{name}' not found.");
            return;
        }
        PlayMusicClip(sound.clip, loop, forceRestart);
    }

    /// <summary>Play a music track from a direct AudioClip reference, crossfading from the current track.</summary>
    public void PlayMusicClip(AudioClip clip, bool loop = true, bool forceRestart = false)
    {
        if (clip == null) return;

        var current = usingSourceA ? musicSourceA : musicSourceB;
        if (!forceRestart && current.clip == clip && current.isPlaying) return;

        var next = usingSourceA ? musicSourceB : musicSourceA;
        next.clip = clip;
        next.loop = loop;
        next.volume = 0f;
        next.Play();

        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        musicFadeRoutine = StartCoroutine(CrossfadeMusic(current, next));
        usingSourceA = !usingSourceA;
    }

    public void StopMusic(float fadeOutDuration = -1f)
    {
        var current = usingSourceA ? musicSourceA : musicSourceB;
        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        musicFadeRoutine = StartCoroutine(FadeOutAndStop(current, fadeOutDuration < 0f ? musicFadeDuration : fadeOutDuration));
    }

    private IEnumerator CrossfadeMusic(AudioSource from, AudioSource to)
    {
        float targetVol = audioMixer == null ? musicVolume * masterVolume : 1f;
        float t = 0f;
        float fromStartVol = from.volume;

        while (t < musicFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / musicFadeDuration);
            to.volume = Mathf.Lerp(0f, targetVol, p);
            from.volume = Mathf.Lerp(fromStartVol, 0f, p);
            yield return null;
        }

        to.volume = targetVol;
        from.volume = 0f;
        from.Stop();
    }

    private IEnumerator FadeOutAndStop(AudioSource src, float duration)
    {
        float startVol = src.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(startVol, 0f, t / duration);
            yield return null;
        }
        src.volume = 0f;
        src.Stop();
    }

    // ---------------- Volume control ----------------

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        if (audioMixer != null)
            audioMixer.SetFloat(musicMixerParam, LinearToDecibel(musicVolume * masterVolume));
        else
            ApplyVolumes();
    }

    public void SetSFXVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        if (audioMixer != null)
            audioMixer.SetFloat(sfxMixerParam, LinearToDecibel(sfxVolume * masterVolume));
    }

    private void ApplyVolumes()
    {
        if (audioMixer != null)
        {
            audioMixer.SetFloat(musicMixerParam, LinearToDecibel(musicVolume * masterVolume));
            audioMixer.SetFloat(sfxMixerParam, LinearToDecibel(sfxVolume * masterVolume));
        }
        else
        {
            var current = usingSourceA ? musicSourceA : musicSourceB;
            if (current.isPlaying)
                current.volume = musicVolume * masterVolume;
        }
    }

    private float LinearToDecibel(float linear)
    {
        // Avoid log(0)
        return linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
    }

    // ---------------- Utility ----------------

    /// <summary>Add or update a sound entry at runtime (e.g. for dynamically loaded audio).</summary>
    public void RegisterSound(string name, AudioClip clip, float volume = 1f, float pitch = 1f, bool loop = false)
    {
        var sound = new Sound { name = name, clip = clip, volume = volume, pitch = pitch, loop = loop };
        soundLookup[name] = sound;
    }

    public bool IsMusicPlaying()
    {
        var current = usingSourceA ? musicSourceA : musicSourceB;
        return current.isPlaying;
    }
}