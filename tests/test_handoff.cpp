// Handoff: item crosses a belt-to-belt junction with no loss/overlap.
#include "../core/world.h"
#include "test.h"

using namespace jv;

int main() {
    World w;
    const uint32_t b1 = w.placeBelt(0, 0, E, 3);
    const uint32_t b2 = w.placeBelt(3, 0, E, 3);
    JV_CHECK(b1 != INVALID_ID);
    JV_CHECK(b2 != INVALID_ID);
    JV_CHECK_EQ(w.belt(b1).nextBelt, b2);

    w.injectItemAtEntry(b1);
    bool sawOnB2 = false;
    for (int t = 0; t < 200; ++t) {
        w.advance(w.config().dt);
        JV_CHECK(w.checkInvariants());
        JV_CHECK_EQ(w.itemCount(), 1);
        if (w.itemCount() == 1) {
            const uint32_t id = w.items().idOf(0);
            if (w.items().alive(id) && w.items().beltId(id) == b2) sawOnB2 = true;
        }
    }
    JV_CHECK(sawOnB2);

    JV_REPORT();
}
