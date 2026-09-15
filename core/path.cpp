// JoyVeyor core — GridStraight path implementation.
#include "path.h"

namespace jv {

namespace {
// World-space center of a cell (cell (x,y) spans [x, x+cs) x [y, y+cs)).
inline Vec2 cellCenter(int32_t x, int32_t y, float cs) {
    return Vec2(static_cast<float>(x) + 0.5f * cs, static_cast<float>(y) + 0.5f * cs);
}
}  // namespace

Vec2 GridStraightPath::entryPoint() const {
    const Vec2 c = cellCenter(cellOrigin.x, cellOrigin.y, cellSize);
    return c - dirVec(dir) * (0.5f * cellSize);
}

Vec2 GridStraightPath::exitPoint() const {
    // entry + dir * length: exact for cardinal directions (unit vectors).
    return entryPoint() + dirVec(dir) * length;
}

Vec2 GridStraightPath::pos(float arcPos) const {
    if (arcPos < 0.0f) arcPos = 0.0f;
    if (arcPos > length) arcPos = length;
    return entryPoint() + dirVec(dir) * arcPos;
}

float GridStraightPath::arcPosAt(const Vec2& p) const {
    const Vec2 e = entryPoint();
    const Vec2 d = dirVec(dir);
    // Projection onto the path line (unit direction).
    return (p.x - e.x) * d.x + (p.y - e.y) * d.y;
}

GridStraightPath makeGridStraightPath(int32_t x, int32_t y, Dir dir, int32_t len,
                                      float cellSize) {
    GridStraightPath p;
    p.cellOrigin = GridCell{x, y};
    p.dir = dir;
    p.cellSize = cellSize;
    p.length = static_cast<float>(len) * cellSize;
    return p;
}

}  // namespace jv
