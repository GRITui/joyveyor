using System;
using System.Runtime.InteropServices;

public static class JoyveyorBridge
{
    public const uint InvalidId = 0xFFFFFFFFu;

    public const byte DirN = 0;
    public const byte DirE = 1;
    public const byte DirS = 2;
    public const byte DirW = 3;

    public static float[] LastPosXY { get; private set; }
    public static uint[] LastIds { get; private set; }

    // Sprint 7 perf: the snapshot arrays are reused across frames instead of
    // being reallocated on every GetSnapshot call (previously a fresh
    // float[maxItems*2] + uint[maxItems] every LateUpdate — a steady per-frame
    // allocation in the update path). Grown only if a larger maxItems is asked.
    static float[] cachedPosXY;
    static uint[] cachedIds;

    [DllImport("jv_unity")] public static extern IntPtr jv_world_create();
    [DllImport("jv_unity")] public static extern void jv_world_destroy(IntPtr w);
    [DllImport("jv_unity")] public static extern void jv_world_advance(IntPtr w, float seconds);
    [DllImport("jv_unity")] public static extern void jv_world_reset(IntPtr w);
    [DllImport("jv_unity")] public static extern void jv_world_tick(IntPtr w);
    [DllImport("jv_unity")] public static extern ulong jv_world_tick_count(IntPtr w);
    [DllImport("jv_unity")] public static extern uint jv_place_belt(IntPtr w, int x, int y, byte dir, int len);
    [DllImport("jv_unity")] public static extern uint jv_place_source(IntPtr w, int x, int y, ushort spawnPeriod);
    [DllImport("jv_unity")] public static extern uint jv_place_sink(IntPtr w, int x, int y, ushort capacity);
    [DllImport("jv_unity")] public static extern void jv_sink_storage(IntPtr w, uint sinkId, out ushort count, out ushort cap);
    [DllImport("jv_unity")] public static extern uint jv_place_splitter(IntPtr w, int x, int y, byte outA, byte outB);
    [DllImport("jv_unity")] public static extern uint jv_place_merger(IntPtr w, int x, int y, byte inA, byte inB, byte @out);
    [DllImport("jv_unity")] public static extern ulong jv_spawned(IntPtr w);
    [DllImport("jv_unity")] public static extern ulong jv_delivered(IntPtr w);
    [DllImport("jv_unity")] public static extern ulong jv_consumed(IntPtr w);
    [DllImport("jv_unity")] public static extern int jv_item_count(IntPtr w);
    [DllImport("jv_unity")]
    public static extern int jv_snapshot_items(IntPtr w, float alpha, [Out] float[] itemPosXY, [Out] uint[] itemIds, int maxItems);
    [DllImport("jv_unity")] public static extern int jv_has_cycle(IntPtr w);
    [DllImport("jv_unity")] public static extern int jv_is_deadlocked(IntPtr w);
    [DllImport("jv_unity")] public static extern int jv_check_invariants(IntPtr w);
    [DllImport("jv_unity")] public static extern int jv_remove_belt(IntPtr w, uint beltId);
    [DllImport("jv_unity")] public static extern int jv_remove_node(IntPtr w, uint nodeId);
    [DllImport("jv_unity")] public static extern uint jv_belt_at_cell(IntPtr w, int x, int y);
    [DllImport("jv_unity")] public static extern int jv_belt_geometry(IntPtr w, int x, int y, out byte dir, out int len);
    [DllImport("jv_unity")] public static extern uint jv_node_at_cell(IntPtr w, int x, int y);
    [DllImport("jv_unity")] public static extern IntPtr jv_save_layout(IntPtr w);
    [DllImport("jv_unity", CharSet = CharSet.Ansi)] public static extern int jv_load_layout(IntPtr w, string text);
    [DllImport("jv_unity", CharSet = CharSet.Ansi)] public static extern IntPtr jv_last_placement_error(IntPtr w);
    [DllImport("jv_unity")] public static extern void jv_free(IntPtr p);

    // Serialize the current layout; frees the C buffer before returning.
    public static string SaveLayout(IntPtr world)
    {
        IntPtr p = jv_save_layout(world);
        if (p == IntPtr.Zero) return string.Empty;
        try { return Marshal.PtrToStringAnsi(p); }
        finally { jv_free(p); }
    }

    // Load a layout (1 = ok, 0 = rejected, world untouched).
    public static bool LoadLayout(IntPtr world, string text)
    {
        return jv_load_layout(world, text) == 1;
    }

    // Human-readable reason for the last rejected placement ("" if ok).
    public static string LastPlacementError(IntPtr world)
    {
        IntPtr p = jv_last_placement_error(world);
        return p == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(p);
    }

    public static int GetSnapshot(IntPtr world, float alpha, int maxItems)
    {
        // Reuse the cached arrays (grown only if a larger maxItems is asked)
        // instead of reallocating every frame — see the field comment above.
        if (cachedPosXY == null || cachedPosXY.Length < maxItems * 2)
            cachedPosXY = new float[maxItems * 2];
        if (cachedIds == null || cachedIds.Length < maxItems)
            cachedIds = new uint[maxItems];
        LastPosXY = cachedPosXY;
        LastIds = cachedIds;
        return jv_snapshot_items(world, alpha, LastPosXY, LastIds, maxItems);
    }
}
