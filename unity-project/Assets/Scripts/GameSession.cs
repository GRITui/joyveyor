using System;
using System.Collections.Generic;
using UnityEngine;

// JoyVeyor v1.0 — the game loop (jv-design-gameplay §Core Loop / §Win-Lose &
// Scoring, jv-design-uxui §Screen Flow). Game layer only: no sim changes.
//
// State machine: Build -> Run -> Complete / Failed.
//   Build: player places pieces within Budget (locked pieces are fixed).
//   Run:   the sim advances one tick per fixed update (30 Hz); the timer is
//          tickCount vs TimeLimit — we simply stop advancing at the limit.
//   Win:   every sink's storageCount >= storageCapacity (quota met).
//   Lose:  timeLimit reached with any quota unmet.
//
// Stars: 1★ win · 2★ win <= ParTime · 3★ 2★ AND player pieces <= ParPieces.
// Score: max(0, (TimeLimit - completionTicks)*10 + (Budget - piecesUsed)*50).
public class GameSession : MonoBehaviour
{
    public enum Phase { Build, Run, Complete, Failed }

    [Serializable]
    public struct PlayerPiece
    {
        public char Tag;   // S/K/T/M/B
        public int A, B, C, D, E;
    }

    public int LevelIndex = 1;
    public LevelDef Level;
    public Phase phase = Phase.Build;
    public uint Ticks;            // sim ticks elapsed this round
    public uint CompletionTicks;  // set on Complete
    public int Stars;
    public uint Score;
    // Live player-piece count (budget tracking + 3★ condition).
    public int PiecesUsed => playerPieces.Count;
    public bool Paused;           // run-phase pause (Esc)
    public string Message = "";   // transient HUD message (budget, locked…)

    public event System.Action OnPhaseChanged;

    readonly List<PlayerPiece> playerPieces = new List<PlayerPiece>();
    readonly HashSet<long> lockedCells = new HashSet<long>();
    ProgressStore progress;

    JoyveyorRunner runner
    {
        get
        {
            if (joyveyorRunner == null) joyveyorRunner = GetComponent<JoyveyorRunner>();
            return joyveyorRunner;
        }
    }
    JoyveyorRunner joyveyorRunner;
    JVAudio audio;
    int lastCountdownSecond = -1;  // last remaining-seconds value we ticked

    void OnDestroy()
    {
        if (joyveyorRunner != null) joyveyorRunner.externallyTicked = false;
    }

    // ---- Level lifecycle ----

    public bool LoadLevel(int index)
    {
        if (!LevelDef.Load(index, out LevelDef lv)) return false;
        Level = lv;
        LevelIndex = index;
        if (progress == null) progress = ProgressStore.Load();
        if (!progress.IsUnlocked(index)) return false;
        StartBuild();
        return true;
    }

    public void StartBuild()
    {
        runner.externallyTicked = true;
        runner.ResetWorld();
        playerPieces.Clear();
        lockedCells.Clear();
        Ticks = 0;
        CompletionTicks = 0;
        Stars = 0;
        Score = 0;
        Paused = false;
        Message = "";
        if (!Level.ApplyLocked(runner.World))
        {
            Debug.LogError("[GameSession] locked pieces rejected for level " + LevelIndex);
            return;
        }
        foreach (var p in Level.Locked) lockedCells.Add(CellKey(p.A, p.B));
        SetPhase(Phase.Build);
    }

    public void StartRun()
    {
        if (phase != Phase.Build) return;
        Ticks = 0;
        lastCountdownSecond = -1;
        SetPhase(Phase.Run);
    }

    public void Pause()
    {
        if (phase == Phase.Run) { Paused = !Paused; SetPhase(Phase.Run); }
    }

    // ---- Run-phase ticking: one sim tick per fixed update (30 Hz) ----

    void FixedUpdate()
    {
        Tick();
    }

    public void Tick()
    {
        if (phase != Phase.Run || Paused) return;
        if (Ticks >= Level.TimeLimit) return;  // timer: stop advancing at the limit
        JoyveyorBridge.jv_world_tick(runner.World);
        ++Ticks;
        CountdownTick();
        if (AllQuotasMet()) Complete();
        else if (Ticks >= Level.TimeLimit) Fail();
    }

    // Last-5-s countdown (Sprint 8): one tick per remaining second 5..2,
    // the "1" gets the 1200 Hz 100 ms variant.
    void CountdownTick()
    {
        int remaining = (int)(Level.TimeLimit - Ticks);
        if (remaining <= 0 || remaining > 5 || remaining == lastCountdownSecond) return;
        lastCountdownSecond = remaining;
        if (Audio != null) Audio.PlayCountdown(remaining == 1);
    }

    // Win check: every sink's storageCount >= storageCapacity (jv_sink_storage).
    // Sinks = locked K pieces + player K pieces (allocation-free per tick).
    public bool AllQuotasMet()
    {
        IntPtr w = runner.World;
        foreach (var p in Level.Locked)
            if (p.Tag == 'K' && !SinkFull(w, p.A, p.B)) return false;
        foreach (var p in playerPieces)
            if (p.Tag == 'K' && !SinkFull(w, p.A, p.B)) return false;
        return true;
    }

    static bool SinkFull(IntPtr w, int x, int y)
    {
        uint id = JoyveyorBridge.jv_node_at_cell(w, x, y);
        if (id == JoyveyorBridge.InvalidId) return false;
        ushort count, cap;
        JoyveyorBridge.jv_sink_storage(w, id, out count, out cap);
        return count >= cap;
    }

    void Complete()
    {
        CompletionTicks = Ticks;
        Stars = ComputeStars(CompletionTicks, PiecesUsed);
        Score = ComputeScore(CompletionTicks, PiecesUsed);
        if (progress != null) progress.RecordWin(LevelIndex, Stars, CompletionTicks, (uint)PiecesUsed, Score);
        if (Audio != null) Audio.PlayLevelComplete();
        SetPhase(Phase.Complete);
    }

    void Fail()
    {
        if (Audio != null) Audio.PlayLevelFail();
        SetPhase(Phase.Failed);
    }

    // ---- Scoring (jv-design-gameplay §Win-Lose & Scoring) ----

    public int ComputeStars(uint completionTicks, int pieces)
    {
        if (completionTicks > Level.ParTime) return 1;
        if (pieces > Level.ParPieces) return 2;
        return 3;
    }

    public uint ComputeScore(uint completionTicks, int pieces)
    {
        long s = (long)(Level.TimeLimit - completionTicks) * 10 + (long)(Level.Budget - pieces) * 50;
        return s > 0 ? (uint)s : 0;
    }

    // ---- Player placement (budget + locked-cell rules) ----

    // Place a player piece. Returns false (with Message set) when rejected.
    // (c,d,e) are per-tag fields: S=period, K=capacity, T=outA/outB,
    // M=inA/inB/out, B=dir/len — see LevelDef.LockedPiece.
    public bool PlacePiece(char tag, int a, int b, int c = 0, int d = 0, int e = 0)
    {
        if (phase != Phase.Build) return false;
        if (playerPieces.Count >= Level.Budget)
        {
            Fail("budget: " + playerPieces.Count + "/" + Level.Budget + " pieces used");
            if (Audio != null) Audio.PlayInvalid();
            return false;
        }
        if (tag == 'B')
        {
            // Every cell the belt occupies must be free of locked pieces.
            int len = d;
            for (int i = 0; i < len; ++i)
            {
                int x = a + (c == JoyveyorBridge.DirE ? i : c == JoyveyorBridge.DirW ? -i : 0);
                int y = b + (c == JoyveyorBridge.DirS ? i : c == JoyveyorBridge.DirN ? -i : 0);
                if (lockedCells.Contains(CellKey(x, y)))
                {
                    Fail("locked piece: can't build there");
                    if (Audio != null) Audio.PlayInvalid();
                    return false;
                }
            }
        }
        else if (lockedCells.Contains(CellKey(a, b)))
        {
            Fail("locked piece: can't build there");
            if (Audio != null) Audio.PlayInvalid();
            return false;
        }
        uint id;
        switch (tag)
        {
            case 'S': id = JoyveyorBridge.jv_place_source(runner.World, a, b, (ushort)c); break;
            case 'K': id = JoyveyorBridge.jv_place_sink(runner.World, a, b, (ushort)c); break;
            case 'T': id = JoyveyorBridge.jv_place_splitter(runner.World, a, b, (byte)c, (byte)d); break;
            case 'M': id = JoyveyorBridge.jv_place_merger(runner.World, a, b, (byte)c, (byte)d, (byte)e); break;
            case 'B': id = JoyveyorBridge.jv_place_belt(runner.World, a, b, (byte)c, d); break;
            default:
                Fail("unknown piece");
                return false;
        }
        if (id == JoyveyorBridge.InvalidId)
        {
            string why = JoyveyorBridge.LastPlacementError(runner.World);
            Fail(why.Length > 0 ? "placement rejected: " + why : "placement rejected");
            if (Audio != null) Audio.PlayInvalid();
            return false;
        }
        playerPieces.Add(new PlayerPiece { Tag = tag, A = a, B = b, C = c, D = d, E = e });
        if (Audio != null) Audio.PlayPlace();
        SetPhase(Phase.Build);  // refresh HUD (piece count)
        return true;
    }

    // Remove a player piece. Locked pieces are never removable.
    public bool RemovePiece(char tag, int a, int b)
    {
        if (phase != Phase.Build) return false;
        int i = playerPieces.FindIndex(p => p.A == a && p.B == b && p.Tag == tag);
        if (i < 0)
        {
            if (lockedCells.Contains(CellKey(a, b)))
            {
                Fail("locked piece: can't remove");
                if (Audio != null) Audio.PlayInvalid();
            }
            return false;
        }
        uint id = tag == 'B'
            ? JoyveyorBridge.jv_belt_at_cell(runner.World, a, b)
            : JoyveyorBridge.jv_node_at_cell(runner.World, a, b);
        bool ok = tag == 'B'
            ? JoyveyorBridge.jv_remove_belt(runner.World, id) == 1
            : JoyveyorBridge.jv_remove_node(runner.World, id) == 1;
        if (!ok)
        {
            Fail("busy (has items)");
            if (Audio != null) Audio.PlayInvalid();
            return false;
        }
        playerPieces.RemoveAt(i);
        if (Audio != null) Audio.PlayDelete();
        SetPhase(Phase.Build);
        return true;
    }

    public IReadOnlyList<PlayerPiece> PlayerPieces => playerPieces;

    // ---- Progress access ----

    public ProgressStore Progress => progress ?? (progress = ProgressStore.Load());

    public int TotalStars => Progress.TotalStars();

    // Audio (Sprint 8): null-safe — the sandbox scene has it; headless probes
    // that add a bare GameSession don't.
    JVAudio Audio
    {
        get
        {
            if (audio == null) audio = GetComponent<JVAudio>();
            return audio;
        }
    }

    // ---- Internals ----

    static long CellKey(int x, int y) => ((long)x << 32) | (uint)y;

    void SetPhase(Phase p)
    {
        phase = p;
        if (OnPhaseChanged != null) OnPhaseChanged();
    }

    void Fail(string m)
    {
        Message = m;
    }

    public void ClearMessage()
    {
        Message = "";
    }
}
