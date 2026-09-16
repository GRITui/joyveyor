#include "jv_unity_bridge.h"

#include "core/world.h"

namespace {
inline jv::World* world(JVWorld w) { return static_cast<jv::World*>(w); }
}

// jv::World::renderSnapshot is declared in core/world.h but not defined in
// core/ (static linking never pulls it in). Provide the definition here so
// the shared library links; it uses only World's public accessors.
namespace jv {

RenderSnapshot World::renderSnapshot(float alpha) const {
    RenderSnapshot snap;
    snap.tick = tickCount();
    snap.alpha = alpha;
    const ItemPool& pool = items();
    const int32_t total = pool.size();
    for (int32_t i = 0; i < total && snap.itemCount < RenderSnapshot::kMaxItems; ++i) {
        if (!pool.alive(i)) continue;
        const uint32_t it = pool.idOf(i);
        const uint32_t nid = pool.nodeId(i);
        Vec2 p;
        if (nid != INVALID_ID) {
            // Queued at a node: render at the node's cell center.
            const Node& n = node(nid);
            const float cs = config().cellSize;
            p = Vec2(static_cast<float>(n.cell.x) + 0.5f * cs,
                     static_cast<float>(n.cell.y) + 0.5f * cs);
        } else {
            // On a belt: interpolate between the previous tick's arc
            // position and the current one (alpha 0 → N-1, 1 → N).
            const Belt& b = belt(pool.beltId(i));
            const float prev = pool.prevArcPos(i);
            const float arc = prev + (pool.arcPos(i) - prev) * alpha;
            p = b.path().pos(arc);
        }
        snap.itemPos[snap.itemCount] = p;
        snap.itemId[snap.itemCount] = it;
        ++snap.itemCount;
    }
    return snap;
}

}  // namespace jv

extern "C" {

JVWorld jv_world_create(void) {
    return new jv::World(jv::SimConfig());
}

void jv_world_destroy(JVWorld w) {
    delete world(w);
}

void jv_world_advance(JVWorld w, float seconds) {
    if (w) world(w)->advance(seconds);
}

uint64_t jv_world_tick_count(JVWorld w) {
    return w ? world(w)->tickCount() : 0;
}

uint32_t jv_place_belt(JVWorld w, int32_t x, int32_t y, uint8_t dir, int32_t len) {
    if (!w) return jv::INVALID_ID;
    return world(w)->placeBelt(x, y, static_cast<jv::Dir>(dir), static_cast<int>(len));
}

uint32_t jv_place_source(JVWorld w, int32_t x, int32_t y) {
    if (!w) return jv::INVALID_ID;
    return world(w)->placeSource(jv::GridCell{x, y});
}

uint32_t jv_place_sink(JVWorld w, int32_t x, int32_t y, uint16_t capacity) {
    if (!w) return jv::INVALID_ID;
    return world(w)->placeSink(jv::GridCell{x, y}, capacity);
}

uint32_t jv_place_splitter(JVWorld w, int32_t x, int32_t y, uint8_t outA, uint8_t outB) {
    if (!w) return jv::INVALID_ID;
    return world(w)->placeSplitter(jv::GridCell{x, y},
                                   static_cast<jv::Dir>(outA),
                                   static_cast<jv::Dir>(outB));
}

uint32_t jv_place_merger(JVWorld w, int32_t x, int32_t y, uint8_t inA, uint8_t inB, uint8_t out) {
    if (!w) return jv::INVALID_ID;
    return world(w)->placeMerger(jv::GridCell{x, y},
                                 static_cast<jv::Dir>(inA),
                                 static_cast<jv::Dir>(inB),
                                 static_cast<jv::Dir>(out));
}

uint64_t jv_spawned(JVWorld w) {
    return w ? world(w)->spawnedCount() : 0;
}

uint64_t jv_delivered(JVWorld w) {
    return w ? world(w)->deliveredCount() : 0;
}

uint64_t jv_consumed(JVWorld w) {
    return w ? world(w)->consumedCount() : 0;
}

int32_t jv_item_count(JVWorld w) {
    return w ? world(w)->itemCount() : 0;
}

int32_t jv_snapshot_items(JVWorld w, float alpha, float* itemPosXY, uint32_t* itemIds, int32_t maxItems) {
    if (!w || !itemPosXY || !itemIds || maxItems <= 0) return 0;
    const jv::RenderSnapshot snap = world(w)->renderSnapshot(alpha);
    const int32_t n = snap.itemCount < maxItems ? snap.itemCount : maxItems;
    for (int32_t i = 0; i < n; ++i) {
        itemPosXY[2 * i] = snap.itemPos[i].x;
        itemPosXY[2 * i + 1] = snap.itemPos[i].y;
        itemIds[i] = snap.itemId[i];
    }
    return n;
}

int jv_has_cycle(JVWorld w) {
    return w && world(w)->hasCycle() ? 1 : 0;
}

int jv_is_deadlocked(JVWorld w) {
    return w && world(w)->isDeadlocked() ? 1 : 0;
}

int jv_check_invariants(JVWorld w) {
    return w && world(w)->checkInvariants() ? 1 : 0;
}

}  // extern "C"
