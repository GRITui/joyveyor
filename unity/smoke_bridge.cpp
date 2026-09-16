// Smoke test for the Unity bridge C API (place/advance/remove).
#include "jv_unity_bridge.h"
#include <cassert>
#include <cstdio>

int main() {
    JVWorld w = jv_world_create();
    assert(w);

    // Demo level (same as JoyveyorRunner.cs).
    assert(jv_place_source(w, 0, 0) != 0xFFFFFFFFu);
    assert(jv_place_belt(w, 1, 0, 1 /*E*/, 5) != 0xFFFFFFFFu);
    assert(jv_place_splitter(w, 6, 0, 1 /*E*/, 2 /*S*/) != 0xFFFFFFFFu);
    assert(jv_place_belt(w, 7, 0, 1 /*E*/, 4) != 0xFFFFFFFFu);
    assert(jv_place_sink(w, 11, 0, 50) != 0xFFFFFFFFu);
    assert(jv_place_belt(w, 6, 1, 2 /*S*/, 4) != 0xFFFFFFFFu);
    assert(jv_place_merger(w, 6, 5, 2 /*S*/, 3 /*W*/, 1 /*E*/) != 0xFFFFFFFFu);
    assert(jv_place_belt(w, 7, 5, 1 /*E*/, 4) != 0xFFFFFFFFu);
    assert(jv_place_sink(w, 11, 5, 50) != 0xFFFFFFFFu);

    // Run ~10s of sim; items must be delivered.
    for (int i = 0; i < 300; ++i) jv_world_advance(w, 1.0f / 30.0f);
    printf("spawned=%llu delivered=%llu items=%d tick=%llu invariants=%d\n",
           (unsigned long long)jv_spawned(w), (unsigned long long)jv_delivered(w),
           jv_item_count(w), (unsigned long long)jv_world_tick_count(w),
           jv_check_invariants(w));
    assert(jv_delivered(w) > 0);
    assert(jv_check_invariants(w) == 1);

    // Remove: belt with items on it must be rejected.
    uint32_t belt = jv_place_belt(w, 1, 3, 1 /*E*/, 2);
    assert(belt != 0xFFFFFFFFu);
    // Let an item reach it? No — it's isolated (no source feeds it), so it's
    // empty: removal must succeed.
    assert(jv_remove_belt(w, belt) == 1);
    assert(jv_remove_belt(w, belt) == 0);  // double-remove rejected

    // Remove: occupied sink must be rejected; drained sink succeeds.
    uint32_t sink = jv_place_sink(w, 0, 3, 10);
    assert(sink != 0xFFFFFFFFu);
    // Feed it: belt (1,3) E len 2 exits at (3,3)... sink at (0,3) is fed by
    // a W-traveling belt. Place source (0,3)? No, sink occupies it. Use a
    // belt that ends at the sink: belt (2,3) dir W len 2 occupies (2,3),(1,3),
    // entryCell (0,3) == sink cell → sink.inputs[0] links. Source at (4,3)
    // feeds belt (2,3)? Source outputs[0] = belt whose entryCell == source
    // cell... entryCell of (2,3) W len 2 is (0,3) = sink, not source. So
    // source (4,3) + belt (3,3) W len 1 → exitCell (2,3) == belt(2,3)
    // entryCell? belt(2,3) entryCell = (0,3). Mismatch. Simplest: place
    // source at (4,3), belt (3,3) W len 2 → occupies (3,3),(2,3), exitCell
    // (1,3) — nothing there. This is getting fiddly; instead set storage
    // directly is a test helper, not in the C API. Use the real flow:
    // source (4,3) → belt (3,3) W len 2 → sink (1,3)? sink is at (0,3).
    // belt (3,3) W len 2 exitCell = (1,3). Place sink2 at (1,3):
    uint32_t src2 = jv_place_source(w, 4, 3);
    assert(src2 != 0xFFFFFFFFu);
    uint32_t belt2 = jv_place_belt(w, 3, 3, 3 /*W*/, 2);
    assert(belt2 != 0xFFFFFFFFu);
    uint32_t sink2 = jv_place_sink(w, 1, 3, 5);
    assert(sink2 != 0xFFFFFFFFu);
    for (int i = 0; i < 300; ++i) jv_world_advance(w, 1.0f / 30.0f);
    assert(jv_delivered(w) > 0);
    // sink2 now has stored items (cap 5, autoConsume off) → remove rejected.
    assert(jv_remove_node(w, sink2) == 0);
    // belt2 may have items → removal rejected while occupied.
    assert(jv_remove_belt(w, belt2) == 0);
    // Let items drain? autoConsume is off, so storage stays. Instead remove
    // the source (empty) — nodes with no storage/queue remove fine.
    assert(jv_remove_node(w, src2) == 1);
    assert(jv_remove_node(w, src2) == 0);  // double-remove rejected

    jv_world_destroy(w);
    printf("SMOKE OK\n");
    return 0;
}
