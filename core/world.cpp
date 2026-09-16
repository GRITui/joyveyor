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

// ---------------------------------------------------------------------------
// Networks (README §2.1/§3.1)
// ---------------------------------------------------------------------------
void World::recomputeNetworks() {
    const int32_t nb = belts_.size();
    const int32_t nn = nodes_.size();

    // Reset the network pool; components are recomputed from scratch.
    for (int32_t i = 0; i < networks_.size(); ++i) networks_.remove(i);
    networks_.compact();

    std::vector<uint32_t> beltNet(static_cast<size_t>(nb), INVALID_ID);
    std::vector<uint32_t> nodeNet(static_cast<size_t>(nn), INVALID_ID);
    std::vector<uint32_t> prevBelt(static_cast<size_t>(nb), INVALID_ID);
    for (int32_t i = 0; i < nb; ++i) {
        if (!belts_.alive(i)) continue;
        const uint32_t nxt = belts_[i].nextBelt;
        if (nxt != INVALID_ID && belts_.alive(nxt)) prevBelt[nxt] = static_cast<uint32_t>(i);
    }

    auto resetNetwork = [&](uint32_t netId) {
        Network& net = networks_[netId];
        net.beltCount = 0;
        net.nodeCount = 0;
        net.itemCount = 0;
        net.hasCycle = false;
        net.cycleCount = 0;
        net.jamTicks = 0;
    };

    std::vector<uint32_t> stackBelt;
    std::vector<uint32_t> stackNode;

    for (int32_t i = 0; i < nb; ++i) {
        if (!belts_.alive(i) || beltNet[i] != INVALID_ID) continue;
        const uint32_t netId = networks_.add();
        resetNetwork(netId);
        Network& net = networks_[netId];
        stackBelt.clear();
        stackNode.clear();
        stackBelt.push_back(static_cast<uint32_t>(i));
        beltNet[i] = netId;
        while (!stackBelt.empty() || !stackNode.empty()) {
            if (!stackBelt.empty()) {
                const uint32_t bi = stackBelt.back();
                stackBelt.pop_back();
                Belt& b = belts_[bi];
                b.networkId = netId;
                net.beltCount++;
                if (b.nextBelt != INVALID_ID && belts_.alive(b.nextBelt) &&
                    beltNet[b.nextBelt] == INVALID_ID) {
                    beltNet[b.nextBelt] = netId;
                    stackBelt.push_back(b.nextBelt);
                }
                if (prevBelt[bi] != INVALID_ID && belts_.alive(prevBelt[bi]) &&
                    beltNet[prevBelt[bi]] == INVALID_ID) {
                    beltNet[prevBelt[bi]] = netId;
                    stackBelt.push_back(prevBelt[bi]);
                }
                if (b.inNode != INVALID_ID && nodes_.alive(b.inNode) &&
                    nodeNet[b.inNode] == INVALID_ID) {
                    nodeNet[b.inNode] = netId;
                    stackNode.push_back(b.inNode);
                }
                if (b.outNode != INVALID_ID && nodes_.alive(b.outNode) &&
                    nodeNet[b.outNode] == INVALID_ID) {
                    nodeNet[b.outNode] = netId;
                    stackNode.push_back(b.outNode);
                }
                continue;
            }
            const uint32_t ni = stackNode.back();
            stackNode.pop_back();
            Node& n = nodes_[ni];
            n.networkId = netId;
            net.nodeCount++;
            for (int32_t k = 0; k < n.outputCount; ++k) {
                const uint32_t belt = n.outputs[k];
                if (belt != INVALID_ID && belts_.alive(belt) && beltNet[belt] == INVALID_ID) {
                    beltNet[belt] = netId;
                    stackBelt.push_back(belt);
                }
            }
            for (int32_t k = 0; k < n.inputCount; ++k) {
                const uint32_t belt = n.inputs[k];
                if (belt != INVALID_ID && belts_.alive(belt) && beltNet[belt] == INVALID_ID) {
                    beltNet[belt] = netId;
                    stackBelt.push_back(belt);
                }
            }
        }
    }

    // Isolated nodes (no belt connected yet) get their own single-node network.
    for (int32_t i = 0; i < nn; ++i) {
        if (!nodes_.alive(i) || nodeNet[i] != INVALID_ID) continue;
        const uint32_t netId = networks_.add();
        resetNetwork(netId);
        networks_[netId].nodeCount = 1;
        nodes_[i].networkId = netId;
    }

    // Cycle detection + reverse-topological move order via iterative DFS
    // post-order (downstream finishes before its upstream, so raw post-order
    // is already "downstream first").
    auto successors = [&](uint32_t bi, uint32_t out[4]) -> int32_t {
        const Belt& b = belts_[bi];
        if (b.nextBelt != INVALID_ID && belts_.alive(b.nextBelt)) {
            out[0] = b.nextBelt;
            return 1;
        }
        if (b.outNode != INVALID_ID && nodes_.alive(b.outNode)) {
            const Node& n = nodes_[b.outNode];
            int32_t c = 0;
            for (int32_t k = 0; k < n.outputCount && c < 4; ++k) {
                if (n.outputs[k] != INVALID_ID && belts_.alive(n.outputs[k])) out[c++] = n.outputs[k];
            }
            return c;
        }
        return 0;
    };

    beltMoveOrder_.clear();
    std::vector<uint8_t> color(static_cast<size_t>(nb), 0);  // 0 white, 1 gray, 2 black
    std::vector<int32_t> dfsBelt;
    std::vector<int32_t> dfsChild;
    for (int32_t s = 0; s < nb; ++s) {
        if (!belts_.alive(s) || color[s] != 0) continue;
        dfsBelt.clear();
        dfsChild.clear();
        dfsBelt.push_back(s);
        dfsChild.push_back(0);
        color[s] = 1;
        while (!dfsBelt.empty()) {
            const int32_t top = dfsBelt.back();
            uint32_t succ[4];
            const int32_t sc = successors(static_cast<uint32_t>(top), succ);
            int32_t& ci = dfsChild.back();
            if (ci < sc) {
                const uint32_t child = succ[ci];
                ++ci;
                if (color[child] == 0) {
                    color[child] = 1;
                    dfsBelt.push_back(static_cast<int32_t>(child));
                    dfsChild.push_back(0);
                } else if (color[child] == 1) {
                    const uint32_t netId = belts_[static_cast<uint32_t>(top)].networkId;
                    if (networks_.alive(netId) && !networks_[netId].hasCycle) {
                        Network& net = networks_[netId];
                        net.hasCycle = true;
                        net.cycleCount = 0;
                        int32_t startPos = -1;
                        for (int32_t p = 0; p < static_cast<int32_t>(dfsBelt.size()); ++p) {
                            if (dfsBelt[p] == static_cast<int32_t>(child)) { startPos = p; break; }
                        }
                        if (startPos >= 0) {
                            for (int32_t p = startPos;
                                 p < static_cast<int32_t>(dfsBelt.size()) && net.cycleCount < 16; ++p) {
                                net.cycleBeltIds[net.cycleCount++] = static_cast<uint32_t>(dfsBelt[p]);
                            }
                        }
                    }
                }
            } else {
                color[top] = 2;
                beltMoveOrder_.push_back(static_cast<uint32_t>(top));
                dfsBelt.pop_back();
                dfsChild.pop_back();
            }
        }
    }

    if (hasCycle()) {
        // Deterministic, simple fallback: ascending belt id order.
        beltMoveOrder_.clear();
        for (int32_t i = 0; i < nb; ++i) {
            if (belts_.alive(i)) beltMoveOrder_.push_back(static_cast<uint32_t>(i));
        }
    }
}

void World::recomputeChunkActivity() {
    for (auto& c : chunks_) c.active = false;
    for (int32_t i = 0; i < belts_.size(); ++i) {
        if (!belts_.alive(i)) continue;
        const Belt& b = belts_[i];
        if (b.headItem == INVALID_ID) continue;
        const int32_t idx = chunkIndexOf(chunkCoord(b.cellOrigin.x), chunkCoord(b.cellOrigin.y));
        if (idx >= 0) chunks_[idx].active = true;
    }
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        if (!nodes_.alive(i)) continue;
        const Node& n = nodes_[i];
        bool active = !n.queue.empty() || n.storageCount > 0;
        if (n.kind == NodeKind::Source && n.outputs[0] != INVALID_ID) active = true;
        if (!active) continue;
        const int32_t idx = chunkIndexOf(chunkCoord(n.cell.x), chunkCoord(n.cell.y));
        if (idx >= 0) chunks_[idx].active = true;
    }
}

uint32_t World::networkOfBelt(uint32_t beltId) const {
    if (!belts_.alive(beltId)) return INVALID_ID;
    return belts_[beltId].networkId;
}

bool World::hasCycle(uint32_t networkId) const {
    if (!networks_.alive(networkId)) return false;
    return networks_[networkId].hasCycle;
}

bool World::hasCycle() const {
    for (int32_t i = 0; i < networks_.size(); ++i) {
        if (networks_.alive(i) && networks_[i].hasCycle) return true;
    }
    return false;
}

int32_t World::cycleBeltIds(uint32_t networkId, uint32_t* out, int32_t maxCount) const {
    if (!networks_.alive(networkId) || out == nullptr) return 0;
    const Network& net = networks_[networkId];
    const int32_t n = std::min<int32_t>(static_cast<int32_t>(net.cycleCount), maxCount);
    for (int32_t i = 0; i < n; ++i) out[i] = net.cycleBeltIds[i];
    return n;
}

bool World::isDeadlocked(uint32_t networkId) const {
    if (!networks_.alive(networkId)) return false;
    return networks_[networkId].jamTicks >= kDeadlockJamTicks;
}

bool World::isDeadlocked() const {
    for (int32_t i = 0; i < networks_.size(); ++i) {
        if (networks_.alive(i) && isDeadlocked(static_cast<uint32_t>(i))) return true;
    }
    return false;
}

// ---------------------------------------------------------------------------
// Time / tick (README §2.2)
// ---------------------------------------------------------------------------
void World::advance(float seconds) {
    timeAccumulator_ += seconds;
    while (timeAccumulator_ >= cfg_.dt) {
        timeAccumulator_ -= cfg_.dt;
        tick();
    }
}

void World::tick() {
    tickSinksConsume();
    tickNodesDispatch();
    tickBeltsMove();
    tickSourcesSpawn();
    tickBookkeeping();
    ++tickCount_;
}

void World::tickSinksConsume() {
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        if (!nodes_.alive(i)) continue;
        Node& n = nodes_[i];
        if (n.kind == NodeKind::Sink && n.autoConsume && n.storageCount > 0) {
            --n.storageCount;
            ++consumedCount_;
        }
    }
}

void World::tickNodesDispatch() {
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        if (!nodes_.alive(i)) continue;
        Node& n = nodes_[i];
        while (!n.queue.empty()) {
            const uint32_t it = n.queue.front();
            bool dispatched = false;
            if (n.kind == NodeKind::Splitter && n.outputCount > 0) {
                const uint32_t outIdx = n.rrCursor % n.outputCount;
                const uint32_t targetBelt = n.outputs[outIdx];
                if (targetBelt != INVALID_ID) {
                    items_.setRouteHint(it, targetBelt);
                    if (dispatchItemToBelt(it, targetBelt)) {
                        n.rrCursor = (n.rrCursor + 1) % n.outputCount;
                        dispatched = true;
                    }
                }
            } else if (n.kind == NodeKind::Merger || n.kind == NodeKind::Junction) {
                const uint32_t targetBelt = n.outputs[0];
                if (targetBelt != INVALID_ID && dispatchItemToBelt(it, targetBelt)) dispatched = true;
            }
            if (!dispatched) break;
            n.queue.pop();
        }
    }
}

int32_t World::substepCount() const {
    const float maxStep = 0.5f * cfg_.cellSize;
    if (maxStep <= 0.0f) return 1;
    const int32_t k = static_cast<int32_t>(std::ceil((cfg_.beltSpeed * cfg_.dt) / maxStep));
    return k < 1 ? 1 : k;
}

bool World::canEnterBelt(uint32_t beltId) const {
    if (beltId == INVALID_ID || !belts_.alive(beltId)) return false;
    const Belt& b = belts_[beltId];
    if (b.occupancy >= b.capacity) return false;
    if (b.headItem != INVALID_ID && items_.arcPos(b.headItem) < cfg_.minGap) return false;
    return true;
}

bool World::downstreamFree(const Belt& b) const {
    if (b.nextBelt != INVALID_ID) return canEnterBelt(b.nextBelt);
    if (b.outNode != INVALID_ID && nodes_.alive(b.outNode)) {
        const Node& n = nodes_[b.outNode];
        if (n.kind == NodeKind::Sink) return n.storageCount < n.storageCapacity;
        return !n.queue.full();
    }
    return false;  // open end: nothing to hand off to
}

bool World::dispatchItemToBelt(uint32_t itemId, uint32_t beltId) {
    if (!canEnterBelt(beltId)) return false;
    Belt& b = belts_[beltId];
    items_.setArcPos(itemId, 0.0f);
    items_.setBeltId(itemId, beltId);
    items_.setState(itemId, static_cast<uint8_t>(ItemState::Moving));
    items_.setNodeId(itemId, INVALID_ID);
    items_.setNextOnBelt(itemId, b.headItem);
    b.headItem = itemId;
    ++b.occupancy;
    return true;
}

bool World::tryEnqueue(uint32_t nodeId, uint32_t itemId) {
    if (!nodes_.alive(nodeId)) return false;
    Node& n = nodes_[nodeId];
    if (n.queue.full()) return false;
    n.queue.push(itemId);
    items_.setState(itemId, static_cast<uint8_t>(ItemState::QueuedAtNode));
    items_.setNodeId(itemId, nodeId);
    items_.setBeltId(itemId, INVALID_ID);
    return true;
}

void World::removeFromBeltList(Belt& b, uint32_t it) {
    if (b.headItem == it) {
        b.headItem = items_.nextOnBelt(it);
        return;
    }
    uint32_t prev = b.headItem;
    while (prev != INVALID_ID) {
        const uint32_t nxt = items_.nextOnBelt(prev);
        if (nxt == it) {
            items_.setNextOnBelt(prev, items_.nextOnBelt(it));
            return;
        }
        prev = nxt;
    }
}

bool World::handoffTailItem(uint32_t it, Belt& b) {
    if (!downstreamFree(b)) return false;
    if (b.nextBelt != INVALID_ID) {
        Belt& nb = belts_[b.nextBelt];
        removeFromBeltList(b, it);
        --b.occupancy;
        items_.setArcPos(it, 0.0f);
        items_.setBeltId(it, nb.id);
        items_.setState(it, static_cast<uint8_t>(ItemState::Moving));
        items_.setNodeId(it, INVALID_ID);
        items_.setNextOnBelt(it, nb.headItem);
        nb.headItem = it;
        ++nb.occupancy;
        return true;
    }
    if (b.outNode != INVALID_ID && nodes_.alive(b.outNode)) {
        Node& n = nodes_[b.outNode];
        if (n.kind == NodeKind::Sink) {
            removeFromBeltList(b, it);
            --b.occupancy;
            ++n.storageCount;
            ++deliveredCount_;
            lastDeliveredId_ = it;
            items_.remove(it);
            return true;
        }
        if (!tryEnqueue(n.id, it)) return false;
        removeFromBeltList(b, it);
        --b.occupancy;
        return true;
    }
    return false;
}

void World::moveBelt(uint32_t beltId, float step) {
    Belt& b = belts_[beltId];
    uint32_t ids[kMaxBeltCapacity + 1];
    int32_t n = 0;
    for (uint32_t it = b.headItem; it != INVALID_ID; it = items_.nextOnBelt(it)) {
        ids[n++] = it;
    }
    int32_t liveCount = n;
    for (int32_t i = n - 1; i >= 0; --i) {
        const uint32_t it = ids[i];
        const bool isTail = (i == liveCount - 1);
        const float cap = isTail ? b.length : (items_.arcPos(ids[i + 1]) - cfg_.minGap);
        const float pos = items_.arcPos(it);
        float newPos = pos + step;
        if (newPos > cap) newPos = cap;
        if (newPos < pos) newPos = pos;
        if (newPos > pos + 1e-9f) {
            items_.setArcPos(it, newPos);
            items_.setState(it, static_cast<uint8_t>(ItemState::Moving));
            ++movedThisTick_;
        } else {
            items_.setState(it, static_cast<uint8_t>(ItemState::HeldBackpressure));
        }
        if (isTail && items_.arcPos(it) >= b.length - kEndEps) {
            if (handoffTailItem(it, b)) --liveCount;
        }
    }
}

void World::tickBeltsMove() {
    const int32_t k = substepCount();
    for (uint32_t beltId : beltMoveOrder_) {
        if (!belts_.alive(beltId)) continue;
        const Belt& b0 = belts_[beltId];
        const float step = (b0.speed * cfg_.dt) / static_cast<float>(k);
        for (int32_t s = 0; s < k; ++s) moveBelt(beltId, step);
    }
}

void World::tickSourcesSpawn() {
    // Fixed spawn period: 15 ticks == 0.5s at the default 30 Hz tick rate.
    constexpr uint16_t kSpawnPeriodTicks = 15;
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        if (!nodes_.alive(i)) continue;
        Node& n = nodes_[i];
        if (n.kind != NodeKind::Source || n.outputs[0] == INVALID_ID) continue;
        if (n.spawnTimer > 0) {
            --n.spawnTimer;
            continue;
        }
        if (canEnterBelt(n.outputs[0])) {
            Belt& b = belts_[n.outputs[0]];
            const uint32_t it = items_.add(0, b.id, 0.0f, static_cast<uint8_t>(ItemState::Moving),
                                            INVALID_ID);
            items_.setNextOnBelt(it, b.headItem);
            b.headItem = it;
            ++b.occupancy;
            ++spawnedCount_;
            n.spawnTimer = kSpawnPeriodTicks;
        }
    }
}

void World::tickBookkeeping() {
    for (int32_t i = 0; i < belts_.size(); ++i) {
        if (!belts_.alive(i)) continue;
        Belt& b = belts_[i];
        uint16_t count = 0;
        for (uint32_t it = b.headItem; it != INVALID_ID; it = items_.nextOnBelt(it)) ++count;
        b.occupancy = count;
    }
    for (int32_t i = 0; i < networks_.size(); ++i) {
        if (networks_.alive(i)) networks_[i].itemCount = 0;
    }
    for (int32_t i = 0; i < belts_.size(); ++i) {
        if (!belts_.alive(i)) continue;
        const Belt& b = belts_[i];
        if (b.networkId != INVALID_ID && networks_.alive(b.networkId)) {
            networks_[b.networkId].itemCount += b.occupancy;
        }
    }
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        if (!nodes_.alive(i)) continue;
        const Node& n = nodes_[i];
        if (n.networkId != INVALID_ID && networks_.alive(n.networkId)) {
            networks_[n.networkId].itemCount += static_cast<uint32_t>(n.queue.sizeOf());
        }
    }
    for (int32_t i = 0; i < networks_.size(); ++i) {
        if (!networks_.alive(i)) continue;
        Network& net = networks_[i];
        if (movedThisTick_ == 0 && net.itemCount > 0) {
            ++net.jamTicks;
        } else {
            net.jamTicks = 0;
        }
    }
    recomputeChunkActivity();
    movedThisTick_ = 0;
}

// ---------------------------------------------------------------------------
// Invariants (README §2.1)
// ---------------------------------------------------------------------------
bool World::checkInvariants() const {
    if (static_cast<uint64_t>(itemCount()) != spawnedCount_ - deliveredCount_) return false;
    for (int32_t i = 0; i < belts_.size(); ++i) {
        if (!belts_.alive(i)) continue;
        const Belt& b = belts_[i];
        if (b.occupancy > b.capacity) return false;
        bool first = true;
        float prevArc = 0.0f;
        for (uint32_t it = b.headItem; it != INVALID_ID; it = items_.nextOnBelt(it)) {
            const float arc = items_.arcPos(it);
            if (!first && arc - prevArc < cfg_.minGap - 1e-6f) return false;
            prevArc = arc;
            first = false;
        }
    }
    for (int32_t i = 0; i < nodes_.size(); ++i) {
        if (!nodes_.alive(i)) continue;
        if (nodes_[i].queue.sizeOf() > kNodeQueueCap) return false;
    }
    return true;
}

// ---------------------------------------------------------------------------
// Snapshot (README §2.2)
// ---------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------
uint32_t World::injectItemAtEntry(uint32_t beltId, uint16_t itemType) {
    if (!belts_.alive(beltId)) return INVALID_ID;
    Belt& b = belts_[beltId];
    const uint32_t it = items_.add(itemType, beltId, 0.0f, static_cast<uint8_t>(ItemState::Moving),
                                    INVALID_ID);
    items_.setNextOnBelt(it, b.headItem);
    b.headItem = it;
    ++b.occupancy;
    ++spawnedCount_;
    return it;
}

void World::setSinkStorage(uint32_t sinkId, uint16_t count) {
    if (!nodes_.alive(sinkId)) return;
    Node& n = nodes_[sinkId];
    if (n.kind != NodeKind::Sink) return;
    n.storageCount = std::min(count, n.storageCapacity);
}

void World::drainSink(uint32_t sinkId) {
    if (!nodes_.alive(sinkId)) return;
    Node& n = nodes_[sinkId];
    if (n.kind != NodeKind::Sink) return;
    n.storageCount = 0;
}

}  // namespace jv
