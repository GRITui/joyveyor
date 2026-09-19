// Deadlock: static cycle detection + runtime jam detection (README §3.1).
#include "../core/world.h"
#include "test.h"

using namespace jv;

int main() {
    // 1) Static cycle: closed square loop via mergers (avoids splitter rrCursor
    // sticking on an unconnected output slot).
    {
        World w;
        const uint32_t tl = w.placeMerger(GridCell{0, 0}, N, S, E);
        const uint32_t tr = w.placeMerger(GridCell{3, 0}, E, N, S);
        const uint32_t br = w.placeMerger(GridCell{3, 3}, S, E, W);
        const uint32_t bl = w.placeMerger(GridCell{0, 3}, W, S, N);
        JV_CHECK(tl != INVALID_ID && tr != INVALID_ID && br != INVALID_ID && bl != INVALID_ID);
        JV_CHECK(w.placeBelt(1, 0, E, 2) != INVALID_ID);
        JV_CHECK(w.placeBelt(3, 1, S, 2) != INVALID_ID);
        JV_CHECK(w.placeBelt(2, 3, W, 2) != INVALID_ID);
        JV_CHECK(w.placeBelt(0, 2, N, 2) != INVALID_ID);
        JV_CHECK(w.hasCycle());
    }

    // 2) Non-cyclic line: never reports a cycle or a deadlock.
    {
        World w;
        const uint32_t belt = w.placeBelt(0, 0, E, 3);
        const uint32_t sink = w.placeSink(GridCell{3, 0}, 10);
        JV_CHECK(belt != INVALID_ID && sink != INVALID_ID);
        w.injectItemAtEntry(belt);
        JV_CHECK(!w.hasCycle());
        for (int t = 0; t < 200; ++t) {
            w.tick();
            JV_CHECK(w.checkInvariants());
            JV_CHECK(!w.isDeadlocked());
        }
    }

    // 3) Healthy line feeding an already-full sink: items park at minGap
    // (zero movement) but the network is NOT cyclic, so it must NOT report
    // a deadlock. (This case previously asserted deadlocked==true — that was
    // the t_6509d19d false positive: the jam detector didn't check hasCycle.)
    {
        SimConfig cfg;
        cfg.beltSpeed = 10.0f;
        World w(cfg);
        const uint32_t belt = w.placeBelt(0, 0, E, 2);
        const uint32_t sink = w.placeSink(GridCell{2, 0}, 1);
        JV_CHECK(belt != INVALID_ID && sink != INVALID_ID);
        w.setSinkStorage(sink, 1);
        w.injectItemAtEntry(belt);

        bool deadlocked = false;
        for (int t = 0; t < 400; ++t) {
            w.tick();
            JV_CHECK(w.checkInvariants());
            if (w.isDeadlocked()) { deadlocked = true; break; }
        }
        JV_CHECK(!deadlocked);
        const uint32_t netId = w.networkOfBelt(belt);
        JV_CHECK(w.network(netId).jamTicks < kDeadlockJamTicks);
    }

    JV_REPORT();
}
