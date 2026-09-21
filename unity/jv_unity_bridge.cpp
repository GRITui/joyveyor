#include "jv_unity_bridge.h"

#include "core/save.h"
#include "core/world.h"

#include <cstdlib>
#include <cstring>

namespace {
inline jv::World* world(JVWorld w) { return static_cast<jv::World*>(w); }
}

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

uint32_t jv_place_source(JVWorld w, int32_t x, int32_t y, uint16_t spawnPeriod) {
    if (!w) return jv::INVALID_ID;
    return world(w)->placeSource(jv::GridCell{x, y}, spawnPeriod);
}

uint32_t jv_place_sink(JVWorld w, int32_t x, int32_t y, uint16_t capacity) {
    if (!w) return jv::INVALID_ID;
    return world(w)->placeSink(jv::GridCell{x, y}, capacity);
}

void jv_sink_storage(JVWorld w, uint32_t sinkId, uint16_t* count, uint16_t* cap) {
    if (!count || !cap) return;
    *count = 0;
    *cap = 0;
    if (!w) return;
    jv::World* wd = world(w);
    if (!wd->nodeAlive(sinkId) || wd->node(sinkId).kind != jv::NodeKind::Sink) return;
    const jv::Node& n = wd->node(sinkId);
    *count = n.storageCount;
    *cap = n.storageCapacity;
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

int jv_remove_belt(JVWorld w, uint32_t beltId) {
    return w && world(w)->removeBelt(beltId) ? 1 : 0;
}

int jv_remove_node(JVWorld w, uint32_t nodeId) {
    return w && world(w)->removeNode(nodeId) ? 1 : 0;
}

uint32_t jv_belt_at_cell(JVWorld w, int32_t x, int32_t y) {
    return w ? world(w)->beltAtCell(x, y) : jv::INVALID_ID;
}

uint32_t jv_node_at_cell(JVWorld w, int32_t x, int32_t y) {
    return w ? world(w)->nodeAtCell(x, y) : jv::INVALID_ID;
}

char* jv_save_layout(JVWorld w) {
    if (!w) return nullptr;
    const std::string s = jv::saveLayout(*world(w));
    char* out = static_cast<char*>(std::malloc(s.size() + 1));
    if (out) std::memcpy(out, s.c_str(), s.size() + 1);
    return out;
}

int jv_load_layout(JVWorld w, const char* text) {
    if (!w || !text) return 0;
    return jv::loadLayout(*world(w), text) ? 1 : 0;
}

void jv_free(void* p) {
    std::free(p);
}

const char* jv_last_placement_error(JVWorld w) {
    return w ? world(w)->lastPlacementError().c_str() : "";
}

}  // extern "C"
