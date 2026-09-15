// JoyVeyor core — dense-ID pools (README §2.1).
//
// Design:
//  * Dense 32-bit IDs: a live element's id == its index in the pool.
//  * add() appends (id = size). remove() marks the slot dead. compact()
//    shifts live elements down and reassigns dense ids (id = new index).
//  * The hot item pool is Structure-of-Arrays (cache friendly, deterministic
//    iteration, trivially parallelizable later).
//  * No raw pointers in any serializable struct; all references are dense ids.
//  * Growth allocates only when the pool exceeds capacity (never per tick in
//    the tested scenarios).
#pragma once

#include "types.h"

namespace jv {

// ---------------------------------------------------------------------------
// ItemPool — SoA hot pool for ItemInstance (README §2.1).
//
// Fields (parallel arrays, one per item):
//   id, itemType, beltId, arcPos, state, routeHint, nextOnBelt,
//   nodeId (node the item is queued at; INVALID while on a belt),
//   prevArcPos (previous tick position, for render interpolation)
//
// nextOnBelt links items on the same belt in arcPos order (front → back).
// Items move uniformly and never overtake, so the order is maintained
// without per-tick resorting (README §2.1).
// ---------------------------------------------------------------------------
class ItemPool {
public:
    static constexpr int32_t kInitialCapacity = 4096;

    ItemPool() = default;
    ItemPool(const ItemPool&) = delete;
    ItemPool& operator=(const ItemPool&) = delete;

    // Append a new item; returns its dense id (== index).
    uint32_t add(uint16_t itemType, uint32_t beltId, float arcPos, uint8_t state,
                 uint32_t routeHint);

    // Mark an item dead (still occupies its slot until compact()).
    void remove(uint32_t id);

    // Shift live items to the front; reassigns dense ids (id = new index).
    // nextOnBelt links are rewritten for surviving items.
    void compact();

    int32_t size() const { return size_; }
    int32_t capacity() const { return capacity_; }
    int32_t liveCount() const { return liveCount_; }
    int32_t deadCount() const { return size_ - liveCount_; }
    bool alive(uint32_t id) const { return id < static_cast<uint32_t>(size_) && alive_[id]; }

    // ---- SoA access (valid while alive(id)) ----
    uint32_t idOf(uint32_t i) const { return ids_[i]; }
    uint16_t itemType(uint32_t i) const { return itemTypes_[i]; }
    uint32_t beltId(uint32_t i) const { return beltIds_[i]; }
    float arcPos(uint32_t i) const { return arcPos_[i]; }
    uint8_t state(uint32_t i) const { return states_[i]; }
    uint32_t routeHint(uint32_t i) const { return routeHints_[i]; }
    uint32_t nextOnBelt(uint32_t i) const { return nextOnBelt_[i]; }
    uint32_t nodeId(uint32_t i) const { return nodeIds_[i]; }
    float prevArcPos(uint32_t i) const { return prevArcPos_[i]; }

    void setBeltId(uint32_t i, uint32_t b) { beltIds_[i] = b; }
    void setArcPos(uint32_t i, float p) { arcPos_[i] = p; }
    void setState(uint32_t i, uint8_t s) { states_[i] = s; }
    void setRouteHint(uint32_t i, uint32_t h) { routeHints_[i] = h; }
    void setNextOnBelt(uint32_t i, uint32_t n) { nextOnBelt_[i] = n; }
    void setNodeId(uint32_t i, uint32_t n) { nodeIds_[i] = n; }
    void setPrevArcPos(uint32_t i, float p) { prevArcPos_[i] = p; }

    // Raw SoA array access (for hashing / bulk reads).
    const uint32_t* ids() const { return ids_; }
    const uint16_t* itemTypes() const { return itemTypes_; }
    const uint32_t* beltIds() const { return beltIds_; }
    const float* arcPosArr() const { return arcPos_; }
    const uint8_t* states() const { return states_; }
    const uint32_t* routeHints() const { return routeHints_; }
    const uint32_t* nextOnBeltArr() const { return nextOnBelt_; }
    const uint32_t* nodeIdsArr() const { return nodeIds_; }
    const float* prevArcPosArr() const { return prevArcPos_; }
    const uint8_t* aliveArr() const { return alive_; }

private:
    void grow();

    uint32_t* ids_ = nullptr;
    uint16_t* itemTypes_ = nullptr;
    uint32_t* beltIds_ = nullptr;
    float* arcPos_ = nullptr;
    uint8_t* states_ = nullptr;
    uint32_t* routeHints_ = nullptr;
    uint32_t* nextOnBelt_ = nullptr;
    uint32_t* nodeIds_ = nullptr;
    float* prevArcPos_ = nullptr;
    uint8_t* alive_ = nullptr;
    int32_t size_ = 0;
    int32_t capacity_ = 0;
    int32_t liveCount_ = 0;
};

// ---------------------------------------------------------------------------
// DensePool<T> — dense-ID pool of plain structs (Belt / Node / Network).
// ---------------------------------------------------------------------------
template <typename T, int32_t InitialCapacity>
class DensePool {
public:
    DensePool() { growTo(InitialCapacity); }
    DensePool(const DensePool&) = delete;
    DensePool& operator=(const DensePool&) = delete;

    // Append a default-constructed element; returns its dense id.
    uint32_t add() {
        if (size_ >= capacity_) grow();
        T& e = data_[size_];
        e.id = static_cast<uint32_t>(size_);
        alive_[size_] = 1;
        return static_cast<uint32_t>(size_++);
    }

    void remove(uint32_t id) {
        if (id < static_cast<uint32_t>(size_)) alive_[id] = 0;
    }

    // Shift live elements down; reassigns dense ids (id = new index).
    void compact() {
        int32_t w = 0;
        for (int32_t r = 0; r < size_; ++r) {
            if (!alive_[r]) continue;
            if (w != r) data_[w] = data_[r];
            data_[w].id = static_cast<uint32_t>(w);
            alive_[w] = 1;
            ++w;
        }
        size_ = w;
    }

    int32_t size() const { return size_; }
    int32_t capacity() const { return capacity_; }
    bool alive(uint32_t id) const { return id < static_cast<uint32_t>(size_) && alive_[id]; }

    T* data(uint32_t id) { return &data_[id]; }
    const T* data(uint32_t id) const { return &data_[id]; }
    T* begin() { return data_; }
    const T* begin() const { return data_; }
    T* end() { return data_ + size_; }
    const T* end() const { return data_ + size_; }

    T& operator[](uint32_t id) { return data_[id]; }
    const T& operator[](uint32_t id) const { return data_[id]; }

private:
    void growTo(int32_t n) {
        if (n <= capacity_) return;
        T* nd = new T[n]();
        uint8_t* na = new uint8_t[n]();
        for (int32_t i = 0; i < size_; ++i) {
            nd[i] = data_[i];
            na[i] = alive_[i];
        }
        delete[] data_;
        delete[] alive_;
        data_ = nd;
        alive_ = na;
        capacity_ = n;
    }

    void grow() { growTo(capacity_ < 8 ? 8 : capacity_ * 2); }

    T* data_ = nullptr;
    uint8_t* alive_ = nullptr;
    int32_t size_ = 0;
    int32_t capacity_ = 0;
};

}  // namespace jv
