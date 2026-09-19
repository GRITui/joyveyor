// Splitter: 1->2 round-robin dispatch, conservation, invariants (README §5).
#include "../core/world.h"
#include "test.h"

#include <vector>

using namespace jv;

int main() {
    SimConfig cfg;
    cfg.beltSpeed = 6.0f;
    World w(cfg);

    const uint32_t source = w.placeSource(GridCell{0, 0});
    const uint32_t splitter = w.placeSplitter(GridCell{3, 0}, E, S);
    const uint32_t sinkA = w.placeSink(GridCell{6, 0}, 20);
    const uint32_t sinkB = w.placeSink(GridCell{3, 3}, 20);
    JV_CHECK(source != INVALID_ID);
    JV_CHECK(splitter != INVALID_ID);
    JV_CHECK(sinkA != INVALID_ID);
    JV_CHECK(sinkB != INVALID_ID);

    const uint32_t beltIn = w.placeBelt(1, 0, E, 2);
    const uint32_t beltA = w.placeBelt(4, 0, E, 2);
    const uint32_t beltB = w.placeBelt(3, 1, S, 2);
    JV_CHECK(beltIn != INVALID_ID);
    JV_CHECK(beltA != INVALID_ID);
    JV_CHECK(beltB != INVALID_ID);
    JV_CHECK_EQ(w.node(splitter).outputs[0], beltA);
    JV_CHECK_EQ(w.node(splitter).outputs[1], beltB);

    std::vector<char> order;
    uint16_t prevA = 0, prevB = 0;
    bool sourceRemoved = false;
    for (int t = 0; t < 5000; ++t) {
        w.tick();
        JV_CHECK(w.checkInvariants());
        const uint16_t curA = w.node(sinkA).storageCount;
        const uint16_t curB = w.node(sinkB).storageCount;
        if (curA > prevA) { order.push_back('A'); prevA = curA; }
        if (curB > prevB) { order.push_back('B'); prevB = curB; }
        if (!sourceRemoved && order.size() >= 4) {
            w.removeNode(source);
            sourceRemoved = true;
        }
        if (sourceRemoved && w.itemCount() == 0) break;
    }

    JV_CHECK(order.size() >= 4);
    for (size_t i = 0; i < order.size(); ++i) {
        JV_CHECK_EQ(order[i], (i % 2 == 0) ? 'A' : 'B');
    }
    JV_CHECK(sourceRemoved);
    JV_CHECK_EQ(w.itemCount(), 0);
    JV_CHECK_EQ(w.deliveredCount(), w.spawnedCount());

    // 2) Full-branch fall-through (t_59dcaa3e): one branch's sink fills
    // instantly; the free branch must still receive the majority of items
    // instead of starving behind the full one.
    {
        World w2;
        const uint32_t src = w2.placeSource(GridCell{0, 0});
        const uint32_t split = w2.placeSplitter(GridCell{3, 0}, E, S);
        const uint32_t sinkE = w2.placeSink(GridCell{5, 0}, 1);   // fills instantly
        const uint32_t sinkS = w2.placeSink(GridCell{3, 11}, 50); // free
        JV_CHECK(src != INVALID_ID && split != INVALID_ID);
        JV_CHECK(sinkE != INVALID_ID && sinkS != INVALID_ID);
        const uint32_t inBelt = w2.placeBelt(1, 0, E, 2);
        const uint32_t eBelt = w2.placeBelt(4, 0, E, 1);
        const uint32_t sBelt = w2.placeBelt(3, 1, S, 10);
        JV_CHECK(inBelt != INVALID_ID && eBelt != INVALID_ID && sBelt != INVALID_ID);

        const int ticks = 120 * 30;  // 120s at 30 Hz
        for (int t = 0; t < ticks; ++t) {
            w2.tick();
            JV_CHECK(w2.checkInvariants());
        }

        // Count items sitting on the free (S) branch belt.
        int32_t sBeltItems = 0;
        for (uint32_t it = w2.belt(sBelt).headItem; it != INVALID_ID;
             it = w2.items().nextOnBelt(it)) ++sBeltItems;
        const uint32_t sDelivered = w2.node(sinkS).storageCount;
        const uint32_t spawned = w2.spawnedCount();
        // Free branch (sink + in-transit) must hold >= 80% of (spawned - 1),
        // the -1 being the single item that filled the E sink.
        JV_CHECK(spawned > 1);
        JV_CHECK(static_cast<uint64_t>(sDelivered + sBeltItems) * 10 >=
                 static_cast<uint64_t>(spawned - 1) * 8);
    }

    JV_REPORT();
}
