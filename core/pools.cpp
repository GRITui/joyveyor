// JoyVeyor core — ItemPool implementation (README §2.1).
#include "pools.h"

#include <cstring>
#include <cstdlib>

namespace jv {

namespace {
template <typename T>
T* allocArray(int32_t n) {
    T* p = new T[n]();
    return p;
}
}  // namespace

uint32_t ItemPool::add(uint16_t itemType, uint32_t beltId, float arcPos, uint8_t state,
                       uint32_t routeHint) {
    if (size_ >= capacity_) grow();
    const int32_t i = size_++;
    ids_[i] = static_cast<uint32_t>(i);  // dense id == index
    itemTypes_[i] = itemType;
    beltIds_[i] = beltId;
    arcPos_[i] = arcPos;
    states_[i] = state;
    routeHints_[i] = routeHint;
    nextOnBelt_[i] = INVALID_ID;
    alive_[i] = 1;
    nodeIds_[i] = INVALID_ID;
    prevArcPos_[i] = arcPos;
    ++liveCount_;
    return static_cast<uint32_t>(i);
}

void ItemPool::remove(uint32_t id) {
    if (id < static_cast<uint32_t>(size_) && alive_[id]) {
        alive_[id] = 0;
        --liveCount_;
    }
}

void ItemPool::compact() {
    int32_t w = 0;
    for (int32_t r = 0; r < size_; ++r) {
        if (!alive_[r]) continue;
        if (w != r) {
            ids_[w] = ids_[r];
            itemTypes_[w] = itemTypes_[r];
            beltIds_[w] = beltIds_[r];
            arcPos_[w] = arcPos_[r];
            states_[w] = states_[r];
            routeHints_[w] = routeHints_[r];
            nextOnBelt_[w] = nextOnBelt_[r];
            alive_[w] = 1;
            nodeIds_[w] = nodeIds_[r];
            prevArcPos_[w] = prevArcPos_[r];
        }
        ids_[w] = static_cast<uint32_t>(w);  // reassign dense id
        ++w;
    }
    size_ = w;
    liveCount_ = w;
}

void ItemPool::grow() {
    const int32_t ncap = capacity_ < kInitialCapacity ? kInitialCapacity : capacity_ * 2;

    uint32_t* ids2 = allocArray<uint32_t>(ncap);
    uint16_t* types2 = allocArray<uint16_t>(ncap);
    uint32_t* belts2 = allocArray<uint32_t>(ncap);
    float* arc2 = allocArray<float>(ncap);
    uint8_t* states2 = allocArray<uint8_t>(ncap);
    uint32_t* hints2 = allocArray<uint32_t>(ncap);
    uint32_t* next2 = allocArray<uint32_t>(ncap);
    uint8_t* alive2 = allocArray<uint8_t>(ncap);
    uint32_t* nodeIds2 = allocArray<uint32_t>(ncap);
    float* prevArc2 = allocArray<float>(ncap);
    for (int32_t i = 0; i < ncap; ++i) nodeIds2[i] = INVALID_ID;

    if (size_ > 0) {
        std::memcpy(ids2, ids_, static_cast<size_t>(size_) * sizeof(uint32_t));
        std::memcpy(types2, itemTypes_, static_cast<size_t>(size_) * sizeof(uint16_t));
        std::memcpy(belts2, beltIds_, static_cast<size_t>(size_) * sizeof(uint32_t));
        std::memcpy(arc2, arcPos_, static_cast<size_t>(size_) * sizeof(float));
        std::memcpy(states2, states_, static_cast<size_t>(size_) * sizeof(uint8_t));
        std::memcpy(hints2, routeHints_, static_cast<size_t>(size_) * sizeof(uint32_t));
        std::memcpy(next2, nextOnBelt_, static_cast<size_t>(size_) * sizeof(uint32_t));
        std::memcpy(alive2, alive_, static_cast<size_t>(size_) * sizeof(uint8_t));
        std::memcpy(nodeIds2, nodeIds_, static_cast<size_t>(size_) * sizeof(uint32_t));
        std::memcpy(prevArc2, prevArcPos_, static_cast<size_t>(size_) * sizeof(float));
    }

    delete[] ids_;
    delete[] itemTypes_;
    delete[] beltIds_;
    delete[] arcPos_;
    delete[] states_;
    delete[] routeHints_;
    delete[] nextOnBelt_;
    delete[] alive_;
    delete[] nodeIds_;
    delete[] prevArcPos_;

    ids_ = ids2;
    itemTypes_ = types2;
    beltIds_ = belts2;
    arcPos_ = arc2;
    states_ = states2;
    routeHints_ = hints2;
    nextOnBelt_ = next2;
    alive_ = alive2;
    nodeIds_ = nodeIds2;
    prevArcPos_ = prevArc2;
    capacity_ = ncap;
}

}  // namespace jv
