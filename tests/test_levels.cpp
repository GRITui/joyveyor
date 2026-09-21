// JoyVeyor core — #JVLG1 level format + 10 authored levels + winnability
// (v1.0 S2, jv-design-gameplay §Level Format / §10-Level Outline).
//
// For each level (unity-project/Assets/Levels/levelNN.jvl):
//   * parse the #JVLG1 header (name/grid/time_limit/par_time/par_pieces/budget);
//   * apply the locked pieces to a fresh World;
//   * layer the reference solution (reference/levelNN.sol) on top;
//   * advance ticks to time_limit and assert EVERY sink's storageCount ==
//     capacity (quota met) before the limit  -> the level is winnable;
//   * assert the reference hits par_time (2★): the last quota is met at or
//     before par_time;
//   * assert the reference uses <= budget player pieces (3★ piece budget).
//
// If a reference can't hit par, the level or the reference is wrong — the
// assertion is the contract, not the thing to loosen.
#include "../core/level.h"
#include "../core/save.h"
#include "../core/world.h"

#include "test.h"

#include <cstdlib>
#include <string>
#include <vector>

using namespace jv;

namespace {

// Resolve the Levels dir: JV_LEVELS env, else the default relative to the
// build dir (tests run from build/).
std::string levelsDir() {
    if (const char* e = std::getenv("JV_LEVELS")) return std::string(e);
    return "../unity-project/Assets/Levels";
}

// Find the tick at which all sinks first reach capacity (0 if never).
uint64_t completionTick(World& w, uint32_t timeLimit) {
    auto allFull = [&]() {
        for (int32_t i = 0; i < w.nodeCount(); ++i) {
            const Node& n = w.node(i);
            if (n.kind == NodeKind::Sink && n.storageCount < n.storageCapacity)
                return false;
        }
        return true;
    };
    for (uint64_t t = 0; t <= timeLimit; ++t) {
        w.tick();
        if (allFull()) return w.tickCount();
    }
    return 0;  // never met
}

bool runLevel(const std::string& jvlPath, const std::string& solPath,
              const char* label) {
    std::string text;
    JV_CHECK(readFile(jvlPath, text));
    Level lv;
    JV_CHECK(parseLevel(text, lv));
    JV_CHECK(!lv.name.empty());
    JV_CHECK(lv.gridW > 0 && lv.gridH > 0);
    JV_CHECK(lv.timeLimit > 0);
    JV_CHECK(lv.parTime > 0 && lv.parTime <= lv.timeLimit);
    JV_CHECK(lv.budget >= 1);
    JV_CHECK(!lv.ops.empty());

    std::string solText;
    JV_CHECK(readFile(solPath, solText));
    std::vector<PlacementOp> sol;
    JV_CHECK(parsePlacementOps(solText, sol));
    JV_CHECK(!sol.empty());

    // Reference solution must fit the 3★ piece budget.
    JV_CHECK(sol.size() <= lv.budget);

    World w(SimConfig{});
    JV_CHECK(applyLevel(w, lv));
    JV_CHECK(applyOps(w, sol));  // layer player pieces on the locked setup
    JV_CHECK(w.checkInvariants());

    const uint64_t done = completionTick(w, lv.timeLimit);
    JV_CHECK(done != 0);                 // winnable within time_limit
    JV_CHECK(done <= lv.timeLimit);      // ... before the limit
    JV_CHECK(done <= lv.parTime);        // reference hits par (2★)
    JV_CHECK(w.checkInvariants());       // no conservation/overlap breakage

    // Every sink is exactly full (quota met, no overflow past capacity).
    for (int32_t i = 0; i < w.nodeCount(); ++i) {
        const Node& n = w.node(i);
        if (n.kind == NodeKind::Sink)
            JV_CHECK(n.storageCount == n.storageCapacity);
    }
    (void)label;
    return true;
}

}  // namespace

int main() {
    const std::string dir = levelsDir();
    for (int i = 1; i <= 10; ++i) {
        char jvl[64], sol[64];
        std::snprintf(jvl, sizeof(jvl), "%s/level%02d.jvl", dir.c_str(), i);
        std::snprintf(sol, sizeof(sol), "%s/reference/level%02d.sol", dir.c_str(), i);
        runLevel(jvl, sol, "level");
    }

    // Malformed level is rejected atomically (level unchanged).
    Level bad;
    JV_CHECK(!parseLevel("#JVLG1\nname X\ngrid 24 16\n", bad));  // missing fields
    JV_CHECK(!parseLevel("#JVL1\nS 0 0\n", bad));                // wrong header
    JV_CHECK(!parseLevel("#JVLG1\nname X\ngrid 24 16\ntime_limit 900\n"
                         "par_time 950\npar_pieces 3\nbudget 4\nS 0 0 15\n", bad));  // par>limit
    JV_CHECK(bad.name.empty());

    JV_REPORT();
}
