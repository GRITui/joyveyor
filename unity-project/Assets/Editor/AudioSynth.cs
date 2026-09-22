using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// JoyVeyor v1.0 — Sprint 8: build-time audio baker (jv-design-visual-audio §6).
// Synthesizes the 11 SFX + the ambient factory hum as 16-bit mono PCM WAV at
// 44.1 kHz. No dependencies: a compact RIFF/WAV writer + System.Math
// oscillators (sine/square/saw) + linear/exp envelopes.
//
// Output: Assets/Audio/SFX/*.wav (committed). Belt hum + ambient are the only
// loops (looped at runtime via AudioSource.loop in JVAudio).
public static class AudioSynth
{
    const int Rate = 44100;
    const string OutDir = "Assets/Audio/SFX";

    [MenuItem("Joyveyor/Bake Audio (SFX + Ambient)")]
    public static void BakeAll()
    {
        Directory.CreateDirectory(OutDir);
        int n = 0;
        n += Write("place.wav", SfxPlace());            // sine 660 Hz, 60 ms, exp decay
        n += Write("delete.wav", SfxDelete());          // square 220->110 Hz sweep, 80 ms
        n += Write("invalid.wav", SfxInvalid());        // square 110 Hz, 120 ms, double pulse
        n += Write("belt_hum.wav", SfxBeltHum());       // saw 60+120 Hz, 1 s loop, low vol
        n += Write("delivery.wav", SfxDelivery());      // 880 Hz 50 ms + 1320 Hz 40 ms ding
        n += Write("sink_full.wav", SfxSinkFull());     // rising sines 440/660/880, 60 ms each
        n += Write("deadlock_alarm.wav", SfxDeadlock());// square 440 150 ms + 330 150 ms, x2
        n += Write("ui_click.wav", SfxUiClick());       // sine 1200 Hz, 30 ms, tiny
        n += Write("level_complete.wav", SfxLevelComplete()); // major arp + noise shimmer
        n += Write("level_fail.wav", SfxLevelFail());   // minor desc + low thud
        n += Write("countdown.wav", SfxCountdown());    // sine 1000 Hz, 30 ms
        n += Write("countdown_final.wav", SfxCountdownFinal()); // "1": 1200 Hz, 100 ms
        n += Write("ambient.wav", SfxAmbient());        // 50+100 Hz + LP brown noise, 2 s loop
        AssetDatabase.Refresh();
        // Belt hum + ambient are the only loops. Unity 6 encodes every audio
        // asset loopable by default (AudioImporter.loopable is obsolete), so
        // looping is a runtime choice: JVAudio sets AudioSource.loop only on
        // those two.
        Debug.Log("[AudioSynth] baked " + n + " WAVs -> " + OutDir);
    }

    // ---- RIFF/WAV writer: 16-bit mono PCM ----

    static int Write(string name, float[] samples)
    {
        using (var fs = new FileStream(OutDir + "/" + name, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            int data = samples.Length * 2;
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + data);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);            // fmt chunk size
            bw.Write((short)1);      // PCM
            bw.Write((short)1);      // mono
            bw.Write(Rate);          // sample rate
            bw.Write(Rate * 2);      // byte rate
            bw.Write((short)2);      // block align
            bw.Write((short)16);     // bits per sample
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(data);
            for (int i = 0; i < samples.Length; ++i)
            {
                float v = Mathf.Clamp(samples[i], -1f, 1f);
                bw.Write((short)(v * short.MaxValue));
            }
        }
        return 1;
    }

    // ---- Oscillators (System.Math) + envelopes ----

    static float Sine(float phase) => (float)Math.Sin(phase * 2.0 * Math.PI);
    static float Square(float phase) => (phase % 1.0f) < 0.5f ? 0.5f : -0.5f;
    static float Saw(float phase) => 2.0f * (phase % 1.0f) - 1.0f;
    static float ExpDecay(float t, float tau) => (float)Math.Exp(-t / tau);
    static float[] Tones(double seconds) => new float[(int)(seconds * Rate)];

    // ---- SFX (jv-design-visual-audio §6 table) ----

    static float[] SfxPlace()
    {
        float[] s = Tones(0.060);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            s[i] = Sine(t * 660f) * 0.7f * ExpDecay(t, 0.012f);
        }
        return s;
    }

    static float[] SfxDelete()
    {
        float[] s = Tones(0.080);
        float phase = 0f;
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            float f = 220f + (110f - 220f) * (t / 0.080f);  // 220 -> 110 Hz sweep
            phase += f / Rate;
            s[i] = Square(phase) * 0.5f * ExpDecay(t, 0.015f);
        }
        return s;
    }

    static float[] SfxInvalid()
    {
        float[] s = Tones(0.120);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            bool on = t < 0.045f || (t >= 0.060f && t < 0.105f);  // double pulse: on-off-on-off
            s[i] = on ? Square(t * 110f) * 0.5f : 0f;
        }
        return s;
    }

    static float[] SfxBeltHum()
    {
        // 1 s = exactly 60 cycles of 60 Hz and 120 cycles of 120 Hz -> seamless loop.
        float[] s = Tones(1.0);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            s[i] = (Saw(t * 60f) * 0.6f + Saw(t * 120f) * 0.3f) * 0.15f;  // low vol
        }
        return s;
    }

    static float[] SfxDelivery()
    {
        float[] s = Tones(0.090);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            if (t < 0.050f) s[i] += Sine(t * 880f) * 0.7f * ExpDecay(t, 0.015f);
            else s[i] += Sine((t - 0.050f) * 1320f) * 0.6f * ExpDecay(t - 0.050f, 0.012f);
        }
        return s;
    }

    static float[] SfxSinkFull()
    {
        float[] f = { 440f, 660f, 880f };
        float[] s = Tones(0.180);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            int k = (int)(t / 0.060f);
            float u = t - k * 0.060f;
            float env = u < 0.005f ? u / 0.005f : ExpDecay(u, 0.020f);  // 5 ms attack -> decay
            s[i] = Sine(u * f[k]) * 0.6f * env;
        }
        return s;
    }

    static float[] SfxDeadlock()
    {
        float[] s = Tones(0.600);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            float u = t - (int)(t / 0.300f) * 0.300f;  // 440 then 330, repeated x2
            s[i] = Square(u * (u < 0.150f ? 440f : 330f)) * (u < 0.150f ? 0.55f : 0.50f);
        }
        return s;
    }

    static float[] SfxUiClick()
    {
        float[] s = Tones(0.030);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            s[i] = Sine(t * 1200f) * 0.3f * ExpDecay(t, 0.005f);  // tiny
        }
        return s;
    }

    static float[] SfxLevelComplete()
    {
        float[] f = { 523f, 659f, 784f, 1047f };  // major arp
        float[] s = Tones(0.600);
        float lp = 0f;
        var rng = new System.Random(7);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            int k = (int)(t / 0.080f);
            if (k < 4)
            {
                float u = t - k * 0.080f;
                float toneEnv = u < 0.005f ? u / 0.005f : ExpDecay(u, 0.025f);
                s[i] += Sine(u * f[k]) * 0.55f * toneEnv;
            }
            // soft noise shimmer (low-passed white, quiet, fades out at the end)
            float w = (float)rng.NextDouble() * 2f - 1f;
            lp += 0.10f * (w - lp);
            float env = t < 0.100f ? t / 0.100f : t > 0.400f ? (0.600f - t) / 0.200f : 1f;
            s[i] += lp * 0.06f * env;
        }
        return s;
    }

    static float[] SfxLevelFail()
    {
        float[] f = { 392f, 330f, 262f };  // minor desc
        float[] s = Tones(0.510);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            int k = (int)(t / 0.120f);
            if (k < 3)
            {
                float u = t - k * 0.120f;
                float env = u < 0.005f ? u / 0.005f : ExpDecay(u, 0.030f);
                s[i] += Sine(u * f[k]) * 0.6f * env;
            }
            if (t >= 0.360f) s[i] += Sine((t - 0.360f) * 55f) * 0.8f * ExpDecay(t - 0.360f, 0.050f);  // low thud
        }
        return s;
    }

    static float[] SfxCountdown()
    {
        float[] s = Tones(0.030);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            s[i] = Sine(t * 1000f) * 0.5f * ExpDecay(t, 0.004f);
        }
        return s;
    }

    static float[] SfxCountdownFinal()
    {
        float[] s = Tones(0.100);
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            s[i] = Sine(t * 1200f) * 0.7f * ExpDecay(t, 0.020f);
        }
        return s;
    }

    static float[] SfxAmbient()
    {
        // 2 s = exactly 100 cycles of 50 Hz / 200 of 100 Hz -> seamless sine loop.
        // Brown noise: random walk + one-pole low-pass; last 50 ms crossfaded
        // with the first 50 ms so the loop point is smooth.
        float[] s = Tones(2.0);
        var rng = new System.Random(1234);
        float brown = 0f, lp = 0f;
        for (int i = 0; i < s.Length; ++i)
        {
            float t = i / (float)Rate;
            float w = (float)rng.NextDouble() * 2f - 1f;
            brown = (brown + 0.02f * w) * 0.999f;
            lp += 0.03f * (brown - lp);
            s[i] = (Sine(t * 50f) * 0.5f + Sine(t * 100f) * 0.25f + lp * 0.15f) * 0.12f;  // very low
        }
        int X = (int)(0.050f * Rate);  // 50 ms edge crossfade
        for (int i = 0; i < X; ++i)
        {
            float t = i / (float)(X - 1);
            s[s.Length - X + i] = s[s.Length - X + i] * (1f - t) + s[i] * t;
        }
        return s;
    }
}
