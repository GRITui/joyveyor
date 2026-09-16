// Backpressure: belt -> sink (capacity 2, no auto-consume), 5 items queued.
#include "../core/world.h"
#include "test.h"

using namespace jv;

int main() {
    World w;
    const uint32_t sink = w.placeSink(GridCell{6, 0}, 2);
    const uint32_t belt = w.placeBelt(0, 0, E, 6);
    JV_CHECK(belt != INVALID_ID);
    JV_CHECK(sink != INVALID_ID);
    w.node(sink).autoConsume = false;

    const float minGap = w.config().minGap;
    for (int i = 0; i < 5; ++i) {
        w.injectItemAtEntry(belt);
        // Space injections out so items don't overlap at arcPos 0.
        for (int t = 0; t < 20; ++t) {
            w.advance(w.config().dt);
            JV_CHECK(w.checkInvariants());
        }
        (void)minGap;
    }

    // Run to steady state.
    for (int t = 0; t < 200; ++t) {
        w.advance(w.config().dt);
        JV_CHECK(w.checkInvariants());
    }

    // Sink storage is full and not auto-consuming: backpressure holds the rest on the belt.
    // deliveredCount tracks items that have entered sink storage, not final consumption.
    JV_CHECK_EQ(w.node(sink).storageCount, 2);
    JV_CHECK_EQ(static_cast<int>(w.deliveredCount()), 2);
    JV_CHECK_EQ(w.itemCount(), 3);

    // Repeatedly drain to free storage, letting the remaining items flow through.
    for (int t = 0; t < 400 && w.deliveredCount() != 5; ++t) {
        if (w.node(sink).storageCount > 0) w.drainSink(sink);
        w.advance(w.config().dt);
        JV_CHECK(w.checkInvariants());
    }
    JV_CHECK_EQ(static_cast<int>(w.deliveredCount()), 5);

    JV_REPORT();
}
