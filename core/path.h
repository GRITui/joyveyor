// JoyVeyor core — grid path parameterization (README §2.1/§2.2).
//
// GridStraight: a straight conveyor path along cell centers.
//   * The path runs from the entry edge of cellOrigin to the exit edge of the
//     last cell, through cell centers.
//   * arcPos is arc length from the entry point (world units).
//   * pos(0) == entry point and pos(length) == exit point, exactly, for all
//     four cardinal directions (unit direction vectors, integer cell math).
#pragma once

#include "types.h"

namespace jv {

struct GridStraightPath {
    GridCell cellOrigin{};
    Dir dir = E;
    float length = 0.0f;   // total path length (world units)
    float cellSize = 1.0f; // world units per cell

    // World-space entry point (midpoint of the entry edge of cellOrigin).
    Vec2 entryPoint() const;
    // World-space exit point (midpoint of the exit edge of the last cell).
    Vec2 exitPoint() const;

    // Position along the path at arc length arcPos (clamped to [0, length]).
    Vec2 pos(float arcPos) const;

    // Arc length of the projection of world point p onto the path line
    // (unclamped; exact for points on the path).
    float arcPosAt(const Vec2& p) const;
};

// Build the path for a belt placed at (x, y) with direction dir spanning
// `len` cells (snap-to-grid: input cells are already integer).
GridStraightPath makeGridStraightPath(int32_t x, int32_t y, Dir dir, int32_t len,
                                      float cellSize);

}  // namespace jv
