// Placement order: node connections must work regardless of whether the
// node or the belt is placed first (README §5).
#include "../core/world.h"
#include "test.h"

using namespace jv;

int main() {
    // Test A: node-first (source and sink placed before the belt).
    {
        SimConfig cfg;
        World w(cfg);
        const uint32_t source = w.placeSource(GridCell{0, 0});
        const uint32_t sink = w.placeSink(GridCell{6, 0}, 50);
        const uint32_t belt = w.placeBelt(1, 0, E, 5);
        JV_CHECK(source != INVALID_ID);
        JV_CHECK(sink != INVALID_ID);
        JV_CHECK(belt != INVALID_ID);
        w.advance(10.0f);
        JV_CHECK(w.deliveredCount() > 0);
    }

    // Test B: belt-first (the bug: sink placed after the belt it feeds into).
    {
        SimConfig cfg;
        World w(cfg);
        const uint32_t source = w.placeSource(GridCell{0, 0});
        const uint32_t belt = w.placeBelt(1, 0, E, 5);
        const uint32_t sink = w.placeSink(GridCell{6, 0}, 50);
        JV_CHECK(source != INVALID_ID);
        JV_CHECK(belt != INVALID_ID);
        JV_CHECK(sink != INVALID_ID);
        JV_CHECK(w.belt(belt).outNode != INVALID_ID);
        JV_CHECK_EQ(w.node(sink).inputs[0], belt);
        w.advance(10.0f);
        JV_CHECK(w.deliveredCount() > 0);
    }

    // Test C: demo layout (same placement order as the Unity demo level).
    {
        SimConfig cfg;
        World w(cfg);
        const uint32_t source = w.placeSource(GridCell{0, 0});
        const uint32_t belt1 = w.placeBelt(1, 0, E, 5);
        const uint32_t splitter = w.placeSplitter(GridCell{6, 0}, E, S);
        const uint32_t belt2 = w.placeBelt(7, 0, E, 4);
        const uint32_t sink1 = w.placeSink(GridCell{11, 0}, 50);
        const uint32_t belt3 = w.placeBelt(6, 1, S, 4);
        const uint32_t merger = w.placeMerger(GridCell{6, 5}, S, W, E);
        const uint32_t belt4 = w.placeBelt(7, 5, E, 4);
        const uint32_t sink2 = w.placeSink(GridCell{11, 5}, 50);
        JV_CHECK(source != INVALID_ID);
        JV_CHECK(belt1 != INVALID_ID);
        JV_CHECK(splitter != INVALID_ID);
        JV_CHECK(belt2 != INVALID_ID);
        JV_CHECK(sink1 != INVALID_ID);
        JV_CHECK(belt3 != INVALID_ID);
        JV_CHECK(merger != INVALID_ID);
        JV_CHECK(belt4 != INVALID_ID);
        JV_CHECK(sink2 != INVALID_ID);
        w.advance(10.0f);
        JV_CHECK(w.deliveredCount() > 0);
        JV_CHECK(w.checkInvariants());
        JV_CHECK(!w.isDeadlocked());
    }

    JV_REPORT();
}
