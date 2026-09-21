// JoyVeyor core — S3 bridge regression guard: the game-session entry points
// (jv_world_reset / jv_world_tick) must behave exactly like the World API the
// C# GameSession drives them through:
//   * jv_world_tick == one fixed 30 Hz tick (tickCount +1, no accumulator drift)
//   * jv_world_reset clears the world (nodes, items, counters, tickCount)
//   * reset + re-place + tick reproduces the S2 level-1 reference completion
//     (257 ticks) — the same path the headless GameTest probe asserts.
#include "../unity/jv_unity_bridge.h"
#include "../core/level.h"
#include "../core/save.h"

#include "test.h"

#include <string>
#include <vector>

using namespace jv;

namespace {

std::string levelsDir() {
    if (const char* e = std::getenv("JV_LEVELS")) return std::string(e);
    return "../unity-project/Assets/Levels";
}

}  // namespace

int main() {
    JVWorld w = jv_world_create();
    JV_CHECK(w != nullptr);

    // ---- jv_world_tick: exactly one tick per call ----
    JV_CHECK_EQ(jv_world_tick_count(w), 0ULL);
    jv_world_tick(w);
    JV_CHECK_EQ(jv_world_tick_count(w), 1ULL);
    jv_world_tick(w);
    JV_CHECK_EQ(jv_world_tick_count(w), 2ULL);

    // ---- jv_world_reset: clears a populated world ----
    JV_CHECK_EQ(jv_place_source(w, 0, 0, 15), 0u);
    JV_CHECK(jv_place_belt(w, 1, 0, 1 /*E*/, 4) != INVALID_ID);
    JV_CHECK(jv_place_sink(w, 5, 0, 10) != INVALID_ID);
    for (int i = 0; i < 60; ++i) jv_world_tick(w);
    JV_CHECK(jv_spawned(w) > 0);
    JV_CHECK(jv_item_count(w) >= 0);

    jv_world_reset(w);
    JV_CHECK_EQ(jv_world_tick_count(w), 0ULL);
    JV_CHECK_EQ(jv_spawned(w), 0ULL);
    JV_CHECK_EQ(jv_delivered(w), 0ULL);
    JV_CHECK_EQ(jv_consumed(w), 0ULL);
    JV_CHECK_EQ(jv_item_count(w), 0);
    JV_CHECK_EQ(jv_node_at_cell(w, 0, 0), 0xFFFFFFFFu);  // source gone
    JV_CHECK_EQ(jv_belt_at_cell(w, 1, 0), 0xFFFFFFFFu);  // belt gone
    JV_CHECK(jv_check_invariants(w) == 1);

    // ---- reset + re-place reproduces the level-1 reference completion ----
    // Same ops the C# GameSession applies (locked pieces, then the .sol),
    // then one jv_world_tick per frame until every sink is full.
    std::string text;
    JV_CHECK(readFile(levelsDir() + "/level01.jvl", text));
    Level lv;
    JV_CHECK(parseLevel(text, lv));
    JV_CHECK(applyLevel(*static_cast<World*>(w), lv));  // reset + locked

    std::string solText;
    JV_CHECK(readFile(levelsDir() + "/reference/level01.sol", solText));
    std::vector<PlacementOp> sol;
    JV_CHECK(parsePlacementOps(solText, sol));
    JV_CHECK(applyOps(*static_cast<World*>(w), sol));

    auto allFull = [&]() {
        for (int32_t i = 0; i < static_cast<World*>(w)->nodeCount(); ++i) {
            const Node& n = static_cast<World*>(w)->node(i);
            if (n.kind == NodeKind::Sink && n.storageCount < n.storageCapacity)
                return false;
        }
        return true;
    };

    uint64_t done = 0;
    for (uint64_t t = 0; t <= lv.timeLimit; ++t) {
        jv_world_tick(w);
        if (allFull()) { done = jv_world_tick_count(w); break; }
    }
    JV_CHECK(done != 0);
    JV_CHECK_EQ(done, 257ULL);  // S2 reference: level 1 completes at tick 257
    JV_CHECK(jv_check_invariants(w) == 1);

    jv_world_destroy(w);
    JV_REPORT();
}
