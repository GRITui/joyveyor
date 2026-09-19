// JoyVeyor core — save/load round-trip test (README §3.4, Phase 4).
//
// Verifies:
//  * save → load reproduces the exact layout (belts + nodes, dense-id order).
//  * save is deterministic (two saves of the same world are byte-identical).
//  * a loaded world still simulates (items flow, invariants hold).
//  * malformed input is rejected and leaves the world untouched (atomic).
#include "save.h"
#include "world.h"

#include "test.h"

using namespace jv;

// Build the demo layout (mirrors JoyveyorRunner.PlaceDemoLevel).
static void buildDemo(World& w) {
    w.placeSource(GridCell{0, 0});
    w.placeBelt(1, 0, E, 5);
    w.placeSplitter(GridCell{6, 0}, E, S);
    w.placeBelt(7, 0, E, 4);
    w.placeSink(GridCell{11, 0}, 50);
    w.placeBelt(6, 1, S, 4);
    w.placeMerger(GridCell{6, 5}, S, W, E);
    w.placeBelt(7, 5, E, 4);
    w.placeSink(GridCell{11, 5}, 50);
}

static void checkLayoutEqual(const World& a, const World& b) {
    JV_CHECK_EQ(a.beltCount(), b.beltCount());
    JV_CHECK_EQ(a.nodeCount(), b.nodeCount());
    for (int32_t i = 0; i < a.beltCount(); ++i) {
        const Belt& x = a.belt(i);
        const Belt& y = b.belt(i);
        JV_CHECK_EQ(x.cellOrigin.x, y.cellOrigin.x);
        JV_CHECK_EQ(x.cellOrigin.y, y.cellOrigin.y);
        JV_CHECK_EQ(x.dir, y.dir);
        JV_CHECK_EQ(x.lenCells, y.lenCells);
    }
    for (int32_t i = 0; i < a.nodeCount(); ++i) {
        const Node& x = a.node(i);
        const Node& y = b.node(i);
        JV_CHECK_EQ(x.kind, y.kind);
        JV_CHECK_EQ(x.cell.x, y.cell.x);
        JV_CHECK_EQ(x.cell.y, y.cell.y);
        JV_CHECK_EQ(x.storageCapacity, y.storageCapacity);
    }
}

int main() {
    // 1) Round-trip: demo layout survives save → load.
    World a(SimConfig{});
    buildDemo(a);
    JV_CHECK_EQ(a.beltCount(), 4);
    JV_CHECK_EQ(a.nodeCount(), 5);

    std::string saved = saveLayout(a);
    JV_CHECK(saved.size() > 0);
    JV_CHECK(saved.rfind("#JVL1", 0) == 0);  // starts with header

    World b(SimConfig{});
    JV_CHECK(loadLayout(b, saved));
    checkLayoutEqual(a, b);

    // 2) Determinism: saving the loaded world reproduces the same text.
    std::string saved2 = saveLayout(b);
    JV_CHECK(saved == saved2);

    // 3) A loaded world still simulates: run ticks, items flow, invariants hold.
    for (int i = 0; i < 300; ++i) b.tick();
    JV_CHECK(b.spawnedCount() > 0);
    JV_CHECK(b.deliveredCount() > 0);
    JV_CHECK(b.checkInvariants());

    // 4) Atomicity: malformed input is rejected, world left untouched.
    World c(SimConfig{});
    buildDemo(c);
    const int32_t beltsBefore = c.beltCount();
    const int32_t nodesBefore = c.nodeCount();

    JV_CHECK(!loadLayout(c, "garbage no header"));        // no header
    JV_CHECK(!loadLayout(c, "#JVL1\nX 0 0\n"));           // bad tag
    JV_CHECK(!loadLayout(c, "#JVL1\nB 0 0 9 3\n"));       // bad dir
    JV_CHECK(!loadLayout(c, "#JVL1\nK 0 0 0\n"));         // sink cap < 1
    JV_CHECK(!loadLayout(c, "#JVL1\nB 0 0 1 0\n"));       // belt len < 1
    JV_CHECK(!loadLayout(c, "#JVL1\nT 0 0 1 1\n"));       // splitter same dir
    // World unchanged after every rejection.
    JV_CHECK_EQ(c.beltCount(), beltsBefore);
    JV_CHECK_EQ(c.nodeCount(), nodesBefore);

    // 5) Empty save (header only) loads to an empty world.
    World d(SimConfig{});
    buildDemo(d);
    JV_CHECK(loadLayout(d, "#JVL1\n"));
    JV_CHECK_EQ(d.beltCount(), 0);
    JV_CHECK_EQ(d.nodeCount(), 0);

    JV_REPORT();
}
