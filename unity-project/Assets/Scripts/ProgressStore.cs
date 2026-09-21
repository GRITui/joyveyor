using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// JoyVeyor v1.0 — local progress (jv-design-gameplay §Meta Progression).
// One JSON file in Application.persistentDataPath:
//   { "version": 1, "levels": { "1": { "stars": 2, "time": 257, "pieces": 3, "score": 6430 } } }
// Per level: best stars (max), best time (min, among wins), best piece count
// (min, among wins), best score (max). Level N+1 unlocks when N has >= 1 star.
//
// ponytail: hand-rolled JSON (no System.Text.Json in .NET Standard 2.1); the
// schema is fixed and tiny, so a 2-pass emit + a minimal reader is enough.
// Add a real JSON lib only if the schema grows (per-level arrays, settings…).
public class ProgressStore
{
    public const int TotalLevels = 10;

    public struct LevelResult
    {
        public int Stars;
        public uint Time;      // completion ticks (lower is better)
        public uint Pieces;    // player pieces used (lower is better)
        public uint Score;
    }

    public readonly Dictionary<int, LevelResult> Levels = new Dictionary<int, LevelResult>();

    public static string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "joyveyor_progress.json");

    public static ProgressStore Load()
    {
        var p = new ProgressStore();
        try
        {
            if (File.Exists(SavePath)) p.Parse(File.ReadAllText(SavePath));
        }
        catch (System.Exception)
        {
            // Corrupt save: start fresh rather than crash the game.
        }
        return p;
    }

    public void Save()
    {
        try
        {
            string dir = Application.persistentDataPath;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SavePath, ToJson());
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ProgressStore] save failed: " + e.Message);
        }
    }

    // Merge a win into the stored bests. Returns true if anything improved.
    public bool RecordWin(int level, int stars, uint time, uint pieces, uint score)
    {
        LevelResult cur;
        bool has = Levels.TryGetValue(level, out cur);
        LevelResult next = has ? cur : new LevelResult();
        bool changed = !has;
        if (stars > next.Stars) { next.Stars = stars; changed = true; }
        if (!has || time < next.Time) { next.Time = time; changed = true; }
        if (!has || pieces < next.Pieces) { next.Pieces = pieces; changed = true; }
        if (score > next.Score) { next.Score = score; changed = true; }
        if (changed)
        {
            Levels[level] = next;
            Save();
        }
        return changed;
    }

    public int Stars(int level)
    {
        LevelResult r;
        return Levels.TryGetValue(level, out r) ? r.Stars : 0;
    }

    public bool IsUnlocked(int level)
    {
        if (level < 1 || level > TotalLevels) return false;
        if (level == 1) return true;
        return Stars(level - 1) >= 1;  // linear unlock: win N to open N+1
    }

    public int TotalStars()
    {
        int sum = 0;
        foreach (var kv in Levels) sum += kv.Value.Stars;
        return sum;
    }

    // ---- JSON (hand-rolled; see ponytail note above) ----

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"version\":1,\"levels\":{");
        bool first = true;
        for (int i = 1; i <= TotalLevels; ++i)
        {
            LevelResult r;
            if (!Levels.TryGetValue(i, out r) || r.Stars < 1) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(i).Append("\":{\"stars\":").Append(r.Stars)
              .Append(",\"time\":").Append(r.Time)
              .Append(",\"pieces\":").Append(r.Pieces)
              .Append(",\"score\":").Append(r.Score).Append('}');
        }
        sb.Append("}}");
        return sb.ToString();
    }

    public void Parse(string json)
    {
        Levels.Clear();
        int i = 0;
        object root = ReadValue(json, ref i);
        if (!(root is Dictionary<string, object>)) return;
        object lvObj;
        if (!((Dictionary<string, object>)root).TryGetValue("levels", out lvObj)
            || !(lvObj is Dictionary<string, object>)) return;
        foreach (var kv in (Dictionary<string, object>)lvObj)
        {
            int level;
            if (!int.TryParse(kv.Key, out level)) continue;
            if (!(kv.Value is Dictionary<string, object>)) continue;
            var f = (Dictionary<string, object>)kv.Value;
            LevelResult r = default;
            object ov;
            if (f.TryGetValue("stars", out ov) && ov is long) r.Stars = (int)(long)ov;
            if (f.TryGetValue("time", out ov) && ov is long) r.Time = (uint)(long)ov;
            if (f.TryGetValue("pieces", out ov) && ov is long) r.Pieces = (uint)(long)ov;
            if (f.TryGetValue("score", out ov) && ov is long) r.Score = (uint)(long)ov;
            if (r.Stars >= 1) Levels[level] = r;
        }
    }

    // Minimal recursive-descent JSON reader: objects -> Dictionary<string,object>,
    // numbers -> long, strings -> string. Unknown/unsupported values -> null.
    static object ReadValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) return null;
        char c = s[i];
        if (c == '{')
        {
            var o = new Dictionary<string, object>();
            ++i;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { ++i; return o; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                string key = ReadString(s, ref i);
                if (key == null) return null;
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') return null;
                ++i;
                object val = ReadValue(s, ref i);
                o[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { ++i; continue; }
                if (i < s.Length && s[i] == '}') { ++i; return o; }
                return null;
            }
            return null;
        }
        if (c == '"') return ReadString(s, ref i);
        if (c == '-' || char.IsDigit(c))
        {
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) ++i;
            long v;
            return long.TryParse(s.Substring(st, i - st), out v) ? (object)v : null;
        }
        // true/false/null: skip the token
        while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) ++i;
        return null;
    }

    static string ReadString(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '"') return null;
        int j = i + 1;
        while (j < s.Length && s[j] != '"') { if (s[j] == '\\') ++j; ++j; }
        if (j >= s.Length) return null;
        string v = s.Substring(i + 1, j - i - 1);
        i = j + 1;
        return v;
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) ++i;
    }
}
