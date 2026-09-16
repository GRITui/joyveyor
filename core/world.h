// JoyVeyor core — World: placement, fixed-tick simulation, backpressure,
// networks, chunks, snapshots (README §2.1/§2.2/§3.1/§3.2/§3.3).
//
// Determinism rules (README §2.1 invariant 4):
//  * fixed iteration order everywhere (dense ids ascending, precomputed
//    reverse-topological belt move order);
//  * no unordered containers in the sim path;
//  * no per-tick heap allocation (vectors grow only on placement changes);
//  * single-threaded, no randomness.
#pragma once

#include "pools.h"
#include "path.h"
#include "types.h"

#include <vector>

namespace jv {

// Node queue bound (README §2.1: FixedQueue<uint32_t, 64>).
constexpr int32_t kNodeQueueCap = 64;
// Deadlock threshold (README §3.1: 120 ticks ≈ 4 s).
constexpr uint32_t kDeadlockJamTicks = 120;
// Chunk edge in cells (README §2.2/§3.3: 16×16).
constexpr int32_t kChunkCells = 16;
// Belt lane capacity bound (Belt::capacity is uint8_t).
constexpr uint8_t kMaxBeltCapacity = 255;
// End-of-belt delivery epsilon (float safety, world units).
constexpr float kEndEps = 1e-6f;

// ---- Belt (README §2.1) ----
struct Belt {
    uint32_t id = INVALID_ID;
    PathKind kind = PathKind::GridStraight;
    GridCell cellOrigin{};   // first occupied cell (entry side)
    Dir dir = E;
    int32_t lenCells = 0;
    float cellSize = 1.0f;
    float length = 0.0f;     // total path length (world units)
    float speed = 1.0f;      // world units / second
    uint8_t capacity = 0;    // max simultaneous items (lane capacity)
    uint32_t inNode = INVALID_ID;  // entry node (INVALID if open)
    uint32_t outNode = INVALID_ID; // exit node (INVALID if open)
    uint32_t nextBelt = INVALID_ID; // direct downstream belt (INVALID = none)
    uint32_t networkId = INVALID_ID;
    uint16_t occupancy = 0;
    // Items on this belt form an arcPos-ascending linked list via
    // ItemPool::nextOnBelt; headItem is the entry-side (smallest arcPos) item.
    // Items move uniformly and never overtake → no per-tick resort (README §2.1).
    uint32_t headItem = INVALID_ID;

    GridStraightPath path() const;
    // Cell the item exits into (one past the last occupied cell).
    GridCell exitCell() const;
    // Cell the item enters from (one before the origin cell).
    GridCell entryCell() const;
    // Occupied cell i (0 = entry side).
    GridCell cellAt(int32_t i) const;
};

// ---- Node (README §2.1) ----
struct Node {
    uint32_t id = INVALID_ID;
    NodeKind kind = NodeKind::Junction;
    GridCell cell{};

    // Source / Sink
    uint16_t storageCapacity = 0;  // Sink: max stored items
    uint16_t storageCount = 0;     // Sink: items stored (delivered)
    bool autoConsume = false;      // Sink: consume 1 stored item / tick (step 1)
    uint16_t spawnTimer = 0;       // Source: fixed-tick countdown to next spawn

    // Connections (dense belt ids). Convention:
    //   Source:   outputs[0] = fed belt
    //   Sink:     inputs[0]  = feeding belt
    //   Splitter: inputs[0]  = input belt; outputs[0..1] = belts in outDirA/B
    //   Merger:   inputs[0..1] = belts in inDirA/B; outputs[0] = fed belt
    uint8_t outputCount = 0;
    uint32_t outputs[4] = {INVALID_ID, INVALID_ID, INVALID_ID, INVALID_ID};
    Dir outDirs[4] = {E, S, E, S};
    uint8_t routingMode = 0;       // 0 = RoundRobin (MVP)
    uint8_t ratios[4] = {0, 0, 0, 0};
    uint32_t rrCursor = 0;
    uint8_t inputCount = 0;
    uint32_t inputs[4] = {INVALID_ID, INVALID_ID, INVALID_ID, INVALID_ID};
    Dir inDirs[4] = {W, N, W, N};

    // Bounded FIFO for items waiting at the node (backpressure queue, §3.2).
    FixedQueue<uint32_t, kNodeQueueCap> queue;

    uint32_t networkId = INVALID_ID;
};

// ---- Network: connected component (README §2.1) ----
struct Network {
    uint32_t id = INVALID_ID;
    uint32_t beltCount = 0;
    uint32_t nodeCount = 0;
    uint32_t itemCount = 0;   // live items (on belts + in queues)
    bool hasCycle = false;    // iterative DFS cycle detection on the belt graph
    uint32_t cycleBeltIds[16] = {};
    uint32_t cycleCount = 0;
    uint32_t jamTicks = 0;    // consecutive zero-progress ticks (§3.1)
};

// ---- Chunk: 16×16 cell partition (README §2.2/§3.3) ----
struct Chunk {
    int32_t cx = 0;
    int32_t cy = 0;
    bool active = false;  // moving items / queued items / spawnable source /
                          // non-empty sink storage; inactive chunks are skipped
};

// ---- Read-only render snapshot (README §2.2; refined by a later issue) ----
struct RenderSnapshot {
    static constexpr int32_t kMaxItems = 2048;
    static constexpr int32_t kMaxChunks = 256;

    uint64_t tick = 0;
    float alpha = 0.0f;  // interpolation between tick N-1 (0) and N (1)
    int32_t itemCount = 0;

    // Items, grouped chunk-major: chunk i owns items [chunkItemStart[i],
    // chunkItemStart[i] + chunkItemCount[i]).
    Vec2 itemPos[kMaxItems] = {};
    uint32_t itemId[kMaxItems] = {};

    int32_t chunkCount = 0;
    int32_t chunkCX[kMaxChunks] = {};
    int32_t chunkCY[kMaxChunks] = {};
    bool chunkActive[kMaxChunks] = {};
    int32_t chunkItemStart[kMaxChunks] = {};
    int32_t chunkItemCount[kMaxChunks] = {};
};

// ---------------------------------------------------------------------------
// World
// ---------------------------------------------------------------------------
class World {
public:
    explicit World(const SimConfig& cfg = SimConfig());

    // ---- Placement (snap-to-grid; returns INVALID_ID when rejected) ----
    // Belt occupies cells (x,y) .. (x,y)+(len-1)*vec(dir).
    // Connection rule: belt B connects to belt A iff B.entryCell() ==
    // A.exitCell() and B.dir == A.dir (compatible, straight continuation).
    // Rejects: len < 1, cell overlap with belts/nodes, incompatible
    // connections, double-occupied node connection slots.
    uint32_t placeBelt(int32_t x, int32_t y, Dir dir, int len);
    uint32_t placeSource(GridCell cell);
    uint32_t placeSink(GridCell cell, uint16_t capacity);
    uint32_t placeSplitter(GridCell cell, Dir outA, Dir outB);
    uint32_t placeMerger(GridCell cell, Dir inA, Dir inB, Dir out);
    bool removeBelt(uint32_t beltId);   // only when empty
    bool removeNode(uint32_t nodeId);   // only when storage/queue empty

    // ---- Time (README §2.2) ----
    // Time accumulator → integer ticks at fixed dt.
    void advance(float seconds);
    // One deterministic tick:
    //  1) sinks consume  2) nodes dispatch  3) belts move (reverse
    //     topological, downstream first; sub-stepped so no item moves more
    //     than half a cell per step)  4) sources spawn  5) bookkeeping
    //     (occupancy, network item counts, jam ticks, chunk activity).
    void tick();

    // ---- Counters ----
    uint64_t spawnedCount() const { return spawnedCount_; }
    uint64_t deliveredCount() const { return deliveredCount_; }
    uint64_t consumedCount() const { return consumedCount_; }
    int32_t itemCount() const { return items_.liveCount(); }
    uint64_t tickCount() const { return tickCount_; }
    uint32_t lastDeliveredId() const { return lastDeliveredId_; }

    // ---- Invariants (README §2.1) ----
    // Conservation (totalItems == spawned - delivered), no overlap
    // (arcPos[i+1] - arcPos[i] >= minGap - 1e-6), occupancy <= capacity,
    // queue sizes <= queue capacity.
    bool checkInvariants() const;

    // ---- Routing helpers (splitter/merger dispatch builds on these) ----
    // Entry gate (§3.2): occupancy < capacity AND first item arcPos >= minGap.
    bool canEnterBelt(uint32_t beltId) const;
    // Move a queued item onto the belt entry if the gate is open.
    bool dispatchItemToBelt(uint32_t itemId, uint32_t beltId);
    // Push an item onto a node's bounded FIFO (backpressure queue).
    bool tryEnqueue(uint32_t nodeId, uint32_t itemId);

    // ---- Networks / deadlock (README §3.1) ----
    int32_t networkCount() const { return networks_.size(); }
    const Network& network(uint32_t id) const { return *networks_.data(id); }
    uint32_t networkOfBelt(uint32_t beltId) const;
    bool hasCycle(uint32_t networkId) const;
    bool hasCycle() const;  // any network
    int32_t cycleBeltIds(uint32_t networkId, uint32_t* out, int32_t maxCount) const;
    bool isDeadlocked(uint32_t networkId) const;
    bool isDeadlocked() const;  // any network

    // ---- Snapshot (README §2.2) ----
    RenderSnapshot renderSnapshot(float alpha = 1.0f) const;

    // ---- Access ----
    const SimConfig& config() const { return cfg_; }
    ItemPool& items() { return items_; }
    const ItemPool& items() const { return items_; }
    Belt& belt(uint32_t id) { return *belts_.data(id); }
    const Belt& belt(uint32_t id) const { return *belts_.data(id); }
    Node& node(uint32_t id) { return *nodes_.data(id); }
    const Node& node(uint32_t id) const { return *nodes_.data(id); }
    int32_t beltCount() const { return belts_.size(); }
    int32_t nodeCount() const { return nodes_.size(); }

    // ---- Test helpers ----
    // Inject an item at a belt's entry (arcPos 0); counts toward spawned.
    uint32_t injectItemAtEntry(uint32_t beltId, uint16_t itemType = 0);
    // Set sink storage directly (simulates a pre-filled chest).
    void setSinkStorage(uint32_t sinkId, uint16_t count);
    // Instantly empty a sink's storage.
    void drainSink(uint32_t sinkId);

private:
    // Placement helpers
    bool cellFree(const GridCell& c) const;
    // Node-connection rules (entry/exit side); return false to reject.
    bool tryLinkEntry(Node& n, Belt& b);
    bool tryLinkExit(Node& n, Belt& b);
    const Node* nodeAt(const GridCell& c) const;
    Node* nodeAt(const GridCell& c);
    const Belt* beltAt(const GridCell& c) const;  // belt occupying the cell
    Belt* beltAt(const GridCell& c);
    void recomputeNetworks();   // components + cycles + move order (on change)
    void recomputeChunkActivity();
    int32_t chunkIndexOf(int32_t cx, int32_t cy) const;
    void ensureChunk(int32_t cx, int32_t cy);
    static int32_t chunkCoord(int32_t v);  // floor(v / kChunkCells)

    // Tick steps
    void tickSinksConsume();
    void tickNodesDispatch();
    void tickBeltsMove();
    void tickSourcesSpawn();
    void tickBookkeeping();

    // Movement
    int32_t substepCount() const;
    void moveBelt(uint32_t beltId, float step);
    bool downstreamFree(const Belt& b) const;
    // Hand off the belt's tail item (at the exit). Returns true if the item
    // left the belt's list (delivered / transferred / enqueued).
    bool handoffTailItem(uint32_t it, Belt& b);
    void removeFromBeltList(Belt& b, uint32_t it);

    SimConfig cfg_;
    ItemPool items_;
    DensePool<Belt, 256> belts_;
    DensePool<Node, 256> nodes_;
    DensePool<Network, 64> networks_;
    std::vector<Chunk> chunks_;
    std::vector<uint32_t> beltMoveOrder_;   // reverse topological (downstream first)
    std::vector<uint32_t> movedPerNetwork_; // per-tick scratch (resized on placement)

    float timeAccumulator_ = 0.0f;
    uint64_t tickCount_ = 0;
    uint64_t spawnedCount_ = 0;
    uint64_t deliveredCount_ = 0;
    uint64_t consumedCount_ = 0;
    uint32_t lastDeliveredId_ = INVALID_ID;
    uint64_t movedThisTick_ = 0;
};

}  // namespace jv
