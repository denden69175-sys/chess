#nullable enable
using UnityEngine;

/// <summary>Short sound effects synthesised at start-up, so the project needs no audio files.</summary>
public static class Sfx
{
    private const int Rate = 44100;

    // Declared before the clips: static fields are set up in order, and the clips use it.
    private static readonly System.Random Random = new System.Random(1);

    public static readonly AudioClip Click = Make("click", 0.05f, t => Tone(t, 1300) * Decay(t, 60) * 0.35f);
    public static readonly AudioClip Move = Make("move", 0.09f, t => (Tone(t, 190) * 0.7f + Noise() * 0.3f) * Decay(t, 45) * 0.7f);
    public static readonly AudioClip Capture = Make("capture", 0.16f, t => (Tone(t, 130) * 0.5f + Noise() * 0.5f) * Decay(t, 22) * 0.85f);
    public static readonly AudioClip Check = Make("check", 0.26f, t => Tone(t, t < 0.12f ? 880 : 1175) * Decay(t % 0.13f, 18) * 0.4f);
    public static readonly AudioClip Jump = Make("jump", 0.7f, t => Sweep(t, 220, 1600, 0.7f) * Mathf.Sin(t / 0.7f * Mathf.PI) * (0.7f + 0.3f * Mathf.Sin(t * 70)) * 0.45f);
    public static readonly AudioClip Win = Make("win", 0.8f, t => Notes(t, new[] { 523f, 659f, 784f, 1047f }, 0.16f) * 0.4f);
    public static readonly AudioClip Lose = Make("lose", 0.7f, t => Notes(t, new[] { 440f, 370f, 294f }, 0.2f) * 0.4f);
    public static readonly AudioClip Start = Make("start", 0.4f, t => Notes(t, new[] { 392f, 587f }, 0.14f) * 0.35f);

    private static AudioSource? _source;

    public static bool Enabled
    {
        get => PlayerPrefs.GetInt("sound", 1) == 1;
        set => PlayerPrefs.SetInt("sound", value ? 1 : 0);
    }

    public static void Play(AudioClip clip)
    {
        if (!Enabled)
            return;
        if (_source == null)
        {
            var host = new GameObject("Sound");
            Object.DontDestroyOnLoad(host);
            _source = host.AddComponent<AudioSource>();
            if (Object.FindObjectOfType<AudioListener>() == null)
                host.AddComponent<AudioListener>();
        }
        _source.PlayOneShot(clip);
    }

    private delegate float Wave(float time);

    private static AudioClip Make(string name, float seconds, Wave wave)
    {
        int length = (int)(Rate * seconds);
        var data = new float[length];
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / Rate;
            // Short fade in and out so the clips never pop.
            float edge = Mathf.Clamp01(i / 200f) * Mathf.Clamp01((length - i) / 400f);
            data[i] = Mathf.Clamp(wave(t) * edge, -1, 1);
        }
        var clip = AudioClip.Create(name, length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Tone(float t, float frequency) => Mathf.Sin(2 * Mathf.PI * frequency * t);

    private static float Decay(float t, float speed) => Mathf.Exp(-t * speed);

    private static float Noise() => (float)(Random.NextDouble() * 2 - 1);

    private static float Sweep(float t, float from, float to, float length)
    {
        // Phase of a tone whose pitch rises linearly from 'from' to 'to'.
        float k = (to - from) / length;
        return Mathf.Sin(2 * Mathf.PI * (from * t + k * t * t / 2));
    }

    private static float Notes(float t, float[] notes, float step)
    {
        int index = Mathf.Min(notes.Length - 1, (int)(t / step));
        float local = t - index * step;
        float last = index == notes.Length - 1 ? 3 : 10;
        return (Tone(local, notes[index]) + 0.3f * Tone(local, notes[index] * 2)) * Decay(local, last);
    }
}
