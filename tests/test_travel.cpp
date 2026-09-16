// Belt travel: straight belt -> sink, and open-belt free travel.
#include "../core/world.h"
#include "test.h"

#include <cmath>

using namespace jv;

int main() {
    // Belt (len 4, dir E) -> sink at exit. Speed set so it traverses in 1s.
    {
        SimConfig cfg;
        cfg.beltSpeed = 4.0f;
        World w(cfg);
        const uint32_t sink = w.placeSink(GridCell{4, 0}, 4);
        const uint32_t belt = w.placeBelt(0, 0, E, 4);
        JV_CHECK(belt != INVALID_ID);
        JV_CHECK(sink != INVALID_ID);
        JV_CHECK_EQ(w.belt(belt).outNode, sink);

        w.injectItemAtEntry(belt);
        for (int i = 0; i < 30; ++i) {
            w.advance(w.config().dt);
            JV_CHECK(w.checkInvariants());
        }
        JV_CHECK_EQ(static_cast<int>(w.deliveredCount()), 1);
        JV_CHECK(w.checkInvariants());
    }

    // Open belt (no sink): item travels at beltSpeed with no gating.
    {
        SimConfig cfg;
        cfg.beltSpeed = 4.0f;
        World w(cfg);
        const uint32_t belt = w.placeBelt(0, 0, E, 4);
        JV_CHECK(belt != INVALID_ID);
        const uint32_t it = w.injectItemAtEntry(belt);
        const int32_t n = 20;  // stay short of belt length so nothing caps the travel
        for (int32_t i = 0; i < n; ++i) w.advance(w.config().dt);
        const float expected = w.config().beltSpeed * static_cast<float>(n) * w.config().dt;
        JV_CHECK(std::fabs(w.items().arcPos(it) - expected) < 1e-3f);
    }

    JV_REPORT();
}
