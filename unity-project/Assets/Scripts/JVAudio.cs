using System;
using UnityEngine;

// JoyVeyor v1.0 — Sprint 8: runtime audio (jv-design-visual-audio §6).
// Plays the 13 baked WAVs from Assets/Audio/SFX (baked at build time by
// Assets/Editor/AudioSynth.cs).
//
// Wiring (event -> file):
//   place.wav            GameSession.PlacePiece / GridEditor.Place success
//   delete.wav           GameSession.RemovePiece / GridEditor.DeleteAt success
//   invalid.wav          placement or removal rejected (budget, locked, busy…)
//   belt_hum.wav         loop while belts are active (Run phase / sandbox sim)
//   delivery.wav         runner.delivered increments (any sink delivery)
//   sink_full.wav        a sink's storageCount reaches capacity
//   deadlock_alarm.wav   jv_is_deadlocked flips 0 -> 1
//   ui_click.wav         UI clicks (tool/dir/len/demo/clear/save/load/pause)
//   level_complete.wav   GameSession -> Complete
//   level_fail.wav       GameSession -> Failed
//   countdown.wav        Run phase, 5..2 s remaining, once per second
//   countdown_final.wav  Run phase, 1 s remaining ("1")
//   ambient.wav          always-on factory hum (loop)
//
// delivery + deadlock + belt-hum are watched here because they are sim-level
// facts, identical in the sandbox and the game; everything else is driven by
// the game layer (GameSession) and the editor UI (GridEditor).
[RequireComponent(typeof(JoyveyorRunner))]
public class JVAudio : MonoBehaviour
{
    public AudioClip place;
    public AudioClip deleteSfx;
    public AudioClip invalid;
    public AudioClip beltHum;
    public AudioClip delivery;
    public AudioClip sinkFull;
    public AudioClip deadlockAlarm;
    public AudioClip uiClick;
    public AudioClip levelComplete;
    public AudioClip levelFail;
    public AudioClip countdown;
    public AudioClip countdownFinal;
    public AudioClip ambient;

    const int OneShotPool = 4;

    AudioSource[] oneShots;
    int oneShotNext;
    AudioSource beltSrc;
    AudioSource ambientSrc;

    ulong lastDelivered;
    bool deadlocked;

    void Awake()
    {
        oneShots = new AudioSource[OneShotPool];
        for (int i = 0; i < OneShotPool; ++i)
            oneShots[i] = MakeSource("one-shot-" + i, false);
        beltSrc = MakeSource("belt-hum", true);
        beltSrc.volume = 0.25f;   // low vol
        ambientSrc = MakeSource("ambient", true);
        ambientSrc.volume = 0.15f;  // very low, always-on under gameplay
        if (ambient != null) ambientSrc.Play();
    }

    AudioSource MakeSource(string name, bool loop)
    {
        var go = new GameObject("JVAudio/" + name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.loop = loop;
        src.playOnAwake = false;
        return src;
    }

    void PlayClip(AudioClip clip, float volume)
    {
        if (clip == null) return;
        var src = oneShots[oneShotNext];
        oneShotNext = (oneShotNext + 1) % OneShotPool;
        src.clip = clip;
        src.volume = volume;
        src.Play();
    }

    // ---- SFX entry points (called by GameSession / GridEditor) ----

    public void PlayPlace() => PlayClip(place, 0.7f);
    public void PlayDelete() => PlayClip(deleteSfx, 0.6f);
    public void PlayInvalid() => PlayClip(invalid, 0.5f);
    public void PlayUiClick() => PlayClip(uiClick, 0.4f);
    public void PlayDelivery() => PlayClip(delivery, 0.7f);
    public void PlaySinkFull() => PlayClip(sinkFull, 0.7f);
    public void PlayDeadlock() => PlayClip(deadlockAlarm, 0.8f);
    public void PlayLevelComplete() => PlayClip(levelComplete, 0.9f);
    public void PlayLevelFail() => PlayClip(levelFail, 0.9f);
    public void PlayCountdown(bool final) => PlayClip(final ? countdownFinal : countdown, final ? 0.8f : 0.5f);

    // Belt hum loop: on while belts are active.
    public void SetBeltHum(bool active)
    {
        if (beltHum == null) return;
        if (active && !beltSrc.isPlaying)
        {
            beltSrc.clip = beltHum;
            beltSrc.Play();
        }
        else if (!active && beltSrc.isPlaying)
            beltSrc.Stop();
    }

    // ---- Sim-level watching (delivery, deadlock, belt-active state) ----

    void LateUpdate()
    {
        var runner = GetComponent<JoyveyorRunner>();
        if (runner == null || runner.World == IntPtr.Zero) return;

        // Delivery: any increment of the world's delivered counter.
        if (runner.delivered < lastDelivered) lastDelivered = runner.delivered;  // world reset
        if (runner.delivered > lastDelivered)
        {
            lastDelivered = runner.delivered;
            PlayDelivery();
        }

        // Deadlock alarm: once per jam episode.
        bool jam = JoyveyorBridge.jv_is_deadlocked(runner.World) == 1;
        if (jam && !deadlocked) { deadlocked = true; PlayDeadlock(); }
        else if (!jam) deadlocked = false;

        // Belts active: the world is ticking. Sandbox self-ticks (unless
        // paused); the game layer ticks only in the Run phase (unpaused).
        var session = GetComponent<GameSession>();
        bool beltsActive = !runner.paused &&
            (session == null || (session.phase == GameSession.Phase.Run && !session.Paused));
        SetBeltHum(beltsActive);
    }
}
