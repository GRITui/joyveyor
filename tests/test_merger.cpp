// Merger: 2->1 merge with no overlap, conservation (README §5).
#include "../core/world.h"
#include "test.h"

using namespace jv;

int main() {
    SimConfig cfg;
    cfg.beltSpeed = 6.0f;
    World w(cfg);

    const uint32_t sourceA = w.placeSource(GridCell{7, 10});
    const uint32_t sourceB = w.placeSource(GridCell{13, 10});
    const uint32_t merger = w.placeMerger(GridCell{10, 10}, E, W, N);
    const uint32_t sink = w.placeSink(GridCell{10, 7}, 20);
    JV_CHECK(sourceA != INVALID_ID);
    JV_CHECK(sourceB != INVALID_ID);
    JV_CHECK(merger != INVALID_ID);
    JV_CHECK(sink != INVALID_ID);

    const uint32_t beltInA = w.placeBelt(8, 10, E, 2);
    const uint32_t beltInB = w.placeBelt(12, 10, W, 2);
    const uint32_t beltOut = w.placeBelt(10, 9, N, 2);
    JV_CHECK(beltInA != INVALID_ID);
    JV_CHECK(beltInB != INVALID_ID);
    JV_CHECK(beltOut != INVALID_ID);
    JV_CHECK_EQ(w.node(merger).inputs[0], beltInA);
    JV_CHECK_EQ(w.node(merger).inputs[1], beltInB);
    JV_CHECK_EQ(w.node(merger).outputs[0], beltOut);

    uint16_t prevSink = 0;
    bool sourcesRemoved = false;
    for (int t = 0; t < 5000; ++t) {
        w.tick();
        JV_CHECK(w.checkInvariants());
        const uint16_t curSink = w.node(sink).storageCount;
        if (!sourcesRemoved && curSink - prevSink >= 0 && curSink >= 4) {
            w.removeNode(sourceA);
            w.removeNode(sourceB);
            sourcesRemoved = true;
        }
        prevSink = curSink;
        if (sourcesRemoved && w.itemCount() == 0) break;
    }

    JV_CHECK(prevSink >= 4);
    JV_CHECK(sourcesRemoved);
    JV_CHECK_EQ(w.itemCount(), 0);
    JV_CHECK_EQ(w.deliveredCount(), w.spawnedCount());

    JV_REPORT();
}
