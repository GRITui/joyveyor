// JoyVeyor Unity bridge — plain C API (extern "C").
#ifndef JV_UNITY_BRIDGE_H
#define JV_UNITY_BRIDGE_H

#include <cstdint>

#ifdef __cplusplus
extern "C" {
#endif

typedef void* JVWorld;

JVWorld jv_world_create(void);
void jv_world_destroy(JVWorld w);
void jv_world_advance(JVWorld w, float seconds);
uint64_t jv_world_tick_count(JVWorld w);
uint32_t jv_place_belt(JVWorld w, int32_t x, int32_t y, uint8_t dir, int32_t len);
uint32_t jv_place_source(JVWorld w, int32_t x, int32_t y);
uint32_t jv_place_sink(JVWorld w, int32_t x, int32_t y, uint16_t capacity);
uint32_t jv_place_splitter(JVWorld w, int32_t x, int32_t y, uint8_t outA, uint8_t outB);
uint32_t jv_place_merger(JVWorld w, int32_t x, int32_t y, uint8_t inA, uint8_t inB, uint8_t out);
uint64_t jv_spawned(JVWorld w);
uint64_t jv_delivered(JVWorld w);
uint64_t jv_consumed(JVWorld w);
int32_t jv_item_count(JVWorld w);
int32_t jv_snapshot_items(JVWorld w, float alpha, float* itemPosXY, uint32_t* itemIds, int32_t maxItems);
int jv_has_cycle(JVWorld w);
int jv_is_deadlocked(JVWorld w);
int jv_check_invariants(JVWorld w);
int jv_remove_belt(JVWorld w, uint32_t beltId);
int jv_remove_node(JVWorld w, uint32_t nodeId);
uint32_t jv_belt_at_cell(JVWorld w, int32_t x, int32_t y);
uint32_t jv_node_at_cell(JVWorld w, int32_t x, int32_t y);
char* jv_save_layout(JVWorld w);   // malloc'd; free with jv_free
int jv_load_layout(JVWorld w, const char* text);  // 1 ok / 0 rejected (world untouched)
const char* jv_last_placement_error(JVWorld w);   // "" if last placement ok
void jv_free(void* p);

#ifdef __cplusplus
}
#endif

#endif  // JV_UNITY_BRIDGE_H
