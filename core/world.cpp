// JoyVeyor core — World implementation.
#include "world.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace jv {

// ---------------------------------------------------------------------------
// Belt
// ---------------------------------------------------------------------------
GridStraightPath Belt::path() const {
    return makeGridStraightPath(cellOrigin.x, cellOrigin.y, dir, lenCells, cellSize);
}

GridCell Belt::exitCell() const {
    return cellAt(lenCells);  // one past the last occupied cell
}

GridCell Belt::entryCell() const {
    return cellAt(-1);  // one before the origin cell
}

GridCell Belt::cellAt(int32_t i) const {
    const Vec2 v = dirVec(dir);
    const int32_t dx = (v.x > 0.0f) ? 1 : (v.x < 0.0f ? -1 : 0);
    const int32_t dy = (v.y > 0.0f) ? 1 : (v.y < 0.0f ? -1 : 0);
    return GridCell{cellOrigin.x + dx * i, cellOrigin.y + dy * i};
}

// ---------------------------------------------------------------------------
// World — construction & placement helpers
// ---------------------------------------------------------------------------
World::World(const SimConfig& cfg) : cfg_(cfg) {}

int32_t World::chunkCoord(int32_t v) {
    // Floor division by kChunkCells (works for negative coordinates).
    return v >= 0 ? v / kChunkCells : -((-v + kChunkCells - 1) / kChunkCells);
}

int32_t World::chunkIndexOf(int32_t cx, int32_t cy) const {
    for (int32_t i = 0; i < static_cast<int32_t>(chunks_.size()); ++i) {
        if (chunks_[i].cx == cx && chunks_[i].cy == cy) return i;
    }
    return -1;
}

void World::ensureChunk(int32_t cx, int32_t cy) {
    if (chunkIndexOf(cx, cy) < 0) {
        Chunk c;
        c.cx = cx;
        c.cy = cy;
        c.active = false;
        chunks_.push_back(c);
    }
}

bool World::cellFree(const GridCell& c) const {
    if (nodeAt(c)) return false;
    if (beltAt(c)) return false;
    return true;
}

const Node* World::nodeAt(const GridCell& c) const {
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        const Node& n = *nodes_.data(i);
        if (nodes_.alive(i) && n.cell == c) return &n;
    }
    return nullptr;
}

Node* World::nodeAt(const GridCell& c) {
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        Node& n = *nodes_.data(i);
        if (nodes_.alive(i) && n.cell == c) return &n;
    }
    return nullptr;
}

const Belt* World::beltAt(const GridCell& c) const {
    for (int32_t i = 0; i < belts_.size(); ++i) {
        const Belt& b = *belts_.data(i);
        if (!belts_.alive(i)) continue;
        for (int32_t k = 0; k < b.lenCells; ++k) {
            if (b.cellAt(k) == c) return &b;
        }
    }
    return nullptr;
}

Belt* World::beltAt(const GridCell& c) {
    for (int32_t i = 0; i < belts_.size(); ++i) {
        Belt& b = *belts_.data(i);
        if (!belts_.alive(i)) continue;
        for (int32_t k = 0; k < b.lenCells; ++k) {
            if (b.cellAt(k) == c) return &b;
        }
    }
    return nullptr;
}

// ---------------------------------------------------------------------------
// Placement
// ---------------------------------------------------------------------------
uint32_t World::placeBelt(int32_t x, int32_t y, Dir dir, int len) {
    if (len < 1) return INVALID_ID;

    const GridCell origin{x, y};
    const Vec2 v = dirVec(dir);
    const int32_t dx = (v.x > 0.0f) ? 1 : (v.x < 0.0f ? -1 : 0);
    const int32_t dy = (v.y > 0.0f) ? 1 : (v.y < 0.0f ? -1 : 0);

    // 1) Overlap check: every occupied cell must be free.
    for (int32_t i = 0; i < len; ++i) {
        if (!cellFree(GridCell{origin.x + dx * i, origin.y + dy * i})) return INVALID_ID;
    }

    // 2) Create the belt.
    const uint32_t id = belts_.add();
    Belt& b = belts_[id];
    b.kind = PathKind::GridStraight;
    b.cellOrigin = origin;
    b.dir = dir;
    b.lenCells = len;
    b.cellSize = cfg_.cellSize;
    b.length = static_cast<float>(len) * cfg_.cellSize;
    b.speed = cfg_.beltSpeed;
    b.inNode = INVALID_ID;
    b.outNode = INVALID_ID;
    b.nextBelt = INVALID_ID;
    b.occupancy = 0;
    b.headItem = INVALID_ID;
    // Lane capacity: max items with arcPos spacing >= minGap, last item at
    // arcPos <= length (README §2.1/§3.2).
    if (cfg_.minGap > 0.0f) {
        const int32_t cap =
            static_cast<int32_t>(std::floor(b.length / cfg_.minGap + 1e-6f)) + 1;
        b.capacity = static_cast<uint8_t>(std::min<int32_t>(cap, kMaxBeltCapacity));
    } else {
        b.capacity = kMaxBeltCapacity;
    }

    const GridCell entry = b.entryCell();
    const GridCell exit = b.exitCell();

    // 3) Upstream connection: entry cell.
    if (Node* n = nodeAt(entry)) {
        switch (n->kind) {
            case NodeKind::Source:
                if (n->outputs[0] != INVALID_ID) { belts_.remove(id); return INVALID_ID; }
                n->outputs[0] = id;
                n->outputCount = 1;
                b.inNode = n->id;
                break;
            case NodeKind::Splitter: {
                int32_t slot = -1;
                for (int32_t i = 0; i < n->outputCount; ++i) {
                    if (n->outDirs[i] == dir && n->outputs[i] == INVALID_ID) { slot = i; break; }
                }
                if (slot < 0) { belts_.remove(id); return INVALID_ID; }
                n->outputs[slot] = id;
                b.inNode = n->id;
                break;
            }
            case NodeKind::Merger:
                if (n->outDirs[0] != dir || n->outputs[0] != INVALID_ID) {
                    belts_.remove(id);
                    return INVALID_ID;
                }
                n->outputs[0] = id;
                n->outputCount = 1;
                b.inNode = n->id;
                break;
            case NodeKind::Junction:
                b.inNode = n->id;
                break;
            case NodeKind::Sink:
                belts_.remove(id);
                return INVALID_ID;  // a belt cannot exit a sink
        }
    } else if (Belt* up = beltAt(entry)) {
        // Belt-to-belt: the upstream belt must exit exactly into this belt's
        // origin, in the same direction (compatible straight continuation).
        if (up->dir != dir || up->exitCell() != origin || up->nextBelt != INVALID_ID) {
            belts_.remove(id);
            return INVALID_ID;
        }
        up->nextBelt = id;
        b.inNode = up->outNode;  // INVALID for a direct belt-to-belt link
    }

    // 4) Downstream connection: exit cell.
    if (Node* n = nodeAt(exit)) {
        switch (n->kind) {
            case NodeKind::Sink:
                if (n->inputs[0] != INVALID_ID) { belts_.remove(id); return INVALID_ID; }
                n->inputs[0] = id;
                n->inputCount = 1;
                b.outNode = n->id;
                break;
            case NodeKind::Splitter:
                if (n->inputs[0] != INVALID_ID) { belts_.remove(id); return INVALID_ID; }
                n->inputs[0] = id;
                n->inputCount = 1;
                b.outNode = n->id;
                break;
            case NodeKind::Merger: {
                int32_t slot = -1;
                for (int32_t i = 0; i < n->inputCount; ++i) {
                    if (n->inDirs[i] == dir && n->inputs[i] == INVALID_ID) { slot = i; break; }
                }
                if (slot < 0) { belts_.remove(id); return INVALID_ID; }
                n->inputs[slot] = id;
                b.outNode = n->id;
                break;
            }
            case NodeKind::Junction:
                b.outNode = n->id;
                break;
            case NodeKind::Source:
                belts_.remove(id);
                return INVALID_ID;  // a belt cannot enter a source
        }
    } else if (const Belt* dn = beltAt(exit)) {
        // The downstream belt must start exactly where this belt ends, in the
        // same direction.
        if (dn->dir != dir || dn->cellOrigin != exit) {
            belts_.remove(id);
            return INVALID_ID;
        }
        b.nextBelt = dn->id;
    }

    ensureChunk(chunkCoord(origin.x), chunkCoord(origin.y));
    recomputeNetworks();
    return id;
}

uint32_t World::placeSource(GridCell cell) {
    if (!cellFree(cell)) return INVALID_ID;
    const uint32_t id = nodes_.add();
    Node& n = nodes_[id];
    n.kind = NodeKind::Source;
    n.cell = cell;
    n.storageCapacity = 0;
    n.storageCount = 0;
    n.spawnTimer = 0;
    ensureChunk(chunkCoord(cell.x), chunkCoord(cell.y));
    recomputeNetworks();
    return id;
}

uint32_t World::placeSink(GridCell cell, uint16_t capacity) {
    if (capacity < 1) return INVALID_ID;
    if (!cellFree(cell)) return INVALID_ID;
    const uint32_t id = nodes_.add();
    Node& n = nodes_[id];
    n.kind = NodeKind::Sink;
    n.cell = cell;
    n.storageCapacity = capacity;
    n.storageCount = 0;
    ensureChunk(chunkCoord(cell.x), chunkCoord(cell.y));
    recomputeNetworks();
    return id;
}

uint32_t World::placeSplitter(GridCell cell, Dir outA, Dir outB) {
    if (outA == outB) return INVALID_ID;
    if (!cellFree(cell)) return INVALID_ID;
    const uint32_t id = nodes_.add();
    Node& n = nodes_[id];
    n.kind = NodeKind::Splitter;
    n.cell = cell;
    n.outputCount = 2;
    n.outDirs[0] = outA;
    n.outDirs[1] = outB;
    n.outputs[0] = INVALID_ID;
    n.outputs[1] = INVALID_ID;
    n.routingMode = 0;  // RoundRobin
    ensureChunk(chunkCoord(cell.x), chunkCoord(cell.y));
    recomputeNetworks();
    return id;
}

uint32_t World::placeMerger(GridCell cell, Dir inA, Dir inB, Dir out) {
    if (inA == inB || out == inA || out == inB) return INVALID_ID;
    if (!cellFree(cell)) return INVALID_ID;
    const uint32_t id = nodes_.add();
    Node& n = nodes_[id];
    n.kind = NodeKind::Merger;
    n.cell = cell;
    n.inputCount = 2;
    n.inDirs[0] = inA;
    n.inDirs[1] = inB;
    n.outDirs[0] = out;
    n.outputs[0] = INVALID_ID;
    ensureChunk(chunkCoord(cell.x), chunkCoord(cell.y));
    recomputeNetworks();
    return id;
}

bool World::removeBelt(uint32_t beltId) {
    if (!belts_.alive(beltId)) return false;
    Belt& b = belts_[beltId];
    if (b.occupancy > 0) return false;  // items must be gone first

    // Disconnect upstream belt.
    for (int32_t i = 0; i < belts_.size(); ++i) {
        if (belts_.alive(i) && belts_[i].nextBelt == beltId) belts_[i].nextBelt = INVALID_ID;
    }
    // Disconnect nodes.
    if (b.inNode != INVALID_ID && nodes_.alive(b.inNode)) {
        Node& n = nodes_[b.inNode];
        for (int32_t i = 0; i < 4; ++i) {
            if (n.outputs[i] == beltId) n.outputs[i] = INVALID_ID;
        }
    }
    if (b.outNode != INVALID_ID && nodes_.alive(b.outNode)) {
        Node& n = nodes_[b.outNode];
        for (int32_t i = 0; i < 4; ++i) {
            if (n.inputs[i] == beltId) n.inputs[i] = INVALID_ID;
        }
    }

    belts_.remove(beltId);
    recomputeNetworks();
    return true;
}

bool World::removeNode(uint32_t nodeId) {
    if (!nodes_.alive(nodeId)) return false;
    Node& n = nodes_[nodeId];
    if (n.storageCount > 0 || !n.queue.empty()) return false;

    for (int32_t i = 0; i < belts_.size(); ++i) {
        if (!belts_.alive(i)) continue;
        if (belts_[i].inNode == nodeId) belts_[i].inNode = INVALID_ID;
        if (belts_[i].outNode == nodeId) belts_[i].outNode = INVALID_ID;
    }

    nodes_.remove(nodeId);
    recomputeNetworks();
    return true;
}

}  // namespace jv
