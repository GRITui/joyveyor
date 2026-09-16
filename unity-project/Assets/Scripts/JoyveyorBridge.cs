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

    [DllImport("jv_unity")] public static extern IntPtr jv_world_create();
    [DllImport("jv_unity")] public static extern void jv_world_destroy(IntPtr w);
    [DllImport("jv_unity")] public static extern void jv_world_advance(IntPtr w, float seconds);
    [DllImport("jv_unity")] public static extern ulong jv_world_tick_count(IntPtr w);
    [DllImport("jv_unity")] public static extern uint jv_place_belt(IntPtr w, int x, int y, byte dir, int len);
    [DllImport("jv_unity")] public static extern uint jv_place_source(IntPtr w, int x, int y);
    [DllImport("jv_unity")] public static extern uint jv_place_sink(IntPtr w, int x, int y, ushort capacity);
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

    public static int GetSnapshot(IntPtr world, float alpha, int maxItems)
    {
        LastPosXY = new float[maxItems * 2];
        LastIds = new uint[maxItems];
        return jv_snapshot_items(world, alpha, LastPosXY, LastIds, maxItems);
    }
}
