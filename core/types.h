// JoyVeyor core — shared types (README §2.1).
// Engine-agnostic, zero dependencies, C++17.
#pragma once

#include <cstdint>

namespace jv {

// ---- Simulation configuration (fixed-tick, 30 Hz default) ----
struct SimConfig {
    float cellSize = 1.0f;        // world units per grid cell
    float itemLength = 0.2f;      // item extent along the path (world units)
    float minGap = 0.05f;         // minimum arc-length spacing between items
    float beltSpeed = 1.0f;       // world units / second
    float dt = 1.0f / 30.0f;      // fixed tick length (30 Hz)
};

// ---- Cardinal grid directions (y grows "south", i.e. row-major screen grid) ----
enum Dir : uint8_t {
    N = 0,
    E = 1,
    S = 2,
    W = 3,
};

struct GridCell {
    int32_t x = 0;
    int32_t y = 0;

    bool operator==(const GridCell& o) const { return x == o.x && y == o.y; }
    bool operator!=(const GridCell& o) const { return !(*this == o); }
};

// ---- Item lifecycle state (README §2.1) ----
enum class ItemState : uint8_t {
    Moving = 0,          // advancing along its belt
    HeldBackpressure = 1,  // stopped by downstream pressure; resumes FIFO
    QueuedAtNode = 2,    // waiting in a node's bounded FIFO
    Delivering = 3,      // reserved: in-flight handoff to a consuming node
};

// ---- Node kinds (README §2.1) ----
enum class NodeKind : uint8_t {
    Source = 0,
    Sink = 1,
    Splitter = 2,
    Merger = 3,
    Junction = 4,
};

// ---- Belt path kinds (README §2.1; MVP implements GridStraight) ----
enum class PathKind : uint8_t {
    GridStraight = 0,
    Spline = 1,
};

// Dense-ID sentinel for "no belt / no node / no next".
constexpr uint32_t INVALID_ID = ~0u;

// ---- Minimal 2D vector ----
struct Vec2 {
    float x = 0.0f;
    float y = 0.0f;

    Vec2() = default;
    Vec2(float x_, float y_) : x(x_), y(y_) {}

    Vec2 operator+(const Vec2& o) const { return Vec2(x + o.x, y + o.y); }
    Vec2 operator-(const Vec2& o) const { return Vec2(x - o.x, y - o.y); }
    Vec2 operator*(float s) const { return Vec2(x * s, y * s); }
    bool operator==(const Vec2& o) const { return x == o.x && y == o.y; }
};

inline Vec2 dirVec(Dir d) {
    switch (d) {
        case N: return Vec2(0.0f, -1.0f);
        case E: return Vec2(1.0f, 0.0f);
        case S: return Vec2(0.0f, 1.0f);
        case W: return Vec2(-1.0f, 0.0f);
    }
    return Vec2(0.0f, 0.0f);
}

// Neighbor of cell c one step in direction d.
inline GridCell cellOffset(const GridCell& c, Dir d) {
    const Vec2 v = dirVec(d);
    return GridCell{c.x + static_cast<int32_t>(v.x != 0.0f ? (v.x > 0.0f ? 1 : -1) : 0),
                    c.y + static_cast<int32_t>(v.y != 0.0f ? (v.y > 0.0f ? 1 : -1) : 0)};
}

// Opposite direction.
inline Dir oppositeDir(Dir d) {
    switch (d) {
        case N: return S;
        case S: return N;
        case E: return W;
        case W: return E;
    }
    return d;
}

// ---- Bounded FIFO (no heap; used for node queues, README §2.1/§3.2) ----
template <typename T, int32_t Cap>
struct FixedQueue {
    T data[Cap] = {};
    int32_t head = 0;
    int32_t size = 0;

    bool empty() const { return size == 0; }
    bool full() const { return size == Cap; }
    int32_t capacity() const { return Cap; }
    int32_t sizeOf() const { return size; }

    void push(const T& v) {
        if (full()) return;
        data[(head + size) % Cap] = v;
        ++size;
    }

    T pop() {
        T v = data[head];
        head = (head + 1) % Cap;
        --size;
        return v;
    }

    const T& front() const { return data[head]; }
    T& front() { return data[head]; }

    // Deterministic access in FIFO order: i = 0 is the front.
    const T& at(int32_t i) const { return data[(head + i) % Cap]; }
    T& at(int32_t i) { return data[(head + i) % Cap]; }

    void clear() { head = 0; size = 0; }
};

}  // namespace jv
