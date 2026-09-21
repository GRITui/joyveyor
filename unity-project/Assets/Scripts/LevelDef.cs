using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// JoyVeyor v1.0 — #JVLG1 level definition (jv-design-gameplay §Level Format).
// A level is the *setup* of a round: metadata header + locked pieces.
// Player pieces are built in-game within `Budget`; they are not stored here.
//
// Text format (see core/level.h):
//   #JVLG1
//   name <free text>
//   grid <W> <H>
//   time_limit <ticks>
//   par_time <ticks>      # 2★ condition
//   par_pieces <count>    # 3★ condition (player pieces)
//   budget <count>        # max player-placed pieces
//   <placement lines>     # S x y [period] / K x y cap / T x y a b / M x y a b o / B x y dir len
public class LevelDef
{
    public string Name = "";
    public int GridW = 24, GridH = 16;
    public uint TimeLimit = 900;
    public uint ParTime = 450;
    public uint ParPieces = 3;
    public uint Budget = 4;

    // Locked pieces, file order (nodes then belts).
    public struct LockedPiece
    {
        public char Tag;
        public int A, B, C, D, E;
    }
    public readonly List<LockedPiece> Locked = new List<LockedPiece>();

    // ---- Parsing (mirrors core/level.cpp; atomic — returns false on malformed) ----

    public static bool Parse(string text, out LevelDef lv)
    {
        lv = null;
        var o = new LevelDef();
        string[] lines = text.Replace("\r", "").Split('\n');
        bool first = true;
        bool sawGrid = false, sawTime = false, sawParTime = false, sawParPieces = false, sawBudget = false;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (first)
            {
                first = false;
                if (line != "#JVLG1") return false;
                continue;
            }
            string[] t = line.Split(' ');
            bool isHeaderKey = t[0] == "name" || t[0] == "grid" || t[0] == "time_limit"
                || t[0] == "par_time" || t[0] == "par_pieces" || t[0] == "budget";
            if (isHeaderKey)
            {
                if (t[0] == "name")
                {
                    o.Name = line.Substring(5).Trim();
                }
                else if (t[0] == "grid")
                {
                    if (t.Length < 3 || !int.TryParse(t[1], out int gw) || !int.TryParse(t[2], out int gh) || gw < 1 || gh < 1)
                        return false;
                    o.GridW = gw; o.GridH = gh; sawGrid = true;
                }
                else if (t[0] == "time_limit")
                {
                    if (t.Length < 2 || !uint.TryParse(t[1], out uint v) || v < 1) return false;
                    o.TimeLimit = v; sawTime = true;
                }
                else if (t[0] == "par_time")
                {
                    if (t.Length < 2 || !uint.TryParse(t[1], out uint v)) return false;
                    o.ParTime = v; sawParTime = true;
                }
                else if (t[0] == "par_pieces")
                {
                    if (t.Length < 2 || !uint.TryParse(t[1], out uint v)) return false;
                    o.ParPieces = v; sawParPieces = true;
                }
                else if (t[0] == "budget")
                {
                    if (t.Length < 2 || !uint.TryParse(t[1], out uint v)) return false;
                    o.Budget = v; sawBudget = true;
                }
                else return false;
                continue;
            }
            if (!ParsePlacementLine(line, out LockedPiece op)) return false;
            o.Locked.Add(op);
        }
        if (first) return false;
        if (!sawGrid || !sawTime || !sawParTime || !sawParPieces || !sawBudget) return false;
        if (o.Locked.Count == 0) return false;
        if (o.ParTime > o.TimeLimit) return false;
        lv = o;
        return true;
    }

    // One placement line (S/K/T/M/B) — same grammar as core/save.cpp.
    public static bool ParsePlacementLine(string line, out LockedPiece op)
    {
        op = default;
        string[] t = line.Split(' ');
        if (t.Length < 2 || t[0].Length != 1) return false;
        char tag = t[0][0];
        int[] v = new int[5];
        int n = 0;
        for (int i = 1; i < t.Length; ++i)
        {
            if (!int.TryParse(t[i], out v[n])) return false;
            ++n;
        }
        switch (tag)
        {
            case 'S':  // S x y [period]
                if (n < 2 || n > 3 || v[0] < 0 || v[1] < 0) return false;
                op.Tag = tag; op.A = v[0]; op.B = v[1]; op.C = n == 3 ? v[2] : 15;
                return op.C >= 1;
            case 'K':  // K x y cap
                if (n != 3 || v[0] < 0 || v[1] < 0 || v[2] < 1) return false;
                op.Tag = tag; op.A = v[0]; op.B = v[1]; op.C = v[2];
                return true;
            case 'T':  // T x y a b
                if (n != 4 || v[0] < 0 || v[1] < 0) return false;
                op.Tag = tag; op.A = v[0]; op.B = v[1]; op.C = v[2]; op.D = v[3];
                return op.C != op.D;
            case 'M':  // M x y a b o
                if (n != 5 || v[0] < 0 || v[1] < 0) return false;
                op.Tag = tag; op.A = v[0]; op.B = v[1]; op.C = v[2]; op.D = v[3]; op.E = v[4];
                return op.C != op.D && op.E != op.C && op.E != op.D;
            case 'B':  // B x y dir len
                if (n != 4 || v[0] < 0 || v[1] < 0 || v[3] < 1) return false;
                op.Tag = tag; op.A = v[0]; op.B = v[1]; op.C = v[2]; op.D = v[3];
                return true;
            default:
                return false;
        }
    }

    // Apply every locked piece to the world in file order. False if any is rejected.
    public bool ApplyLocked(IntPtr world)
    {
        foreach (var p in Locked)
        {
            uint id;
            switch (p.Tag)
            {
                case 'S': id = JoyveyorBridge.jv_place_source(world, p.A, p.B, (ushort)p.C); break;
                case 'K': id = JoyveyorBridge.jv_place_sink(world, p.A, p.B, (ushort)p.C); break;
                case 'T': id = JoyveyorBridge.jv_place_splitter(world, p.A, p.B, (byte)p.C, (byte)p.D); break;
                case 'M': id = JoyveyorBridge.jv_place_merger(world, p.A, p.B, (byte)p.C, (byte)p.D, (byte)p.E); break;
                case 'B': id = JoyveyorBridge.jv_place_belt(world, p.A, p.B, (byte)p.C, p.D); break;
                default: return false;
            }
            if (id == JoyveyorBridge.InvalidId) return false;
        }
        return true;
    }

    // ---- Loading ----

    public static string LevelsDir()
    {
        // Editor runs from the project root; play builds from the bundle.
        if (Application.isEditor)
            return "Assets/Levels";
        return Path.Combine(Application.streamingAssetsPath, "Levels");
    }

    public static bool Load(int index, out LevelDef lv)
    {
        lv = null;
        string path = Path.Combine(LevelsDir(), string.Format("level{0:00}.jvl", index));
        if (!File.Exists(path)) return false;
        try
        {
            return Parse(File.ReadAllText(path), out lv);
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
