using System;
using UnityEditor;
using UnityEngine;

// Headless bridge test: runs in EDIT mode (no playmode). Verifies the dylib
// dlopen's and P/Invoke marshals correctly inside the real Unity process,
// then drives the C++ sim the same way JoyveyorRunner does at runtime.
public static class BridgeLoadTest
{
    [MenuItem("Joyveyor/Bridge Load Test")]
    static void Run()
    {
        try
        {
            IntPtr w = JoyveyorBridge.jv_world_create();
            if (w == IntPtr.Zero) { Fail("jv_world_create returned null"); return; }

            // Demo level, same as JoyveyorRunner.PlaceDemoLevel.
            JoyveyorBridge.jv_place_source(w, 0, 0);
            JoyveyorBridge.jv_place_belt(w, 1, 0, JoyveyorBridge.DirE, 5);
            JoyveyorBridge.jv_place_splitter(w, 6, 0, JoyveyorBridge.DirE, JoyveyorBridge.DirS);
            JoyveyorBridge.jv_place_belt(w, 7, 0, JoyveyorBridge.DirE, 4);
            JoyveyorBridge.jv_place_sink(w, 11, 0, 50);
            JoyveyorBridge.jv_place_belt(w, 6, 1, JoyveyorBridge.DirS, 4);
            JoyveyorBridge.jv_place_merger(w, 6, 5, JoyveyorBridge.DirS, JoyveyorBridge.DirE, JoyveyorBridge.DirW);
            JoyveyorBridge.jv_place_belt(w, 7, 5, JoyveyorBridge.DirE, 4);
            JoyveyorBridge.jv_place_sink(w, 11, 5, 50);

            for (int i = 0; i < 300; i++) JoyveyorBridge.jv_world_advance(w, 1f / 30f);

            ulong spawned = JoyveyorBridge.jv_spawned(w);
            ulong delivered = JoyveyorBridge.jv_delivered(w);
            ulong consumed = JoyveyorBridge.jv_consumed(w);
            int items = JoyveyorBridge.jv_item_count(w);
            int inv = JoyveyorBridge.jv_check_invariants(w);
            int dead = JoyveyorBridge.jv_is_deadlocked(w);

            // Snapshot round-trip (the render path).
            var xy = new float[1024 * 2];
            var ids = new uint[1024];
            int n = JoyveyorBridge.jv_snapshot_items(w, 1f, xy, ids, 1024);

            // Remove path (editor delete tool).
            uint beltAt = JoyveyorBridge.jv_belt_at_cell(w, 1, 0);
            int removed = JoyveyorBridge.jv_remove_belt(w, beltAt);

            JoyveyorBridge.jv_world_destroy(w);

            bool ok = spawned > 0 && delivered > 0 && inv == 1 && dead == 0 && n == (int)items;
            Debug.Log("[BridgeTest] dlopen OK  spawned=" + spawned
                + " delivered=" + delivered + " consumed=" + consumed
                + " inFlight=" + items + " snapshot=" + n
                + " invariants=" + inv + " deadlock=" + dead
                + " removeBelt=" + removed + "  => " + (ok ? "PASS" : "FAIL"));
            EditorApplication.Exit(ok ? 0 : 1);
        }
        catch (System.Exception e)
        {
            Fail("exception: " + e.Message + "\n" + e.StackTrace);
        }
    }

    static void Fail(string m)
    {
        Debug.LogError("[BridgeTest] " + m);
        EditorApplication.Exit(1);
    }
}
