// JoyVeyor core — level save/load (README §3.4, Phase 4).
//
// A save is the *layout* — live belts + live nodes — as text placement ops.
// Items, networks and connections are NOT stored: they are recomputed by the
// placement code on load (order-independent connections, README §2.1). So a
// save is small and always valid.
//
// Format: `#JVL1` header, then one element per line. Nodes first, then belts
// (so belt placement can link to already-placed nodes; belt-to-belt links are
// derived from geometry at placement time).
//   S x y           source
//   K x y cap       sink
//   T x y a b       splitter (outA, outB)
//   M x y a b o     merger (inA, inB, out)
//   B x y dir len   belt
//
// load() is atomic: it parses + validates the whole string, then applies. A
// malformed file leaves the world untouched.
//
// Determinism: iteration is dense-id ascending (no unordered containers).
#pragma once
#include "world.h"

#include <string>

namespace jv {

// Serialize the current layout to a text string.
std::string saveLayout(const World& w);

// Parse + apply a layout string to `w`. Returns true on success (world
// replaced by the saved layout); false on malformed input (world unchanged).
bool loadLayout(World& w, const std::string& text);

}  // namespace jv
