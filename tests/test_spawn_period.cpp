// JoyVeyor core — per-source spawn period + per-sink storage (v1.0 S1).
//
// Verifies:
//  * a period-5 source spawns exactly 3x the items of a period-15 source
//    over 150 ticks (30 vs 10 — deterministic, no randomness in sim path);
//  * spawnPeriod round-trips through save/load (S x y period);
//  * legacy 'S x y' lines (no period) load with the default 15;
//  * a sink's storageCount/storageCapacity are exact after deliveries
//    (the data jv_sink_storage() exposes).
#include "save.h"
#include "world.h"

#include "test.h"

using namespace jv;

// source (0,0) -> belt (1,0) E len 2 -> sink (2,0).
static void buildLine(World& w, uint16_t period, uint16_t sinkCap) {
    JV_CHECK(w.placeSource(GridCell{0, 0}, period) != INVALID_ID);
    JV_CHECK(w.placeBelt(1, 0, E, 2) != INVALID_ID);  // occupies (1,0),(2,0)
    JV_CHECK(w.placeSink(GridCell{3, 0}, sinkCap) != INVALID_ID);
}

static const Node* findSource(const World& w) {
    for (int32_t i = 0; i < w.nodeCount(); ++i) {
        const Node& n = w.node(i);
        if (n.kind == NodeKind::Source) return &n;
    }
    return nullptr;
}

int main() {
    // 1) Fast source (period 5) spawns exactly 3x a period-15 source in 150
    //    ticks. Spawn gate stays open (belt cap 41, ~3 items in flight), so
    //    the counts are exact: ticks 1,6,...,146 (30) vs 1,16,...,136 (10).
    World fast(SimConfig{});
    buildLine(fast, 5, 100);
    World slow(SimConfig{});
    buildLine(slow, 15, 100);
    for (int i = 0; i < 150; ++i) {
        fast.tick();
        slow.tick();
    }
    JV_CHECK_EQ(fast.spawnedCount(), 30u);
    JV_CHECK_EQ(slow.spawnedCount(), 10u);
    JV_CHECK_EQ(fast.spawnedCount(), 3 * slow.spawnedCount());
    JV_CHECK(fast.checkInvariants());
    JV_CHECK(slow.checkInvariants());

    // 2) Sink storage is exact after deliveries: every delivered item in this
    //    line lands in the sink, and capacity is what was placed.
    const Node& fs = fast.node(1);
    JV_CHECK(fs.kind == NodeKind::Sink);
    JV_CHECK_EQ(fs.storageCapacity, 100u);
    JV_CHECK_EQ(fs.storageCount, fast.deliveredCount());
    JV_CHECK(fast.deliveredCount() > 0);

    // 3) spawnPeriod round-trips through save/load.
    World a(SimConfig{});
    buildLine(a, 7, 10);
    const std::string saved = saveLayout(a);
    JV_CHECK(saved.find("S 0 0 7") != std::string::npos);  // period is written
    World b(SimConfig{});
    JV_CHECK(loadLayout(b, saved));
    const Node* bs = findSource(b);
    JV_CHECK(bs != nullptr);
    JV_CHECK_EQ(bs->spawnPeriod, 7u);
    JV_CHECK_EQ(b.node(1).storageCapacity, 10u);  // sink cap survives too

    // 4) Backward compatible: legacy 'S x y' (no period) loads as 15, and a
    //    fresh default source is 15.
    World c(SimConfig{});
    JV_CHECK(loadLayout(c, "#JVL1\nS 0 0\nB 1 0 1 2\nK 3 0 5\n"));
    const Node* cs = findSource(c);
    JV_CHECK(cs != nullptr);
    JV_CHECK_EQ(cs->spawnPeriod, 15u);
    World d(SimConfig{});
    const uint32_t defSrc = d.placeSource(GridCell{0, 0});
    JV_CHECK_EQ(d.node(defSrc).spawnPeriod, 15u);
    // Zero period is clamped to the default (never a 0-tick spawn loop).
    World e(SimConfig{});
    const uint32_t zeroSrc = e.placeSource(GridCell{0, 0}, 0);
    JV_CHECK_EQ(e.node(zeroSrc).spawnPeriod, 15u);
    // Malformed period is rejected atomically (fresh world).
    World f(SimConfig{});
    JV_CHECK(!loadLayout(f, "#JVL1\nS 0 0 0\n"));
    JV_CHECK_EQ(f.nodeCount(), 0);

    JV_REPORT();
}
